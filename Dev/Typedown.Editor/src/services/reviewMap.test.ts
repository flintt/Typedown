import { trackRevisions, YOU } from './review';
import { leafMarks, leafPointOf, mapLeaves, markdownOffsetOf } from './reviewMap';

const marks = (baseline: string, current: string, leaves: { key: string, text: string }[]) => {
    const revisions = trackRevisions(baseline, current, [], { from: 0, to: current.length, source: YOU, time: 1 })
    return { revisions, marks: leafMarks(mapLeaves(current, leaves), revisions, current) }
}

test('leaves are found in order, past the markers between them', () => {
    const md = '# Title\n\n> quoted\n> more\n\n- item\n- two\n'
    const map = mapLeaves(md, [{ key: 'h', text: '# Title' }, { key: 'q', text: 'quoted\nmore' }, { key: 'i1', text: 'item' }, { key: 'i2', text: 'two' }])
    expect(map.pieces.map(p => [p.key, p.at, md.slice(p.md, p.md + p.length)])).toEqual([
        ['h', 0, '# Title'], ['q', 0, 'quoted'], ['q', 7, 'more'], ['i1', 0, 'item'], ['i2', 0, 'two']])
    expect(leafPointOf(map, md.indexOf('more') + 2)).toEqual({ key: 'q', offset: 9 })
    expect(markdownOffsetOf(map, 'q', 9)).toBe(md.indexOf('more') + 2)
})

test('a line not found where it should be is left out, not matched further on', () => {
    const md = 'first\n\nsecond paragraph\n'
    const map = mapLeaves(md, [{ key: 'a', text: 'missing' }, { key: 'b', text: 'first' }, { key: 'c', text: 'second paragraph' }])
    expect(map.pieces.map(p => p.key)).toEqual(['b', 'c'])
})

test('an inserted word is a run in its leaf; a replacement shows the deleted text before it', () => {
    const { marks: m } = marks('a cat sat\n', 'a dog sat here\n', [{ key: 'p', text: 'a dog sat here' }])
    const segs = m.get('p')!.segments
    expect(segs.map(s => [s.start, s.end, s.inserted, s.deletedBefore])).toEqual([[2, 5, true, 'cat'], [9, 14, true, undefined]])
})

test('a new paragraph has the bar; a deleted one is a strip above the next', () => {
    const leaves = [{ key: 'a', text: 'one' }, { key: 'b', text: 'two' }, { key: 'c', text: 'three' }]
    let { marks: m } = marks('one\n\nthree\n', 'one\n\ntwo\n\nthree\n', leaves)
    expect(m.get('b')!.whole).toBeTruthy()
    expect(m.get('a')).toBeUndefined()
    ;({ marks: m } = marks('one\n\ntwo\n\nthree\n', 'one\n\nthree\n', [leaves[0], leaves[2]]))
    expect(m.get('c')!.deletedAbove!.map(d => d.text)).toEqual(['two'])
})

test('a word deleted inside a line is shown where it was: after the character before it', () => {
    let { marks: m } = marks('keep this word\n', 'keep word\n', [{ key: 'p', text: 'keep word' }])
    const [seg] = m.get('p')!.segments
    expect(seg.inserted).toBe(false)
    // Drawn right after the character before the deletion: its place, whichever side of the space the diff put it.
    const text = 'keep word'
    expect(text.slice(0, seg.start + 1) + (seg.deletedAfter ?? '')).toMatch(/^keep ?(this ?)$/)
    ;({ marks: m } = marks('keep this\n', 'keep\n', [{ key: 'p', text: 'keep' }]))
    expect(m.get('p')!.segments[0].deletedAfter).toBe(' this')
    expect(m.get('p')!.segments[0].start).toBe(3)
})

