/**
 * The room a document drawn a part at a time keeps for what is not drawn yet (StateRender.estimateRest): the height of
 * the root's ::before (above, with the fraction of a pixel keepInPlace leaves as its padding) and ::after (below).
 *
 * Written into two rules of a style sheet of its own, not as properties on the root: any change of the root's own style -
 * even custom properties registered as not inherited - had the browser restyle every block under it, 35-60 ms a write on
 * a document of 2,900 blocks, written every 100 ms while it is drawn and with the fraction on almost every slice (LT01: a
 * 1.5 MB document of raw HTML blocks; slices that should take 40 ms took 80-250, frames 200-300 ms apart). Changed
 * through the style sheet's rules, the browser restyles the two pseudo-elements and lays the root out: 8 ms.
 *
 * Only through the CSSOM, never the style element's text: the page watches the head for style changes (predictHeights
 * watch()), and a change of its text there would start the predictions over.
 *
 * The values are kept per root (a document put away with its root - Muya.takeDocument - takes its room with it) and
 * are what the rules say for the root in the page (every root is #ag-editor-id; only one is in the page at a time).
 */
const values = new WeakMap()
let rules = null
let shown = null

const NAMES = ['above', 'below', 'fraction']

function sheetRules() {
  if (rules) return rules
  try {
    const style = document.createElement('style')
    style.setAttribute('data-td-room', '')
    document.head.appendChild(style)
    const sheet = style.sheet
    sheet.insertRule('#ag-editor-id::before {}', 0)
    sheet.insertRule('#ag-editor-id::after {}', 1)
    rules = { before: sheet.cssRules[0].style, after: sheet.cssRules[1].style }
  } catch (err) {
    rules = null
  }
  return rules
}

// Made at once, while the page starts: made later, its coming would be a change of the head's styles in a drawing.
if (typeof document !== 'undefined' && document.head) sheetRules()

const write = (style, property, value) => {
  if (value) {
    if (style.getPropertyValue(property) !== value) style.setProperty(property, value)
  } else if (style.getPropertyValue(property)) style.removeProperty(property)
}

/** Puts the room of `root` on the page: what the rules say from now on (a root shown in place of another). */
export function showRoom(root) {
  const r = sheetRules()
  if (!r) return
  shown = root
  const v = (root && values.get(root)) || {}
  write(r.before, 'height', v.above)
  write(r.before, 'padding-top', v.fraction)
  write(r.after, 'height', v.below)
}

/** The room `name` ('above', 'below' or 'fraction') of `root`, as a CSS length ('120px'), or '' where none is kept. */
export function getRoom(root, name) {
  const v = root && values.get(root)
  return (v && v[name]) || ''
}

/** Sets the room `name` of `root` to `value` (a CSS length), or takes it away (null or ''). */
export function setRoom(root, name, value) {
  if (!root || !NAMES.includes(name)) return
  let v = values.get(root)
  if (!v) values.set(root, (v = {}))
  if (value) v[name] = value
  else delete v[name]
  if (root === shown || root.isConnected) showRoom(root)
}
