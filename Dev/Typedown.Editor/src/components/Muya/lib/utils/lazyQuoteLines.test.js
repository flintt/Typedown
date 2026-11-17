import Lexer from '../parser/marked/lexer'
import ExportMarkdown from './exportMarkdown'
import { splitLazyLines, breakLazyLine } from './lazyQuoteLines'

// A quote's lazy lines (written with no ">") kept as written: an edit of the quote's paragraph wrote "> " before each
// of them, changing lines the reader did not touch (review-matrix's "lazy quote", from afd0b95f on).
describe("a quote's lazy lines", () => {
  const lex = markdown => new Lexer({ gfm: true }).lex(markdown)
  const marks = markdown => lex(markdown).filter(t => t.type === 'paragraph').map(t => t.lazyLines || null)
  // The line count goes with the marks.
  const counts = markdown => lex(markdown).filter(t => t.lazyLines).map(t => t.lazyLineCount)
  // A quote of paragraphs, as importMarkdown builds it: p > span with the paragraph's text (and its lazyLines).
  const quote = (...paragraphs) => ({
    type: 'blockquote',
    children: paragraphs.map(([text, lazyLines]) => ({ type: 'p', children: [span(text, lazyLines)] }))
  })
  const write = blocks => new ExportMarkdown(blocks).generate()
  function span (text, lazyLines) { return { type: 'span', functionType: 'paragraphContent', text, lazyLines, lazyLineCount: lazyLines && text.split('\n').length } }

  test('the lexer marks the lines that had no ">"', () => {
    expect(marks('> a\nb\n')).toEqual([[1]])
    expect(marks('> a\n> b\nc\nd\n')).toEqual([[2, 3]])
    expect(marks('> a\nb\n>\n> c\nd\n')).toEqual([[1], [1]])
    expect(marks('> a\n> b\n')).toEqual([null])
    expect(counts('> a\n> b\nc\nd\n')).toEqual([4])
  })

  test("a nested quote marks its own; a list's paragraph in a quote is not marked", () => {
    expect(marks('> > a\nb\n')).toEqual([[1]])
    expect(lex('> - a\nb\n').some(t => t.lazyLines)).toBe(false)
  })

  test('written back as they were', () => {
    expect(write([quote(['a\nb', [1]])])).toBe('> a\nb\n')
    expect(write([quote(['a\nb\nc', [2]])])).toBe('> a\n> b\nc\n')
    expect(write([{ type: 'blockquote', children: [quote(['a\nb', [1]])] }])).toBe('> > a\nb\n')
  })

  test('the reader’s edit kept, the line still lazy', () => {
    expect(write([quote(['a\nbZ', [1]])])).toBe('> a\nbZ\n')
  })

  test('a line that would start a block, or underline the paragraph, gets its "> "', () => {
    for (const line of ['# h', '- item', '1. item', '---', '--', '===', '```', '> q', '<div>', 'a | b', '$$', '[x]: /u', '    code', ''])
      expect(write([quote(['a\n' + line, [1]])])).toBe('> a\n> ' + line + '\n')
  })

  test('out of a quote the marks do nothing', () => {
    const p = { type: 'p', children: [span('a\nb', [1])] }
    expect(write([p])).toBe('a\nb\n')
  })

  test('lines added or taken by an edit not followed: every line gets its "> "', () => {
    const s = span('a\nb', [1])
    s.text = 'a\nb\nc'
    expect(write([quote([s.text, undefined])].map(q => ({ ...q, children: [{ type: 'p', children: [s] }] })))).toBe('> a\n> b\n> c\n')
  })

  test('Enter: the lazy lines after the caret go with the new paragraph', () => {
    // "> a" + "b" (lazy) + "c" (lazy); Enter in "a" after its first letter... here at the end of line 1 ("b|").
    const text = 'a\nb\nc'
    const before = span(text, [1, 2])
    const after = { type: 'span', functionType: 'paragraphContent', text: '' }
    const offset = 'a\nb'.length
    splitLazyLines(before, after, text, offset)
    before.text = text.slice(0, offset)
    after.text = text.slice(offset)
    expect(write([quote([before.text, null])].map(q => ({ ...q, children: [{ type: 'p', children: [before] }, { type: 'p', children: [after] }] }))))
      .toBe('> a\nb\n>\n> \nc\n')
  })

  test('Enter in the quoted first line: the lazy line goes on with the new paragraph', () => {
    const text = 'ab\nc'
    const before = span(text, [1])
    const after = { type: 'span', functionType: 'paragraphContent', text: '' }
    splitLazyLines(before, after, text, 1)
    before.text = 'a'
    after.text = 'b\nc'
    expect(before.lazyLines).toBeUndefined()
    expect(after.lazyLines).toEqual([1])
    expect(write([{ type: 'blockquote', children: [{ type: 'p', children: [before] }, { type: 'p', children: [after] }] }])).toBe('> a\n>\n> b\nc\n')
  })

  test('Shift+Enter: the lines after the break one further on', () => {
    const text = 'ab\nc'
    const s = span(text, [1])
    breakLazyLine(s, text, 1)
    s.text = 'a\nb\nc'
    expect(write([{ type: 'blockquote', children: [{ type: 'p', children: [s] }] }])).toBe('> a\n> b\nc\n')
  })

  test('read again, what is written is the same quote', () => {
    for (const source of ['> a\nb\n', '> a\n> b\nc\n\nd\n', '> > a\nb\n']) {
      const tokens = lex(source)
      expect(lex(source.replace(/\n$/, '') + '\n').map(t => t.type)).toEqual(tokens.map(t => t.type))
    }
  })
})
