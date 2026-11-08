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
    /**
     * One format the reader applied (the two ** of a bold made with Ctrl+B): the id of the group's first revision, and
     * the mark - accepted, rejected and counted together.
     */
    group?: string
    format?: string
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
    /** Where its text is in the document as reported (matched again only where that text still is). */
    curStart?: number
    curEnd?: number
    group?: string
    format?: string
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
 * Which ways the last diffTexts took, for the page checks (Tools/EditorBench/review-matrix.js): lines paired and
 * compared by words, lines taken as deleted or added whole, groups too big to pair compared as one run of words, and
 * comparisons given up past their cost (the whole middle one replacement).
 */
export const diffPaths = { local: 0, pairs: 0, wholeLines: 0, unpairedGroups: 0, gaveUp: 0 }

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
        // V as it was, only the diagonals this step can read (all of it each step took 64 MB at 2,000 steps).
        trace.push(v.slice(offset - d - 1, offset + d + 2))
        for (let k = -d; k <= d; k += 2) {
            let x = k === -d || (k !== d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1
            let y = x - k
            while (x < n && y < m && a[start + x] === b[start + y]) { x++; y++ }
            v[offset + k] = x
            if (x >= n && y >= m) { found = d; break }
        }
    }
    if (found < 0) { diffPaths.gaveUp++; return [{ a: start, b: start, aLen: n, bLen: m }] }
    // Back through the trace: each step one deletion or insertion, the equal runs between them skipped.
    const steps: { a: number, b: number, del: boolean }[] = []
    let x = n, y = m
    for (let d = found; d > 0; d--) {
        const vd = trace[d]
        const o = d + 1
        const k = x - y
        const down = k === -d || (k !== d && vd[o + k - 1] < vd[o + k + 1])
        const prevK = down ? k + 1 : k - 1
        const prevX = vd[o + prevK]
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
    diffPaths.local = diffPaths.pairs = diffPaths.wholeLines = diffPaths.unpairedGroups = diffPaths.gaveUp = 0
    if (baseline === current) return []
    // The words of everything between what the two have in common at their ends, the fewest words changed: fast while
    // few differ, however long the document (Myers: the length times the words changed). Line by line first, a group of
    // lines was compared apart from the rest - blank lines matched across paragraphs, a letter typed marked its word,
    // Enter deleted a paragraph and added two (review-matrix.js, 2026-11-06). Only when too much differs for that, line
    // by line (diffByLines).
    // The fewest words is not always what reads best: a paragraph edited beside one deleted had the deleted one's words
    // serve the edited one's ("paragraph" kept, ".\n\nThird" made ","). So line by line too (each group of changed lines
    // its best way), and the one marking less taken, a few characters counted for each hunk: one clean deletion beats a
    // few scraps.
    const lines = diffByLines(baseline, current)
    const linePaths = { ...diffPaths }
    const [pre, suf] = wordEnds(baseline, current)
    const words: Hunk[] = []
    const gaveUp = diffPaths.gaveUp
    wordHunks(words, baseline.slice(pre, baseline.length - suf), current.slice(pre, current.length - suf), pre, pre, 3000)
    const cost = (list: Hunk[]) => hunksCost(baseline, current, list)
    // Line by line only where it marks hardly more than the fewest words would (it reads better, it must not mark more).
    const marked = (list: Hunk[]) => list.reduce((n, h) => n + (h.baseEnd - h.baseStart) + (h.curEnd - h.curStart), 0)
    if (diffPaths.gaveUp === gaveUp && (cost(words) <= cost(lines) || marked(lines) > marked(words) + 2)) {
        Object.assign(diffPaths, { local: 1, pairs: 0, wholeLines: 0, unpairedGroups: 0 })
        return words.map(h => slide(baseline, current, h))
    }
    Object.assign(diffPaths, linePaths)
    return lines
}

/**
 * How much a way of telling the differences marks: its characters, a few more for each hunk (one clean deletion beats a
 * few scraps), and more for one that takes a line break with part of a line ("." and the next paragraph's first word made
 * "," - a paragraph deleted, read across two).
 */
