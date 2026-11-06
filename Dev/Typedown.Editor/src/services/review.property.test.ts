// Review mode's diff on random documents (fixed seeds, so a failure comes back the same): one small edit somewhere is
// revisions no bigger than the edit, rejecting them all gives the baseline back, accepting them all makes the baseline the
// text. "修订了一个小地方，出现大量删除和新加的内容" (the user, 2026-11-06) is what these keep from coming back.
import { acceptAll, changedRegion, rejectAll, trackRevisions, YOU } from './review'

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
