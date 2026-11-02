// Markdown blocks in a plain text file (a reader's request, 2026-11-02): a fence marked md or markdown is drawn as
// Markdown in the plain view laid out as prose (Settings.PlainTextProse); the rest of the file stays plain text. Drawn
// only: the file's text is not touched, and the caret going into a block (or a click on it) shows its lines again.
import marked from '../Muya/lib/parser/marked'
import { sanitize } from '../Muya/lib/utils'
import { PREVIEW_DOMPURIFY_CONFIG } from '../Muya/lib/config'

const OPEN = /^ {0,3}(`{3,}|~{3,})(.*)$/
const MARKDOWN_INFO = /^[ \t]*(md|markdown)(?:[ \t].*)?$/i

/**
 * The Markdown blocks among lines, as CommonMark reads fences: a fence closes at a line of the same character, at least
 * as long, with nothing after it but spaces; a backtick fence's info has no backtick. Fences of other languages are
 * passed over whole (an "```md" inside a "```js" block is code), and one never closed is no block.
 * [{ from, to }]: the opening and closing lines, 0-based; the Markdown is the lines between.
 */
export function findMarkdownBlocks(lines) {
  const blocks = []
  for (let i = 0; i < lines.length; i++) {
    const open = OPEN.exec(lines[i])
    if (!open) continue
    const fence = open[1]
    const info = open[2]
    if (fence[0] === '`' && info.includes('`')) continue
    const close = new RegExp(`^ {0,3}${fence[0] === '`' ? '`' : '~'}{${fence.length},}[ \\t]*$`)
    let end = -1
    for (let j = i + 1; j < lines.length; j++) {
      if (close.test(lines[j])) { end = j; break }
    }
    if (end < 0) break
    if (MARKDOWN_INFO.test(info)) blocks.push({ from: i, to: end })
    i = end
  }
  return blocks
}

/** The HTML a block's Markdown is drawn as: through the page's own Markdown and its sanitizer, as a preview is. */
export function markdownBlockHtml(markdown) {
  return sanitize(marked(markdown), PREVIEW_DOMPURIFY_CONFIG, false)
}

/**
 * The blocks drawn over their lines in a CodeMirror editor, but the one the caret is in; called as the text or the
 * caret changes. Returns the marks made, to be cleared by the next call (or with none, to clear them all).
 */
export function drawMarkdownBlocks(editor, previous, enabled) {
  editor.operation(() => {
    for (const mark of previous) mark.clear()
  })
  if (!enabled) return []
  const doc = editor.getDoc()
  const lines = []
  doc.eachLine(line => { lines.push(line.text) })
  const caret = doc.getCursor().line
  const selecting = doc.somethingSelected()
  const marks = []
  editor.operation(() => {
    for (const block of findMarkdownBlocks(lines)) {
      if (!selecting && caret >= block.from && caret <= block.to) continue
      const element = document.createElement('div')
      element.className = 'plain-md-block'
      element.innerHTML = markdownBlockHtml(lines.slice(block.from + 1, block.to).join('\n'))
      // A click shows the block's lines, the caret on its first line of Markdown.
      element.addEventListener('mousedown', event => {
        event.preventDefault()
        editor.focus()
        doc.setCursor({ line: Math.min(block.from + 1, block.to), ch: 0 })
      })
      marks.push(doc.markText({ line: block.from, ch: 0 }, { line: block.to, ch: lines[block.to].length }, {
        replacedWith: element,
        handleMouseEvents: true,
        clearWhenEmpty: false,
      }))
    }
  })
  return marks
}
