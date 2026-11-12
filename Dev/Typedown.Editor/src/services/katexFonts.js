// The page's KaTeX fonts, as its own @font-face rules have them: each face's family, style, weight and its sources'
// absolute addresses (the bundle's hashed names), in the order the rule gives them. A formula copied as a picture embeds
// the first source of each (services/blockCopy); an export points the raw KaTeX stylesheet at them (services/exportHtml).
// Each walked the style sheets itself (the modularity audit's M14); what each makes of the list stays its own. A sheet
// that cannot be read is passed over.
export const discoverKatexFontFaces = () => {
  const faces = []
  for (const sheet of Array.from(document.styleSheets)) {
    let rules
    try { rules = sheet.cssRules } catch (e) { continue }
    for (const rule of Array.from(rules)) {
      if (!(rule instanceof CSSFontFaceRule)) continue
      const family = (rule.style.getPropertyValue('font-family') || '').replace(/["']/g, '').trim()
      if (!family.startsWith('KaTeX_')) continue
      const sources = []
      for (const m of (rule.style.getPropertyValue('src') || '').matchAll(/url\(["']?([^"')]+)["']?\)/g))
        sources.push(new URL(m[1], sheet.href || document.baseURI).href)
      if (!sources.length) continue
      faces.push({ family, style: rule.style.getPropertyValue('font-style') || 'normal', weight: rule.style.getPropertyValue('font-weight') || 'normal', sources })
    }
  }
  return faces
}
