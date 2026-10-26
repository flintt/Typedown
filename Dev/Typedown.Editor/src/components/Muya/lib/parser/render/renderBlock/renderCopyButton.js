import { h } from '../snabbdom'
import { svgIcon } from './renderSvgIcon'

// The code block's copy button; `diagram`: a diagram's (ag-diagram-copy), on its figure, which copies the picture too.
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
