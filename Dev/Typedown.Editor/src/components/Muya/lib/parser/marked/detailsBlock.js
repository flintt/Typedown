// A <details> block written as GitHub writes one, taken as a block of its own (Settings > Editor > Details blocks): its
// summary and the Markdown in it, which CommonMark splits at the first blank line into an HTML block, the blocks after it
// and a lone </details> (Typedown issue #1). Only the forms written back exactly as they came are taken:
//
//   <details>                     or  <details open>
//   <summary>Title</summary>          (on its own line, on the <details> line, or none)
//
//   Markdown, any blocks
//
//   </details>
//
// <details> with other attributes, a <summary> written otherwise or no </details> of its own: left to the HTML block rule,
// as before.

const OPEN = /^<details( open)?>(.*)$/
const SUMMARY = /^<summary>(.*)<\/summary>$/
// A <details> or </details> line inside: nested ones are counted, so the first block ends at its own closing line.
const NESTED_OPEN = /^ {0,3}<details[\s>]/i
const CLOSE = /^<\/details>$/
const FENCE = /^ {0,3}(`{3,}|~{3,})/

/**
 * The <details> block `src` starts with, or null. `length`: how much of `src` it takes, its line end included; `body`:
 * the Markdown in it without the blank lines around it, which `leadingBlank` and `trailingBlank` count.
 */
export function matchDetails (src) {
  if (!src.startsWith('<details')) return null
  const lines = src.split('\n')
  const open = OPEN.exec(lines[0])
  if (!open) return null
  let summary = null
  let summaryInline = false
  let at = 1
  const rest = open[2]
  if (rest !== '') {
    const inline = SUMMARY.exec(rest)
    if (!inline || inline[1].includes('<summary')) return null
    summary = inline[1]
    summaryInline = true
  } else if (lines.length > 1) {
    const own = SUMMARY.exec(lines[1])
    if (own && !own[1].includes('<summary')) {
      summary = own[1]
      at = 2
    }
  }
  let depth = 1
  let fence = null
  let end = -1
  for (let i = at; i < lines.length; i++) {
    const line = lines[i]
    if (fence) {
      if (new RegExp(`^ {0,3}${fence[0] === '`' ? '`' : '~'}{${fence.length},}\\s*$`).test(line)) fence = null
      continue
    }
    const opening = FENCE.exec(line)
    if (opening) {
      fence = opening[1]
      continue
    }
    if (NESTED_OPEN.test(line)) depth++
    else if (CLOSE.test(line) || /^ {0,3}<\/details>\s*$/i.test(line)) {
      depth--
      if (depth === 0) {
        // Its own closing line written exactly so, or the block is left to the HTML rule.
        if (!CLOSE.test(line)) return null
        end = i
        break
      }
    }
  }
  if (end < 0) return null
  const inner = lines.slice(at, end)
  let leadingBlank = 0
  while (leadingBlank < inner.length && inner[leadingBlank].trim() === '') leadingBlank++
  let trailingBlank = 0
  while (trailingBlank < inner.length - leadingBlank && inner[inner.length - 1 - trailingBlank].trim() === '') trailingBlank++
  const body = inner.slice(leadingBlank, inner.length - trailingBlank).join('\n')
  const taken = lines.slice(0, end + 1).join('\n')
  return {
    length: taken.length + (end + 1 < lines.length ? 1 : 0),
    open: !!open[1],
    summary,
    summaryInline,
    leadingBlank,
    trailingBlank,
    body
  }
}
