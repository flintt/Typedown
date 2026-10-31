import { CLASS_OR_ID } from '../../../config'

// Han, kana and the CJK punctuation and full-width forms: a line ending between two of them joins without a space (as
// the CSS segment break rules have it; Chromium puts a space there). Hangul is not in it: Korean spaces its words.
const CJK = /[⺀-⿿　-〿぀-ヿㇰ-ㇿ㐀-䶿一-鿿豈-﫿＀-｠￠-￦\u{20000}-\u{3FFFF}]/u

export default function softLineBreak (h, cursor, block, token, outerClass) {
  const { lineBreak, isAtEnd } = token
  let selector = `span.${CLASS_OR_ID.AG_SOFT_LINE_BREAK}`
  if (isAtEnd) {
    selector += `.${CLASS_OR_ID.AG_LINE_END}`
  }
  // Settings > Editor > Join the lines of a paragraph in reading mode: the line ending shows as a space there
  // (assets/styles/index.css), or as nothing between two CJK characters.
  const text = block && typeof block.text === 'string' ? block.text : ''
  const before = Array.from(text.slice(Math.max(0, token.range.start - 2), token.range.start)).pop()
  const after = Array.from(text.slice(token.range.end, token.range.end + 2))[0]
  // A line ended with a backslash is a hard break in Markdown; the parser leaves the backslash as text before a soft one.
  const backslashes = text.slice(0, token.range.start).match(/\\*$/)[0].length
  if (backslashes % 2 === 1) {
    selector += `.${CLASS_OR_ID.AG_SOFT_LINE_BREAK_KEEP}`
  } else if (before && after && CJK.test(before) && CJK.test(after)) {
    selector += `.${CLASS_OR_ID.AG_SOFT_LINE_BREAK_CJK}`
  }

  return [
    h(selector, lineBreak)
  ]
}
