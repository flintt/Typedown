import transport from 'services/transport'

// Hide Done Items, in reading mode and the visual editor (the user's 18, 2026-11-20; docs/todo.md 2026-11-20 用户清单): on, a
// list item or paragraph struck through whole and a ticked task are folded away - each run of them one line, "N hidden ·
// Show", that opens it. Only what is drawn changes, never the text. An ordered list keeps its numbers: the hidden items
// stay list items of no height, still counted. A paragraph with a few words struck stays; a hidden item takes the items
// in it along. Drawn again (an edit, the long document's next part), the marks are made again. A run's line opens it for
// a while: a click elsewhere, or the caret going elsewhere, folds it again (the user, 2026-11-21).

const DONE = 'td-done'
const FIRST = 'td-done-first'

let on = false
let label = '{0}'
// The run opened by its line, by its first block's id; its blocks' ids; the caret when it was opened (still there, the
// run stays open).
let shownRun: string | null = null
let shownRunIds: string[] = []
let shownCaret: Node | null = null
// A press on a run's line taken: its button up and click are not the editor's.
let swallow = false
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

// What done() said of each block, until something in it changes (the observer's records, forgotten below): each key
// looked at every block of the document again - the user's orbit document (75k characters) spent 6 to 10 ms a key in it.
let doneOf = new WeakMap<Element, boolean>()
let doneReading = false

// The blocks a change was in (and those they are in) looked at again.
function forget(records: MutationRecord[]) {
    for (const r of records) {
        const node: Element | null = r.target.nodeType === 1 ? r.target as Element : r.target.parentElement
        for (let block = node?.closest('li, p') ?? null; block; block = block.parentElement?.closest('li, p') ?? null) doneOf.delete(block)
    }
}

