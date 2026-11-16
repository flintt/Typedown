import Lexer from './lexer'
import { matchDetails } from './detailsBlock'

const lex = (markdown, details = true) => new Lexer({ gfm: true, details }).lex(markdown)
const types = (markdown, details = true) => lex(markdown, details).map(token => token.type)

// Typedown issue #1: the summary and the Markdown in it one block, not an HTML block, the blocks after it and a lone
// </details>.
const ISSUE = '<details>\n<summary>example111</summary>\n\n- abc\n\n> 123\n\n</details>\n'

describe('<details> blocks', () => {
  test('the issue\'s example is one block with its summary, a list and a quotation in it', () => {
    const tokens = lex(ISSUE)
    expect(tokens.map(t => t.type)).toEqual(['details_start', 'list_start', 'list_item_start', 'text', 'list_item_end', 'list_end',
      'blockquote_start', 'paragraph', 'blockquote_end', 'details_end'])
    expect(tokens[0]).toMatchObject({ summary: 'example111', open: false, summaryInline: false, leadingBlank: 1, trailingBlank: 1 })
  })

  test('off, as CommonMark has it: an HTML block, the blocks after it and </details>', () => {
    expect(types(ISSUE, false)).toEqual(['html', 'list_start', 'list_item_start', 'text', 'list_item_end', 'list_end',
      'blockquote_start', 'paragraph', 'blockquote_end', 'html'])
  })

  test('the forms taken: open, the summary on the <details> line, none, no blank lines', () => {
    expect(matchDetails('<details open>\n<summary>A</summary>\n\nx\n\n</details>')).toMatchObject({ open: true, summary: 'A' })
    expect(matchDetails('<details><summary>A</summary>\n\nx\n\n</details>')).toMatchObject({ summaryInline: true, summary: 'A', body: 'x' })
    expect(matchDetails('<details>\n\nx\n\n</details>')).toMatchObject({ summary: null, body: 'x' })
    expect(matchDetails('<details>\n<summary></summary>\nx\n</details>')).toMatchObject({ summary: '', leadingBlank: 0, trailingBlank: 0, body: 'x' })
    expect(matchDetails('<details>\n<summary>A</summary>\n\n</details>')).toMatchObject({ body: '', leadingBlank: 1, trailingBlank: 0 })
  })

  test('nested blocks and fenced code: the first block ends at its own closing line', () => {
    const src = '<details>\n<summary>Out</summary>\n\n<details>\n<summary>In</summary>\n\ninner\n\n</details>\n\n```\n</details>\n```\n\n</details>\nafter\n'
    const found = matchDetails(src)
    expect(found).not.toBeNull()
    expect(src.slice(found.length)).toBe('after\n')
    expect(types(src)).toEqual(['details_start', 'details_start', 'paragraph', 'details_end', 'code', 'details_end', 'paragraph'])
  })

  test('left to the HTML block rule: other attributes, no closing line of its own, a closing line written otherwise', () => {
    expect(matchDetails('<details class="x">\n<summary>A</summary>\n\nx\n\n</details>')).toBeNull()
    expect(matchDetails('<details>\n<summary>A</summary>\n\nx\n')).toBeNull()
    expect(matchDetails('<details>\n<summary>A</summary>\n\nx\n\n</details> trailing')).toBeNull()
    expect(matchDetails('  <details>\n\nx\n\n</details>')).toBeNull()
    expect(types('<details class="x">\n<summary>A</summary>\n\nx\n\n</details>')[0]).toBe('html')
  })

  test('it takes its line end, and the text after it goes on', () => {
    const src = '# Before\n\n' + ISSUE + '\n## After\n'
    expect(types(src)).toEqual(['heading', 'details_start', 'list_start', 'list_item_start', 'text', 'list_item_end', 'list_end',
      'blockquote_start', 'paragraph', 'blockquote_end', 'details_end', 'heading'])
  })
})
