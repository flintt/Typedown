// Markdown blocks in a plain text file (a reader's request, 2026-11-02): a fence marked md or markdown is drawn as
// Markdown in the plain view laid out as prose (Settings.PlainTextProse); the rest of the file stays plain text. Drawn
// only: the file's text is not touched, and the caret going into a block (or a click on it) shows its lines again.
import marked from '../Muya/lib/parser/marked'
import { getImageInfo, loadImage, sanitize } from '../Muya/lib/utils'
import { PREVIEW_DOMPURIFY_CONFIG } from '../Muya/lib/config'
import loadRenderer from '../Muya/lib/renderers'

// Mermaid is a stateful global: one diagram drawn at a time, as the visual editor draws them.
let mermaidQueue = Promise.resolve()
let mermaidId = 0

/**
 * A block's Mermaid diagrams drawn, as the visual editor draws them (its renderer - Mermaid or Pele - and the theme's
 * light or dark): a ```mermaid fence in a block was shown as its code (a reader's report). One that cannot be drawn
 * stays code.
 */
export function drawBlockDiagrams(element) {
  const codes = [...element.querySelectorAll('code.language-mermaid')]
  if (!codes.length) return mermaidQueue
  mermaidQueue = mermaidQueue.catch(() => {}).then(async () => {
    let mermaid
    try {
      mermaid = await loadRenderer('mermaid')
      mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: window.actualTheme === 'dark' ? 'dark' : 'default' })
    } catch (err) {
      return
    }
    for (const code of codes) {
      if (!element.isConnected) return
      const renderId = `plain-mermaid-${mermaidId++}`
      try {
        const { svg } = await mermaid.render(renderId, code.textContent)
        const diagram = document.createElement('div')
        diagram.className = 'plain-md-diagram'
        diagram.innerHTML = svg
        code.parentNode.replaceWith(diagram)
      } catch (err) {
        // Kept as its code.
      } finally {
        document.getElementById(`d${renderId}`)?.remove()
      }
    }
  })
  return mermaidQueue
}

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
 * A block's pictures loaded as the visual editor loads them: a path relative to the file's folder resolved there
 * (getImageInfo), then the address the page can load (loadImage). As written, "images/a.png" was looked for beside
 * the page and never showed (a reader's report). Set once loaded; one that fails keeps its alt text.
 */
export function loadBlockImages(element) {
  for (const img of element.querySelectorAll('img')) {
    const raw = img.getAttribute('src') || ''
    if (!raw) continue
    img.removeAttribute('src')
    const { src } = getImageInfo(raw)
    loadImage(src).then(({ url }) => { img.src = url }).catch(() => { img.alt = img.alt || raw })
  }
}

/**
 * The blocks drawn over their lines in a CodeMirror editor, but the one the caret is in. The text is read and the
 * blocks found again when it changes (update); a caret move only shows the lines of the block it went into and draws
 * the one it left (caretMoved) - finding them on every move read the whole file, slow in a long one.
 */
export class MarkdownBlocksView {
  constructor(editor) {
    this.editor = editor
    this.blocks = []
    this.lines = []
    this.marks = new Map() // block.from -> mark
    this.open = null // the block the caret is in, shown as its lines
  }

  blockAt(line) {
    return this.blocks.find(block => line >= block.from && line <= block.to) || null
  }

  draw(block) {
    const { editor, lines } = this
    const doc = editor.getDoc()
    const element = document.createElement('div')
    element.className = 'plain-md-block'
    element.innerHTML = markdownBlockHtml(lines.slice(block.from + 1, block.to).join('\n'))
    loadBlockImages(element)
    // Drawn into the block once it is on the page (the diagram is measured as it is drawn), then the line heights
    // CodeMirror keeps measured again.
    setTimeout(() => { drawBlockDiagrams(element).then(() => editor.refresh()) }, 0)
    // A click shows the block's lines, the caret on its first line of Markdown.
    element.addEventListener('mousedown', event => {
      event.preventDefault()
      editor.focus()
      doc.setCursor({ line: Math.min(block.from + 1, block.to), ch: 0 })
    })
    this.marks.set(block.from, doc.markText({ line: block.from, ch: 0 }, { line: block.to, ch: lines[block.to].length }, {
      replacedWith: element,
      handleMouseEvents: true,
      clearWhenEmpty: false,
    }))
  }

  clear() {
    this.editor.operation(() => { for (const mark of this.marks.values()) mark.clear() })
    this.marks.clear()
    this.blocks = []
    this.open = null
  }

  // The text changed (or the view was turned on): the blocks found again and drawn, but the caret's.
  update() {
    this.clear()
    const doc = this.editor.getDoc()
    const lines = []
    doc.eachLine(line => { lines.push(line.text) })
    this.lines = lines
    this.blocks = findMarkdownBlocks(lines)
    this.open = doc.somethingSelected() ? null : this.blockAt(doc.getCursor().line)
    this.editor.operation(() => { for (const block of this.blocks) if (block !== this.open) this.draw(block) })
  }

  // The caret moved: the block it is in now shown as its lines, the one it left drawn again.
  caretMoved() {
    const doc = this.editor.getDoc()
    const now = doc.somethingSelected() ? null : this.blockAt(doc.getCursor().line)
    if (now === this.open) return
    this.editor.operation(() => {
      if (now) {
        const mark = this.marks.get(now.from)
        if (mark) mark.clear()
        this.marks.delete(now.from)
      }
      if (this.open && !this.marks.has(this.open.from)) this.draw(this.open)
    })
    this.open = now
  }
}
