import React, { useCallback, useEffect, useRef, useState } from "react";
import { anchorKey, PREVIEW_FROM_CHARS, ScrollAnchor, scrollForAnchor } from "services/loadPreview";

/**
 * The block of a new document's model the scroll anchor names (services/loadPreview): the top-level block's index, to
 * draw first, and the key of the block whose text it is, to put back at the anchor's height. Looked for from where
 * the page was through the document; null if not there.
 */
function findAnchorBlock(blocks: any[], anchor: ScrollAnchor): { index: number, leafKey: string } | null {
    const key = anchorKey(anchor.text ?? '').slice(0, 40)
    if (key.length < 4) return null
    const leafIn = (block: any): string | null => {
        if (typeof block?.text === 'string' && block.text.length) {
            const text = anchorKey(block.text)
            const n = Math.min(key.length, text.length)
            if (n >= 4 && key.slice(0, n) === text.slice(0, n)) return block.key
        }
        for (const child of block?.children ?? []) {
            const found = leafIn(child)
            if (found) return found
        }
        return null
    }
    const from = Math.max(0, Math.min(blocks.length - 1, Math.round((anchor.at ?? 0) * blocks.length)))
    for (let d = 0; d < blocks.length; d++) {
        for (const index of d === 0 ? [from] : [from + d, from - d]) {
            if (index < 0 || index >= blocks.length) continue
            const leafKey = leafIn(blocks[index])
            if (leafKey) return { index, leafKey }
        }
    }
    return null
}
import transport, { beforeHostMessage } from "services/transport";
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
import { classifyNormalization, extractProtectedPayload } from "services/normalization";
import { highlightExternalChange, scrollTargetForChange } from "services/externalChange";
import { setVimState } from "services/vim";
import { createReadingKeys } from "services/vimReading";
import { KeptDocuments, blockCaretKey, caretKey, isForward, markedAt, sameLines } from "services/keptDocuments";
import { CURSOR_ANCHOR_DNA, CURSOR_FOCUS_DNA } from 'components/Muya/lib/config'
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
    /** The block that was at the top of the window when the document was left: put back there rather than by offset. */
    scrollAnchorRef?: React.MutableRefObject<ScrollAnchor | null>
    /** True when the new markdown is an edit of the text shown (automation): only the changed blocks are replaced. */
    localChangeRef?: React.MutableRefObject<boolean>
    scrollToChangeRef?: React.MutableRefObject<boolean>
    onMarkdownChange: (markdown: string) => void
    /** The shell puts a function here that reports the current text at once, for saves and exports. */
    flushRef?: React.MutableRefObject<(() => void) | null>
    /** Set to what finishes a load whose caret is still to come, now (Muya.finishLoadNow). */
    finishLoadRef?: React.MutableRefObject<(() => void) | null>
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
/** Selects the document's text (reading mode's select-all). */
function selectDocument(container: HTMLElement | undefined) {
    const selection = window.getSelection()
    if (!container || !selection) return
    const range = document.createRange()
    range.selectNodeContents(container)
    selection.removeAllRanges()
    selection.addRange(range)
}

Muya.use(LinkTools, { jumpClick: (linkInfo: { href: string }) => { transport.postMessage('OpenURI', { uri: linkInfo.href }) } })
Muya.use(TableBarTools)
Muya.use(FootnoteTool)

// Keeping documents a tab switch left (see the content effect). Only long ones: a short document is built again in a
// few milliseconds and keeping it would be risk for nothing. Two at most and 600,000 characters in all, because a kept
// document holds all its elements: measured (Tools/EditorBench/switch-ab.js, Edge), Tools/EditorBench/doc300k.md
// (300,000 characters, 180,000 nodes) held about 80 MB of the page's memory and 9 MB of its script heap, a plainer
// 325,000-character document (54,000 nodes) about 20 MB. Two is what switching back and forth between two or three
// long tabs needs; a third pushes the oldest out.
const KEEP_FROM_CHARS = 50000
const KEEP_DOCUMENTS = 2
const KEEP_CHARS = 600000
const DIAGRAM_TYPES = new Set(['mermaid', 'flowchart', 'sequence', 'plantuml', 'vega-lite'])
const hasDiagram = (blocks: any[]): boolean => blocks.some(b => DIAGRAM_TYPES.has(b.functionType) || (b.children?.length && hasDiagram(b.children)))
// Keys that move the caret or the page and change nothing in the document.
const MOVING_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown', 'Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'Escape'])

/** What is known of the document shown, to tell whether it may be kept when a tab switch replaces it. */
interface ShownDocument {
    text: string
    /** Its source and Muya's export of it (importedRef) and whether the first-edit notice was shown for it. */
    imported: { source: string, normalized: string }
    warn: boolean
    /** Everything the drawing depends on besides the text and the caret, as it was when the document was built. */
    renderKey: string
    /** The caret the host gave with the text, and where the build put it in the blocks. */
    loadCaret: { given: string, at: string }
    blockCount: number
    /** Nothing has been done to it but moving the caret and the page. */
    pristine: boolean
    /** The caret its last report gave the host, and where it was in the blocks then. */
    reported: { caret: string, at: string } | null
}

/** Anything done to the document shown other than moving the caret or the page: it is not kept when left. */
const markEdited = (shown: React.MutableRefObject<ShownDocument | null>) => { if (shown.current) shown.current.pristine = false }

interface KeptDocument extends ShownDocument {
    cursorAt: string
    kept: { blocks: any[], cursor: any, root: HTMLElement }
}

