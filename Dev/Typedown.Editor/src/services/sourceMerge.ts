// The reader's own text kept where they did not edit (docs/todo.md, "一次编辑后整篇格式被规范化").
//
// Muya reads a document into its model and writes the whole model back: one edit used to make the whole normalized
// serialization the document (tables padded, blank lines and markers made uniform), and a version history or a diff
// showed nearly every line changed for one keystroke. Here the edit is carried over to the source instead, a three-way
// merge by lines: `base` is Muya's export of the document as it was loaded, `source` the document as it was loaded,
// `current` Muya's export now. Where the export did not change, the source's lines stay; where it changed, the
// export's lines are taken (a block edited is written as Muya writes it, as before). The caller checks the result
// reads back as `current` before using it (Muya's normalizeMarkdown), and falls back to `current` when it does not.

/** Lines with their line breaks, so joining them gives the text back exactly. */
export function splitLines(text: string): string[] {
    const lines: string[] = []
    let start = 0
    for (let i = 0; i < text.length; i++) {
        if (text.charCodeAt(i) === 10) {
            lines.push(text.slice(start, i + 1))
            start = i + 1
        }
    }
    if (start < text.length) lines.push(text.slice(start))
    return lines
}

/** Pairs of equal lines [a index, b index], increasing in both: a patience diff, with LCS for small leftovers. */
export function matchLines(a: string[], b: string[]): Array<[number, number]> {
    const pairs: Array<[number, number]> = []
    const recurse = (a0: number, a1: number, b0: number, b1: number) => {
        // Equal lines at both ends first.
        while (a0 < a1 && b0 < b1 && a[a0] === b[b0]) pairs.push([a0++, b0++])
        const tail: Array<[number, number]> = []
        while (a0 < a1 && b0 < b1 && a[a1 - 1] === b[b1 - 1]) tail.push([--a1, --b1])
        if (a0 < a1 && b0 < b1) {
            const anchors = uniqueAnchors(a, a0, a1, b, b0, b1)
            if (anchors.length) {
                let pa = a0, pb = b0
                for (const [ia, ib] of anchors) {
                    recurse(pa, ia, pb, ib)
                    pairs.push([ia, ib])
                    pa = ia + 1
                    pb = ib + 1
                }
                recurse(pa, a1, pb, b1)
            } else if ((a1 - a0) * (b1 - b0) <= 250000) {
                lcs(a, a0, a1, b, b0, b1, pairs)
            }
            // Larger and with no line unique to both: left unmatched, which the merge reads as changed.
        }
        for (let i = tail.length - 1; i >= 0; i--) pairs.push(tail[i])
    }
    recurse(0, a.length, 0, b.length)
    return pairs
}

// Lines found exactly once on each side, in an order increasing on both (the longest such sequence).
function uniqueAnchors(a: string[], a0: number, a1: number, b: string[], b0: number, b1: number): Array<[number, number]> {
    const count = new Map<string, { a: number, b: number, ia: number, ib: number }>()
    for (let i = a0; i < a1; i++) {
        const e = count.get(a[i])
        if (e) e.a++
        else count.set(a[i], { a: 1, b: 0, ia: i, ib: -1 })
    }
    for (let i = b0; i < b1; i++) {
        const e = count.get(b[i])
        if (e) { e.b++; e.ib = i }
    }
    const candidates: Array<[number, number]> = []
    count.forEach(e => { if (e.a === 1 && e.b === 1) candidates.push([e.ia, e.ib]) })
    candidates.sort((x, y) => x[0] - y[0])
    // Longest increasing subsequence on the b index.
    const tails: number[] = []
    const previous: number[] = new Array(candidates.length)
    for (let i = 0; i < candidates.length; i++) {
        let lo = 0, hi = tails.length
        while (lo < hi) {
            const mid = (lo + hi) >> 1
            if (candidates[tails[mid]][1] < candidates[i][1]) lo = mid + 1
            else hi = mid
        }
        previous[i] = lo > 0 ? tails[lo - 1] : -1
        tails[lo] = i
    }
    const result: Array<[number, number]> = []
    for (let i = tails.length ? tails[tails.length - 1] : -1; i >= 0; i = previous[i]) result.push(candidates[i])
    return result.reverse()
}

