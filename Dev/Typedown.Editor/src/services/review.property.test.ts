// Review mode's diff on random documents (fixed seeds, so a failure comes back the same): one small edit somewhere is
// revisions no bigger than the edit, rejecting them all gives the baseline back, accepting them all makes the baseline the
// text. "修订了一个小地方，出现大量删除和新加的内容" (the user, 2026-11-06) is what these keep from coming back.
import { acceptAll, changedRegion, diffAroundCounts, diffTexts, diffTextsAround, forgetLastDiff, LOCAL_DIFF_MIN_HUNKS, rejectAll, trackRevisions, YOU } from './review'

const rng = (seed: number) => () => {
    seed |= 0; seed = seed + 0x6D2B79F5 | 0
    let t = Math.imul(seed ^ seed >>> 15, 1 | seed)
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t
    return ((t ^ t >>> 14) >>> 0) / 4294967296
}

const WORDS = ['the', 'cat', 'sat', 'on', 'a', 'mat', 'paragraph', 'review', '修订', '中文', '天气', 'x', '---', '**bold**', '`code`', '[link](u)']

function randomDoc(r: () => number): string {
    const blocks: string[] = []
    const n = 3 + Math.floor(r() * 12)
    for (let i = 0; i < n; i++) {
        const kind = r()
        const words = () => Array.from({ length: 1 + Math.floor(r() * 9) }, () => WORDS[Math.floor(r() * WORDS.length)]).join(r() < 0.8 ? ' ' : '')
        if (kind < 0.15) blocks.push('# ' + words())
        else if (kind < 0.35) blocks.push(Array.from({ length: 1 + Math.floor(r() * 3) }, () => '- ' + words()).join('\n'))
        else if (kind < 0.45) blocks.push('same line')
        else if (kind < 0.5) blocks.push('')
        else blocks.push(words())
    }
    return blocks.join('\n\n') + (r() < 0.8 ? '\n' : '')
}

const ALPHABET = 'abcXYZ 中文\n*'

function randomEdit(r: () => number, text: string): { text: string, size: number } {
    const at = Math.floor(r() * (text.length + 1))
    const del = r() < 0.5 ? Math.floor(r() * 4) : 0
    const insLen = r() < 0.7 || del === 0 ? 1 + Math.floor(r() * 3) : 0
    const ins = Array.from({ length: insLen }, () => ALPHABET[Math.floor(r() * ALPHABET.length)]).join('')
    const end = Math.min(text.length, at + del)
    return { text: text.slice(0, at) + ins + text.slice(end), size: ins.length + (end - at) }
}

const marked = (revs: { oldText: string, newText: string }[]) => revs.reduce((s, x) => s + x.oldText.length + x.newText.length, 0)

test('one small edit anywhere: revisions no bigger than it; reject all and accept all give back the texts', () => {
    for (let seed = 1; seed <= 400; seed++) {
        const r = rng(seed)
        const baseline = randomDoc(r)
        const edit = randomEdit(r, baseline)
        if (edit.text === baseline) continue
        const region = changedRegion(baseline, edit.text)!
        const revs = trackRevisions(baseline, edit.text, [], { ...region, source: YOU, time: 1 })
        const label = `seed ${seed}: ${JSON.stringify(baseline)} -> ${JSON.stringify(edit.text)} gave ${JSON.stringify(revs.map(x => [x.oldText, x.newText]))}`
        // The fewest characters, or (where it reads better: a paragraph deleted whole) two more at most.
        if (marked(revs) > edit.size + 2) throw new Error(`${marked(revs)} marked for an edit of ${edit.size}; ${label}`)
        if (rejectAll(edit.text, revs) !== baseline) throw new Error('reject all; ' + label)
        if (acceptAll(baseline, revs) !== edit.text) throw new Error('accept all; ' + label)
    }
})

test('edits made one after another: each step bounded, the whole always reversible', () => {
    for (let seed = 1000; seed < 1150; seed++) {
        const r = rng(seed)
        const baseline = randomDoc(r)
        let text = baseline
        let revs = trackRevisions(baseline, text, [], { source: YOU, time: 0 })
        let total = 0
        for (let step = 0; step < 5; step++) {
            const edit = randomEdit(r, text)
            if (edit.text === text) continue
            total += edit.size
            revs = trackRevisions(baseline, edit.text, revs, { ...changedRegion(text, edit.text)!, source: { name: step % 2 ? 'Bot' : '' }, time: step })
            text = edit.text
            const label = `seed ${seed} step ${step}: ${JSON.stringify(baseline)} -> ${JSON.stringify(text)}`
            // Fewest words, then letters: near the fewest letters, not always exactly.
            if (marked(revs) > total + Math.ceil(total / 2) + 2) throw new Error(`${marked(revs)} marked after edits of ${total}; ${label}`)
            if (rejectAll(text, revs) !== baseline) throw new Error('reject all; ' + label)
            if (acceptAll(baseline, revs) !== text) throw new Error('accept all; ' + label)
        }
    }
})

// Many revisions: only the paragraphs about a change compared again (diffTextsAround; the user, 2026-11-24: typing lagged
// with 300 revisions shown). What it gives is always reversible and marks no more than the edits made, and a letter
// typed at a paragraph's end is what comparing the whole gives.
const manyDoc = (word: (i: number) => string) => '# Many\n\n' + Array.from({ length: 120 }, (_, i) => `${i % 30 === 15 ? '```js\nlet x = 1\n\nlet y = 2\n```\n\n' : ''}Line ${i} has a ${word(i)} word, then more.`).join('\n\n') + '\n'

