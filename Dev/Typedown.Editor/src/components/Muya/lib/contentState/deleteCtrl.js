import selection from '../selection'

const deleteCtrl = ContentState => {
  // A thematic break taken away by Backspace after it or Delete before it. The block after it kept the blank lines it
  // had after the break (utils/blankLines.js): it is given the ones before the break instead - the gap where the break
  // was - so a document that started with a break does not start with a blank line now.
  ContentState.prototype.removeThematicBreak = function (hr) {
    const after = this.getNextSibling(hr)
    if (after) {
      if (hr.blankLinesBefore != null) {
        after.blankLinesBefore = hr.blankLinesBefore
        after.blankLinesParent = hr.blankLinesParent
      } else {
        delete after.blankLinesBefore
      }
    }
    this.removeBlock(hr)
  }

  // Handle `delete` keydown event on document.
  ContentState.prototype.docDeleteHandler = function (event) {
    // handle delete selected image
    const { selectedImage } = this
    if (selectedImage) {
      event.preventDefault()
      this.selectedImage = null
      return this.deleteImage(selectedImage)
    }
    if (this.selectedTableCells) {
      event?.preventDefault()
      return this.deleteSelectedTableCells()
    }
  }

  ContentState.prototype.deleteHandler = function (event) {
    const { start, end } = selection.getCursorRange()
    if (!start || !end) {
      return
    }

    if (this.deleteSelectionInCell(event, start, end)) {
      return
    }
    const startBlock = this.getBlock(start.key)
    const nextBlock = this.findNextBlockInLocation(startBlock)

    // TODO: @jocs It will delete all the editor and cause error in console when there is only one empty table. same as #67
    if (startBlock.type === 'figure') event?.preventDefault()
    // If select multiple paragraph or multiple characters in one paragraph, just let
    // updateCtrl to handle this case.
    if (start.key !== end.key || start.offset !== end.offset) {
      return
    }
    // Only handle h1~h6 span block
    const { type, text, key } = startBlock
    if (/span/.test(type) && start.offset === 0 && text[1] === '\n') {
      event?.preventDefault()
      startBlock.text = text.substring(2)
      this.cursor = {
        start: { key, offset: 0 },
        end: { key, offset: 0 }
      }
      return this.singleRender(startBlock)
    }
    if (
      /h\d|span/.test(type) &&
      start.offset === text.length
    ) {
      event?.preventDefault()
      // Each table cell is a block of its own, so merging one into the next would take a cell off the row and
      // leave the table short a column. A forward delete at the end of a cell does nothing instead.
      if (startBlock.functionType === 'cellContent' || nextBlock?.functionType === 'cellContent') {
        return
      }
      // A thematic break after: Delete takes it away (its text `---` was joined to this block's end).
      if (nextBlock && nextBlock.functionType === 'thematicBreakLine') {
        this.removeThematicBreak(this.getParent(nextBlock))
        const offset = start.offset
        this.cursor = {
          start: { key, offset },
          end: { key, offset }
        }
        return this.render()
      }
      if (nextBlock && /h\d|span/.test(nextBlock.type)) {
        // if cursor at the end of code block-language input, do nothing!
        if (nextBlock.functionType === 'codeContent' && startBlock.functionType === 'languageInput') {
          return
        }

        startBlock.text += nextBlock.text

        const toBeRemoved = [nextBlock]

        let parent = this.getParent(nextBlock)
        let target = nextBlock

        while (this.isOnlyRemoveableChild(target)) {
          toBeRemoved.push(parent)
          target = parent
          parent = this.getParent(parent)
        }

        toBeRemoved.forEach(b => this.removeBlock(b))

        const offset = start.offset
        this.cursor = {
          start: { key, offset },
          end: { key, offset }
        }
        this.render()
      }
    }
  }
}

export default deleteCtrl
