import katex from 'katex'
import 'katex/contrib/mhchem'
import katexCss from '!!raw-loader!katex/dist/katex.css'
import { texColors } from '../components/Muya/lib/parser/render/texColors'
import marked from '../components/Muya/lib/parser/marked'
import { sanitize, escapeHTML } from '../components/Muya/lib/utils'
import { EXPORT_DOMPURIFY_CONFIG } from '../components/Muya/lib/config'
import { absoluteImageUrls } from './localPaths'

// What the copy buttons of a display formula and of a table put on the clipboard besides their Markdown
// (ContentState.copyBlock): a formula's picture, a table's HTML.

const blobDataUrl = blob => new Promise((resolve, reject) => {
  const reader = new FileReader()
  reader.onload = () => resolve(String(reader.result))
  reader.onerror = () => reject(reader.error)
  reader.readAsDataURL(blob)
})

// The page's KaTeX fonts as @font-face rules holding the fonts themselves (data: addresses): a picture drawn from an SVG
// loads nothing from outside it, and a formula drawn without its fonts is in the wrong letters. Each face's first source
// (the woff2 the page uses). Made once; a face that cannot be read is left out (the browser's fallback draws it).
let fontFacesPromise = null
const katexFontFaces = () => {
  if (!fontFacesPromise) {
    fontFacesPromise = (async () => {
      const faces = []
      for (const sheet of Array.from(document.styleSheets)) {
        let rules
        try { rules = sheet.cssRules } catch (e) { continue }
        for (const rule of Array.from(rules)) {
          if (!(rule instanceof CSSFontFaceRule)) continue
          const family = (rule.style.getPropertyValue('font-family') || '').replace(/["']/g, '').trim()
          if (!family.startsWith('KaTeX_')) continue
          const first = (rule.style.getPropertyValue('src') || '').match(/url\(["']?([^"')]+)["']?\)/)
          if (!first) continue
          faces.push({ family, style: rule.style.getPropertyValue('font-style') || 'normal', weight: rule.style.getPropertyValue('font-weight') || 'normal', url: new URL(first[1], sheet.href || document.baseURI).href })
        }
      }
      const css = await Promise.all(faces.map(async face => {
        try {
          const response = await fetch(face.url)
          if (!response.ok) return ''
          const data = await blobDataUrl(await response.blob())
          return `@font-face{font-family:${face.family};font-style:${face.style};font-weight:${face.weight};src:url(${data})}`
        } catch (e) {
          return ''
        }
      }))
      return css.join('\n')
    })()
    // Not kept when it failed as a whole: the next copy tries again.
    fontFacesPromise.catch(() => { fontFacesPromise = null })
  }
  return fontFacesPromise
}

// KaTeX's own rules without its @font-face ones (relative addresses, which a picture cannot load).
const katexRules = () => katexCss.replace(/@font-face\s*\{[^}]*\}/g, '')

/**
 * A display formula (its TeX) as a PNG, base64 without the data: prefix - what Word, OneNote, mail and chat paste: drawn
 * by KaTeX as the editor draws it, in black on white whatever theme the editor shows, at a 16px base size (an export's)
 * and twice its pixels, with a little white around it. Throws when KaTeX cannot read the formula or the picture cannot
 * be made.
 */
export const mathPicture = async (tex) => {
  const html = katex.renderToString(texColors(tex), { displayMode: true })
  const fonts = await katexFontFaces()
  // Laid out on the page (out of sight) to be measured: the size the picture needs.
  const holder = document.createElement('div')
  holder.style.cssText = 'position:fixed;left:-100000px;top:0;visibility:hidden;contain:layout'
  const box = document.createElement('div')
  box.setAttribute('style', 'display:inline-block;padding:6px 8px;background:#fff;color:#000;font-size:16px;line-height:1.5')
  box.innerHTML = html
  const display = box.querySelector('.katex-display')
  if (display) display.style.margin = '0'
  holder.appendChild(box)
  document.body.appendChild(holder)
  let width, height
  try {
    await document.fonts.ready
    const rect = box.getBoundingClientRect()
    width = Math.ceil(rect.width)
    height = Math.ceil(rect.height)
  } finally {
    holder.remove()
  }
  if (!(width > 0 && height > 0)) throw new Error('the formula has no size')
  const style = document.createElement('style')
  style.textContent = fonts + '\n' + katexRules()
  const content = document.createElement('div')
  content.appendChild(style)
  content.appendChild(box)
  const xhtml = new XMLSerializer().serializeToString(content)
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}"><foreignObject x="0" y="0" width="${width}" height="${height}">${xhtml}</foreignObject></svg>`
  const image = new Image()
  await new Promise((resolve, reject) => {
    image.onload = resolve
    image.onerror = () => reject(new Error('the formula picture did not load'))
    image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg)
  })
  const scale = 2
  const canvas = document.createElement('canvas')
  canvas.width = width * scale
  canvas.height = height * scale
  const context = canvas.getContext('2d')
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.drawImage(image, 0, 0, canvas.width, canvas.height)
  return canvas.toDataURL('image/png').slice('data:image/png;base64,'.length)
}

/**
 * A display formula as MathML (KaTeX's, with its TeX as an annotation): for Word, which makes an equation it can edit of
 * MathML put on the clipboard under a format of its name (the host's SetTextAndPngAsync); the picture and the text stay
 * for every other program. Null when KaTeX cannot read the formula.
 */
export const mathMl = (tex) => {
  try {
    const html = katex.renderToString(texColors(tex), { displayMode: true, output: 'mathml' })
    const math = html.match(/<math[\s\S]*<\/math>/)
    return math ? math[0] : null
  } catch (e) {
    return null
  }
}

const ALIGN = /text-align:\s*(left|center|right)/i

/**
 * A table's Markdown as the HTML a word processor or a spreadsheet pastes as a table: the header row in <thead> as <th>,
 * each cell's bold, italic, struck, code and links as they are, each column's alignment as an align attribute too (Word
 * and Excel read that one), pictures at absolute addresses; formulas in cells as their TeX between $ signs (KaTeX's
 * drawing is nothing those programs can read). Thin borders, so the table shows as one where it is pasted.
 */
export const tableHtml = (markdown, options = {}) => {
  const raw = marked(markdown, {
    ...options,
    mathRenderer: (math, displayMode) => escapeHTML(displayMode ? `$$${math}$$` : `$${math}$`)
  })
  const html = absoluteImageUrls(sanitize(raw, EXPORT_DOMPURIFY_CONFIG, false))
  const wrapper = document.createElement('div')
  wrapper.innerHTML = html
  const table = wrapper.querySelector('table')
  if (!table) return null
  table.setAttribute('border', '1')
  table.setAttribute('cellspacing', '0')
  table.setAttribute('cellpadding', '4')
  table.setAttribute('style', 'border-collapse:collapse')
  for (const cell of Array.from(table.querySelectorAll('th, td'))) {
    const align = (cell.getAttribute('style') || '').match(ALIGN)
    if (align) cell.setAttribute('align', align[1].toLowerCase())
    else if (cell.getAttribute('align')) cell.setAttribute('style', `text-align:${cell.getAttribute('align')}`)
  }
  return table.outerHTML
}
