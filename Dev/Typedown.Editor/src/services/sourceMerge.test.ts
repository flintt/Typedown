import { alignment, blockSource, cursorIn, matchLines, mergeSource, splitLines } from './sourceMerge'

const merge = (source: string, base: string, current: string) => {
    const s = splitLines(source), b = splitLines(base)
    return mergeSource(s, b, splitLines(current), alignment(b, s))
}

describe('splitLines', () => {
    it('keeps the line breaks, so the lines join back to the text', () => {
        for (const text of ['', 'a', 'a\n', 'a\nb', 'a\n\nb\n', '\n\n'])
            expect(splitLines(text).join('')).toBe(text)
    })
})

describe('matchLines', () => {
    it('pairs equal lines in order', () => {
        const a = splitLines('x\na\nb\nc\ny\n'), b = splitLines('a\nb\nz\nc\n')
        const pairs = matchLines(a, b).map(([i, j]) => a[i] + '=' + b[j])
        expect(pairs).toEqual(['a\n=a\n', 'b\n=b\n', 'c\n=c\n'])
    })
    it('finds a long run among repeated lines', () => {
        const a = splitLines('-\n-\nmid\n-\n-\n'), b = splitLines('-\nmid\n-\n')
        expect(matchLines(a, b).length).toBe(3)
    })
})

describe('mergeSource', () => {
    // A table written compactly in the source, padded by the export; the paragraph after it edited.
    const source = '# T\n\n|a|b|\n|-|-|\n|1|22|\n\nSome text.\n'
    const base = '# T\n\n| a   | b   |\n| --- | --- |\n| 1   | 22  |\n\nSome text.\n'

    it('keeps the source where the export did not change', () => {
        const current = base.replace('Some text.', 'Some more text.')
        expect(merge(source, base, current)).toBe('# T\n\n|a|b|\n|-|-|\n|1|22|\n\nSome more text.\n')
    })

    it('writes an edited block as the export writes it', () => {
        const current = base.replace('| 1   | 22  |', '| 1   | 223 |')
        // Nothing of the source kept: the edit is in the only block written differently.
        expect(merge(source, base, current)).toBeNull()
    })

    it('keeps lines only the source has, where nothing was edited', () => {
        // A footnote the reader wrote in the middle, the export moved to the end; the first line edited.
        const src = 'a\n\n[^1]: note\n\nb\n\nc\n'
        const b = 'a\n\nb\n\nc\n\n[^1]: note\n'
        const cur = 'aa\n\nb\n\nc\n\n[^1]: note\n'
        // The note stays where the reader wrote it, and is not added at the end as well.
        expect(merge(src, b, cur)).toBe('aa\n\n[^1]: note\n\nb\n\nc\n')
    })

    it('may get an edit next to a moved line wrong - which the caller checks for (Muya reads it back)', () => {
        // The edit is right where the note was moved from: whatever comes out, the caller's check decides.
        const src = 'a\n\n[^1]: note\n\nb\n'
        const b = 'a\n\nb\n\n[^1]: note\n'
        const cur = 'a\n\nbb\n\n[^1]: note\n'
        const merged = merge(src, b, cur)
        expect(merged === null || merged.includes('bb')).toBe(true)
    })

    it('is null when the source is written as the export writes it (nothing to keep)', () => {
        const text = 'a\n\nb\n'
        expect(merge(text, text, 'a\n\nc\n')).toBeNull()
    })

    it('carries several edits over, in order', () => {
        const src = '* one\n* two\n\n|a|b|\n|-|-|\n|1|2|\n\nend\n'
        const b = '- one\n- two\n\n| a   | b   |\n| --- | --- |\n| 1   | 2   |\n\nend\n'
        const cur = '- one\n- two\n- three\n\n| a   | b   |\n| --- | --- |\n| 1   | 2   |\n\nthe end\n'
        // The list was edited (written as the export writes it), the table was not (kept), the last line was edited.
        expect(merge(src, b, cur)).toBe('- one\n- two\n- three\n\n|a|b|\n|-|-|\n|1|2|\n\nthe end\n')
    })
})

describe('cursorIn', () => {
    const at = (line: number, ch: number) => ({ anchor: { line, ch }, focus: { line, ch } })
    it('keeps a caret on a line both texts have, moved to where that line is', () => {
        // Muya's export pads the table; the text the host holds keeps it compact. The caret in "Some text." stays there.
        const exported = '# T\n\n| a   | b   |\n| --- | --- |\n\nSome text.\n'
        const held = '# T\n\n|a|b|\n|-|-|\n\nSome text.\n'
        expect(cursorIn(exported, held, at(5, 4))).toEqual(at(5, 4))
    })
    it('puts a caret on a line only one text has as far past the line before, its column cut to the line', () => {
        const exported = '# T\n\n| a   | b   |\n| --- | --- |\n\nend\n'
        const held = '# T\n\n|a|b|\n|-|-|\n\nend\n'
        expect(cursorIn(exported, held, at(2, 9))).toEqual(at(2, 5))
    })
    it('follows the lines when the texts have a different number of them', () => {
        const exported = 'a\n\nb\n\nc\n\n[^1]: note\n'
        const held = 'a\n\n[^1]: note\n\nb\n\nc\n'
        expect(cursorIn(exported, held, at(4, 1))).toEqual(at(6, 1))
    })
    it('is the caret itself when the texts are the same', () => {
        const c = at(1, 0)
        expect(cursorIn('x\ny\n', 'x\ny\n', c)).toBe(c)
    })
    it('keeps a caret on the empty line after the last line break', () => {
        expect(cursorIn('| a   |\n| --- |\n', '|a|\n|-|\n', at(2, 0))).toEqual(at(2, 0))
    })
})

describe('blockSource', () => {
    // Muya's export of a table pads it; the source keeps it compact. A stand-in for Muya's reading back.
    const pad = (text: string) => text.replace('|a|b|\n|-|-|\n|1|22|', '| a   | b   |\n| --- | --- |\n| 1   | 22  |')
    const table = '| a   | b   |\n| --- | --- |\n| 1   | 22  |'
    const exported = '# T\n\n' + table + '\n\nSome text.\n'
    it('gives the block as the source has it', () => {
        expect(blockSource(table, exported, '# T\n\n|a|b|\n|-|-|\n|1|22|\n\nSome text.\n', pad)).toBe('|a|b|\n|-|-|\n|1|22|')
    })
    it('at the start and at the end of a document', () => {
        expect(blockSource(table, table + '\n\nEnd.\n', '|a|b|\n|-|-|\n|1|22|\n\nEnd.\n', pad)).toBe('|a|b|\n|-|-|\n|1|22|')
        expect(blockSource(table, '# T\n\n' + table + '\n', '# T\n\n|a|b|\n|-|-|\n|1|22|', pad)).toBe('|a|b|\n|-|-|\n|1|22|')
    })
    it('none when the source is the export, or when what is found is not the same block', () => {
        expect(blockSource(table, exported, exported, pad)).toBeNull()
        // The paragraph before the table edited too: the lines between the anchors are more than the table.
        expect(blockSource(table, '# T\n\nNew.\n' + table + '\n\nSome text.\n', '# T\n\nOld.\n|a|b|\n|-|-|\n|1|22|\n\nSome text.\n', pad)).toBeNull()
    })
    it('none for a block found twice in the export', () => {
        expect(blockSource(table, table + '\n\n' + table + '\n', '|a|b|\n|-|-|\n|1|22|\n\n|a|b|\n|-|-|\n|1|22|\n', pad)).toBeNull()
    })
})
