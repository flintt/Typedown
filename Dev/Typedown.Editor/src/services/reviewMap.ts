// Where review mode's revisions (services/review.ts, offsets in the Markdown) are in the visual editor: Muya keeps the
// document as blocks whose leaves hold the Markdown of their line or paragraph ("# Title", "some **bold** text", a
// table cell, a line of code), and writes them out in order with the markers between them ("> ", "- ", "| ", the
// fences). Each leaf's lines are found in the Markdown in that order; what lies between two of them may only be such
// markers. A line not found so is left without marks rather than marked in the wrong place.

import type { Revision } from './review'

export interface Leaf {
    key: string
    text: string
    /** The blocks it is in, outermost first (Muya's reviewLeaves). */
    path?: { key: string, type: string, functionType?: string }[]
}

/** One line of a leaf where it is in the Markdown. */
interface Piece {
    key: string
    /** Offset in the leaf's text. */
    at: number
    length: number
    /** Offset in the Markdown. */
    md: number
}

export interface LeafMap {
    pieces: Piece[]
    /** Each leaf's blocks, by key. */
    paths: Map<string, { key: string, type: string, functionType?: string }[]>
    /** Each leaf's text length and text, by key, and the leaves in order. */
    lengths: Map<string, number>
    texts: Map<string, string>
    order: string[]
}

// Between two leaves' lines: spaces, quotation and list markers, numbers, table pipes and the alignment row, heading
// marks, fences, task boxes, math and front matter fences.
// A footnote's "[^1]: " too.
const GAP = /^[\s>*+\-\d.)|:#`~[\]xX$=^]*$/

