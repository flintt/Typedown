// <details> blocks (parser/marked/detailsBlock.js; Settings > Editor > Details blocks): opened and closed in the view, made,
// taken apart, and the caret into and out of their summary.
const detailsCtrl = ContentState => {
  const isDetails = block => !!block && block.type === 'figure' && block.functionType === 'details'

  /** The <details> block `block` is in (itself, its summary or a block in it at any depth), or null. */
  ContentState.prototype.closestDetails = function (block) {
    let b = block
    while (b) {
      if (isDetails(b)) return b
      b = b.parent ? this.getBlock(b.parent) : null
    }
    return null
  }

  /** A <details> block made in the editor: its summary empty (the reader names it), one empty paragraph in it, open. */
  ContentState.prototype.createDetailsBlock = function (summary = '') {
    const figure = this.createBlock('figure', { functionType: 'details' })
    Object.assign(figure, { detailsOpen: false, expanded: true, hasSummary: true, summaryInline: false })
    this.appendChild(figure, this.createBlock('span', { text: summary, functionType: 'detailsSummary' }))
    return figure
  }

  /**
   * Opens or closes a <details> block where it is shown, the document not changed: `block.expanded` is the view's (the
   * file's `<details open>` is `block.detailsOpen`, written back as it was). `figure` is its element, set at once; a
   * later drawing draws it so too (renderContainerBlock). Closed with the caret in it, the caret goes to the end of its
   * summary: a caret in a block not shown typed where nothing could be seen.
   */
  ContentState.prototype.toggleDetails = function (block, figure, expanded = !block.expanded) {
    block.expanded = expanded
    if (!expanded && this.cursor && this.cursor.start) {
      const at = this.getBlock(this.cursor.start.key)
      const summary = block.children[0]
      if (at && at !== summary && this.closestDetails(at) && this.isInside(at, block)) {
        const offset = summary.text.length
        this.cursor = { start: { key: summary.key, offset }, end: { key: summary.key, offset } }
        this.partialRender()
        return
      }
    }
    if (!figure) return
    figure.classList.toggle('ag-details-open', expanded)
    const toggle = figure.querySelector(':scope > .ag-details-toggle')
    if (toggle) {
      toggle.textContent = expanded ? '▾' : '▸'
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false')
    }
  }

  // Whether `block` is `ancestor` or in it.
  ContentState.prototype.isInside = function (block, ancestor) {
    let b = block
    while (b) {
      if (b === ancestor) return true
      b = b.parent ? this.getBlock(b.parent) : null
    }
    return false
  }

  /**
   * The <details> blocks `block` is inside of, closed, opened (in the view): what is shown of it and the caret in it are
   * seen. Its own summary is shown while closed, so a summary opens only the blocks around its own.
   */
  ContentState.prototype.openDetailsAround = function (block) {
    let child = block
    let b = block && block.parent ? this.getBlock(block.parent) : null
    while (b) {
      if (isDetails(b) && !b.expanded && b.children[0] !== child) {
        this.toggleDetails(b, typeof document !== 'undefined' ? document.getElementById(b.key) : null, true)
      }
      child = b
      b = b.parent ? this.getBlock(b.parent) : null
    }
  }

  /** Before a drawing: the caret (a search's match, an undo, a jump) in a closed block opens it. */
  ContentState.prototype.revealCaret = function () {
    const key = this.cursor && this.cursor.start && this.cursor.start.key
    const block = key && this.getBlock(key)
    if (block) this.openDetailsAround(block)
  }

  /** Enter in a summary: no line break in it - the block opened, the caret at the start of what is in it. */
  ContentState.prototype.leaveDetailsSummary = function (summary) {
    const figure = this.getBlock(summary.parent)
    if (!isDetails(figure)) return false
    figure.expanded = true
    let first = figure.children[1]
    if (!first) {
      first = this.createBlockP()
      this.appendChild(figure, first)
    }
    const leaf = this.firstInDescendant(first)
    this.cursor = { start: { key: leaf.key, offset: 0 }, end: { key: leaf.key, offset: 0 } }
    this.partialRender()
    return true
  }

  /**
   * A <details> block taken apart: its summary a paragraph (when it has any text), what was in it left where it was,
   * in place of the block. The caret where it was, or at the start of the first block when it was in the summary.
   */
  ContentState.prototype.unwrapDetails = function (figure) {
    const [summary, ...content] = figure.children
    const caretInSummary = this.cursor && this.cursor.start && this.cursor.start.key === summary.key
    const offset = caretInSummary ? this.cursor.start.offset : 0
    let first = null
    if (summary.text) {
      first = this.createBlockP(summary.text)
      this.insertBefore(first, figure)
    }
    for (const child of content) {
      // An empty paragraph made to type into stays out when the summary had text.
      if (first && content.length === 1 && child.type === 'p' && !child.children.some(c => c.text)) continue
      this.insertBefore(child, figure)
      if (!first) first = child
    }
    if (!first) {
      first = this.createBlockP()
      this.insertBefore(first, figure)
    }
    this.removeBlock(figure)
    if (caretInSummary) {
      const leaf = this.firstInDescendant(first)
      const at = summary.text ? Math.min(offset, leaf.text.length) : 0
      this.cursor = { start: { key: leaf.key, offset: at }, end: { key: leaf.key, offset: at } }
    }
  }

  /**
   * Paragraph > Details block: in a <details> block, it is taken apart; otherwise the block the caret is in, or the
   * blocks selected, go into a new one, the caret in its (empty) summary to name it. Off in the settings: nothing.
   */
  ContentState.prototype.handleDetailsMenu = function () {
    if (this.muya.options.detailsBlocks === false) return false
    const { start, end } = this.cursor
    const startBlock = this.getBlock(start.key)
    const inside = this.closestDetails(startBlock)
    if (inside) {
      this.unwrapDetails(inside)
      return true
    }
    const figure = this.createDetailsBlock('')
    if (start.key === end.key) {
      const block = startBlock.type === 'span' ? this.getParent(startBlock) : startBlock
      this.insertAfter(figure, block)
      this.removeBlock(block)
      this.appendChild(figure, block)
    } else {
      const { parent, startIndex, endIndex } = this.getCommonParent()
      const children = parent ? parent.children : this.blocks
      const taken = children.slice(startIndex, endIndex + 1)
      this.insertAfter(figure, taken[taken.length - 1])
      for (const child of taken) {
        this.removeBlock(child, children)
        this.appendChild(figure, child)
      }
    }
    const summary = figure.children[0]
    this.cursor = { start: { key: summary.key, offset: 0 }, end: { key: summary.key, offset: 0 } }
    return true
  }

  /**
   * `<details>` typed alone on a line, then Enter: a <details> block, open, what Enter took to the next line in it, the
   * caret in its summary. Null when the line is not that, or the setting is off (an HTML block then, as before).
   */
  ContentState.prototype.updateDetailsBlock = function (block, next) {
    if (this.muya.options.detailsBlocks === false || !block || block.type !== 'p') return null
    if (!block.children[0] || block.children[0].text.trim() !== '<details>') return null
    const figure = this.createDetailsBlock('')
    this.insertBefore(figure, block)
    this.removeBlock(block)
    if (next && next.type === 'p' && this.getBlock(next.key)) {
      this.removeBlock(next)
      this.appendChild(figure, next)
    } else {
      this.appendChild(figure, this.createBlockP())
    }
    return figure.children[0]
  }
}

export default detailsCtrl
