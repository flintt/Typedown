// Review mode's marks in the visual editor (services/reviewMap.ts gives them, by leaf block): inserted runs drawn the way
// a search's matches are (spans around the text, so the caret and the text stay Muya's own), deleted text in the span's
// pseudo-elements (on the page, not in the text: the caret never goes into it and it is never written out), and the
// marks of a whole leaf - the bar of a new paragraph, the strip of paragraphs deleted above it, a program's tag - as
// classes and attributes on the leaf's element.

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

/** The leaf's own marks on its selector and attributes. Code is not split into runs (its highlighting redraws it): a bar. */
export const reviewBlockMarks = (selector, data, marks, isCode) => {
  if (marks.whole || (isCode && marks.segments.length)) selector += '.ag-review-whole'
  const above = marks.deletedAbove || []
  if (above.length) {
    selector += '.ag-review-del-above'
    data.attrs['data-del-above'] = above.map(d => d.text).join('\n')
    data.attrs['data-del-above-rid'] = above[0].rid
  }
  const below = marks.deletedBelow || []
  if (below.length) {
    selector += '.ag-review-del-below'
    data.attrs['data-del-below'] = below.map(d => d.text).join('\n')
    data.attrs['data-del-below-rid'] = below[0].rid
  }
  if (marks.sources.length) {
    selector += '.ag-review-tagged'
    data.attrs['data-review-source'] = marks.sources.join(', ')
  }
  if (marks.whole) data.attrs['data-rid'] = marks.whole
  return selector
}
