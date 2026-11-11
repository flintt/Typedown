import React, { useCallback, useEffect, useRef, useState } from "react";
import transport from "services/transport";
import { setScroller } from "services/scrollbar";
import { matchString } from 'services/common'
import { UnControlled as CodeMirror } from 'react-codemirror2';
import 'codemirror/lib/codemirror.css';
import { getTOC, sourceHeadingLine } from "services/common";
import { minimalChange } from "services/minimalChange";
import { EXTERNAL_CHANGE_CLASS, EXTERNAL_CHANGE_MS, scrollTargetForChange } from "services/externalChange";
import { setVimState, vimCommand } from "services/vim";
import { wordCount as countWords } from 'components/Muya/lib/utils';
import { FIND_EDIT_REFRESH_MS, findResultsOpen, noteFindChange, setFindProvider } from 'services/findResults';
import { FENCE, isTableRow, lineMarksLength, previewOf } from 'services/findPreview';
import { MarkdownBlocksView } from './markdownBlocks';
import { keyBinding } from 'components/Muya/lib/utils/keyBinding';
import { registerReviewView, reviewActive } from 'services/reviewSession';
import type { Revision } from 'services/review';
require('codemirror/mode/markdown/markdown');
require('codemirror/addon/scroll/scrollpastend');
require('codemirror/keymap/vim');
require('codemirror/addon/dialog/dialog.css');

// Vim keys (services/vim): :w saves, :q closes the tab, :wq and :x do both - through the application, which asks
// before closing an unsaved document as it always does.
const CodeMirrorLib = require('codemirror');
CodeMirrorLib.commands.save = () => vimCommand('write');
CodeMirrorLib.Vim.defineEx('quit', 'q', () => vimCommand('quit'));
CodeMirrorLib.Vim.defineEx('wq', 'wq', () => vimCommand('writeQuit'));
CodeMirrorLib.Vim.defineEx('xit', 'x', () => vimCommand('writeQuit'));

// A key binding from the host (Muya/lib/utils/keyBinding) as CodeMirror names a key: "Shift-Ctrl-Alt-Z".
const codeMirrorKey = (binding: any) => {
    const name = binding && CodeMirrorLib.keyNames[binding.keyCode]
    if (!name) return null
    return (binding.shift ? 'Shift-' : '') + (binding.ctrl ? 'Ctrl-' : '') + (binding.alt ? 'Alt-' : '') + name
}

// CodeMirror's own history is not the document's: the undo and redo shortcuts as set ask the host for its undo and
// redo, and Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z given away from them do nothing (not CodeMirror's history either).
const historyKeys = (options: any) => {
    const ask = (redo: boolean) => () => transport.postMessage('HistoryRequested', { redo })
    const nothing = () => {}
    const keys: Record<string, () => void> = { 'Ctrl-Z': nothing, 'Ctrl-Y': nothing, 'Shift-Ctrl-Z': nothing }
    const undo = codeMirrorKey(keyBinding(options, 'undo'))
    const redo = codeMirrorKey(keyBinding(options, 'redo'))
    if (redo === 'Ctrl-Y') keys['Shift-Ctrl-Z'] = ask(true)
    if (redo) keys[redo] = ask(true)
    if (undo) keys[undo] = ask(false)
    return keys
}

const VIM_LABELS: Record<string, string> = { normal: 'NORMAL', insert: 'INSERT', replace: 'REPLACE', visual: 'VISUAL', linewise: 'V-LINE', blockwise: 'V-BLOCK' };

interface ICodeMirrorEditor {
    markdown: string
    cursor: any
    /** Incremented by the container whenever the host replaces the content; forces the effect below to re-check. */
    contentVersion?: number
    /** The host's load of the document shown: a new one is another document (its own undo history). */
    loadId?: number
    options: any
    searchOpen: number
    searchArg: { value: string, opt: any } | undefined
    scrollTopRef: React.MutableRefObject<number>
    scrollFromHostRef?: React.MutableRefObject<boolean>
    /** Set for the text an undo or a redo put back: its caret into view by the least scroll, only if out of view. */
    revealCaretRef?: React.MutableRefObject<boolean>
    /** True when the new markdown is an edit of the text shown (automation): only the changed part is replaced. */
    localChangeRef?: React.MutableRefObject<boolean>
    scrollToChangeRef?: React.MutableRefObject<boolean>
    onMarkdownChange: (markdown: string) => void
    /** Fired once host content has been pushed into the editor (see the FileLoaded handshake in Editor). */
    onContentApplied?: () => void
    onCursorChange: (cursor: any) => void
    onStateChange: (state: any) => void
    onSearchArgChange: (arg: { value: string, opt: any } | undefined) => void
}

const STANDAR_Y = 320