function hunksCost(baseline: string, current: string, list: Hunk[]): number {
    let n = 0
    for (const h of list) {
        const a = baseline.slice(h.baseStart, h.baseEnd), b = current.slice(h.curStart, h.curEnd)
        n += a.length + b.length + 3
        const across = (t: string) => t.includes('\n') && /[^\n]/.test(t.replace(/^[^\n]*\n/, '').replace(/\n[^\n]*$/, '')) === false && /[^\n]/.test(t) && !/^\n|\n$/.test(t)
        if ((a.includes('\n') && b.length > 0 && /\S/.test(b)) || (b.includes('\n') && a.length > 0 && /\S/.test(a)) || across(a) || across(b)) n += 10
    }
    return n
}

/** diffTexts line by line, for texts too different to compare word by word at once. */
function diffByLines(baseline: string, current: string): Hunk[] {
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
        // Lines changed, two ways, the one marking less taken (each hunk counting a little, so that one clean deletion
        // beats a few scraps): (1) what the two sides do not have in common at their ends, its words compared - one edit
        // in one place (a letter typed, a word, Enter splitting a paragraph, a format) is exactly that; (2) each line
        // paired with the one it most resembles, the words of a pair compared, a line paired with none added or deleted
        // whole - several edits apart, or a paragraph edited beside one deleted. Line by line alone, a letter typed in a
        // word marked the word and Enter in a paragraph deleted it and added two (review-matrix.js, 2026-11-06).
        const aText = baseline.slice(aFrom, aTo), bText = current.slice(bFrom, bTo)
        const [pre, suf] = commonEnds(aText, bText)
        let local: Hunk[] | null = null
        if (aText.length - pre - suf <= LOCAL_EDIT && bText.length - pre - suf <= LOCAL_EDIT) {
            local = []
            wordHunks(local, aText.slice(pre, aText.length - suf), bText.slice(pre, bText.length - suf), aFrom + pre, bFrom + pre)
        }
        const paired = pairedHunks(aLines.slice(op.a, op.a + op.aLen), bLines.slice(op.b, op.b + op.bLen), aText, bText, aFrom, bFrom)
        const cost = (list: Hunk[]) => hunksCost(baseline, current, list)
        if (local && cost(local) <= cost(paired.hunks)) {
            diffPaths.local++
            hunks.push(...local)
        } else {
            diffPaths.pairs += paired.pairs
            diffPaths.wholeLines += paired.whole
            if (paired.unpaired) diffPaths.unpairedGroups++
            hunks.push(...paired.hunks)
        }
    }
    return hunks.map(h => slide(baseline, current, h))
}

// A group of changed lines compared line by line (diffTexts' second way): the hunks, and how many lines were paired and
// taken whole; too many to pair, its words compared as one run (unpaired).
function pairedHunks(aGroup: string[], bGroup: string[], aText: string, bText: string, aFrom: number, bFrom: number) {
    const hunks: Hunk[] = []
    let pairs = 0, whole = 0
    const steps = aGroup.length * bGroup.length <= 40000 ? pairLines(aGroup, bGroup) : null
    if (!steps) {
        wordHunks(hunks, aText, bText, aFrom, bFrom)
        return { hunks, pairs, whole, unpaired: true }
    }
    aGroup = [...aGroup]
    bGroup = [...bGroup]
    let a = aFrom, b = bFrom
    let run: Hunk | null = null
    const flush = () => { if (run) hunks.push(run); run = null }
    for (const step of steps) {
        if (step === 'pair') {
            pairs++
            flush()
            const aLine = aGroup.shift()!, bLine = bGroup.shift()!
            wordHunks(hunks, aLine, bLine, a, b)
            a += aLine.length
            b += bLine.length
        } else if (step === 'del') {
            whole++
            const aLine = aGroup.shift()!
            if (run && run.curStart === run.curEnd) run.baseEnd += aLine.length
            else { flush(); run = { baseStart: a, baseEnd: a + aLine.length, curStart: b, curEnd: b } }
            a += aLine.length
        } else {
            whole++
            const bLine = bGroup.shift()!
            if (run && run.baseStart === run.baseEnd) run.curEnd += bLine.length
            else { flush(); run = { baseStart: a, baseEnd: a, curStart: b, curEnd: b + bLine.length } }
            b += bLine.length
        }
    }
    flush()
    return { hunks, pairs, whole, unpaired: false }
}

