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
            // Edited: as Muya writes it now - but the lines of it the edit did not change as the source has them where
            // the source's line is that line written its own way (a ~~~ fence; editing the code inside turned it into
            // \`\`\`), and without a blank line Muya adds that the source does not have (a lazy line after a quotation
            // was pushed out of it).
            const region = editedRegion(source.slice(j, nj), base.slice(i, ni), current.slice(k, nk))
            out.push(...region.lines)
            kept = kept || region.kept
        }
        i = ni; j = nj; k = nk
    }
    return kept ? out.join('') : null
}

// A line as far as the edit is concerned: a code fence of either mark and any length the same, and a list item's marker
// followed by a tab or spaces.
const fenceKey = (line: string) => {
    const key = line.replace(/^( {0,3})(`{3,}|~{3,})/, '$1```').replace(/^(\s*)([*+-]|\d+[.)])[ \t]+/, '$1$2 ')
    // A table's row, its cells padded or not, its delimiters of any length.
    return isTableLine(key) ? key.replace(/[ \t]*\|[ \t]*/g, '|').replace(/-+/g, '-').replace(/[ \t]+$/, '') : key
}

const isTableLine = (line: string) => /^[ \t]*\|.*\|[ \t]*\n?$/.test(line)

// A line's block marks (quotation, list marker) with the spaces around them.
const prefixOf = (line: string) => /^[ \t]*(?:>[ \t]?)*[ \t]*(?:(?:[*+-]|\d+[.)])[ \t]+)?/.exec(line)![0]
// The same marks, written with other spaces only.
const samePrefix = (a: string, b: string) => {
    const pa = prefixOf(a), pb = prefixOf(b)
    return pa !== pb && pa.replace(/[ \t]+/g, '') === pb.replace(/[ \t]+/g, '') && pa.replace(/[ \t]+/g, '').length > 0
}

// A table's row written as the source writes its rows: compact (no space at the pipes) or one space either side.
function asSourceRow(row: string, sourceRows: string[]): string {
    if (!sourceRows.length) return row
    const nl = row.endsWith('\n') ? '\n' : ''
    const body = row.replace(/\n$/, '')
    if (sourceRows.every(r => !/[ \t]\||\|[ \t]/.test(r))) return body.replace(/[ \t]*\|[ \t]*/g, '|').replace(/-{2,}/g, '-') + nl
    if (sourceRows.every(r => !/[ \t]{2,}/.test(r.replace(/^[ \t]+/, '')))) return body.replace(/[ \t]*\|[ \t]*/g, ' | ').trim().replace(/-{4,}/g, '---') + nl
    return row
}

/**
 * An edited stretch of the document: Muya's lines now, each line the edit did not change (the same in the base and now)
 * taken from the source where the source has it written its own way (fenceKey); a blank line Muya added where the source
 * has none dropped. `kept`: something of the source's own writing is in it.
 */
export function editedRegion(source: string[], base: string[], current: string[]): { lines: string[], kept: boolean } {
    const toCurrent = alignment(base, current)
    // The base lines' own source lines, in order, by fenceKey; blank lines only to blank lines.
    const toSource = new Int32Array(base.length).fill(-1)
    let from = 0
    for (let b = 0; b < base.length; b++) {
        for (let x = from; x < source.length; x++) {
            if (fenceKey(source[x]) === fenceKey(base[b])) { toSource[b] = x; from = x + 1; break }
            // Only past lines the source has that the base does not (a line of the source's own): no further.
            if (source[x].trim() && base.slice(b).some(l => fenceKey(l) === fenceKey(source[x]))) break
        }
    }
    const sourceHasBlank = source.some(l => !l.trim())
    const fromBase = new Int32Array(current.length).fill(-1)
    for (let b = 0; b < base.length; b++) if (toCurrent[b] >= 0) fromBase[toCurrent[b]] = b
    const lines: string[] = []
    let kept = false
    // A table's rows the source writes its own way: an edited row written the same way (Muya pads every cell).
    const sourceRows = source.filter(l => isTableLine(l) && !/^[ \t|:-]+\n?$/.test(l))
    for (let c = 0; c < current.length; c++) {
        const b = fromBase[c]
        if (b >= 0 && toSource[b] >= 0) {
            const line = source[toSource[b]]
            if (line !== current[c]) kept = true
            lines.push(line)
        } else if (b >= 0 && !current[c].trim() && !sourceHasBlank) {
            kept = true
        } else if (base.length === current.length && source.length === base.length && samePrefix(current[c], source[c])) {
            // An edited line, one for one with the source's: its block marks written as the source writes them (a list
            // item's tab after its marker, a quotation's ">" without a space).
            const line = prefixOf(source[c]) + current[c].slice(prefixOf(current[c]).length)
            if (line !== current[c]) kept = true
            lines.push(line)
        } else if (isTableLine(current[c]) && sourceRows.length) {
            const row = asSourceRow(current[c], sourceRows)
            if (row !== current[c]) kept = true
            lines.push(row)
        } else lines.push(current[c])
    }
    return { lines, kept }
}

// ---- footnote definitions ----

const NOTE = /^ {0,3}\[\^([^\]\n]+)\]:/

