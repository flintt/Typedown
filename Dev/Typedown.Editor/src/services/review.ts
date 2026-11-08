// Review mode (docs/todo.md, "审阅模式"): the revisions are the differences between the document and its baseline - the
// text as last saved when review began, with the revisions accepted since written into it. Nothing is written into the
// Markdown: the host keeps the baseline and each revision's metadata (who made it, when) in the app's own data.
//
// The differences are worked out afresh after every change (a line diff, then a word diff inside the lines that
// differ; a CJK character counts as a word), and each one is matched with the revisions known before the change, so
// that the source and time of a revision stay with it while the text around it is edited. A revision the change
// touched is the change's; one it did not keeps what it had.

export type RevisionKind = 'insert' | 'delete' | 'replace'

/** A place where the document differs from the baseline, by offsets in both. */
export interface Hunk {
    baseStart: number
    baseEnd: number
    curStart: number
    curEnd: number
}

/** Who made a change: the reader (name '') or an automation client by its program name and session. */
export interface RevisionSource {
    name: string
    session?: string
}

export const YOU: RevisionSource = { name: '' }

export interface Revision extends Hunk {
    id: string
    kind: RevisionKind
    /** The baseline's text here (deleted or replaced). */
    oldText: string
    /** The document's text here (inserted or the replacement). */
    newText: string
    /** The program's name, '' for the reader. */
    source: string
    session?: string
    /** When it was made (ms since 1970). */
    time: number
}

/** What a revision is known by when the host keeps it: its place in the baseline and its text there and now. */
export interface StoredRevision {
    id: string
    kind?: RevisionKind
    baseStart: number
    baseEnd: number
    oldText?: string
    newText: string
    source: string
    session?: string
    time: number
    /** The line it starts on in the document (1-based) and that line's beginning: "the block it is in", for the record. */
    line?: number
    block?: string
}

// ---- tokens ----

// A word of letters or digits; any CJK character (and kana, hangul) alone; a run of spaces; a line break; any other
// character alone. Words make a replaced word one revision instead of a few letters of it.
const TOKEN = /[぀-ヿ㐀-䶿一-鿿豈-﫿가-힯]|[\p{L}\p{N}_]+|[ \t]+|\n|[\s\S]/gu

export function tokenize(text: string): string[] {
    return text.match(TOKEN) ?? []
}

// ---- the diff ----

type Op = { a: number, b: number, aLen: number, bLen: number }

/**
 * The edits turning a into b (Myers' algorithm, the common ends taken off first), as runs: aLen items of a at a replaced
 * by bLen items of b at b. Equal items compare with ===. Gives up past maxCost differences and reports the whole middle
 * as one replacement.
 */
export function diffSequences<T>(a: T[], b: T[], maxCost = 4000): Op[] {
    let start = 0
    while (start < a.length && start < b.length && a[start] === b[start]) start++
    let endA = a.length, endB = b.length
    while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) { endA--; endB-- }
    const n = endA - start, m = endB - start
    if (n === 0 && m === 0) return []
    if (n === 0 || m === 0) return [{ a: start, b: start, aLen: n, bLen: m }]
    const max = Math.min(n + m, maxCost)
    const offset = max + 1
    const v = new Int32Array(2 * max + 3)
    const trace: Int32Array[] = []
    let found = -1
    for (let d = 0; d <= max && found < 0; d++) {
        trace.push(v.slice())
        for (let k = -d; k <= d; k += 2) {
            let x = k === -d || (k !== d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1
            let y = x - k
            while (x < n && y < m && a[start + x] === b[start + y]) { x++; y++ }
            v[offset + k] = x
            if (x >= n && y >= m) { found = d; break }
        }
    }
    if (found < 0) return [{ a: start, b: start, aLen: n, bLen: m }]
    // Back through the trace: each step one deletion or insertion, the equal runs between them skipped.
    const steps: { a: number, b: number, del: boolean }[] = []
    let x = n, y = m
    for (let d = found; d > 0; d--) {
        const vd = trace[d]
        const k = x - y
        const down = k === -d || (k !== d && vd[offset + k - 1] < vd[offset + k + 1])
        const prevK = down ? k + 1 : k - 1
        const prevX = vd[offset + prevK]
        const prevY = prevX - prevK
        while (x > prevX + (down ? 0 : 1) && y > prevY + (down ? 1 : 0)) { x--; y-- }
        if (down) { y--; steps.push({ a: x, b: y, del: false }) }
        else { x--; steps.push({ a: x, b: y, del: true }) }
        x = prevX
        y = prevY
    }
    steps.reverse()
    // Runs of adjacent steps into one edit each.
    const ops: Op[] = []
    for (const s of steps) {
        const last = ops[ops.length - 1]
        if (last && last.a + last.aLen === s.a && last.b + last.bLen === s.b) {
            if (s.del) last.aLen++
            else last.bLen++
        } else ops.push({ a: s.a, b: s.b, aLen: s.del ? 1 : 0, bLen: s.del ? 0 : 1 })
    }
    return ops.map(o => ({ a: o.a + start, b: o.b + start, aLen: o.aLen, bLen: o.bLen }))
}

