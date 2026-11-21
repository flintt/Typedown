// Dragging a list item by the handle shown beside it (eventHandler/listDrag) to another place in the document (a
// reader's request, 2026-11-02; the handle alone, not the marker too - where to press was unclear). The item goes with
// the items nested in it; a line shows where it will land, indented to the level it will have.
// - Into any list (its own, another one further on, one in a quotation; docs/todo.md "列表拖动：跨列表与拖出列表", A):
//   the line can only show a level the items around it allow (no deeper than one below the item above, no shallower
//   than the item below), so no item after it changes parent; it takes that list's kind, marker and spacing.
// - Out of the lists, between two other blocks (B): it becomes a list of its own, one item long, keeping its kind,
//   marker, nested items and box. Next to a list it would run into once the Markdown is read again (same kind, same
//   marker - a blank line between does not part them) it goes into that list instead, and the line shows it there.
// - Near the window's top or bottom edge the page scrolls, faster the nearer, and the place follows without the pointer
//   moving; a long document's blocks drawn in while it scrolls are places too.
const GHOST_ID = 'mu-list-drag-ghost'
const START_DISTANCE = 4
// How near the window's edge the page scrolls (px, at most a fifth of the window), and how fast at the edge (px a frame).
const EDGE_ZONE = 56
const EDGE_SPEED = 28

const isListEl = el => !!el && (el.tagName === 'UL' || el.tagName === 'OL')

// The outermost list of a list item element: up through the lists nested in items.
const outerListEl = li => {
  let list = li.parentElement
  while (list && list.parentElement && list.parentElement.tagName === 'LI' && isListEl(list.parentElement.parentElement)) {
    list = list.parentElement.parentElement
  }
  return list
}

// The marker a list is written with: the one Markdown tells lists apart by ("-" "*" "+", or "." ")" after a number).
const listMarker = list => {
  const first = list && list.children && list.children[0]
  const marker = first && first.bulletMarkerOrDelimiter
  return marker || (list.type === 'ol' ? '.' : '-')
}
const isTask = item => !!item && item.listItemType === 'task'
// Two lists one after the other that the Markdown, read again, makes one: the same kind (numbered, bulleted, or of tasks -
// Muya reads a run of tasks among bulleted items as a list of its own, MarkText #870) and the same marker.
const runTogether = (a, b) => !!a && !!b && /^(ul|ol)$/.test(a.type) && a.type === b.type && listMarker(a) === listMarker(b) &&
  (a.type === 'ol' || isTask(a.children[0]) === isTask(b.children[0]))

// The page's scroll speed for the pointer at y (px a frame; up is negative, 0 away from the edges).
const edgeSpeed = y => {
  const height = window.innerHeight
  const zone = Math.min(EDGE_ZONE, height / 5)
  if (y < zone) return -Math.ceil(EDGE_SPEED * Math.min(1, (zone - y) / zone))
  if (y > height - zone) return Math.ceil(EDGE_SPEED * Math.min(1, (y - (height - zone)) / zone))
  return 0
}