/**
 * The text's lines with its footnote definitions taken out (a definition, its indented lines and the blank lines after
 * it), and each definition by its label with the index, in the lines left, of the line it came after (-1: the start).
 */
export function takeNotes(lines: string[]): { rest: string[], notes: Map<string, { lines: string[], after: number }> } {
    const rest: string[] = []
    const notes = new Map<string, { lines: string[], after: number }>()
    for (let i = 0; i < lines.length;) {
        const m = NOTE.exec(lines[i])
        if (!m) { rest.push(lines[i++]); continue }
        const block = [lines[i++]]
        while (i < lines.length && (/^(?: {4}|\t)\S/.test(lines[i]) || (!lines[i].trim() && i + 1 < lines.length && /^(?: {4}|\t)\S/.test(lines[i + 1])))) block.push(lines[i++])
        while (i < lines.length && !lines[i].trim()) block.push(lines[i++])
        notes.set(m[1], { lines: block, after: rest.length - 1 })
    }
    return { rest, notes }
}

/**
 * The merge with footnote definitions where the source has them: Muya writes every definition at the end, and an edit
 * near one (or of it) moved it there - and the paragraph after it before it (review-matrix.js). The definitions are
 * taken out of all three texts, the rest merged, and each of the source's put back after the line it followed, as the
 * edit has it now (an edited definition is the export's); one the source did not have goes at the end, as Muya writes
 * it. Null when there is nothing to put back or the rest does not merge.
 */
export function mergeKeepingNotes(source: string[], base: string[], current: string[]): string | null {
    const s = takeNotes(source), b = takeNotes(base), c = takeNotes(current)
    if (!s.notes.size) return null
    const merged = mergeSource(s.rest, b.rest, c.rest, alignment(b.rest, s.rest)) ?? c.rest.join('')
    const lines = splitLines(merged)
    // Where each of the source's lines left is in the merged text.
    const toMerged = new Int32Array(s.rest.length).fill(-1)
    for (const [is, im] of matchLines(s.rest, lines)) toMerged[is] = im
    const insertAfter = new Map<number, string[]>()
    for (const [label, note] of s.notes) {
        const now = c.notes.get(label)
        if (!now) continue
        const edited = b.notes.get(label)?.lines.join('') !== now.lines.join('')
        let at = note.after
        while (at >= 0 && toMerged[at] < 0) at--
        const target = at < 0 ? -1 : toMerged[at]
        // An edited one as the export has it, with the blank lines the source had after it.
        const lastText = (list: string[]) => { for (let i = list.length - 1; i >= 0; i--) if (list[i].trim()) return i; return -1 }
        const trailing = note.lines.slice(lastText(note.lines) + 1)
        const text = edited ? [...now.lines.slice(0, lastText(now.lines) + 1), ...trailing] : note.lines
        insertAfter.set(target, [...(insertAfter.get(target) ?? []), ...text])
    }
    const out: string[] = [...(insertAfter.get(-1) ?? [])]
    lines.forEach((line, i) => { out.push(line); out.push(...(insertAfter.get(i) ?? [])) })
    // A definition put after the last line needs that line ended.
    for (let i = 0; i < out.length - 1; i++) if (!out[i].endsWith('\n')) out[i] += '\n'
    // The export's own new definitions, at the end.
    const added = [...c.notes.entries()].filter(([label]) => !s.notes.has(label)).flatMap(([, n]) => n.lines)
    if (added.length) {
        if (out.length && !out[out.length - 1].endsWith('\n')) out[out.length - 1] += '\n'
        if (out.length && out[out.length - 1].trim()) out.push('\n')
        out.push(...added)
    }
    return out.join('')
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

/**
 * One block's text as the document holds it (a copy button's Markdown): `block` is Muya's export of the block, `exported`
 * its export of the whole document now and `source` the document's text as the page gives it to the host (the reader's
 * own lines kept where they did not edit). The block's lines found once in the export; the source's lines between the
 * nearest lines matched on both sides around them, without blank lines at either end. Used only when they read back as
 * the block (`normalize`: as Muya writes a text) - a compact table stays compact, a fence as it was written. Null when
 * not found or not the same block: the caller takes Muya's export.
 */
export function blockSource(block: string, exported: string, source: string, normalize: (text: string) => string): string | null {
    const trim = (text: string) => text.replace(/^\n+|\n+$/g, '')
    const wanted = trim(block)
    if (!wanted) return null
    if (source === exported) return null
    const blockLines = splitLines(wanted + '\n')
    const all = splitLines(exported.endsWith('\n') ? exported : exported + '\n')
    let at = -1
    for (let i = 0; i + blockLines.length <= all.length; i++) {
        if (!same(all, i, i + blockLines.length, blockLines, 0, blockLines.length)) continue
        if (at >= 0) return null
        at = i
    }
    if (at < 0) return null
    const sourceLines = splitLines(source.endsWith('\n') ? source : source + '\n')
    let before = -1, after = sourceLines.length
    for (const [ie, is] of matchLines(all, sourceLines)) {
        if (ie < at) before = is
        else if (ie >= at + blockLines.length) { after = is; break }
    }
    const candidate = trim(sourceLines.slice(before + 1, after).join(''))
    if (!candidate || trim(normalize(candidate + '\n')) !== wanted) return null
    return candidate
}
