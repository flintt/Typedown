import { alignment, blockSource, cursorIn, editedRegion, matchLines, mergeKeepingNotes, mergeSource, splitLines } from './sourceMerge'
import { numbersMasked, orderedLists, renumberedAt, styleOf } from './orderedLists'

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

    it('keeps an edited table written its own way: the other rows as they were, the edited one as compact', () => {
        const current = base.replace('| 1   | 22  |', '| 1   | 223 |')
        expect(merge(source, base, current)).toBe(source.replace('|1|22|', '|1|223|'))
    })

    it('a table written with one space at its pipes keeps that', () => {
        const src = '| a | b |\n| --- | --- |\n| 1 | 22 |\n'
        const b = '| a   | b   |\n| --- | --- |\n| 1   | 22  |\n'
        expect(merge(src, b, b.replace('| 1   | 22  |', '| 1   | 223 |'))).toBe('| a | b |\n| --- | --- |\n| 1 | 223 |\n')
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

describe('editedRegion (an edited block keeps its own writing where the edit did not change it)', () => {
    it('keeps a ~~~ fence when the code inside it is edited', () => {
        const src = '# C\n\n~~~js\nlet a = 1;\n~~~\n\nAfter.\n'
        const b = '# C\n\n```js\nlet a = 1;\n```\n\nAfter.\n'
        expect(merge(src, b, b.replace('let a', 'let aZ'))).toBe(src.replace('let a', 'let aZ'))
    })
    it('keeps a longer fence of its own length', () => {
        const src = '````\ncode\n````\n'
        const b = '```\ncode\n```\n'
        expect(merge(src, b, '```\ncodeZ\n```\n')).toBe('````\ncodeZ\n````\n')
    })
    it('does not add the blank line Muya puts after a quotation with a lazy line', () => {
        const src = '> quoted line\nlazy continuation\n\nAfter.\n'
        const b = '> quoted line\n\nlazy continuation\n\nAfter.\n'
        const cur = '> quoted lZine\n\nlazy continuation\n\nAfter.\n'
        expect(merge(src, b, cur)).toBe('> quoted lZine\nlazy continuation\n\nAfter.\n')
    })
    it('keeps the blank lines of what is pasted into an empty document (v1.4.2 tag build: paste-web-check)', () => {
        // The stretch the paste replaced has no source lines; Muya's one blank line for the empty document lines up with
        // the blank line between the paragraph and the code, which must stay.
        const cur = ['Code follows:', '', '```python', 'x', '```']
        expect(editedRegion([], [''], cur).lines).toEqual(cur)
    })
    it('takes the edited lines as Muya writes them', () => {
        const src = '~~~\na\n~~~\n'
        const b = '```\na\n```\n'
        expect(merge(src, b, '```\na\nb\n```\n')).toBe('~~~\na\nb\n~~~\n')
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

describe('mergeKeepingNotes (a footnote definition stays where the source has it)', () => {
    const src = '# F\n\nText with note[^1].\n\n[^1]: The note.\n\nAfter the note here.\n'
    const base = '# F\n\nText with note[^1].\n\nAfter the note here.\n\n[^1]: The note.\n'
    const keep = (current: string) => mergeKeepingNotes(splitLines(src), splitLines(base), splitLines(current))
    it('an edit of the paragraph after the definition', () => {
        expect(keep(base.replace('After the', 'After theZ'))).toBe(src.replace('After the', 'After theZ'))
    })
    it('an edit of the definition itself, in its place', () => {
        expect(keep(base.replace('The note.', 'The nZote.'))).toBe(src.replace('The note.', 'The nZote.'))
    })
    it('an edit before it', () => {
        expect(keep(base.replace('Text with', 'Text withZ'))).toBe(src.replace('Text with', 'Text withZ'))
    })
    it('nothing to keep without definitions', () => {
        expect(mergeKeepingNotes(splitLines('a\n'), splitLines('a\n'), splitLines('b\n'))).toBeNull()
    })
})

describe('an edited list item keeps its own marker spacing', () => {
    it('a tab after the marker', () => {
        expect(merge('-\titem one\n-\titem two\n', '- item one\n- item two\n', '- item oneZ\n- item two\n')).toBe('-\titem oneZ\n-\titem two\n')
    })
})

describe('an ordered list numbered the document\'s way (orderedLists.ts)', () => {
    // Muya's export: every list numbered on from its first item.
    const muya = (text: string) => {
        const lines = text.split('\n')
        for (const list of orderedLists(lines)) list.forEach((item, k) => {
            lines[item.line] = lines[item.line].slice(0, item.from) + (list[0].n + k) + lines[item.line].slice(item.to)
        })
        return lines.join('\n')
    }
    // The document after an edit Muya wrote as `edited` (its export of the edited text).
    const after = (source: string, edited: string) => merge(source, muya(source), muya(edited)) ?? muya(edited)
    const edits = (x: string) => ({
        // (Before the first item: Muya keeps the list's first number.)
        first: x.replace(/^(\d+[.)])/, '$1 new\n$1'),
        middle: x.replace(/(\n)(?=\S+[.)] b)/, '\nN. new\n'),
        end: x + 'N. new\n',
        deleted: x.replace(/^\S+ b\n/m, ''),
        split: x.replace(/(\S+[.)]) b\n/, '$1 b1\nN. b2\n'),
        joined: x.replace(/ b\n\S+ c\n/, ' bc\n'),
    })
    const cases: [string, string, Record<string, string>][] = [
        ['numbered on from 3', '3. a\n4. b\n5. c\n', {
            first: '3. new\n4. a\n5. b\n6. c\n', middle: '3. a\n4. new\n5. b\n6. c\n', end: '3. a\n4. b\n5. c\n6. new\n',
            deleted: '3. a\n4. c\n', split: '3. a\n4. b1\n5. b2\n6. c\n', joined: '3. a\n4. bc\n',
        }],
        ['all one number', '1. a\n1. b\n1. c\n', {
            first: '1. new\n1. a\n1. b\n1. c\n', middle: '1. a\n1. new\n1. b\n1. c\n', end: '1. a\n1. b\n1. c\n1. new\n',
            deleted: '1. a\n1. c\n', split: '1. a\n1. b1\n1. b2\n1. c\n', joined: '1. a\n1. bc\n',
        }],
        ['its own numbers', '8. a\n10. b\n11. c\n', {
            first: '8. new\n8. a\n10. b\n11. c\n', middle: '8. a\n9. new\n10. b\n11. c\n', end: '8. a\n10. b\n11. c\n12. new\n',
            deleted: '8. a\n11. c\n', split: '8. a\n10. b1\n11. b2\n11. c\n', joined: '8. a\n10. bc\n',
        }],
        ['"1)" all one number', '1) a\n1) b\n1) c\n', {
            first: '1) new\n1) a\n1) b\n1) c\n', middle: '1) a\n1) new\n1) b\n1) c\n', end: '1) a\n1) b\n1) c\n1) new\n',
            deleted: '1) a\n1) c\n', split: '1) a\n1) b1\n1) b2\n1) c\n', joined: '1) a\n1) bc\n',
        }],
    ]
    for (const [name, source, want] of cases) {
        for (const [edit, edited] of Object.entries(edits(source))) {
            it(`${name}: ${edit}`, () => {
                const text = edited.replace(/N\./g, '1' + (source.includes(')') ? ')' : '.'))
                expect(after(source, text)).toBe(want[edit])
            })
        }
    }
    it('a list in a quotation, a nested one, and one after another list are each their own', () => {
        const source = '> 1. a\n> 1. b\n\n- x\n  1. p\n  1. q\n\n5. m\n7. n\n'
        const edited = source.replace('> 1. b', '> 1. b\n> 1. c').replace('  1. q', '  1. q\n  1. r').replace('7. n', '7. n\n8. o')
        expect(after(source, edited)).toBe('> 1. a\n> 1. b\n> 1. c\n\n- x\n  1. p\n  1. q\n  1. r\n\n5. m\n7. n\n8. o\n')
    })
    it('a rejected item numbered back the way the list was numbered', () => {
        expect(renumberedAt('1. a\n2. new\n3. b\n4. c\n', '1. a\n3. b\n4. c\n', 5)).toBe('1. a\n2. b\n3. c\n')
        expect(renumberedAt('1. a\n2. c\n', '1. a\n2. b\n2. c\n', 5)).toBe('1. a\n2. b\n3. c\n')
        expect(renumberedAt('1. a\n1. c\n', '1. a\n1. b\n1. c\n', 5)).toBe('1. a\n1. b\n1. c\n')
        expect(renumberedAt('8. a\n11. c\n', '8. a\n10. b\n11. c\n', 5)).toBe('8. a\n10. b\n11. c\n')
    })
    it('numbers masked for comparing items keep the text\'s length', () => {
        expect(numbersMasked('10. a\n> 3) b\n  7. c\nnot 5. this\n')).toBe('##. a\n> #) b\n  #. c\nnot 5. this\n')
        expect(styleOf([3, 4, 5])).toBe('on')
        expect(styleOf([1, 1])).toBe('same')
        expect(styleOf([8, 10])).toBe('own')
    })
})
