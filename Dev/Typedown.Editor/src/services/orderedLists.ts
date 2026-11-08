// An ordered list's numbers as the document writes them (the user, 2026-11-07): Muya numbers every list on from its
// first item, and a list written all "1." or numbered 8 then 10 came back renumbered after any edit in it - the whole
// list a change, the first-edit notice for it, a review that could not be rejected back to the file. The numbers are
// kept the document's way instead:
//   - numbered on (1, 2, 3 or from n): numbered on after an item put in or taken out;
//   - all one number (all "1." or all "0."): a new item that number too;
//   - anything else (8 then 10): the items not edited keep theirs, a new one the one before it plus one.
// The delimiter ("." or ")") and the first number are the document's either way.

/** An ordered list's item: its line, where its number is in that line, the number. */
interface Item {
    line: number
    from: number
    to: number
    n: number
}

const ITEM = /^((?:[ \t]*>[ \t]?)*)([ \t]*)(\d{1,9})([.)])(?=[ \t]|$)/
const BULLET = /^((?:[ \t]*>[ \t]?)*)([ \t]*)[*+-](?=[ \t]|$)/
const FENCE = /^(?:[ \t]*>[ \t]?)*[ \t]*(`{3,}|~{3,})/

const body = (line: string) => line.endsWith('\n') ? line.slice(0, -1) : line

/** The ordered lists of the lines (each line with or without its line break), their items in order. */
export function orderedLists(lines: string[]): Item[][] {
    const lists: Item[][] = []
    const open: { quotes: number, indent: number, delim: string, items: Item[] }[] = []
    let fence: string | null = null
    let blankBefore = false
    for (let i = 0; i < lines.length; i++) {
        const line = body(lines[i])
        const f = FENCE.exec(line)
        if (fence) { if (f && f[1][0] === fence[0] && f[1].length >= fence.length) fence = null; blankBefore = false; continue }
        if (f) { fence = f[1]; blankBefore = false; continue }
        if (!line.trim()) { blankBefore = true; continue }
        const quotes = (/^((?:[ \t]*>[ \t]?)*)/.exec(line)![1].match(/>/g) ?? []).length
        const m = ITEM.exec(line)
        const b = m ? null : BULLET.exec(line)
        const indent = m ? m[2].length : b ? b[2].length : /^(?:[ \t]*>[ \t]?)*([ \t]*)/.exec(line)![1].length
        // Lists this line ends: out of their quotation; an item of another kind at their level or above; text not
        // indented into them after a blank line.
        for (let k = open.length - 1; k >= 0; k--) {
            const l = open[k]
            const ends = quotes < l.quotes ||
                (m && indent === l.indent && quotes === l.quotes && m[4] !== l.delim) ||
                (m && indent < l.indent && quotes === l.quotes) ||
                (b && indent <= l.indent && quotes === l.quotes) ||
                (!m && !b && blankBefore && indent <= l.indent)
            if (ends) open.splice(k, 1)
        }
        if (m) {
            const item = { line: i, from: m[1].length + m[2].length, to: m[1].length + m[2].length + m[3].length, n: Number(m[3]) }
            const same = open.find(l => l.quotes === quotes && l.indent === indent && l.delim === m[4])
            if (same) same.items.push(item)
            else {
                const list = { quotes, indent, delim: m[4], items: [item] }
                open.push(list)
                lists.push(list.items)
            }
        }
        blankBefore = false
    }
    return lists
}

export type NumberingStyle = 'on' | 'same' | 'own'

/** How a list numbers its items: on from its first, all one number, or its own way. */
export function styleOf(numbers: number[]): NumberingStyle {
    if (numbers.length < 2) return 'on'
    if (numbers.every((n, i) => n === numbers[0] + i)) return 'on'
    if (numbers.every(n => n === numbers[0])) return 'same'
    return 'own'
}

const withNumber = (line: string, item: Item, n: number) => line.slice(0, item.from) + n + line.slice(item.to)

/**
 * The lines with each ordered list numbered as the source's list numbers it: `origins[i]` is the source line the
 * output's line i is (-1: a line Muya wrote, new or edited). A list with none of the source's lines in it is left as it is.
 */
export function numberedAsSource(lines: string[], origins: number[], source: string[]): string[] {
    const sourceLists = orderedLists(source)
    const listOfSourceLine = new Map<number, Item[]>()
    for (const list of sourceLists) for (const item of list) listOfSourceLine.set(item.line, list)
    const out = [...lines]
    for (const list of orderedLists(lines)) {
        const from = list.map(item => listOfSourceLine.get(origins[item.line] ?? -1)).find(Boolean)
        if (!from) continue
        const numbers = from.map(item => item.n)
        const style = styleOf(numbers)
        // Each item's own number in the source: by the line it is, else (an item edited, written by Muya) the source's
        // items no line was kept of, in order, between the ones that were - the item edited in place keeps its number.
        const own = new Map<Item, number>()
        const claimed = new Set<number>()
        for (const item of list) {
            const s = from.find(x => x.line === (origins[item.line] ?? -1))
            if (s) { own.set(item, s.n); claimed.add(s.line) }
        }
        let k = 0
        while (k < list.length) {
            if (own.has(list[k])) { k++; continue }
            let end = k
            while (end < list.length && !own.has(list[end])) end++
            const before = k > 0 ? from.findIndex(x => x.n === own.get(list[k - 1]) && claimed.has(x.line)) : -1
            const after = end < list.length ? from.findIndex(x => x.n === own.get(list[end]) && claimed.has(x.line)) : from.length
            const free = from.slice(before + 1, after < 0 ? from.length : after).filter(x => !claimed.has(x.line))
            for (let j = k; j < end && j - k < free.length; j++) own.set(list[j], free[j - k].n)
            k = end
        }
        const sourceNumber = (item: Item) => own.get(item)
        let previous: number | null = null
        list.forEach((item, k) => {
            const own = sourceNumber(item)
            const n = style === 'on' ? numbers[0] + k
                : style === 'same' ? numbers[0]
                    : own ?? (previous == null ? numbers[0] : previous + 1)
            if (n !== item.n) out[item.line] = withNumber(out[item.line], item, n)
            previous = n
        })
    }
    return out
}

/**
 * After a revision at `at` was rejected (`before` the text then, `after` with it taken back): the list at that place
 * numbered again the way it was numbered before - on, all one number, or left as it is.
 */
export function renumberedAt(before: string, after: string, at: number): string {
    const lineOf = (text: string) => text.slice(0, at).split('\n').length - 1
    const beforeLines = before.split('\n'), afterLines = after.split('\n')
    const near = (lists: Item[][], line: number) => lists.find(l => l[0].line <= line + 1 && l[l.length - 1].line >= line - 1)
    const was = near(orderedLists(beforeLines), lineOf(before))
    const now = near(orderedLists(afterLines), lineOf(after))
    if (!was || !now) return after
    const style = styleOf(was.map(item => item.n))
    if (style === 'own') return after
    const first = was[0].n
    let changed = false
    now.forEach((item, k) => {
        const n = style === 'on' ? first + k : first
        if (n !== item.n) { afterLines[item.line] = withNumber(afterLines[item.line], item, n); changed = true }
    })
    return changed ? afterLines.join('\n') : after
}

/**
 * The text with every ordered list's numbers written as "#": for comparing items, not numbers. Of the same length (a
 * diff's offsets stay the text's), or one "#" for any number (lines matched whatever their numbers).
 */
export function numbersMasked(text: string, sameLength = true): string {
    return text.replace(/^((?:[ \t]*>[ \t]?)*[ \t]*)(\d{1,9})(?=[.)](?:[ \t]|$))/gm, (_, lead: string, digits: string) => lead + '#'.repeat(sameLength ? digits.length : 1))
}