const lineStarts = (text: string) => {
    const starts = [0]
    for (let i = text.indexOf('\n'); i >= 0; i = text.indexOf('\n', i + 1)) starts.push(i + 1)
    return starts
}

const splitLines = (text: string) => {
    // Each line with its line break, so that the lines joined are the text.
    const lines = text.split(/(?<=\n)/)
    return lines.length === 1 && lines[0] === '' ? [] : lines
}

/**
 * Where current differs from baseline: the lines that differ, then within them the words. Pure insertions and
 * deletions of whole lines are moved to begin at a line's start where they can, so that a paragraph added between two
 * others is that paragraph and its line break, not the break before it and the paragraph.
 */
export function diffTexts(baseline: string, current: string): Hunk[] {
    if (baseline === current) return []
    const aLines = splitLines(baseline), bLines = splitLines(current)
    const aStarts = lineStarts(baseline), bStarts = lineStarts(current)
    const hunks: Hunk[] = []
    for (const op of diffSequences(aLines, bLines)) {
        const aFrom = aStarts[op.a] ?? baseline.length, aTo = op.a + op.aLen < aStarts.length ? aStarts[op.a + op.aLen] : baseline.length
        const bFrom = bStarts[op.b] ?? current.length, bTo = op.b + op.bLen < bStarts.length ? bStarts[op.b + op.bLen] : current.length
        if (op.aLen === 0 || op.bLen === 0) {
            hunks.push({ baseStart: aFrom, baseEnd: aTo, curStart: bFrom, curEnd: bTo })
            continue
        }
        // Lines changed: each line paired with the one it most resembles on the other side, the words of a pair
        // compared; a line paired with none was added or deleted whole. Compared as one run of words, a paragraph
        // edited next to one deleted had its words matched with the deleted one's ("paragraph" with "paragraph").
        const aGroup = aLines.slice(op.a, op.a + op.aLen), bGroup = bLines.slice(op.b, op.b + op.bLen)
        const steps = aGroup.length * bGroup.length <= 40000 ? pairLines(aGroup, bGroup) : null
        if (!steps) {
            wordHunks(hunks, baseline.slice(aFrom, aTo), current.slice(bFrom, bTo), aFrom, bFrom)
            continue
        }
        let a = aFrom, b = bFrom
        let run: Hunk | null = null
        const flush = () => { if (run) hunks.push(run); run = null }
        for (const step of steps) {
            if (step === 'pair') {
                flush()
                const aLine = aGroup.shift()!, bLine = bGroup.shift()!
                wordHunks(hunks, aLine, bLine, a, b)
                a += aLine.length
                b += bLine.length
            } else if (step === 'del') {
                const aLine = aGroup.shift()!
                if (run && run.curStart === run.curEnd) run.baseEnd += aLine.length
                else { flush(); run = { baseStart: a, baseEnd: a + aLine.length, curStart: b, curEnd: b } }
                a += aLine.length
            } else {
                const bLine = bGroup.shift()!
                if (run && run.baseStart === run.baseEnd) run.curEnd += bLine.length
                else { flush(); run = { baseStart: a, baseEnd: a, curStart: b, curEnd: b + bLine.length } }
                b += bLine.length
            }
        }
        flush()
    }
    return hunks.map(h => slide(baseline, current, h))
}

