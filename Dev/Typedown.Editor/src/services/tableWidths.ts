// A table's column widths, kept in the Markdown itself: the delimiter row's dashes, each the percent of the page's width
// its column takes - one column `| -------------------- |`, the next `| ------------------------------ |` is 20% and 30%,
// the table half the page. Together 100 or more, the table is as wide as the page and they are its columns' shares (so
// a row of large counts written by hand still means its proportions). Other editors show the table as they always do;
// Typeleaf draws the columns so. (The table's own width is kept so that dragging one column's edge changes that column
// and the next only: the user, 2026-11-13 - a table narrower than the page jumped to the page's width, every column
// with it.)
//
// The app writes a table with each column padded to its content, the dashes to the same width (an aligned table), and
// people write `| --- | --- |`: neither is a width. Widths are read only when the dashes are not all alike and the
// delimiter row is not padded to the header's cells - which is how the app writes widths: the shares as dash counts,
// the cells padded as before.

const MIN_DASHES = 3
// A table of widths together less than this is drawn as wide as the page at their proportions: a short row written by
// hand (`| --- | ------ |`) means proportions, not a table a tenth of the page wide.
export const MIN_TABLE = 20

// A row's cells as written, spaces kept: the outer pipes off, split at the pipes not escaped.
function segments(line: string): string[] {
    let s = line.replace(/^\s*\|?/, '').replace(/\s*$/, '')
    if (s.endsWith('|') && !s.endsWith('\\|')) s = s.slice(0, -1)
    return s.split(/(?<!\\)\|/)
}

/** The columns' widths (percent of the page, together at most 100) a table's delimiter row gives, or null for none. */
export function widthsFromDelimiter(headerLine: string, delimiterLine: string): number[] | null {
    const head = segments(headerLine)
    const delimiter = segments(delimiterLine)
    if (head.length !== delimiter.length || delimiter.length < 2) return null
    const dashes = delimiter.map(s => (s.match(/-/g) || []).length)
    if (dashes.some(d => d < 1) || dashes.every(d => d === dashes[0])) return null
    // Padded to the header's cells: an aligned table, not widths.
    if (delimiter.every((s, i) => s.length === head[i].length)) return null
    const sum = dashes.reduce((a, b) => a + b, 0)
    return sum >= MIN_TABLE && sum < 100 ? dashes : shares(dashes)
}

/** The table's width, percent of the page: its columns' together. */
export function tableWidth(widths: number[]): number {
    return widths.reduce((a, b) => a + b, 0)
}

/** Each column's share of the table, percent (as a header cell's width in a table laid out fixed). */
export function columnShares(widths: number[]): number[] {
    const total = tableWidth(widths)
    return widths.map(w => Math.round(w / total * 10000) / 100)
}

/** Numbers as shares of a total, 100 unless given (rounded, together that total). */
export function shares(values: number[], of = 100): number[] {
    const total = values.reduce((a, b) => a + b, 0)
    const whole = Math.round(of)
    if (!(total > 0)) return values.map(() => Math.round(whole / values.length))
    const out = values.map(v => Math.max(1, Math.round(v / total * whole)))
    const diff = whole - out.reduce((a, b) => a + b, 0)
    const largest = out.indexOf(Math.max(...out))
    out[largest] += diff
    return out
}

/**
 * The dashes to write for each column of a table with widths: its share, at least three. When that happens to be the
 * padded width of every column (so a reader would take it for an aligned table), the widest column gets one more.
 */
export function delimiterDashes(widths: number[], paddedWidths: Array<number | null>): number[] {
    const dashes = widths.map(w => Math.max(MIN_DASHES, Math.round(w)))
    if (dashes.every((d, i) => paddedWidths[i] != null && d + 2 === paddedWidths[i])) {
        const largest = dashes.indexOf(Math.max(...dashes))
        dashes[largest] += 1
    }
    return dashes
}