test('many revisions: edits one after another, compared only about them, always reversible', () => {
    for (let seed = 2000; seed < 2010; seed++) {
        forgetLastDiff()
        const r = rng(seed)
        const baseline = manyDoc(() => 'plain')
        let text = manyDoc(i => `changed${i}`)
        let revs = trackRevisions(baseline, text, [], { source: YOU, time: 0 })
        if (revs.length < LOCAL_DIFF_MIN_HUNKS) throw new Error(`only ${revs.length} revisions to start with`)
        const start = marked(revs)
        let total = 0
        for (let step = 0; step < 30; step++) {
            const edit = randomEdit(r, text)
            if (edit.text === text) continue
            total += edit.size
            revs = trackRevisions(baseline, edit.text, revs, { ...changedRegion(text, edit.text)!, source: YOU, time: step + 1 })
            text = edit.text
            const label = `seed ${seed} step ${step}`
            if (rejectAll(text, revs) !== baseline) throw new Error('reject all; ' + label)
            if (acceptAll(baseline, revs) !== text) throw new Error('accept all; ' + label)
            if (marked(revs) > start + total + Math.ceil(total / 2) + 2) throw new Error(`${marked(revs)} marked, ${start} to start with and edits of ${total}; ${label}`)
        }
    }
})

// A long document with a few long passages added (the user's, 2026-11-24: 71 revisions made 25 hunks, under the 40 that
// compared only about a change, and each key compared the whole: 40 to 70 ms here). Few hunks, but long: only about the
// change too, and what comparing the whole gives.
test('a few long passages added: a letter typed is compared only about it', () => {
    const para = (i: number) => `Paragraph ${i} says something plain about the matter, and then a little more about it.`
    const baseline = '# Few\n\n' + Array.from({ length: 200 }, (_, i) => para(i)).join('\n\n') + '\n'
    const added = (n: number) => Array.from({ length: 6 }, (_, k) => `Added ${n}.${k}: a new passage of some length, written in, with words enough to be read.`).join('\n\n')
    let text = baseline
    for (const n of [150, 100, 50, 20, 5]) text = text.replace(para(n) + '\n', para(n) + '\n\n' + added(n) + '\n')
    forgetLastDiff()
    const hunks = diffTextsAround(baseline, text)
    expect(hunks.length).toBeLessThan(40)
    expect(hunks.length).toBeGreaterThan(0)
    const before = { ...diffAroundCounts }
    let at = text.indexOf(para(120)) + para(120).length
    for (const ch of 'abcde') {
        text = text.slice(0, at) + ch + text.slice(at)
        at++
        expect(diffTextsAround(baseline, text)).toEqual(diffTexts(baseline, text))
    }
    expect(diffAroundCounts.around - before.around).toBe(5)
    expect(diffAroundCounts.whole - before.whole).toBe(0)
})

// The last paragraph deleted, then an edit reaching to the end (review-layout-check's B15 then B16): the deletion at the
// end was left out of the baseline's stretch, and the paragraph came out added.
test('compared about a change: a deletion at the end or the start is kept in the baseline', () => {
    const baseline = '# T\n\n第一段文字。\n\n第二段文字。\n\n## 二\n\n结尾一段。\n'
    for (const [first, then] of [
        [baseline.replace('\n\n结尾一段。\n', '\n'), baseline.replace('第一段文字。\n\n第二段文字。\n\n', '')],
        [baseline.replace('# T\n\n', ''), baseline.replace('第二段文字。', '第二段改了。')],
    ]) {
        forgetLastDiff()
        diffTextsAround(baseline, first)
        const hunks = diffTextsAround(baseline, then)
        expect(hunks).toEqual(diffTexts(baseline, then))
    }
})

// A long list with no blank line, each item revised: only the lines about the change compared, and as the whole.
test('a long tight list, each item revised: a letter typed, Enter, a deletion - what comparing the whole gives', () => {
    const list = (word: (i: number) => string) => '# L\n\n' + Array.from({ length: 150 }, (_, i) => `${i + 1}. Item ${i} has a ${word(i)} word and more`).join('\n') + '\n\nAfter.\n'
    const baseline = list(() => 'plain')
    let text = list(i => `changed${i}`)
    forgetLastDiff()
    diffTextsAround(baseline, text)
    const at = text.indexOf('Item 75 ') + 'Item 75 has a changed75 word and more'.length
    const edits = [
        (t: string) => t.slice(0, at) + 'x' + t.slice(at),
        (t: string) => t.slice(0, at) + 'y' + t.slice(at),
        (t: string) => t.slice(0, at) + '\n77. new' + t.slice(at),
        (t: string) => t.slice(0, at - 4) + t.slice(at),
    ]
    for (const edit of edits) {
        const next = edit(text)
        const local = diffTextsAround(baseline, next)
        expect(local).toEqual(diffTexts(baseline, next))
        expect(rejectAll(next, trackRevisions(baseline, next, [], { source: YOU, time: 1 }))).toBe(baseline)
        text = next
    }
})

test('many revisions: a letter typed at a paragraph\'s end is what comparing the whole gives', () => {
    const baseline = manyDoc(() => 'plain')
    let text = manyDoc(i => `changed${i}`)
    forgetLastDiff()
    diffTextsAround(baseline, text)
    const at = text.indexOf('Line 60 ') + text.slice(text.indexOf('Line 60 ')).indexOf('\n')
    for (const ch of 'abc') {
        const next = text.slice(0, at) + ch + text.slice(at)
        const local = diffTextsAround(baseline, next)
        expect(local).toEqual(diffTexts(baseline, next))
        text = next
    }
})
