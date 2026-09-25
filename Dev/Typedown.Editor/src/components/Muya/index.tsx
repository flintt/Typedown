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
    /** Reports the outline, word count and caret line. The shell tags it with the load it belongs to. */
    onStateChange: (state: { wordCount: any, toc: any[], cur: any }) => void
    /** Reading mode only: the heading the page has been scrolled to. Highlight only — never a scroll. */
    /** Where the page was when it named the heading: its position among the headings, or that it was jumped to. */
    onOutlineCurrent: (slug: string, where: { y: number, index?: number, of?: number, top?: number, jump?: boolean }) => void
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
// Where a heading goes when it is chosen in the outline: at the top, a line's breathing room below the
// edge. The caret's 320 put it a third of the way down, and the reader then saw the section before it
// at the top of the window — and the outline, reading the top, marked that one instead.
const OUTLINE_TOP = 16

/** Anything that means the reader is moving the page themselves; the scroll hold stops at the first of them. */
const GIVE_WAY = ['wheel', 'keydown', 'pointerdown', 'touchstart']

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
    // The heading the outline was last told about in reading mode, and the moment until which a jump the
    // reader asked for stands: the jump's own scroll must not be re-read as the reader moving on.
    const readingSlugRef = useRef<string | null>(null)
    const jumpHoldRef = useRef(0)

    const relativeScroll = useCallback((delta: number) => {
        window.scrollBy(0, delta)
    }, [])

    const scrollToElement = useCallback((selector, offset = STANDAR_Y) => {
        if (editor == null) {
            return;
        }
        const anchor = document.querySelector(selector)
        if (anchor) {
            const { y } = anchor.getBoundingClientRect()
            relativeScroll(y - offset)
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

    // One editor, one document. Keeping the document a tab switch moved away from — a second editor held in
    // memory so returning to it was instant — was tried and taken out again. It bought about two seconds on
    // a switch between long documents and cost five faults: a new editor rendering into the document on
    // screen and wiping it, one held in memory answering the keyboard and deleting an image nobody could
    // see, the report after a switch reaching nobody so the outline described the previous document, a loop
    // with the outline that made the page twitch, and one that only ever appeared with the host running and
    // that nothing here could reproduce. Every one was found in use, none by a check.
    //
    // Two things it left behind are worth keeping, because both are right on their own: StateRender renders
    // into its own container rather than looking its root up in the document, and the setters below only
    // push a setting that has actually changed instead of re-rendering the whole document each time.

    // Several of Muya's setters re-render the whole document, which is fine once and wasteful every time
    // after: the editor remembers what has been applied to it, so a setting is only pushed when it changed.
    const applyOnce = (muya: any, key: string, value: unknown, apply: () => void) => {
        if (!muya) return
        const applied = muya.__applied || (muya.__applied = {})
        const encoded = JSON.stringify(value ?? null)
        if (applied[key] === encoded) return
        applied[key] = encoded
        apply()
    }

    useEffect(() => {
        const ele = document.getElementById('editor')
        const o = optionsRef.current
        const muya = new Muya(ele, o);
        // The constructor already built the document with these, so they count as applied.
        (muya as any).__applied = {
            font: JSON.stringify({ fontSize: o?.fontSize, lineHeight: o?.lineHeight }),
            direction: JSON.stringify(o?.textDirection ?? null),
            spellcheck: JSON.stringify(!!o?.spellcheckEnabled),
            listIndentation: JSON.stringify(o?.listIndentation ?? null),
            readOnly: JSON.stringify(!!o?.readOnly)
        };
        (window as any).__typedownMuya = muya // for Tools/EditorBench and DevTools inspection
        setEditor(muya)
        return () => muya.destroy()
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
        scrollToElement(`#${slug}`, OUTLINE_TOP)
        // The reader chose this heading, so this is the one the outline marks — also at the end of the
        // document, where the page cannot scroll far enough to bring it to the top and reading the top
        // would name the section before it.
        if (props.options?.readOnly && readingSlugRef.current !== slug) {
            readingSlugRef.current = slug
            jumpHoldRef.current = performance.now() + 300
            props.onOutlineCurrent(slug, { y: Math.round(window.scrollY), jump: true })
        }
    }), [editor, scrollToElement, props.options?.readOnly]);

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

    // The outline used to follow the page here in reading mode, where there is no caret for it to follow.
    // It was taken out: it reported the heading it had scrolled to through StateChange, the same message
    // the host reads `cur` from to decide where to scroll — so the page scrolled, reported a heading, was
    // scrolled to it and reported again, with nobody touching it. The outline flickered and collapsed as
    // the host rebuilt it, text could not be dragged because the selection died on every render, and it
    // ended in a crash.
    //
    // Following the page is worth having, but not on the channel the host uses to move the page. It needs a
    // message of its own that only ever sets the highlight. Until then the highlight stays on the caret,
    // which in reading mode means it stays where the caret was left.

    // Reading mode has no caret, so the outline has nothing to follow. Anchor it to the page instead: the
    // section being read is the one whose heading has passed the top of the window. The headings are the
    // top-level h1..h6 of the document and their element id is the slug the outline uses, so this needs
    // nothing from the host and reports nothing but a slug.
    useEffect(() => {
        if (!editor || !props.options?.readOnly) return
        let frame = 0
        let retries = 60
        const anchor = () => {
            frame = 0
            if (performance.now() < jumpHoldRef.current) return
            const root = editor.container?.querySelector?.('#ag-editor-id') || document.getElementById('ag-editor-id')
            const heads = root ? Array.from(root.querySelectorAll(':scope > h1, :scope > h2, :scope > h3, :scope > h4, :scope > h5, :scope > h6')) as HTMLElement[] : []
            if (!heads.length) return
            // Every position read in one pass, before looking at any of them. A binary search reading one
            // rectangle at a time was asking the browser where things are while the page was still moving —
            // each comparison saw a different scroll position, so "the tops only increase" stopped being
            // true and the answer landed anywhere: scrolling down reported headings from further up.
            const tops = heads.map(h => h.getBoundingClientRect().top)
            // Only a finished layout is worth reading. While the document is still being laid out the
            // headings that have no box yet all sit at zero, which reads as "already scrolled past", and the
            // outline was seen to jump to the last heading, then the fifth, then the right one — all in the
            // first tenth of a second after opening. Positions in document order are what a real layout has.
            // An unfinished one is looked at again next frame, for a while: giving up on it left the
            // outline pointing at a place the page had merely passed through on its way somewhere else.
            for (let i = 1; i < tops.length; i++) if (tops[i] <= tops[i - 1]) {
                if (retries-- > 0) frame = requestAnimationFrame(anchor)
                return
            }
            retries = 60
            // A heading counts as reached once it is where a jump would have put it, not only once it has
            // gone past the edge: the two have to agree, or a chosen heading is marked as the one before.
            let found = -1
            for (let i = 0; i < tops.length; i++) { if (tops[i] <= OUTLINE_TOP + 1) found = i; else break }
            const chosen = found >= 0 ? found : 0
            const slug = heads[chosen].id
            if (!slug || slug === readingSlugRef.current) return
            readingSlugRef.current = slug
            // The position goes with it: in the log the heading can then be checked against where the page
            // actually was, which is the only way to tell a wrong answer from a document that simply moved.
            props.onOutlineCurrent(slug, { y: Math.round(window.scrollY), index: chosen, of: heads.length, top: Math.round(tops[chosen]) })
        }
        const onScroll = () => { if (!frame) frame = requestAnimationFrame(anchor) }
        window.addEventListener('scroll', onScroll, { passive: true })
        // Headings also move under a page that has not scrolled: diagrams and images finish rendering
        // and everything below them shifts. The document's height changes with it, and that is watched
        // too — the outline was left on the heading that had been at the top before the diagrams came in.
        const root = editor.container?.querySelector?.('#ag-editor-id') || document.getElementById('ag-editor-id')
        const sizes = typeof ResizeObserver === 'function' && root ? new ResizeObserver(onScroll) : null
        if (root) sizes?.observe(root)
        frame = requestAnimationFrame(anchor)
        return () => {
            window.removeEventListener('scroll', onScroll)
            sizes?.disconnect()
            if (frame) cancelAnimationFrame(frame)
            readingSlugRef.current = null
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
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
        // Only a caret the reader moved is worth following. A load reports a selection too, through two
        // timers, which on a long document fire a good while after the page has been put where the
        // document was left — and following that "movement" took the page to wherever the caret had been
        // remembered, the top of the document as often as not. That was the jump to the top after a tab
        // switch, and in reading mode there is no caret to follow at all.
        if (selection?.fromLoad || props.options?.readOnly) return
        const { y } = selection.cursorCoords
        // Typewriter mode centres the caret line.
        if (props.options?.typewriter) {
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

        // 同步内容与光标
        props.onMarkdownChange(markdown)
        props.onCursorChange(cursor)

        // StateChange 必须在 onMarkdownChange、onCursorChange 之后发送，否则会导致编辑器内容/光标不同步
        props.onStateChange({ wordCount, toc, cur })
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
        // One flag for everything that puts the page back, the frame-by-frame hold and the delayed second
        // go alike: the moment the reader moves the page, none of them may touch it again. The delayed one
        // used to ignore this and yanked the page back a tenth of a second after a drag.
        let yielded = false
        const giveWay = () => { yielded = true; for (const e of GIVE_WAY) window.removeEventListener(e, giveWay) }
        for (const e of GIVE_WAY) window.addEventListener(e, giveWay, { once: true, passive: true })
        // Laying a long document out goes on for a while after the first paint, and the page can be put
        // back to the top by that work with nobody scrolling it — which is what made switching tabs show
        // the top of the document for a moment. Keep putting it back until the layout has settled, or
        // until the reader scrolls themselves.
        let stopHold = () => { }
        if (keepScroll && scrollTop > 0) {
            // Counted in frames, not milliseconds: laying out a long document blocks the main thread for
            // whole seconds, and a deadline in wall-clock time would expire while nothing could run.
            // Only frames in which the document is tall enough to be scrolled that far count towards the
            // budget: while it is still being built the offset cannot be applied at all, the page sits at
            // the top, and a budget spent there ran out just as the document became tall enough — the
            // reader then saw the top of the new document for a third of a second before it moved.
            let frames = 40
            let cap = 600
            let done = false
            const tall = () => document.documentElement.scrollHeight >= scrollTop + window.innerHeight
            // The knock arrives as a scroll event, so answer it there as well as on the next frame:
            // during the layout a frame can be a hundred milliseconds long, and that is a hundred
            // milliseconds of looking at the top of the document. Correcting puts scrollY back where it
            // belongs, so the event this fires in turn finds nothing to do.
            const putBack = () => { if (!done && !yielded && Math.abs(window.scrollY - scrollTop) > 2) window.scrollTo(window.scrollX, scrollTop) }
            const stop = () => { done = true; window.removeEventListener('scroll', putBack); for (const e of GIVE_WAY) window.removeEventListener(e, giveWay) }
            const hold = () => {
                if (done) return
                // Dragging the scrollbar raises no wheel event, so watching the wheel alone left the hold
                // pulling the page back from under the pointer — the reader drags, it yanks.
                if (yielded || cap-- <= 0 || (tall() && frames-- <= 0)) return stop()
                putBack()
                requestAnimationFrame(hold)
            }
            window.addEventListener('scroll', putBack, { passive: true })
            requestAnimationFrame(hold)
            stopHold = stop
        }
        setTimeout(() => {
            if (!yielded) {
                window.scrollTo(window.scrollX, scrollTop)
                if (!keepScroll) scrollToCursorIfInvisible()
            }
            // The hold above decides for itself when it is over; ending it here ended it after six frames,
            // whatever its budget said. The reader's own movement still ends everything at once.
            if (!keepScroll) giveWay()
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            search(searchArgRef.current)
        }, 100);
        void stopHold
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.scrollFromHostRef, scrollToCursorIfInvisible, search])

    // The host takes the web view out of its window and puts it back when it navigates between its own
    // pages (leaving the settings does that), and the browser is at the top when it comes back. The host
    // sends the offset it last heard of; the page waits for a window to exist again before going there.
    useEffect(() => transport.addListener<{ y: number }>('RestoreScroll', ({ y }) => {
        if (!(y > 0)) return
        let tries = 120
        const go = () => { if (window.innerHeight > 0 || tries-- <= 0) settleScroll(y, true); else requestAnimationFrame(go) }
        go()
    }), [settleScroll]);

    useEffect(() => {
        if (!editor) return
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
        markLongDocument(props.markdown)
        editor.setMarkdown(props.markdown, cursorRef.current)
        settleScroll(props.scrollTopRef.current, !!props.scrollFromHostRef?.current)
        props.onContentApplied?.()
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, props.markdown, props.contentVersion, props.scrollTopRef, settleScroll, markLongDocument])

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
            <div id="editor" />
        </div>
    )

}

export default MuyaEditor