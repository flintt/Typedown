// Review mode on the page (docs/todo.md, "审阅模式"): the document's revisions against the baseline the host sent with
// the document (services/review.ts), kept up after every change, drawn by the editor shown (a ReviewView: Muya's or
// CodeMirror's), accepted and rejected from the keys, the buttons beside a revision and the host's commands (its status
// bar), and reported back to the host, which keeps them with the document in the app's data.

import transport from './transport'
import {
    acceptAll, acceptInto, changedRegion, countRevisions, nextRevision, rejectAll, rejectFrom, Revision, revisionAt,
    RevisionSource, shiftAfterAccept, StoredRevision, storedOf, trackRevisions, YOU,
} from './review'

/** What the host sends with a document in review (or in ReviewSet): null or on false, no review. */
export interface ReviewPayload {
    on: boolean
    /** Show the revisions (false: the document as if all were accepted). */
    show?: boolean
    /** Only one source's revisions shown and acted on ('' the reader's); null or absent: all. */
    filter?: string | null
    baseline?: string
    revisions?: StoredRevision[]
    /** Who the differences nobody is known for are from (a program's write to a tab in the background). */
    fallback?: RevisionSource | null
    /** The words the page shows, in the interface's language. */
    labels?: { accept?: string, reject?: string, you?: string }
}

/** How an editor shows the revisions: Muya's and CodeMirror's. Offsets are in the Markdown. */
export interface ReviewView {
    draw(revisions: Revision[], text: string): void
    clear(): void
    caretOffset(): number | null
    setCaret(offset: number): void
    /** Where a revision is on the screen (its first mark), for its buttons. */
    rectOf(id: string): DOMRect | null
    /** The revision under an element (a mark the mouse is on), or null. */
    revisionOf(element: Element): string | null
}

let view: ReviewView | null = null

export function registerReviewView(v: ReviewView): () => void {
    view = v
    // The editor that comes draws what there is (a switch between the visual editor and source mode).
    setTimeout(() => { if (view === v) redraw() }, 0)
    return () => { if (view === v) view = null }
}

export interface ReviewOutcome {
    id: string
    outcome: 'accepted' | 'rejected'
    at: number
    source: string
    session?: string
    kind: string
    oldText: string
    newText: string
}

interface State {
    on: boolean
    show: boolean
    filter: string | null
    baseline: string
    revisions: Revision[]
    /** Revisions known from before (the host's, for a load) not yet matched with the text. */
    pending: StoredRevision[] | null
    fallback: RevisionSource
    lastText: string | null
    baselineChanged: boolean
    outcomes: ReviewOutcome[]
    labels: { accept: string, reject: string, you: string }
}

const state: State = {
    on: false, show: true, filter: null, baseline: '', revisions: [], pending: null, fallback: YOU, lastText: null,
    baselineChanged: false, outcomes: [], labels: { accept: '✓', reject: '✗', you: '' },
}

export const reviewActive = () => state.on

/** The page's text now and the way to put new text in (as the reader's edit, reported to the host): the editor shell's. */
export interface ReviewHost {
    text: () => string
    loadId: () => number | undefined
    replaceText: (text: string) => void
}

let host: ReviewHost | null = null

export function connectReviewHost(h: ReviewHost) {
    host = h
}

/** A document in, or the host's review state for the one shown changed. */
export function setReview(payload: ReviewPayload | null | undefined, newDocument: boolean) {
    const on = !!payload?.on
    if (!on) {
        const was = state.on
        state.on = false
        state.revisions = []
        state.pending = null
        state.lastText = null
        hideActions()
        if (was) view?.clear()
        return
    }
    const p = payload!
    if (newDocument || !state.on || typeof p.baseline === 'string') {
        state.baseline = p.baseline ?? state.baseline
        state.pending = p.revisions ?? (newDocument ? [] : state.revisions)
        state.revisions = []
        state.lastText = null
        state.outcomes = []
        state.baselineChanged = false
    }
    state.on = true
    state.show = p.show !== false
    state.filter = p.filter ?? null
    state.fallback = p.fallback ?? YOU
    if (p.labels) state.labels = { ...state.labels, ...Object.fromEntries(Object.entries(p.labels).filter(([, v]) => !!v)) }
    if (!newDocument) refresh()
}

/** The source of the next change (an automation edit being applied); null: the reader's. */
let nextSource: RevisionSource | null = null

export function noteChangeSource(source: RevisionSource | null) {
    nextSource = source
}

