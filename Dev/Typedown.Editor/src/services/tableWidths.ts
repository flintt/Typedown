// A table's column widths, kept in the Markdown itself: the delimiter row's dashes, as a share of the table each - one
// column `| -------------------- |`, the next `| ------------------------------ |` is 20% and 30%. Other editors show the
// table as they always do; Typeleaf draws the columns at those shares.
//
// The app writes a table with each column padded to its content, the dashes to the same width (an aligned table), and
// people write `| --- | --- |`: neither is a width. Widths are read only when the dashes are not all alike and the
// delimiter row is not padded to the header's cells - which is how the app writes widths: the shares as dash counts,
// the cells padded as before.

const MIN_DASHES = 3

// A row's cells as written, spaces kept: the outer pipes off, split at the pipes not escaped.
function segments(line: string): string[] {
    let s = line.replace(/^\s*\|?/, '').replace(/\s*$/, '')
    if (s.endsWith('|') && !s.endsWith('\\|')) s = s.slice(0, -1)
    return s.split(/(?<!\\)\|/)
}

/** The columns' shares (percent, together 100) a table's delimiter row gives, or null for none. */
export function widthsFromDelimiter(headerLine: string, delimiterLine: string): number[] | null {
    const head = segments(headerLine)
    const delimiter = segments(delimiterLine)
    if (head.length !== delimiter.length || delimiter.length < 2) return null
    const dashes = delimiter.map(s => (s.match(/-/g) || []).length)
    if (dashes.some(d => d < 1) || dashes.every(d => d === dashes[0])) return null
    // Padded to the header's cells: an aligned table, not widths.
    if (delimiter.every((s, i) => s.length === head[i].length)) return null
    return shares(dashes)
}

/** Numbers as shares of 100 (rounded, together 100). */
export function shares(values: number[]): number[] {
    const total = values.reduce((a, b) => a + b, 0)
    if (!(total > 0)) return values.map(() => Math.round(100 / values.length))
    const out = values.map(v => Math.max(1, Math.round(v / total * 100)))
    const diff = 100 - out.reduce((a, b) => a + b, 0)
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
