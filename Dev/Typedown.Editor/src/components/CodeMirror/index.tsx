import React, { useCallback, useEffect, useRef, useState } from "react";
import transport from "services/transport";
import { matchString } from 'services/common'
import { UnControlled as CodeMirror } from 'react-codemirror2';
import 'codemirror/lib/codemirror.css';
import { getTOC } from "services/common";
import { minimalChange } from "services/minimalChange";
import { EXTERNAL_CHANGE_CLASS, EXTERNAL_CHANGE_MS, scrollTargetForChange } from "services/externalChange";
import { setVimState, vimCommand } from "services/vim";
import { wordCount as countWords } from 'components/Muya/lib/utils';
import { FIND_EDIT_REFRESH_MS, findResultsOpen, noteFindChange, setFindProvider } from 'services/findResults';
import { FENCE, isTableRow, lineMarksLength, previewOf } from 'services/findPreview';
require('codemirror/mode/markdown/markdown');
require('codemirror/keymap/vim');
require('codemirror/addon/dialog/dialog.css');

// Vim keys (services/vim): :w saves, :q closes the tab, :wq and :x do both - through the application, which asks
// before closing an unsaved document as it always does.
const CodeMirrorLib = require('codemirror');
CodeMirrorLib.commands.save = () => vimCommand('write');
CodeMirrorLib.Vim.defineEx('quit', 'q', () => vimCommand('quit'));
CodeMirrorLib.Vim.defineEx('wq', 'wq', () => vimCommand('writeQuit'));
CodeMirrorLib.Vim.defineEx('xit', 'x', () => vimCommand('writeQuit'));

const VIM_LABELS: Record<string, string> = { normal: 'NORMAL', insert: 'INSERT', replace: 'REPLACE', visual: 'VISUAL', linewise: 'V-LINE', blockwise: 'V-BLOCK' };

interface ICodeMirrorEditor {
    markdown: string
    cursor: any
    /** Incremented by the container whenever the host replaces the content; forces the effect below to re-check. */
    contentVersion?: number
    options: any
    searchOpen: number
    searchArg: { value: string, opt: any } | undefined
    scrollTopRef: React.MutableRefObject<number>
    scrollFromHostRef?: React.MutableRefObject<boolean>
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
    const matchIndexRef = useRef(0);
    const markdownRef = useRef('');
    const searchArgRef = useRef<any>();
    const cursorRef = useRef<any>();
    const vim = !!props.options?.vimMode && !props.options?.readOnly
    const [vimLabel, setVimLabel] = useState('NORMAL');
    useEffect(() => {
        // The mode Vim is in, for the badge and for the host, which must leave Vim's Ctrl keys alone in normal and
        // visual mode (services/vim).
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
        return () => editor.off('vim-mode-change', onMode)
    }, [editor, vim])

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

    const replaceOne = useCallback((match, replaceValue, offset = 0) => {
        const text = editor.getValue();
        const start = match.index + offset;
        const end = start + match.match.length;
        const value = text.substring(0, start) + replaceValue + text.substring(end);
        editor.setValue(value);
        return replaceValue.length - match.match.length;
    }, [editor])

