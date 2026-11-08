// Dragging a list item by its marker to another place in its list, at another level too (a reader's request,
// 2026-11-02). The item goes with the items nested in it; a line shows where it will land, indented to the level it
// will have. Within one list and the lists nested in it: the line can only show a level the items around it allow
// (no deeper than one below the item above, no shallower than the item below), so no item after it changes parent.
const GHOST_ID = 'mu-list-drag-ghost'
const START_DISTANCE = 4

const listDragCtrl = ContentState => {
  // The list item block whose marker the event is on (left of its text, not its task box), or null.
  ContentState.prototype.listItemByMarker = function (event) {
    const target = event.target
    if (!target || !target.closest || target.tagName === 'INPUT' || target.closest('.ag-front-icon')) return null
    // The item is the one whose own line is under the pointer, found in the list around it: a bullet is drawn outside
    // its item's box (the event is the list's), and a nested item's marker lies inside the outer item's box.
    let list = target.closest('ul, ol')
    if (!list || !this.muya.container.contains(list)) return null
    while (list.parentElement && list.parentElement.closest('ul, ol') && this.muya.container.contains(list.parentElement.closest('ul, ol'))) {
      list = list.parentElement.closest('ul, ol')
    }
    const li = [...list.querySelectorAll('li')].find(item => {
      const r = this.listItemTextElement(item)?.getBoundingClientRect()
      return r && event.clientY >= r.top && event.clientY <= r.bottom
    })
    if (!li) return null
    const text = this.listItemTextElement(li)
    if (!text) return null
    if (event.clientX >= text.getBoundingClientRect().left) return null
    const block = this.getBlock(li.id)
    return block && block.type === 'li' ? block : null
  }

  // The element of a list item's own first line: its paragraph (after a task's box), not the lists nested in it.
  ContentState.prototype.listItemTextElement = function (li) {
    for (const child of li.children) {
      if (child.tagName === 'INPUT' || child.tagName === 'UL' || child.tagName === 'OL') continue
      return child
    }
    return null
  }

  // How deep a list item is: 0 in the outermost list.
  ContentState.prototype.listItemDepth = function (item) {
    let depth = 0
    let outer = this.getParent(this.getParent(item))
    while (outer && outer.type === 'li') {
      depth++
      outer = this.getParent(this.getParent(outer))
    }
    return depth
  }

  ContentState.prototype.outermostList = function (item) {
    let list = this.getParent(item)
    while (this.getParent(list) && this.getParent(list).type === 'li') list = this.getParent(this.getParent(list))
    return list
  }

  ContentState.prototype.listDragBegin = function (event) {
    const item = this.listItemByMarker(event)
    if (!item || document.body.classList.contains('read-only')) return false
    this.listDrag = { item, startX: event.clientX, startY: event.clientY, started: false, drop: null }
    return true
  }

  ContentState.prototype.listDragMove = function (event) {
    const drag = this.listDrag
    if (!drag) return
    if (!drag.started) {
      if (Math.abs(event.clientX - drag.startX) < START_DISTANCE && Math.abs(event.clientY - drag.startY) < START_DISTANCE) return
      drag.started = true
      document.body.classList.add('ag-list-dragging')
    }
    event.preventDefault()
    const outer = this.outermostList(drag.item)
    const outerEl = this.muya.container.querySelector(`[id="${outer.key}"]`)
    const draggedEl = this.muya.container.querySelector(`[id="${drag.item.key}"]`)
    if (!outerEl || !draggedEl) return this.listDragCancel()
    // The items it can land beside, in the document's order: not itself or what is nested in it.
    const items = [...outerEl.querySelectorAll('li')].filter(el => el !== draggedEl && !draggedEl.contains(el))
    let next = items.findIndex(el => {
      const r = this.listItemTextElement(el)?.getBoundingClientRect()
      return r && event.clientY < r.top + r.height / 2
    })
    if (next < 0) next = items.length
    const prevEl = items[next - 1] || null
    const nextEl = items[next] || null
    const prev = prevEl && this.getBlock(prevEl.id)
    const nextBlock = nextEl && this.getBlock(nextEl.id)
    const maxDepth = prev ? this.listItemDepth(prev) + 1 : 0
    const minDepth = Math.min(nextBlock ? this.listItemDepth(nextBlock) : 0, maxDepth)
    // One level per indent of the list, measured where one item is nested in another.
    const firstText = this.listItemTextElement(outerEl.querySelector('li'))
    const base = firstText ? firstText.getBoundingClientRect().left : outerEl.getBoundingClientRect().left
    const nested = outerEl.querySelector('li li')
    let indent = 28
    if (nested) {
      const inner = this.listItemTextElement(nested)?.getBoundingClientRect().left
      const outerText = this.listItemTextElement(nested.parentElement.closest('li'))?.getBoundingClientRect().left
      if (inner && outerText && inner > outerText) indent = inner - outerText
    }
    const wanted = this.listItemDepth(drag.item) + Math.round((event.clientX - drag.startX) / indent)
    const depth = Math.max(minDepth, Math.min(maxDepth, wanted))
    drag.drop = { prev, depth }
    // The line where it lands.
    const outerRect = outerEl.getBoundingClientRect()
    const top = nextEl ? this.listItemTextElement(nextEl).getBoundingClientRect().top - 2 : outerRect.bottom
    let ghost = document.getElementById(GHOST_ID)
    if (!ghost) {
      ghost = document.createElement('div')
      ghost.id = GHOST_ID
      document.body.appendChild(ghost)
    }
    const left = base + depth * indent
    Object.assign(ghost.style, { left: `${left}px`, top: `${top}px`, width: `${Math.max(40, outerRect.right - left)}px` })
  }

  ContentState.prototype.listDragCancel = function () {
    this.listDrag = null
    document.body.classList.remove('ag-list-dragging')
    const ghost = document.getElementById(GHOST_ID)
    ghost && ghost.remove()
  }

  ContentState.prototype.listDragEnd = function () {
    const drag = this.listDrag
    const started = drag && drag.started
    const drop = drag && drag.drop
    this.listDragCancel()
    // A click on the marker, no drag: the caret at the start of the item's text, where a click there put it before.
    if (drag && !started) {
      const text = this.firstInDescendant(drag.item)
      if (text) {
        const at = { key: text.key, offset: 0 }
        this.cursor = { start: at, end: at }
        this.partialRender()
      }
      return false
    }
    if (!drop) return started
    this.moveListItem(drag.item, drop.prev, drop.depth)
    return true
  }

  /**
   * The item (and what is nested in it) moved to just after prev in the document's order, at the given depth; with
   * no prev, to the top of its list. A list left empty goes; the item takes its new list's kind (ordered or not) and
   * spacing. One change for the host, so one undo puts it back.
   */
  ContentState.prototype.moveListItem = function (item, prev, depth) {
    const outer = this.outermostList(item)
    const from = this.getParent(item)
    const wasDepth = this.listItemDepth(item)
    const wasPrev = this.getPreSibling(item)
    // Where it is already: nothing to do.
    if (prev === wasPrev && depth === wasDepth) return
    if (!prev && !wasPrev && wasDepth === 0 && depth === 0) return
    this.removeBlock(item)
    if (!from.children.length) this.removeBlock(from)
    let list
    if (!prev) {
      list = outer
      if (list.children.length) this.insertBefore(item, list.children[0])
      else this.appendChild(list, item)
    } else if (depth === this.listItemDepth(prev) + 1) {
      list = prev.children[prev.children.length - 1]
      if (!list || !/^(ul|ol)$/.test(list.type)) {
        list = this.createBlock(from.type)
        if (from.listType) list.listType = from.listType
        if (from.type === 'ol') list.start = 1
        this.appendChild(prev, list)
      }
      if (list.children.length) this.insertBefore(item, list.children[0])
      else this.appendChild(list, item)
    } else {
      let anchor = prev
      while (this.listItemDepth(anchor) > depth) anchor = this.getParent(this.getParent(anchor))
      this.insertAfter(item, anchor)
      list = this.getParent(item)
    }
    // Its new list's kind and spacing, from an item already there.
    const sibling = list.children.find(child => child !== item)
    if (sibling) {
      item.isLooseListItem = sibling.isLooseListItem
      if (item.listItemType !== 'task' && sibling.listItemType !== 'task') {
        item.listItemType = sibling.listItemType
        item.bulletMarkerOrDelimiter = sibling.bulletMarkerOrDelimiter
      }
    }
    const text = this.firstInDescendant(item)
    if (text) {
      const at = { key: text.key, offset: text.text.length }
      this.cursor = { start: at, end: at }
    }
    this.render()
    this.muya.dispatchChange()
  }
}

export default listDragCtrl
