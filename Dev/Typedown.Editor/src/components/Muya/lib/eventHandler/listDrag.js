// A list item dragged by its marker or by the handle shown beside it (contentState/listDragCtrl). Pointer events, not
// the browser's drag and drop: in the web view that is a drop from outside, which the host takes for files and pictures.
// The handle (⋮⋮, left of the item under the pointer) is what shows an item can be dragged: the marker alone gave no
// sign of it (a reader's report). Placed once a frame, from the one item under the pointer.
const HANDLE_ID = 'mu-list-drag-handle'

class ListDrag {
  constructor (muya) {
    this.muya = muya
    const { container, eventCenter, contentState } = muya
    const handle = document.createElement('div')
    handle.id = HANDLE_ID
    handle.textContent = '⋮⋮'
    handle.setAttribute('aria-hidden', 'true')
    document.body.appendChild(handle)
    this.handle = handle
    let handleItem = null
    let pending = null
    let frame = 0

    const hide = () => {
      handleItem = null
      handle.style.display = 'none'
    }
    hide()

    // The item under the pointer: the innermost one around it, or, over a bullet drawn outside its item, the list's
    // child on that line.
    const itemAt = event => {
      const target = event.target
      if (!target || !target.closest || document.body.classList.contains('read-only')) return null
      if (target === handle) return handleItem
      let li = target.closest('li')
      if (!li && /^(UL|OL)$/.test(target.tagName)) {
        li = [...target.children].find(child => {
          const r = child.tagName === 'LI' && contentState.listItemTextElement(child)?.getBoundingClientRect()
          return r && event.clientY >= r.top && event.clientY <= r.bottom
        })
      }
      if (!li || !container.contains(li)) return null
      const block = contentState.getBlock(li.id)
      return block && block.type === 'li' ? { block, li } : null
    }

    const place = () => {
      frame = 0
      const event = pending
      pending = null
      if (!event || contentState.listDrag) return
      const found = itemAt(event)
      if (!found) return hide()
      if (found === handleItem) return
      const text = contentState.listItemTextElement(found.li)
      if (!text) return hide()
      const r = text.getBoundingClientRect()
      const line = Math.min(r.height, parseFloat(getComputedStyle(text).lineHeight) || r.height)
      handleItem = found
      // Left of the marker and of the paragraph marker icon (.ag-front-icon, 30 px left of the text).
      Object.assign(handle.style, { display: 'block', left: `${Math.max(2, r.left - 58)}px`, top: `${r.top + line / 2 - 10}px` })
    }

    eventCenter.attachDOMEvent(container, 'mousemove', event => {
      if (contentState.listDrag) return
      pending = event
      if (!frame) frame = requestAnimationFrame(place)
    })
    eventCenter.attachDOMEvent(container, 'mouseleave', event => {
      if (event.relatedTarget !== handle) hide()
    })
    eventCenter.attachDOMEvent(handle, 'mouseleave', event => {
      if (!container.contains(event.relatedTarget)) hide()
    })
    // Typing or scrolling: the handle goes until the pointer moves again.
    eventCenter.attachDOMEvent(window, 'keydown', event => {
      if (contentState.listDrag && event.key === 'Escape') return contentState.listDragCancel()
      if (!contentState.listDrag) hide()
    })
    eventCenter.attachDOMEvent(window, 'scroll', () => { if (!contentState.listDrag) hide() })

    // A drag begun on the handle, or on an item's marker.
    eventCenter.attachDOMEvent(handle, 'mousedown', event => {
      if (event.button !== 0 || !handleItem) return
      event.preventDefault()
      event.stopPropagation()
      if (contentState.listDragBegin(event, handleItem.block)) contentState.listDrag.byHandle = true
    })
    eventCenter.attachDOMEvent(container, 'mousedown', event => {
      if (event.button !== 0 || event.shiftKey || event.ctrlKey || event.altKey) return
      if (contentState.listDragBegin(event)) {
        // Not a click into the text: no caret, no selection started.
        event.preventDefault()
      }
    })
    eventCenter.attachDOMEvent(window, 'mousemove', event => {
      if (!contentState.listDrag) return
      if (contentState.listDrag.started) handle.style.display = 'none'
      contentState.listDragMove(event)
    })
    eventCenter.attachDOMEvent(window, 'mouseup', event => {
      if (!contentState.listDrag) return
      if (contentState.listDragEnd()) event.preventDefault()
      hide()
    })
    eventCenter.attachDOMEvent(window, 'blur', () => { contentState.listDrag && contentState.listDragCancel(); hide() })
  }

  destroy () {
    this.handle && this.handle.remove()
  }
}

export default ListDrag
