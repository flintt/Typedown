// A list item dragged by its marker (contentState/listDragCtrl). Pointer events, not the browser's drag and drop: in
// the web view that is a drop from outside, which the host takes for files and pictures.
class ListDrag {
  constructor (muya) {
    this.muya = muya
    const { container, eventCenter, contentState } = muya
    let hovered = null
    const setHovered = li => {
      if (hovered === li) return
      hovered && hovered.classList.remove('ag-marker-hover')
      hovered = li
      hovered && hovered.classList.add('ag-marker-hover')
    }

    eventCenter.attachDOMEvent(container, 'mousedown', event => {
      if (event.button !== 0 || event.shiftKey || event.ctrlKey || event.altKey) return
      if (contentState.listDragBegin(event)) {
        // Not a click into the text: no caret, no selection started.
        event.preventDefault()
      }
    })
    // The marker as a handle: the grab cursor over it.
    eventCenter.attachDOMEvent(container, 'mousemove', event => {
      if (contentState.listDrag) return
      const item = event.target && event.target.closest && event.target.closest('li') ? contentState.listItemByMarker(event) : null
      setHovered(item ? container.querySelector(`[id="${item.key}"]`) : null)
    })
    eventCenter.attachDOMEvent(container, 'mouseleave', () => setHovered(null))
    eventCenter.attachDOMEvent(window, 'mousemove', event => {
      if (contentState.listDrag) contentState.listDragMove(event)
    })
    eventCenter.attachDOMEvent(window, 'mouseup', event => {
      if (!contentState.listDrag) return
      if (contentState.listDragEnd()) event.preventDefault()
    })
    eventCenter.attachDOMEvent(window, 'keydown', event => {
      if (contentState.listDrag && event.key === 'Escape') contentState.listDragCancel()
    })
    eventCenter.attachDOMEvent(window, 'blur', () => contentState.listDrag && contentState.listDragCancel())
  }
}

export default ListDrag
