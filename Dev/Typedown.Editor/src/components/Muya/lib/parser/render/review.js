// Review mode's marks in the visual editor (services/reviewMap.ts gives them, by leaf block): inserted runs drawn the way
// a search's matches are (spans around the text, so the caret and the text stay Muya's own), deleted text in the span's
// pseudo-elements (on the page, not in the text: the caret never goes into it and it is never written out), and the
// marks of a whole leaf - the bar of a new paragraph, a program's tag - as classes and attributes on the leaf's element.
// The blocks deleted above or below a leaf are a strip each, an element of their own in the leaf (its text in a
// pseudo-element, so never in the leaf's text): each has its own buttons (the user, 2026-11-07: three links deleted
// together were one strip, one revision).

import { h } from './snabbdom'

/** The class of a review run, for the selector (and the emoji and backslash runs, which take only a class). */
export const reviewClassOf = segment => {
  let selector = segment.inserted ? 'ag-review-ins' : 'ag-review-mark'
  if (segment.source) selector += '.ag-review-ext'
  if (segment.deletedBefore) selector += '.ag-review-del-before'
  if (segment.deletedAfter) selector += '.ag-review-del-after'
  return selector
}

export const reviewAttrsOf = segment => {
  const attrs = { 'data-rid': segment.rid }
  if (segment.deletedBefore) attrs['data-del-before'] = segment.deletedBefore
  if (segment.deletedAfter) attrs['data-del-after'] = segment.deletedAfter
  return attrs
}

/**
 * A search's highlights with the leaf's review runs among them, sorted: a run that overlaps a match is left out while
 * the search is on (the match is what the reader is looking for, and the two cannot nest).
 */
export const withReviewLights = (highlights, segments) => {
  const lights = segments
    .filter(s => !highlights.some(h => h.start < s.end && s.start < h.end))
    .map(s => ({ start: s.start, end: s.end, active: false, review: s }))
  return [...highlights, ...lights].sort((a, b) => a.start - b.start)
}

/** The strips of the blocks deleted above (`where` 'above') or below a leaf, as elements; none for code (see below). */
export const reviewStrips = (marks, where, isCode) => {
  if (!marks || isCode) return []
  const list = (where === 'above' ? marks.deletedAbove : marks.deletedBelow) || []
  return list.map(d => h(`span.ag-review-strip.ag-review-strip-${where}`, {
    attrs: { 'data-rid': d.rid, [`data-del-${where}`]: d.text, contenteditable: 'false' }
  }))
}

/**
 * The leaf's own marks on its selector and attributes. Code is not split into runs (its highlighting redraws it): a bar,
 * and its deleted blocks one strip in a pseudo-element (an element in the code would be highlighted over).
 */
export const reviewBlockMarks = (selector, data, marks, isCode) => {
  if (marks.whole || (isCode && marks.segments.length)) selector += '.ag-review-whole'
  const above = isCode ? marks.deletedAbove || [] : []
  if (above.length) {
    selector += '.ag-review-del-above'
    data.attrs['data-del-above'] = above.map(d => d.text).join('\n')
    data.attrs['data-del-above-rid'] = above[0].rid
  }
  const below = isCode ? marks.deletedBelow || [] : []
  if (below.length) {
    selector += '.ag-review-del-below'
    data.attrs['data-del-below'] = below.map(d => d.text).join('\n')
    data.attrs['data-del-below-rid'] = below[0].rid
  }
  if (marks.sources.length) {
    selector += '.ag-review-tagged'
    data.attrs['data-review-source'] = marks.sources.join(', ')
  }
  // What its marks say changed (a task ticked, an item indented) and a block split off: a bar of their own at its left.
  if (marks.structure) selector += '.ag-review-structure'
  if (marks.split) selector += '.ag-review-split'
  const own = marks.whole || marks.structure || marks.split || (isCode && marks.segments.length ? marks.segments[0].rid : null)
  if (own) data.attrs['data-rid'] = own
  return selector
}
