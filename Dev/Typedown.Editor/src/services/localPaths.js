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

/**
 * An absolute local path as a file address for Word: "C:\\notes\\流程 图.png" is file:///C:/notes/流程 图.png. The
 * characters stay as they are: Word does not decode %E6%B5%81... back into the name, and showed an empty frame for
 * every picture with Chinese in its path (a space as such works too). Only # and ? are escaped, which would end the
 * path. A UNC path is file://server/...
 */
const toFileUrl = absolute => {
  const posix = absolute.replace(/\\/g, '/').replace(/#/g, '%23').replace(/\?/g, '%3F')
  if (posix.startsWith('//')) return 'file:' + posix
  return 'file://' + (posix.startsWith('/') ? '' : '/') + posix
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

/**
 * SVG pictures in a copy's HTML as PNG, embedded: Word does not show an SVG it is given in pasted HTML, wherever it
 * is, but takes a picture inside the HTML (data:). Each is drawn from the picture on the page (already loaded, so no
 * waiting), at twice its size for sharpness, and keeps the size it is shown at. One that is not on the page, or that
 * the page may not read (another site's), stays as it was.
 */
export const svgImagesAsPng = (html, page = document) => {
  if (!html || !/\.svg/i.test(html)) return html
  const drawn = Array.from(page.querySelectorAll('#ag-editor-id img'))
  const key = src => { try { return decodeURI(src).replace(/\\/g, '/').toLowerCase() } catch { return src.toLowerCase() } }
  const wrapper = page.createElement('div')
  wrapper.innerHTML = html
  for (const img of Array.from(wrapper.querySelectorAll('img'))) {
    const src = img.getAttribute('src') || ''
    if (!/\.svg(?:[?#].*)?$/i.test(src)) continue
    const source = drawn.find(d => d.complete && d.naturalWidth > 0 && key(d.src) === key(src))
    if (!source) continue
    try {
      const rect = source.getBoundingClientRect()
      const width = Math.round(rect.width || source.naturalWidth)
      const height = Math.round(rect.height || source.naturalHeight)
      const canvas = page.createElement('canvas')
      canvas.width = width * 2
      canvas.height = height * 2
      canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height)
      img.setAttribute('src', canvas.toDataURL('image/png'))
      img.setAttribute('width', String(width))
      img.setAttribute('height', String(height))
    } catch (e) { /* a picture the page may not read stays an address */ }
  }
  return wrapper.innerHTML
}
