import ExportHtml from './exportHtml'

const SVG = 'http://www.w3.org/2000/svg'

type Run = { text: string, bold: boolean, mono: boolean }

// The lines a label's HTML shows, each in runs of bold or code text: a <br> or a block starts a new line. One empty line
// between two is kept (an event model's name, a gap, its data); more, or at either end, are not.
const labelLines = (element: Element): Run[][] => {
    const lines: Run[][] = [[]]
    const blank = (line: Run[]) => !line.some(run => run.text.trim())
    const walk = (node: Node, bold: boolean, mono: boolean) => {
        if (node.nodeType === Node.TEXT_NODE) {
            const text = (node.textContent ?? '').replace(/\s+/g, ' ')
            if (text) lines[lines.length - 1].push({ text, bold, mono })
            return
        }
        if (!(node instanceof Element)) return
        const name = node.localName
        if (name === 'br') { lines.push([]); return }
        const block = /^(div|p|li|h[1-6]|tr|pre)$/.test(name) || (name === 'code' && /display:\s*block/.test(node.getAttribute('style') ?? ''))
        if (block && !blank(lines[lines.length - 1])) lines.push([])
        const isBold = bold || /^(b|strong|th|h[1-6])$/.test(name) || /font-weight:\s*(bold|[6-9]00)/.test(node.getAttribute('style') ?? '')
        const isMono = mono || /^(code|pre|kbd|samp)$/.test(name)
        node.childNodes.forEach(child => walk(child, isBold, isMono))
        if (block && !blank(lines[lines.length - 1])) lines.push([])
    }
    walk(element, false, false)
    const out: Run[][] = []
    for (const line of lines) {
        const trimmed = line.map(run => ({ ...run })).filter(run => run.text)
        if (trimmed.length) {
            trimmed[0].text = trimmed[0].text.trimStart()
            trimmed[trimmed.length - 1].text = trimmed[trimmed.length - 1].text.trimEnd()
        }
        if (blank(trimmed)) {
            if (out.length && out[out.length - 1].length) out.push([])
        } else {
            out.push(trimmed)
        }
    }
    while (out.length && !out[out.length - 1].length) out.pop()
    return out
}

// A line cut at spaces (or anywhere, for text without them: Chinese) to fit the width.
const wrapLine = (line: string, width: number, measure: (text: string) => number): string[] => {
    if (width <= 0 || measure(line) <= width) return [line]
    const out: string[] = []
    let current = ''
    for (const piece of line.match(/\S+\s*|\s+/g) ?? [line]) {
        for (const unit of measure(piece) > width ? Array.from(piece) : [piece]) {
            if (current && measure(current + unit) > width) {
                out.push(current.trim())
                current = unit.trimStart()
            } else {
                current += unit
            }
        }
    }
    if (current.trim()) out.push(current.trim())
    return out
}

const MONO = 'Consolas, "Courier New", monospace'

// The labels Mermaid writes as HTML inside the drawing (<foreignObject>; a journey's, an event model's, a Venn's,
// whatever htmlLabels does not reach) as SVG text in the same box: centred, a line of one run wrapped to the box's
// width, in the label's font, size, weight and colour, bold and code parts kept. A canvas a drawing with HTML in it
// was drawn on cannot be read back (the browser's rule), so such a diagram never became a picture: Word showed
// "diagram not exported", copying it gave no picture.
const labelsAsText = (original: SVGSVGElement, copy: SVGSVGElement) => {
    const originals = Array.from(original.querySelectorAll('foreignObject'))
    const copies = Array.from(copy.querySelectorAll('foreignObject'))
    const context = document.createElement('canvas').getContext('2d')!
    copies.forEach((object, index) => {
        const x = parseFloat(object.getAttribute('x') ?? '0') || 0
        const y = parseFloat(object.getAttribute('y') ?? '0') || 0
        const width = parseFloat(object.getAttribute('width') ?? '0') || 0
        const height = parseFloat(object.getAttribute('height') ?? '0') || 0
        const source = originals[index] ?? object
        const styled = Array.from(source.querySelectorAll('*')).find(e => Array.from(e.childNodes).some(n => n.nodeType === Node.TEXT_NODE && n.textContent?.trim())) ?? source
        const style = styled.isConnected ? getComputedStyle(styled) : null
        const size = parseFloat(style?.fontSize ?? '') || 14
        const family = style?.fontFamily || '"trebuchet ms", verdana, arial, sans-serif'
        const weight = style?.fontWeight || 'normal'
        const color = style?.color || '#333333'
        const align = style?.textAlign
        const lines = labelLines(object).flatMap(line => {
            if (line.length !== 1) return [line]
            const run = line[0]
            context.font = `${run.bold ? 'bold' : weight} ${run.mono ? size * 0.9 : size}px ${run.mono ? MONO : family}`
            return wrapLine(run.text, width, text => context.measureText(text).width).map(text => [{ ...run, text }])
        })
        const replacement = document.createElementNS(SVG, 'text')
        if (lines.length) {
            const lineHeight = size * 1.25
            const anchor = align === 'left' || align === 'start' ? 'start' : align === 'right' || align === 'end' ? 'end' : 'middle'
            const at = anchor === 'start' ? x : anchor === 'end' ? x + width : x + width / 2
            replacement.setAttribute('text-anchor', anchor)
            replacement.setAttribute('dominant-baseline', 'central')
            replacement.setAttribute('font-size', String(size))
            replacement.setAttribute('font-family', family)
            replacement.setAttribute('font-weight', weight)
            replacement.setAttribute('fill', color)
            const top = y + height / 2 - (lines.length - 1) * lineHeight / 2
            lines.forEach((line, i) => {
                line.forEach((run, j) => {
                    const span = document.createElementNS(SVG, 'tspan')
                    // The first run of a line starts it; the others follow on.
                    if (j === 0) {
                        span.setAttribute('x', String(at))
                        span.setAttribute('y', String(top + i * lineHeight))
                    }
                    if (run.bold) span.setAttribute('font-weight', 'bold')
                    if (run.mono) {
                        span.setAttribute('font-family', MONO)
                        span.setAttribute('font-size', String(size * 0.9))
                    }
                    span.textContent = run.text
                    replacement.appendChild(span)
                })
            })
        }
        object.replaceWith(replacement)
    })
}

