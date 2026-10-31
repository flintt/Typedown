// The page's own scrolls of the last few seconds, by what made them. Told with a jump nobody made (StateRender's
// DrawJump report), they say in the host's log which of them moved the page - or that none did, and the browser did
// it itself (hp, 1.4.0: a 1.5 MB document went 98,600 px up just after it was drawn whole, and the report then had no way
// to say what moved it).
const KEEP_MS = 3000
const MAX_NOTES = 32
const notes: { at: number, who: string, from: number, to: number }[] = []

/** Called right after the page was scrolled by `who`, from `from` (window.scrollY before). Only a move is noted. */
export function noteScroll(who: string, from: number) {
    const to = window.scrollY
    if (Math.abs(to - from) < 1) return
    const now = performance.now()
    notes.push({ at: now, who, from: Math.round(from), to: Math.round(to) })
    while (notes.length && (notes.length > MAX_NOTES || now - notes[0].at > KEEP_MS)) notes.shift()
}

/** The page's own scrolls of the last `ms`, newest last: who, from, to, and how long ago. */
export function recentScrolls(ms = 1500) {
    const now = performance.now()
    return notes.filter(n => now - n.at <= ms).map(n => ({ who: n.who, from: n.from, to: n.to, ago: Math.round(now - n.at) }))
}

// For the checks (Tools/EditorBench, LT01): who moved the page lately.
(window as any).__typedownScrolls = (ms = 1500) => recentScrolls(ms)
