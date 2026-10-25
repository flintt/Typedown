// Long documents a tab switch left, kept out of the editor whole so that switching back shows them again instead of
// parsing and drawing their text anew (components/Muya/index.tsx says when one may be kept and when shown).

/** A caret as the host keeps and sends it (its CursorState: anchor and focus, each a line and a ch), compared as text. */
export const caretKey = (cursor: any): string => {
    if (!cursor) return 'null'
    const pos = (p: any) => p && typeof p.line === 'number' && typeof p.ch === 'number' ? { line: p.line, ch: p.ch } : null
    return JSON.stringify({ anchor: pos(cursor.anchor), focus: pos(cursor.focus) })
}

/** Where Muya's caret is in its blocks (a block key and an offset at each end), compared as text. */
export const blockCaretKey = (cursor: any): string => {
    const pos = (p: any) => p ? { key: p.key, offset: p.offset } : null
    return JSON.stringify({ anchor: pos(cursor?.anchor), focus: pos(cursor?.focus) })
}

/** Whether a host caret's anchor comes before its focus (or is it): a new build puts the anchor first. */
export const isForward = (cursor: any): boolean => {
    const a = cursor?.anchor, f = cursor?.focus
    if (!a || !f) return false
    return a.line < f.line || (a.line === f.line && a.ch <= f.ch)
}

/**
 * The text with the two caret markers exactly at the caret's anchor and focus, as Muya's addCursorToMarkdown puts them
 * when it has nothing to move (it moves a caret off a blank line, a fence, a table edge, a list or heading marker).
 * Null when the caret is not a place in the text.
 */
export const markedAt = (text: string, cursor: any, anchorMark: string, focusMark: string): string | null => {
    const lines = text.split('\n')
    const { anchor, focus } = cursor ?? {}
    if (!isForward(cursor)) return null
    const fits = (p: any) => Number.isInteger(p.line) && Number.isInteger(p.ch) && p.line >= 0 && p.ch >= 0 &&
        lines[p.line] !== undefined && p.ch <= lines[p.line].length
    if (!fits(anchor) || !fits(focus)) return null
    const a = lines[anchor.line]
    lines[anchor.line] = a.substring(0, anchor.ch) + anchorMark + a.substring(anchor.ch)
    const f = lines[focus.line]
    const ch = anchor.line === focus.line ? focus.ch + anchorMark.length : focus.ch
    lines[focus.line] = f.substring(0, ch) + focusMark + f.substring(ch)
    return lines.join('\n')
}

/** How many whole lines two texts share from their start. */
export const sameLines = (a: string, b: string): number => {
    let i = 0
    const n = Math.min(a.length, b.length)
    while (i < n && a.charCodeAt(i) === b.charCodeAt(i)) i++
    if (i === a.length && i === b.length) return a.split('\n').length
    let lines = 0
    for (let j = 0; j < i; j++) if (a.charCodeAt(j) === 10) lines++
    return lines
}

/**
 * The documents kept, the most recent first, within a number of documents and a number of characters in all: the
 * oldest goes when either is passed. One per text: keeping a text again replaces the copy kept before.
 */
export class KeptDocuments<T extends { text: string }> {
    private items: T[] = []

    constructor(private readonly maxCount: number, private readonly maxChars: number) { }

    put(item: T) {
        if (this.maxCount <= 0 || item.text.length > this.maxChars) return
        this.items = [item, ...this.items.filter(i => i.text !== item.text)]
        let chars = this.items.reduce((sum, i) => sum + i.text.length, 0)
        while (this.items.length > this.maxCount || chars > this.maxChars) chars -= this.items.pop()?.text.length ?? 0
    }

    /** The document kept with this text, no longer kept (whether or not the caller can show it). */
    take(text: string): T | undefined {
        const index = this.items.findIndex(i => i.text === text)
        if (index < 0) return undefined
        return this.items.splice(index, 1)[0]
    }

    /** A document is kept with this text (it may still not be one that can be shown). */
    has(text: string): boolean {
        return this.items.some(i => i.text === text)
    }

    clear() {
        this.items = []
    }

    get size() {
        return this.items.length
    }
}
