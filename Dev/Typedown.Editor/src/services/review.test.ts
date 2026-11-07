import {
    acceptAll, acceptInto, changedRegion, countRevisions, diffSequences, diffTexts, nextRevision, rejectAll, rejectFrom,
    groupFormats, onlyWriting, REVISION_PAUSE_MS, Revision, revisionAt, shiftAfterAccept, storedOf, tokenize, trackRevisions, YOU,
} from './review';

const apply = (a: string[], b: string[]) => {
    // The ops applied to a give b.
    const out: string[] = []
    let i = 0
    for (const op of diffSequences(a, b)) {
        out.push(...a.slice(i, op.a), ...b.slice(op.b, op.b + op.bLen))
        i = op.a + op.aLen
    }
    out.push(...a.slice(i))
    return out
}

// The hunks applied to the baseline give the current text, and each hunk's text is what the two texts have there.
const roundTrip = (baseline: string, current: string) => {
    const hunks = diffTexts(baseline, current)
    let text = baseline
    for (const h of [...hunks].reverse()) text = text.slice(0, h.baseStart) + current.slice(h.curStart, h.curEnd) + text.slice(h.baseEnd)
    expect(text).toBe(current)
    return hunks.map(h => [baseline.slice(h.baseStart, h.baseEnd), current.slice(h.curStart, h.curEnd)])
}

const you = (time = 1) => ({ source: YOU, time })
const program = (name: string, time = 2) => ({ source: { name, session: 's1' }, time })

test('the sequence diff turns one into the other', () => {
    const cases = [['abcabba', 'cbabac'], ['', 'abc'], ['abc', ''], ['abc', 'abc'], ['axbxc', 'abc'], ['kitten', 'sitting']]
    for (const [a, b] of cases) expect(apply(a.split(''), b.split('')).join('')).toBe(b)
    const random = (n: number, seed: number) => Array.from({ length: n }, (_, i) => 'abcd'[(seed * 31 + i * i * 7) % 4])
    for (let s = 1; s < 30; s++) {
        const a = random(20 + s, s), b = random(25, s + 3)
        expect(apply(a, b).join('')).toBe(b.join(''))
    }
})

test('words are tokens; CJK characters one each', () => {
    expect(tokenize('the cat, 中文')).toEqual(['the', ' ', 'cat', ',', ' ', '中', '文'])
})

test('a replaced word is one revision, not its letters', () => {
    expect(roundTrip('the cat sat\n', 'the dog sat\n')).toEqual([['cat', 'dog']])
})

test('two words replaced a space apart stay two (each may have another source)', () => {
    expect(roundTrip('the cat sat\n', 'a dog sat\n')).toEqual([['the', 'a'], ['cat', 'dog']])
})

test('a paragraph added between two is that paragraph and its blank line', () => {
    const pairs = roundTrip('a\n\nc\n', 'a\n\nb\n\nc\n')
    expect(pairs).toEqual([['', 'b\n\n']])
})

test('a paragraph deleted', () => {
    expect(roundTrip('a\n\nb\n\nc\n', 'a\n\nc\n')).toEqual([['b\n\n', '']])
})

test('a paragraph edited beside one deleted: the edit in its words, the other deleted whole', () => {
    // The fewest words changed: the deleted paragraph's words may serve the edited one's, no more is marked than that.
    const pairs = roundTrip('Second paragraph.\n\nThird paragraph.\n', 'Second paragraph, revised.\n')
    expect(pairs.reduce((n, [a, b]) => n + a.length + b.length, 0)).toBeLessThanOrEqual(27)
})

test('Chinese edits are by character', () => {
    expect(roundTrip('今天天气很好。\n', '今天天气不好。\n')).toEqual([['很', '不']])
})

test('many scattered changes round-trip', () => {
    const base = Array.from({ length: 200 }, (_, i) => `line ${i} has some words`).join('\n')
    const cur = base.replace('line 5 ', 'line five ').replace('line 120 has', 'line 120 had').replace('line 199 has some words', '')
    roundTrip(base, cur)
})

test('a revision keeps its source while the reader types elsewhere', () => {
    const baseline = 'one\n\ntwo\n'
    const afterProgram = 'one more\n\ntwo\n'
    let revs = trackRevisions(baseline, afterProgram, [], { ...changedRegion('one\n\ntwo\n', afterProgram)!, ...program('Bot') })
    expect(revs).toHaveLength(1)
    expect(revs[0].source).toBe('Bot')
    const typed = 'one more\n\ntwo three\n'
    revs = trackRevisions(baseline, typed, revs, { ...changedRegion(afterProgram, typed)!, ...you(5) })
    expect(revs.map(r => [r.newText, r.source])).toEqual([[' more', 'Bot'], [' three', '']])
    expect(revs[0].time).toBe(2)
})