/** The text changed (or was loaded): the revisions worked out again and drawn. */
export function refresh() {
    if (!state.on || !host) return
    const text = host.text()
    if (text === state.lastText && !state.pending) return redraw()
    const time = Date.now()
    const previous = state.pending ?? state.revisions
    const region = state.lastText === null || state.pending ? null : changedRegion(state.lastText, text)
    const source = nextSource ?? YOU
    state.revisions = trackRevisions(state.baseline, text, previous, region ? { ...region, source, time } : { source: state.fallback, time })
    state.pending = null
    state.lastText = text
    redraw()
    scheduleReport()
}

const shown = () => !state.show ? [] : state.filter === null ? state.revisions : state.revisions.filter(r => r.source === state.filter)

function redraw() {
    if (!view) return
    if (!state.on) { view.clear(); return }
    view.draw(shown(), state.lastText ?? host?.text() ?? '')
}

// ---- reporting ----

let reportTimer = 0

function scheduleReport() {
    clearTimeout(reportTimer)
    reportTimer = window.setTimeout(reportNow, 300)
}

/** Sends the revisions to the host now (before a save, a switch away). */
export function reportNow() {
    clearTimeout(reportTimer)
    reportTimer = 0
    if (!state.on || !host || state.lastText === null) return
    const text = state.lastText
    const counts = countRevisions(state.revisions)
    transport.postMessage('ReviewState', {
        loadId: host.loadId(),
        baseline: state.baselineChanged ? state.baseline : undefined,
        revisions: state.revisions.map(r => storedOf(r, text)),
        inserted: counts.inserted,
        deleted: counts.deleted,
        sources: counts.sources,
        outcomes: state.outcomes,
    })
    state.baselineChanged = false
    state.outcomes = []
}

// ---- acting on revisions ----

const outcomeOf = (r: Revision, outcome: 'accepted' | 'rejected'): ReviewOutcome =>
    ({ id: r.id, outcome, at: Date.now(), source: r.source, session: r.session, kind: r.kind, oldText: r.oldText, newText: r.newText })

function accept(revisions: Revision[]) {
    if (!revisions.length || !host) return
    state.baseline = revisions.length === 1 ? acceptInto(state.baseline, revisions[0]) : acceptAll(state.baseline, revisions)
    state.baselineChanged = true
    state.outcomes.push(...revisions.map(r => outcomeOf(r, 'accepted')))
    const text = host.text()
    state.revisions = trackRevisions(state.baseline, text, shiftAfterAccept(state.revisions, revisions), { source: YOU, time: Date.now() })
    state.lastText = text
    hideActions()
    redraw()
    reportNow()
}

function reject(revisions: Revision[]) {
    if (!revisions.length || !host) return
    const text = host.text()
    const next = revisions.length === 1 ? rejectFrom(text, revisions[0]) : rejectAll(text, revisions)
    state.outcomes.push(...revisions.map(r => outcomeOf(r, 'rejected')))
    const ids = new Set(revisions.map(r => r.id))
    // The text without them: the other revisions keep what they are (no change region - nobody new made them).
    state.revisions = trackRevisions(state.baseline, next, state.revisions.filter(r => !ids.has(r.id)), { source: YOU, time: Date.now() })
    state.lastText = next
    hideActions()
    host.replaceText(next)
    redraw()
    reportNow()
}

const byId = (id: string | null) => (id && state.revisions.find(r => r.id === id)) || null

function atCaret(): Revision | null {
    const offset = view?.caretOffset()
    return offset == null ? null : revisionAt(shown(), offset)
}

function go(back: boolean) {
    const offset = view?.caretOffset() ?? (back ? Number.MAX_SAFE_INTEGER : -1)
    const r = nextRevision(shown(), offset, back)
    if (!r) return
    view?.setCaret(r.curStart)
    // The caret is in it now: its buttons, even for the one just typed in (the reader went to it).
    quietUntilOther = null
    setTimeout(() => showActionsFor(r.id), 0)
}

/** The host's commands (its status bar, its menus): next, previous, accept, reject, acceptAll, rejectAll. */
export function command(name: string, source?: string | null) {
    if (!state.on) return
    const of = (list: Revision[]) => source === undefined || source === null ? list : list.filter(r => r.source === source)
    switch (name) {
        case 'next': return go(false)
        case 'previous': return go(true)
        case 'accept': { const r = atCaret(); if (r) accept([r]); return }
        case 'reject': { const r = atCaret(); if (r) reject([r]); return }
        case 'acceptAll': return accept(of(shown()))
        case 'rejectAll': return reject(of(shown()))
    }
}

