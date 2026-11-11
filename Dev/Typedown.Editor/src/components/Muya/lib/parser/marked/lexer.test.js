import Lexer from './lexer'

const code = '# git branch\n* test\n# git remote\norigin'
const lex = markdown => new Lexer({ gfm: true }).lex(markdown)
const types = (markdown, options = { gfm: true }) => new Lexer(options).lex(markdown).map(token => token.type)

describe('GFM table block boundaries', () => {
  test.each([
    ['leading pipes after plain text', 'intro\n| A | B |\n| --- | --- |\n| 1 | 2 |'],
    ['leading pipes after formatted text', '**改进汇总**：\n| 版本 | 改进焦点 |\n| --- | --- |\n| v1 | 结构化 |'],
    ['no leading pipes', 'intro\nA | B\n--- | ---\n1 | 2']
  ])('%s: a valid table interrupts the paragraph', (_name, markdown) => {
    expect(types(markdown)).toEqual(['paragraph', 'table'])
  })

  test.each([
    ['no delimiter row', 'intro\n| A | B |\n| 1 | 2 |'],
    ['invalid delimiter cell', 'intro\n| A | B |\n| --- | nope |\n| 1 | 2 |'],
    ['header and delimiter widths differ', 'intro\n| A | B |\n| --- |\n| 1 | 2 |'],
    ['ordinary pipe text', 'intro\nthis | remains prose\nand so does this']
  ])('%s remains paragraph text', (_name, markdown) => {
    expect(types(markdown)).toEqual(['paragraph'])
  })

  test.each([
    ['normal', { gfm: false }],
    ['pedantic', { gfm: true, pedantic: true }]
  ])('%s mode does not enable GFM table interruption', (_name, options) => {
    expect(types('intro\n| A | B |\n| --- | --- |\n| 1 | 2 |', options)).toEqual(['paragraph'])
  })

  test('many pipe-bearing lines without a delimiter stay one paragraph', () => {
    const markdown = ['intro', ...Array.from({ length: 2000 }, (_, i) => `value ${i} | still prose`)].join('\n')
    expect(types(markdown)).toEqual(['paragraph'])
  })

  test('a link definition followed by text is not merged by the rejected-table fallback', () => {
    const tokens = new Lexer({ gfm: true, disableInline: true }).lex('[foo]: /url\nbar\n')
    expect(tokens.map(token => token.text)).toEqual(['[foo]: /url', 'bar'])
  })
})

test.each(['', '\n'])('a heading after a list ends the list with heading gap %j', gap => {
  const tokens = lex('- master\n- test\n## 提交和拉取说明\n' + gap + '```bash\n' + code + '\n```\n提交：push\n\n拉取：pull')
  const heading = tokens.findIndex(t => t.type === 'heading')
  expect(tokens[heading - 1].type).toBe('list_end')
  expect(tokens.filter(t => t.type === 'heading')).toHaveLength(1)
  expect(tokens.filter(t => t.type === 'code')).toEqual([
    expect.objectContaining({ lang: 'bash', text: code })
  ])
  expect(tokens.filter(t => t.type === 'list_start')).toHaveLength(1)
})

test.each(['```', '~~~'])('a %s fence directly after a list is not split at its list-like content', fence => {
  const tokens = lex('- item\n' + fence + 'bash\n' + code + '\n' + fence)
  const block = tokens.findIndex(t => t.type === 'code')
  expect(tokens[block - 1].type).toBe('list_end')
  expect(tokens[block].text).toBe(code)
})

test.each(['- ', '10. '])('indented headings and fences remain inside a %s list item', marker => {
  const indent = ' '.repeat(marker.length)
  const tokens = lex(marker + 'item\n' + ['## Nested', '```bash', 'echo ok', '```'].map(s => indent + s).join('\n'))
  const end = tokens.findIndex(t => t.type === 'list_end')
  expect(tokens.findIndex(t => t.type === 'heading')).toBeLessThan(end)
  expect(tokens.findIndex(t => t.type === 'code')).toBeLessThan(end)
  expect(tokens.find(t => t.type === 'code').text).toBe('echo ok')
})

test('a heading with insufficient indentation ends an ordered list', () => {
  const tokens = lex('10. item\n   ## Outside\ntext')
  const heading = tokens.findIndex(t => t.type === 'heading')
  expect(tokens[heading - 1].type).toBe('list_end')
})

test('lazy paragraph continuation and inline backticks are not block boundaries', () => {
  const tokens = lex('- item\nlazy continuation\n```inline```\n#hashtag')
  expect(tokens.filter(t => t.type === 'list_end')).toHaveLength(1)
  expect(tokens[tokens.length - 1].type).toBe('list_end')
  expect(tokens.some(t => t.type === 'heading' || t.type === 'code')).toBe(false)
})

describe('GFM table rows with more cells than the header', () => {
  const table = markdown => lex(markdown).find(t => t.type === 'table')

  test.each([
    ['leading pipes', '| A | B |\n|:--|--:|\n| 1 | 2 | surplus1 | surplus2 |\n| 3 |'],
    ['no leading pipes', 'A | B\n:-- | --:\n1 | 2 | surplus1 | surplus2\n3']
  ])('%s: the table widens so the surplus cells are kept', (_name, markdown) => {
    const { header, align, cells } = table(markdown)
    expect(header).toEqual(['A', 'B', '', ''])
    expect(align).toEqual(['left', 'right', null, null])
    expect(cells).toEqual([['1', '2', 'surplus1', 'surplus2'], ['3', '', '', '']])
  })

  test.each([
    ['a trailing pipe without leading pipes', 'A | B\n--- | ---\n1 | 2 |'],
    ['an empty surplus cell', '| A | B |\n| --- | --- |\n| 1 | 2 | |']
  ])('%s does not widen the table', (_name, markdown) => {
    const { header, cells } = table(markdown)
    expect(header).toEqual(['A', 'B'])
    expect(cells).toEqual([['1', '2']])
  })
})