    const replace = useCallback(({ value, opt }) => {
        if (opt.isSingle) {
            if (!matchsRef.current[matchIndexRef.current])
                return;
            const offset = replaceOne(matchsRef.current[matchIndexRef.current], value)
            matchsRef.current = matchsRef.current.map((e, i) => {
                if (i < matchIndexRef.current) return e;
                e.index += offset;
                return e;
            }).filter((e, i) => i != matchIndexRef.current)
            matchIndexRef.current--;
            find({ action: 'next' })
        } else {
            if (!matchsRef.current)
                return;
            let offset = 0;
            for (const match of matchsRef.current) {
                offset += replaceOne(match, value, offset)
            }
        }
    }, [find, replaceOne])

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
        editor?.execCommand('selectAll')
    }), [editor]);

    useEffect(() => {
        searchArgRef.current = props.searchArg;
    }, [props.searchArg])

    useEffect(() => {
        markdownRef.current = ''
    }, [editor])

    useEffect(() => {
        if (!editor) return
        const local = props.localChangeRef?.current
        if (props.localChangeRef) props.localChangeRef.current = false
        const scrollToChange = !!props.scrollToChangeRef?.current
        if (props.scrollToChangeRef) props.scrollToChangeRef.current = false
        if (local && markdownRef.current != props.markdown) {
            // Replacing only what changed keeps the cursor, the selection and the scroll position where the reader
            // left them; CodeMirror maps them over the change.
            const change = minimalChange(editor.getValue(), props.markdown)
            markdownRef.current = props.markdown
            if (change) {
                editor.replaceRange(change.text, editor.posFromIndex(change.from), editor.posFromIndex(change.to), '+automation')
                if (change.text && props.options?.highlightAutomationChanges !== false) {
                    const mark = editor.markText(editor.posFromIndex(change.from), editor.posFromIndex(change.from + change.text.length), { className: EXTERNAL_CHANGE_CLASS })
                    setTimeout(() => mark.clear(), EXTERNAL_CHANGE_MS)
                }
                // reveal: "change": the start of the change into view unless the reader can already see it.
                if (scrollToChange) {
                    const start = editor.charCoords(editor.posFromIndex(change.from), 'page')
                    const end = editor.charCoords(editor.posFromIndex(change.from + change.text.length), 'page')
                    const y = scrollTargetForChange(start.top, end.bottom, window.scrollY)
                    if (y !== null) window.scrollTo(window.scrollX, y)
                }
            }
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
        } else if (markdownRef.current != props.markdown) {
            markdownRef.current = props.markdown
            const { anchor, head } = cursorRef.current ?? {}
            editor.setValue(markdownRef.current)
            if (anchor && head)
                editor.setSelection(anchor, head, { scroll: !props.scrollFromHostRef?.current })
            window.scrollTo(window.scrollX, props.scrollTopRef.current)
            if (props.scrollFromHostRef) props.scrollFromHostRef.current = false
            if (searchArgRef.current?.value != "" && searchArgRef.current?.opt?.selection) {
                const { value, opt } = searchArgRef.current
                const selection = opt.selection.head ? opt.selection : undefined
                search({ value, opt: { ...opt, selection } })
            }
        }
        props.onContentApplied?.()
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

    const countRef = useRef<{ time: number, value: any, timer?: any }>({ time: 0, value: null })

    const handleCodeMirrorState = useCallback((value: string) => {
        // Counted as the visual editor counts (Muya's wordCount): source mode had its own count (every character,
        // words split at spaces only), so a mode switch moved the count with nothing written - and what follows the
        // count, as edits, took the jump for writing. On a long document at most every 300 ms while typing, and once
        // more when it stops, as the visual editor does: counting it is a share of every keystroke.
        const counted = countRef.current
        clearTimeout(counted.timer)
        const now = Date.now()
        if (value.length < 50000 || !counted.value || now - counted.time >= 300) {
            counted.time = now
            counted.value = countWords(value)
        } else {
            counted.timer = setTimeout(() => {
                counted.time = Date.now()
                counted.value = countWords(value)
                const { toc } = getTOC(value)
                onStateChange({ wordCount: counted.value, toc, cur: toc[0] })
            }, 300)
        }
        const { toc } = getTOC(value)
        const state = { wordCount: counted.value, toc, cur: toc[0] }
        onStateChange(state)
    }, [onStateChange])

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
        const onscroll = () => {
            props.scrollTopRef.current = window.scrollY
        }
        addEventListener('scroll', onscroll);
        return () => removeEventListener('scroll', onscroll)
    }, [props.scrollTopRef])

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

    return (
        <div
            className="cm-s-one-dark"
            style={{
                boxSizing: 'border-box',
                paddingTop: marginTop,
                paddingLeft: 14,
                paddingRight: 14,
                fontSize: props.options?.fontSize,
                lineHeight: props.options?.lineHeight,
                fontFamily: props.options?.fontFamily ? `${props.options.fontFamily}, "Open Sans", "Segoe UI", sans-serif` : undefined
            }}>
            <CodeMirror
                ref={(ref: any) => ref && setEditor(ref.editor)}
                options={{
                    theme: 'one-dark',
                    mode: 'markdown',
                    lineNumbers: true,
                    lineWrapping: true,
                    readOnly: !!props.options?.readOnly,
                    keyMap: vim ? 'vim' : 'default',
                    // CodeMirror's own history is not the document's: these ask the host for its undo and redo (Vim keeps
                    // its own keys).
                    extraKeys: vim ? {} : {
                        'Ctrl-Z': () => transport.postMessage('HistoryRequested', { redo: false }),
                        'Ctrl-Y': () => transport.postMessage('HistoryRequested', { redo: true }),
                        'Shift-Ctrl-Z': () => transport.postMessage('HistoryRequested', { redo: true }),
                    }
                }}
                onChange={handleCodeMirrorContent}
                onSelection={handleCodeMirrorSelection}
            />
            {vim && <div className="vim-mode-badge">-- {vimLabel} --</div>}
        </div>
    )
}

export default CodeMirrorEditor
