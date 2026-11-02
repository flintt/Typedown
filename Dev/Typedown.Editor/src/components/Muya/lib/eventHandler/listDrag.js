// A list item dragged by the handle shown beside it (contentState/listDragCtrl), and by nothing else: the marker too
// left it unclear where to press (a reader's report). Pointer events, not
// the browser's drag and drop: in the web view that is a drop from outside, which the host takes for files and pictures.
// The handle (⋮⋮, left of the item under the pointer) is what shows an item can be dragged: the marker alone gave no
// sign of it (a reader's report). Placed once a frame, from the one item under the pointer.
const HANDLE_ID = 'mu-list-drag-handle'

let measure = null

// The width of an item's marker as the browser draws it: what it says ("• ", "3. ") in the text's font.
const markerWidth = (li, style) => {
  const list = li.parentElement
  let label = '• '
  if (list && list.tagName === 'OL') {
    const number = (parseInt(list.getAttribute('start'), 10) || 1) + [...list.children].indexOf(li)
    label = `${number}${li.getAttribute('data-marker') || '.'} `
  }
  measure = measure || document.createElement('canvas').getContext('2d')
  measure.font = style.font || `${style.fontSize} ${style.fontFamily}`
  return measure.measureText(label).width
}

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

    let hideTimer = 0
    const keep = () => { clearTimeout(hideTimer); hideTimer = 0 }
    const hide = () => {
      keep()
      handleItem = null
      handle.style.display = 'none'
    }
    // Gone a moment after the pointer left the item, not at once: on its way to the handle it crosses the marker, the
    // list's margin and the editor's edge, and the handle went before it got there (a reader's report).
    const hideSoon = () => { if (!hideTimer) hideTimer = setTimeout(hide, 400) }
    // On the way: level with the handle's line, between the handle and the item's text.
    const towardHandle = event => {
      if (!handleItem || handle.style.display === 'none') return false
      const h = handle.getBoundingClientRect()
      const text = contentState.listItemTextElement(handleItem.li)
      const right = text ? text.getBoundingClientRect().left + 8 : h.right + 60
      return event.clientY >= h.top - 10 && event.clientY <= h.bottom + 10 && event.clientX >= h.left - 6 && event.clientX <= right
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
      // The item whose own line the pointer is on: left of a nested item's text is its parent's (or grandparent's)
      // indent, which is theirs in the page, and the handle showed for them there (a reader's report). Of the items in
      // this one, the deepest whose text holds the pointer's height.
      for (const inner of [...li.querySelectorAll('li')].reverse()) {
        const r = contentState.listItemTextElement(inner)?.getBoundingClientRect()
        if (r && event.clientY >= r.top && event.clientY <= r.bottom) { li = inner; break }
      }
      const block = contentState.getBlock(li.id)
      return block && block.type === 'li' ? { block, li } : null
    }

    const place = () => {
      frame = 0
      const event = pending
      pending = null
      if (!event || contentState.listDrag) return
      const found = itemAt(event)
      if (!found) return towardHandle(event) ? keep() : hideSoon()
      keep()
      if (handleItem && found.li === handleItem.li && handle.style.display !== 'none') return
      // On the way to the handle of a nested item the pointer crosses its parent's indent, which is the parent's: the
      // handle went to the parent before it was reached (a reader's report). Kept while the pointer stays on its line.
      if (handleItem && towardHandle(event)) return
      const text = contentState.listItemTextElement(found.li)
      if (!text) return hide()
      const r = text.getBoundingClientRect()
      const style = getComputedStyle(text)
      const line = Math.min(r.height, parseFloat(style.lineHeight) || r.height)
      handleItem = found
      // Close to its marker, just left of it (a reader asked for it nearer than it was): the marker is the browser's
      // own (::marker), drawn outside the item, so its width is measured from what it says - a bullet, "10. " - in the
      // text's font; a task's box is an element. Left of the paragraph marker icon when that is shown on the item.
      let left = r.left - markerWidth(found.li, style) - 4
      const box = found.li.querySelector(':scope > input')
      if (box) left = Math.min(left, box.getBoundingClientRect().left - 4)
      const icon = text.querySelector('.ag-front-icon')
      if (icon && getComputedStyle(icon).display !== 'none') left = Math.min(left, icon.getBoundingClientRect().left - 2)
      Object.assign(handle.style, { display: 'block', left: `${Math.max(2, left - 18)}px`, top: `${r.top + line / 2 - 10}px` })
    }

    eventCenter.attachDOMEvent(container, 'mousemove', event => {
      if (contentState.listDrag) return
      pending = event
      if (!frame) frame = requestAnimationFrame(place)
    })
    // Outside the editor (the handle can sit in its margin): kept on the way to the handle and over it.
    eventCenter.attachDOMEvent(window, 'mousemove', event => {
      if (contentState.listDrag || !handleItem || container.contains(event.target)) return
      if (event.target === handle || towardHandle(event)) keep()
      else hideSoon()
    })
    eventCenter.attachDOMEvent(handle, 'mouseenter', keep)
    eventCenter.attachDOMEvent(handle, 'mouseleave', event => {
      if (!container.contains(event.relatedTarget)) hideSoon()
    })
    // Typing or scrolling: the handle goes until the pointer moves again.
    eventCenter.attachDOMEvent(window, 'keydown', event => {
      if (contentState.listDrag && event.key === 'Escape') return contentState.listDragCancel()
      if (!contentState.listDrag) hide()
    })
    eventCenter.attachDOMEvent(window, 'scroll', () => { if (!contentState.listDrag) hide() })

    // A drag begun on the handle.
    eventCenter.attachDOMEvent(handle, 'mousedown', event => {
      if (event.button !== 0 || !handleItem) return
      event.preventDefault()
      event.stopPropagation()
      if (contentState.listDragBegin(event, handleItem.block)) {
        contentState.listDrag.byHandle = true
        document.body.classList.add('ag-list-pressed')
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
