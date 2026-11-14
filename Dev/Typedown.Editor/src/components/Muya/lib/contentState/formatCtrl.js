import selection from '../selection'
import { tokenizer } from '../parser/'
import { FORMAT_MARKER_MAP, FORMAT_TYPES } from '../config'
import { getImageInfo } from '../utils/getImageInfo'

// The parts of the block's text a format token's marks take, [from, to) each: what taking the format off removes,
// leaving what it holds - the text inside **, *, ~~ or <u></u>, a link's text, a picture's description, the code or
// formula itself. The ranges are the token's own in the block's text, so a format inside another comes off as well as
// one outside: the tokens were spliced out of their parent's children and the text put together again from the top
// tokens' raw text, which an inner token's splice did not reach - only the outermost format ever came off (Italic in
// ***x***, Bold in <u>**x**</u> or ~~**x**~~, the user, 2026-11-14).
const markRanges = token => {
  const { range: { start, end }, type, marker = '' } = token
  switch (type) {
    case 'strong':
    case 'del':
    case 'em':
      return [[start, start + marker.length], [end - marker.length, end]]
    case 'html_tag': { // underline, sub, sup, mark; an element without a close tag (<img>) goes whole
      const open = token.openTag ? token.openTag.length : end - start
      const close = token.closeTag ? token.closeTag.length : 0
      return [[start, start + open], [end - close, end]]
    }
    case 'link':
    case 'image':
    case 'inline_code':
    case 'inline_math': {
      const kept = (type === 'link' ? token.anchor : type === 'image' ? token.alt : token.content) || ''
      return [[start, start + marker.length], [start + marker.length + kept.length, end]]
    }
    default:
      return []
  }
}

// Where an offset in the text goes once the marks are out: back by what was removed before it (into a removed part,
// to its start).
const offsetAfter = (offset, ranges) =>
  ranges.reduce((moved, [from, to]) => moved - Math.min(Math.max(offset - from, 0), to - from), offset)

const clearFormat = (token, { start, end }, removed) => {
  const ranges = markRanges(token)
  if (start) start.delata += offsetAfter(start.offset, ranges) - start.offset
  if (end) end.delata += offsetAfter(end.offset, ranges) - end.offset
  removed.push(...ranges)
}

// The text with the marks taken out (the ranges never overlap: a format's marks lie outside what it holds).
const withoutMarks = (text, removed) =>
  removed
    .filter(([from, to]) => to > from)
    .sort((a, b) => b[0] - a[0])
    .reduce((t, [from, to]) => t.substring(0, from) + t.substring(to), text)

const addFormat = (type, block, { start, end }) => {
  if (
    block.type !== 'span' ||
    (block.type === 'span' && !/paragraphContent|cellContent|atxLine/.test(block.functionType))
  ) {
    return false
  }
  switch (type) {
    case 'em':
    case 'del':
    case 'inline_code':
    case 'strong':
    case 'inline_math': {
      const MARKER = FORMAT_MARKER_MAP[type]
      const oldText = block.text
      block.text = oldText.substring(0, start.offset) +
        MARKER + oldText.substring(start.offset, end.offset) +
        MARKER + oldText.substring(end.offset)
      start.offset += MARKER.length
      end.offset += MARKER.length
      break
    }
    case 'sub':
    case 'sup':
    case 'mark':
    case 'u': {
      const MARKER = FORMAT_MARKER_MAP[type]
      const oldText = block.text
      block.text = oldText.substring(0, start.offset) +
        MARKER.open + oldText.substring(start.offset, end.offset) +
        MARKER.close + oldText.substring(end.offset)
      start.offset += MARKER.open.length
      end.offset += MARKER.open.length
      break
    }
    case 'link':
    case 'image': {
      const oldText = block.text
      const anchorTextLen = end.offset - start.offset
      block.text = oldText.substring(0, start.offset) +
        (type === 'link' ? '[' : '![') +
        oldText.substring(start.offset, end.offset) + ']()' +
        oldText.substring(end.offset)
      // put cursor between `()`
      start.offset += type === 'link' ? 3 + anchorTextLen : 4 + anchorTextLen
      end.offset = start.offset
      break
    }
  }
}

const checkTokenIsInlineFormat = token => {
  const { type, tag } = token
  if (FORMAT_TYPES.includes(type)) return true
  if (type === 'html_tag' && /^(?:u|sub|sup|mark|img)$/i.test(tag)) return true
  return false
}