// The words of two texts compared, the hunks at their offsets.
function wordHunks(hunks: Hunk[], aText: string, bText: string, aFrom: number, bFrom: number, maxCost = 2000) {
    const aTokens = tokenize(aText), bTokens = tokenize(bText)
    const aPos = positions(aTokens), bPos = positions(bTokens)
    const ops = diffSequences(aTokens, bTokens, maxCost)
    // Words changed only a space or a mark apart are looked at together as well ("# review" to "eview": the "# r" gone,
    // not "#" made "eview" and "review " deleted - the same number of words).
    for (let i = 0; i < ops.length;) {
        let j = i
        while (j + 1 < ops.length && ops[j + 1].a - (ops[j].a + ops[j].aLen) <= 2 && aPos[ops[j + 1].a] - aPos[ops[j].a + ops[j].aLen] <= 3) j++
        const one = (t: Op) => letters(aText, bText, aPos[t.a], aPos[t.a + t.aLen], bPos[t.b], bPos[t.b + t.bLen])
        const apart = ops.slice(i, j + 1).flatMap(one)
        const together = j > i ? letters(aText, bText, aPos[ops[i].a], aPos[ops[j].a + ops[j].aLen], bPos[ops[i].b], bPos[ops[j].b + ops[j].bLen]) : null
        const size = (list: Hunk[]) => list.reduce((n, h) => n + h.baseEnd - h.baseStart + h.curEnd - h.curStart, 0)
        for (const h of together && size(together) < size(apart) ? together : apart)
            hunks.push({ baseStart: aFrom + h.baseStart, baseEnd: aFrom + h.baseEnd, curStart: bFrom + h.curStart, curEnd: bFrom + h.curEnd })
        i = j + 1
    }
}

/**
 * aText[aS, aE) made bText[bS, bE): its ends in common taken off, then by letters where that marks at most three
 * quarters as much ("paragraph" to "paraZgraph" is a "Z" added; a word put for a wholly different one stays one
 * replacement). Offsets in the two texts.
 */
function letters(aText: string, bText: string, aS: number, aE: number, bS: number, bE: number): Hunk[] {
    const [pre, suf] = commonEnds(aText.slice(aS, aE), bText.slice(bS, bE))
    const h = { baseStart: aS + pre, baseEnd: aE - suf, curStart: bS + pre, curEnd: bE - suf }
    const x = aText.slice(h.baseStart, h.baseEnd), y = bText.slice(h.curStart, h.curEnd)
    if (x && y && x.length <= 160 && y.length <= 160) {
        const ops = diffSequences([...x], [...y])
        const offsets = (str: string) => { const o = [0]; for (const ch of str) o.push(o[o.length - 1] + ch.length); return o }
        const xo = offsets(x), yo = offsets(y)
        const marked = ops.reduce((n, o) => n + (xo[o.a + o.aLen] - xo[o.a]) + (yo[o.b + o.bLen] - yo[o.b]), 0)
        // And in one or two places only: "Body" made "Word" stays one replacement, not three scraps of letters.
        if ((ops.length <= 2 && marked * 4 <= (x.length + y.length) * 3) || marked * 2 < x.length + y.length)
            return ops.map(o => ({ baseStart: h.baseStart + xo[o.a], baseEnd: h.baseStart + xo[o.a + o.aLen], curStart: h.curStart + yo[o.b], curEnd: h.curStart + yo[o.b + o.bLen] }))
    }
    return [h]
}

// A letter of a word (not CJK, whose characters are words of their own).
const WORD_CHAR = /[\p{L}\p{N}_]/u
const CJK_CHAR = /[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uac00-\ud7af]/
const inWord = (t: string, i: number) => i > 0 && i < t.length && WORD_CHAR.test(t[i - 1]) && WORD_CHAR.test(t[i]) && !CJK_CHAR.test(t[i - 1]) && !CJK_CHAR.test(t[i])

/**
 * commonEnds moved back to the edges of words: the words around an edit are compared whole (and then by letters), so
 * "gamma" and "delta" sharing their last "a" does not cut both (" delta" added, not "a delt").
 */