test('typing on at the end of an insertion of ones own keeps its id', () => {
    const baseline = 'a\n'
    let revs = trackRevisions(baseline, 'a b\n', [], { ...changedRegion(baseline, 'a b\n')!, ...you() })
    const id = revs[0].id
    revs = trackRevisions(baseline, 'a bc\n', revs, { ...changedRegion('a b\n', 'a bc\n')!, ...you(3) })
    expect(revs).toHaveLength(1)
    expect(revs[0].id).toBe(id)
    expect(revs[0].newText).toBe(' bc')
})

test('the reader typing inside a program insertion: the program keeps its text, the letters typed are the reader\'s', () => {
    const baseline = 'a\n\nb\n'
    const bot = 'a\n\nRewritten by the bot.\n\nb\n'
    let revs = trackRevisions(baseline, bot, [], { ...changedRegion(baseline, bot)!, ...program('Bot') })
    expect(revs.map(r => r.source)).toEqual(['Bot'])
    const typed = bot.replace('the bot.', 'the bot. Mine')
    revs = trackRevisions(baseline, typed, revs, { ...changedRegion(bot, typed)!, ...you(9) })
    expect(revs.map(r => [r.newText, r.source])).toEqual([['Rewritten by the bot.', 'Bot'], [' Mine', ''], ['\n\n', 'Bot']])
    // Typing on elsewhere keeps the three as they are.
    const more = typed.replace('a\n', 'a!\n')
    revs = trackRevisions(baseline, more, revs, { ...changedRegion(typed, more)!, ...you(10) })
    expect(revs[0].source).toBe('')
    expect(revs.filter(r => r.source === '').map(r => r.newText)).toEqual(['!', ' Mine'])
    expect(revs.filter(r => r.source === 'Bot').map(r => r.newText).join('').trim()).toBe('Rewritten by the bot.')
    // All of the program's accepted: the baseline has its text, the reader's are left.
    const bots = revs.filter(r => r.source === 'Bot')
    const accepted = acceptAll(baseline, bots)
    expect(accepted).toBe(bot)
    const left = trackRevisions(accepted, more, shiftAfterAccept(revs, bots), { source: YOU, time: 11 })
    expect(left.map(r => [r.newText, r.source])).toEqual([['!', ''], [' Mine', '']])
    // Rejecting the reader's typing from the program's text: the program's stays.
    const mine = revs.find(r => r.newText === ' Mine')!
    const back = rejectFrom(more, mine)
    const after = trackRevisions(baseline, back, revs.filter(r => r !== mine), { ...changedRegion(more, back)!, ...you(12) })
    expect(after.map(r => r.source)).toEqual(['', 'Bot'])
    expect(after[1].newText.trim()).toBe('Rewritten by the bot.')
})

test('the reader editing a letter inside a program insertion: that letter is the reader\'s', () => {
    const baseline = 'a\n'
    let revs = trackRevisions(baseline, 'a xyz\n', [], { ...changedRegion(baseline, 'a xyz\n')!, ...program('Bot') })
    revs = trackRevisions(baseline, 'a xYz\n', revs, { ...changedRegion('a xyz\n', 'a xYz\n')!, ...you(9) })
    expect(revs.map(r => [r.newText, r.source])).toEqual([[' x', 'Bot'], ['Y', ''], ['z', 'Bot']])
})

test('accept writes into the baseline, reject puts the baseline back; the other revisions stay', () => {
    const baseline = 'alpha beta gamma\n'
    const current = 'alpha BETA gamma delta\n'
    const revs = trackRevisions(baseline, current, [], { from: 0, to: current.length, ...you() })
    expect(revs.map(r => [r.kind, r.oldText, r.newText])).toEqual([['replace', 'beta', 'BETA'], ['insert', '', ' delta']])
    // Accept the first: only the second is left, with its id.
    const accepted = acceptInto(baseline, revs[0])
    const left = trackRevisions(accepted, current, shiftAfterAccept(revs, [revs[0]]), { ...you(7) })
    expect(left.map(r => [r.id, r.newText])).toEqual([[revs[1].id, ' delta']])
    expect(left[0].time).toBe(1)
    // Reject the second: the document as the baseline there.
    const rejected = rejectFrom(current, revs[1])
    expect(rejected).toBe('alpha BETA gamma\n')
    const after = trackRevisions(baseline, rejected, revs, { ...changedRegion(current, rejected)!, ...you(8) })
    expect(after.map(r => r.id)).toEqual([revs[0].id])
})

