import CodeMirror from "components/CodeMirror";
import MuyaEditor from "components/Muya";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { remote } from "services/remote";
import transport from "services/transport";
import './index.scss'
import ExportHtml from "services/exportHtml";
import { htmlToMarkdown } from "services/importHtml";
import { DEFAULT_TURNDOWN_CONFIG } from "components/Muya/lib/config";
import { getHtmlToc, getTOC } from "services/common";

const Editor: React.FC = () => {
    // The document text and cursor live in refs, not React state: a state update per keystroke would commit
    // a React render while the contenteditable has focus, and React then walks the whole editor DOM to
    // snapshot/restore the selection (O(document size) on every key press). Only host-driven content
    // replacements (LoadFile/SetMarkdown/ImportFile) bump `contentVersion` to push new text into the editor.
    const markdownRef = useRef<string>();
    // Reporting the text is throttled while typing; this runs it now (see Muya's flushContentChange).
    const flushRef = useRef<(() => void) | null>(null);
    const cursorRef = useRef<any>();
    const [contentVersion, setContentVersion] = useState(0);
    const [options, setOptions] = useState<any>();
    const optionsRef = useRef<any>();
    const [searchOpen, setSearchOpen] = useState(0);
    const [searchArg, setSearchArg] = useState<{ value: string, opt: any }>();
    const muyaScrollTopRef = useRef(0);
    const codeMirrorScrollRef = useRef(0);

    // Every host-driven load carries a `loadId`; it is echoed on everything the editor reports so the host can drop
    // messages that still belong to the previous document (a tab switch races with in-flight change events).
    const loadIdRef = useRef<number>()

    // The FileLoaded handshake tells the host what the editor actually holds after a load (Muya normalizes the
    // markdown on import), so the host's "saved" hash must come from the first change report *after* the editor
    // applied the new text — not from a fixed delay, which a large document or a slow first render overruns and
    // then leaves the file looking modified without an edit. The timers are only fallbacks.
    const fileLoadPending = useRef<{ armed: boolean, timer?: number }>()

    const flushFileLoaded = useCallback(() => {
        const pending = fileLoadPending.current
        if (!pending) return
        clearTimeout(pending.timer)
        fileLoadPending.current = undefined
        transport.postMessage('FileLoaded', { text: markdownRef.current, loadId: loadIdRef.current })
    }, [])

    const OnFileLoaded = useCallback(() => {
        clearTimeout(fileLoadPending.current?.timer)
        fileLoadPending.current = { armed: false, timer: window.setTimeout(flushFileLoaded, 3000) }
    }, [flushFileLoaded])

    // Called by the child editor right after it applied host content; the next change report completes the handshake.
    const onContentApplied = useCallback(() => {
        const pending = fileLoadPending.current
        if (!pending || pending.armed) return
        clearTimeout(pending.timer)
        pending.armed = true
        pending.timer = window.setTimeout(flushFileLoaded, 500)
    }, [flushFileLoaded])

    // Editor -> host: called by the child editor on every change; no React re-render involved.
    const onMarkdownChange = useCallback((markdown: string) => {
        if (markdown == undefined) return
        const pending = fileLoadPending.current
        if (pending) {
            // Before the new text is applied any report still describes the previous document: drop it.
            if (!pending.armed) return
            markdownRef.current = markdown
            flushFileLoaded()
            return
        }
        if (markdownRef.current != markdown) {
            markdownRef.current = markdown
            transport.postMessage('MarkdownChange', { text: markdown, loadId: loadIdRef.current });
        }
    }, [flushFileLoaded])

    // The outline, the word count and the caret line, tagged with the load they describe. Without the tag
    // the host cannot tell a report about the document just left from one about the document now shown, and
    // a quick switch left it holding the previous document's outline — headings that are not in the
    // document on screen, which it then tried to scroll to.
    const onStateChange = useCallback((state: any) => {
        if (fileLoadPending.current && !fileLoadPending.current.armed) return
        transport.postMessage('StateChange', { state, loadId: loadIdRef.current, muya: true })
    }, [])

    // Which heading the reader has scrolled to, in reading mode. Deliberately not part of the state report:
    // the host decides where to scroll from `cur` in that report, so a heading sent that way comes back as a
    // scroll, which moves the page, which reports another heading.
    const onOutlineCurrent = useCallback((slug: string) => {
        transport.postMessage('OutlineCurrent', { slug, loadId: loadIdRef.current })
    }, [])

    const onCursorChange = useCallback((cursor: any) => {
        if (fileLoadPending.current && !fileLoadPending.current.armed) return
        cursorRef.current = cursor
        transport.postMessage('CursorChange', { cursor, loadId: loadIdRef.current })
    }, [])

    // Host -> editor: replace the content without echoing it back as a MarkdownChange.
    // A remembered scroll offset (reading mode has no caret to remember) takes precedence over scrolling to the caret.
    const scrollFromHostRef = useRef(false)

    const setContentFromHost = useCallback((markdown: string, cursor?: any, scrollTop?: number | null) => {
        markdownRef.current = markdown
        cursorRef.current = cursor
        scrollFromHostRef.current = typeof scrollTop === 'number'
        muyaScrollTopRef.current = typeof scrollTop === 'number' ? scrollTop : 0
        codeMirrorScrollRef.current = typeof scrollTop === 'number' ? scrollTop : 0
        setContentVersion(v => v + 1)
    }, [])

    useEffect(() => {
        remote.getSettings().then(({ markdown, basePath, cursor, scrollTop, loadId, ...opt }: any) => {
            window.basePath = basePath
            loadIdRef.current = loadId
            setOptions(opt)
            OnFileLoaded();
            setContentFromHost(markdown, cursor ?? undefined, scrollTop)
        })
    }, [OnFileLoaded, setContentFromHost]);

    useEffect(() => {
        optionsRef.current = options
    }, [options])

    // The host asks for this before it saves: whatever it holds must be what is on screen.
    useEffect(() => transport.addListener<{ token?: number }>('FlushContent', ({ token }) => {
        flushRef.current?.()
        transport.postMessage('ContentFlushed', { token: token ?? 0, text: markdownRef.current })
    }), []);

    useEffect(() => transport.addListener<IExportArgs>('Export', async ({ type, context, basePath, title, options }) => {
        flushRef.current?.()
        // The theme and the user's own CSS style the editor, so the exported file should carry them too —
        // otherwise a document looks different the moment it leaves the app.
        const styling = [optionsRef.current?.themeCss, optionsRef.current?.customCss].filter(Boolean).join('\n')
        const generateOption: any = { printOptimization: false, title, toc: getHtmlToc(getTOC(markdownRef.current ?? '').toc), ...options }
        if (styling) generateOption.extraCss = [generateOption.extraCss, styling].filter(Boolean).join('\n')
        const baseUrl = basePath ? `file:///${basePath.replaceAll('\\', '/')}/` : undefined
        const html = await new ExportHtml(markdownRef.current, { ...optionsRef.current, baseUrl }).generate(generateOption)
        if (type == 'print') {
            remote.printHTML({ html, context })
        } else {
            remote.exportCallback({ html, context })
        }
    }), []);

    useEffect(() => transport.addListener<{ type: string, text: string }>('ImportFile', ({ text }) => {
        // Imported content is a genuine edit: show it and report it to the host.
        const markdown = htmlToMarkdown(text, [], DEFAULT_TURNDOWN_CONFIG)
        onMarkdownChange(markdown)
        setContentVersion(v => v + 1)
    }), [options, onMarkdownChange]);

    useEffect(() => transport.addListener<{ text: string, basePath: string, cursor?: any, scrollTop?: number | null, loadId?: number }>('LoadFile', ({ text, basePath, cursor, scrollTop, loadId }) => {
        window.basePath = basePath
        loadIdRef.current = loadId
        OnFileLoaded();
        setContentFromHost(text, cursor ?? undefined, scrollTop)
    }), [OnFileLoaded, setContentFromHost]);

    useEffect(() => transport.addListener<{ text: string, cursor: string, basePath: string }>('SetMarkdown', ({ text, cursor, basePath }) => {
        window.basePath = basePath
        setContentFromHost(text, cursor)
    }), [setContentFromHost]);

    useEffect(() => transport.addListener<Record<string, unknown>>('SettingsChanged', (newOptions) => {
        for (const name in newOptions) {
            const value = newOptions[name];
            if (name.startsWith('search'))
                setSearchArg(old => old ? { ...old, opt: { ...old.opt, [name]: value } } : old)
        }
        setOptions((oldOptions: any) => ({ ...oldOptions, ...newOptions }))
    }), []);

    useEffect(() => transport.addListener<{ open: number }>('SearchOpenChange', ({ open }) => {
        setSearchOpen(open)
    }), []);

    if (!options) {
        return <></>
    }

    if (options.sourceCode) {
        return (
            <CodeMirror
                options={options}
                cursor={cursorRef.current}
                markdown={markdownRef.current ?? ''}
                contentVersion={contentVersion}
                searchOpen={searchOpen}
                searchArg={searchArg}
                scrollTopRef={codeMirrorScrollRef}
                scrollFromHostRef={scrollFromHostRef}
                onMarkdownChange={onMarkdownChange}
                onContentApplied={onContentApplied}
                onStateChange={onStateChange}
                onCursorChange={onCursorChange}
                onSearchArgChange={setSearchArg}
            />
        )
    } else {
        return (
            <MuyaEditor
                options={options}
                cursor={cursorRef.current}
                markdown={markdownRef.current ?? ''}
                contentVersion={contentVersion}
                searchOpen={searchOpen}
                searchArg={searchArg}
                scrollTopRef={muyaScrollTopRef}
                scrollFromHostRef={scrollFromHostRef}
                flushRef={flushRef}
                onMarkdownChange={onMarkdownChange}
                onContentApplied={onContentApplied}
                onStateChange={onStateChange}
                onOutlineCurrent={onOutlineCurrent}
                onCursorChange={onCursorChange}
                onSearchArgChange={setSearchArg}
            />
        )
    }
}

export default Editor;