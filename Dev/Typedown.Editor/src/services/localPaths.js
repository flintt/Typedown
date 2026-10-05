// Local image paths: resolving one against the document folder, and the file:/// address a copy gives it.
import path from 'path-browserify'

/**
 * Resolve a relative image path against the document folder. `path-browserify` is POSIX-only, so a Windows base
 * path such as `C:\notes\sub` was treated as a single segment and `../img/a.png` resolved to `/img/a.png`
 * (upstream #17). Handle drive-letter and UNC prefixes explicitly and resolve the rest as POSIX segments.
 */
export const resolveLocalPath = (basePath, src) => {
  const toPosix = p => p.replace(/\\/g, '/')
  let base = toPosix(basePath)
  let prefix = ''
  const drive = base.match(/^([a-zA-Z]:)(\/.*)?$/)
  const unc = base.match(/^(\/\/[^/]+\/[^/]+)(\/.*)?$/)
  if (drive) {
    prefix = drive[1]
    base = drive[2] || '/'
  } else if (unc) {
    prefix = unc[1]
    base = unc[2] || '/'
  }
  return prefix + path.resolve(base, toPosix(src))
}

/** An absolute local path as a file URL: "C:\\notes\\a b.png" is file:///C:/notes/a%20b.png, a UNC path file://server/... */
const toFileUrl = absolute => {
  const posix = absolute.replace(/\\/g, '/')
  const encoded = encodeURI(posix).replace(/#/g, '%23').replace(/\?/g, '%3F')
  if (posix.startsWith('//')) return 'file:' + encoded
  return 'file://' + (posix.startsWith('/') ? '' : '/') + encoded
}

/**
 * The HTML a copy puts on the clipboard, with its local images at absolute file:/// addresses. The HTML is made from
 * the Markdown, where an image is relative to the document ("images/a.png"): Word and mail clients pasting it have no
 * document folder to resolve that against, and showed an empty frame. Web, data and file URLs stay as they are; so do
 * relative ones while the document has no folder (not saved yet).
 */
export const absoluteImageUrls = (html, basePath = window.basePath) => {
  if (!html || !/<img/i.test(html)) return html
  const wrapper = document.createElement('div')
  wrapper.innerHTML = html
  for (const img of Array.from(wrapper.querySelectorAll('img'))) {
    const src = img.getAttribute('src') || ''
    const drive = /^[a-zA-Z]:[\\/]/.test(src)
    if (!src || (!drive && /^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(src))) continue
    let local = src
    try { local = decodeURI(src) } catch { /* left as written */ }
    const absolute = /^(?:\/|\\\\|[a-zA-Z]:[\\/])/.test(local) ? local : basePath ? resolveLocalPath(basePath, local) : null
    if (absolute) img.setAttribute('src', toFileUrl(absolute))
  }
  return wrapper.innerHTML
}
