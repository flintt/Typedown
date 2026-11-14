import { columnShares, delimiterDashes, MIN_TABLE, shares, tableWidth, widthsFromDelimiter } from './tableWidths'

test('widths are read from a delimiter row of unlike dashes not padded to the cells: percents of the page', () => {
    expect(widthsFromDelimiter('| Name | Note | Value |', '| -------------------- | -------------------------------------------------- | ------------------------------ |'))
        .toEqual([20, 50, 30])
    // Together less than 100: a table narrower than the page, as it was when its widths were set.
    expect(widthsFromDelimiter('Name | Note', ':----------: | ------------------------------:')).toEqual([10, 30])
    // Together 100 or more (large counts written by hand): proportions, the table as wide as the page.
    expect(widthsFromDelimiter('| a | b |', '| ' + '-'.repeat(60) + ' | ' + '-'.repeat(180) + ' |')).toEqual([25, 75])
    // Together less than MIN_TABLE (a short row written by hand): proportions too, not a sliver of a table.
    expect(MIN_TABLE).toBe(20)
    expect(widthsFromDelimiter('| a | b |', '| ---- | ------------ |')).toEqual([25, 75])
})

test('a table\'s width is its columns\' together; each column\'s share of the table as a header cell\'s width', () => {
    expect(tableWidth([10, 30])).toBe(40)
    expect(columnShares([10, 30])).toEqual([25, 75])
    expect(columnShares([20, 50, 30])).toEqual([20, 50, 30])
    // Shares of a total other than 100: a column put in or taken out leaves the table as wide.
    expect(shares([10, 15, 15], 40)).toEqual([10, 15, 15])
    expect(shares([1, 1, 1], 40).reduce((a, b) => a + b, 0)).toBe(40)
})

test('no widths in a table as people write it or as the app aligns it', () => {
    expect(widthsFromDelimiter('| Name | Note |', '| --- | --- |')).toBeNull()
    expect(widthsFromDelimiter('| Name | Note |', '|---|---|')).toBeNull()
    // Aligned: the dashes as wide as the padded cells.
    expect(widthsFromDelimiter('| Name  | A longer note |', '| ----- | ------------- |')).toBeNull()
    expect(widthsFromDelimiter('| Name  | Long note |', '| :---- | --------: |')).toBeNull()
    // Not a delimiter of this header.
    expect(widthsFromDelimiter('| a | b | c |', '| --- | ------ |')).toBeNull()
})

test('a pipe escaped in a header cell is part of the cell', () => {
    expect(widthsFromDelimiter('| a \\| b | c |', '| ---------- | ------------------------------ |')).toEqual([10, 30])
})

test('shares add up to 100, and the dashes written read back as the same widths', () => {
    expect(shares([1, 1, 1]).reduce((a, b) => a + b, 0)).toBe(100)
    const widths = [20, 30, 50]
    const dashes = delimiterDashes(widths, [7, 9, 12])
    const delimiter = '| ' + dashes.map(d => '-'.repeat(d)).join(' | ') + ' |'
    expect(widthsFromDelimiter('| Name  | Value   | Note       |', delimiter)).toEqual(widths)
    // The shares happening to be the padded widths: one more dash, still not read as an aligned table.
    const same = delimiterDashes([5, 95], [7, 97])
    expect(same).toEqual([5, 96])
})