// Each SVG drawing in the element (a diagram) replaced by a PNG of it, drawn at twice its size on white - less for a big
// one, kept within 8000 pixels a side and 16 million in all: a canvas past the browser's limits gave an empty picture. The
// drawing is drawn on its own, with only its own styles: the page's (a dark theme) do not reach it. One that cannot be
// drawn (a canvas the browser will not read back) is left as it is. Its text (alt) is the diagram's kind and the start of
// its source, when the export marked its block with them.
export const diagramsToPictures = async (root: HTMLElement) => {
    for (const svg of Array.from(root.querySelectorAll('svg'))) {
        if (svg.parentElement?.closest('svg')) continue
        try {
            const box = svg.viewBox?.baseVal
            const width = box && box.width > 0 ? box.width : parseFloat(svg.getAttribute('width') ?? '')
            const height = box && box.height > 0 ? box.height : parseFloat(svg.getAttribute('height') ?? '')
            if (!(width > 0 && height > 0)) continue
            const copy = svg.cloneNode(true) as SVGSVGElement
            copy.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
            copy.setAttribute('width', String(width))
            copy.setAttribute('height', String(height))
            copy.style.maxWidth = ''
            // Mermaid's live stylesheet centres a mind-map root label, but that CSS is
            // absent when this SVG is loaded as an image for rasterization. Give its text
            // an explicit anchor so the white label stays centred in the blue circle.
            if (copy.classList.contains('mindmapDiagram')) {
                copy.querySelector('.section-root g.label text')?.setAttribute('text-anchor', 'middle')
            }
            labelsAsText(svg, copy)
            const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(copy)], { type: 'image/svg+xml' }))
            try {
                const image = new Image()
                await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; image.src = url })
                const scale = Math.min(2, 8000 / width, 8000 / height, Math.sqrt(16e6 / (width * height)))
                const canvas = document.createElement('canvas')
                canvas.width = Math.ceil(width * scale)
                canvas.height = Math.ceil(height * scale)
                const context = canvas.getContext('2d')!
                context.fillStyle = '#ffffff'
                context.fillRect(0, 0, canvas.width, canvas.height)
                context.drawImage(image, 0, 0, canvas.width, canvas.height)
                const picture = document.createElement('img')
                picture.setAttribute('src', canvas.toDataURL('image/png'))
                picture.setAttribute('width', String(Math.round(width)))
                const source = svg.closest<HTMLElement>('[data-diagram-source]')?.dataset.diagramSource
                picture.setAttribute('alt', source ? source.slice(0, 250) : 'diagram')
                svg.replaceWith(picture)
            } finally {
                URL.revokeObjectURL(url)
            }
        } catch (e) {
            console.log('a diagram could not be made a picture', e)
        }
    }
    for (const marked of Array.from(root.querySelectorAll<HTMLElement>('[data-diagram-source]'))) delete marked.dataset.diagramSource
}


const blobBase64 = (blob: Blob) => new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result).replace(/^data:[^,]*,/, ''))
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
})

/**
 * One diagram block (its Markdown, the fenced source) as a PNG, base64 without the data: prefix - the picture a format
 * that takes no SVG gets (RenderXhtml with diagramsAsPictures): drawn again in the light theme, on white, at twice its
 * size, whatever theme the editor shows it in. A PlantUML diagram is the server's PNG. Null when no picture can be
 * made: a diagram that cannot be drawn, PlantUML drawing off or its server out of reach.
 */
export const diagramPicture = async (markdown: string, options: any): Promise<string | null> => {
    const html = await new ExportHtml(markdown, { ...options, diagramsAsPictures: true }).renderHtml('')
    const body = new DOMParser().parseFromString(`<!DOCTYPE html><html><body>${html}</body></html>`, 'text/html').body
    await diagramsToPictures(body)
    const src = body.querySelector('img')?.getAttribute('src') ?? ''
    if (src.startsWith('data:image/png;base64,')) return src.slice('data:image/png;base64,'.length)
    if (/^https?:/.test(src)) {
        try {
            const response = await fetch(src)
            const blob = response.ok ? await response.blob() : null
            if (blob && blob.type === 'image/png') return await blobBase64(blob)
        } catch (e) {
            console.log('a diagram picture could not be fetched', e)
        }
    }
    return null
}
