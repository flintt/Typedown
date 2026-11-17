// A quote's lazy lines (lexer markLazyLines: the lines written with no ">") kept on the paragraph's span as lazyLines,
// their indexes, and lazyLineCount, how many lines the paragraph had then. The export writes them with no ">" only while
// the paragraph has that many lines: an edit that added or took lines and is not followed here (a paste, two paragraphs
// joined) writes every line with its "> " - the structure kept, only not the writing. Enter and Shift+Enter follow them.

const lineOf = (text, offset) => text.slice(0, offset).split('\n').length - 1

const drop = span => { delete span.lazyLines; delete span.lazyLineCount }

// The lines in effect: the marks, if the paragraph still has the lines they were made for.
export function lazyLinesOf (span, lineCount) {
  return span.lazyLines && span.lazyLineCount === lineCount ? span.lazyLines : null
}

// Enter at offset: the lines up to the caret's stay with the span, the rest go to the new paragraph's span (its first
// line, the caret's line's end, is never lazy - nothing is above it in its paragraph).
export function splitLazyLines (span, newSpan, text, offset) {
  const lines = lazyLinesOf(span, text.split('\n').length)
  if (!lines) return
  const at = lineOf(text, offset)
  const kept = lines.filter(i => i <= at)
  const moved = lines.filter(i => i > at).map(i => i - at)
  if (kept.length) { span.lazyLines = kept; span.lazyLineCount = at + 1 } else drop(span)
  if (moved.length) { newSpan.lazyLines = moved; newSpan.lazyLineCount = text.split('\n').length - at }
}

// Shift+Enter at offset: the caret's line breaks in two, the lines after it one further on.
export function breakLazyLine (span, text, offset) {
  const lines = lazyLinesOf(span, text.split('\n').length)
  if (!lines) return
  const at = lineOf(text, offset)
  span.lazyLines = lines.map(i => i > at ? i + 1 : i)
  span.lazyLineCount = text.split('\n').length + 1
}
