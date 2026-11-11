// Review mode on the page (docs/todo.md, "审阅模式"): the document's revisions against the baseline the host sent with
// the document (services/review.ts), kept up after every change, drawn by the editor shown (a ReviewView: Muya's or
// CodeMirror's), accepted and rejected from the keys, the buttons beside a revision and the host's commands (its status
// bar), and reported back to the host, which keeps them with the document in the app's data.

import transport from './transport'
import { diffPaths } from './review'
import {
    acceptAll, acceptInto, changedRegion, groupFormats, isFormatMark, onlyWriting, countRevisions, nextRevision, rejectAll, rejectFrom, Revision, revisionAt,
    RevisionSource, shiftAfterAccept, StoredRevision, storedOf, structureChange, trackRevisions, YOU,
} from './review'
import { renumberedAt } from './orderedLists'
import { isBreakAndMarks } from './reviewMap'

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
    labels?: { accept?: string, reject?: string, you?: string, locale?: string, formats?: Record<string, string>, changes?: Record<string, string> }
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
    labels: { accept: string, reject: string, you: string, locale?: string, formats?: Record<string, string>, changes?: Record<string, string> }
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
    /** The text put in, the caret at that offset. */
    replaceText: (text: string, at: number) => void
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
        // As the editor holds text: LF, no byte order mark - a baseline taken from a CRLF file otherwise differed on every line.
        state.baseline = (p.baseline ?? state.baseline).replace(/^\ufeff/, '').replace(/\r\n?/g, '\n')
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

// The page's clock for revisions; a test moves it on (window.__typedownReviewClockOffset, ms) instead of waiting.
const now = () => Date.now() + (Number((window as any).__typedownReviewClockOffset) || 0)

/** Whether the caret was put elsewhere before the next change (the page's EditReport): typing on starts a new revision. */
let nextMoved = false

export function noteChangeMoved(moved: boolean) {
    nextMoved = moved
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
    const time = now()
    const moved = nextMoved
    nextMoved = false
    const previous = state.pending ?? state.revisions
    const region = state.lastText === null || state.pending ? null : changedRegion(state.lastText, text)
    const source = nextSource ?? YOU
    state.revisions = trackRevisions(state.baseline, text, previous, region ? { ...region, source, time, moved } : { source: state.fallback, time })
    absorbWriting(text, region, time)
    groupFormats(state.revisions, text, region)
    state.pending = null
    state.lastText = text
    redraw()
    scheduleReport()
}