/** Alt+Down, Alt+Up, Alt+Enter, Alt+Backspace while review is on; true when the key was review's. */
export function onReviewKey(e: KeyboardEvent): boolean {
    if (!state.on || !e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return false
    const name = e.key === 'ArrowDown' ? 'next' : e.key === 'ArrowUp' ? 'previous' : e.key === 'Enter' ? 'accept' : e.key === 'Backspace' ? 'reject' : null
    if (!name) return false
    e.preventDefault()
    e.stopPropagation()
    command(name)
    return true
}

// ---- the buttons beside a revision ----

let actions: HTMLDivElement | null = null
let actionsFor: string | null = null
// Typing hides them until the caret goes to another revision.
let quietUntilOther: string | null = null

function actionsElement() {
    if (actions) return actions
    actions = document.createElement('div')
    actions.id = 'td-review-actions'
    actions.setAttribute('contenteditable', 'false')
    const button = (cls: string, text: string, act: () => void) => {
        const b = document.createElement('button')
        b.className = cls
        b.textContent = text
        b.tabIndex = -1
        // On mousedown, before the editor loses its caret to the click.
        b.addEventListener('mousedown', e => { e.preventDefault(); e.stopPropagation(); act() })
        return b
    }
    actions.append(
        button('td-review-accept', '✓', () => { const r = byId(actionsFor); if (r) accept([r]) }),
        button('td-review-reject', '✗', () => { const r = byId(actionsFor); if (r) reject([r]) }),
    )
    document.body.appendChild(actions)
    return actions
}

function hideActions() {
    actionsFor = null
    if (actions) actions.style.display = 'none'
}

/** The buttons at the right end of the revision's line, in the margin beside the text where there is room. */
export function showActionsFor(id: string | null) {
    if (!state.on || !id || !state.show || id === quietUntilOther) return hideActions()
    const rect = view?.rectOf(id)
    if (!rect) return hideActions()
    const el = actionsElement()
    const [accept, reject] = Array.from(el.children) as HTMLElement[]
    accept.title = state.labels.accept
    reject.title = state.labels.reject
    accept.setAttribute('aria-label', state.labels.accept)
    reject.setAttribute('aria-label', state.labels.reject)
    el.style.display = 'flex'
    const editor = document.querySelector('#ag-editor-id') ?? document.querySelector('.CodeMirror-lines')
    const column = editor?.getBoundingClientRect()
    const width = el.offsetWidth || 56
    const right = column ? column.right : window.innerWidth - 8
    // Past the text's column when the window leaves room there; else at its right end, over the margin of the line.
    const x = right + 4 + width <= window.innerWidth ? right + 4 : Math.max(0, right - width)
    el.style.left = `${Math.round(x + window.scrollX)}px`
    el.style.top = `${Math.round(rect.top + window.scrollY)}px`
    actionsFor = id
    quietUntilOther = null
}

let hoverTimer = 0

/** Wires the caret, the mouse and the keys to the buttons; once for the page. */
export function installReviewActions() {
    document.addEventListener('selectionchange', () => {
        if (!state.on) return
        const r = atCaret()
        if (!r) { if (actionsFor && !hovering) hideActions(); quietUntilOther = null; return }
        if (r.id !== actionsFor && r.id !== quietUntilOther) showActionsFor(r.id)
    })
    window.addEventListener('keydown', e => {
        if (!state.on || e.altKey || e.ctrlKey || e.metaKey) return
        if (e.key.length === 1 || e.key === 'Backspace' || e.key === 'Delete' || e.key === 'Enter') {
            quietUntilOther = actionsFor ?? atCaret()?.id ?? null
            hideActions()
        }
    }, true)
    window.addEventListener('keydown', e => { onReviewKey(e) }, true)
    document.addEventListener('mouseover', e => {
        if (!state.on || !view) return
        const target = e.target as Element
        if (actions && actions.contains(target)) { clearTimeout(hoverTimer); return }
        const id = target instanceof Element ? view.revisionOf(target) : null
        clearTimeout(hoverTimer)
        hovering = !!id
        if (id) hoverTimer = window.setTimeout(() => { quietUntilOther = null; showActionsFor(id) }, 500)
    })
    window.addEventListener('scroll', () => { if (actionsFor) showActionsFor(actionsFor) }, true)
}

let hovering = false

/** For page checks: the state as the page holds it. */
;(window as any).__typedownReview = () => ({
    on: state.on, show: state.show, filter: state.filter, baseline: state.baseline,
    revisions: state.revisions.map(r => ({ id: r.id, kind: r.kind, oldText: r.oldText, newText: r.newText, source: r.source, curStart: r.curStart })),
    actionsFor,
})
