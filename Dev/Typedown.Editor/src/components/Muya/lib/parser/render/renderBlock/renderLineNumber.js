import { h } from '../snabbdom'
import { CLASS_OR_ID } from '../../../config'

const NEW_LINE_EXP = /\n(?!$)/g

const renderLineNumberRows = codeContent => {
  const { text } = codeContent
  const match = text.match(NEW_LINE_EXP)
  let linesNum = match ? match.length + 1 : 1
  if (text.endsWith('\n')) {
    linesNum++
  }
  const data = {
    attrs: {
      'aria-hidden': true
    }
  }
  const children = [...new Array(linesNum)].map(() => h('span'))

  // Taken out of what a copy puts on the clipboard (copyCutCtrl.js, services/plainText.ts): its block spans were a line
  // of their own before the code.
  return h(`span.line-numbers-rows.${CLASS_OR_ID.AG_COPY_REMOVE}`, data, children)
}

export default renderLineNumberRows
