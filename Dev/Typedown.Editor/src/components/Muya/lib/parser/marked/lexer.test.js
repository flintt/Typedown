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
