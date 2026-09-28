import Lexer from './lexer'

const code = '# git branch\n* test\n# git remote\norigin'
const lex = markdown => new Lexer({ gfm: true }).lex(markdown)

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
