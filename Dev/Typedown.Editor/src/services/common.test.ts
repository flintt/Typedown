import { getTOC, sourceHeadingLine, sourceHeadingSlug } from './common'

// An ES module jest does not take; search only (matchString), not the outline.
jest.mock('execall', () => () => [])

// Source mode's outline: the visual editor's (the document's top-level headings, setext ones too), each heading's slug
// its line.
describe('getTOC', () => {
    const outline = (markdown: string) => getTOC(markdown).toc.map(t => [t.lvl, t.content, sourceHeadingLine(t.slug)])

    it('ATX and setext headings, at their lines', () => {
        expect(outline('# One\n\ntext\n\nTwo\n===\n\n## Four ##\n\nFive\n--\n')).toEqual([
            [1, 'One', 0], [1, 'Two', 4], [2, 'Four', 7], [2, 'Five', 9],
        ])
    })

    // A setext heading spans the paragraph's lines above its underline (CommonMark; the visual editor's lexer since
    // afd0b95f): its line is the first of them. A line above a blank one is its own paragraph.
    it('as the visual editor: an underline under two lines is one heading, at its first line', () => {
        expect(outline('Three\nlines\n---\n\nSix\n===\n')).toEqual([[2, 'Three lines', 0], [1, 'Six', 4]])
        expect(outline('Alone\n\nTwo\nlines\n===\n')).toEqual([[1, 'Two lines', 2]])
    })

    it('not a "#" line in fenced code, nor a heading in a list or a quote', () => {
        expect(outline('```sh\n# comment\n```\n\n- # in a list\n\n> # in a quote\n\n~~~\n## tilde\n~~~\n\n# Real\n')).toEqual([[1, 'Real', 12]])
    })

    it('not the front matter, nor a thematic break under a blank line', () => {
        expect(outline('---\ntitle: x\n---\n\n# After\n\ntext\n\n---\n\n## Next\n')).toEqual([[1, 'After', 4], [2, 'Next', 10]])
    })

    it('the heading text without its marks', () => {
        expect(outline('# A **bold** `code` [link](x)\n')).toEqual([[1, 'A bold code link', 0]])
    })

    it('a slug reads back to its line, nothing else does', () => {
        expect(sourceHeadingLine(sourceHeadingSlug(42))).toBe(42)
        expect(sourceHeadingLine('heading-1')).toBeNull()
        expect(sourceHeadingLine(undefined)).toBeNull()
    })
})
