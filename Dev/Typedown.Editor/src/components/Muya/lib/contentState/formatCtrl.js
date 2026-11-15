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

// The selection without the marks at its edges: its start moved past each format's marks it lies in, its end back
// before them, so what is compared with the formats is the text the selection shows. A format is on when every letter
// selected has it - the marks are no letters: with Ctrl+A in ***x*** or a drag past the word's ends, only the outermost
// format's range held the selection and only it showed pressed (the user, 2026-11-15). The marks are each format's own
// (markRanges, as taking it off removes them); a picture is what shows, not marks. A selection of marks alone stays.
const visibleRange = (tokens, from, to) => {
  if (from >= to) return [from, to]
  const marks = []
  ;(function collect (tks) {
    for (const token of tks) {
      if (checkTokenIsInlineFormat(token) && token.type !== 'image' && !(token.type === 'html_tag' && /^img$/i.test(token.tag))) {
        marks.push(...markRanges(token).filter(([a, b]) => b > a))
      }
      if (token.children && token.children.length) collect(token.children)
    }
  })(tokens)
  let start = from
  let end = to
  for (let moved = true; moved && start < end;) {
    moved = false
    for (const [a, b] of marks) if (start >= a && start < b) { start = b; moved = true }
  }
  for (let moved = true; moved && start < end;) {
    moved = false
    for (const [a, b] of marks) if (end > a && end <= b) { end = a; moved = true }
  }
  return start < end ? [start, end] : [from, to]
}

// The formats a press with the caret alone keeps for the text typed next, as Word does (the user, 2026-11-15): the
// press put the empty marks in at once ("****", the caret between them), which stayed in the file when nothing was
// typed and were no format to the editor, so a second press added another pair ("********"). The marks go in only
// with what is typed (inputHandler, takePendingFormats).
const PENDING_TYPES = ['strong', 'em', 'del', 'u', 'inline_code', 'inline_math', 'sub', 'sup', 'mark']

// The open and close marks of the formats kept, the first pressed outermost.
export const pendingMarks = types => types.reduce(([open, close], type) => {
  const marker = FORMAT_MARKER_MAP[type]
  return typeof marker === 'string' ? [open + marker, marker + close] : [open + marker.open, marker.close + close]
}, ['', ''])

const formatCtrl = ContentState => {
  // The formats kept for the caret, or null; dropped once the caret has moved or the block's text changed (another
  // edit, another document shown). While an IME composes, the caret moves on through the composed text: kept.
  ContentState.prototype.pendingFormatsAt = function (range) {
    const pending = this.pendingFormats
    if (!pending) return null
    const { start, end } = range && range.start && range.end ? range : this.cursor || {}
    const block = this.getBlock(pending.key)
    const composing = this.muya.keyboard && this.muya.keyboard.isComposed
    const there = start && end && start.key === pending.key && end.key === pending.key &&
      start.offset === pending.offset && end.offset === pending.offset
    if (!block || block.text !== pending.text || (!there && !composing)) {
      this.pendingFormats = null
      return null
    }
    return pending
  }

  // The text typed where formats were kept: given the block's text as the page has it now and the caret after the
  // typing, the text with the typed part inside the marks and the caret after it (still inside them), or null when
  // what changed is not just text typed at the kept place. The formats are given up either way.
  ContentState.prototype.takePendingFormats = function (key, text, caret) {
    const pending = this.pendingFormats
    this.pendingFormats = null
    if (!pending || pending.key !== key) return null
    const block = this.getBlock(key)
    const { offset: at, text: before, types } = pending
    const typed = text.length - before.length
    if (!block || block.text !== before || typed <= 0 || caret !== at + typed ||
      text.substring(0, at) !== before.substring(0, at) || text.substring(at + typed) !== before.substring(at)) {
      return null
    }
    const [open, close] = pendingMarks(types)
    return {
      text: before.substring(0, at) + open + text.substr(at, typed) + close + before.substring(at),
      offset: at + open.length + typed
    }
  }

  // `pending`: the formats kept for the caret count too (what the host's buttons show).
  ContentState.prototype.selectionFormats = function ({ start, end } = selection.getCursorRange(), { pending = false } = {}) {
    if (!start || !end) {
      return { formats: [], tokens: [], neighbors: [] }
    }
    if (pending) {
      const result = this.selectionFormats({ start, end })
      const kept = this.pendingFormatsAt({ start, end })
      if (kept) {
        for (const type of kept.types) {
          result.formats.push(/^(?:u|sub|sup|mark)$/.test(type) ? { type: 'html_tag', tag: type, pending: true } : { type, pending: true })
        }
      }
      return result
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
      // The format pressed, the visible letters' (and so the one a press takes off, format below); the neighbours,
      // what the selection touches, marks and all.
      const [from, to] = visibleRange(tokens, start.offset, end.offset)
      ;(function iterator (tks) {
        for (const token of tks) {
          if (
            checkTokenIsInlineFormat(token) &&
            from >= token.range.start &&
            to <= token.range.end
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

    // The caret alone, the format not on there: nothing goes in, the format is kept for the text typed next (pressed
    // again, it is given up). In a format already there the press takes it off the word, as before.
    if (
      start.key === end.key && start.offset === end.offset && PENDING_TYPES.includes(type) &&
      startBlock && startBlock.type === 'span' && /paragraphContent|cellContent|atxLine/.test(startBlock.functionType)
    ) {
      const on = this.selectionFormats({ start: { ...start }, end: { ...end } }).formats
        .some(t => t.type === type || (t.type === 'html_tag' && t.tag === type))
      if (!on) {
        const kept = this.pendingFormatsAt({ start, end })
        const types = kept ? kept.types.filter(t => t !== type) : []
        if (!kept || types.length === kept.types.length) types.push(type)
        this.pendingFormats = types.length ? { key: start.key, offset: start.offset, text: startBlock.text, types } : null
        this.cursor = { start: { key: start.key, offset: start.offset }, end: { key: end.key, offset: end.offset } }
        // The caret back in the page (a menu had the focus); the text is as it was.
        selection.setCursorRange(this.cursor)
        return 'pending'
      }
    }
    if (type === 'clear') this.pendingFormats = null

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
