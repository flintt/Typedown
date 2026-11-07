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
    let pos = 0
    // A line not found where it should be (a block whose text is not among the leaves): the next one found, a line of
    // some length, is taken wherever it is - what came after a diagram was all left unmarked (the user, 2026-11-06).
    let missed = false
    for (const leaf of leaves) {
        lengths.set(leaf.key, leaf.text.length)
        texts.set(leaf.key, leaf.text)
        order.push(leaf.key)
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
    return { pieces, lengths, texts, order }
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

// A strip's text: its own line breaks kept (a paragraph a line, a blank line between two not drawn).
const shownDeleted = (text: string) => text.replace(/\n+$/, '').replace(/^\n+/, '').replace(/\n{2,}/g, '\n')

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
