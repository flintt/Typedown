import { trackRevisions, YOU } from './review'
import { markedMarkdown, markRendered } from './reviewExport'

const revisionsOf = (baseline: string, current: string) => trackRevisions(baseline, current, [], { from: 0, to: current.length, source: { name: 'Bot' }, time: 5 })

// The marked text as a renderer would put a paragraph a line (enough for the marks: they are in its text).
const render = (markdown: string) => {
    const body = document.createElement('body')
    for (const block of markdown.split(/\n{2,}/)) if (block.trim()) {
        const p = document.createElement('p')
        p.textContent = block.replace(/\\(.)/g, '$1')
        body.append(p)
    }
    return body
}

test('a revision in a line: an <ins> and a <del> with its source and time; the deleted text written as it reads', () => {
    const current = 'The new *word* here.\n'
    const revs = revisionsOf('The old *word* here.\n', current)
    const marked = markedMarkdown(current, revs)
    expect(marked).not.toContain('<')
    expect(marked.replace(/[-]\d*?|[]/g, '')).toContain('new')
    const body = render(marked)
    markRendered(body, revs)
    const ins = body.querySelector('ins')!, del = body.querySelector('del')!
    expect(ins.textContent).toBe('new')
    expect(del.textContent).toBe('old')
    expect(ins.getAttribute('data-rev-source')).toBe('Bot')
    expect(ins.getAttribute('data-rev-time')).toBe('5')
    expect(body.textContent).not.toMatch(/[-]/)
})

test('a paragraph added whole: its element marked, not its words', () => {
    const baseline = 'One.\n\nThree.\n'
    const current = 'One.\n\nTwo added.\n\nThree.\n'
    const revs = trackRevisions(baseline, current, [], { from: 0, to: current.length, source: YOU, time: 7 })
    const body = render(markedMarkdown(current, revs))
    markRendered(body, revs)
    const p = body.querySelector('[data-rev-para]')!
    expect(p.getAttribute('data-rev-para')).toBe('ins')
    expect(p.textContent).toBe('Two added.')
    expect(body.querySelectorAll('ins').length).toBe(0)
})

test('a paragraph deleted whole comes back as a paragraph marked deleted', () => {
    const revs = revisionsOf('One.\n\nGone.\n\nThree.\n', 'One.\n\nThree.\n')
    const body = render(markedMarkdown('One.\n\nThree.\n', revs))
    markRendered(body, revs)
    const p = body.querySelector('[data-rev-para="del"]')!
    expect(p.textContent).toBe('Gone.')
    expect([...body.querySelectorAll('p')].map(x => x.textContent!.trim())).toEqual(['One.', 'Gone.', 'Three.'])
})

test('no revisions: the text as it is', () => {
    expect(markedMarkdown('a\n', [])).toBe('a\n')
})