test('a deletion beside a mark the editor hides is drawn on a character that shows', () => {
    // Where the deletion was, and the leaf's text: the mark must be on a character that is not *, `, [ or the like.
    const cases: [string, string, string][] = [
        ['a x **bold** c\n', 'a **bold** c\n', 'a **bold** c'],
        ['a **bold** x c\n', 'a **bold** c\n', 'a **bold** c'],
        ['see x [link](http://e.com) end\n', 'see [link](http://e.com) end\n', 'see [link](http://e.com) end'],
        ['run x `code` now\n', 'run `code` now\n', 'run `code` now'],
        ['# Big x title\n', '# Big title\n', '# Big title'],
        ['x **start** here\n', '**start** here\n', '**start** here'],
        ['end **here** x\n', 'end **here**\n', 'end **here**'],
    ]
    for (const [baseline, current, leaf] of cases) {
        const { revisions, marks: m } = marks(baseline, current, [{ key: 'p', text: leaf }])
        const seg = m.get('p')!.segments.find(s => s.deletedBefore || s.deletedAfter)!
        expect(seg).toBeTruthy()
        expect(leaf[seg.start]).not.toMatch(/[*`[\]()#]/)
        // The place it is drawn at, in the leaf's text, is where the deletion was or only marks away from it.
        const drawnAt = seg.deletedAfter ? seg.start + 1 : seg.start
        const at = revisions[0].curStart - (current.indexOf(leaf))
        const between = leaf.slice(Math.min(at, drawnAt), Math.max(at, drawnAt))
        expect(between).toMatch(/^[*`[\]()#]*$/)
    }
})

test('two revisions side by side in a run keep a mark each', () => {
    // An insertion of the program's and the reader's deletion right inside it: the deletion has its own mark.
    const baseline = 'one two three\n'
    let revisions = trackRevisions(baseline, 'one two NEW three\n', [], { from: 8, to: 12, source: { name: 'Bot' }, time: 1 })
    revisions = trackRevisions(baseline, 'one NEW three\n', revisions, { from: 4, to: 4, oldTo: 8, source: YOU, time: 2 })
    const m = leafMarks(mapLeaves('one NEW three\n', [{ key: 'p', text: 'one NEW three' }]), revisions, 'one NEW three\n')
    const ids = new Set(m.get('p')!.segments.map(s => s.rid))
    expect(ids.size).toBe(revisions.length)
})

test('the paragraphs deleted at the end are below the last leaf', () => {
    const { marks: m } = marks('one\n\ntwo\n', 'one\n', [{ key: 'a', text: 'one' }])
    expect(m.get('a')!.deletedBelow!.map(d => d.text)).toEqual(['two'])
})

test('a program revision tags its leaf', () => {
    const revisions = trackRevisions('x\n', 'x y\n', [], { from: 0, to: 4, source: { name: 'Bot', session: '1' }, time: 1 })
    const m = leafMarks(mapLeaves('x y\n', [{ key: 'p', text: 'x y' }]), revisions, 'x y\n')
    expect(m.get('p')!.sources).toEqual(['Bot'])
})

test('after a diagram (its fence with "mermaid", no leaf of its own) the leaves are found on', () => {
    const md = 'before\n\n```mermaid\ngraph TD\n  A-->B\n```\n\nafter one\n\nafter two\n'
    const map = mapLeaves(md, [{ key: 'a', text: 'before' }, { key: 'd', text: 'graph TD\n  A-->B' }, { key: 'b', text: 'after one' }, { key: 'c', text: 'after two' }])
    expect(map.pieces.map(p => p.key)).toEqual(['a', 'd', 'd', 'b', 'c'])
})

test('after a footnote definition and a formula block the leaves are found on', () => {
    const md = 'Text[^note].\n\n[^note]: The note.\n\n$$\nx^2\n$$\n\nAfter.\n'
    const map = mapLeaves(md, [{ key: 't', text: 'Text[^note].' }, { key: 'n', text: 'The note.' }, { key: 'm', text: 'x^2' }, { key: 'a', text: 'After.' }])
    expect(map.pieces.map(p => p.key)).toEqual(['t', 'n', 'm', 'a'])
})

test('a block whose text is in no leaf (a drawing) is passed over: the next leaf of some length is found after it', () => {
    const md = 'one\n\n<svg>drawn</svg>\n\nsecond paragraph\n'
    const map = mapLeaves(md, [{ key: 'a', text: 'one' }, { key: 'x', text: 'not in the text' }, { key: 'b', text: 'second paragraph' }])
    expect(map.pieces.map(p => p.key)).toEqual(['a', 'b'])
})

test('a footnote definition Muya keeps at the end is found where the text has it, and the leaves after it too', () => {
    const md = 'Text[^1].\n\n[^1]: The note.\n\nAfter here.\n\nLast one.\n'
    const map = mapLeaves(md, [{ key: 't', text: 'Text[^1].' }, { key: 'a', text: 'After here.' }, { key: 'l', text: 'Last one.' }, { key: 'n', text: 'The note.' }])
    expect(map.pieces.map(p => p.key)).toEqual(['t', 'n', 'a', 'l'])
})