describe('blank lines between blocks are counted exactly', () => {
  const blocks = markdown => new Lexer({ gfm: true, disableInline: true }).lex(markdown)
    .filter(t => !/_end$/.test(t.type) && !/item_start$/.test(t.type))
    .map(t => [t.text === '' ? 'empty' : t.type, t.blankLinesBefore, t.blankLinesAfter])

  test.each([1, 2, 3, 4, 5, 6])('%i blank lines between two paragraphs', n => {
    const empties = Math.floor((n - 1) / 2)
    expect(blocks(`a\n${'\n'.repeat(n)}b\n`)).toEqual([
      ['paragraph', undefined, undefined],
      ...Array(empties).fill(['empty', undefined, undefined]),
      ['paragraph', n, undefined]
    ])
  })

  test('at the start and at the end of the text', () => {
    expect(blocks('\n\n\na\n')).toEqual([['empty', undefined, undefined], ['paragraph', 3, undefined]])
    expect(blocks('a\n\n\n\n')).toEqual([['paragraph', undefined, 3], ['empty', undefined, undefined]])
  })

  test('blank lines a list or an HTML block takes with it are a gap after it, not part of it', () => {
    expect(blocks('- a\n\n\n\nb\n')).toEqual([['list_start', undefined, undefined], ['text', undefined, undefined],
      ['empty', undefined, undefined], ['paragraph', 3, undefined]])
    expect(blocks('<div>x</div>\n\n\n\nb\n')).toEqual([['html', undefined, undefined],
      ['empty', undefined, undefined], ['paragraph', 3, undefined]])
  })

  test('two lists of different kinds keep the gap between them and stay tight', () => {
    const tokens = new Lexer({ gfm: true, disableInline: true }).lex('- a\n- b\n\n\n- [ ] c\n')
    expect(tokens.filter(t => t.type === 'loose_item_start')).toEqual([])
    expect(tokens.filter(t => t.type === 'list_start').map(t => t.blankLinesBefore)).toEqual([undefined, 2])
  })

  test('the start and end of a list item are its own lines, not counted', () => {
    const tokens = new Lexer({ gfm: true, disableInline: true }).lex('- a\n\n\n  b\n\n- c\n')
    expect(tokens.filter(t => t.blankLinesBefore || t.blankLinesAfter).map(t => [t.text, t.blankLinesBefore, t.blankLinesAfter]))
      .toEqual([['b', 2, undefined]])
  })
})

// Two places the lexer read Markdown otherwise than CommonMark (the 2026-11-09 audit's 13): a quote's lazy line, and
// a setext heading over more than one line. Written back, both changed the document's structure.
describe('lazy quote lines and setext headings of several lines, as CommonMark reads them', () => {
  const read = markdown => new Lexer({ gfm: true }).lex(markdown)
    .map(t => t.type + (t.text != null ? ':' + t.text : '') + (t.depth ? '#' + t.depth : ''))

  test('a line under a quote with no > is the quote paragraph going on', () => {
    expect(read('> a\nb\n')).toEqual(['blockquote_start', 'paragraph:a\nb', 'blockquote_end'])
    expect(read('> a\n> b\nc\n\nd\n')).toEqual(['blockquote_start', 'paragraph:a\nb\nc', 'blockquote_end', 'paragraph:d'])
  })

  test('indented code after a blank line is code; right under a paragraph it goes on the paragraph', () => {
    expect(read('Before\n\n    code\n\nAfter\n')).toEqual(['paragraph:Before', 'code:code', 'paragraph:After'])
    expect(read('Before\n    code\n')).toEqual(['paragraph:Before\n    code'])
  })

  test("a quote's code takes no lazy line (spec 236, 237)", () => {
    expect(read('>     foo\n    bar\n')).toEqual(['blockquote_start', 'code:foo', 'blockquote_end', 'code:bar'])
    // The fence opened in the quote ends with it; foo is outside (how the lone fence itself is read is another matter).
    const fenced = read('> ```\nfoo\n```\n')
    expect(fenced.indexOf('blockquote_end')).toBe(2)
    expect(fenced[3]).toBe('paragraph:foo')
  })

  test('a list item or a rule under a quote is not taken into it', () => {
    expect(read('> a\n- b\n')[3]).toBe('list_start')
    expect(read('> a\n---\n')).toEqual(['blockquote_start', 'paragraph:a', 'blockquote_end', 'hr'])
  })

  test('dashes or equals under lines of a paragraph make it one heading', () => {
    expect(read('Foo\nBar\n---\n')).toEqual(['heading:Foo\nBar#2'])
    expect(read('Foo\nBar\n===\n')).toEqual(['heading:Foo\nBar#1'])
    expect(read('Foo\nBar\n===\nBaz\n')).toEqual(['heading:Foo\nBar#1', 'paragraph:Baz'])
    expect(read('Foo\n---\n')).toEqual(['heading:Foo#2'])
  })

  test('a rule after a blank line, a rule with spaces, and one under a list item stay rules', () => {
    expect(read('Foo\n\n---\n')).toEqual(['paragraph:Foo', 'hr'])
    expect(read('Foo\nBar\n- - -\n')).toEqual(['paragraph:Foo\nBar', 'hr'])
    expect(read('- a\nb\n---\n').slice(-1)).toEqual(['hr'])
  })
})
