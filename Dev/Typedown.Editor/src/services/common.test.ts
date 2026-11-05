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

    // The visual editor's lexer takes one line of text above an underline: two are not a heading there, nor here; the
    // underlined line that looks like one does not take the next heading's line.
    it('as the visual editor: an underline under two lines is no heading', () => {
        expect(outline('Three\nlines\n---\n\nSix\n===\n')).toEqual([[1, 'Six', 4]])
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