// What may lie between two leaves: the markers, and also a fence's line with its info string (a diagram's "```mermaid",
// which no leaf holds), a footnote's definition (Muya keeps it at the end), a formula's $$.
const gapOk = (gap: string) => GAP.test(gap.replace(/^[ \t]*(?:`{3,}|~{3,})[^\n]*$/gm, '').replace(/^\[\^[^\]\n]*\]:[^\n]*$/gm, '').replace(/^[ \t]*\$\$[ \t]*$/gm, ''))

export function mapLeaves(markdown: string, leaves: Leaf[]): LeafMap {
    const pieces: Piece[] = []
    const lengths = new Map<string, number>()
    const texts = new Map<string, string>()
    const order: string[] = []
    const paths = new Map<string, { key: string, type: string, functionType?: string }[]>()
    let pos = 0
    // A line not found where it should be (a block whose text is not among the leaves): the next one found, a line of
    // some length, is taken wherever it is - what came after a diagram was all left unmarked (the user, 2026-11-06).
    let missed = false
    for (const leaf of leaves) {
        lengths.set(leaf.key, leaf.text.length)
        texts.set(leaf.key, leaf.text)
        order.push(leaf.key)
        if (leaf.path) paths.set(leaf.key, leaf.path)
        let at = 0
        for (const line of leaf.text.split('\n')) {
            if (line.length) {
                const found = markdown.indexOf(line, pos)
                if (found >= 0 && (gapOk(markdown.slice(pos, found)) || (missed && line.trim().length >= 3))) {
                    pieces.push({ key: leaf.key, at, length: line.length, md: found })
                    pos = found + line.length
                    missed = false
                } else {
                    // A leaf Muya keeps elsewhere than the text has it (a footnote's definition, at the end of its
                    // blocks): found anywhere, the place reached kept.
                    const anywhere = line.trim().length >= 3 ? markdown.indexOf(line) : -1
                    if (anywhere >= 0 && anywhere < pos && markdown.indexOf(line, anywhere + 1) < 0) pieces.push({ key: leaf.key, at, length: line.length, md: anywhere })
                    else missed = true
                }
            }
            at += line.length + 1
        }
    }
    pieces.sort((a, b) => a.md - b.md)
    return { pieces, lengths, texts, order, paths }
}

/** The leaf place of a Markdown offset: in a line of a leaf (its end too), or else the start of the next one. */
export function leafPointOf(map: LeafMap, offset: number): { key: string, offset: number } | null {
    for (const p of map.pieces) {
        if (offset < p.md) return { key: p.key, offset: p.at }
        if (offset <= p.md + p.length) return { key: p.key, offset: p.at + offset - p.md }
    }
    const last = map.pieces[map.pieces.length - 1]
    return last ? { key: last.key, offset: last.at + last.length } : null
}

/** The Markdown offset of a place in a leaf (the caret's), or null where the leaf's line was not found. */
export function markdownOffsetOf(map: LeafMap, key: string, offset: number): number | null {
    let best: Piece | null = null
    for (const p of map.pieces) {
        if (p.key !== key) continue
        if (offset >= p.at && offset <= p.at + p.length) return p.md + offset - p.at
        if (p.at <= offset) best = p
    }
    return best ? best.md + best.length : null
}

/** A run of a leaf's text drawn as part of a revision, or a place in it where deleted text is shown. */
export interface Segment {
    start: number
    end: number
    rid: string
    /** Inserted text: green, underlined. */
    inserted: boolean
    /** Deleted text shown before (or, at the leaf's end, after) the run. */
    deletedBefore?: string
    deletedAfter?: string
    /** The program the revision is from ('' for the reader). */
    source: string
}

export interface LeafMarks {
    segments: Segment[]
    /** The whole leaf is inserted: the green bar at its left. */
    whole?: string
    /** Paragraphs deleted just before this leaf: the red strip above it. */
    deletedAbove?: { text: string, rid: string, source: string }[]
    /** Paragraphs deleted at the end of the document, after the last leaf. */
    deletedBelow?: { text: string, rid: string, source: string }[]
    /** The programs whose revisions are in it, for the tag at its left. */
    sources: string[]
    /** What its block marks say changed (a task ticked, an item indented, made a heading): that revision's id. */
    structure?: string
    /** A new block split off before it (Enter in the middle of an item): that revision's id. */
    split?: string
}

const marksOf = (all: Map<string, LeafMarks>, key: string) => {
    let m = all.get(key)
    if (!m) all.set(key, m = { segments: [], sources: [] })
    return m
}

// A line break and the next line's block marks (a list's "- ", a quotation's "> "): what joining two blocks takes away
// and splitting one puts in.
const BREAK_AND_MARKS = /\n[ \t]*(?:>[ \t]?)*[ \t]*(?:(?:[*+-]|\d+[.)])[ \t]+)?(?:\[[ xX]\][ \t]+)?/g
export const isBreakAndMarks = (text: string) => /\n/.test(text) && !text.replace(BREAK_AND_MARKS, '').replace(/\n/g, '').trim()

// Deleted text inside a line: a line break and the marks after it one "↵" (two blocks joined), never the marks
// themselves (the user, 2026-11-07: "¶ -" showed).
const shownInline = (text: string) => isBreakAndMarks(text) ? '↵' : text.replace(BREAK_AND_MARKS, ' ↵ ')

/** A deletion of whole lines (blocks): at a line's start, ending at one. */
export function isWholeLines(r: Revision, current: string): boolean {
    const atLineStart = r.curStart === 0 || current[r.curStart - 1] === '\n'
    return r.oldText.includes('\n') && atLineStart && /\S/.test(r.oldText) && (r.curEnd === r.curStart || current[r.curEnd - 1] === '\n' || r.curEnd === current.length)
}

/** A block deleted, drawn where it was as a block of its kind (a "ghost"), and where: before or after a block's element. */
export interface Ghost {
    rid: string
    source: string
    kind: 'p' | 'heading' | 'li' | 'quote' | 'code' | 'math' | 'diagram' | 'html' | 'table' | 'row' | 'hr' | 'front'
    /** Its lines, without their block marks (a list item's text, a row's cells). */
    lines: string[]
    /** A list item's marker as it was ("•", "2.", with "☐"/"☑"); a heading's level. */
    marker?: string
    level?: number
    /** How far in it was: 0 at the top level of its list, 1 an item inside another. */
    depth?: number
    /** The block whose element it goes beside. */
    key: string
    where: 'before' | 'after'
    /** Inside a table: its cells. */
    cells?: string[][]
}

const LIST_ITEM = /^([ \t]*)(?:([*+-])|(\d{1,9}[.)]))[ \t]+(\[[ xX]\][ \t]+)?/
const DIAGRAMS = /^(?:mermaid|flowchart|sequence|plantuml|vega-lite)\b/

// A deleted stretch's blocks (its own revision is a block, mostly: deletions are split by block, review.ts).
function ghostKind(text: string): { kind: Ghost['kind'], lines: string[], marker?: string, level?: number, depth?: number, cells?: string[][] } {
    const lines = text.replace(/^\n+/, '').replace(/\n+$/, '').split('\n')
    const first = lines[0] ?? ''
    const fence = /^[ \t]*(`{3,}|~{3,})[ \t]*([^\s`]*)/.exec(first)
    if (fence) {
        const inner = lines.slice(1, lines.length > 1 && /^[ \t]*(`{3,}|~{3,})[ \t]*$/.test(lines[lines.length - 1]) ? -1 : undefined)
        return { kind: DIAGRAMS.test(fence[2]) ? 'diagram' : 'code', lines: inner }
    }
    if (/^[ \t]*\$\$/.test(first)) return { kind: 'math', lines: lines.filter(l => !/^[ \t]*\$\$[ \t]*$/.test(l)) }
    if (/^---[ \t]*$/.test(first) && lines.length > 1 && /^(?:---|\.\.\.)[ \t]*$/.test(lines[lines.length - 1])) return { kind: 'front', lines: lines.slice(1, -1) }
    if (/^[ \t]{0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$/.test(first) && lines.length === 1) return { kind: 'hr', lines: [] }
    const h = /^[ \t]{0,3}(#{1,6})[ \t]+(.*)$/.exec(first)
    if (h && lines.length === 1) return { kind: 'heading', lines: [h[2].replace(/[ \t]+#+[ \t]*$/, '')], level: h[1].length }
    if (/^[ \t]*\|/.test(first)) {
        const rows = lines.filter(l => /\|/.test(l))
        const delimiter = rows.findIndex(l => /^[ \t]*\|?[ \t]*:?-+:?[ \t]*(\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$/.test(l))
        const cells = rows.filter((_, i) => i !== delimiter).map(l => l.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim()))
        return { kind: delimiter >= 0 ? 'table' : 'row', lines: rows, cells }
    }
    if (/^[ \t]{0,3}<[a-zA-Z!/]/.test(first)) return { kind: 'html', lines }
    if (/^[ \t]*>/.test(first)) return { kind: 'quote', lines: lines.map(l => l.replace(/^[ \t]*(?:>[ \t]?)+/, '')) }
    const li = LIST_ITEM.exec(first)
    if (li) {
        const box = li[4] ? (/\[[xX]\]/.test(li[4]) ? '☑' : '☐') : ''
        const marker = (li[2] ? '•' : li[3]) + (box ? ' ' + box : '')
        return { kind: 'li', lines: [first.slice(li[0].length), ...lines.slice(1).map(l => l.trim())], marker, depth: li[1].length ? 1 : 0 }
    }
    return { kind: 'p', lines: lines.map(l => l.trim()) }
}

type PathEntry = { key: string, type: string, functionType?: string }

/**
 * The blocks deleted whole, each drawn where it was: a list item among its list's items (after the one before it, or
 * before the one after it - never in the next item's place or in another list), a table row among the rows, anything
 * else between the top-level blocks around it. Never inside a block drawn whole (a formula, a diagram, code, HTML): the
 * user, 2026-11-07, a strip put in a formula broke it.
 */
export function ghostsOf(map: LeafMap, revisions: Revision[], current: string): Ghost[] {
    const ghosts: Ghost[] = []
    const lineOf = (md: number) => current.slice(current.lastIndexOf('\n', md - 1) + 1, (current.indexOf('\n', md) + 1 || current.length + 1) - 1)
    for (const r of revisions) {
        if (!r.oldText || r.curEnd !== r.curStart || !isWholeLines(r, current)) continue
        const g = ghostKind(r.oldText)
        const next = map.pieces.find(p => p.md >= r.curStart)
        const prev = [...map.pieces].reverse().find(p => p.md + p.length <= r.curStart)
        const pathOf = (p: Piece | undefined): PathEntry[] => (p && map.paths.get(p.key)) || []
        const adjacentNext = !!next && current[r.curStart] !== '\n' && current.lastIndexOf('\n', next.md - 1) + 1 === r.curStart
        let place: { key: string, where: 'before' | 'after' } | null = null
        let inside = false
        const deepest = (path: PathEntry[], type: string) => [...path].reverse().find(b => b.type === type)
        // A part of a quotation deleted, the rest of it still there: inside it, where it was (with its bar), as a
        // paragraph of it - not out of it into the next block (the user, 2026-11-07).
        const lineBefore = r.curStart > 0 ? lineOf(r.curStart - 1) : ''
        const inQuote = (p: Piece | undefined, line: string) => {
            const path = pathOf(p)
            const q = path.map(b => b.type).lastIndexOf('blockquote')
            return q >= 0 && /^[ \t]*>/.test(line) ? path[q + 1]?.key ?? null : null
        }
        if (g.kind === 'quote') {
            const after = prev ? inQuote(prev, lineBefore) : null
            const before = !after && next && adjacentNext ? inQuote(next, lineOf(r.curStart)) : null
            if (after || before) {
                const inner = ghostKind(r.oldText.split('\n').map(l => l.replace(/^[ \t]*(?:>[ \t]?)+/, '')).join('\n'))
                place = after ? { key: after, where: 'after' } : { key: before!, where: 'before' }
                ghosts.push({ rid: r.id, source: r.source, ...inner, kind: inner.kind === 'li' ? 'li' : inner.kind, lines: inner.lines.filter(l => l.trim()), depth: 0, ...place })
                continue
            }
        }
        if (g.kind === 'li') {
            const indent = (LIST_ITEM.exec(r.oldText.replace(/^\n+/, ''))?.[1] ?? '').length
            const itemIndent = (p: Piece) => { const m = LIST_ITEM.exec(lineOf(p.md)); return m ? m[1].length : -1 }
            if (next && adjacentNext && itemIndent(next) === indent) { const li = deepest(pathOf(next), 'li'); if (li) place = { key: li.key, where: 'before' } }
            if (!place && prev && itemIndent(prev) === indent) { const li = deepest(pathOf(prev), 'li'); if (li) place = { key: li.key, where: 'after' } }
            if (!place && prev && itemIndent(prev) >= 0 && itemIndent(prev) < indent) {
                // The last of the items inside the one before: after that item's own text, inside it.
                const li = deepest(pathOf(prev), 'li'); if (li) { inside = true; place = { key: pathOf(prev)[pathOf(prev).length - 1]?.key ?? li.key, where: 'after' } }
            }
        } else if (g.kind === 'row') {
            const tr = (p: Piece | undefined) => deepest(pathOf(p), 'tr')
            if (next && tr(next)) place = { key: tr(next)!.key, where: 'before' }
            else if (prev && tr(prev)) place = { key: tr(prev)!.key, where: 'after' }
        }
        if (!place) {
            // Between the top-level blocks: before the next one, else after the last.
            const top = (p: Piece | undefined) => pathOf(p)[0]?.key ?? p?.key
            if (next && top(next)) place = { key: top(next)!, where: 'before' }
            else if (prev && top(prev)) place = { key: top(prev)!, where: 'after' }
        }
        if (!place) continue
        // (Nested only where it is not among its own list's items: after the text of the item it was in.)
        ghosts.push({ rid: r.id, source: r.source, ...g, depth: inside ? g.depth : 0, ...place })
    }
    return ghosts
}

/**
 * The marks to draw in each leaf for the revisions shown. A deletion that takes whole lines away is a strip above the
 * leaf that follows it; one inside a line is shown where it was, as a mark on the character after it (before the
 * leaf's end) or the one before it.
 */
export function leafMarks(map: LeafMap, revisions: Revision[], current: string): Map<string, LeafMarks> {
    const all = new Map<string, LeafMarks>()
    const tag = (key: string, source: string) => {
        const m = marksOf(all, key)
        if (source && !m.sources.includes(source)) m.sources.push(source)
    }
    for (const r of revisions) {
        // The inserted text, a run per line of a leaf it covers.
        const covered = new Map<string, number>()
        let firstInsert: { key: string, start: number } | null = null
        if (r.curEnd > r.curStart) {
            for (const p of map.pieces) {
                const from = Math.max(r.curStart, p.md), to = Math.min(r.curEnd, p.md + p.length)
                if (from >= to) continue
                const seg: Segment = { start: p.at + from - p.md, end: p.at + to - p.md, rid: r.id, inserted: true, source: r.source }
                marksOf(all, p.key).segments.push(seg)
                tag(p.key, r.source)
                covered.set(p.key, (covered.get(p.key) ?? 0) + (to - from))
                if (!firstInsert) firstInsert = { key: p.key, start: seg.start }
            }
            // A leaf all of whose text is inserted is a new paragraph: the bar.
            for (const [key, n] of covered) {
                const total = map.pieces.filter(p => p.key === key).reduce((s, p) => s + p.length, 0)
                if (n === total && total > 0) marksOf(all, key).whole = r.id
            }
        }
        // What the block marks say changed: the line's leaf marked at its left, nothing of the marks drawn.
        if (r.structure) {
            const p = leafPointOf(map, Math.min(current.length, r.curEnd))
            if (p) { const m = marksOf(all, p.key); m.structure = m.structure ?? r.id; tag(p.key, r.source) }
            continue
        }
        // A line break and marks put in (an item split in two): the new block's leaf marked at its left.
        if (!r.oldText && isBreakAndMarks(r.newText)) {
            const next = map.pieces.find(p => p.md >= r.curEnd)
            if (next) { const m = marksOf(all, next.key); m.split = m.split ?? r.id; tag(next.key, r.source) }
            continue
        }
        if (!r.oldText) continue
        // Whole blocks deleted: drawn where they were, as blocks of their own (ghostsOf).
        if (isWholeLines(r, current)) continue
        const point = firstInsert ?? (() => { const p = leafPointOf(map, r.curStart); return p ? { key: p.key, start: p.offset } : null })()
        if (!point) continue
        const text = shownInline(r.oldText)
        const m = marksOf(all, point.key)
        tag(point.key, r.source)
        const length = map.lengths.get(point.key) ?? 0
        if (firstInsert) {
            // A replacement: the deleted text before the inserted run.
            const seg = m.segments.find(s => s.rid === r.id && s.start === point.start)
            if (seg) seg.deletedBefore = text
            continue
        }
        const place = deletionPlace(map.texts.get(point.key) ?? '', point.start)
        if (place) m.segments.push({ start: place.at, end: place.at + 1, rid: r.id, inserted: false, ...(place.after ? { deletedAfter: text } : { deletedBefore: text }), source: r.source })
        else if (length === 0) (m.deletedAbove ??= []).push({ text, rid: r.id, source: r.source })
    }
    for (const m of all.values()) m.segments = settle(m.segments)
    return all
}

// The marks the visual editor may hide (bold, emphasis, code, links, math, escapes, headings): a deletion is drawn on a
// character beside it that shows - on one of these it was drawn inside the hidden mark, out of sight, or where the
// mark showed once the caret came in (the user, 2026-11-06: "删除的文字跑到不是原本它的位置").
const MAYBE_HIDDEN = /[*_~=`[\]()<>!$^\\#:|{}]/

/**
 * Where to draw a deletion at offset `k` of a leaf's text: after the character before it (the place right after that
 * character is the deletion's place), else before the character at it, each only where that character is not a mark the
 * editor may hide; else the nearest character that is not one, on the side the deletion is. Null for an empty leaf.
 */
export function deletionPlace(text: string, k: number): { at: number, after: boolean } | null {
    if (!text.length) return null
    k = Math.max(0, Math.min(k, text.length))
    const shows = (i: number) => i >= 0 && i < text.length && !MAYBE_HIDDEN.test(text[i]) && text[i] !== '\n'
    if (shows(k - 1)) return { at: k - 1, after: true }
    if (shows(k)) return { at: k, after: false }
    for (let j = k; j < text.length; j++) if (shows(j)) return { at: j, after: false }
    for (let j = k - 1; j >= 0; j--) if (shows(j)) return { at: j, after: true }
    return k < text.length ? { at: k, after: false } : { at: text.length - 1, after: true }
}

// The runs of a leaf sorted and made not to overlap. A deletion's mark on a character an insertion covers splits the
// insertion's run there; the character takes the deletion's id when the insertion is another revision's, so that each
// revision has a mark of its own to be pointed at (its buttons, a click), not the one it is drawn beside.
function settle(segments: Segment[]): Segment[] {
    let runs = segments.filter(s => s.inserted).sort((a, b) => a.start - b.start).map(s => ({ ...s }))
    const marks = segments.filter(s => !s.inserted).sort((a, b) => a.start - b.start)
    const loose: Segment[] = []
    for (const d of marks) {
        const c = d.start
        const i = runs.findIndex(r => r.start <= c && c < r.end)
        if (i < 0) {
            const same = loose.find(l => l.start === c)
            if (same) {
                if (d.deletedBefore) same.deletedBefore = (same.deletedBefore ?? '') + d.deletedBefore
                if (d.deletedAfter) same.deletedAfter = (same.deletedAfter ?? '') + d.deletedAfter
            } else loose.push({ ...d })
            continue
        }
        const r = runs[i]
        const own = r.rid === d.rid
        const pieces: Segment[] = []
        if (r.start < c) pieces.push({ ...r, end: c, deletedBefore: r.deletedBefore, deletedAfter: undefined })
        const middle: Segment = { ...r, start: c, end: c + 1, rid: own ? r.rid : d.rid, source: own ? r.source : d.source,
            deletedBefore: r.start === c ? r.deletedBefore : undefined, deletedAfter: c + 1 === r.end ? r.deletedAfter : undefined }
        if (d.deletedBefore) middle.deletedBefore = (middle.deletedBefore ?? '') + d.deletedBefore
        if (d.deletedAfter) middle.deletedAfter = d.deletedAfter + (middle.deletedAfter ?? '')
        pieces.push(middle)
        if (c + 1 < r.end) pieces.push({ ...r, start: c + 1, deletedBefore: undefined, deletedAfter: r.deletedAfter })
        runs = [...runs.slice(0, i), ...pieces, ...runs.slice(i + 1)]
    }
    return [...runs, ...loose].sort((a, b) => a.start - b.start).filter(s => s.end > s.start)
}