function absorbWriting(text: string, region: { from: number, to: number } | null, time: number) {
    // Only the writing changed where the change did not touch (Muya numbered a list on, wrote a fence its way): no revision
    // - taken into the baseline, so it cannot be rejected either (the user's decision, 2026-11-06).
    // The reader's own too (a list marker changed by hand): the user's decision is that writing is not a revision. Not a
    // format's marks (Ctrl+B's **), which change how the words read: those are a revision, grouped (groupFormats).
    // Writing the change itself put in or took out (Enter making an empty list item) stays a revision: it is the reader's
    // doing; only what it rewrote (a number, a mark) goes.
    const touched = (r: Revision) => !!region && r.curStart <= region.to && region.from <= r.curEnd
    // (A rewriting inside one line - a table's cells padded - goes even where the change touched it; a line the change
    // put in or took out stays.)
    // Nor one right against a revision of words (the space typed before a word is part of what was typed).
    const writingOnly = state.revisions.map(r => onlyWriting(state.baseline, text, r) && !isFormatMark(r))
    const besideWords = (i: number) => [i - 1, i + 1].some(j => j >= 0 && j < state.revisions.length && !writingOnly[j] &&
        (state.revisions[j].curEnd === state.revisions[i].curStart || state.revisions[i].curEnd === state.revisions[j].curStart))
    // A mark put for a mark of its kind (a number for a number, a fence's ~ for `, a list's * for -) - not a marker
    // made spaces (Backspace at an item's start, the item no longer one) - is writing even where the change touched it.
    const kind = (t: string) => /^\d+$/.test(t) ? 'n' : /^[`~]+$/.test(t) ? 'f' : /^[*+-]$/.test(t) ? 'l' : /^[=-]+$/.test(t) ? 'u' : /^[ \t]+$/.test(t) ? 'w' : null
    const sameKindOfMark = (r: Revision) => r.kind === 'replace' && !!kind(r.oldText) && kind(r.oldText) === kind(r.newText)
    // Nor one at the caret: a space just typed is the reader's (the word after it comes with the next key).
    const caret = view?.caretOffset() ?? (region ? region.to : -1)
    const atCaret = (r: Revision) => r.kind !== 'replace' && r.curStart <= caret && caret <= r.curEnd
    // An ordered list's number moved on (an item put in above it) is writing even beside the item put in.
    const listNumber = (r: Revision) => r.kind === 'replace' && kind(r.oldText) === 'n' && kind(r.newText) === 'n' &&
        /^[ \t]*(?:>[ \t]?)*[ \t]*\d*$/.test(text.slice(text.lastIndexOf('\n', r.curStart - 1) + 1, r.curStart)) && /^\d*[.)]/.test(text.slice(r.curEnd))
    const writing = state.revisions.filter((r, i) => writingOnly[i] && (listNumber(r) ||
        !touched(r) || ((sameKindOfMark(r) || (r.kind !== 'replace' && !(r.newText + r.oldText).includes('\n'))) && !besideWords(i) && !atCaret(r))))
    if (writing.length) {
        state.baseline = acceptAll(state.baseline, writing)
        state.baselineChanged = true
        state.revisions = trackRevisions(state.baseline, text, shiftAfterAccept(state.revisions, writing), { source: state.fallback, time })
    }
}

const shown = () => !state.show ? [] : state.filter === null ? state.revisions : state.revisions.filter(r => r.source === state.filter)

function redraw() {
    if (!view) return
    if (!state.on) { view.clear(); return }
    const text = state.lastText ?? host?.text() ?? ''
    // What a revision changes of the structure (a task ticked, an item indented): drawn at its block's left, said by
    // its buttons.
    for (const r of state.revisions) r.structure = structureChange(state.baseline, text, r) ?? undefined
    view.draw(shown(), text)
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

// With the rest of their groups (a format's two marks).
const withGroups = (revisions: Revision[]) => {
    const groups = new Set(revisions.map(r => r.group).filter(Boolean))
    return groups.size ? state.revisions.filter(r => revisions.includes(r) || (r.group && groups.has(r.group))) : revisions
}

function accept(revisions: Revision[]) {
    revisions = withGroups(revisions)
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
    revisions = withGroups(revisions)
    if (!revisions.length || !host) return
    const text = host.text()
    let next = revisions.length === 1 ? rejectFrom(text, revisions[0]) : rejectAll(text, revisions)
    // An item put in or taken out of a numbered list: the list numbered again its own way (on from its first, all one
    // number), so that rejecting every revision gives the baseline back to the byte (orderedLists.ts).
    for (const r of [...revisions].sort((a, b) => b.curStart - a.curStart)) next = renumberedAt(text, next, r.curStart)
    state.outcomes.push(...revisions.map(r => outcomeOf(r, 'rejected')))
    const ids = new Set(revisions.map(r => r.id))
    // The text without them: the other revisions keep what they are (no change region - nobody new made them).
    const region = changedRegion(text, next)
    state.revisions = trackRevisions(state.baseline, next, state.revisions.filter(r => !ids.has(r.id)), region ? { ...region, source: YOU, time: Date.now() } : { source: YOU, time: Date.now() })
    // The numbers put back (a list numbered on again) are the writing: into the baseline, as at any change.
    absorbWriting(next, region, Date.now())
    state.lastText = next
    hideActions()
    host.replaceText(next, Math.min(...revisions.map(r => r.curStart)))
    redraw()
    reportNow()
}

const byId = (id: string | null) => (id && state.revisions.find(r => r.id === id)) || null

/**
 * The context menu's review items (the 2026-11-11 menu report): the revision under the pointer as the right button went
 * down - its id and its source - and whether the pointer was on a deleted block drawn where it was (a ghost: not the
 * document's text, nothing to edit). Null when review is off.
 */
export function reviewAtTarget(target: Element | null): { revision: string | null, source: string | null, ghost: boolean } | null {
    if (!state.on || !target) return null
    const id = view?.revisionOf(target) ?? null
    const r = byId(id)
    return { revision: r ? r.id : null, source: r?.source ?? null, ghost: !!target.closest('.ag-review-ghost') }
}

/** The menu's Accept this, Reject this, Accept all from this source: on the revision right-clicked. */
export function actOnRevision(id: string, how: 'accept' | 'reject' | 'acceptSource') {
    const r = byId(id)
    if (!state.on || !r) return
    if (how === 'accept') accept([r])
    else if (how === 'reject') reject([r])
    else accept(shown().filter(x => x.source === r.source))
}

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
    // Who made it and when, beside the buttons (the user, 2026-11-06: "同意或拒绝按钮旁边加时间和修改来源").
    const meta = document.createElement('span')
    meta.className = 'td-review-meta'
    actions.append(
        meta,
        button('td-review-accept', '✓', () => { const r = byId(actionsFor); if (r) accept([r]) }),
        button('td-review-reject', '✗', () => { const r = byId(actionsFor); if (r) reject([r]) }),
    )
    document.body.appendChild(actions)
    return actions
}

// The revision the buttons are for is outlined: with several revisions on one line, which one they act on.
const CURRENT = 'td-review-current'

function markCurrent(id: string | null) {
    document.querySelectorAll('.' + CURRENT).forEach(e => e.classList.remove(CURRENT))
    if (!id) return
    const r = byId(id)
    const ids = r?.group ? state.revisions.filter(x => x.group === r.group).map(x => x.id) : [id]
    for (const one of ids)
        document.querySelectorAll(`[data-rid="${one}"], [data-del-above-rid="${one}"], [data-del-below-rid="${one}"]`).forEach(e => e.classList.add(CURRENT))
}

function hideActions() {
    actionsFor = null
    markCurrent(null)
    if (actions) actions.style.display = 'none'
}

/** The buttons at the right end of the revision's line, in the margin beside the text where there is room. */
export function showActionsFor(id: string | null) {
    if (!state.on || !id || !state.show || id === quietUntilOther) return hideActions()
    const rect = view?.rectOf(id)
    if (!rect) return hideActions()
    const el = actionsElement()
    const [meta, accept, reject] = Array.from(el.children) as HTMLElement[]
    // Each revision its own buttons: what they act on said in their tooltips, the revision outlined.
    const r = byId(id)
    const what = r ? (r.newText || r.oldText).replace(/\s+/g, ' ').trim().slice(0, 40) : ''
    accept.title = what ? `${state.labels.accept}: ${what}` : state.labels.accept
    reject.title = what ? `${state.labels.reject}: ${what}` : state.labels.reject
    accept.setAttribute('aria-label', accept.title)
    reject.setAttribute('aria-label', reject.title)
    el.dataset.for = id
    if (r) {
        const who = r.source || state.labels.you
        const format = r.format ? state.labels.formats?.[r.format] ?? r.format : changeLabel(r)
        meta.textContent = [who, format, revisionTime(r.time, state.labels.locale)].filter(Boolean).join(' · ')
        meta.title = [who, new Date(r.time).toLocaleString(state.labels.locale || undefined), r.session ? `#${r.session}` : ''].filter(Boolean).join(' · ')
    } else meta.textContent = ''
    markCurrent(id)
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

// What a revision of the structure, a join or a split is, beside its buttons: the host's words, else English.
const CHANGES: Record<string, string> = {
    check: 'Checked', uncheck: 'Unchecked', indent: 'Indented', outdent: 'Outdented', toList: 'Made a list item',
    toParagraph: 'Made a paragraph', toHeading: 'Made a heading', headingLevel: 'Heading level', quote: 'Quoted',
    unquote: 'Unquoted', structure: 'Structure', join: 'Joined', split: 'Split',
}

function changeLabel(r: Revision): string {
    const name = r.structure ?? (r.kind === 'delete' && isBreakAndMarks(r.oldText) ? 'join' : r.kind === 'insert' && isBreakAndMarks(r.newText) ? 'split' : null)
    return name ? state.labels.changes?.[name] ?? CHANGES[name] ?? name : ''
}

/** The host's words for the page (the deleted blocks' cards, the changes beside the buttons). */
export const reviewLabels = () => state.labels

/**
 * A revision's time as its buttons say it: today's as hours and minutes, this year's with the day, older ones with the
 * year - in the interface's language's way (the host gives its locale). A revision written to again has the last time.
 */
export function revisionTime(time: number, locale?: string, now = new Date()): string {
    const when = new Date(time)
    const loc = locale || undefined
    const hm: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }
    if (when.toDateString() === now.toDateString()) return when.toLocaleTimeString(loc, hm)
    if (when.getFullYear() === now.getFullYear()) return when.toLocaleString(loc, { month: 'numeric', day: 'numeric', ...hm })
    return when.toLocaleString(loc, { year: 'numeric', month: 'numeric', day: 'numeric', ...hm })
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

/** The revisions an export keeps (Word's own revisions), all of them whatever is shown; null when not in review. */
export function revisionsForExport(): Revision[] | null {
    if (!state.on || !state.revisions.length) return null
    if (host && host.text() !== state.lastText) refresh()
    return state.revisions
}

/** For page checks: the state as the page holds it. */
(window as any).__typedownReview = () => ({
    on: state.on, show: state.show, filter: state.filter, baseline: state.baseline,
    revisions: state.revisions.map(r => ({ id: r.id, kind: r.kind, oldText: r.oldText, newText: r.newText, source: r.source, curStart: r.curStart, group: r.group, format: r.format })),
    actionsFor,
    paths: { ...diffPaths },
})
