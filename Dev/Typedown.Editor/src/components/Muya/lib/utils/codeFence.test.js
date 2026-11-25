import ExportMarkdown from './exportMarkdown'

// A fenced code block written out (the audit's C01): an empty one is its two fences - it gained an empty line between
// them, "```\n\n```".
describe('a fenced code block written out', () => {
  // As importMarkdown builds it: pre > (language input, code > span with the text).
  const code = (text, lang = '') => ({
    type: 'pre',
    functionType: 'fencecode',
    lang,
    children: [{ type: 'span' }, { type: 'code', children: [{ type: 'span', text }] }]
  })
  const write = blocks => new ExportMarkdown(blocks).generate()

  test('an empty one is the two fences', () => {
    expect(write([code('')])).toBe('```\n```\n')
    expect(write([code('', 'js')])).toBe('```js\n```\n')
  })

  test('one with text as before, empty lines in it kept', () => {
    expect(write([code('a')])).toBe('```\na\n```\n')
    expect(write([code('\na\n')])).toBe('```\n\na\n\n```\n')
  })
})
