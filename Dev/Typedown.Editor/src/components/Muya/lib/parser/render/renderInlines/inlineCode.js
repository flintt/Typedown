import { CLASS_OR_ID } from '../../../config'

export default function inlineCode (h, cursor, block, token, outerClass) {
  const className = this.getClassName(outerClass, block, token, cursor)
  const { marker } = token
  const { start, end } = token.range

  const startMarker = this.highlight(h, block, start, start + marker.length, token)
  const endMarker = this.highlight(h, block, end - marker.length, end, token)
  const from = start + marker.length
  const to = end - marker.length
  let content
  if (block.functionType === 'cellContent') {
    // In a table cell \| is a pipe that does not end the cell, code or not (GFM): its backslash is shown as a backslash
    // escape is - hidden, grey while the caret is in the code - not as part of the code.
    content = []
    let at = from
    for (let i = from; i < to - 1; i++) {
      if (block.text[i] === '\\' && block.text[i + 1] === '|') {
        if (i > at) content.push(...this.highlight(h, block, at, i, token))
        content.push(h(`span.${className}.${CLASS_OR_ID.AG_REMOVE}`, this.highlight(h, block, i, i + 1, token)))
        at = i + 1
      }
    }
    content.push(...this.highlight(h, block, at, to, token))
  } else {
    content = this.highlight(h, block, from, to, token)
  }

  return [
    h(`span.${className}.${CLASS_OR_ID.AG_REMOVE}`, startMarker),
    h(`code.${CLASS_OR_ID.AG_INLINE_RULE}`, {
      attrs: {
        spellcheck: 'false'
      }
    }, content),
    h(`span.${className}.${CLASS_OR_ID.AG_REMOVE}`, endMarker)
  ]
}