function lcs(a: string[], a0: number, a1: number, b: string[], b0: number, b1: number, out: Array<[number, number]>) {
    const n = a1 - a0, m = b1 - b0
    const table = new Uint32Array((n + 1) * (m + 1))
    for (let i = n - 1; i >= 0; i--)
        for (let j = m - 1; j >= 0; j--)
            table[i * (m + 1) + j] = a[a0 + i] === b[b0 + j] ? table[(i + 1) * (m + 1) + j + 1] + 1 : Math.max(table[(i + 1) * (m + 1) + j], table[i * (m + 1) + j + 1])
    let i = 0, j = 0
    while (i < n && j < m) {
        if (a[a0 + i] === b[b0 + j]) { out.push([a0 + i, b0 + j]); i++; j++ }
        else if (table[(i + 1) * (m + 1) + j] >= table[i * (m + 1) + j + 1]) i++
        else j++
    }
}

/** What a document's base lines are matched to on another side: base index -> that side's index. */
export function alignment(base: string[], other: string[]): Int32Array {
    const map = new Int32Array(base.length).fill(-1)
    for (const [ib, io] of matchLines(base, other)) map[ib] = io
    return map
}

const same = (x: string[], x0: number, x1: number, y: string[], y0: number, y1: number) => {
    if (x1 - x0 !== y1 - y0) return false
    for (let i = 0; i < x1 - x0; i++) if (x[x0 + i] !== y[y0 + i]) return false
    return true
}

/**
 * The source with the edits of base -> current carried over: the source's lines where the export did not change,
 * the current export's where it did. `toSource` is alignment(base lines, source lines), the same for every edit of a
 * load. Null when nothing of the source could be kept (the result would be `current` itself).
 */
export function mergeSource(source: string[], base: string[], current: string[], toSource: Int32Array): string | null {
    const toCurrent = alignment(base, current)
    const out: string[] = []
    let kept = false
    let i = 0, j = 0, k = 0
    while (i < base.length || j < source.length || k < current.length) {
        if (i < base.length && toSource[i] === j && toCurrent[i] === k) {
            out.push(source[j])
            i++; j++; k++
            continue
        }
        // The next line of the base matched on both sides, past where each side is.
        let ni = i
        while (ni < base.length && !(toSource[ni] >= j && toCurrent[ni] >= k)) ni++
        const nj = ni < base.length ? toSource[ni] : source.length
        const nk = ni < base.length ? toCurrent[ni] : current.length
        if (same(current, k, nk, base, i, ni)) {
            // Not edited: the source as it was.
            for (let x = j; x < nj; x++) out.push(source[x])
            if (nj > j || ni > i) kept = kept || !same(source, j, nj, base, i, ni)
        } else {
            // Edited (whether or not the source was written as Muya writes it): as Muya writes it now.
            for (let x = k; x < nk; x++) out.push(current[x])
        }
        i = ni; j = nj; k = nk
    }
    return kept ? out.join('') : null
}

type Position = { line: number, ch: number }

/**
 * A caret position in `from` (lines as split by '\n') where it is in `to`: on the same line where that line is matched,
 * else as far past the nearest matched line before it (not past the next one); the column cut to the line's length.
 * The page reports its caret in Muya's export, the host holds the text the page gave it (the source kept, here) and
 * puts the caret back by that text's lines on a reload or an automation edit.
 */
export function positionIn(fromLines: string[], toLines: string[], pairs: Array<[number, number]>, pos: Position): Position {
    let before: [number, number] | null = null, after: [number, number] | null = null
    for (const p of pairs) {
        if (p[0] === pos.line) return { line: p[1], ch: Math.min(pos.ch, lineLength(toLines[p[1]])) }
        if (p[0] < pos.line) before = p
        else { after = p; break }
    }
    const lo = before ? before[1] + 1 : 0
    const hi = after ? after[1] - 1 : toLines.length - 1
    let line = before ? before[1] + (pos.line - before[0]) : pos.line
    line = Math.max(lo, Math.min(line, Math.max(lo, hi)))
    line = Math.max(0, Math.min(line, toLines.length - 1))
    return { line, ch: Math.min(pos.ch, lineLength(toLines[line])) }
}

const lineLength = (line: string | undefined) => line == null ? 0 : line.endsWith('\n') ? line.length - 1 : line.length

/** The caret ({ anchor, focus } of lines and columns) of `from` put where it is in `to` (positionIn). */
export function cursorIn<T extends { anchor?: Position, focus?: Position }>(from: string, to: string, cursor: T): T {
    if (!cursor || from === to || !cursor.anchor || !cursor.focus) return cursor
    const fromLines = splitLines(from), toLines = splitLines(to)
    // A text ending in a line break has one empty line more after it, as split by '\n'.
    if (from.endsWith('\n')) fromLines.push('')
    if (to.endsWith('\n')) toLines.push('')
    const pairs = matchLines(fromLines, toLines)
    return { ...cursor, anchor: positionIn(fromLines, toLines, pairs, cursor.anchor), focus: positionIn(fromLines, toLines, pairs, cursor.focus) }
}
