import { h } from '../snabbdom'
import { svgIcon } from './renderSvgIcon'

// The code block's copy button; `diagram`: a figure's (ag-diagram-copy) - a diagram's, a display formula's or a table's -
// which copies a picture or HTML too (ContentState.copyBlock).
const renderCopyButton = (diagram = false) => {
  const selector = diagram ? 'a.ag-code-copy.ag-diagram-copy' : 'a.ag-code-copy'

  return h(selector, {
    attrs: {
      'data-tooltip': 'CopyContent',
      contenteditable: 'false'
    }
  }, svgIcon('copy', 16))
}

export default renderCopyButton
