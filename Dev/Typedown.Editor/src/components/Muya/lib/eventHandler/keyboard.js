import { EVENT_KEYS } from '../config'
import selection from '../selection'
import { findNearestParagraph } from '../selection/dom'
import { getParagraphReference, getImageInfo } from '../utils'
import { checkEditEmoji } from '../ui/emojis'
import { keyBinding, matchesKey, asKeys } from '../utils/keyBinding'

class Keyboard {
  constructor(muya) {
    this.muya = muya
    this.isComposed = false
    this.isControlDown = false
    this.shownFloat = new Set()
    this.recordIsComposed()
    this.dispatchEditorState()
    this.keydownBinding()
    this.keyupBinding()
    this.inputBinding()
    this.listen()
  }

  listen() {
    // cache shown float box
    this.muya.eventCenter.subscribe('muya-float', (tool, status) => {
      status ? this.shownFloat.add(tool) : this.shownFloat.delete(tool)
      if (tool.name === 'ag-front-menu' && !status) {
        const seletedParagraph = this.muya.container.querySelector('.ag-selected')
        if (seletedParagraph) {
          this.muya.contentState.selectedBlock = null
          // prevent rerender, so change the class manually.
          seletedParagraph.classList.toggle('ag-selected')
        }
      }
    })
  }

  hideAllFloatTools() {
    for (const tool of this.shownFloat) {
      tool.hide()
    }
  }

  recordIsComposed() {
    const { container, eventCenter, contentState } = this.muya
    const handler = event => {
      if (event.type === 'compositionstart') {
        this.isComposed = true
      } else if (event.type === 'compositionend') {
        this.isComposed = false
        // Because the compose event will not cause `input` event, So need call `inputHandler` by ourself
        contentState.inputHandler(event)
        eventCenter.dispatch('stateChange')
      }
    }

    eventCenter.attachDOMEvent(container, 'compositionend', handler)
    // eventCenter.attachDOMEvent(container, 'compositionupdate', handler)
    eventCenter.attachDOMEvent(container, 'compositionstart', handler)
  }

  dispatchEditorState() {
    const { container, eventCenter } = this.muya

    let timer = null
    const changeHandler = event => {
      if (
        event.type === 'keyup' &&
        (event.key === EVENT_KEYS.ArrowUp || event.key === EVENT_KEYS.ArrowDown) &&
        this.shownFloat.size > 0
      ) {
        return
      }
      // Cursor outside editor area or over not editable elements.
      if (event.target.closest('[contenteditable=false]')) {
        return
      }

      // We need check cursor is null, because we may copy the html preview content,
      // and no need to dispatch change.
      const { start, end } = selection.getCursorRange()
      if (!start || !end) {
        return
      }

      if (timer) clearTimeout(timer)
      timer = setTimeout(() => {
        if (!this.isComposed && event.type === 'click') {
          this.muya.dispatchChange()
        } else {
          // The caret can be gone by now (another document shown since the key): nothing to report, and
          // selectionChange throws without one.
          const now = selection.getCursorRange()
          if (!now.start || !now.end) return
          this.muya.dispatchSelectionChange()
          this.muya.dispatchSelectionFormats()
        }
      })
    }

    eventCenter.attachDOMEvent(container, 'click', changeHandler)
    eventCenter.attachDOMEvent(container, 'contextmenu', changeHandler)
    eventCenter.attachDOMEvent(container, 'keyup', changeHandler)
  }

  // The keys a person can change (Settings > Shortcuts, under "not recommended to change"): each answered by the handler
  // that did it before, given the event as that handler expects it. A key given away does nothing more here - Tab moved
  // to another key leaves the focus in the document, a line-break key moved is an Enter. True when the key was taken.
  boundKey(event) {
    const { contentState } = this.muya
    const options = this.muya.options
    if (this.isComposed) return false
    const indent = matchesKey(event, keyBinding(options, 'indent'))
    const outdent = !indent && matchesKey(event, keyBinding(options, 'outdent'))
    if (indent || outdent) {
      contentState.tabHandler(asKeys(event, { key: 'Tab', code: 'Tab', shiftKey: outdent, ctrlKey: false, altKey: false }))
      this.muya.dispatchChange()
      return true
    }
    if (event.key === 'Tab' && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault()
      return true
    }
    // Out of a table: in a cell only; elsewhere the key is what it otherwise is.
    if (matchesKey(event, keyBinding(options, 'leaveTable')) && contentState.leaveTable()) {
      event.preventDefault()
      this.muya.dispatchChange()
      return true
    }
    const soft = matchesKey(event, keyBinding(options, 'softLineBreak'))
    const cell = !soft && matchesKey(event, keyBinding(options, 'cellLineBreak'))
    if (soft || cell) {
      contentState.enterHandler(asKeys(event, { key: 'Enter', code: 'Enter', shiftKey: soft, altKey: cell, ctrlKey: false }))
      this.muya.dispatchChange()
      return true
    }
    if (event.key === 'Enter' && !event.ctrlKey && !event.metaKey && (event.shiftKey || event.altKey)) {
      contentState.enterHandler(asKeys(event, { shiftKey: false, altKey: false }))
      this.muya.dispatchChange()
      return true
    }
    return this.edgeKey(event)
  }