function doneNow(el: Element): boolean {
    let is = doneOf.get(el)
    if (is === undefined) doneOf.set(el, is = done(el))
    return is
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
const observer = new MutationObserver(records => {
    forget(records)
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
        doneOf = new WeakMap()
    }
    // Changes not yet told (this called before the observer's turn), and reading mode in or out (its marks drawn else).
    forget(observer.takeRecords())
    if (reading() !== doneReading) { doneReading = reading(); doneOf = new WeakMap() }
    // The caret's line (reading mode: the first line on the screen) kept where it is on the screen when the folds change
    // above it (turning the switch on took the caret's line up, the user, 2026-11-21) - and only then. Its place is the
    // one the last marking left (in the document, which a scroll does not change): measured here, after the editor had
    // drawn a block anew and taken the marks off, it was the place with the done items showing, and putting the marks
    // back then scrolled the page by their height - a click jumped (the user, trial 1.4.3.4).
    const before = kept
    mark(editor)
    const folds = foldsOf(editor)
    if (folds !== lastFolds) {
        say(`folds: ${editor.querySelectorAll(`.${FIRST}`).length} folded, opened ${shownRun ?? 'none'}, caret's run ${caretRun ?? 'none'}`)
        const line = before ? keptNow(before) : null
        if (before && line) {
            const now = line.getBoundingClientRect()
            const moved = Math.round(now.top + window.scrollY - before.top)
            if (now.height > 0 && Math.abs(moved) >= 1) {
                window.scrollBy(0, moved)
                say(`kept "${(line.textContent || '').slice(0, 24)}"${line === before.element ? '' : ' (drawn anew)'} in its place: scrolled ${moved} px (to ${Math.round(window.scrollY)})`)
            } else if (now.height === 0) say(`not kept: "${(line.textContent || '').slice(0, 24)}" is folded away`)
        } else say(before ? `not kept: "${(before.element.textContent || '').slice(0, 24)}" was drawn anew` : 'not kept: no line to keep')
        lastFolds = folds
    }
    remember(lineToKeep(editor))
}

// The line kept in its place, where it is in the document after the last marking; and the folds then.
let kept: { element: Element, top: number, block: string | null } | null = null
let lastFolds = ''

function remember(element: Element | null) {
    kept = element ? { element, top: element.getBoundingClientRect().top + window.scrollY, block: element.closest('[id]')?.id ?? null } : null
}

// The line kept, or the one drawn in its place: a format redraws the block, and the line kept was gone - nothing was kept
// and the page jumped (the user's x280, trial 1.4.3.11: "drawn anew" each time).
function keptNow(before: { element: Element, block: string | null }): Element | null {
    if (before.element.isConnected) return before.element
    const block = before.block ? document.getElementById(before.block) : null
    if (!block) return null
    return block.matches('.ag-paragraph-content') ? block : block.querySelector('.ag-paragraph-content') ?? block
}

// What is folded: what a change of moves the lines below it.
function foldsOf(editor: HTMLElement): string {
    return [...editor.querySelectorAll(`.${DONE}`)].map(el => el.id).join(' ')
}

// What Hide Done Items did, in the app's log (its folds changing, a scroll to keep the caret's line): a jump the reader
// sees can be told from it.
function say(what: string) {
    try { transport.postMessage('HideDoneLog', { what }) } catch (err) { console.log(err) }
}

// The content line a node is in.
function lineOf(node: Node | null | undefined): Element | null {
    return (node?.nodeType === 1 ? node as Element : node?.parentElement)?.closest('.ag-paragraph-content') ?? null
}

// Who scrolls the page in the next frames (a press below a run opened: the page went 2742 px up a frame after the fold,
// by no scroll of ours - the user's x280, trial 1.4.3.11): each call, where from, in the log.
let watchingScrolls = 0
function watchScrolls() {
    const w = window as any
    if (!w.__hdScrollsWrapped) {
        w.__hdScrollsWrapped = true
        const from = () => (new Error().stack || '').split('\n').slice(2, 6).map(l => l.trim().replace(/\(?https?:\/\/[^)]*\//, '(')).join(' < ')
        const wrap = (target: any, name: string, what: string) => {
            const original = target[name]
            target[name] = function (this: any, ...args: any[]) {
                if (watchingScrolls) say(`scrolled by ${what}(${args.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a)).join(', ')}) at ${Math.round(window.scrollY)}: ${from()}`)
                return original.apply(this, args)
            }
        }
        wrap(window, 'scrollTo', 'scrollTo')
        wrap(window, 'scrollBy', 'scrollBy')
        wrap(Element.prototype, 'scrollIntoView', 'scrollIntoView')
        wrap(Element.prototype, 'scrollIntoViewIfNeeded', 'scrollIntoViewIfNeeded')
        let last = window.scrollY
        window.addEventListener('scroll', () => {
            if (watchingScrolls && Math.abs(window.scrollY - last) > 200) say(`the page went from ${Math.round(last)} to ${Math.round(window.scrollY)} (no call above: the browser's own, or scrollTop set)`)
            last = window.scrollY
        }, { passive: true })
    }
    watchingScrolls++
    setTimeout(() => { watchingScrolls-- }, 600)
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

// Only the blocks whose marks change are touched: taking every mark off and putting them back styled and laid out the
// whole document at each key - 111 ms a key against 40 with Hide Done Items off on the user's document (hp88, trial
// 1.4.3.19: "lag to much, a least 1~2 sec"; hide-done-typing-perf-check.js).
function mark(editor: HTMLElement) {
    const want = new Map<Element, string | null>()
    if (on) wanted(editor, want)
    editor.querySelectorAll(`.${DONE}`).forEach(el => {
        if (want.has(el)) return
        el.classList.remove(DONE, FIRST)
        delete (el as HTMLElement).dataset.hiddenRun
    })
    want.forEach((run, el) => {
        if (!el.classList.contains(DONE)) el.classList.add(DONE)
        if (el.classList.contains(FIRST) !== (run !== null)) el.classList.toggle(FIRST, run !== null)
        const data = (el as HTMLElement).dataset
        if (run === null) { if (data.hiddenRun !== undefined) delete data.hiddenRun }
        else if (data.hiddenRun !== run) data.hiddenRun = run
    })
}

// The blocks to fold, each with its run's label if it is a run's first (else null).
function wanted(editor: HTMLElement, want: Map<Element, string | null>) {
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
        if (inside || !doneNow(el)) continue
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
    // A selection within one line (Ctrl+A's first step, a word double-clicked) whose line is in a run: that run shown, as
    // the caret's - a line selected and struck through (Ctrl+A, then the strike's key) was folded away at once under
    // the selection, and what was below jumped up (the user, trial 1.4.3.11 on x280). A selection over more lines (Ctrl+A
    // taken further) opens nothing.
    const within = !reading() && selection && !selection.isCollapsed ? lineOf(selection.anchorNode) : null
    if (within && within === lineOf(selection!.focusNode) && editor.contains(within)) {
        const run = runs.find(r => r.some(el => el.contains(within)))
        if (run) caretRun = run[0].id
    }
    // The run opened by its line folds again once the caret has gone elsewhere (a key moving it out; a click elsewhere
    // folds it at once, below).
    if (shownRun && caret && editor.contains(caret) && caret !== shownCaret && !shownRunIds.some(id => document.getElementById(id)?.contains(caret))) shownRun = null
    for (const run of runs) {
        if (shownRun && run[0].id === shownRun) {
            shownRunIds = run.map(el => el.id)
            continue
        }
        if (caretRun && run[0].id === caretRun) continue
        // A search's match in it: shown while the search is on (in reading mode nothing else would show it - the match is
        // still in the struck text).
        if (run.some(el => el.querySelector('.ag-highlight, .ag-selection'))) continue
        run.forEach((el, i) => want.set(el, i === 0 ? label.replace('{0}', String(run.length)) : null))
    }
}

// The caret moved: marked again in the next frame, the line it went to kept in its place - not the one it left (a click
// into a run opened it and the page scrolled 253 px to keep the line clicked away from, the user's trial 1.4.3.6).
function schedule() {
    if (frame) return
    frame = requestAnimationFrame(() => {
        frame = 0
        const editor = document.getElementById('ag-editor-id')
        if (editor && on) remember(lineToKeep(editor))
        applyHideDone()
    })
}

/** The setting (View > Hide Done Items) and the run's line ("{0} hidden · Show"). */
export function setHideDone(value: boolean, text?: string) {
    // Turned on or off: the caret's line kept from where it is now (nothing was marked while off).
    const editor = document.getElementById('ag-editor-id')
    if (value !== on && editor) {
        remember(lineToKeep(editor))
        say(`turned ${value ? 'on' : 'off'}`)
    }
    on = value
    if (text) label = text
    if (!installed) {
        installed = true
        // The caret moved (the visual editor): the run it is in shown, the one it left folded.
        document.addEventListener('selectionchange', () => { if (on && !reading()) schedule() })
        // On the button going down, before the editor puts the caret anywhere: a run's line opens it for a while, the
        // caret left where it was. Handled at the click, the editor had put the caret in the run already and the line was
        // gone - it opened for good now and then, till a line at its right edge folded it, and the other times only till
        // the next click (the user, 2026-11-21). A press elsewhere: the line pressed kept where it is on the screen,
        // measured now, before anything moves (it jumped as the run above it folded), and a run opened folds unless the
        // press is in it.
        document.addEventListener('mousedown', e => {
            const editor = document.getElementById('ag-editor-id')
            const target = e.target as Element | null
            if (!on || !editor || !target || !editor.contains(target)) return
            const first = target.closest(`.${FIRST}`)
            // A third press on a line: the line selected here. The browser's own takes the paragraph to the start of
            // the next, and with a fold there (nothing in it to select) it selected nothing (the user's 36, 2026-11-22).
            const tripled = !first && e.detail >= 3 ? target.closest('.ag-paragraph-content') : null
            if (tripled) {
                e.preventDefault()
                // Its ends in its text, not the line itself: the editor reads a selection's ends as places in text, and
                // one at the line's own end it read as 1 - a format then took the first letter (the user, 2026-11-23).
                const range = document.createRange()
                const walker = document.createTreeWalker(tripled, NodeFilter.SHOW_TEXT)
                const texts: Text[] = []
                for (let n = walker.nextNode(); n; n = walker.nextNode()) texts.push(n as Text)
                if (texts.length) {
                    range.setStart(texts[0], 0)
                    range.setEnd(texts[texts.length - 1], texts[texts.length - 1].length)
                } else range.selectNodeContents(tripled)
                const selection = window.getSelection()
                selection?.removeAllRanges()
                selection?.addRange(range)
                return
            }
            if (first) {
                e.preventDefault()
                e.stopPropagation()
                // Its button up and its click are this press's too: let through, the editor put the caret back where it
                // had been and went there - the page scrolled away from the run just opened.
                swallow = true
                remember(first)
                shownRun = first.id
                shownRunIds = [first.id]
                shownCaret = window.getSelection()?.anchorNode ?? null
                applyHideDone()
                return
            }
            const pressed = target.closest('.ag-paragraph-content') ?? target.closest('li, p')
            remember(pressed)
            if (shownRun && !shownRunIds.some(id => document.getElementById(id)?.contains(target))) {
                shownRun = null
                // Where the line pressed is on the screen as the run folds, and the next two frames: a jump the reader
                // still sees after it says which step moved it (the user, 1.4.3.9: "in some cases" it jumped).
                const at = () => pressed && pressed.isConnected ? Math.round(pressed.getBoundingClientRect().top) : 'gone'
                const before = at()
                watchScrolls()
                applyHideDone()
                const after = at()
                requestAnimationFrame(() => {
                    const one = at(), y1 = Math.round(window.scrollY)
                    requestAnimationFrame(() => say(`pressed below a run opened: "${(pressed?.textContent || '').slice(0, 20)}" at ${before}, ${after} as it folded, ${one} a frame on (scroll ${y1}), ${at()} two frames on (scroll ${Math.round(window.scrollY)})`))
                })
            }
        }, true)
        for (const kind of ['mouseup', 'click']) {
            document.addEventListener(kind, e => {
                if (!swallow) return
                e.preventDefault()
                e.stopPropagation()
                if (kind === 'click') swallow = false
            }, true)
        }
    }
    applyHideDone()
}
