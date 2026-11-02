import '../Muya/lib/config' // first: config and utils import each other, and config needs utils done
import { findMarkdownBlocks, markdownBlockHtml } from './markdownBlocks'

// Fences as CommonMark reads them: which Markdown blocks a plain text file has.
const blocks = text => findMarkdownBlocks(text.split('\n')).map(b => [b.from, b.to])

test('an md and a markdown fence, backticks and tildes', () => {
  expect(blocks('text\n```md\n# A\n```\nmore\n~~~markdown\n- b\n~~~\nend')).toEqual([[1, 3], [5, 7]])
})

test('other languages are not Markdown blocks, and an md fence inside one is code', () => {
  expect(blocks('```js\n```md\nx\n```\n')).toEqual([])
  expect(blocks('```\nplain\n```\n```md\n# A\n```')).toEqual([[3, 5]])
})

test('a longer outer fence holds a code block inside', () => {
  expect(blocks('````md\n# A\n```js\ncode\n```\nafter\n````\nout')).toEqual([[0, 6]])
  expect(blocks('~~~md\n```js\ncode\n```\n~~~')).toEqual([[0, 4]])
})

test('a shorter or other-character line does not close a fence', () => {
  expect(blocks('````md\n```\n````')).toEqual([[0, 2]])
  expect(blocks('~~~md\n```\n~~~')).toEqual([[0, 2]])
})

test('with the same fence inside, the first closing line closes it (CommonMark)', () => {
  expect(blocks('```md\n```js\nx\n```\nafter\n```')).toEqual([[0, 3]])
})

test('a fence never closed is no block', () => {
  expect(blocks('```md\n# never closed')).toEqual([])
})

test('the info word must be md or markdown on its own, case aside', () => {
  expect(blocks('```MD\nx\n```\n```mdx\ny\n```\n``` markdown title\nz\n```')).toEqual([[0, 2], [6, 8]])
})

test('a fence indented four spaces is not one', () => {
  expect(blocks('    ```md\n    x\n    ```')).toEqual([])
})

test('a block is drawn as Markdown, and its scripts go', () => {
  const html = markdownBlockHtml('# Title\n\n**bold** <script>alert(1)</script>\n')
  expect(html).toContain('<h1')
  expect(html).toContain('<strong>bold</strong>')
  expect(html).not.toContain('<script')
})
