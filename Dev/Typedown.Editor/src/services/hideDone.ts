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

/** The marks made again for what is drawn now. */
export function applyHideDone() {
    const editor = document.getElementById('ag-editor-id')
    if (!editor) return
    editor.querySelectorAll(`.${DONE}, .${OPENED}`).forEach(el => {
        el.classList.remove(DONE, FIRST, OPENED)
        delete (el as HTMLElement).dataset.hiddenRun
        delete (el as HTMLElement).dataset.collapseRun
    })
    if (!on) return
    // The visual editor too (one switch for both, the user's call): the run with the caret in it is shown - an item just
    // ticked or struck stays under the caret until it goes elsewhere.
    const caret = reading() ? null : window.getSelection()?.anchorNode ?? null
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
    for (const run of runs) {
        if (opened.has(run[0].id)) {
            run[0].classList.add(OPENED)
            ;(run[0] as HTMLElement).dataset.collapseRun = collapseLabel.replace('{0}', String(run.length))
            continue
        }
        if (caret && editor.contains(caret) && run.some(el => el.contains(caret))) continue
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
        const editor = document.getElementById('ag-editor-id')
        // Drawn again: marked again (the marks are classes, no change to the tree, so this does not wake itself).
        if (editor) new MutationObserver(() => { if (on) schedule() }).observe(editor, { childList: true, subtree: true, characterData: true })
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
