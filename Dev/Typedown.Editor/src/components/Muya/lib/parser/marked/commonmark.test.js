import marked from './index'

// What the exports render (HTML, PDF, Word, a copy for Word: services/exportHtml.js through this marked) against
// CommonMark 0.31.2, for the cases Tools/EditorBench/spec-conformance-check.js found wrong and that changed the meaning.
const html = markdown => marked(markdown, { footnote: true })

describe('CommonMark in the exports', () => {
  // C1 (examples 256, 316): a list item's paragraphs stay apart - they were joined into one ("one two").
  test.each([
    ['- one\n\n  two\n', '<ul>\n<li><p>one</p>\n<p>two</p>\n</li>\n</ul>\n'],
    ['- a\n- b\n\n  c\n- d\n', /<li><p>b<\/p>\n<p>c<\/p>/]
  ])('a list item\'s paragraphs: %j', (markdown, expected) => {
    if (typeof expected === 'string') expect(html(markdown)).toBe(expected)
    else expect(html(markdown)).toMatch(expected)
  })

  // C2 (examples 493, 494): an angle-bracketed destination left unclosed is no link, its text kept - the whole
  // paragraph was "undefined".
  test.each([
    ['[link](<foo\\>)\n', '<p>[link](&lt;foo&gt;)</p>\n'],
    ['before [a](<b)c and after\n', /before \[a\]\(&lt;b\)c and after/]
  ])('an unclosed <destination>: %j', (markdown, expected) => {
    const got = html(markdown)
    expect(got).not.toMatch(/undefined/)
    if (typeof expected === 'string') expect(got).toBe(expected)
    else expect(got).toMatch(expected)
  })

  test('a link in angle brackets still is one', () => {
    expect(html('[a](<b c>)\n')).toMatch(/<a href="b%20c">a<\/a>/)
  })
})