const CodeMirrorEditor: React.FC<ICodeMirrorEditor> = (props) => {
    const { onStateChange } = props
    const [editor, setEditor] = useState<any>();
    const [marginTop, setMarginTop] = useState(0);
    const matchsRef = useRef<any[]>([]);
    // The load whose text the undo history belongs to.
    const historyLoadIdRef = useRef<number | undefined>(undefined);
    const matchIndexRef = useRef(0);
    const markdownRef = useRef('');
    const searchArgRef = useRef<any>();
    const cursorRef = useRef<any>();
    // Vim is source mode's: not in a plain text file laid out as prose, which is visual mode's view of it (with Vim,
    // the reader saw source mode there and Ctrl+/ went to Vim).
    const vim = !!props.options?.vimMode && !props.options?.readOnly && !props.options?.plainTextProse
    const [vimLabel, setVimLabel] = useState('NORMAL');
    useEffect(() => {
        // The mode Vim is in, for the badge and for the host, which must leave Vim's Ctrl keys alone in normal and
        // visual mode (services/vim).
        // The key map set here too: the editor's options are read once, as it is made, and a plain text file switched
        // between its prose and source views kept the one it was made with.
        if (editor) editor.setOption('keyMap', vim ? 'vim' : 'default')
        if (!editor || !vim) {
            setVimState('off')
            return
        }
        setVimState('normal')
        setVimLabel('NORMAL')
        const onMode = (e: { mode: string, subMode?: string }) => {
            setVimState(e.mode === 'visual' ? 'visual' : e.mode as any)
            setVimLabel(VIM_LABELS[e.mode === 'visual' && e.subMode ? e.subMode : e.mode] ?? e.mode.toUpperCase())
        }
        editor.on('vim-mode-change', onMode)
        return () => {
            editor.off('vim-mode-change', onMode)
            // Leaving source mode (a Markdown tab after a plain text file, the mode switched): Vim is off, or the host's
            // status bar kept "INSERT" after it (a reader's report).
            setVimState('off')
        }
    }, [editor, vim])

    // The editor scrolls itself (it is the window's height): the page does not. It drew every line of the document
    // when the page scrolled - its viewport was the whole text - and a 40,000-line file took 20 s to open (a reader:
    // a long .txt was slow). Now it draws the lines on screen.
    const relativeScroll = useCallback((delta: number) => {
        if (!editor) return
        editor.scrollTo(null, editor.getScrollInfo().top + delta)
    }, [editor])

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

    const setMatchSelection = useCallback((match) => {
        if (match) {
            const head = editor.posFromIndex(match.index);
            const anchor = editor.posFromIndex(match.index + match.match.length);
            editor.setSelection(anchor, head);
        }
    }, [editor])

    // The marks of the matches drawn again: the one chosen as the current match, the others as the rest.
    const remark = useCallback(() => {
        matchsRef.current.forEach((item, i) => {
            item.marker.clear()
            item.marker = editor.markText(
                editor.posFromIndex(item.index),
                editor.posFromIndex(item.index + item.match.length),
                { className: i == matchIndexRef.current ? 'ag-highlight' : 'ag-selection' }
            )
        })
    }, [editor])

    // The last search: its matches are found again with it after an edit while the results list is open.
    const lastSearchRef = useRef<{ value: string, opt: any } | null>(null)

    const search = useCallback(({ value, opt }) => {
        const lastMatch = matchsRef.current[matchIndexRef.current];
        matchsRef.current.forEach(({ marker }) => marker.clear())
        if ((value == null || value.length == 0)) {
            if (lastMatch?.index >= 0) {
                const head = editor.posFromIndex(lastMatch.index);
                editor.setSelection(head, head);
            }
            // No matches left (their marks are gone): the bar counts none, the results list is empty.
            matchsRef.current = []
            lastSearchRef.current = null
            noteFindChange()
            return;
        }
        lastSearchRef.current = { value, opt }
        let startIndex = 0
        if (opt.selection) {
            const { anchor, head } = opt.selection;
            startIndex = Math.min(editor.indexFromPos(anchor), editor.indexFromPos(head))
        }
        const matchs = matchString(editor.getValue(), value, opt)
        for (matchIndexRef.current = 0;
            matchIndexRef.current < matchs.length && matchs[matchIndexRef.current].index < startIndex;
            matchIndexRef.current++
        );
        setMatchSelection(matchs[matchIndexRef.current])
        matchsRef.current = matchs.map(({ index, match, subMatches }, i) => ({
            marker: editor.markText(
                editor.posFromIndex(index),
                editor.posFromIndex(index + match.length),
                { className: i == matchIndexRef.current ? 'ag-highlight' : 'ag-selection' }
            ),
            index,
            match,
            subMatches
        }))
        noteFindChange()
    }, [editor, setMatchSelection])

    const find = useCallback(({ action }) => {
        if (action == 'next') {
            matchIndexRef.current++;
        } else {
            matchIndexRef.current--;
        }
        if (matchIndexRef.current < 0) {
            matchIndexRef.current = matchsRef.current.length - 1;
        }
        if (matchIndexRef.current >= matchsRef.current.length) {
            matchIndexRef.current = 0;
        }
        setMatchSelection(matchsRef.current[matchIndexRef.current])
        remark()
        noteFindChange()
    }, [setMatchSelection, remark])

    // Replace works on the matches of the text as it is now, found again from the last search: positions kept from
    // that search went stale at any edit - a second Replace All cut "X X END" to "X X", typing before a match replaced
    // the wrong place (the audit's E02). All of them in one change, not the whole text set once per match (P01: a
    // thousand matches took 20 s).
    const replace = useCallback(({ value, opt }) => {
        const last = lastSearchRef.current
        if (!editor || !last?.value) return
        const text = editor.getValue()
        const matchs = matchString(text, last.value, last.opt)
        if (!matchs.length) return
        let chosen = matchs
        if (opt.isSingle) {
            // The current match: the one at the selection (where the last find put it), else the next after the caret.
            const from = editor.indexFromPos(editor.getCursor('from'))
            const at = matchs.find(m => m.index >= from) ?? matchs[0]
            chosen = [at]
        }
        // A regular expression's groups put in, each match its own ($0 the match, $1.. its groups; \$ a dollar), as the
        // visual editor does - they went in as they were (the audit's E06).
        const regexp = !!last.opt?.searchIsRegexp
        const expand = (m: any) => !regexp ? value : value.replace(/(?<!\\)\$(\d)/g, (whole: string, d: string) => {
            const n = parseInt(d)
            return n === 0 ? m.match : (n <= (m.subMatches?.length ?? 0) ? (m.subMatches[n - 1] ?? '') : whole)
        })
        let out = '', pos = 0, lastEnd = 0
        for (const m of chosen) {
            out += text.substring(pos, m.index) + expand(m)
            lastEnd = out.length
            pos = m.index + m.match.length
        }
        out += text.substring(pos)
        editor.replaceRange(out, editor.posFromIndex(0), editor.posFromIndex(text.length))
        // On from just after the (last) replaced text, the marks and the count found again.
        const caret = editor.posFromIndex(lastEnd)
        editor.setSelection(caret, caret)
        search({ value: last.value, opt: { ...last.opt, selection: { anchor: caret, head: caret } } })
    }, [editor, search])

    useEffect(() => transport.addListener<{ text: string }>('Paste', ({ text }) => {
        editor?.replaceSelection(text)
    }), [editor]);

    // Text typed while this document was on its way (services/inputHold.ts), in at the caret.
    useEffect(() => transport.addListener<{ text: string, done: () => void }>('InsertTyped', ({ text, done }) => {
        if (editor && !props.options?.readOnly) editor.replaceSelection(text.replace(/\r\n?/g, '\n'))
        done()
    }), [editor, props.options?.readOnly]);

    // The keyboard to the editor, unless the page is holding keys for a document on its way (services/inputHold.ts).
    useEffect(() => transport.addListener('TakeKeyboard', () => {
        if (document.activeElement?.hasAttribute('data-input-hold')) return
        if (editor && !editor.hasFocus()) editor.focus()
    }), [editor]);

    // A heading clicked in the outline: the caret to the start of its line, the line near the top of the window. Only the
    // visual editor listened for it, and in source mode the outline showed but a click did nothing (the user, 2026-11-05).
    useEffect(() => transport.addListener<{ slug: string }>('ScrollTo', ({ slug }) => {
        const line = sourceHeadingLine(slug)
        if (!editor || line === null || line >= editor.lineCount()) return
        editor.setCursor({ line, ch: 0 })
        const top = editor.charCoords({ line, ch: 0 }, 'local').top
        editor.scrollTo(null, Math.max(0, top - 40))
        if (!editor.hasFocus()) editor.focus()
    }), [editor]);

    useEffect(() => transport.addListener('DeleteSelection', () => {
        editor?.replaceSelection('')
    }), [editor]);

    useEffect(() => transport.addListener('Copy', () => {
        document.execCommand('copy')
    }), []);

    useEffect(() => transport.addListener('Cut', () => {
        document.execCommand('cut')
    }), []);

    useEffect(() => transport.addListener('SelectAll', () => {
        if (!editor) return
        // Settings > Editor > Select all in steps: the caret's paragraph (its lines between blank lines) first, then
        // the document.
        if (props.options?.selectAllInSteps) {
            const doc = editor.getDoc()
            const from = doc.getCursor('from'), to = doc.getCursor('to')
            const blank = (line: number) => !doc.getLine(line).trim()
            let first = from.line, last = to.line
            while (first > 0 && !blank(first - 1)) first--
            while (last < doc.lineCount() - 1 && !blank(last + 1)) last++
            const start = { line: first, ch: 0 }, end = { line: last, ch: doc.getLine(last).length }
            const whole = first === 0 && last === doc.lineCount() - 1
            const already = from.line === start.line && from.ch === 0 && to.line === end.line && to.ch === end.ch
            if (!whole && !already && !(blank(from.line) && from.line === to.line)) {
                doc.setSelection(start, end)
                return
            }
        }
        editor.execCommand('selectAll')
    }), [editor, props.options?.selectAllInSteps]);

    useEffect(() => {
        searchArgRef.current = props.searchArg;
    }, [props.searchArg])

    useEffect(() => {
        markdownRef.current = ''
    }, [editor])

    // Review mode's marks in source mode (services/reviewSession.ts): inserted text marked, deleted text a widget where
    // it was (not in the text), a whole inserted line with the bar, deleted lines a strip above the line after them.
    useEffect(() => {
        if (!editor) return
        let marks: { clear: () => void }[] = []
        let lines: any[] = []
        const clear = () => {
            for (const m of marks) m.clear()
            for (const l of lines) editor.removeLineClass(l, 'wrap', 'cm-review-line-ins')
            marks = []
            lines = []
        }
        const deleted = (text: string, r: Revision, block: boolean) => {
            const span = document.createElement(block ? 'div' : 'span')
            span.className = block ? 'cm-review-del-lines' : 'cm-review-del'
            span.textContent = block ? text.replace(/\n+$/, '') : text.replace(/\n/g, ' ¶ ')
            span.setAttribute('data-rid', r.id)
            if (r.source) span.setAttribute('data-review-source', r.source)
            return span
        }
        return registerReviewView({
            draw(revisions, text) {
                editor.operation(() => {
                    clear()
                    for (const r of revisions) {
                        const from = editor.posFromIndex(r.curStart), to = editor.posFromIndex(r.curEnd)
                        if (r.curEnd > r.curStart) {
                            marks.push(editor.markText(from, to, { className: r.source ? 'cm-review-ins cm-review-ext' : 'cm-review-ins', attributes: { 'data-rid': r.id, ...(r.source ? { 'data-review-source': r.source } : {}) } }))
                            const wholeLines = (r.curStart === 0 || text[r.curStart - 1] === '\n') && (r.curEnd === text.length || text[r.curEnd - 1] === '\n')
                            if (wholeLines) for (let line = from.line; line < (to.ch === 0 ? to.line : to.line + 1); line++) lines.push(editor.addLineClass(line, 'wrap', 'cm-review-line-ins'))
                        }
                        if (r.oldText) {
                            const block = r.oldText.includes('\n') && from.ch === 0
                            if (block) marks.push(editor.addLineWidget(Math.min(from.line, editor.lineCount() - 1), deleted(r.oldText, r, true), { above: true }))
                            else marks.push(editor.setBookmark(from, { widget: deleted(r.oldText, r, false), insertLeft: true }))
                        }
                    }
                })
            },
            clear() {
                editor.operation(clear)
            },
            caretOffset() {
                return editor.indexFromPos(editor.getCursor())
            },
            setCaret(offset) {
                editor.setCursor(editor.posFromIndex(offset))
                editor.scrollIntoView(null, editor.getScrollInfo().clientHeight / 3)
                editor.focus()
            },
            rectOf(id) {
                const element = editor.getWrapperElement().querySelector(`[data-rid="${id}"]`)
                return element ? element.getBoundingClientRect() : null
            },
            revisionOf(element) {
                const marked = element.closest('.CodeMirror [data-rid]')
                return marked ? marked.getAttribute('data-rid') : null
            },
        })
    }, [editor])

    useEffect(() => {
        if (!editor) return
        const local = props.localChangeRef?.current
        if (props.localChangeRef) props.localChangeRef.current = false
        const scrollToChange = !!props.scrollToChangeRef?.current
        if (props.scrollToChangeRef) props.scrollToChangeRef.current = false
        let replaced = false
        if (local && markdownRef.current != props.markdown) {
            // Replacing only what changed keeps the cursor, the selection and the scroll position where the reader
            // left them; CodeMirror maps them over the change.
            const change = minimalChange(editor.getValue(), props.markdown)
            markdownRef.current = props.markdown
            if (change) {
                editor.replaceRange(change.text, editor.posFromIndex(change.from), editor.posFromIndex(change.to), '+automation')
                if (change.text && props.options?.highlightAutomationChanges !== false && !reviewActive()) {
                    const mark = editor.markText(editor.posFromIndex(change.from), editor.posFromIndex(change.from + change.text.length), { className: EXTERNAL_CHANGE_CLASS })
                    setTimeout(() => mark.clear(), EXTERNAL_CHANGE_MS)
                }
                // reveal: "change": the start of the change into view unless the reader can already see it.
                if (scrollToChange) {
                    const start = editor.charCoords(editor.posFromIndex(change.from), 'local')
                    const end = editor.charCoords(editor.posFromIndex(change.from + change.text.length), 'local')
                    const y = scrollTargetForChange(start.top, end.bottom, editor.getScrollInfo().top)
                    if (y !== null) editor.scrollTo(null, y)
                }
            }
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
        } else if (markdownRef.current != props.markdown) {
            replaced = true
            markdownRef.current = props.markdown
            // The caret that came with this text, not the one held: that effect (below) runs after this one, and held the
            // document before - a .txt opened after a .md (the editor already up for it, shown as plain text a moment
            // before its text came) took the .md's caret, a line and column of another file (a reader: the .txt's caret
            // drifted at every switch).
            // None given (a file never opened before): at its start, not at the caret of the one before.
            const given = props.cursor
            const anchor = given?.anchor
            const head = given?.focus ?? given?.head
            cursorRef.current = anchor && head ? { anchor, head } : undefined
            editor.setValue(markdownRef.current)
            // Another document: an undo history of its own. setValue is itself an undoable change, and Vim's u (the
            // editor's own history, not the host's) turned the document shown into the one before (the audit's E01).
            if (props.loadId !== historyLoadIdRef.current) {
                editor.clearHistory()
                historyLoadIdRef.current = props.loadId
            }
            if (anchor && head)
                editor.setSelection(anchor, head, { scroll: !props.scrollFromHostRef?.current })
            editor.scrollTo(null, props.scrollTopRef.current)
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            // An undo or a redo: where the reader was (above), the caret then into view by the least scroll if it is out.
            if (props.revealCaretRef?.current) {
                props.revealCaretRef.current = false
                if (head) editor.scrollIntoView(head, 48)
            }
            if (searchArgRef.current?.value != "" && searchArgRef.current?.opt?.selection) {
                const { value, opt } = searchArgRef.current
                const selection = opt.selection.head ? opt.selection : undefined
                search({ value, opt: { ...opt, selection } })
            }
        }
        props.onContentApplied?.()
        // The new text's count and outline once the load has taken it: the change setValue raised came before, while the
        // page still dropped reports as the previous document's - source mode showed the outline before it until an edit.
        if (replaced) handleCodeMirrorState(markdownRef.current, true)
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [editor, props.markdown, props.contentVersion, props.scrollTopRef, search])

    useEffect(() => {
        if (props.searchArg && editor) {
            const { value, opt } = props.searchArg
            const selection = opt.selection.head ? opt.selection : undefined
            search({ value, opt: { ...opt, selection } })
        }
    }, [editor, props.searchArg, search])

    useEffect(() => {
        const { anchor, focus: head } = props.cursor ?? {}
        cursorRef.current = { anchor, head }
    }, [editor, props.cursor])

    const countRef = useRef<{ time: number, value: any, timer?: any, toc?: any[] }>({ time: 0, value: null })
    // The heading the caret is under, as the outline marks it: the last one at or above the caret's line - the first
    // heading always, before (the user, 2026-11-06: the outline marked the first heading wherever the caret was).
    const curOf = useCallback((toc: any[] | undefined) => {
        if (!toc?.length || !editor) return undefined
        const line = editor.getCursor().line
        let cur
        for (const item of toc) {
            const at = sourceHeadingLine(item.slug)
            if (at === null || at > line) break
            cur = item
        }
        return cur
    }, [editor])
    const sentCurRef = useRef<string | undefined>(undefined)

    const handleCodeMirrorState = useCallback((value: string, loaded = false) => {
        // Counted as the visual editor counts (Muya's wordCount): source mode had its own count (every character,
        // words split at spaces only), so a mode switch moved the count with nothing written - and what follows the
        // count, as edits, took the jump for writing. On a long document at most every 300 ms while typing, and once
        // more when it stops, as the visual editor does: counting it is a share of every keystroke.
        const counted = countRef.current
        clearTimeout(counted.timer)
        const now = Date.now()
        // The outline with it: read by the visual editor's lexer now (services/common.ts getTOC), about 70 ms on 300k
        // characters - on a long document only once the typing stops, not every 300 ms of it.
        const long = value.length >= 50000
        // A document just loaded: counted at once, not the one before's count and outline a moment longer.
        if (!long || loaded || !counted.value || now - counted.time >= 300) {
            counted.time = now
            counted.value = countWords(value)
            if (!long || loaded || !counted.toc) counted.toc = getTOC(value).toc
        }
        if (long) {
            counted.timer = setTimeout(() => {
                counted.time = Date.now()
                counted.value = countWords(value)
                counted.toc = getTOC(value).toc
                const cur = curOf(counted.toc)
                sentCurRef.current = cur?.slug
                onStateChange({ wordCount: counted.value, toc: counted.toc, cur })
            }, 300)
        }
        const toc = counted.toc ?? []
        const cur = curOf(toc)
        sentCurRef.current = cur?.slug
        const state = { wordCount: counted.value, toc, cur }
        onStateChange(state)
    }, [onStateChange, curOf])

    // The caret moved into another section (no edit): the outline's mark follows it.
    useEffect(() => {
        if (!editor) return
        const moved = () => {
            const counted = countRef.current
            if (!counted.toc) return
            const cur = curOf(counted.toc)
            if (cur?.slug === sentCurRef.current) return
            sentCurRef.current = cur?.slug
            onStateChange({ wordCount: counted.value, toc: counted.toc, cur })
        }
        editor.on('cursorActivity', moved)
        return () => { editor.off('cursorActivity', moved) }
    }, [editor, curOf, onStateChange])

    const handleCodeMirrorContent = useCallback((cm: any, data: any, value: string) => {
        handleCodeMirrorState(value)
        markdownRef.current = value;
        props.onMarkdownChange(value)
    }, [handleCodeMirrorState, props])

    const handleCodeMirrorSelection = useCallback((cm: any, data: any) => {
        const { anchor, head } = data.ranges[0]
        cursorRef.current = { anchor, head }
        props.onCursorChange({ anchor, focus: head })
        const selectionText = cm.getSelection();
        transport.postMessage('CodeMirrorSelectionChange', { cursor: cursorRef.current, selectionText })
    }, [props])

    useEffect(() => {
        setMarginTop(currentMarginTop => {
            const newMarginTop = { 0: 0, 1: 50, 2: 90 }[props.searchOpen] ?? 0;
            relativeScroll(newMarginTop - currentMarginTop)
            return newMarginTop;
        })
    }, [props.searchOpen, relativeScroll])

    useEffect(() => transport.addListener<{ open: number }>('SearchOpenChange', ({ open }) => {
        if (open == 0) {
            matchsRef.current.forEach(({ marker }) => marker.clear())
            matchsRef.current = []
            lastSearchRef.current = null
            props.onSearchArgChange(undefined)
            noteFindChange()
        }
    }), [editor, props, relativeScroll]);

    // The find bar's results list (services/findResults): a preview of each match from its line of the text, without the
    // line's own marks; an entry chosen there selected, as a step to it would.
    useEffect(() => {
        if (!editor) return
        // The lines of fenced code blocks (fences too), found once for each list: their matches are shown as written.
        let code = new Uint8Array(0)
        return setFindProvider({
            begin: () => {
                const count = editor.lineCount()
                code = new Uint8Array(count)
                let fence: string | null = null
                for (let l = 0; l < count; l++) {
                    const m = FENCE.exec(editor.getLine(l) ?? '')
                    if (fence) {
                        code[l] = 1
                        if (m && m[1][0] === fence[0] && m[1].length >= fence.length) fence = null
                    } else if (m) {
                        code[l] = 1
                        fence = m[1]
                    }
                }
            },
            state: () => {
                const total = matchsRef.current.length
                return { total, index: total ? matchIndexRef.current : -1 }
            },
            preview: (i: number) => {
                const m = matchsRef.current[i]
                const pos = editor.posFromIndex(m.index)
                const line: string = editor.getLine(pos.line) ?? ''
                const start = pos.ch
                const end = Math.min(line.length, start + m.match.length)
                if (code[pos.line]) return previewOf(line, start, end, { raw: true })
                const lead = lineMarksLength(line)
                return start < lead ? previewOf(line, start, end, { raw: true }) : previewOf(line, start, end, { lead, table: isTableRow(line) })
            },
            token: (i: number) => matchsRef.current[i],
            activate: (i: number, token: unknown) => {
                let k = token ? matchsRef.current.indexOf(token as any) : -1
                if (k < 0) k = i
                if (!(k >= 0 && k < matchsRef.current.length)) return
                matchIndexRef.current = k
                setMatchSelection(matchsRef.current[k])
                remark()
                scrollToElementIfInvisible('.ag-highlight')
                setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
                noteFindChange()
            }
        })
    }, [editor, setMatchSelection, remark, scrollToElementIfInvisible])

    // An edit while the results list is open: the matches found again once the typing stops, the selection left as it is.
    useEffect(() => {
        if (!editor) return
        let timer: any = 0
        const refresh = () => {
            timer = 0
            const last = lastSearchRef.current
            if (!findResultsOpen() || !last) return
            const chosen = matchsRef.current[matchIndexRef.current]
            const at = chosen?.marker?.find()?.from
            const was = at ? editor.indexFromPos(at) : -1
            const caret = editor.indexFromPos(editor.getCursor('from'))
            const found = matchString(editor.getValue(), last.value, last.opt)
            let index = found.findIndex(({ index, match }) => index === was && match === chosen?.match)
            if (index < 0) index = found.findIndex(({ index }) => index >= caret)
            if (index < 0) index = 0
            matchsRef.current.forEach(({ marker }) => marker.clear())
            matchIndexRef.current = index
            matchsRef.current = found.map(({ index, match, subMatches }) => ({ marker: null, index, match, subMatches }))
            remarkNew()
            noteFindChange()
        }
        // Marks for matches that have none yet (remark clears each first).
        const remarkNew = () => matchsRef.current.forEach((item, i) => {
            item.marker = editor.markText(editor.posFromIndex(item.index), editor.posFromIndex(item.index + item.match.length),
                { className: i == matchIndexRef.current ? 'ag-highlight' : 'ag-selection' })
        })
        const onChanges = () => {
            if (!findResultsOpen() || !lastSearchRef.current) return
            clearTimeout(timer)
            timer = setTimeout(refresh, FIND_EDIT_REFRESH_MS)
        }
        editor.on('changes', onChanges)
        return () => { clearTimeout(timer); editor.off('changes', onChanges) }
    }, [editor])

    useEffect(() => transport.addListener<{ value: string, opt: any }>('Search', (arg) => {
        props.onSearchArgChange(arg)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, props, scrollToElementIfInvisible, search]);

    useEffect(() => transport.addListener<{ action: string }>('Find', (arg) => {
        find(arg)
        setTimeout(() => scrollToElementIfInvisible('.ag-highlight'), 0)
    }), [editor, find, scrollToElementIfInvisible]);

    useEffect(() => transport.addListener<{ value: string, opt: any }>('Replace', (arg) => {
        replace(arg)
        noteFindChange()
    }), [replace]);

    useEffect(() => {
        editor?.focus()
    }, [editor])

    // As in the visual editor: the page given the keyboard with nothing in it focused, the editor takes it.
    useEffect(() => {
        if (!editor) return
        const onFocus = () => {
            if (document.activeElement && document.activeElement !== document.body) return
            editor.focus()
        }
        window.addEventListener('focus', onFocus)
        return () => window.removeEventListener('focus', onFocus)
    }, [editor])

    useEffect(() => {
        if (!editor) return
        const onscroll = () => {
            props.scrollTopRef.current = editor.getScrollInfo().top
        }
        editor.on('scroll', onscroll)
        return () => editor.off('scroll', onscroll)
    }, [editor, props.scrollTopRef])

    // Its scroller is what the host's scroll bar shows and moves while it is there (services/scrollbar).
    useEffect(() => {
        if (!editor) return
        setScroller(editor.getScrollerElement())
        return () => setScroller(null)
    }, [editor])

    // The editor the window's height, below the top margin: it scrolls itself and draws only the lines on screen.
    // Settings > Editor > Scroll past the end in source mode too: the last line can come up the window.
    useEffect(() => {
        if (!editor) return
        editor.setSize(null, `calc(100vh - ${marginTop}px)`)
        editor.setOption('scrollPastEnd', !!props.options?.scrollPastEnd)
        editor.refresh()
    }, [editor, marginTop, props.options?.scrollPastEnd])

    // Font and tab size reach the editor by hand: CodeMirror 5 keeps its own font size once created, and
    // its tab width is an option, not a style. "The text in code boxes stays as it was when the font is
    // enlarged" and "pasted code is not indented by four" were Store reviews.
    useEffect(() => {
        if (!editor) return
        const wrapper = editor.getWrapperElement()
        wrapper.style.fontSize = props.options?.fontSize ? `${props.options.fontSize}px` : ''
        wrapper.style.lineHeight = props.options?.lineHeight ? String(props.options.lineHeight) : ''
        const tab = Number(props.options?.tabSize) || 4
        editor.setOption('tabSize', tab)
        editor.setOption('indentUnit', tab)
        editor.refresh()
    }, [editor, props.options?.fontSize, props.options?.lineHeight, props.options?.tabSize])

    // A plain text file laid out as prose: its md and markdown fences drawn as Markdown (markdownBlocks), the one the
    // caret is in shown as its lines. Found again a moment after the text changes, not on every key; a caret move
    // only switches the block it entered and the one it left.
    useEffect(() => {
        if (!editor || !props.options?.plainTextShown || !props.options?.plainTextProse) return
        const view = new MarkdownBlocksView(editor)
        let timer: any = null
        const update = () => { timer = null; view.update() }
        const changed = () => { if (!timer) timer = setTimeout(update, 120) }
        const moved = () => { if (!timer) view.caretMoved() }
        view.update()
        editor.on('changes', changed)
        editor.on('cursorActivity', moved)
        return () => {
            editor.off('changes', changed)
            editor.off('cursorActivity', moved)
            if (timer) clearTimeout(timer)
            view.clear()
        }
    }, [editor, props.options?.plainTextShown, props.options?.plainTextProse])

    // A press in the space around the editor (the prose view's margins either side of its column, the padding above):
    // the editor never heard it, and the browser made a selection of its own over the lines - painted in another
    // colour, while the editor's own went on showing the one before (a reader: two selections, two colours, in a .txt).
    // Taken here instead: the browser's selection prevented, the editor's made from the point, held to the editor's box.
    useEffect(() => {
        if (!editor) return
        const wrapper = editor.getWrapperElement()
        const at = (e: MouseEvent) => {
            const r = wrapper.getBoundingClientRect()
            const x = Math.min(Math.max(e.clientX, r.left + 1), r.right - 1)
            const y = Math.min(Math.max(e.clientY, r.top + 1), r.bottom - 1)
            return editor.coordsChar({ left: x, top: y }, 'window')
        }
        const onDown = (e: MouseEvent) => {
            if (e.button !== 0 || e.defaultPrevented) return
            const target = e.target as Element
            if (!(target instanceof Element) || target.closest('.CodeMirror') || !target.contains(wrapper)) return
            e.preventDefault()
            window.getSelection()?.removeAllRanges()
            editor.focus()
            const anchor = e.shiftKey ? editor.getCursor('anchor') : at(e)
            editor.setSelection(anchor, at(e))
            const move = (m: MouseEvent) => {
                // Past the top or the bottom: the editor scrolls itself, a little each move.
                const r = wrapper.getBoundingClientRect()
                const over = m.clientY < r.top ? m.clientY - r.top : m.clientY > r.bottom ? m.clientY - r.bottom : 0
                if (over) editor.scrollTo(null, editor.getScrollInfo().top + over)
                editor.setSelection(anchor, at(m), { scroll: false })
            }
            const up = () => {
                document.removeEventListener('mousemove', move)
                document.removeEventListener('mouseup', up)
            }
            document.addEventListener('mousemove', move)
            document.addEventListener('mouseup', up)
        }
        document.addEventListener('mousedown', onDown)
        return () => document.removeEventListener('mousedown', onDown)
    }, [editor])

    // A plain text file's view, set on the editor as it changes: the options it was made with are not read again.
    useEffect(() => {
        if (!editor) return
        editor.setOption('mode', props.options?.plainTextShown ? 'text/plain' : 'markdown')
        editor.setOption('lineNumbers', !props.options?.plainTextProse)
        editor.refresh()
    }, [editor, props.options?.plainTextShown, props.options?.plainTextProse])

    return (
        <div
            className={props.options?.plainTextProse ? 'cm-s-one-dark plain-prose' : 'cm-s-one-dark'}
            style={{
                boxSizing: 'border-box',
                paddingTop: marginTop,
                paddingLeft: 14,
                paddingRight: 14,
                // A plain text file in visual mode: laid out as the visual editor lays a document out, in the editor's
                // width, with no line numbers - and still nothing in it parsed (Settings.PlainTextProse).
                ...(props.options?.plainTextProse ? { maxWidth: props.options?.editorAreaWidth || '900px', margin: '0 auto' } : {}),
                fontSize: props.options?.fontSize,
                lineHeight: props.options?.lineHeight,
                fontFamily: props.options?.fontFamily ? `${props.options.fontFamily}, "Open Sans", "Segoe UI", sans-serif` : undefined
            }}>
            <CodeMirror
                ref={(ref: any) => ref && setEditor(ref.editor)}
                options={{
                    theme: 'one-dark',
                    // A plain text file (.txt, .log): no Markdown highlighting, its "#" and "*" are just characters.
                    mode: props.options?.plainTextShown ? 'text/plain' : 'markdown',
                    lineNumbers: !props.options?.plainTextProse,
                    lineWrapping: true,
                    readOnly: !!props.options?.readOnly,
                    keyMap: vim ? 'vim' : 'default',
                    // The host's undo and redo (historyKeys; Vim keeps its own keys).
                    extraKeys: vim ? {} : historyKeys(props.options)
                }}
                onChange={handleCodeMirrorContent}
                onSelection={handleCodeMirrorSelection}
            />
            {vim && <div className="vim-mode-badge">-- {vimLabel} --</div>}
        </div>
    )
}

export default CodeMirrorEditor