  // The document's start and end on their keys (Ctrl+Home and Ctrl+End by default), in reading mode too (only the page
  // scrolls there); Ctrl+Home and Ctrl+End given away do nothing. True when the key was taken.
  edgeKey(event) {
    const options = this.muya.options
    const start = matchesKey(event, keyBinding(options, 'documentStart'))
    const end = !start && matchesKey(event, keyBinding(options, 'documentEnd'))
    if (start || end) {
      // Done already when the page drew that end first (Muya/index.tsx, while the document is drawn a part at a time).
      if (!event.defaultPrevented) this.muya.moveToEdge(end)
      event.preventDefault()
      return true
    }
    if ((event.key === 'Home' || event.key === 'End') && event.ctrlKey && !event.shiftKey && !event.altKey) {
      event.preventDefault()
      return true
    }
    return false
  }

  keydownBinding() {
    const { container, eventCenter, contentState } = this.muya
    const docHandler = event => {
      // A document kept in memory while another is shown is still an editor with these handlers bound, and
      // it still holds whatever was selected in it when it was left — an image selected there would be
      // deleted by a Backspace meant for the document actually on screen. Only the one in the page answers.
      if (!this.muya.container.isConnected) {
        return
      }
      if (this.muya.options.readOnly) {
        this.edgeKey(event)
        return
      }
      // A selected image previewed: Space by default, the "preview image" shortcut as set.
      if (contentState.selectedImage && matchesKey(event, keyBinding(this.muya.options, 'previewImage'))) {
        const { token } = contentState.selectedImage
        const { src } = getImageInfo(token.src || token.attrs.src)
        if (src) {
          eventCenter.dispatch('preview-image', {
            data: src
          })
        }
        return
      }
      switch (event.code) {
        case EVENT_KEYS.Enter:
          return contentState.docEnterHandler(event)
        case EVENT_KEYS.Backspace: {
          return contentState.docBackspaceHandler(event)
        }
        case EVENT_KEYS.Delete: {
          return contentState.docDeleteHandler(event)
        }
        case EVENT_KEYS.ArrowUp: // fallthrough
        case EVENT_KEYS.ArrowDown: // fallthrough
        case EVENT_KEYS.ArrowLeft: // fallthrough
        case EVENT_KEYS.ArrowRight: // fallthrough
          return contentState.docArrowHandler(event)
      }
    }

    const handler = event => {
      if (!this.isControlDown && (event.metaKey || event.ctrlKey)) {
        this.isControlDown = true
        container.classList.add('ag-meta-or-ctrl')
      }

      // Reading mode: the container is not editable, so nothing below can apply — but Backspace/Enter/Tab would
      // still run their content handlers against the last cursor. Arrow keys and shortcuts are left alone.
      if (this.muya.options.readOnly) {
        this.edgeKey(event)
        return
      }

      if (
        this.shownFloat.size > 0 &&
        (
          event.key === EVENT_KEYS.Enter ||
          event.key === EVENT_KEYS.Escape ||
          event.key === EVENT_KEYS.Tab ||
          event.key === EVENT_KEYS.ArrowUp ||
          event.key === EVENT_KEYS.ArrowDown
        )
      ) {
        let needPreventDefault = false

        for (const tool of this.shownFloat) {
          if (
            tool.name === 'ag-format-picker' ||
            tool.name === 'ag-table-picker' ||
            tool.name === 'ag-quick-insert' ||
            tool.name === 'ag-emoji-picker' ||
            tool.name === 'ag-front-menu' ||
            tool.name === 'ag-list-picker' ||
            tool.name === 'ag-image-selector'
          ) {
            needPreventDefault = true
            break
          }
        }
        if (needPreventDefault) {
          event.preventDefault()
        }
        // event.stopPropagation()
        return
      }
      if (this.boundKey(event)) return
      switch (event.key) {
        // A Backspace or Delete the handler made itself (it prevented the browser's) fires no input event: told here,
        // as Enter is. Without it the host kept the text from before - an empty paragraph taken out of a run of blank
        // lines came back on the next save, and a save at once wrote the old text.
        case EVENT_KEYS.Backspace:
          contentState.backspaceHandler(event)
          if (event.defaultPrevented) this.muya.dispatchChange()
          break
        case EVENT_KEYS.Delete:
          contentState.deleteHandler(event)
          if (event.defaultPrevented) this.muya.dispatchChange()
          break
        case EVENT_KEYS.Enter:
          if (!this.isComposed) {
            contentState.enterHandler(event)
            this.muya.dispatchChange()
          }
          break
        case EVENT_KEYS.ArrowUp: // fallthrough
        case EVENT_KEYS.ArrowDown: // fallthrough
        case EVENT_KEYS.ArrowLeft: // fallthrough
        case EVENT_KEYS.ArrowRight: // fallthrough
          if (!this.isComposed) {
            contentState.arrowHandler(event)
          }
          break
        // Tab is always the handler's own (it indents, unindents, inserts spaces or jumps): told here as Enter is, or
        // the host kept the text from before the indent and a save at once lost it.
        case EVENT_KEYS.Tab:
          contentState.tabHandler(event)
          this.muya.dispatchChange()
          break
        default:
          break
      }
    }

    eventCenter.attachDOMEvent(container, 'keydown', handler)
    eventCenter.attachDOMEvent(document, 'keydown', docHandler)
  }