// The words of two texts compared, the hunks at their offsets.
function wordHunks(hunks: Hunk[], aText: string, bText: string, aFrom: number, bFrom: number) {
    const aTokens = tokenize(aText), bTokens = tokenize(bText)
    const aPos = positions(aTokens), bPos = positions(bTokens)
    for (const t of diffSequences(aTokens, bTokens, 2000))
        hunks.push({ baseStart: aFrom + aPos[t.a], baseEnd: aFrom + aPos[t.a + t.aLen], curStart: bFrom + bPos[t.b], curEnd: bFrom + bPos[t.b + t.bLen] })
}

// How alike two lines are, 0 to 1: the share of their words (not spaces) they have in common. Two blank lines are alike.
function similarity(a: string, b: string): number {
    const words = (s: string) => tokenize(s).filter(t => /\S/.test(t))
    const x = words(a), y = words(b)
    if (!x.length && !y.length) return 1
    if (!x.length || !y.length) return 0
    const counts = new Map<string, number>()
    for (const t of x) counts.set(t, (counts.get(t) ?? 0) + 1)
    let common = 0
    for (const t of y) {
        const n = counts.get(t) ?? 0
        if (n > 0) { common++; counts.set(t, n - 1) }
    }
    return 2 * common / (x.length + y.length)
}

/**
 * The lines of two runs paired in order (each pair alike enough), the others added or deleted: the cheapest way, a pair
 * costing how unlike its lines are and a line on its own costing one.
 */
function pairLines(a: string[], b: string[]): ('pair' | 'del' | 'ins')[] {
    const n = a.length, m = b.length
    const cost = new Float64Array((n + 1) * (m + 1))
    const at = (i: number, j: number) => i * (m + 1) + j
    const alike = (i: number, j: number) => similarity(a[i], b[j])
    const sims = new Float64Array(n * m)
    for (let i = 0; i < n; i++) for (let j = 0; j < m; j++) sims[i * m + j] = alike(i, j)
    for (let i = n; i >= 0; i--) for (let j = m; j >= 0; j--) {
        if (i === n && j === m) continue
        let best = Infinity
        if (i < n) best = Math.min(best, 1 + cost[at(i + 1, j)])
        if (j < m) best = Math.min(best, 1 + cost[at(i, j + 1)])
        if (i < n && j < m && sims[i * m + j] >= 0.3) best = Math.min(best, 2 * (1 - sims[i * m + j]) + cost[at(i + 1, j + 1)])
        cost[at(i, j)] = best
    }
    const steps: ('pair' | 'del' | 'ins')[] = []
    let i = 0, j = 0
    while (i < n || j < m) {
        const here = cost[at(i, j)]
        if (i < n && j < m && sims[i * m + j] >= 0.3 && Math.abs(here - (2 * (1 - sims[i * m + j]) + cost[at(i + 1, j + 1)])) < 1e-9) { steps.push('pair'); i++; j++ }
        else if (i < n && Math.abs(here - (1 + cost[at(i + 1, j)])) < 1e-9) { steps.push('del'); i++ }
        else { steps.push('ins'); j++ }
    }
    return steps
}

const positions = (tokens: string[]) => {
    const pos = [0]
    for (const t of tokens) pos.push(pos[pos.length - 1] + t.length)
    return pos
}