test('accept all and reject all by source', () => {
    const baseline = 'a b c\n'
    let revs = trackRevisions(baseline, 'a B c\n', [], { ...changedRegion(baseline, 'a B c\n')!, ...program('Bot') })
    revs = trackRevisions(baseline, 'a B C\n', revs, { ...changedRegion('a B c\n', 'a B C\n')!, ...you() })
    const bot = revs.filter(r => r.source === 'Bot')
    expect(acceptAll(baseline, bot)).toBe('a B c\n')
    expect(rejectAll('a B C\n', bot)).toBe('a b C\n')
    expect(countRevisions(revs)).toEqual({ inserted: 2, deleted: 2, sources: [{ name: 'Bot', count: 1 }, { name: '', count: 1 }] })
})

test('revisions stored and read back are matched by their place in the baseline', () => {
    const baseline = 'x\n\ny\n'
    const current = 'x!\n\ny?\n'
    const revs = trackRevisions(baseline, current, [], { from: 0, to: current.length, ...program('Bot') })
    const stored = revs.map(r => storedOf(r, current))
    expect(stored[1].line).toBe(3)
    expect(stored[1].block).toBe('y?')
    const again = trackRevisions(baseline, current, stored, { source: YOU, time: 99 })
    expect(again.map(r => [r.id, r.source])).toEqual(revs.map(r => [r.id, 'Bot']))
})

test('the revision at the caret, the next and the previous', () => {
    const current = 'a X b Y c\n'
    const revs: Revision[] = trackRevisions('a b c\n', current, [], { from: 0, to: current.length, ...you() })
    expect(revs).toHaveLength(2)
    const [x, y] = revs.map(r => r.newText.trim())
    expect([x, y]).toEqual(['X', 'Y'])
    expect(revisionAt(revs, 3)?.newText.trim()).toBe('X')
    expect(revisionAt(revs, 0)).toBeNull()
    expect(nextRevision(revs, 0)?.newText.trim()).toBe('X')
    expect(nextRevision(revs, revs[0].curStart)?.newText.trim()).toBe('Y')
    expect(nextRevision(revs, revs[1].curStart)?.newText.trim()).toBe('X')
    expect(nextRevision(revs, 0, true)?.newText.trim()).toBe('Y')
})

test('only the writing changed: list numbers, markers, fences, emphasis marks, blank lines - not the words', () => {
    const only = (a: string, b: string) => diffTexts(a, b).every(h => onlyWriting(a, b, h))
    expect(only('3. three\n4. four\n5. five\n', '3. three\n4. four\n6. five\n')).toBe(true)
    expect(only('~~~js\ncode\n~~~\n', '```js\ncode\n```\n')).toBe(true)
    expect(only('* one\n', '- one\n')).toBe(true)
    expect(only('a __b__ c\n', 'a **b** c\n')).toBe(true)
    expect(only('> a\nb\n', '> a\n\nb\n')).toBe(true)
    expect(only('a word\n', 'a ward\n')).toBe(false)
    expect(only('5. five\n', '5. fives\n')).toBe(false)
})

test('a bold put on with Ctrl+B is one group of its two marks', () => {
    const baseline = 'Plain words here.\n'
    const current = 'Plain **words** here.\n'
    const revs = trackRevisions(baseline, current, [], { ...changedRegion(baseline, current)!, ...you() })
    groupFormats(revs, current, changedRegion(baseline, current))
    expect(revs.map(r => r.newText)).toEqual(['**', '**'])
    expect(revs[0].group).toBe(revs[0].id)
    expect(revs[1].group).toBe(revs[0].id)
    expect(revs[0].format).toBe('bold')
    expect(countRevisions(revs).inserted).toBe(1)
    // Kept through the next change elsewhere.
    const more = current + '\nMore.\n'
    const again = trackRevisions(baseline, more, revs, { ...changedRegion(current, more)!, ...you(3) })
    expect(again.filter(r => r.group === revs[0].id).length).toBe(2)
})

test('list items written at different times are revisions of their own: rejecting the last leaves the one before', () => {
    const baseline = '# L\n\n- one\n'
    const one = baseline + '- two\n'
    let revs = trackRevisions(baseline, one, [], { ...changedRegion(baseline, one)!, source: YOU, time: 0 })
    expect(revs.map(r => r.newText)).toEqual(['- two\n'])
    // Later than the pause: the next item typed on at the end is another revision.
    const two = one + '- three\n'
    revs = trackRevisions(baseline, two, revs, { ...changedRegion(one, two)!, source: YOU, time: REVISION_PAUSE_MS + 1 })
    expect(revs.map(r => r.newText)).toEqual(['- two\n', '- three\n'])
    expect(revs[0].time).toBe(0)
    const back = rejectFrom(two, revs[1])
    expect(back).toBe(one)
    const after = trackRevisions(baseline, back, revs.filter(r => r !== revs[1]), { ...changedRegion(two, back)!, source: YOU, time: REVISION_PAUSE_MS + 2 })
    expect(after.map(r => [r.id, r.newText])).toEqual([[revs[0].id, '- two\n']])
    // Accepting the first alone leaves the second.
    const left = trackRevisions(acceptInto(baseline, revs[0]), two, shiftAfterAccept(revs, [revs[0]]), { source: YOU, time: REVISION_PAUSE_MS + 3 })
    expect(left.map(r => [r.id, r.newText])).toEqual([[revs[1].id, '- three\n']])
})

