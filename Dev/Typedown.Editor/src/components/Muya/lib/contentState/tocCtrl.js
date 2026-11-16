import { getHeadingPlainText } from '../utils'

const tocCtrl = ContentState => {
  ContentState.prototype.getTOC = function () {
    const { blocks } = this
    const toc = []
    let cur = null

    const start = this.findOutMostBlock(this.getBlock(this.cursor.start.key))
    const end = this.findOutMostBlock(this.getBlock(this.cursor.end.key))

    // The headings in <details> blocks are the document's too: listed where they are (the jump opens the block).
    const walk = []
    const visit = list => {
      for (const block of list) {
        walk.push(block)
        if (block.type === 'figure' && block.functionType === 'details') visit(block.children.slice(1))
      }
    }
    visit(blocks)
    for (const block of walk) {
      if (/^h\d$/.test(block.type)) {
        const { headingStyle, key, type } = block
        const { text } = block.children[0]
        const content = getHeadingPlainText(headingStyle === 'setext' ? text.trim() : text.replace(/^\s*#{1,6}\s{1,}/, '').trim())
        const lvl = +type.substring(1)
        const slug = key
        toc.push({
          content,
          lvl,
          slug
        })
      }
      if (toc.length > 0) {
        if (block == start) {
          cur = toc[toc.length - 1]
        } else if (block == end && cur != toc[toc.length - 1]) {
          cur = null
        }
      }
    }
    
    return { toc, cur }
  }
}

export default tocCtrl
