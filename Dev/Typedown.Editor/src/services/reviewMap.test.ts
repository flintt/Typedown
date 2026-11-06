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

test('a word deleted inside a line is shown where it was, at the line end after the last character', () => {
    let { marks: m } = marks('keep this word\n', 'keep word\n', [{ key: 'p', text: 'keep word' }])
    const [seg] = m.get('p')!.segments
    expect(seg.inserted).toBe(false)
    expect(['this ', ' this']).toContain(seg.deletedBefore)
    ;({ marks: m } = marks('keep this\n', 'keep\n', [{ key: 'p', text: 'keep' }]))
    expect(m.get('p')!.segments[0].deletedAfter).toBe(' this')
    expect(m.get('p')!.segments[0].start).toBe(3)
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
