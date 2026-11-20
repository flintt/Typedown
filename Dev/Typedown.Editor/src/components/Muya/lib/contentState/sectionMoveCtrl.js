// A section moved from the outline (docs/todo.md 2026-11-20 用户清单 28, as Obsidian does): a heading with everything
// under it up to the next heading of its level or higher - its sub-sections too - put before or after another heading as
// its sibling, or into it as its last sub-section, every heading in it raised or lowered by the same steps. The blocks
// themselves move (their keys, the outline's slugs, stay); one render and one change for the host, so one undo.

const levelOf = block => /^h[1-6]$/.test(block.type) ? Number(block.type[1]) : 0

const sectionMoveCtrl = ContentState => {
  // The blocks of a heading's section: it and its siblings after it, up to the next heading of its level or higher.
  ContentState.prototype.sectionOf = function (heading) {
    const level = levelOf(heading)
    const section = [heading]
    for (let block = this.getNextSibling(heading); block; block = this.getNextSibling(block)) {
      const l = levelOf(block)
      if (l && l <= level) break
      section.push(block)
    }
    return section
  }

  // The headings in some blocks as the outline lists them: the blocks themselves and what is in <details> blocks.
  const headingsIn = blocks => {
    const found = []
    const visit = list => {
      for (const block of list) {
        if (levelOf(block)) found.push(block)
        if (block.type === 'figure' && block.functionType === 'details') visit(block.children.slice(1))
      }
    }
    visit(blocks)
    return found
  }

  // A heading put at another level, written as it was: '#' marks, or underlined while it can be (levels 1 and 2).
  ContentState.prototype.setHeadingLevel = function (heading, level) {
    const line = heading.children[0]
    if (heading.headingStyle === 'setext' && level > 2) {
      heading.headingStyle = 'atx'
      line.functionType = 'atxLine'
      line.text = '#'.repeat(level) + ' ' + line.text.trim()
    } else if (heading.headingStyle !== 'setext') {
      line.text = line.text.replace(/^( {0,3})#{1,6}/, (_, indent) => indent + '#'.repeat(level))
    }
    heading.type = 'h' + level
  }

  /**
   * Moves the section of the heading `fromKey` before or after the heading `toKey` (`where` 'before', 'after': its
   * sibling; 'inside': its last sub-section). { ok: true }, or { ok: false, reason } with nothing changed: 'readOnly',
   * 'notHeading', 'ownSection' (the target is in the section moved), 'tooDeep' (a heading would go past level 6).
   */
  ContentState.prototype.moveSection = function (fromKey, toKey, where) {
    if (this.muya.options.readOnly) return { ok: false, reason: 'readOnly' }
    const from = this.getBlock(fromKey)
    const to = this.getBlock(toKey)
    if (!from || !to || !levelOf(from) || !levelOf(to) || !/^(before|after|inside)$/.test(where)) return { ok: false, reason: 'notHeading' }
    const section = this.sectionOf(from)
    const moved = new Set(section)
    for (let block = to; block; block = this.getBlock(block.parent)) {
      if (moved.has(block)) return { ok: false, reason: 'ownSection' }
    }
    // A heading under the target, or an earlier sibling of it in the section moved, is its own section's end.
    const level = where === 'inside' ? levelOf(to) + 1 : levelOf(to)
    const steps = level - levelOf(from)
    const headings = headingsIn(section)
    if (headings.some(h => levelOf(h) + steps < 1 || levelOf(h) + steps > 6)) return { ok: false, reason: 'tooDeep' }

    // The blank lines read with the document (utils/blankLines.js) are for where the blocks were: the ones moved, and their
    // old and new neighbours, written with one blank line between (the first heading moved away, the one after it was
    // written with its blank line at the top of the file).
    const plain = block => { if (block) { delete block.blankLinesBefore; delete block.blankLinesAfter } }
    plain(this.getPreSibling(section[0]))
    plain(this.getNextSibling(section[section.length - 1]))
    for (const block of section) {
      this.removeBlock(block)
      block.preSibling = block.nextSibling = null
      plain(block)
    }
    if (steps) for (const h of headings) this.setHeadingLevel(h, levelOf(h) + steps)
    if (where === 'before') {
      for (const block of section) this.insertBefore(block, to)
    } else {
      // After the target's whole section (its sub-sections too), what the move took out of it already gone.
      const targetSection = this.sectionOf(to)
      let previous = targetSection[targetSection.length - 1]
      for (const block of section) {
        this.insertAfter(block, previous)
        previous = block
      }
    }
    plain(this.getPreSibling(section[0]))
    plain(this.getNextSibling(section[section.length - 1]))
    const key = from.children[0].key
    this.cursor = { start: { key, offset: 0 }, end: { key, offset: 0 } }
    this.render()
    this.muya.dispatchChange()
    return { ok: true }
  }
}

export default sectionMoveCtrl
