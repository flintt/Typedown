import { setScrollLoadId } from 'services/scrollbar'
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
import { resetUserIntent, takeChangeOrigin } from "services/changeOrigin";
import { sha256Hex } from "services/sha256";
import { classifierVersion } from "services/normalization";

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

    // The FileLoaded handshake reports the loaded source, preserving its spelling even though Muya normalizes
    // its internal model. The host's "saved" hash must come from the first change report *after* the editor
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

    // An automation edit the page has been asked to apply (ApplyDocumentEdit), until it has replied.
    const pendingEditRef = useRef<{ operationId: string } | null>(null)

    // Reports what the page now holds for the edit in flight: the exact source and what the first visual edit would
    // do to it (services/normalization). Without Muya (source mode) nothing is known about the latter.
    const describeNormalization = useCallback(() => {
        const source = markdownRef.current ?? ''
        const sourceHash = sha256Hex(source)
        const pending = (window as any).__typedownPendingNormalization?.()
        const normalization = pending
            ? { pendingNormalization: pending.pendingNormalization, sourceHash, normalizedHash: sha256Hex(pending.normalized), reasons: pending.reasons, classifierVersion: pending.classifierVersion }
            : { pendingNormalization: 'unknown', sourceHash, normalizedHash: null, reasons: ['notEvaluated'], classifierVersion }
        return { sourceHash, normalization }
    }, [])

    const replyEditApplied = useCallback(() => {
        const edit = pendingEditRef.current
        if (!edit) return false
        pendingEditRef.current = null
        transport.postMessage('DocumentEditApplied', { operationId: edit.operationId, loadId: loadIdRef.current, outcome: 'applied', ...describeNormalization() })
        return true
    }, [describeNormalization])

    // Host -> editor: what the first visual edit would do to the text shown now (document.get's normalization).
    useEffect(() => transport.addListener<{ token: number }>('QueryNormalization', ({ token }) => {
        flushRef.current?.()
        transport.postMessage('NormalizationReport', { token, loadId: loadIdRef.current, ...describeNormalization() })
    }), [describeNormalization]);

    // Called by the child editor right after it applied host content; the next change report completes the handshake.
    const onContentApplied = useCallback(() => {
        if (replyEditApplied()) return
        const pending = fileLoadPending.current
        if (!pending || pending.armed) return
        clearTimeout(pending.timer)
        pending.armed = true
        pending.timer = window.setTimeout(flushFileLoaded, 500)
    }, [flushFileLoaded, replyEditApplied])

    // Editor -> host: called by the child editor on every change; no React re-render involved.
    const onMarkdownChange = useCallback((markdown: string) => {
        if (markdown == undefined) return
        const pending = fileLoadPending.current
        if (pending) {
            // Before the new text is applied any report still describes the previous document: drop it.
            if (!pending.armed) return
            markdownRef.current = markdown
            resetUserIntent()
            flushFileLoaded()
            return
        }
        // While an automation edit is being applied the host holds the text it sent; the editor's reports of
        // applying it are not edits and must not advance the host's history or revision.
        if (pendingEditRef.current) {
            markdownRef.current = markdown
            return
        }
        if (markdownRef.current != markdown) {
            markdownRef.current = markdown
            transport.postMessage('MarkdownChange', { text: markdown, loadId: loadIdRef.current, origin: takeChangeOrigin() });
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
    const onOutlineCurrent = useCallback((slug: string, where?: unknown) => {
        transport.postMessage('OutlineCurrent', { slug, where, loadId: loadIdRef.current })
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
            setScrollLoadId(loadId)
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
        if (type == 'print') {
            // Print this page — the document as the reader sees it, in the editor's theme — rather than an
            // export rendered with another stylesheet. The print rules in index.css take the editing chrome
            // out and lay every block out; the editor loses focus first so no block is the active one.
            try {
                (document.activeElement as HTMLElement | null)?.blur?.()
                await new Promise(r => requestAnimationFrame(() => r(null)))
                window.print()
                return
            } catch (e) {
                console.log('window.print failed, printing the export instead', e)
            }
        }
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

    // Loads arrive one message each and are applied one per frame, the newest only. A key held down on
    // the tab shortcut sent five hundred of them in a few seconds; every one was rendered in turn, the host
    // dropped their reports as stale, and the reader waited a minute for the last to come round. Only the
    // load the host still wants can matter, and the host's stale-report rule already says which that is.
    const pendingLoadRef = useRef<{ text: string, basePath: string, cursor?: any, scrollTop?: number | null, loadId?: number } | null>(null)
    useEffect(() => transport.addListener<{ text: string, basePath: string, cursor?: any, scrollTop?: number | null, loadId?: number }>('LoadFile', (load) => {
        // A load replaces whatever an automation edit was applying (the host restoring it, or a tab switch): that
        // edit's outcome is unknown to the host, which then restores from its own copy.
        const edit = pendingEditRef.current
        if (edit) {
            pendingEditRef.current = null
            transport.postMessage('DocumentEditApplied', { operationId: edit.operationId, loadId: loadIdRef.current, outcome: 'failed', reason: 'superseded' })
        }
        const scheduled = pendingLoadRef.current != null
        pendingLoadRef.current = load
        if (scheduled) return
        requestAnimationFrame(() => {
            const pending = pendingLoadRef.current
            pendingLoadRef.current = null
            if (!pending) return
            const { text, basePath, cursor, scrollTop, loadId } = pending
            window.basePath = basePath
            loadIdRef.current = loadId
            setScrollLoadId(loadId)
            OnFileLoaded();
            setContentFromHost(text, cursor ?? undefined, scrollTop)
        })
    }), [OnFileLoaded, setContentFromHost]);

    // Host -> editor: an automation edit (docs/automation-api-spec.md, section 2.2, steps 6-7). In this one event the
    // page takes in its own throttled typing, compares the live text with the base the host checked, and either
    // refuses (the reader typed meanwhile) or starts applying; the reply follows once the editor holds the text.
    useEffect(() => transport.addListener<{ operationId: string, baseContentHash: string, text: string }>('ApplyDocumentEdit', ({ operationId, baseContentHash, text }) => {
        const refuse = (outcome: string, reason: string) => transport.postMessage('DocumentEditApplied', { operationId, loadId: loadIdRef.current, outcome, reason })
        if (fileLoadPending.current || pendingEditRef.current) return refuse('failed', 'busy')
        flushRef.current?.()
        if (sha256Hex(markdownRef.current ?? '') !== baseContentHash) return refuse('conflict', 'baseContentHashMismatch')
        pendingEditRef.current = { operationId }
        resetUserIntent()
        const y = optionsRef.current?.sourceCode ? codeMirrorScrollRef.current : window.scrollY
        setContentFromHost(text, undefined, y)
    }), [setContentFromHost]);

    useEffect(() => transport.addListener<{ text: string, cursor: string, basePath: string }>('SetMarkdown', ({ text, cursor, basePath }) => {
        window.basePath = basePath
        setContentFromHost(text, cursor)
    }), [setContentFromHost]);

    useEffect(() => transport.addListener<Record<string, unknown>>('SettingsChanged', (newOptions) => {
        // Finish any throttled edit before changing mode or unmounting its editor.
        if ('sourceCode' in newOptions || 'readOnly' in newOptions) flushRef.current?.()
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
