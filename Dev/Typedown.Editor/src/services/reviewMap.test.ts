import { trackRevisions, YOU } from './review';
import { ghostsOf, leafMarks, leafPointOf, mapLeaves, markdownOffsetOf } from './reviewMap';

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

// Leaves with the blocks they are in, as Muya's reviewLeaves gives them.
const leaf = (key: string, text: string, ...path: [string, string][]) => ({ key, text, path: path.map(([k, type]) => ({ key: k, type })) })
const ghosts = (baseline: string, current: string, leaves: ReturnType<typeof leaf>[]) => {
    const revisions = trackRevisions(baseline, current, [], { from: 0, to: current.length, source: YOU, time: 1 })
    return ghostsOf(mapLeaves(current, leaves), revisions, current).map(g => ({ kind: g.kind, lines: g.lines, marker: g.marker, key: g.key, where: g.where }))
}

test('a new paragraph has the bar; a deleted one is a block of its own before the next, not in it', () => {
    const leaves = [leaf('a', 'one', ['P1', 'p']), leaf('b', 'two', ['P2', 'p']), leaf('c', 'three', ['P3', 'p'])]
    const { marks: m } = marks('one\n\nthree\n', 'one\n\ntwo\n\nthree\n', leaves)
    expect(m.get('b')!.whole).toBeTruthy()
    expect(m.get('a')).toBeUndefined()
    const after = marks('one\n\ntwo\n\nthree\n', 'one\n\nthree\n', [leaves[0], leaves[2]])
    expect(after.marks.get('c')).toBeUndefined()
    expect(ghosts('one\n\ntwo\n\nthree\n', 'one\n\nthree\n', [leaves[0], leaves[2]])).toEqual([{ kind: 'p', lines: ['two'], marker: undefined, key: 'P3', where: 'before' }])
})

test('a deleted heading, table, formula: each its own kind of block, between the top-level blocks', () => {
    const base = '# Title\n\nText.\n\n## Gone\n\n|a|b|\n|-|-|\n|1|2|\n\n$$\nx^2\n$$\n\nEnd.\n'
    const leaves = [leaf('h', '# Title', ['H', 'h1']), leaf('t', 'Text.', ['P', 'p']), leaf('e', 'End.', ['E', 'p'])]
    expect(ghosts(base, '# Title\n\nText.\n\nEnd.\n', leaves)).toEqual([
        { kind: 'heading', lines: ['Gone'], marker: undefined, key: 'E', where: 'before' },
        { kind: 'table', lines: ['|a|b|', '|-|-|', '|1|2|'], marker: undefined, key: 'E', where: 'before' },
        { kind: 'math', lines: ['x^2'], marker: undefined, key: 'E', where: 'before' },
    ])
})

test('a deleted list item goes among its list\'s items: before the next, after the last, never in another list', () => {
    const base = '- 苹果\n- 香蕉\n- 橙子\n\n1. 第一步\n'
    const li = (k: string, t: string, list: string) => leaf(k, t, [list, 'ul'], ['LI' + k, 'li'], ['P' + k, 'p'])
    const one = li('a', '苹果', 'U'), three = li('c', '橙子', 'U'), step = leaf('s', '第一步', ['O', 'ol'], ['LIs', 'li'], ['Ps', 'p'])
    expect(ghosts(base, '- 苹果\n- 橙子\n\n1. 第一步\n', [one, three, step])).toEqual([{ kind: 'li', lines: ['香蕉'], marker: '•', key: 'LIc', where: 'before' }])
    // The last two: after the item before them, in their order - not into the numbered list below.
    const two = li('b', '香蕉', 'U')
    const g = ghosts(base, '- 苹果\n\n1. 第一步\n', [one, step])
    expect(g.map(x => [x.lines[0], x.key, x.where])).toEqual([['香蕉', 'LIa', 'after'], ['橙子', 'LIa', 'after']])
    expect(two.key).toBe('b')
})

test('a deleted table row goes among the rows', () => {
    const base = '|a|b|\n|-|-|\n|1|2|\n|3|4|\n'
    const cell = (k: string, t: string, row: string) => leaf(k, t, ['F', 'figure'], ['T', 'table'], ['B', 'tbody'], [row, 'tr'])
    const g = ghosts(base, '|a|b|\n|-|-|\n|3|4|\n', [cell('a', 'a', 'R0'), cell('b', 'b', 'R0'), cell('c', '3', 'R2'), cell('d', '4', 'R2')])
    expect(g.map(x => [x.kind, x.key, x.where])).toEqual([['row', 'R2', 'before']])
})

test('two items joined: one "↵" deleted, not the marks; an item split: the new one marked at its left', () => {
    const joined = marks('- 香蕉\n- 橙子\n', '- 香蕉橙子\n', [{ key: 'p', text: '香蕉橙子' }])
    const shown = joined.marks.get('p')!.segments.map(s => s.deletedAfter ?? s.deletedBefore).filter(Boolean)
    expect(shown).toEqual(['↵'])
    const split = marks('- 香蕉\n', '- 香\n- 蕉\n', [{ key: 'a', text: '香' }, { key: 'b', text: '蕉' }])
    expect(split.marks.get('b')!.split).toBeTruthy()
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

test('the paragraphs deleted at the end are after the last block', () => {
    expect(ghosts('one\n\ntwo\n', 'one\n', [leaf('a', 'one', ['P', 'p'])])).toEqual([{ kind: 'p', lines: ['two'], marker: undefined, key: 'P', where: 'after' }])
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

test('the last item of a loose list deleted stays at the end of its list, not at the start of the next one (3.15)', () => {
    const li = (k: string, t: string, list: string) => leaf(k, t, [list, list === 'O' ? 'ol' : 'ul'], ['LI' + k, 'li'], ['P' + k, 'p'])
    const h = leaf('h', '# T', ['H', 'h1']), one = li('a', '一', 'O'), two = li('b', '二', 'O'), jia = li('j', '甲', 'U')
    const base = '# T\n\n1. 一\n\n2. 二\n\n3. 三\n\n- 甲\n'
    const at = (g: { lines: string[], key: string, where: string }[]) => g.map(x => [x.lines[0], x.key, x.where])
    expect(at(ghosts(base, '# T\n\n1. 一\n\n2. 二\n\n- 甲\n', [h, one, two, jia]))).toEqual([['三', 'LIb', 'after']])
    // The first, a middle one, two together: in their list.
    expect(at(ghosts(base, '# T\n\n2. 二\n\n3. 三\n\n- 甲\n', [h, two, li('c', '三', 'O'), jia]))).toEqual([['一', 'LIb', 'before']])
    expect(at(ghosts(base, '# T\n\n1. 一\n\n3. 三\n\n- 甲\n', [h, one, li('c', '三', 'O'), jia]))).toEqual([['二', 'LIc', 'before']])
    expect(at(ghosts(base, '# T\n\n1. 一\n\n- 甲\n', [h, one, jia]))).toEqual([['二', 'LIa', 'after'], ['三', 'LIa', 'after']])
    // A loose bullet list before a numbered one, and a task list.
    expect(at(ghosts('- a\n\n- b\n\n1. x\n', '- a\n\n1. x\n', [li('a', 'a', 'U'), li('x', 'x', 'O')]))).toEqual([['b', 'LIa', 'after']])
    expect(at(ghosts('- [ ] a\n\n- [x] b\n\n1. x\n', '- [ ] a\n\n1. x\n', [li('a', 'a', 'U'), li('x', 'x', 'O')]))).toEqual([['b', 'LIa', 'after']])
})
