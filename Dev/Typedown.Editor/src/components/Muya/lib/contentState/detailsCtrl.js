// <details> blocks (parser/marked/detailsBlock.js; Settings > Editor > Details blocks): opened and closed in the view.
const detailsCtrl = ContentState => {
  /**
   * Opens or closes a <details> block where it is shown, the document not changed: `block.expanded` is the view's (the
   * file's `<details open>` is `block.detailsOpen`, written back as it was). `figure` is its element, set at once; a
   * later drawing draws it so too (renderContainerBlock).
   */
  ContentState.prototype.toggleDetails = function (block, figure, expanded = !block.expanded) {
    block.expanded = expanded
    if (!figure) return
    figure.classList.toggle('ag-details-open', expanded)
    const toggle = figure.querySelector(':scope > .ag-details-toggle')
    if (toggle) {
      toggle.textContent = expanded ? '▾' : '▸'
      toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false')
    }
  }
}

export default detailsCtrl