  inputBinding() {
    const { container, eventCenter, contentState } = this.muya
    const inputHandler = event => {
      if (!this.isComposed) {
        contentState.inputHandler(event)
        this.muya.dispatchChange()
      }

      const { lang, paragraph } = contentState.checkEditLanguage()
      if (lang) {
        eventCenter.dispatch('muya-code-picker', {
          reference: getParagraphReference(paragraph, paragraph.id),
          lang,
          cb: item => {
            contentState.selectLanguage(paragraph, item.name)
          }
        })
      } else {
        // hide code picker float box
        eventCenter.dispatch('muya-code-picker', { reference: null })
      }
    }

    eventCenter.attachDOMEvent(container, 'input', inputHandler)
  }

  keyupBinding() {
    const { container, eventCenter, contentState } = this.muya
    const handler = event => {
      if (this.isControlDown && !event.metaKey && !event.ctrlKey) {
        this.isControlDown = false
        container.classList.remove('ag-meta-or-ctrl')
      }
      // check if edit emoji
      const node = selection.getSelectionStart()
      const paragraph = findNearestParagraph(node)
      const emojiNode = checkEditEmoji(node)
      contentState.selectedImage = null
      if (
        paragraph &&
        emojiNode &&
        event.key !== EVENT_KEYS.Enter &&
        event.key !== EVENT_KEYS.ArrowDown &&
        event.key !== EVENT_KEYS.ArrowUp &&
        event.key !== EVENT_KEYS.Tab &&
        event.key !== EVENT_KEYS.Escape
      ) {
        const reference = getParagraphReference(emojiNode, paragraph.id)
        eventCenter.dispatch('muya-emoji-picker', {
          reference,
          emojiNode
        })
      }
      if (!emojiNode) {
        eventCenter.dispatch('muya-emoji-picker', {
          emojiNode
        })
      }

      const { anchor, focus, start, end } = selection.getCursorRange()
      if (!anchor || !focus) {
        return
      }
      if (
        !this.isComposed
      ) {
        const { anchor: oldAnchor, focus: oldFocus } = contentState.cursor
        if (
          anchor.key !== oldAnchor.key ||
          anchor.offset !== oldAnchor.offset ||
          focus.key !== oldFocus.key ||
          focus.offset !== oldFocus.offset
        ) {
          const needRender = contentState.checkNeedRender(contentState.cursor) || contentState.checkNeedRender({ start, end })
          contentState.cursor = { anchor, focus }
          if (needRender) {
            return contentState.partialRender()
          }
        }
      }

      const block = contentState.getBlock(anchor.key)
      if (
        anchor.key === focus.key &&
        anchor.offset !== focus.offset &&
        block.functionType !== 'codeContent' &&
        block.functionType !== 'languageInput'
      ) {
        const reference = contentState.getPositionReference()
        const { formats } = contentState.selectionFormats()
        eventCenter.dispatch('muya-format-picker', { reference, formats })
      } else {
        eventCenter.dispatch('muya-format-picker', { reference: null })
      }
    }

    const mousemoveHandler = event => {
      if (this.isControlDown && !event.metaKey && !event.ctrlKey) {
        this.isControlDown = false
        container.classList.remove('ag-meta-or-ctrl')
      } else if (!this.isControlDown && (event.metaKey || event.ctrlKey)) {
        this.isControlDown = true
        container.classList.add('ag-meta-or-ctrl')
      }
    }

    eventCenter.attachDOMEvent(container, 'keyup', handler) // temp use input event
    eventCenter.attachDOMEvent(container, 'mousemove', mousemoveHandler)
  }
}

export default Keyboard
