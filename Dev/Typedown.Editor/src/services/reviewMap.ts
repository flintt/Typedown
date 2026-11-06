// Where review mode's revisions (services/review.ts, offsets in the Markdown) are in the visual editor: Muya keeps the
// document as blocks whose leaves hold the Markdown of their line or paragraph ("# Title", "some **bold** text", a
// table cell, a line of code), and writes them out in order with the markers between them ("> ", "- ", "| ", the
// fences). Each leaf's lines are found in the Markdown in that order; what lies between two of them may only be such
// markers. A line not found so is left without marks rather than marked in the wrong place.

import type { Revision } from './review'

export interface Leaf {
    key: string
    text: string
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
    /** Each leaf's text length, by key, and the leaves in order. */
    lengths: Map<string, number>
    order: string[]
}

// Between two leaves' lines: spaces, quotation and list markers, numbers, table pipes and the alignment row, heading
// marks, fences, task boxes, math and front matter fences.
const GAP = /^[\s>*+\-\d.)|:#`~[\]xX$=]*$/

export function mapLeaves(markdown: string, leaves: Leaf[]): LeafMap {
    const pieces: Piece[] = []
    const lengths = new Map<string, number>()
    const order: string[] = []
    let pos = 0
    for (const leaf of leaves) {
        lengths.set(leaf.key, leaf.text.length)
        order.push(leaf.key)
        let at = 0
        for (const line of leaf.text.split('\n')) {
            if (line.length) {
                const found = markdown.indexOf(line, pos)
                if (found >= 0 && GAP.test(markdown.slice(pos, found))) {
                    pieces.push({ key: leaf.key, at, length: line.length, md: found })
                    pos = found + line.length
                }
            }
            at += line.length + 1
        }
    }
    return { pieces, lengths, order }
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
}

const marksOf = (all: Map<string, LeafMarks>, key: string) => {
    let m = all.get(key)
    if (!m) all.set(key, m = { segments: [], sources: [] })
    return m
}

const shownDeleted = (text: string) => text.replace(/\n+$/, '').replace(/^\n+/, '')

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
        if (!r.oldText) continue
        const atLineStart = r.curStart === 0 || current[r.curStart - 1] === '\n'
        const lines = r.oldText.includes('\n') && atLineStart && (r.curEnd === r.curStart || current[r.curEnd - 1] === '\n' || r.curEnd === current.length)
        if (lines) {
            const next = map.pieces.find(p => p.md >= r.curStart)
            const strip = { text: shownDeleted(r.oldText), rid: r.id, source: r.source }
            if (next) {
                const m = marksOf(all, next.key)
                ;(m.deletedAbove ??= []).push(strip)
                tag(next.key, r.source)
            } else if (map.order.length) {
                const lastKey = map.pieces.length ? map.pieces[map.pieces.length - 1].key : map.order[map.order.length - 1]
                const m = marksOf(all, lastKey)
                ;(m.deletedBelow ??= []).push(strip)
                tag(lastKey, r.source)
            }
            continue
        }
        const point = firstInsert ?? (() => { const p = leafPointOf(map, r.curStart); return p ? { key: p.key, start: p.offset } : null })()
        if (!point) continue
        const text = r.oldText.replace(/\n/g, ' ¶ ')
        const m = marksOf(all, point.key)
        tag(point.key, r.source)
        const length = map.lengths.get(point.key) ?? 0
        if (firstInsert) {
            // A replacement: the deleted text before the inserted run.
            const seg = m.segments.find(s => s.rid === r.id && s.start === point.start)
            if (seg) seg.deletedBefore = text
        } else if (point.start < length) m.segments.push({ start: point.start, end: point.start + 1, rid: r.id, inserted: false, deletedBefore: text, source: r.source })
        else if (length > 0) m.segments.push({ start: length - 1, end: length, rid: r.id, inserted: false, deletedAfter: text, source: r.source })
        else (m.deletedAbove ??= []).push({ text, rid: r.id, source: r.source })
    }
    for (const m of all.values()) m.segments = settle(m.segments)
    return all
}

// The runs of a leaf sorted and made not to overlap: a deletion's mark on a character an insertion covers joins the
// insertion's run there (split where needed).
function settle(segments: Segment[]): Segment[] {
    const sorted = [...segments].sort((a, b) => a.start - b.start || (a.inserted === b.inserted ? 0 : a.inserted ? -1 : 1))
    const out: Segment[] = []
    for (const s of sorted) {
        const last = out[out.length - 1]
        if (!last || s.start >= last.end) { out.push({ ...s }); continue }
        if (!s.inserted) {
            // A deletion's mark inside an inserted run: the run split at the mark, the deleted text shown there.
            if (s.start === last.start) { last.deletedBefore = (last.deletedBefore ?? '') + (s.deletedBefore ?? ''); last.deletedAfter = s.deletedAfter ?? last.deletedAfter; continue }
            const tail: Segment = { ...last, start: s.start, deletedBefore: s.deletedBefore, deletedAfter: undefined }
            const keepAfter = last.deletedAfter
            last.end = s.start
            last.deletedAfter = undefined
            if (s.deletedAfter) tail.deletedAfter = s.deletedAfter
            else tail.deletedAfter = keepAfter
            out.push(tail)
            continue
        }
        // Two insertions overlapping cannot happen (revisions do not overlap); one is cut short to be safe.
        if (s.end > last.end) out.push({ ...s, start: last.end })
    }
    return out.filter(s => s.end > s.start)
}
