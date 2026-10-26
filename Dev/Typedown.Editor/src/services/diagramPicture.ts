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