test('typed on at once, two paragraphs are still two revisions (a block each); a word typed on is the same one', () => {
    const baseline = 'a\n'
    const t1 = baseline + '\nfirst para\n'
    let revs = trackRevisions(baseline, t1, [], { ...changedRegion(baseline, t1)!, source: YOU, time: 0 })
    const t2 = t1 + '\nsecond para\n'
    revs = trackRevisions(baseline, t2, revs, { ...changedRegion(t1, t2)!, source: YOU, time: 1000 })
    expect(revs.length).toBe(2)
    const t3 = t2.replace('second para', 'second paragraph')
    const again = trackRevisions(baseline, t3, revs, { ...changedRegion(t2, t3)!, source: YOU, time: 2000 })
    expect(again.map(r => r.id)).toEqual(revs.map(r => r.id))
    // After the caret went elsewhere and came back, more typed at the end is a revision of its own.
    const t4 = t3.replace('second paragraph', 'second paragraph done')
    const moved = trackRevisions(baseline, t4, again, { ...changedRegion(t3, t4)!, source: YOU, time: 3000, moved: true })
    expect(moved.length).toBe(3)
})

test('blocks deleted together are a deletion each: one rejected alone puts back only it, in its place', () => {
    const baseline = 'intro\n\nhttps://example.com/a/very/long/path/one\n\nhttps://example.com/two\n\nhttps://example.com/three\n\nend\n'
    const current = 'intro\n\nend\n'
    const revs = trackRevisions(baseline, current, [], { ...changedRegion(baseline, current)!, source: YOU, time: 0 })
    expect(revs.map(r => r.oldText)).toEqual(['https://example.com/a/very/long/path/one\n\n', 'https://example.com/two\n\n', 'https://example.com/three\n\n'])
    expect(new Set(revs.map(r => r.id)).size).toBe(3)
    // The middle one rejected: it alone is back, between the two still deleted.
    const back = rejectFrom(current, revs[1])
    expect(back).toBe('intro\n\nhttps://example.com/two\n\nend\n')
    const left = trackRevisions(baseline, back, revs.filter(r => r !== revs[1]), { ...changedRegion(current, back)!, source: YOU, time: 1 })
    expect(left.map(r => [r.id, r.oldText])).toEqual([[revs[0].id, revs[0].oldText], [revs[2].id, revs[2].oldText]])
    // All rejected at once: the baseline, in its order.
    expect(rejectAll(current, revs)).toBe(baseline)
    // One accepted: the others still deletions.
    const accepted = acceptInto(baseline, revs[0])
    const rest = trackRevisions(accepted, current, shiftAfterAccept(revs, [revs[0]]), { source: YOU, time: 2 })
    expect(rest.map(r => [r.id, r.oldText])).toEqual([[revs[1].id, revs[1].oldText], [revs[2].id, revs[2].oldText]])
    expect(acceptAll(baseline, revs)).toBe(current)
})

test('list items deleted together are a deletion each; a deletion inside a line or inside fenced code stays one', () => {
    const baseline = '# L\n\n- one\n- two\n- three\n- four\n'
    const current = '# L\n\n- one\n- four\n'
    const revs = trackRevisions(baseline, current, [], { ...changedRegion(baseline, current)!, source: YOU, time: 0 })
    expect(revs.map(r => r.oldText)).toEqual(['- two\n', '- three\n'])
    expect(rejectAll(current, revs)).toBe(baseline)
    expect(rejectFrom(current, revs[1])).toBe('# L\n\n- one\n- three\n- four\n')

    const inLine = 'a b c d\n'
    const shorter = 'a d\n'
    expect(trackRevisions(inLine, shorter, [], { ...changedRegion(inLine, shorter)!, source: YOU, time: 0 }).length).toBe(1)

    const fence = 'x\n\n```\nl1\nl2\nl3\n```\n\ny\n'
    const cut = 'x\n\n```\nl1\n```\n\ny\n'
    const inFence = trackRevisions(fence, cut, [], { ...changedRegion(fence, cut)!, source: YOU, time: 0 })
    expect(inFence.map(r => r.oldText)).toEqual(['l2\nl3\n'])
})
