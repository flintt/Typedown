// Dragging a list item by the handle shown beside it (eventHandler/listDrag) to another place in its list, at another
// level too (a reader's request, 2026-11-02; the handle alone, not the marker too - where to press was unclear). The item goes with the items nested in it; a line shows where it will land, indented to the level it
// will have. Within one list and the lists nested in it: the line can only show a level the items around it allow
// (no deeper than one below the item above, no shallower than the item below), so no item after it changes parent.
const GHOST_ID = 'mu-list-drag-ghost'
const START_DISTANCE = 4

const listDragCtrl = ContentState => {
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

  // A drag begun on the handle beside an item.
  ContentState.prototype.listDragBegin = function (event, item) {
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
    // The items it can land beside, in the document's order: not itself or what is nested in it. Found once per drag,
    // and the one under the pointer by halves - they are top to bottom - so a long list reads a few lines per move.
    if (!drag.items) drag.items = [...outerEl.querySelectorAll('li')].filter(el => el !== draggedEl && !draggedEl.contains(el))
    const items = drag.items
    const middle = el => { const r = this.listItemTextElement(el)?.getBoundingClientRect(); return r ? r.top + r.height / 2 : -Infinity }
    let low = 0
    let high = items.length
    while (low < high) {
      const mid = (low + high) >> 1
      if (event.clientY < middle(items[mid])) high = mid
      else low = mid + 1
    }
    const next = low
    const prevEl = items[next - 1] || null
    const nextEl = items[next] || null
    const prev = prevEl && this.getBlock(prevEl.id)
    const nextBlock = nextEl && this.getBlock(nextEl.id)
    const maxDepth = prev ? this.listItemDepth(prev) + 1 : 0
    const minDepth = Math.min(nextBlock ? this.listItemDepth(nextBlock) : 0, maxDepth)
    // One level per indent of the list, measured where one item is nested in another; once per drag.
    if (drag.indent == null) {
      const firstText = this.listItemTextElement(outerEl.querySelector('li'))
      drag.base = firstText ? firstText.getBoundingClientRect().left : outerEl.getBoundingClientRect().left
      const nested = outerEl.querySelector('li li')
      drag.indent = 28
      if (nested) {
        const inner = this.listItemTextElement(nested)?.getBoundingClientRect().left
        const outerText = this.listItemTextElement(nested.parentElement.closest('li'))?.getBoundingClientRect().left
        if (inner && outerText && inner > outerText) drag.indent = inner - outerText
      }
    }
    const { base, indent } = drag
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
    document.body.classList.remove('ag-list-dragging', 'ag-list-pressed')
    const ghost = document.getElementById(GHOST_ID)
    ghost && ghost.remove()
  }

  ContentState.prototype.listDragEnd = function () {
    const drag = this.listDrag
    const started = drag && drag.started
    const drop = drag && drag.drop
    this.listDragCancel()
    if (!started) return false
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
    // Its new list's kind and spacing, from an item already there. Alone in a list made for it, it is tight: it kept the
    // spacing of the loose list it came from, the next item made there (Enter, another drag) took it on, and read again
    // the whole list was loose - its source spaced with blank lines (a reader, 2026-11-03).
    const sibling = list.children.find(child => child !== item)
    item.isLooseListItem = sibling ? sibling.isLooseListItem : false
    if (sibling) {
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