function wordEnds(a: string, b: string): [number, number] {
    let [pre, suf] = commonEnds(a, b)
    while (pre > 0 && (inWord(a, pre) || inWord(b, pre))) pre--
    while (suf > 0 && (inWord(a, a.length - suf) || inWord(b, b.length - suf))) suf--
    return [pre, suf]
}

/** A change in one place as long as this (on either side) is compared on its own, not line by line. */
const LOCAL_EDIT = 400

const lowSurrogate = (c: number) => c >= 0xdc00 && c <= 0xdfff

/** How many characters two texts have in common at their start and at their end (not overlapping, never half a pair). */
export function commonEnds(a: string, b: string): [number, number] {
    const max = Math.min(a.length, b.length)
    let pre = 0
    while (pre < max && a.charCodeAt(pre) === b.charCodeAt(pre)) pre++
    if (pre > 0 && pre < max && lowSurrogate(a.charCodeAt(pre))) pre--
    let suf = 0
    while (suf < max - pre && a.charCodeAt(a.length - 1 - suf) === b.charCodeAt(b.length - 1 - suf)) suf++
    if (suf > 0 && lowSurrogate(a.charCodeAt(a.length - suf))) suf--
    return [pre, suf]
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
    /** Where the changed region ended in the text before (to when not given: a change of the same length). */
    oldTo?: number
    source: RevisionSource
    time: number
}

const touches = (aStart: number, aEnd: number, bStart: number, bEnd: number) => aStart <= bEnd && bStart <= aEnd

/** The region of `after` that differs from `before`: what an edit changed (`oldTo`: where it ended in `before`). */
export function changedRegion(before: string, after: string): { from: number, to: number, oldTo: number } | null {
    if (before === after) return null
    let start = 0
    const max = Math.min(before.length, after.length)
    while (start < max && before.charCodeAt(start) === after.charCodeAt(start)) start++
    let endB = before.length, endA = after.length
    while (endB > start && endA > start && before.charCodeAt(endB - 1) === after.charCodeAt(endA - 1)) { endB--; endA-- }
    return { from: start, to: endA, oldTo: endB }
}

type Known = Revision | StoredRevision

/**
 * Where the text of a revision known before is in the current text: its old place moved over the change (the part the
 * change replaced is not its any more). A stored one (the host's, after a load) is where it says, if its text is there.
 */
function placesOf(p: Known, current: string, region: { from: number, to: number, oldTo: number } | null): { start: number, end: number }[] {
    if (typeof p.curStart !== 'number' || typeof p.curEnd !== 'number' || p.curEnd <= p.curStart) return []
    if (!region) {
        // Not moved: only where its text is.
        return current.slice(p.curStart, p.curEnd) === p.newText ? [{ start: p.curStart, end: p.curEnd }] : []
    }
    const delta = region.to - region.oldTo
    const out: { start: number, end: number }[] = []
    if (p.curStart < region.from) out.push({ start: p.curStart, end: Math.min(p.curEnd, region.from) })
    if (p.curEnd > region.oldTo) out.push({ start: Math.max(p.curStart, region.oldTo) + delta, end: p.curEnd + delta })
    return out.filter(x => x.end > x.start)
}

/**
 * The revisions between baseline and current, each matched with those known before (`previous`): what each revision's
 * text is made of is followed through the change, so a hunk of new text is split where its parts are of different
 * sources (the reader typing inside a program's insertion: the program's text stays the program's, the letters typed are
 * the reader's), and a part keeps the id and time of the revision it was. Text the change put in is the change's; text no
 * one is known for, the revision it touches or else the change's. A hunk with no new text (a deletion) is the one at
 * the same place in the baseline, or the change's if the change touched it.
 */