const formatCtrl = ContentState => {
  ContentState.prototype.selectionFormats = function ({ start, end } = selection.getCursorRange()) {
    if (!start || !end) {
      return { formats: [], tokens: [], neighbors: [] }
    }

    const startBlock = this.getBlock(start.key)
    const formats = []
    const neighbors = []
    let tokens = []
    if (start.key === end.key) {
      const { text } = startBlock
      tokens = tokenizer(text, {
        options: this.muya.options
      })
      ;(function iterator (tks) {
        for (const token of tks) {
          if (
            checkTokenIsInlineFormat(token) &&
            start.offset >= token.range.start &&
            end.offset <= token.range.end
          ) {
            formats.push(token)
          }
          if (
            checkTokenIsInlineFormat(token) &&
            ((start.offset >= token.range.start && start.offset <= token.range.end) ||
            (end.offset >= token.range.start && end.offset <= token.range.end) ||
            (start.offset <= token.range.start && token.range.end <= end.offset))
          ) {
            neighbors.push(token)
          }
          if (token.children && token.children.length) {
            iterator(token.children)
          }
        }
      })(tokens)
    }
    
    return { formats, tokens, neighbors }
  }

  ContentState.prototype.clearBlockFormat = function (block, { start, end } = selection.getCursorRange(), type) {
    if (!start || !end) {
      return
    }
    if (block.type === 'pre') return false
    const { key } = block
    let neighbors
    if (start.key === end.key && start.key === key) {
      ({ neighbors } = this.selectionFormats({ start, end }))
    } else if (start.key !== end.key && start.key === key) {
      ({ neighbors } = this.selectionFormats({ start, end: { key: start.key, offset: block.text.length } }))
    } else if (start.key !== end.key && end.key === key) {
      ({ neighbors } = this.selectionFormats({
        start: {
          key: end.key,
          offset: 0
        },
        end
      }))
    } else {
      ({ neighbors } = this.selectionFormats({
        start: {
          key,
          offset: 0
        },
        end: {
          key,
          offset: block.text.length
        }
      }))
    }

    neighbors = type
      ? neighbors.filter(n => {
        return n.type === type ||
        n.type === 'html_tag' && n.tag === type
      })
      : neighbors

    const removed = []
    for (const neighbor of neighbors) {
      clearFormat(neighbor, { start, end }, removed)
    }
    start.offset += start.delata
    end.offset += end.delata
    block.text = withoutMarks(block.text, removed)
  }

  ContentState.prototype.format = function (type) {
    let { start, end } = selection.getCursorRange()
    if (!start || !end) {
      return
    }

    // A selection that ends at the very start of a later block - a triple click takes the block's line break, up to
    // the next one - ends where the block before it does: only what is selected is formatted. The next block's start
    // got an empty pair of marks ("****Second").
    if (start.key !== end.key && end.offset === 0) {
      const before = this.findPreBlockInLocation(this.getBlock(end.key))
      if (before) end = { key: before.key, offset: before.text.length }
    }
    // In a heading the format starts after the # marks, which are part of its text ("**# Title**" is no heading).
    const startHeading = this.getBlock(start.key)
    if (startHeading && startHeading.functionType === 'atxLine') {
      const marks = (/^ {0,3}#{1,6}(?:[ \t]+|$)/.exec(startHeading.text) || [''])[0].length
      if (start.offset < marks) start = { key: start.key, offset: marks }
    }

    const startBlock = this.getBlock(start.key)
    const endBlock = this.getBlock(end.key)
    start.delata = end.delata = 0
    if (start.key === end.key) {
      const { formats, neighbors } = this.selectionFormats({ start, end })
      const removed = []
      const currentFormats = formats.filter(format => {
        return format.type === type ||
          format.type === 'html_tag' && format.tag === type
      }).reverse()
      const currentNeightbors = neighbors.filter(format => {
        return format.type === type ||
        format.type === 'html_tag' && format.tag === type
      }).reverse()
      // cache delata
      if (type === 'clear') {
        for (const neighbor of neighbors) {
          clearFormat(neighbor, { start, end }, removed)
        }
        start.offset += start.delata
        end.offset += end.delata
        startBlock.text = withoutMarks(startBlock.text, removed)
      } else if (currentFormats.length) {
        for (const token of currentFormats) {
          clearFormat(token, { start, end }, removed)
        }
        start.offset += start.delata
        end.offset += end.delata
        startBlock.text = withoutMarks(startBlock.text, removed)
      } else {
        if (currentNeightbors.length) {
          for (const neighbor of currentNeightbors) {
            clearFormat(neighbor, { start, end }, removed)
          }
        }
        start.offset += start.delata
        end.offset += end.delata
        startBlock.text = withoutMarks(startBlock.text, removed)
        addFormat(type, startBlock, { start, end })
        if (type === 'image') {
          // Show image selector when create a inline image by menu/shortcut/or just input `![]()`
          requestAnimationFrame(() => {
            const startNode = selection.getSelectionStart()
            if (startNode) {
              const imageWrapper = startNode.closest('.ag-inline-image')
              if (imageWrapper && imageWrapper.classList.contains('ag-empty-image')) {
                const imageInfo = getImageInfo(imageWrapper)
                this.muya.eventCenter.dispatch('muya-image-selector', {
                  reference: imageWrapper,
                  imageInfo,
                  cb: () => {}
                })
              }
            }
          })
        }
      }
      this.cursor = { start, end }
      this.partialRender()
    } else {
      let nextBlock = startBlock
      const formatType = type !== 'clear' ? type : undefined
      while (nextBlock && nextBlock !== endBlock) {
        this.clearBlockFormat(nextBlock, { start, end }, formatType)
        nextBlock = this.findNextBlockInLocation(nextBlock)
      }
      this.clearBlockFormat(endBlock, { start, end }, formatType)

      if (type !== 'clear') {
        addFormat(type, startBlock, {
          start,
          end: { offset: startBlock.text.length }
        })
        nextBlock = this.findNextBlockInLocation(startBlock)
        while (nextBlock && nextBlock !== endBlock) {
          addFormat(type, nextBlock, {
            start: { offset: 0 },
            end: { offset: nextBlock.text.length }
          })
          nextBlock = this.findNextBlockInLocation(nextBlock)
        }
        addFormat(type, endBlock, {
          start: { offset: 0 },
          end
        })
      }

      this.cursor = { start, end }
      this.partialRender()
    }
  }
}

export default formatCtrl
