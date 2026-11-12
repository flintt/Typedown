import remote from 'services/remote/common'

// What a link does, whichever way it is asked: Ctrl+click (or a click in reading mode or raw HTML) and the context menu's
// Open link and Remove link. Each way had its own, and the menu checked the link's place in the text - an editing check -
// before opening, so a link it could not place there would not open from the menu though a click opened it (the
// modularity audit's M04). Opening wants only the address; removing wants the place, and keeps the link's text.

/** The address followed: "#…" in this document (its heading), anything else as the system opens it. */
export const followLink = (muya, href) => {
  if (!href) return false
  if (href.startsWith('#')) muya.clickEvent.scrollToAnchor(href)
  else remote.openNewWindow(href)
  return true
}

/**
 * What a link's source becomes with the link gone: its text. "[text](url)", "[text][ref]", "[text][]" and "[text]" give
 * their text; "<url>" its address; "<a …>text</a>" its text; null for anything else (a bare address is text already).
 * The old in-place unlink (ContentState.unlink, which nothing called) did this from Muya's tokens; the menu only knew the
 * first form.
 */
export const linkText = (raw) => {
  if (!raw) return null
  const inline = /^\[([\s\S]*)\]\([\s\S]*\)$/.exec(raw)
  if (inline) return inline[1]
  const reference = /^\[([\s\S]*?)\](?:\[[^\]]*\])?$/.exec(raw)
  if (reference) return reference[1]
  const auto = /^<((?:[a-z][a-z0-9+.-]*:|mailto:)[^<>\s]+|[^<>\s@]+@[^<>\s@]+)>$/i.exec(raw)
  if (auto) return auto[1]
  const html = /^<a\b[^>]*>([\s\S]*)<\/a>$/i.exec(raw)
  if (html) return html[1]
  return null
}

/**
 * A link out of a block's text, its text kept: the source at [from, to) checked to be the link still (the block may have
 * changed since it was right-clicked), replaced, the caret after the text, the change told. False when it was not there
 * or has no text to keep.
 */
export const removeLink = (contentState, { key, start, end, raw }) => {
  const block = contentState.getBlock(key)
  if (!block || !raw || !(end > start) || block.text.slice(start, end) !== raw) return false
  const text = linkText(raw)
  if (text == null) return false
  block.text = block.text.slice(0, start) + text + block.text.slice(end)
  const caret = { key: block.key, offset: start + text.length }
  contentState.cursor = { start: caret, end: caret, anchor: caret, focus: caret }
  contentState.render()
  contentState.muya.dispatchChange()
  return true
}

/**
 * The menu's Edit link: the link's address chosen in its Markdown, to type over - the caret in the link shows the
 * Markdown. A link written another way (<address>, a bare address, [text][label]) is chosen whole. False when the text
 * has moved on since the right-click.
 */
export const editLink = (contentState, { key, start, end, raw }) => {
  const block = contentState.getBlock(key)
  if (!block || !raw || !(end > start) || block.text.slice(start, end) !== raw) return false
  const address = /\]\(\s*<?([^\s)>]*)/.exec(raw)
  const from = address && address[1] ? start + address.index + address[0].length - address[1].length : start
  const to = address && address[1] ? from + address[1].length : end
  contentState.cursor = { start: { key, offset: from }, end: { key, offset: to }, anchor: { key, offset: from }, focus: { key, offset: to } }
  contentState.render()
  return true
}
