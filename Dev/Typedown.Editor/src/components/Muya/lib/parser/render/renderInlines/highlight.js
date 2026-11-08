import { union } from '../../../utils'
import { reviewAttrsOf, reviewClassOf } from '../review'

// change text to highlight vdom
export default function highlight (h, block, rStart, rEnd, token) {
  const { text } = block
  const { highlights } = token
  let result = []
  const unions = []
  let pos = rStart

  if (highlights) {
    for (const light of highlights) {
      const un = union({ start: rStart, end: rEnd }, light)
      if (un) unions.push(un)
    }
  }

  if (unions.length) {
    for (const u of unions) {
      const { start, end, active, review } = u

      if (pos < start) {
        result.push(text.substring(pos, start))
      }

      if (review) result.push(h(`span.${reviewClassOf(review)}`, { attrs: reviewAttrsOf(review) }, text.substring(start, end)))
      else result.push(h(`span.${this.getHighlightClassName(active)}`, text.substring(start, end)))
      pos = end
    }
    if (pos < rEnd) {
      result.push(block.text.substring(pos, rEnd))
    }
  } else {
    result = [text.substring(rStart, rEnd)]
  }

  return result
}
