// The reader's own writing kept where they did not edit (services/sourceMerge), on random documents (fixed seeds): a
// document written its own way (~~~ fences, * markers, __strong__, trailing spaces, no blank line after a quotation's
// lazy line), Muya's way of writing it (simulated: ``` fences, - markers, **strong**, a blank line added), one line of
// Muya's edited - every line the edit did not touch comes out as the source had it.
import { alignment, mergeSource, splitLines } from './sourceMerge'

const rng = (seed: number) => () => {
    seed |= 0; seed = seed + 0x6D2B79F5 | 0
    let t = Math.imul(seed ^ seed >>> 15, 1 | seed)
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t
    return ((t ^ t >>> 14) >>> 0) / 4294967296
}

// Blocks, each its source lines and Muya's.
function block(r: () => number, i: number): { source: string[], muya: string[] } {
    const word = `w${i}x${Math.floor(r() * 1000)}`
    switch (Math.floor(r() * 6)) {
        case 0: return { source: ['~~~js', `let ${word} = 1;`, '~~~'], muya: ['```js', `let ${word} = 1;`, '```'] }
        case 1: return { source: [`* ${word} one`, `* ${word} two`], muya: [`- ${word} one`, `- ${word} two`] }
        case 2: return { source: [`Some __${word}__ here.`], muya: [`Some **${word}** here.`] }
        case 3: return { source: [`Line ${word}   `], muya: [`Line ${word}`] }
        case 4: return { source: [`> quoted ${word}`, `lazy ${word}`], muya: [`> quoted ${word}`, '', `lazy ${word}`] }
        default: return { source: [`Plain ${word}.`], muya: [`Plain ${word}.`] }
    }
}

test('an edit of one line keeps every other line as the source wrote it', () => {
    for (let seed = 1; seed <= 300; seed++) {
        const r = rng(seed)
        const blocks = Array.from({ length: 3 + Math.floor(r() * 8) }, (_, i) => block(r, i))
        const join = (part: 'source' | 'muya') => blocks.map(b => b[part].join('\n')).join('\n\n') + '\n'
        const source = join('source'), base = join('muya')
        const baseLines = splitLines(base)
        // A line with words in it, edited.
        const candidates = baseLines.map((l, i) => i).filter(i => /w\d+x/.test(baseLines[i]) && !/^```|^~~~/.test(baseLines[i]))
        const at = candidates[Math.floor(r() * candidates.length)]
        const edited = baseLines[at].replace(/x(\d+)/, 'xZ$1')
        const current = baseLines.map((l, i) => i === at ? edited : l).join('')
        const s = splitLines(source)
        const merged = mergeSource(s, baseLines, splitLines(current), alignment(baseLines, s))
        if (merged === null) continue
        const word = edited.match(/w\d+xZ\d+/)![0].replace('Z', '')
        // Every source line without the edited word is in the result as it was.
        for (const line of source.split('\n').filter(l => l && !l.includes(word)))
            if (!merged.split('\n').includes(line)) throw new Error(`seed ${seed}: lost ${JSON.stringify(line)} in ${JSON.stringify(merged)}`)
        if (!merged.includes(word.replace('x', 'xZ'))) throw new Error(`seed ${seed}: the edit is not in ${JSON.stringify(merged)}`)
    }
})