// A pure insertion or deletion that could as well sit a few characters earlier (its text ends as the text before it
// does) is moved there when that puts its start at a line's start: "a\n\n" + "b\n\n" reads as the paragraph added.
function slide(baseline: string, current: string, h: Hunk): Hunk {
    const insert = h.baseStart === h.baseEnd
    const remove = h.curStart === h.curEnd
    if (insert === remove) return h
    const text = insert ? current : baseline
    const start = insert ? h.curStart : h.baseStart, end = insert ? h.curEnd : h.baseEnd
    const atLineStart = (i: number) => i === 0 || text[i - 1] === '\n'
    if (atLineStart(start)) return h
    let s = start, e = end
    for (let i = 0; i < 200 && s > 0 && text[s - 1] === text[e - 1]; i++) {
        s--; e--
        if (atLineStart(s)) {
            const shift = start - s
            return insert ? { ...h, baseStart: h.baseStart - shift, baseEnd: h.baseEnd - shift, curStart: s, curEnd: e }
                : { ...h, baseStart: s, baseEnd: e, curStart: h.curStart - shift, curEnd: h.curEnd - shift }
        }
    }
    return h
}

// ---- revisions ----

export function kindOf(h: Hunk): RevisionKind {
    return h.baseStart === h.baseEnd ? 'insert' : h.curStart === h.curEnd ? 'delete' : 'replace'
}

let idCounter = 0
export const newRevisionId = () => `r${Date.now().toString(36)}${(idCounter++).toString(36)}`

/** What a text change was: where it is in the new text, who made it, and when. */
export interface ChangeInfo {
    /** The changed region of the new text (offsets); none: a change of nothing (the baseline moved, a load). */
    from?: number
    to?: number
    source: RevisionSource
    time: number
}

const touches = (aStart: number, aEnd: number, bStart: number, bEnd: number) => aStart <= bEnd && bStart <= aEnd

/** The region of `after` that differs from `before`: what an edit changed. */
export function changedRegion(before: string, after: string): { from: number, to: number } | null {
    if (before === after) return null
    let start = 0
    const max = Math.min(before.length, after.length)
    while (start < max && before.charCodeAt(start) === after.charCodeAt(start)) start++
    let endB = before.length, endA = after.length
    while (endB > start && endA > start && before.charCodeAt(endB - 1) === after.charCodeAt(endA - 1)) { endB--; endA-- }
    return { from: start, to: endA }
}

/**
 * The revisions between baseline and current, each matched with those known before (`previous`, by their baseline
 * places): one at the same place with the same text is the same revision; one the change touched is the change's
 * (keeping the id of a revision of the same source it grew from); any other takes over the one it overlaps, or else is
 * the change's.
 */
export function trackRevisions(baseline: string, current: string, previous: (Revision | StoredRevision)[], change: ChangeInfo): Revision[] {
    const hunks = diffTexts(baseline, current)
    const used = new Set<string>()
    const exact = new Map<string, Revision | StoredRevision>()
    for (const p of previous) exact.set(`${p.baseStart}:${p.baseEnd}:${p.newText}`, p)
    return hunks.map(h => {
        const newText = current.slice(h.curStart, h.curEnd)
        const oldText = baseline.slice(h.baseStart, h.baseEnd)
        const make = (from: Revision | StoredRevision | null, source: RevisionSource, time: number): Revision => {
            const id = from && !used.has(from.id) ? from.id : newRevisionId()
            used.add(id)
            return { ...h, id, kind: kindOf(h), oldText, newText, source: source.name, session: source.session, time }
        }
        const same = exact.get(`${h.baseStart}:${h.baseEnd}:${newText}`)
        if (same && !used.has(same.id)) return make(same, { name: same.source, session: same.session }, same.time)
        const overlapping = previous.filter(p => touches(p.baseStart, p.baseEnd, h.baseStart, h.baseEnd) && !used.has(p.id))
        const touched = change.from !== undefined && change.to !== undefined && touches(h.curStart, h.curEnd, change.from, change.to)
        if (touched) {
            const grown = overlapping.find(p => p.source === change.source.name && (p.session ?? '') === (change.source.session ?? ''))
            return make(grown ?? null, change.source, change.time)
        }
        if (overlapping.length) {
            const p = overlapping[0]
            return make(p, { name: p.source, session: p.session }, p.time)
        }
        return make(null, change.source, change.time)
    })
}

