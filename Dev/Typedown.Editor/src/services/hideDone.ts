// Hide Done Items, in reading mode and the visual editor (the user's 18, 2026-11-20; docs/todo.md 2026-11-20 用户清单): on, a
// list item or paragraph struck through whole and a ticked task are folded away - each run of them one line, "N hidden ·
// Show", that opens it. Only what is drawn changes, never the text. An ordered list keeps its numbers: the hidden items
// stay list items of no height, still counted. A paragraph with a few words struck stays; a hidden item takes the items
// in it along. Drawn again (an edit, the long document's next part), the marks are made again; a run opened stays open.

const DONE = 'td-done'
const FIRST = 'td-done-first'
// An opened run's first block: the line over it folds the run again (the user, 2026-11-20).
const OPENED = 'td-done-opened'

let on = false
let label = '{0}'
let collapseLabel = '{0}'
// Runs opened, by their first block's id.
const opened = new Set<string>()
let installed = false
let frame = 0
// The run the caret is in, by its first block's id: shown (kept while there is a selection rather than a caret).
let caretRun: string | null = null

const reading = () => document.body.classList.contains('read-only')

// The block's own text: its content lines (.ag-paragraph-content), a list item's not those of the lists in it - not the icons and
// markers drawn beside them (a paragraph's "P" icon is text, hidden).
function ownParts(el: Element): Element[] {
    const parts = el.tagName === 'LI' ? [...el.children].filter(c => !/^(UL|OL)$/.test(c.tagName)) : [el]
    return parts.flatMap(p => p.matches('.ag-paragraph-content') ? [p] : [...p.querySelectorAll('.ag-paragraph-content')])
}

// Struck through whole: every word it shows in a <del> (the marks reading mode hides not counted).
function struck(el: Element): boolean {
    let any = false
    for (const part of ownParts(el)) {
        const walker = document.createTreeWalker(part, NodeFilter.SHOW_TEXT)
        for (let n = walker.nextNode(); n; n = walker.nextNode()) {
            if (!n.textContent?.trim()) continue
            const parent = n.parentElement
            // The marks, hidden (.ag-hide) or shown in the block with the caret (.ag-gray): not words.
            if (parent?.closest('.ag-hide, .ag-gray')) continue
            if (!parent?.closest('del')) return false
            any = true
        }
    }
    return any
}

function done(el: Element): boolean {
    if (el.tagName === 'LI') {
        const box = [...el.children].find(c => c.tagName === 'INPUT')
        if (box && (box.classList.contains('ag-checkbox-checked') || (box as HTMLInputElement).checked)) return true
    }
    return struck(el)
}

// Drawn again: marked again at once, before the page is painted. A block's class set anew (the editor marking the active
// block) took the marks off too, and nothing put them back till the caret moved: the done items showed for some frames and
// the document jumped (a click now and then flashed, the user, 2026-11-21). Our own marking's records are taken off after
// it, so it does not wake itself. Watched on the editor there is now: going to source mode and back makes it anew, and the
// one watched before was gone.
const observer = new MutationObserver(() => {
    if (!on) return
    applyHideDone()
    observer.takeRecords()
})
let watched: HTMLElement | null = null

/** The marks made again for what is drawn now. */
export function applyHideDone() {
    const editor = document.getElementById('ag-editor-id')
    if (!editor) return
    if (editor !== watched) {
        observer.disconnect()
        observer.observe(editor, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] })
        watched = editor
    }
    // The caret's line (reading mode: the first line on the screen) kept where it is on the screen: the folds made or
    // opened above it moved it (turning the switch on took the caret's line up, the user, 2026-11-21).
    const anchor = lineToKeep(editor)
    const anchorTop = anchor?.getBoundingClientRect().top
    mark(editor)
    if (anchor && anchorTop !== undefined && anchor.isConnected) {
        const now = anchor.getBoundingClientRect()
        if (now.height > 0 && Math.abs(now.top - anchorTop) >= 1) window.scrollBy(0, now.top - anchorTop)
    }
}