export function trackRevisions(baseline: string, current: string, previous: Known[], change: ChangeInfo): Revision[] {
    const hunks = diffTexts(baseline, current)
    const used = new Set<string>()
    const region = change.from !== undefined && change.to !== undefined ? { from: change.from, to: change.to, oldTo: change.oldTo ?? change.to } : null
    const places = previous.map(p => ({ p, at: placesOf(p, current, region) }))
    const exact = new Map<string, Known>()
    for (const p of previous) exact.set(`${p.baseStart}:${p.baseEnd}:${p.newText}`, p)
    const sameSource = (p: Known, s: RevisionSource) => p.source === s.name && (p.session ?? '') === (s.session ?? '')
    const out: Revision[] = []
    for (const h of hunks) {
        const newText = current.slice(h.curStart, h.curEnd)
        const oldText = baseline.slice(h.baseStart, h.baseEnd)
        const touched = !!region && touches(h.curStart, h.curEnd, region.from, region.to)
        const make = (piece: Hunk, oldPart: string, from: Known | null, source: RevisionSource, time: number): Revision => {
            const id = from && !used.has(from.id) ? from.id : newRevisionId()
            used.add(id)
            const keeps = from && from.id === id && from.group ? { group: from.group, format: from.format } : {}
            return { ...piece, id, kind: kindOf(piece), oldText: oldPart, newText: current.slice(piece.curStart, piece.curEnd), source: source.name, session: source.session, time, ...keeps }
        }
        // The new text, a character at a time: whose it is.
        if (h.curEnd > h.curStart) {
            const owner: (Known | null)[] = new Array(h.curEnd - h.curStart).fill(null)
            let known = false
            for (const { p, at } of places)
                for (const x of at)
                    for (let i = Math.max(x.start, h.curStart); i < Math.min(x.end, h.curEnd); i++) { owner[i - h.curStart] = p; known = true }
            if (known) {
                // Text the change did not put in and no one is known for: the revision next to it (before, else after).
                const isNew = (i: number) => !!region && i >= region.from && i < region.to
                for (let i = 0; i < owner.length; i++) if (!owner[i] && !isNew(h.curStart + i)) owner[i] = owner[i - 1] ?? owner.find(o => !!o) ?? null
                const sourceOf = (o: Known | null): RevisionSource => o ? { name: o.source, session: o.session } : change.source
                // Runs of one source; the first run has the baseline's text this hunk replaces, the others are insertions.
                let start = 0
                let first = true
                for (let i = 1; i <= owner.length; i++) {
                    const a = sourceOf(owner[start]), b = i < owner.length ? sourceOf(owner[i]) : null
                    if (b && a.name === b.name && (a.session ?? '') === (b.session ?? '')) continue
                    const piece: Hunk = first
                        ? { baseStart: h.baseStart, baseEnd: h.baseEnd, curStart: h.curStart + start, curEnd: h.curStart + i }
                        : { baseStart: h.baseEnd, baseEnd: h.baseEnd, curStart: h.curStart + start, curEnd: h.curStart + i }
                    const run = owner.slice(start, i)
                    const was = run.find(o => !!o && !used.has(o.id)) ?? null
                    const grew = run.some(o => !o)
                    out.push(make(piece, first ? oldText : '', was, a, grew || !was ? change.time : was.time))
                    first = false
                    start = i
                }
                continue
            }
        }
        const same = exact.get(`${h.baseStart}:${h.baseEnd}:${newText}`)
        if (same && !used.has(same.id)) { out.push(make(h, oldText, same, { name: same.source, session: same.session }, same.time)); continue }
        const overlapping = previous.filter(p => touches(p.baseStart, p.baseEnd, h.baseStart, h.baseEnd) && !used.has(p.id))
        if (touched) {
            const grown = overlapping.find(p => sameSource(p, change.source)) ?? null
            out.push(make(h, oldText, grown, change.source, change.time))
            continue
        }
        if (overlapping.length) {
            const p = overlapping[0]
            out.push(make(h, oldText, p, { name: p.source, session: p.session }, p.time))
            continue
        }
        out.push(make(h, oldText, null, change.source, change.time))
    }
    return out
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
        // Before it in the baseline, or at the same place and before it in the document (two insertions there).
        for (const a of sorted) if (a.baseEnd < r.baseStart || (a.baseEnd === r.baseStart && a.curEnd <= r.curStart)) shift += a.newText.length - (a.baseEnd - a.baseStart)
        return { ...r, baseStart: r.baseStart + shift, baseEnd: r.baseEnd + shift }
    })
}

/** Accepts the given revisions at once: the new baseline. */
export function acceptAll(baseline: string, revisions: Revision[]): string {
    let text = baseline
    // From the end; two at one place in the baseline, the later in the document first.
    for (const r of [...revisions].sort((a, b) => b.baseStart - a.baseStart || b.curStart - a.curStart)) text = acceptInto(text, r)
    return text
}