const STANDAR_Y = 320
// Where a heading goes when it is chosen in the outline: at the top, a line's breathing room below the
// edge. The caret's 320 put it a third of the way down, and the reader then saw the section before it
// at the top of the window — and the outline, reading the top, marked that one instead.
const OUTLINE_TOP = 16

/** Anything that means the reader is moving the page themselves; the scroll hold stops at the first of them. */
const GIVE_WAY = ['wheel', 'keydown', 'pointerdown', 'touchstart']

const MuyaEditor: React.FC<IMuyaEditor> = (props) => {
    const { onMarkdownChange, onCursorChange, onStateChange, onOutlineCurrent } = props
    const [editor, setEditor] = useState<Muya>();
    const [marginTop, setMarginTop] = useState(0);
    const markdownRef = useRef('');
    // Import/export normalizes Markdown. Keep the original text while the document
    // still exports to its initial value, including after an undo back to that value.
    const importedRef = useRef<{ source: string, normalized: string }>();
    // The notice is put in and taken out by hand, not through React state: a React commit while the editor has the
    // focus walks the whole document to save and put back the selection (react-dom's getSelectionInformation), and
    // on a document with diagrams - thousands of SVG elements - that took over a second, once as the notice came with
    // the load and again as it went ten seconds later.
    const warningSlotRef = useRef<HTMLDivElement>(null)
    const firstEditWarningRef = useRef(false)
    const readOnlyRef = useRef(!!props.options?.readOnly)
    const firstEditWarningTimerRef = useRef<number | null>(null)
    const drawFirstEditWarningRef = useRef<() => void>(() => { })
    const showFirstEditWarning = useCallback((show: boolean) => {
        if (firstEditWarningTimerRef.current !== null) window.clearTimeout(firstEditWarningTimerRef.current)
        firstEditWarningTimerRef.current = null
        firstEditWarningRef.current = show
        drawFirstEditWarningRef.current()
        if (show) firstEditWarningTimerRef.current = window.setTimeout(() => {
            firstEditWarningTimerRef.current = null
            firstEditWarningRef.current = false
            drawFirstEditWarningRef.current()
        }, 10000)
    }, [])
    drawFirstEditWarningRef.current = () => {
        const slot = warningSlotRef.current
        if (!slot) return
        const show = firstEditWarningRef.current && !readOnlyRef.current
        if (show === !!slot.firstElementChild) return
        if (!show) { slot.replaceChildren(); return }
        const notice = document.createElement('div')
        notice.className = 'td-first-edit-warning'
        notice.setAttribute('role', 'status')
        const text = document.createElement('span')
        text.className = 'td-first-edit-warning-text'
        const close = document.createElement('button')
        close.className = 'td-first-edit-warning-close'
        close.setAttribute('aria-label', 'Close')
        close.textContent = '×'
        close.addEventListener('click', () => showFirstEditWarning(false))
        notice.append(text, close)
        slot.append(notice)
    }
    useEffect(() => {
        readOnlyRef.current = !!props.options?.readOnly
        drawFirstEditWarningRef.current()
    }, [props.options?.readOnly])
    useEffect(() => () => {
        if (firstEditWarningTimerRef.current !== null) window.clearTimeout(firstEditWarningTimerRef.current)
    }, [])
    const searchArgRef = useRef<any>();
    const cursorRef = useRef<any>();
    const shownRef = useRef<ShownDocument | null>(null)
    const keptRef = useRef(new KeptDocuments<KeptDocument>(KEEP_DOCUMENTS, KEEP_CHARS))
    const optionsRef = useRef<any>(props.options);
    // The heading the outline was last told about in reading mode, and the moment until which a jump the
    // reader asked for stands: the jump's own scroll must not be re-read as the reader moving on.
    const readingSlugRef = useRef<string | null>(null)
    const jumpHoldRef = useRef(0)
    // Where the caret was at the last selection change, so a report of an unmoved caret is not followed.
    const lastSelectionRef = useRef('')

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
        importedRef.current = undefined
        shownRef.current = null
        keptRef.current.clear()
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
    //
    // What is kept now (keepShown, takeKept below) is not an editor: the one editor stays the only one, and a long
    // document left by a tab switch is kept as inert parts - its blocks, its caret and its elements, out of the
    // page - that nothing listens to, renders into or reports from. Switching back to it with the same text puts
    // them back into the one editor, and from there the load goes on exactly as a new build's would.

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
            renderPlantUml: JSON.stringify([!!o?.renderPlantUml, o?.plantUmlServer ?? '']),
            listIndentation: JSON.stringify(o?.listIndentation ?? null),
            readOnly: JSON.stringify(!!o?.readOnly)
        };
        (window as any).__typedownMuya = muya; // for Tools/EditorBench and DevTools inspection
        // What the next real visual edit would rewrite in the document (services/normalization): the loaded
        // source against Muya's export of it. Once an edit has made the export the text there is nothing left
        // to rewrite. Null before anything was loaded.
        // Also returns the normalized text itself, for its hash in the automation edit reply.
        const pendingNormalization = () => {
            const imported = importedRef.current
            if (!imported) return null
            return markdownRef.current === imported.source
                ? { ...classifyNormalization(imported.source, imported.normalized), normalized: imported.normalized }
                : { ...classifyNormalization(markdownRef.current, markdownRef.current), normalized: markdownRef.current }
        };
        (window as any).__typedownPendingNormalization = pendingNormalization
        // For Tools/EditorBench: how many documents are kept, and a way to drop them.
        const keptHook = { size: () => keptRef.current.size, clear: () => keptRef.current.clear(), has: (text: string) => keptRef.current.has(text) };
        (window as any).__typedownKeptDocuments = keptHook
        // For Tools/EditorBench: the same classifier, to judge what a real edit did.
        ;(window as any).__typedownNormalization = { classifyNormalization, extractProtectedPayload }
        setEditor(muya)
        return () => {
            // Source mode has no Muya: a hook left behind would describe the document as it was when Muya went.
            if ((window as any).__typedownPendingNormalization === pendingNormalization) delete (window as any).__typedownPendingNormalization
            if ((window as any).__typedownKeptDocuments === keptHook) delete (window as any).__typedownKeptDocuments
            keptHook.clear()
            muya.destroy()
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // A document drawn a part at a time is drawn to the end before anything works on its elements: the reader pressing,
    // clicking, pasting or copying in the page (not the scrollbar, nor the wheel - reading goes on while it is drawn),
    // and every command from the host but those that only set what the page looks like or replace the document.
    useEffect(() => {
        if (!editor) return
        const muya = editor as any
        const finish = (e: Event) => {
            if (!muya.renderPending) return
            if (e.type === 'mousedown' && e.target === document.documentElement) return
            muya.completeRender()
        }
        const events = ['keydown', 'mousedown', 'paste', 'cut', 'copy', 'compositionstart', 'dragstart', 'drop', 'contextmenu']
        for (const type of events) document.addEventListener(type, finish, true)
        const passive = new Set(['LoadFile', 'SetMarkdown', 'HoldInput', 'ReleaseInput', 'ThemeChanged', 'SettingsChanged', 'LanguageChanged',
            'OnScroll', 'RestoreScroll', 'FlushContent', 'QueryNormalization', 'QueryEditorStyle', 'AwaitPresentation'])
        const hook = (name: string) => { if (muya.renderPending && !passive.has(name) && !name.startsWith('invoke_')) muya.completeRender() }
        beforeHostMessage.add(hook)
        return () => {
            for (const type of events) document.removeEventListener(type, finish, true)
            beforeHostMessage.delete(hook)
        }
    }, [editor])

    useEffect(() => {
        editor && Object.assign(editor.options, props.options)
    }, [editor, props.options])

    useEffect(() => {
        // Focus mode dims everything but the active block, and reading mode has no active block: together they
        // would grey out the whole document, so reading mode wins.
        editor?.setFocusMode(props.options.focusMode && !props.options.readOnly)
    }, [editor, props.options.focusMode, props.options.readOnly])

    useEffect(() => {
        // Vim keys move around in reading mode (services/vimReading); the visual editor has none.
        if (!props.options?.vimMode || !props.options?.readOnly) {
            setVimState('off')
            return
        }
        setVimState('reading')
        const listener = createReadingKeys(
            () => ({ top: () => window.scrollY, by: dy => window.scrollTo({ top: window.scrollY + dy, behavior: 'instant' as ScrollBehavior }), to: y => window.scrollTo({ top: Math.min(y, document.documentElement.scrollHeight), behavior: 'instant' as ScrollBehavior }), height: () => window.innerHeight }),
            () => Array.from(document.querySelectorAll<HTMLElement>('#editor h1, #editor h2, #editor h3, #editor h4, #editor h5, #editor h6')))
        window.addEventListener('keydown', listener, true)
        return () => window.removeEventListener('keydown', listener, true)
    }, [props.options?.vimMode, props.options?.readOnly])

    useEffect(() => {
        const font = { fontSize: props.options?.fontSize, lineHeight: props.options?.lineHeight }
        applyOnce(editor, 'font', font, () => editor?.setFont(font))
    }, [editor, props.options?.fontSize, props.options?.lineHeight])

    // The tab size setting reaches both what a tab key inserts (Muya) and how wide a tab character is drawn
    // (CSS tab-size, inherited from the root below); without the latter, pasted code with tabs showed
    // eight columns per tab whatever the setting said.
    useEffect(() => {
        const tab = Number(props.options?.tabSize) || 4
        applyOnce(editor, 'tabSize', tab, () => editor?.setTabSize(tab))
    }, [editor, props.options?.tabSize])

    useEffect(() => {
        applyOnce(editor, 'direction', props.options?.textDirection, () => editor?.setTextDirection(props.options?.textDirection))
    }, [editor, props.options?.textDirection])

    useEffect(() => {
        // Chromium/WebView2 spell checking on the contenteditable root (upstream #57)
        const on = !!props.options?.spellcheckEnabled
        applyOnce(editor, 'spellcheck', on, () => editor?.setOptions({ spellcheckEnabled: on }))
    }, [editor, props.options?.spellcheckEnabled])

    useEffect(() => {
        // PlantUML is drawn by plantuml.com from the block's source: only when the person turned it on. Rendering again
        // draws the blocks shown, or puts the notice back.
        // The same when the server changes (Settings > Editor > PlantUML server).
        const on = !!props.options?.renderPlantUml
        const server = props.options?.plantUmlServer ?? ''
        applyOnce(editor, 'renderPlantUml', [on, server], () => editor?.setOptions({ renderPlantUml: on, plantUmlServer: server }, true))
    }, [editor, props.options?.renderPlantUml, props.options?.plantUmlServer])

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
            onOutlineCurrent(slug, { y: Math.round(window.scrollY), jump: true })
        }
    }), [editor, scrollToElement, props.options?.readOnly, onOutlineCurrent]);

    useEffect(() => transport.addListener('UpdateParagraph', type => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.updateParagraph(type)
    }), [editor]);

    useEffect(() => transport.addListener('InsertParagraph', pos => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.insertParagraph(pos, '', true)
    }), [editor]);

    useEffect(() => transport.addListener('DeleteParagraph', () => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.deleteParagraph()
    }), [editor]);

    useEffect(() => transport.addListener('Duplicate', () => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.duplicate()
    }), [editor]);

    useEffect(() => transport.addListener('Format', type => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.format(type)
    }), [editor]);

    useEffect(() => transport.addListener('DeleteSelection', () => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.delete()
    }), [editor]);

    useEffect(() => transport.addListener('SelectAll', () => {
        // Reading mode: the page is not editable and never has the focus Muya's select-all asks for, so it
        // selected nothing (and a copy after it copied nothing). The whole document is selected as text instead.
        if (optionsRef.current?.readOnly) {
            selectDocument(editor?.container)
            return
        }
        editor?.selectAll()
    }), [editor]);

    useEffect(() => transport.addListener<any>('Copy', arg => {
        editor?.clipboard.copy(arg)
    }), [editor]);

    useEffect(() => transport.addListener<any>('Cut', arg => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.clipboard.cut(arg)
    }), [editor]);

    useEffect(() => transport.addListener<any>('Paste', arg => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.clipboard.paste(arg)
    }), [editor]);

    // Text typed while this document was on its way (services/inputHold.ts), in at the caret as a plain paste is: Enter
    // a new paragraph.
    useEffect(() => transport.addListener<{ text: string, done: () => void }>('InsertTyped', ({ text, done }) => {
        if (!editor || optionsRef.current?.readOnly) { done(); return }
        markEdited(shownRef)
        const paragraphs = text.replace(/\r\n?/g, '\n').replace(/\n/g, '\n\n')
        Promise.resolve((editor as any).contentState.pasteHandler('normal', paragraphs, ''))
            .catch((err: unknown) => console.log('typed text could not go in', err))
            .finally(() => { (editor as any).flushContentChange?.(); done() })
    }), [editor]);

    useEffect(() => transport.addListener('TakeKeyboard', () => { if (editor) restoreEditorFocus(editor) }), [editor]);

    useEffect(() => transport.addListener<string>('InsertTable', arg => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.createTable(arg)
    }), [editor]);

    useEffect(() => transport.addListener<string>('InsertImage', arg => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.insertImage(arg)
    }), [editor]);

    useEffect(() => transport.addListener<{ src: string, alt?: string, title?: string }[]>('InsertImages', arg => {
        if (optionsRef.current?.readOnly) return
        markEdited(shownRef)
        editor?.insertImages(arg)
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
        markEdited(shownRef)
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
            // Back from the search with the keyboard in the page: the editor has it, not the body.
            if (document.hasFocus()) restoreEditorFocus(editor)
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
        // A tab switch to or from a kept document swaps the root element (Muya.takeDocument): the one shown is watched.
        const offRoot = editor.on('rootChange', (next: Element) => { if (sizes) { sizes.disconnect(); sizes.observe(next) } })
        frame = requestAnimationFrame(anchor)
        return () => {
            window.removeEventListener('scroll', onScroll)
            offRoot()
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
        // And only a caret that actually moved. Muya reports a selection change on every key-up, modifier
        // keys included, so releasing Ctrl after Ctrl+clicking an anchor link reported the unmoved caret —
        // now above the window, since the page had just gone to the anchor — and the rule below scrolled
        // straight back up to it. The link "did nothing". Same for Ctrl+wheel after scrolling away.
        const at = `${selection?.start?.key}:${selection?.start?.offset}:${selection?.end?.key}:${selection?.end?.offset}`
        const moved = at !== lastSelectionRef.current
        lastSelectionRef.current = at
        if (selection?.fromLoad || props.options?.readOnly || !moved) return
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
        const imported = importedRef.current
        const text = imported && markdown === imported.normalized ? imported.source : markdown
        markdownRef.current = text;
        markLongDocument()
        const shown = shownRef.current
        if (shown) {
            if (!imported || markdown !== imported.normalized) shown.pristine = false
            shown.reported = { caret: caretKey(cursor), at: blockCaretKey((editor as any).contentState.cursor) }
        }

        // 同步内容与光标
        onMarkdownChange(text)
        onCursorChange(cursor)

        // StateChange 必须在 onMarkdownChange、onCursorChange 之后发送，否则会导致编辑器内容/光标不同步
        onStateChange({ wordCount, toc, cur })
    }), [editor, markLongDocument, onMarkdownChange, onCursorChange, onStateChange])

    useEffect(() => {
        if (!editor || !props.flushRef) return
        const flush = () => editor.flushContentChange()
        props.flushRef.current = flush
        return () => {
            if (props.flushRef?.current === flush) props.flushRef.current = null
        }
    }, [editor, props.flushRef])

    useEffect(() => {
        if (!editor || !props.finishLoadRef) return
        const finish = () => editor.finishLoadNow()
        props.finishLoadRef.current = finish
        return () => {
            if (props.finishLoadRef?.current === finish) props.finishLoadRef.current = null
        }
    }, [editor, props.finishLoadRef])

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

    // Extra blank lines (empty paragraphs) keep their height in reading mode too (assets/styles/index.css).
    useEffect(() => {
        document.body.classList.toggle('show-blank-lines', props.options?.showBlankLines === true)
    }, [props.options?.showBlankLines])

    useEffect(() => {
        const readOnly = !!props.options?.readOnly
        document.body.classList.toggle('read-only', readOnly)
        editor?.container?.setAttribute('contenteditable', String(!readOnly))
        // Re-render so the Markdown markers and the editing affordances of the active block disappear (and come
        // back with the caret when reading mode is switched off) instead of waiting for the next edit.
        applyOnce(editor, 'readOnly', readOnly, () => editor?.contentState?.render(!readOnly, true))
    }, [editor, props.options?.readOnly])

    // A paste the browser does itself (Ctrl+V reaching the page while the host's shortcut did not take it) put the
    // clipboard's HTML straight into the document, as foreign elements Muya does not know and never reports: the
    // document on screen and the text saved parted ways. Every paste goes through the host's (Edit > Paste), which
    // brings images and files along and hands the page the text and HTML; reading mode takes none.
    useEffect(() => {
        if (!editor) return
        const onPaste = (event: ClipboardEvent) => {
            const container = editor.container as HTMLElement
            if (!(event.target instanceof Node) || !container.contains(event.target)) return
            event.preventDefault()
            event.stopPropagation()
            if (optionsRef.current?.readOnly) return
            transport.postMessage('PasteRequested', { type: 'normal' })
        }
        // Copy and cut the same way: the browser's own copy (Ctrl+C reaching the page) put the page's markup on the
        // clipboard, with the editor's internal picture addresses Word cannot load and text without the Markdown.
        const onCopy = (event: ClipboardEvent) => {
            const container = editor.container as HTMLElement
            const state = editor.contentState as any
            // Table cells selected as cells: their own copy (a Markdown table), which nothing called.
            if (state?.selectedTableCells) {
                event.preventDefault()
                event.stopPropagation()
                state.docCopyHandler()
                if (event.type === 'cut' && !optionsRef.current?.readOnly) state.docCutHandler()
                return
            }
            const selection = window.getSelection()
            const text = !!selection && !selection.isCollapsed && selection.rangeCount > 0 && selection.getRangeAt(0).intersectsNode(container)
            if (!text && !state?.selectedImage) return
            event.preventDefault()
            event.stopPropagation()
            if (event.type === 'cut' && !optionsRef.current?.readOnly) editor.clipboard.cut({ type: 'normal', copyInfo: null })
            else editor.clipboard.copy({ type: 'normal', copyInfo: null })
        }
        document.addEventListener('paste', onPaste, true)
        document.addEventListener('copy', onCopy, true)
        document.addEventListener('cut', onCopy, true)
        return () => {
            document.removeEventListener('paste', onPaste, true)
            document.removeEventListener('copy', onCopy, true)
            document.removeEventListener('cut', onCopy, true)
        }
    }, [editor])

    // What the reader does to the document in the page itself (typing, pasting, dropping, the editor's own tools)
    // makes the document shown one that is not kept when a tab switch replaces it. Moving the caret or the page does not.
    useEffect(() => {
        if (!editor) return
        const container = editor.container as HTMLElement
        const edited = () => markEdited(shownRef)
        const onKey = (event: KeyboardEvent) => {
            if (MOVING_KEYS.has(event.key) && !event.altKey) return
            if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'c') return
            if (optionsRef.current?.readOnly) return
            markEdited(shownRef)
        }
        // The tools Muya puts beside the document (the paragraph menu, table, picture and link tools, the pickers).
        const onPointer = (event: Event) => {
            if (event.target instanceof Element && event.target.closest('.ag-float-container, .ag-front-icon')) markEdited(shownRef)
        }
        const kinds = ['beforeinput', 'input', 'compositionstart', 'paste', 'drop', 'cut', 'dragstart']
        for (const kind of kinds) container.addEventListener(kind, edited, true)
        document.addEventListener('keydown', onKey, true)
        document.addEventListener('pointerdown', onPointer, true)
        return () => {
            for (const kind of kinds) container.removeEventListener(kind, edited, true)
            document.removeEventListener('keydown', onKey, true)
            document.removeEventListener('pointerdown', onPointer, true)
        }
    }, [editor])

    // Reading mode has no caret, so Muya reports no selection change, and the host kept Copy disabled whatever
    // the reader selected. Here the page says whether text in the document is selected; the host enables the
    // copy commands by it (EditorViewModel.OnReadingSelectionChange).
    useEffect(() => {
        if (!editor || !props.options?.readOnly) return
        let last: boolean | null = null
        let frame = 0
        const report = () => {
            frame = 0
            const selection = window.getSelection()
            const container = editor.container as HTMLElement
            const selected = !!selection && !selection.isCollapsed && selection.rangeCount > 0 &&
                selection.getRangeAt(0).intersectsNode(container) && selection.toString().trim() !== ''
            if (selected === last) return
            last = selected
            transport.postMessage('ReadingSelectionChange', { selected })
        }
        const onChange = () => { if (!frame) frame = requestAnimationFrame(report) }
        // The browser's own Ctrl+A would select the whole page, the tools around the document included.
        const onKey = (event: KeyboardEvent) => {
            if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'a') {
                event.preventDefault()
                selectDocument(editor.container)
            }
        }
        document.addEventListener('selectionchange', onChange)
        document.addEventListener('keydown', onKey, true)
        report()
        return () => {
            document.removeEventListener('selectionchange', onChange)
            document.removeEventListener('keydown', onKey, true)
            if (frame) cancelAnimationFrame(frame)
        }
    }, [editor, props.options?.readOnly])

    useEffect(() => {
        if (props.options?.fontFamily) {
            document.body.style.setProperty('--editorFontFamily', props.options.fontFamily)
        } else {
            document.body.style.removeProperty('--editorFontFamily')
        }
    }, [props.options?.fontFamily])

    // Content is applied after every font/direction/padding/CSS-variable effect above so that the render's
    // selection placement (in the frame after it) forces exactly one layout; style changes after the render
    // would make the following focus() force a second full layout of the document.
    useEffect(() => {
        cursorRef.current = props.cursor
    }, [props.cursor])

    // Puts the page back where the document was left and keeps it there while the layout settles. Shared by
    // both ways a document arrives: rebuilt from its text, or restored from the one kept in memory.
    // The hold of the load before this one. Switching tabs quickly starts a new load while the previous
    // hold is still putting the page back to its own offset: two holds then pull the page to two places
    // every frame until the older one's budget runs out. It is ended here before the next one begins.
    const holdStopRef = useRef<() => void>(() => { })
    // `to` is an offset, or where to be found anew each time: a document drawn a part at a time grows above the window
    // as it is drawn, and the block that was at the top of the window moves down with it (the offset that puts it back
    // changes; the browser's scroll anchoring keeps it in view meanwhile).
    const settleScroll = useCallback((to: number | (() => number | null), keepScroll: boolean) => {
        holdStopRef.current()
        holdStopRef.current = () => { }
        let last = typeof to === 'function' ? (to() ?? 0) : to
        const target = () => typeof to === 'function' ? (last = to() ?? last) : to
        const scrollTop = last
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
            const startedAt = { innerHeight: window.innerHeight, scrollHeight: document.documentElement.scrollHeight }
            // A viewport with no height is a page that is not in a window yet: the host navigates the web
            // view to this page while putting it back into a rebuilt window, and the document is laid out
            // at no width, absurdly tall, before the window arrives. An offset applied then lands somewhere
            // near the start once the real layout comes, and the browser keeps that content in view — the
            // page was at the top after leaving the settings, every time. Such frames do not count.
            const tall = () => window.innerHeight > 0 && document.documentElement.scrollHeight >= target() + window.innerHeight
            // The knock arrives as a scroll event, so answer it there as well as on the next frame:
            // during the layout a frame can be a hundred milliseconds long, and that is a hundred
            // milliseconds of looking at the top of the document. Correcting puts scrollY back where it
            // belongs, so the event this fires in turn finds nothing to do.
            const putBack = () => { if (!done && !yielded && Math.abs(window.scrollY - target()) > 2) window.scrollTo(window.scrollX, target()) }
            const stop = (why: string) => {
                done = true; window.removeEventListener('scroll', putBack); for (const e of GIVE_WAY) window.removeEventListener(e, giveWay)
                // What the hold saw, for the host's log: the only way to tell from a machine one cannot watch.
                transport.postMessage('ScrollSettled', { asked: Math.round(target()), at: Math.round(window.scrollY), why, framesLeft: frames, capLeft: cap, innerHeight: window.innerHeight, scrollHeight: document.documentElement.scrollHeight, startedAt })
            }
            const hold = () => {
                if (done) return
                // Dragging the scrollbar raises no wheel event, so watching the wheel alone left the hold
                // pulling the page back from under the pointer — the reader drags, it yanks.
                if (yielded) return stop('reader moved')
                if (cap-- <= 0) return stop('gave up')
                if (tall() && frames-- <= 0) return stop('held')
                putBack()
                requestAnimationFrame(hold)
            }
            window.addEventListener('scroll', putBack, { passive: true })
            requestAnimationFrame(hold)
            holdStopRef.current = () => { if (!done) stop('replaced') }
        }
        setTimeout(() => {
            if (!yielded) {
                window.scrollTo(window.scrollX, target())
                if (!keepScroll) scrollToCursorIfInvisible()
            }
            // The hold above decides for itself when it is over; ending it here ended it after six frames,
            // whatever its budget said. The reader's own movement still ends everything at once.
            if (!keepScroll) giveWay()
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            search(searchArgRef.current)
        }, 100);
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

    // Everything a document's drawing depends on besides its text and its caret: the settings (fonts, reading mode,
    // direction, diagrams, the CSS...), the folder pictures are found in, the theme and the pictures' freshness.
    const renderKey = () => JSON.stringify([props.options ?? null, window.basePath ?? null, window.actualTheme ?? null, (editor as any)?.imageEpoch ?? 0])

    // A long document a tab switch replaces is kept, built - its blocks, its caret, its elements - when it is drawn
    // exactly as a new build of its text would draw it, and only then: nothing has been done to it but moving the
    // caret and the page (no typing, no edit from the host, nothing Muya reports as changed), its last report is its
    // state (no report on its way, the caret where that report put it), nothing is selected or highlighted in it,
    // no table tool bar is drawn, no picture is still loading and it has no diagram, whose drawing comes later and
    // would be lost while it is out.
    const keepShown = () => {
        const shown = shownRef.current
        shownRef.current = null
        const muya = editor as any
        // A document still being drawn a part at a time is kept as far as it got, and drawn on when it is back
        // (Muya.takeDocument, showDocument): switching between long documents should not wait for one to finish.
        if (!muya || !shown || !shown.pristine || !shown.reported) return
        if (muya.loadCursorPending || muya.contentChangeDue) return
        const { contentState } = muya
        const root = contentState.stateRender.container as HTMLElement | null
        if (!root || importedRef.current?.source !== shown.text || markdownRef.current !== shown.text) return
        if (contentState.blocks.length !== shown.blockCount) return
        const cursorAt = blockCaretKey(contentState.cursor)
        if (cursorAt !== shown.reported.at) return
        if (contentState.searchMatches?.matches?.length || contentState.selectedImage || contentState.selectedTableCells || contentState.selectedBlock) return
        // A table's tool bar: a new build draws one on every table while the caret is in a cell (renderContainerBlock),
        // the editor only on the table the caret went into - and one is left on the others when the caret came with
        // the load. A document with one in it is not drawn as a new build would draw it.
        if (root.querySelector('.ag-image-loading, .ag-tool-table')) return
        keptRef.current.put({ ...shown, cursorAt, kept: muya.takeDocument() })
    }

    // The kept document with this text, when showing it is showing what a new build would: the same settings, and a
    // caret that the build would put where the kept one is. Either the very caret this document was built with and has
    // not left, or the caret it last reported (in lines of Muya's export) when the build would put that caret in the
    // same place: the text and the export agree line for line up to the line after the caret, and the caret sits where
    // nothing moves it (addCursorToMarkdown moves one off a blank line, a fence, a table edge, a list or heading marker).
    const takeKept = (text: string, cursor: any) => {
        const kept = keptRef.current.take(text)
        if (!kept || kept.renderKey !== renderKey()) return null
        if (!(editor as any).canShowDocument(kept.kept) || kept.kept.root.querySelector('.ag-image-loading')) return null
        const given = caretKey(cursor)
        if (given === kept.loadCaret.given && kept.cursorAt === kept.loadCaret.at) return kept
        if (!kept.reported || given !== kept.reported.caret || !isForward(cursor)) return null
        if (kept.text !== kept.imported.normalized && sameLines(kept.text, kept.imported.normalized) <= Math.max(cursor.anchor.line, cursor.focus.line) + 1) return null
        const marked = markedAt(text, cursor, CURSOR_ANCHOR_DNA, CURSOR_FOCUS_DNA)
        if (marked === null) return null
        try {
            if ((editor as any).contentState.addCursorToMarkdown(text, cursor).markdown !== marked) return null
        } catch {
            return null
        }
        return kept
    }

    useEffect(() => {
        if (!editor) return
        const local = props.localChangeRef?.current
        if (props.localChangeRef) props.localChangeRef.current = false
        const scrollToChange = !!props.scrollToChangeRef?.current
        if (props.scrollToChangeRef) props.scrollToChangeRef.current = false
        // A load whose caret is still to be placed reports itself applied once it is (see below), not before.
        if (importedRef.current && markdownRef.current === props.markdown) { if (!editor.loadCursorPending) props.onContentApplied?.(); return }
        // After an actual edit the host may hand the exported text back. Reuse the
        // existing model when it already matches, avoiding another load and caret scroll.
        if (editor.getMarkdown() === props.markdown) {
            markdownRef.current = props.markdown
            importedRef.current = { source: props.markdown, normalized: props.markdown }
            shownRef.current = null
            showFirstEditWarning(false)
            if (!editor.loadCursorPending) props.onContentApplied?.()
            return
        }
        // A whole new document: the one shown may be kept for a switch back (keepShown), and the one coming may be one
        // kept (takeKept).
        if (!local) keepShown()
        else shownRef.current = null
        const kept = local ? null : takeKept(props.markdown, cursorRef.current)
        markdownRef.current = props.markdown
        markLongDocument(props.markdown)
        // An automation edit keeps the blocks it did not change, with the reader's cursor and scroll position; the
        // browser keeps what is on screen in place when blocks above it change. Otherwise, a whole new document.
        const blocksBefore = local ? editor.contentState.blocks : null
        const replacedLocally = !!local && editor.replaceMarkdownLocally(props.markdown)
        // A whole new document is built now; its caret is placed, the page put back where it was left and the load
        // reported applied in the next frame (Muya.setMarkdown says why). Where to put it back is taken now: the
        // page's own scroll events in between (the old document gone, the new one not laid out) would overwrite it.
        const scrollTop = props.scrollTopRef.current
        const keepScroll = !!props.scrollFromHostRef?.current
        const anchor = props.scrollAnchorRef?.current ?? null
        if (props.scrollAnchorRef) props.scrollAnchorRef.current = null
        // A long document is drawn a part at a time (docs/todo.md, "方案 A"): first the block that was at the top of the
        // window when it was left (its scroll anchor), or the caret's; the rest while the page is idle. Not where only an
        // offset says where the page was (no anchor yet): an offset means something only with the whole document laid out.
        let anchorLeaf: string | null = null
        const progressive = props.markdown.length >= PREVIEW_FROM_CHARS && !(window as any).__typedownProgressiveOff ? {
            pick: (blocks: any[], cursorIndex: number) => {
                const found = anchor ? findAnchorBlock(blocks, anchor) : null
                if (found) { anchorLeaf = found.leafKey; return found.index }
                if (keepScroll && scrollTop > 0) return -1
                return cursorIndex >= 0 ? cursorIndex : 0
            },
            done: () => { (window as any).__typedownProgressiveDone = performance.now() },
        } : null
        const loaded = () => {
            // The block that was at the top of the window back at the height it was; drawn a part at a time, followed as
            // the document grows above it. The offset alone where it is not found (the document changed meanwhile).
            const leaf = anchorLeaf
            const at = anchor
            if (keepScroll && at && leaf) settleScroll(() => {
                const element = document.getElementById(leaf)
                return element ? Math.max(0, window.scrollY + element.getBoundingClientRect().top - at.top) : null
            }, keepScroll)
            else settleScroll(keepScroll ? scrollForAnchor(anchor) ?? scrollTop : scrollTop, keepScroll)
            props.onContentApplied?.()
        }
        // A kept document goes back in as it was left, and everything after it happens as after a new build: the caret
        // and the page in the next frame, then the report, the handshake and the outline.
        if (kept) {
            editor.showDocument(kept.kept, loaded)
            ;(window as any).__typedownLastApply = 'kept'
            importedRef.current = kept.imported
            showFirstEditWarning(kept.warn)
            shownRef.current = {
                text: kept.text, imported: kept.imported, warn: kept.warn, renderKey: kept.renderKey, loadCaret: kept.loadCaret,
                blockCount: kept.blockCount, pristine: true, reported: null,
            }
            return
        }
        if (!replacedLocally) editor.setMarkdown(props.markdown, cursorRef.current, true, blocksBefore ? null : loaded, blocksBefore ? null : progressive)
        ;(window as any).__typedownLastProgressive = !blocksBefore && !replacedLocally && (editor as any).renderPending
        if (blocksBefore && props.options?.highlightAutomationChanges !== false)
            highlightExternalChange(editor.changedBlockKeys(blocksBefore).map((key: string) => document.getElementById(key)))
        ;(window as any).__typedownLastApply = replacedLocally ? 'local' : 'whole'
        importedRef.current = { source: props.markdown, normalized: editor.getMarkdown() }
        // Text the first visual edit would change in a way that loses something (today: some raw HTML) gets a notice
        // that does not block editing; source mode keeps it exactly.
        const warn = classifyNormalization(props.markdown, importedRef.current.normalized).pendingNormalization === 'unsafe'
        showFirstEditWarning(warn)
        // What a later switch away needs to know to keep this document (keepShown).
        const { contentState } = editor as any
        shownRef.current = !blocksBefore && props.markdown.length >= KEEP_FROM_CHARS && !hasDiagram(contentState.blocks) ? {
            text: props.markdown, imported: importedRef.current, warn, renderKey: renderKey(),
            loadCaret: { given: caretKey(cursorRef.current), at: blockCaretKey(contentState.cursor) },
            blockCount: contentState.blocks.length, pristine: true, reported: null,
        } : null
        // reveal: "change": the first changed block into view unless the reader can already see it. Measured in page
        // coordinates against where the page is (a local edit) or is about to be put back (a whole new document).
        let changeY: number | null = null
        if (blocksBefore && scrollToChange) {
            const key = editor.changeAnchorKey(blocksBefore)
            const element = key ? document.getElementById(key) : null
            if (element) {
                const rect = element.getBoundingClientRect()
                changeY = scrollTargetForChange(rect.top + window.scrollY, rect.bottom + window.scrollY,
                    replacedLocally ? window.scrollY : props.scrollTopRef.current)
            }
        }
        if (replacedLocally) {
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            if (changeY !== null) window.scrollTo(window.scrollX, changeY)
        }
        // An automation edit that had to be applied as a whole new document: at once, as before.
        else if (blocksBefore) {
            if (changeY !== null) settleScroll(changeY, true)
            else settleScroll(scrollTop, keepScroll)
        }
        // A plain load: `loaded` above runs once its caret is placed.
        else return
        props.onContentApplied?.()
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, props.markdown, props.contentVersion, props.scrollTopRef, settleScroll, markLongDocument, showFirstEditWarning])

    useEffect(() => {
        try {
            editor?.focus()
        } catch (err) {
            console.log(err)
        }
    }, [editor])

    // The host hands the page the keyboard (a new tab, the find bar closed, a menu or a dialog gone) while nothing in it
    // has the focus - the search took it away, or the document was replaced under it: the keys went to the page's body
    // and nowhere. Then the editor takes it, with its caret where it was.
    useEffect(() => {
        if (!editor) return
        const onFocus = () => restoreEditorFocus(editor)
        window.addEventListener('focus', onFocus)
        return () => window.removeEventListener('focus', onFocus)
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
            tabSize: Number(props.options?.tabSize) || 4,
            fontSize: props.options?.fontSize,
            lineHeight: props.options?.lineHeight,
            fontFamily: props.options?.fontFamily ? `${props.options.fontFamily}, "Open Sans", "Segoe UI", sans-serif` : undefined
        }}>
            {/* A wrapper that always exists: Muya replaces #editor's node, so React must never insert next to it -
                inserting the notice before #editor threw and took the whole page down. */}
            <div className="td-first-edit-warning-slot" ref={warningSlotRef} />
            <div id="editor" />
        </div>
    )

}


/**
 * The page has the keyboard and nothing in it has the focus (the body has it): the editor takes it. A selection the
 * reader made in the text is kept - only the focus goes back to the text - and reading mode, where nothing is typed,
 * is left alone: Muya's focus() puts the caret back and so dropped a selection made to copy from.
 */
function restoreEditorFocus(editor: any) {
    try {
        if (document.activeElement && document.activeElement !== document.body) return
        const container = editor?.container as HTMLElement | undefined
        if (!container || container.getAttribute('contenteditable') !== 'true') return
        const selection = document.getSelection()
        if (selection && selection.rangeCount > 0 && !selection.isCollapsed && container.contains(selection.anchorNode)) {
            container.focus({ preventScroll: true })
            return
        }
        editor.focus()
    } catch (err) {
        console.log(err)
    }
}

export default MuyaEditor
