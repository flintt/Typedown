import React, { useCallback, useEffect, useRef, useState } from "react";
import transport from "services/transport";
import Muya from 'components/Muya/lib'
import TablePicker from 'components/Muya/lib/ui/tablePicker'
import CodePicker from 'components/Muya/lib/ui/codePicker'
import EmojiPicker from 'components/Muya/lib/ui/emojiPicker'
import ImageSelector from 'components/Muya/lib/ui/imageSelector'
import ImageToolbar from 'components/Muya/lib/ui/imageToolbar'
import LinkTools from 'components/Muya/lib/ui/linkTools'
import TableBarTools from 'components/Muya/lib/ui/tableTools'
import FootnoteTool from 'components/Muya/lib/ui/footnoteTool'
import FrontMenu from 'components/Muya/lib/ui/frontMenu'
import FormatPicker from 'components/Muya/lib/ui/formatPicker'
import { createApplicationMenuState } from "services/menuState";
import 'components/Muya/themes/default.css'

interface IMuyaEditor {
    markdown: string
    cursor: any
    /** Incremented by the container whenever the host replaces the content; forces the effect below to re-check. */
    contentVersion?: number
    options: any
    searchOpen: number
    searchArg: { value: string, opt: any } | undefined
    scrollTopRef: React.MutableRefObject<number>
    /** True when scrollTopRef holds a remembered offset from the host for this load: restore it instead of chasing the caret. */
    scrollFromHostRef?: React.MutableRefObject<boolean>
    onMarkdownChange: (markdown: string) => void
    /** The shell puts a function here that reports the current text at once, for saves and exports. */
    flushRef?: React.MutableRefObject<(() => void) | null>
    /** Fired once host content has been pushed into the editor (see the FileLoaded handshake in Editor). */
    onContentApplied?: () => void
    onCursorChange: (cursor: any) => void
    onSearchArgChange: (arg: { value: string, opt: any } | undefined) => void
}

Muya.use(TablePicker)
Muya.use(CodePicker)
Muya.use(EmojiPicker)
Muya.use(ImageSelector)
Muya.use(ImageToolbar)
Muya.use(FrontMenu)
// The selection format bar: the plugin was never registered, so the host's handler for it never fired.
Muya.use(FormatPicker)
Muya.use(LinkTools, { jumpClick: (linkInfo: { href: string }) => { transport.postMessage('OpenURI', { uri: linkInfo.href }) } })
Muya.use(TableBarTools)
Muya.use(FootnoteTool)

const STANDAR_Y = 320

/** The style element with this id, appended to the head on first use. */
const styleElement = (id: string) => {
    let style = document.getElementById(id) as HTMLStyleElement | null
    if (!style) {
        style = document.createElement('style')
        style.id = id
        document.head.appendChild(style)
    }
    return style
}

