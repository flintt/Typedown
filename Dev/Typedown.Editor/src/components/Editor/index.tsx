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
    const cursorRef = useRef<any>();
    const [contentVersion, setContentVersion] = useState(0);
    const [options, setOptions] = useState<any>();
    const optionsRef = useRef<any>();
    const [searchOpen, setSearchOpen] = useState(0);
    const [searchArg, setSearchArg] = useState<{ value: string, opt: any }>();
    const muyaScrollTopRef = useRef(0);
    const codeMirrorScrollRef = useRef(0);

    const OnFileLoaded = useCallback(() => setTimeout(() => transport.postMessage('FileLoaded', { text: markdownRef.current }), 100), [])

    // Editor -> host: called by the child editor on every change; no React re-render involved.
    const onMarkdownChange = useCallback((markdown: string) => {
        if (markdown != undefined && markdownRef.current != markdown) {
            markdownRef.current = markdown
            transport.postMessage('MarkdownChange', { text: markdown });
        }
    }, [])

    const onCursorChange = useCallback((cursor: any) => {
        cursorRef.current = cursor
        transport.postMessage('CursorChange', { cursor })
    }, [])

    // Host -> editor: replace the content without echoing it back as a MarkdownChange.
    const setContentFromHost = useCallback((markdown: string, cursor?: any) => {
        markdownRef.current = markdown
        cursorRef.current = cursor
        setContentVersion(v => v + 1)
    }, [])

    useEffect(() => {
        remote.getSettings().then(({ markdown, basePath, ...opt }: any) => {
            window.basePath = basePath
            setOptions(opt)
            setContentFromHost(markdown)
            OnFileLoaded();
        })
    }, [OnFileLoaded, setContentFromHost]);

    useEffect(() => {
        optionsRef.current = options
    }, [options])

    useEffect(() => transport.addListener<IExportArgs>('Export', async ({ type, context, basePath, title, options }) => {
        const generateOption = { printOptimization: false, title, toc: getHtmlToc(getTOC(markdownRef.current ?? '').toc), ...options }
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

    useEffect(() => transport.addListener<{ text: string, basePath: string }>('LoadFile', ({ text, basePath }) => {
        window.basePath = basePath
        setContentFromHost(text, undefined)
        OnFileLoaded();
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
                onMarkdownChange={onMarkdownChange}
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
                onMarkdownChange={onMarkdownChange}
                onCursorChange={onCursorChange}
                onSearchArgChange={setSearchArg}
            />
        )
    }
}

export default Editor;