const listDragCtrl = ContentState => {
  // The element of a list item's own first line: its paragraph (after a task's box), not the lists nested in it.
  ContentState.prototype.listItemTextElement = function (li) {
    if (!li) return null
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

  // The blocks that go with the item when it leaves: its list if it is the only item, and a quotation that held only
  // that list (and so on up).
  ContentState.prototype.listDragVanishing = function (item) {
    const gone = []
    let block = item
    let parent = this.getParent(block)
    while (parent && parent.children.length === 1 && /^(ul|ol|blockquote)$/.test(parent.type)) {
      gone.push(parent.key)
      block = parent
      parent = this.getParent(parent)
    }
    return gone
  }

  // A drag begun on the handle beside an item.
  ContentState.prototype.listDragBegin = function (event, item) {
    if (!item || document.body.classList.contains('read-only')) return false
    this.listDrag = { item, startX: event.clientX, startY: event.clientY, x: event.clientX, y: event.clientY, started: false, drop: null }
    return true
  }

  ContentState.prototype.listDragMove = function (event) {
    const drag = this.listDrag
    if (!drag) return
    drag.x = event.clientX
    drag.y = event.clientY
    if (!drag.started) {
      if (Math.abs(event.clientX - drag.startX) < START_DISTANCE && Math.abs(event.clientY - drag.startY) < START_DISTANCE) return
      drag.started = true
      document.body.classList.add('ag-list-dragging')
      drag.fromDepth = this.listItemDepth(drag.item)
      drag.vanishing = new Set(this.listDragVanishing(drag.item))
      drag.measures = new Map()
      // A long document drawn a part at a time: the blocks drawn in while the drag goes on are places too.
      const root = this.listDragRoot()
      if (root && typeof MutationObserver === 'function') {
        drag.observer = new MutationObserver(() => {
          drag.items = null
          if (!drag.redraw) {
            drag.redraw = requestAnimationFrame(() => {
              drag.redraw = 0
              if (this.listDrag === drag) this.listDragUpdate()
            })
          }
        })
        drag.observer.observe(root, { childList: true })
      }
    }
    event.preventDefault()
    this.listDragUpdate()
    this.listDragAutoScroll()
  }

  // The page scrolled under the pointer (the wheel, or the drag at an edge): the place again, where the pointer is now.
  ContentState.prototype.listDragScrolled = function () {
    if (this.listDrag && this.listDrag.started) this.listDragUpdate()
  }

  // Scrolling while the pointer is near the window's top or bottom edge, a step a frame.
  ContentState.prototype.listDragAutoScroll = function () {
    const drag = this.listDrag
    if (!drag || !drag.started || drag.scrollFrame || !edgeSpeed(drag.y)) return
    const tick = () => {
      drag.scrollFrame = 0
      if (this.listDrag !== drag) return
      const speed = edgeSpeed(drag.y)
      if (!speed) return
      const was = window.scrollY
      window.scrollBy(0, speed)
      if (window.scrollY !== was) this.listDragUpdate()
      drag.scrollFrame = requestAnimationFrame(tick)
    }
    drag.scrollFrame = requestAnimationFrame(tick)
  }

  // The items it can land beside, in the document's order: every list item in the document but itself and what is
  // nested in it. Found once per drag (again once more of the document is drawn), and the one under the pointer by
  // halves - they are top to bottom - so a long document reads a few lines per move.
  ContentState.prototype.listDragCandidates = function (draggedEl) {
    const root = this.muya.container
    return [...root.querySelectorAll('li')].filter(el => {
      if (el === draggedEl || draggedEl.contains(el) || !el.id) return false
      // Not an item folded away by Hide Done Items (services/hideDone.ts): its text, not drawn, measured at the window's
      // top - the line jumped there and the search by halves missed places (dragging up, the user's trial 1.4.3.6).
      if (el.closest('.td-done')) return false
      const block = this.getBlock(el.id)
      return !!block && block.type === 'li'
    })
  }

  // Where the items of a list begin, and how far one level indents them: measured where one item is nested in another
  // (once per list a drag).
  ContentState.prototype.listDragMeasure = function (listEl) {
    const drag = this.listDrag
    const known = drag.measures.get(listEl.id)
    if (known) return known
    const firstText = this.listItemTextElement(listEl.querySelector('li'))
    const base = firstText ? firstText.getBoundingClientRect().left : listEl.getBoundingClientRect().left
    let indent = 28
    const nested = listEl.querySelector('li li')
    if (nested) {
      const inner = this.listItemTextElement(nested)?.getBoundingClientRect().left
      const outerText = this.listItemTextElement(nested.parentElement.closest('li'))?.getBoundingClientRect().left
      if (inner && outerText && inner > outerText) indent = inner - outerText
    }
    const measure = { base, indent }
    drag.measures.set(listEl.id, measure)
    return measure
  }

  // A place in a list: after prevEl (none: at the list's top), before nextEl (none: at its end).
  ContentState.prototype.listDropIn = function (listEl, prevEl, nextEl) {
    const drag = this.listDrag
    // Folded items right before the place (Hide Done Items): it goes after them, where its line shows - under the fold's
    // line, by the next item; after the item before the fold it went in among them.
    const folded = nextEl ? nextEl.previousElementSibling : [...listEl.children].pop()
    if (folded && folded.tagName === 'LI' && folded.classList.contains('td-done') && folded !== prevEl) prevEl = folded
    const prev = prevEl && this.getBlock(prevEl.id)
    const next = nextEl && this.getBlock(nextEl.id)
    const maxDepth = prev ? this.listItemDepth(prev) + 1 : 0
    const minDepth = Math.min(next ? this.listItemDepth(next) : 0, maxDepth)
    const { base, indent } = this.listDragMeasure(listEl)
    const wanted = drag.fromDepth + Math.round((drag.x - drag.startX) / indent)
    const depth = Math.max(minDepth, Math.min(maxDepth, wanted))
    const listRect = listEl.getBoundingClientRect()
    const top = nextEl ? this.listItemTextElement(nextEl).getBoundingClientRect().top - 2 : listRect.bottom
    const left = base + depth * indent
    return { prev, depth, list: this.getBlock(listEl.id), place: 'list', line: { top, left, right: listRect.right } }
  }

  // The blocks a place out of the lists is between: the children of the document, or of the quotation the pointer is
  // in, but those leaving with the item.
  // The element the document's blocks are drawn in.
  ContentState.prototype.listDragRoot = function () {
    const container = this.muya.container
    if (!container) return null
    return container.id === 'ag-editor-id' ? container : container.querySelector('#ag-editor-id') || container
  }

  ContentState.prototype.listDragBlocksIn = function (parentEl) {
    const drag = this.listDrag
    return [...parentEl.children].filter(el => el.id && !drag.vanishing.has(el.id) && this.getBlock(el.id))
  }

  // A place out of the lists: between two blocks (or before the first, after the last).
  ContentState.prototype.listDropOut = function (draggedEl) {
    const drag = this.listDrag
    const root = this.listDragRoot()
    const y = drag.y
    let parentEl = root
    let kids = this.listDragBlocksIn(parentEl)
    for (;;) {
      const inside = kids.find(el => {
        if (el.tagName !== 'BLOCKQUOTE') return false
        const r = el.getBoundingClientRect()
        return y >= r.top && y <= r.bottom
      })
      if (!inside) break
      parentEl = inside
      kids = this.listDragBlocksIn(parentEl)
    }
    // The gap nearest the pointer: after the blocks whose middle is above it (by halves: a document's blocks are many).
    const middle = el => { const r = el.getBoundingClientRect(); return r.top + r.height / 2 }
    let low = 0
    let high = kids.length
    while (low < high) {
      const mid = (low + high) >> 1
      if (y < middle(kids[mid])) high = mid
      else low = mid + 1
    }
    const afterEl = kids[low - 1] || null
    const beforeEl = kids[low] || null
    const after = afterEl && this.getBlock(afterEl.id)
    const before = beforeEl && this.getBlock(beforeEl.id)
    // The list it would be: its own kind and marker. Next to a list it would run into, it goes into that list.
    const from = this.getParent(drag.item)
    const own = { type: from.type, children: [drag.item] }
    const candidate = el => el !== draggedEl && !draggedEl.contains(el) && this.getBlock(el.id)?.type === 'li'
    if (runTogether(after, own)) {
      const items = [...afterEl.querySelectorAll('li')].filter(candidate)
      if (items.length) return this.listDropIn(afterEl, items[items.length - 1], null)
    }
    if (runTogether(own, before)) {
      const first = [...beforeEl.querySelectorAll('li')].find(candidate)
      if (first) return this.listDropIn(beforeEl, null, first)
    }
    const parent = parentEl === root ? null : this.getBlock(parentEl.id)
    const a = afterEl && afterEl.getBoundingClientRect()
    const b = beforeEl && beforeEl.getBoundingClientRect()
    const edge = a || b || root.getBoundingClientRect()
    const top = a && b ? (a.bottom + b.top) / 2 - 1 : a ? a.bottom + 4 : b ? b.top - 4 : edge.top
    return { out: { parent, after, before }, place: 'out', line: { top, left: edge.left, right: edge.right } }
  }

  // Where it lands for the pointer where it is now, and the line that shows it.
  ContentState.prototype.listDragUpdate = function () {
    const drag = this.listDrag
    if (!drag || !drag.started) return
    const root = this.muya.container
    const draggedEl = root && root.querySelector(`[id="${drag.item.key}"]`)
    if (!draggedEl) return this.listDragCancel()
    if (!drag.items) drag.items = this.listDragCandidates(draggedEl)
    const items = drag.items
    const y = drag.y
    const middle = el => { const r = this.listItemTextElement(el)?.getBoundingClientRect(); return r ? r.top + r.height / 2 : -Infinity }
    let low = 0
    let high = items.length
    while (low < high) {
      const mid = (low + high) >> 1
      if (y < middle(items[mid])) high = mid
      else low = mid + 1
    }
    const prevEl = items[low - 1] || null
    const nextEl = items[low] || null
    const prevList = prevEl && outerListEl(prevEl)
    const nextList = nextEl && outerListEl(nextEl)
    let drop
    if (prevList && prevList === nextList) drop = this.listDropIn(prevList, prevEl, nextEl)
    else if (prevList && y <= prevList.getBoundingClientRect().bottom) drop = this.listDropIn(prevList, prevEl, null)
    else if (nextList && y >= nextList.getBoundingClientRect().top) drop = this.listDropIn(nextList, null, nextEl)
    else drop = this.listDropOut(draggedEl)
    drag.drop = drop
    // The line where it lands.
    let ghost = document.getElementById(GHOST_ID)
    if (!ghost) {
      ghost = document.createElement('div')
      ghost.id = GHOST_ID
      document.body.appendChild(ghost)
    }
    const { top, left, right } = drop.line
    ghost.dataset.place = drop.place
    Object.assign(ghost.style, { left: `${left}px`, top: `${top}px`, width: `${Math.max(40, right - left)}px` })
  }

  ContentState.prototype.listDragCancel = function () {
    const drag = this.listDrag
    if (drag) {
      if (drag.scrollFrame) cancelAnimationFrame(drag.scrollFrame)
      if (drag.redraw) cancelAnimationFrame(drag.redraw)
      if (drag.observer) drag.observer.disconnect()
    }
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
    this.moveListItem(drag.item, drop)
    return true
  }

  // The item out of its list; the list goes if that leaves it empty, and a quotation it leaves empty. What was either
  // side of what went, for listDragJoin.
  ContentState.prototype.listDragDetach = function (item) {
    const from = this.getParent(item)
    this.removeBlock(item)
    item.preSibling = null
    item.nextSibling = null
    if (from.children.length) return null
    let gone = from
    let parent = this.getParent(gone)
    while (parent && parent.type === 'blockquote' && parent.children.length === 1) {
      gone = parent
      parent = this.getParent(gone)
    }
    const around = { above: this.getPreSibling(gone), below: this.getNextSibling(gone) }
    // The blank lines read before what went are the gap where it was (as removeThematicBreak does): a document that
    // began with the list does not begin with a blank line now.
    if (around.below) {
      if (gone.blankLinesBefore != null) {
        around.below.blankLinesBefore = gone.blankLinesBefore
        around.below.blankLinesParent = gone.blankLinesParent
      } else {
        delete around.below.blankLinesBefore
      }
    }
    this.removeBlock(gone)
    return around
  }

  // A list gone from between two lists the Markdown would read as one: one here too, so what is saved reads back the
  // same (spaced as the first of them).
  ContentState.prototype.listDragJoin = function (around) {
    if (!around) return
    const { above, below } = around
    if (!runTogether(above, below) || this.getNextSibling(above) !== below) return
    const loose = !!(above.children[0] && above.children[0].isLooseListItem)
    for (const child of [...below.children]) {
      this.removeBlock(child)
      child.preSibling = null
      child.nextSibling = null
      child.isLooseListItem = loose
      this.appendChild(above, child)
    }
    this.removeBlock(below)
  }

  // The item as one of a list's: its kind and marker (a task keeps its box, but not in a numbered list, where Markdown
  // has none and it would be gone once saved).
  ContentState.prototype.listItemJoining = function (item, list) {
    const sibling = list.children.find(child => child !== item)
    // Alone in a list made for it, it is tight: it kept the spacing of the loose list it came from, the next item made
    // there (Enter, another drag) took it on, and read again the whole list was loose - its source spaced with blank
    // lines (a reader, 2026-11-03).
    item.isLooseListItem = sibling ? sibling.isLooseListItem : false
    if (!sibling) return
    item.bulletMarkerOrDelimiter = sibling.bulletMarkerOrDelimiter
    if (list.type === 'ol') {
      if (item.listItemType === 'task') {
        const box = item.children[0]
        if (box && box.type === 'input') {
          item.children.shift()
          if (item.children[0]) item.children[0].preSibling = null
        }
      }
      item.listItemType = 'order'
    } else if (item.listItemType !== 'task') {
      item.listItemType = 'bullet'
    }
  }

  /**
   * The item (and what is nested in it) moved to where the drop says: in a list (after prev in the document's order, at
   * the given depth; with no prev, to the top of `list`), or out of the lists (`out`: after `after`, or before `before`,
   * in `parent` or the document) as a list of its own. A list left empty goes; the item takes its new list's kind and
   * spacing. One change for the host, so one undo puts it back.
   */
  ContentState.prototype.moveListItem = function (item, drop) {
    const from = this.getParent(item)
    if (drop.out) {
      const { parent, after, before } = drop.out
      const list = this.createBlock(from.type)
      list.listType = from.type === 'ol' ? 'order' : item.listItemType === 'task' ? 'task' : 'bullet'
      if (from.type === 'ol') list.start = 1
      const around = this.listDragDetach(item)
      item.isLooseListItem = false
      this.appendChild(list, item)
      if (after) this.insertAfter(list, after)
      else if (before) this.insertBefore(list, before)
      else if (parent) this.appendChild(parent, list)
      else {
        const last = this.blocks[this.blocks.length - 1]
        if (last) this.insertAfter(list, last)
        else this.blocks.push(list)
      }
      this.listDragJoin(around)
      return this.listDragMoved(item)
    }
    const { prev, depth } = drop
    const target = drop.list
    const wasDepth = this.listItemDepth(item)
    const wasPrev = this.getPreSibling(item)
    // Where it is already: nothing to do.
    if (prev && prev === wasPrev && depth === wasDepth) return
    if (!prev && !wasPrev && wasDepth === 0 && depth === 0 && (!target || target === this.outermostList(item))) return
    const around = this.listDragDetach(item)
    let list
    if (!prev) {
      list = target || this.outermostList(item)
      if (list.children.length) this.insertBefore(item, list.children[0])
      else this.appendChild(list, item)
    } else if (depth === this.listItemDepth(prev) + 1) {
      list = prev.children[prev.children.length - 1]
      if (!list || !/^(ul|ol)$/.test(list.type)) {
        list = this.createBlock(from.type)
        list.listType = from.type === 'ol' ? 'order' : isTask(item) ? 'task' : 'bullet'
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
    this.listItemJoining(item, list)
    this.listDragJoin(around)
    this.listDragSplit(item)
    this.listDragMoved(item)
  }

  // A task among bulleted items, or a bulleted item among tasks: Muya reads each run of them as a list of its own
  // (MarkText #870), so the list is split around it here too, as typing "[ ] " in an item does (updateTaskListItem) -
  // the page shows what the saved Markdown reads back as. A run next to a list of its kind and marker joins that one.
  ContentState.prototype.listDragSplit = function (item) {
    const list = this.getParent(item)
    if (!list || list.type !== 'ul' || list.children.every(child => isTask(child) === isTask(item))) return
    const runs = []
    for (const child of list.children) {
      const last = runs[runs.length - 1]
      if (last && isTask(last[0]) === isTask(child)) last.push(child)
      else runs.push([child])
    }
    const lists = [list]
    let anchor = list
    for (const run of runs.slice(1)) {
      const next = this.createBlock('ul', { listType: isTask(run[0]) ? 'task' : 'bullet' })
      for (const child of run) {
        this.removeBlock(child, list)
        child.preSibling = null
        child.nextSibling = null
        this.appendChild(next, child)
      }
      this.insertAfter(next, anchor)
      anchor = next
      lists.push(next)
    }
    list.listType = isTask(runs[0][0]) ? 'task' : 'bullet'
    // Alone in its run, it is tight (see listItemJoining).
    if (runs.some(run => run.length === 1 && run[0] === item)) item.isLooseListItem = false
    const first = lists[0]
    const last = lists[lists.length - 1]
    const after = this.getNextSibling(last)
    this.listDragJoin({ above: this.getPreSibling(first), below: first })
    this.listDragJoin({ above: last, below: after })
  }

  // The caret to the end of the moved item's first line, the page drawn again, the host told: one change.
  ContentState.prototype.listDragMoved = function (item) {
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