const MuyaEditor: React.FC<IMuyaEditor> = (props) => {
    const [editor, setEditor] = useState<Muya>();
    const [marginTop, setMarginTop] = useState(0);
    const markdownRef = useRef('');
    const searchArgRef = useRef<any>();
    const cursorRef = useRef<any>();
    const optionsRef = useRef<any>(props.options);

    const relativeScroll = useCallback((delta: number) => {
        window.scrollBy(0, delta)
    }, [])

    const scrollToElement = useCallback((selector) => {
        if (editor == null) {
            return;
        }
        const anchor = document.querySelector(selector)
        if (anchor) {
            const { y } = anchor.getBoundingClientRect()
            relativeScroll(y - STANDAR_Y)
        }
    }, [editor, relativeScroll])

    const scrollToElementIfInvisible = useCallback((selector) => {
        const anchor = document.querySelector(selector)
        if (anchor) {
            const { y } = anchor.getBoundingClientRect()
            if (y < 0 || y > window.innerHeight) {
                scrollToElement(selector)
            }
        }
    }, [scrollToElement])

    const scrollToCursor = useCallback(() => {
        relativeScroll(editor?.getSelection().cursorCoords.y - STANDAR_Y)
    }, [editor, relativeScroll])

    const scrollToCursorIfInvisible = useCallback(() => {
        try {
            const y = editor?.getSelection().cursorCoords.y;
            if (y < 0 || y > window.innerHeight) {
                relativeScroll(y - STANDAR_Y)
            }
        } catch (err) {
            console.log(err)
        }
    }, [editor, relativeScroll])

    const search = useCallback(arg => {
        if (arg?.value != "" && arg?.opt?.selection && editor) {
            const { value, opt } = arg
            const selection = opt.selection.start ? opt.selection : undefined
            editor.search(value, { ...opt, selection })
        }
    }, [editor])

    useEffect(() => {
        searchArgRef.current = props.searchArg
    }, [props.searchArg])

    useEffect(() => {
        optionsRef.current = props.options
    }, [props.options])

    useEffect(() => {
        markdownRef.current = ''
    }, [editor])

    useEffect(() => {
        search(props.searchArg)
    }, [editor, props.searchArg, search])

    // One window has one editor, so switching tabs used to mean building the other document from scratch —
    // parsing it and creating a hundred thousand elements, seconds of it for a long document. A document that
    // has already been built is kept instead: its element is taken out of the page rather than thrown away,
    // and put back when the reader returns to it. A detached subtree costs the browser nothing to keep, so
    // what this spends is memory, and the two limits below are what bounds it: how many documents, and how
    // much text between them. Every document is worth keeping — rebuilding even a twenty-thousand-character
    // one costs half a second of a window that does not answer — so the size limit is on the total, not on
    // each one, and it is what stops a few very long documents adding up.
    const maxKept = 2
    const maxKeptChars = 800000

    const hostRef = useRef<HTMLDivElement>(null)
    type Doc = { muya: any, element: HTMLElement, markdown: string }
    const activeRef = useRef<Doc | null>(null)
    const keptRef = useRef<Doc[]>([])

    // Several of Muya's setters re-render the whole document, which is fine once but ruinous when the active
    // instance changes and every settings effect runs again against a document that is already correct —
    // four full re-renders of a hundred thousand elements. Each instance remembers what has been applied to
    // it, so a setting is only pushed when it has actually changed for that instance.
    const applyOnce = (muya: any, key: string, value: unknown, apply: () => void) => {
        if (!muya) return
        const applied = muya.__applied || (muya.__applied = {})
        const encoded = JSON.stringify(value ?? null)
        if (applied[key] === encoded) return
        applied[key] = encoded
        apply()
    }

    const createDoc = useCallback((): Doc => {
        const seed = document.createElement('div')
        seed.id = 'editor'
        hostRef.current?.appendChild(seed)
        // Muya replaces the element it is handed with its own, which inherits the attributes; that one is
        // what has to be detached and put back, so take it from the instance rather than keeping the seed.
        const o = optionsRef.current
        const muya = new Muya(seed, o)
        // The constructor already built the document with these, so they count as applied.
        ;(muya as any).__applied = {
            font: JSON.stringify({ fontSize: o?.fontSize, lineHeight: o?.lineHeight }),
            direction: JSON.stringify(o?.textDirection ?? null),
            spellcheck: JSON.stringify(!!o?.spellcheckEnabled),
            listIndentation: JSON.stringify(o?.listIndentation ?? null),
            readOnly: JSON.stringify(!!o?.readOnly)
        }
        return { muya, element: muya.container, markdown: '' }
    }, [])

    const show = useCallback((doc: Doc) => {
        const previous = activeRef.current
        if (previous === doc) return
        previous?.element.remove()
        hostRef.current?.appendChild(doc.element)
        activeRef.current = doc;
        (window as any).__typedownMuya = doc.muya // for Tools/EditorBench and DevTools inspection
        setEditor(doc.muya)
    }, [])

    useEffect(() => {
        const doc = createDoc()
        show(doc)
        const kept = keptRef.current
        return () => { doc.muya.destroy(); kept.forEach(k => k.muya.destroy()); kept.length = 0 }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    useEffect(() => {
        editor && Object.assign(editor.options, props.options)
    }, [editor, props.options])

    useEffect(() => {
        // Focus mode dims everything but the active block, and reading mode has no active block: together they
        // would grey out the whole document, so reading mode wins.
        editor?.setFocusMode(props.options.focusMode && !props.options.readOnly)
    }, [editor, props.options.focusMode, props.options.readOnly])

    useEffect(() => {
        const font = { fontSize: props.options?.fontSize, lineHeight: props.options?.lineHeight }
        applyOnce(editor, 'font', font, () => editor?.setFont(font))
    }, [editor, props.options?.fontSize, props.options?.lineHeight])

    useEffect(() => {
        applyOnce(editor, 'direction', props.options?.textDirection, () => editor?.setTextDirection(props.options?.textDirection))
    }, [editor, props.options?.textDirection])

    useEffect(() => {
        // Chromium/WebView2 spell checking on the contenteditable root (upstream #57)
        const on = !!props.options?.spellcheckEnabled
        applyOnce(editor, 'spellcheck', on, () => editor?.setOptions({ spellcheckEnabled: on }))
    }, [editor, props.options?.spellcheckEnabled])

    useEffect(() => {
        // Muya: 'dfm' = 4-space nested indentation, otherwise the number of spaces after the list marker (1-4).
        const value = props.options?.listIndentation
        applyOnce(editor, 'listIndentation', value, () => editor?.setListIndentation(value === 'dfm' ? 'dfm' : (parseInt(value, 10) || 1)))
    }, [editor, props.options?.listIndentation])

    useEffect(() => transport.addListener<{ slug: string }>('ScrollTo', ({ slug }) => {
        scrollToElement(`#${slug}`)
    }), [editor, scrollToElement]);

    useEffect(() => transport.addListener('UpdateParagraph', type => {
        if (optionsRef.current?.readOnly) return
        editor?.updateParagraph(type)
    }), [editor]);

    useEffect(() => transport.addListener('InsertParagraph', pos => {
        if (optionsRef.current?.readOnly) return
        editor?.insertParagraph(pos, '', true)
    }), [editor]);

    useEffect(() => transport.addListener('DeleteParagraph', () => {
        if (optionsRef.current?.readOnly) return
        editor?.deleteParagraph()
    }), [editor]);

    useEffect(() => transport.addListener('Duplicate', () => {
        if (optionsRef.current?.readOnly) return
        editor?.duplicate()
    }), [editor]);

    useEffect(() => transport.addListener('Format', type => {
        if (optionsRef.current?.readOnly) return
        editor?.format(type)
    }), [editor]);

    useEffect(() => transport.addListener('DeleteSelection', () => {
        if (optionsRef.current?.readOnly) return
        editor?.delete()
    }), [editor]);

    useEffect(() => transport.addListener('SelectAll', () => {
        editor?.selectAll()
    }), [editor]);

    useEffect(() => transport.addListener<any>('Copy', arg => {
        editor?.clipboard.copy(arg)
    }), [editor]);

    useEffect(() => transport.addListener<any>('Cut', arg => {
        if (optionsRef.current?.readOnly) return
        editor?.clipboard.cut(arg)
    }), [editor]);

    useEffect(() => transport.addListener<any>('Paste', arg => {
        if (optionsRef.current?.readOnly) return
        editor?.clipboard.paste(arg)
    }), [editor]);

    useEffect(() => transport.addListener<string>('InsertTable', arg => {
        if (optionsRef.current?.readOnly) return
        editor?.createTable(arg)
    }), [editor]);

    useEffect(() => transport.addListener<string>('InsertImage', arg => {
        if (optionsRef.current?.readOnly) return
        editor?.insertImage(arg)
    }), [editor]);

    useEffect(() => transport.addListener<{ value: string, opt: unknown }>('Search', (arg) => {
        props.onSearchArgChange(arg)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, props, scrollToElementIfInvisible]);

    useEffect(() => transport.addListener<{ action: string }>('Find', ({ action }) => {
        editor?.find(action)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, scrollToElementIfInvisible]);

    useEffect(() => transport.addListener<{ value: string, opt: unknown }>('Replace', ({ value, opt }) => {
        editor?.replace(value, opt)
    }), [editor, scrollToElement]);

    useEffect(() => {
        setMarginTop(currentMarginTop => {
            const newMarginTop = { 0: 0, 1: 50, 2: 90 }[props.searchOpen] ?? 0;
            relativeScroll(newMarginTop - currentMarginTop)
            return newMarginTop;
        })
    }, [props.searchOpen, relativeScroll])

    useEffect(() => transport.addListener<{ open: number }>('SearchOpenChange', ({ open }) => {
        if (open == 0 && editor) {
            const { contentState } = editor as any;
            contentState.setCursorToHighlight();
            contentState.searchMatches.matches = [];
            contentState.render(true);
            props.onSearchArgChange(undefined)
        }
    }), [editor, props, relativeScroll]);

    useEffect(() => transport.addListener<Record<string, unknown>>('SettingsChanged', (newOptions) => {
        for (const name in newOptions) {
            const value = newOptions[name];
            if (name == 'focusMode') {
                editor?.setFocusMode(value)
            } else if (name == 'typewriter') {
                value && scrollToCursor()
            }
        }
    }), [editor, scrollToCursor]);

    // What the last contentChange reported, so the outline can be re-sent with a different current heading
    // without the document having changed. The heading elements are looked up once per document.
    const lastStateRef = useRef<{ wordCount: any, toc: any[] } | null>(null)
    const headingsRef = useRef<{ item: any, el: HTMLElement }[] | null>(null)
    const readingSlugRef = useRef<string | null>(null)

    // The outline follows the caret, and reading mode has no caret — so the highlight used to stop wherever
    // the caret happened to be left and never move again. Anchor it to the scroll position instead: the
    // section being read is the one whose heading has passed the top of the window, and above the first
    // heading it is the first one.
    useEffect(() => {
        if (!editor || !props.options?.readOnly) return
        let frame = 0
        const anchor = () => {
            frame = 0
            const state = lastStateRef.current
            if (!state || !state.toc?.length) return
            if (!headingsRef.current) {
                headingsRef.current = state.toc
                    .map((item: any) => ({ item, el: document.getElementById(item.slug) as HTMLElement }))
                    .filter((pair: any) => pair.el)
            }
            const pairs = headingsRef.current
            if (!pairs.length) return
            // Headings sit in document order, so their tops only increase: a binary search reads a dozen
            // rectangles instead of one per heading, which matters on a document with thousands of them.
            let lo = 0, hi = pairs.length - 1, found = -1
            while (lo <= hi) {
                const mid = (lo + hi) >> 1
                if (pairs[mid].el.getBoundingClientRect().top <= 0) { found = mid; lo = mid + 1 }
                else hi = mid - 1
            }
            // Sections can be short enough for several headings to share the screen, so the line is the top
            // edge itself: a heading becomes current the moment it goes past it, not when it is merely visible.
            const cur = pairs[found >= 0 ? found : 0].item
            const slug = cur ? cur.slug : null
            if (slug === readingSlugRef.current) return
            readingSlugRef.current = slug
            transport.postMessage('StateChange', { state: { wordCount: state.wordCount, toc: state.toc, cur }, muya: true })
        }
        const onScroll = () => { if (!frame) frame = requestAnimationFrame(anchor) }
        window.addEventListener('scroll', onScroll, { passive: true })
        // The document may already be scrolled when reading mode is switched on, or restored to an offset.
        frame = requestAnimationFrame(anchor)
        return () => {
            window.removeEventListener('scroll', onScroll)
            if (frame) cancelAnimationFrame(frame)
        }
    }, [editor, props.options?.readOnly, props.contentVersion])

    const lastRenderedThemeRef = useRef<string | undefined>(window.actualTheme);
    useEffect(() => transport.addListener('ThemeChanged', () => {
        if (editor) {
            // Defer so the theme service's listener has updated window.actualTheme first. A full re-render is only
            // needed to recolor mermaid diagrams, and only when the theme really changed (the host also posts
            // ThemeChanged right after startup, which would otherwise re-render the whole document a second time).
            setTimeout(() => {
                const theme = window.actualTheme
                if (theme === lastRenderedThemeRef.current) return
                lastRenderedThemeRef.current = theme
                const { contentState } = editor as any
                const hasMermaid = (blocks: any[]): boolean => blocks.some(b => b.functionType === 'mermaid' || (b.children?.length && hasMermaid(b.children)))
                if (hasMermaid(contentState.getBlocks()))
                    contentState.render(true)
            }, 0)
        }
    }), [editor]);

    useEffect(() => editor?.on('selectionChange', (selection: any) => {
        const menuState = createApplicationMenuState(selection)
        const selectionText = window.getSelection()?.toString();
        transport.postMessage('SelectionChange', { selection, menuState, selectionText });
        const { y } = selection.cursorCoords
        // Typewriter mode centres the caret line; in reading mode there is no caret to follow.
        if (props.options?.typewriter && !props.options?.readOnly) {
            relativeScroll(y - window.innerHeight / 2 + 136);
        } else if (window.innerHeight - y < 100) {
            relativeScroll(y - window.innerHeight + 100);
        } else if (y < 100 && window.scrollY > 0) {
            // Keep the caret visible when it moves above the viewport (Up arrow / Page Up), upstream #51.
            relativeScroll(y - 100);
        }
    }), [editor, props.options?.typewriter, props.options?.readOnly, relativeScroll])

    useEffect(() => editor?.on('selectionFormats', (formats: any) => {
        const fotmats_simple = formats.map((e: any) => ({ type: e.type, tag: e.tag }));
        transport.postMessage('SelectionFormats', { formats: fotmats_simple });
    }), [editor])

    // Past this many blocks the browser is told it may skip laying out what is off screen (see the
    // .ag-long-document rule). Below it the bookkeeping costs more than it saves.
    const longDocumentBlocks = 6000

    // The class has to be on the element before the first render, or the browser lays the whole document out
    // once and then has to take it apart again — which costs more than it saves. Before the document is
    // parsed the only measure available is its length; afterwards the block count corrects it.
    // Raised from forty thousand: while the blocks off screen are unlaid out the page can only guess its own
    // height, and the scrollbar is drawn from that guess — the thumb ends up too small and the document runs
    // away from the pointer. Documents this side of three hundred thousand characters open in about a second
    // and type in under twenty milliseconds anyway, so they keep an exact scrollbar instead.
    const longDocumentChars = 300000

    const markLongDocument = useCallback((markdown?: string) => {
        // On the wrapper, not on the editor element itself: rendering patches that element and would strip a
        // class it does not know about.
        const root = document.getElementById('editor')
        if (!root) return
        const blocks = (editor as any)?.contentState?.blocks?.length ?? 0
        const long = markdown != null ? markdown.length > longDocumentChars : blocks > longDocumentBlocks
        root.classList.toggle('ag-long-document', long)
    }, [editor])

    useEffect(() => editor?.on('contentChange', ({ markdown, wordCount, cursor, toc: { toc, cur } }: any) => {
        markdownRef.current = markdown;
        markLongDocument()
        lastStateRef.current = { wordCount, toc }
        if (activeRef.current) activeRef.current.markdown = markdown
        headingsRef.current = null
        readingSlugRef.current = cur?.slug ?? null

        // 同步内容与光标
        props.onMarkdownChange(markdown)
        props.onCursorChange(cursor)

        // StateChange 必须在 onMarkdownChange、onCursorChange 之后发送，否则会导致编辑器内容/光标不同步
        transport.postMessage('StateChange', { state: { wordCount, toc, cur }, muya: true });
    }), [editor, props])

    useEffect(() => {
        const ele = document.getElementById('editor');
        if (ele) {
            ele.style.boxSizing = `border-box`
            ele.style.minHeight = `100vh`
            if (props.options?.typewriter && !props.options?.readOnly) {
                ele.style.paddingTop = `calc(50vh - ${136 - marginTop}px)`
                ele.style.paddingBottom = 'calc(50vh - 54px)'
            } else {
                ele.style.paddingTop = `${marginTop}px`
                ele.style.paddingBottom = '0'
            }
        }
    }, [marginTop, props.options?.typewriter, props.options?.readOnly])

    useEffect(() => {
        document.body.style.setProperty('--editorAreaWidth', props.options?.editorAreaWidth)
    }, [props.options?.editorAreaWidth])

    useEffect(() => {
        document.body.classList.toggle('hide-paragraph-marker', props.options?.showParagraphMarker === false)
    }, [props.options?.showParagraphMarker])

    // A custom theme and the user's own CSS are two style elements, in that order: a theme sets the palette,
    // and whatever the user writes in the settings still has the last word.
    useEffect(() => {
        const css = props.options?.themeCss || ''
        styleElement('typedown-theme-css').textContent = css
        // The host paints the body to match the window chrome; a theme that sets its own page colour should win.
        // Clearing it lets the next ThemeChanged from the host paint it again when the theme is switched off.
        if (css) document.body.style.backgroundColor = 'var(--editorBgColor)'
    }, [props.options?.themeCss])

    useEffect(() => {
        styleElement('typedown-custom-css').textContent = props.options?.customCss || ''
    }, [props.options?.customCss])

    useEffect(() => {
        const readOnly = !!props.options?.readOnly
        document.body.classList.toggle('read-only', readOnly)
        editor?.container?.setAttribute('contenteditable', String(!readOnly))
        // Re-render so the Markdown markers and the editing affordances of the active block disappear (and come
        // back with the caret when reading mode is switched off) instead of waiting for the next edit.
        applyOnce(editor, 'readOnly', readOnly, () => editor?.contentState?.render(!readOnly, true))
    }, [editor, props.options?.readOnly])

    useEffect(() => {
        if (props.options?.fontFamily) {
            document.body.style.setProperty('--editorFontFamily', props.options.fontFamily)
        } else {
            document.body.style.removeProperty('--editorFontFamily')
        }
    }, [props.options?.fontFamily])

    // Content is applied after every font/direction/padding/CSS-variable effect above so that the render's
    // synchronous selection placement forces exactly one layout; style changes after the render would make
    // the following focus() force a second full layout of the document.
    useEffect(() => {
        cursorRef.current = props.cursor
    }, [props.cursor])

    // Puts the page back where the document was left and keeps it there while the layout settles. Shared by
    // both ways a document arrives: rebuilt from its text, or restored from the one kept in memory.
    const settleScroll = useCallback((scrollTop: number, keepScroll: boolean) => {
        window.scrollTo(window.scrollX, scrollTop)
        if (!keepScroll) scrollToCursorIfInvisible()
        // Laying a long document out goes on for a while after the first paint, and the page can be put
        // back to the top by that work with nobody scrolling it — which is what made switching tabs show
        // the top of the document for a moment. Keep putting it back until the layout has settled, or
        // until the reader scrolls themselves.
        if (keepScroll && scrollTop > 0) {
            // Counted in frames, not milliseconds: laying out a long document blocks the main thread for
            // whole seconds, and a deadline in wall-clock time would expire while nothing could run.
            // It also runs to the end of its budget rather than stopping at the first few steady frames:
            // the position holds from the first paint and is knocked to the top a second or two later,
            // when the last of the layout lands, so an early stop means the watch is already over.
            let frames = 40
            let done = false
            // The knock arrives as a scroll event, so answer it there as well as on the next frame:
            // during the layout a frame can be a hundred milliseconds long, and that is a hundred
            // milliseconds of looking at the top of the document. Correcting puts scrollY back where it
            // belongs, so the event this fires in turn finds nothing to do.
            const putBack = () => { if (!done && Math.abs(window.scrollY - scrollTop) > 2) window.scrollTo(window.scrollX, scrollTop) }
            const stop = () => {
                done = true
                window.removeEventListener('scroll', putBack)
                window.removeEventListener('wheel', stop)
                window.removeEventListener('keydown', stop)
            }
            const hold = () => {
                if (done) return
                if (frames-- <= 0) return stop()
                putBack()
                requestAnimationFrame(hold)
            }
            window.addEventListener('scroll', putBack, { passive: true })
            window.addEventListener('wheel', stop, { once: true, passive: true })
            window.addEventListener('keydown', stop, { once: true })
            requestAnimationFrame(hold)
        }
        setTimeout(() => {
            window.scrollTo(window.scrollX, scrollTop)
            if (!keepScroll) scrollToCursorIfInvisible()
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            search(searchArgRef.current)
        }, 100);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.scrollFromHostRef, scrollToCursorIfInvisible, search])

    useEffect(() => {
        const active = activeRef.current
        if (!editor || !active) return
        if (markdownRef.current === props.markdown) { props.onContentApplied?.(); return }
        // The editor normalizes what it is given, and the host hands that normalized text back; applying it
        // again would be a second load — and a second load scrolls to the caret, which is why switching
        // tabs could jump to the top of the document a moment after landing in the right place.
        if (editor.getMarkdown() === props.markdown) {
            markdownRef.current = props.markdown
            props.onContentApplied?.()
            return
        }
        markdownRef.current = props.markdown
        const scrollTop = props.scrollTopRef.current
        const keepScroll = !!props.scrollFromHostRef?.current

        // Is this one of the documents still in memory? Its text is kept current by every change report, so
        // matching it means the elements on hand are exactly what building the text again would produce.
        const kept = keptRef.current
        const found = kept.findIndex(d => d.markdown === props.markdown)
        if (found >= 0) {
            const doc = kept.splice(found, 1)[0]
            if (active.markdown.length > 0) kept.push(active)
            show(doc)
            markLongDocument(props.markdown)
            // Nothing changed, so no report would follow on its own — and the host waits for one to know the
            // load finished. Asking for it also refreshes the word count and the outline for this document.
            doc.muya.dispatchChange()
            settleScroll(scrollTop, keepScroll)
            props.onContentApplied?.()
            return
        }

        // A new document: keep the one being left, and build the new one beside it. An empty editor — the one
        // the window starts with, before any file is open — is not a document and is reused rather than kept.
        let target = active
        if (active.markdown.length > 0 && !kept.includes(active)) {
            kept.push(active)
            const total = () => kept.reduce((n, d) => n + d.markdown.length, 0)
            while (kept.length > maxKept || (kept.length > 1 && total() > maxKeptChars)) kept.shift()!.muya.destroy()
            target = createDoc()
            show(target)
        }
        markLongDocument(props.markdown)
        target.markdown = props.markdown
        target.muya.setMarkdown(props.markdown, cursorRef.current)
        settleScroll(scrollTop, keepScroll)
        props.onContentApplied?.()
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, props.markdown, props.contentVersion, props.scrollTopRef, settleScroll, show, createDoc, markLongDocument])

    useEffect(() => {
        try {
            editor?.focus()
        } catch (err) {
            console.log(err)
        }
    }, [editor])

    useEffect(() => {
        const onscroll = () => {
            props.scrollTopRef.current = window.scrollY
        }
        addEventListener('scroll', onscroll);
        return () => removeEventListener('scroll', onscroll)
    }, [props.scrollTopRef])

    return (
        <div style={{
            fontSize: props.options?.fontSize,
            lineHeight: props.options?.lineHeight,
            fontFamily: props.options?.fontFamily ? `${props.options.fontFamily}, "Open Sans", "Segoe UI", sans-serif` : undefined
        }}>
            <div ref={hostRef} />
        </div>
    )

}

export default MuyaEditor