/** The baseline with the revision written in (accepted): the revisions after it move by the difference. */
export function acceptInto(baseline: string, revision: Hunk & { newText: string }): string {
    return baseline.slice(0, revision.baseStart) + revision.newText + baseline.slice(revision.baseEnd)
}

/** The document with the revision taken back (rejected): its place has the baseline's text again. */
export function rejectFrom(current: string, revision: Hunk & { oldText: string }): string {
    return current.slice(0, revision.curStart) + revision.oldText + current.slice(revision.curEnd)
}

/**
 * The previous revisions with their baseline places moved as accepting `accepted` moves them: for matching after the
 * baseline changed. Those before it stay, those after it shift by how much longer the baseline became.
 */
export function shiftAfterAccept(revisions: Revision[], accepted: Revision[]): Revision[] {
    const sorted = [...accepted].sort((a, b) => a.baseStart - b.baseStart)
    const ids = new Set(sorted.map(r => r.id))
    return revisions.filter(r => !ids.has(r.id)).map(r => {
        let shift = 0
        for (const a of sorted) if (a.baseEnd <= r.baseStart) shift += a.newText.length - (a.baseEnd - a.baseStart)
        return { ...r, baseStart: r.baseStart + shift, baseEnd: r.baseEnd + shift }
    })
}

/** Accepts the given revisions at once: the new baseline. */
export function acceptAll(baseline: string, revisions: Revision[]): string {
    let text = baseline
    for (const r of [...revisions].sort((a, b) => b.baseStart - a.baseStart)) text = acceptInto(text, r)
    return text
}

/** Rejects the given revisions at once: the new document text. */
export function rejectAll(current: string, revisions: Revision[]): string {
    let text = current
    for (const r of [...revisions].sort((a, b) => b.curStart - a.curStart)) text = rejectFrom(text, r)
    return text
}

/** The revisions counted for the status bar: how many add text, how many take text away, and by source. */
export function countRevisions(revisions: Revision[]) {
    const sources = new Map<string, number>()
    let inserted = 0, deleted = 0
    for (const r of revisions) {
        if (r.newText) inserted++
        if (r.oldText) deleted++
        sources.set(r.source, (sources.get(r.source) ?? 0) + 1)
    }
    return { inserted, deleted, sources: [...sources.entries()].map(([name, count]) => ({ name, count })) }
}

/** A revision as the host keeps it, with the line it is on and that line's start for the record. */
export function storedOf(r: Revision, current: string): StoredRevision {
    const lineStart = current.lastIndexOf('\n', r.curStart - 1) + 1
    const lineEnd = current.indexOf('\n', r.curStart)
    let line = 1
    for (let i = current.indexOf('\n'); i >= 0 && i < r.curStart; i = current.indexOf('\n', i + 1)) line++
    return {
        id: r.id, kind: r.kind, baseStart: r.baseStart, baseEnd: r.baseEnd, oldText: r.oldText, newText: r.newText,
        source: r.source, session: r.session, time: r.time, line,
        block: current.slice(lineStart, lineEnd < 0 ? current.length : lineEnd).slice(0, 60),
    }
}

/** The revision at a place in the document (the caret's): one whose text holds it, or a deletion right there. */
export function revisionAt(revisions: Revision[], offset: number): Revision | null {
    return revisions.find(r => r.curStart <= offset && offset <= r.curEnd) ?? null
}

/** The next revision after the offset (or the previous before it), wrapping round. */
export function nextRevision(revisions: Revision[], offset: number, back = false): Revision | null {
    if (!revisions.length) return null
    const sorted = [...revisions].sort((a, b) => a.curStart - b.curStart)
    if (back) return [...sorted].reverse().find(r => r.curStart < offset) ?? sorted[sorted.length - 1]
    return sorted.find(r => r.curStart > offset) ?? sorted[0]
}
