import { KeptDocuments, blockCaretKey, caretKey, isForward, markedAt, sameLines } from './keptDocuments'

describe('KeptDocuments', () => {
    const doc = (text: string) => ({ text })

    it('keeps the most recent within the count', () => {
        const kept = new KeptDocuments<{ text: string }>(2, 1000)
        kept.put(doc('a')); kept.put(doc('b')); kept.put(doc('c'))
        expect(kept.size).toBe(2)
        expect(kept.take('a')).toBeUndefined()
        expect(kept.take('c')).toEqual(doc('c'))
        expect(kept.size).toBe(1)
    })

    it('keeps within the characters in all, and never one longer than that', () => {
        const kept = new KeptDocuments<{ text: string }>(5, 10)
        kept.put(doc('aaaaaa')); kept.put(doc('bbbbbb'))
        expect(kept.size).toBe(1)
        expect(kept.take('bbbbbb')).toBeDefined()
        kept.put(doc('x'.repeat(11)))
        expect(kept.size).toBe(0)
    })

    it('keeps one copy of a text, and gives it out once', () => {
        const kept = new KeptDocuments<{ text: string, n: number }>(3, 100)
        kept.put({ text: 'a', n: 1 }); kept.put({ text: 'a', n: 2 })
        expect(kept.size).toBe(1)
        expect(kept.take('a')?.n).toBe(2)
        expect(kept.take('a')).toBeUndefined()
    })
})

describe('carets', () => {
    it('compares host carets by anchor and focus only', () => {
        expect(caretKey({ anchor: { line: 1, ch: 2 }, focus: { line: 1, ch: 2 }, extra: 1 })).toBe(caretKey({ focus: { ch: 2, line: 1 }, anchor: { ch: 2, line: 1 } }))
        expect(caretKey(null)).toBe('null')
        expect(caretKey({ anchor: { line: 1, ch: 2 }, focus: { line: 1, ch: 3 } })).not.toBe(caretKey({ anchor: { line: 1, ch: 2 }, focus: { line: 1, ch: 2 } }))
        expect(blockCaretKey({ anchor: { key: 'ag-1', offset: 3 }, focus: { key: 'ag-2', offset: 0 }, start: {} }))
            .toBe(JSON.stringify({ anchor: { key: 'ag-1', offset: 3 }, focus: { key: 'ag-2', offset: 0 } }))
    })

    it('tells a forward caret', () => {
        expect(isForward({ anchor: { line: 1, ch: 2 }, focus: { line: 1, ch: 2 } })).toBe(true)
        expect(isForward({ anchor: { line: 1, ch: 3 }, focus: { line: 1, ch: 2 } })).toBe(false)
        expect(isForward({ anchor: { line: 0, ch: 9 }, focus: { line: 1, ch: 0 } })).toBe(true)
        expect(isForward(null)).toBe(false)
    })

    it('puts the markers exactly at the caret', () => {
        expect(markedAt('ab\ncd', { anchor: { line: 1, ch: 1 }, focus: { line: 1, ch: 1 } }, '<', '>')).toBe('ab\nc<>d')
        expect(markedAt('ab\ncd', { anchor: { line: 0, ch: 1 }, focus: { line: 1, ch: 2 } }, '<', '>')).toBe('a<b\ncd>')
        expect(markedAt('ab\ncd', { anchor: { line: 0, ch: 0 }, focus: { line: 0, ch: 2 } }, '<', '>')).toBe('<ab>\ncd')
        expect(markedAt('ab', { anchor: { line: 3, ch: 0 }, focus: { line: 3, ch: 0 } }, '<', '>')).toBeNull()
        expect(markedAt('ab', { anchor: { line: 0, ch: 5 }, focus: { line: 0, ch: 5 } }, '<', '>')).toBeNull()
    })
})

describe('sameLines', () => {
    it('counts the whole lines two texts share from their start', () => {
        expect(sameLines('a\nb\nc', 'a\nb\nc')).toBe(3)
        expect(sameLines('a\nb\nc', 'a\nb\nx')).toBe(2)
        expect(sameLines('a\nb\nc', 'a\nbb\nc')).toBe(1)
        expect(sameLines('a\nb', 'x\nb')).toBe(0)
        expect(sameLines('a\nb\n', 'a\nb')).toBe(1)
    })
})