// The line to keep in its place: the caret's in the visual editor when it is on the screen, else the first on the screen.
function lineToKeep(editor: HTMLElement): Element | null {
    const selection = window.getSelection()
    const at = !reading() && selection?.anchorNode && editor.contains(selection.anchorNode) ? selection.anchorNode : null
    const caretLine = (at?.nodeType === 1 ? at as Element : at?.parentElement)?.closest('.ag-paragraph-content') ?? null
    if (caretLine) {
        const r = caretLine.getBoundingClientRect()
        if (r.height > 0 && r.bottom > 0 && r.top < window.innerHeight) return caretLine
    }
    for (const line of editor.querySelectorAll('.ag-paragraph-content')) {
        const r = line.getBoundingClientRect()
        if (r.height > 0 && r.top >= 0) return line
        if (r.top > window.innerHeight) break
    }
    return null
}

function mark(editor: HTMLElement) {
    editor.querySelectorAll(`.${DONE}, .${OPENED}`).forEach(el => {
        el.classList.remove(DONE, FIRST, OPENED)
        delete (el as HTMLElement).dataset.hiddenRun
        delete (el as HTMLElement).dataset.collapseRun
    })
    if (!on) return
    // The visual editor too (one switch for both, the user's call): the run with the caret in it is shown - an item just
    // ticked or struck stays under the caret until it goes elsewhere. Only a caret: a selection (Ctrl+A step by step,
    // a drag) leaves the folds as they were - its start going into a run opened it and the document jumped (the user,
    // 2026-11-21). The run the caret showed last stays shown while there is a selection.
    const selection = window.getSelection()
    const caret = reading() || !selection?.isCollapsed ? null : selection.anchorNode
    if (reading()) caretRun = null
    // Document order: a list item before the ones in it, so one hidden takes those along (they are not looked at).
    const hidden: Element[] = []
    const taken = new Set<Element>()
    for (const el of editor.querySelectorAll('li, p')) {
        if (el.tagName === 'P' && el.closest('li')) continue
        let inside = false
        for (let a = el.parentElement?.closest('li'); a; a = a.parentElement?.closest('li')) if (taken.has(a)) { inside = true; break }
        if (inside || !done(el)) continue
        taken.add(el)
        hidden.push(el)
    }
    // Runs: hidden blocks one right after the other.
    const runs: Element[][] = []
    for (const el of hidden) {
        const last = runs[runs.length - 1]
        if (last && last[last.length - 1].nextElementSibling === el) last.push(el)
        else runs.push([el])
    }
    if (caret && editor.contains(caret)) caretRun = runs.find(run => run.some(el => el.contains(caret)))?.[0].id ?? null
    for (const run of runs) {
        if (opened.has(run[0].id)) {
            run[0].classList.add(OPENED)
            ;(run[0] as HTMLElement).dataset.collapseRun = collapseLabel.replace('{0}', String(run.length))
            continue
        }
        if (caretRun && run[0].id === caretRun) continue
        // A search's match in it: shown while the search is on (in reading mode nothing else would show it - the match is
        // still in the struck text).
        if (run.some(el => el.querySelector('.ag-highlight, .ag-selection'))) continue
        run.forEach(el => el.classList.add(DONE))
        run[0].classList.add(FIRST)
        ;(run[0] as HTMLElement).dataset.hiddenRun = label.replace('{0}', String(run.length))
    }
}

function schedule() {
    if (frame) return
    frame = requestAnimationFrame(() => { frame = 0; applyHideDone() })
}

/** The setting (View > Hide Done Items), the run's line ("{0} hidden · Show") and an opened run's ("{0} shown · Hide"). */
export function setHideDone(value: boolean, text?: string, collapseText?: string) {
    on = value
    if (text) label = text
    if (collapseText) collapseLabel = collapseText
    if (!installed) {
        installed = true
        // The caret moved (the visual editor): the run it is in shown, the one it left folded.
        document.addEventListener('selectionchange', () => { if (on && !reading()) schedule() })
        // A run's line opens it; an opened run's line (its first block's own box, not its text) folds it again.
        document.addEventListener('click', e => {
            if (!on) return
            const target = e.target as Element | null
            const first = target?.closest?.(`#ag-editor-id .${FIRST}`)
            const back = target?.matches?.(`#ag-editor-id .${OPENED}`) ? target : null
            if (!first && !back) return
            e.preventDefault()
            e.stopPropagation()
            if (first) opened.add(first.id)
            else opened.delete(back!.id)
            applyHideDone()
        }, true)
    }
    applyHideDone()
}
