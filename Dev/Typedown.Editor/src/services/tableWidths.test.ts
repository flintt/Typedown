import { delimiterDashes, shares, widthsFromDelimiter } from './tableWidths'

test('widths are read from a delimiter row of unlike dashes not padded to the cells', () => {
    expect(widthsFromDelimiter('| Name | Note | Value |', '| -------------------- | ------------------------------------------------- | ------------------------------ |'))
        .toEqual([20, 50, 30])
    expect(widthsFromDelimiter('Name | Note', ':----------: | ------------------------------:')).toEqual([25, 75])
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
    expect(widthsFromDelimiter('| a \\| b | c |', '| ---------- | ------------------------------ |')).toEqual([25, 75])
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