/** Rejects the given revisions at once: the new document text. */
export function rejectAll(current: string, revisions: Revision[]): string {
    let text = current
    // From the end; at one place, the insertion taken out before the deletion is put back there (the other way round the
    // text put back was taken out again).
    for (const r of [...revisions].sort((a, b) => b.curStart - a.curStart || b.curEnd - a.curEnd)) text = rejectFrom(text, r)
    return text
}

/** The revisions counted for the status bar: how many add text, how many take text away, and by source. */
export function countRevisions(revisions: Revision[]) {
    const sources = new Map<string, number>()
    let inserted = 0, deleted = 0
    for (const r of revisions) {
        // A format's group counts once.
        if (r.group && r.group !== r.id) continue
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
        id: r.id, kind: r.kind, baseStart: r.baseStart, baseEnd: r.baseEnd, curStart: r.curStart, curEnd: r.curEnd, oldText: r.oldText, newText: r.newText,
        source: r.source, session: r.session, time: r.time, line, ...(r.group ? { group: r.group, format: r.format } : {}),
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

// ---- the writing, not the words ----

/**
 * A text's words without its Markdown writing: block marks (quotation, list markers and their numbers, task boxes,
 * heading marks), fences of either mark, setext underlines, emphasis marks, escapes, spaces at line ends and blank lines.
 */
export function wordingOf(text: string): string {
    return text.split('\n').map(line => line
        .replace(/^[ \t]*(?:>[ \t]?)*[ \t]*/, '')
        .replace(/^(?:[*+-]|\d+[.)])[ \t]+/, '')
        .replace(/^\[[ xX]\][ \t]+/, '')
        .replace(/^#{1,6}(?:[ \t]+|$)/, '')
        .replace(/^(`{3,}|~{3,})/, '```')
        .replace(/^(?:=+|-+)[ \t]*$/, '')
        .replace(/[*_]+/g, '')
        .replace(/\\(?=[^\w\s])/g, '')
        .replace(/[ \t]+$/, ''))
        .filter(line => line.length).join('\n')
}

const lineStartOf = (text: string, at: number) => text.lastIndexOf('\n', at - 1) + 1
const lineEndOf = (text: string, at: number) => { const i = text.indexOf('\n', at); return i < 0 ? text.length : i }

/**
 * A revision that changes only how the text is written, not its words (the user's decision, 2026-11-06: "格式改动不算
 * 修订"): the whole lines it is in read the same word for word on both sides - an ordered list numbered on after an
 * item put in, a fence's mark, a blank line, a list's marker.
 */
export function onlyWriting(baseline: string, current: string, r: Hunk): boolean {
    const a = baseline.slice(lineStartOf(baseline, r.baseStart), lineEndOf(baseline, r.baseEnd))
    const b = current.slice(lineStartOf(current, r.curStart), lineEndOf(current, r.curEnd))
    return a !== b && wordingOf(a) === wordingOf(b)
}

const FORMAT_MARKS: Record<string, string> = { '**': 'bold', '__': 'bold', '*': 'italic', '_': 'italic', '~~': 'strike', '`': 'code', '==': 'mark' }

/** A revision that is a format's mark put on or taken off (a "**" alone). */
export const isFormatMark = (r: Revision) => (r.kind === 'insert' || r.kind === 'delete') && !!FORMAT_MARKS[r.newText || r.oldText]

/**
 * The reader's format put on or taken off (Ctrl+B: a ** before and after the words): the two revisions of its marks,
 * both made by the change, one each side of the words on one line, as a group.
 */
export function groupFormats(revisions: Revision[], current: string, region: { from: number, to: number } | null) {
    if (!region) return
    const marks = revisions.filter(r => !r.group && isFormatMark(r) &&
        touches(r.curStart, r.curEnd, region.from - 2, region.to + 2))
    for (let i = 0; i + 1 < marks.length; i++) {
        const a = marks[i], b = marks[i + 1]
        if (a.kind !== b.kind || (a.newText || a.oldText) !== (b.newText || b.oldText) || a.source !== b.source) continue
        if (current.slice(a.curEnd, b.curStart).includes('\n')) continue
        a.group = b.group = a.id
        a.format = b.format = FORMAT_MARKS[a.newText || a.oldText]
        i++
    }
}
