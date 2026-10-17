// The first screen of a long document, shown while the editor builds it (docs/todo.md, "启动恢复大文档的空白").
//
// The editor builds a whole document in one go: a 300,000-character one keeps the page busy for a second or two, and
// the reader looked at an empty page (at start-up, restoring the tabs, and opening one). The page now first draws the
// part the reader will see - from the block the caret is in, a screenful or two - as plain HTML in the editor's own
// styles, lets the browser paint it, and only then gives the document to the editor. The preview is only something to
// look at: it takes no clicks and no keys (the keys typed meanwhile wait as they did, services/inputHold.ts), and it
// goes once the load is reported (FileLoaded) - the editor then shows the same text where it puts the caret.
//
// Only the common Markdown is drawn as such: headings, paragraphs, lists, quotes, fences, rules, emphasis, code and
// links. Anything else shows as its text: a table as its rows, raw HTML without its tags, a picture not at all.

/**
 * Documents this long get a preview: shorter ones are built before the reader can tell. A reader saw a blank at
 * start-up with 20,000 characters; the preview itself is a screen's worth whatever the length, and the one frame it
 * waits for costs nothing beside the build it covers.
 */
export const PREVIEW_FROM_CHARS = 10000

const escape = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/** One line of text with its inline Markdown: code, pictures (dropped), links (their text), strong and emphasis. */
export const inlineHtml = (line: string): string => {
    const parts = line.split(/(`+[^`]*`+)/)
    return parts.map((part, i) => {
        if (i % 2 === 1) return `<code>${escape(part.replace(/^`+|`+$/g, ''))}</code>`
        let s = part
            .replace(/<\/?[A-Za-z][^>]*>/g, '')
            .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
            .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
        s = escape(s)
        return s
            .replace(/(\*\*|__)(?=\S)(.+?)(?<=\S)\1/g, '<strong>$2</strong>')
            .replace(/(\*|_)(?=\S)(.+?)(?<=\S)\1/g, '<em>$2</em>')
            .replace(/~~(?=\S)(.+?)(?<=\S)~~/g, '<del>$1</del>')
    }).join('')
}

const FENCE = /^ {0,3}(`{3,}|~{3,})/

/**
 * Where the preview starts: the first line of the block the given line is in. Fences are followed from the top (a line
 * inside one starts at its opening line), a block otherwise starts after the blank line above it.
 */
export const previewStart = (lines: string[], line: number): number => {
    const target = Math.max(0, Math.min(line, lines.length - 1))
    let fenceOpen = -1
    let fence = ''
    for (let i = 0; i <= target; i++) {
        const m = FENCE.exec(lines[i])
        if (!m) continue
        if (fenceOpen < 0) { fenceOpen = i; fence = m[1][0] }
        else if (m[1][0] === fence) fenceOpen = -1
    }
    if (fenceOpen >= 0) return fenceOpen
    let i = target
    while (i > 0 && lines[i - 1].trim() !== '') i--
    return i
}

/**
 * Where the page was: the block at the top of the window, by the start of its text, how far down the window its top
 * was (negative: scrolled past), and how far through the document the page was (a fraction, where to look first).
 * Read from the page as it scrolls (services/scrollbar), kept by the host with the file's scroll offset, and given back
 * with its next load: the preview puts that block there, and so does the editor once the document is built - the two
 * meet, where the editor's own scroll offset and a preview from the caret did not.
 */
export interface ScrollAnchor { text: string, top: number, at: number }

/** Text as both the page's blocks and the Markdown source can be compared by: no Markdown marks, spaces, or case. */
export const anchorKey = (s: string): string =>
    s.replace(/^\s*(?:#{1,6}|>|[-*+]|\d+[.)])\s+/, '').replace(/[#>*_`~[\]()!|\\]/g, '').replace(/\s+/g, ' ').trim().toLowerCase()

/** The line of the source the anchor's block starts at, looked for from where it was through the document; or -1. */
export const findAnchorLine = (lines: string[], anchor: ScrollAnchor | null | undefined): number => {
    const key = anchorKey(anchor?.text ?? '').slice(0, 40)
    if (key.length < 4) return -1
    const matches = (i: number) => {
        const line = anchorKey(lines[i])
        if (line.length < 2) return false
        const n = Math.min(key.length, line.length)
        return n >= Math.min(4, key.length) && key.slice(0, n) === line.slice(0, n)
    }
    const from = Math.max(0, Math.min(lines.length - 1, Math.round((anchor?.at ?? 0) * lines.length)))
    for (let d = 0; d < lines.length; d++) {
        if (from + d < lines.length && matches(from + d)) return from + d
        if (d > 0 && from - d >= 0 && matches(from - d)) return from - d
    }
    return -1
}

/**
 * The preview's HTML: the document's lines from the block `line` is in, up to about `maxChars` of text. Front matter
 * at the very top is left out, as the editor shows it apart. The block holding `markLine` is marked (data-anchor).
 */
export const previewHtml = (text: string, line = 0, maxChars = 12000, markLine = -1): string => {
    const lines = text.split(/\r?\n/)
    let start = previewStart(lines, line)
    if (start === 0 && lines[0] === '---') {
        const end = lines.indexOf('---', 1)
        if (end > 0) start = end + 1
    }
    const out: string[] = []
    let paragraph: string[] = []
    let paragraphFrom = 0
    let list: { tag: string, items: string[], from: number[] } | null = null
    let chars = 0
    let marked = false
    // A block whose lines take in markLine carries the mark on its element (a list's on the item).
    const holds = (from: number, to: number) => !marked && markLine >= from && markLine <= to && (marked = true)
    const mark = (html: string, from: number, to: number) => holds(from, to) ? html.replace(/^<(\w+)/, '<$1 data-anchor') : html
    const push = (html: string, from: number, to: number) => out.push(mark(html, from, to))
    const flushParagraph = (to: number) => {
        if (paragraph.length) push(`<p>${paragraph.map(inlineHtml).join('<br>')}</p>`, paragraphFrom, to)
        paragraph = []
    }
    const flushList = (to: number) => {
        if (list) {
            const l = list
            out.push(`<${l.tag}>${l.items.map((item, k) => mark(`<li>${inlineHtml(item)}</li>`, l.from[k], k + 1 < l.from.length ? l.from[k + 1] - 1 : to)).join('')}</${l.tag}>`)
        }
        list = null
    }
    const flush = (to: number) => { flushParagraph(to); flushList(to) }
    let i = start
    for (; i < lines.length && (chars < maxChars || i <= markLine); i++) {
        const raw = lines[i]
        chars += raw.length + 1
        const fence = FENCE.exec(raw)
        if (fence) {
            flush(i - 1)
            const code: string[] = []
            let j = i + 1
            const closes = (l: string) => { const m = FENCE.exec(l); return !!m && m[1][0] === fence[1][0] && m[1].length >= fence[1].length }
            while (j < lines.length && !closes(lines[j])) {
                code.push(lines[j])
                chars += lines[j].length + 1
                if (chars >= maxChars) break
                j++
            }
            push(`<pre><code>${escape(code.join('\n'))}</code></pre>`, i, j)
            i = j
            continue
        }
        if (raw.trim() === '') { flush(i - 1); continue }
        const heading = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/.exec(raw)
        if (heading) {
            flush(i - 1)
            push(`<h${heading[1].length}>${inlineHtml(heading[2])}</h${heading[1].length}>`, i, i)
            continue
        }
        if (/^ {0,3}([-*_])(\s*\1){2,}\s*$/.test(raw)) { flush(i - 1); push('<hr>', i, i); continue }
        const quote = /^ {0,3}>\s?(.*)$/.exec(raw)
        if (quote) { flush(i - 1); push(`<blockquote><p>${inlineHtml(quote[1])}</p></blockquote>`, i, i); continue }
        const item = /^\s*(?:([-*+])|(\d+)[.)])\s+(?:\[[ xX]\]\s+)?(.*)$/.exec(raw)
        if (item) {
            flushParagraph(i - 1)
            const tag = item[1] ? 'ul' : 'ol'
            if (!list || list.tag !== tag) { flushList(i - 1); list = { tag, items: [], from: [] } }
            list.items.push(item[3])
            list.from.push(i)
            continue
        }
        // A setext underline turns the paragraph above into a heading.
        if (paragraph.length && /^ {0,3}(=+|-+)\s*$/.test(raw)) {
            const level = raw.trim()[0] === '=' ? 1 : 2
            const title = paragraph.map(inlineHtml).join(' ')
            paragraph = []
            flushList(i - 1)
            push(`<h${level}>${title}</h${level}>`, paragraphFrom, i)
            continue
        }
        flushList(i - 1)
        if (!paragraph.length) paragraphFrom = i
        paragraph.push(raw.trim())
    }
    flush(i - 1)
    return out.join('\n')
}

const PREVIEW_ID = 'td-load-preview'
let safety: number | undefined

/**
 * Shows the preview of this text over the page. With where the page was (its scroll anchor), that block at the height
 * it was, as the editor will put it; without, from the block the caret's line is in, at the top. Returns false, showing
 * nothing, for a short text.
 */
export function showLoadPreview(text: string, cursor?: any, font?: { fontSize?: number, lineHeight?: number, fontFamily?: string }, anchor?: ScrollAnchor | null): boolean {
    if (typeof text !== 'string' || text.length < PREVIEW_FROM_CHARS) { hideLoadPreview(); return false }
    const anchorLine = findAnchorLine(text.split(/\r?\n/), anchor)
    // Enough above the anchor's block to fill the window over it.
    const line = anchorLine >= 0 ? Math.max(0, anchorLine - 60) : typeof cursor?.anchor?.line === 'number' ? cursor.anchor.line : 0
    let host = document.getElementById(PREVIEW_ID)
    if (!host) {
        host = document.createElement('div')
        host.id = PREVIEW_ID
        host.setAttribute('aria-hidden', 'true')
        document.body.appendChild(host)
    }
    host.innerHTML = `<div class="td-load-preview-page">${previewHtml(text, line, 16000, anchorLine)}</div>`
    // The editor's own font where it is on the page (a document shown already), the settings' otherwise (start-up).
    const page = host.firstElementChild as HTMLElement
    const editor = document.getElementById('ag-editor-id')
    if (editor) {
        const style = getComputedStyle(editor)
        page.style.fontSize = style.fontSize
        page.style.lineHeight = style.lineHeight
        page.style.fontFamily = style.fontFamily
    } else {
        if (font?.fontSize) page.style.fontSize = `${font.fontSize}px`
        if (font?.lineHeight) page.style.lineHeight = String(font.lineHeight)
        if (font?.fontFamily) page.style.fontFamily = `${font.fontFamily}, "Open Sans", "Segoe UI", sans-serif`
    }
    host.scrollTop = 0
    // The anchor's block where it was in the window.
    const marked = page.querySelector('[data-anchor]')
    if (marked && anchor) page.style.transform = `translateY(${Math.round(anchor.top - marked.getBoundingClientRect().top)}px)`
    ;(window as any).__typedownLoadPreview = { shownAt: performance.now(), line, anchorLine }
    // A load that is never reported (the page torn down, an error) leaves no preview over the page for good.
    clearTimeout(safety)
    safety = window.setTimeout(hideLoadPreview, 20000)
    return true
}

export function hideLoadPreview() {
    clearTimeout(safety)
    document.getElementById(PREVIEW_ID)?.remove()
}

export const isLoadPreviewShown = () => !!document.getElementById(PREVIEW_ID)

/**
 * Runs `then` once the browser has painted what is on the page now: the frame after the next one has begun (a frame's
 * own callbacks run before its paint, so the work would have held the paint back).
 */
export function afterPaint(then: () => void): () => void {
    let timer: number | undefined
    const frame = requestAnimationFrame(() => { timer = window.setTimeout(then, 0) })
    return () => { cancelAnimationFrame(frame); clearTimeout(timer) }
}

/**
 * The page's scroll anchor now: the editor's block at the top of the window (a few points down, past a block's margin),
 * the start of its text and where its top is; null where the editor shows nothing there.
 */
export function readScrollAnchor(): ScrollAnchor | null {
    const editor = document.getElementById('ag-editor-id')
    if (!editor) return null
    const box = editor.getBoundingClientRect()
    const x = box.left + Math.min(box.width / 2, 300)
    for (const y of [2, 16, 40, 80, 140]) {
        const block = document.elementFromPoint(x, y)?.closest('.ag-paragraph') as HTMLElement | null
        if (!block || !editor.contains(block)) continue
        const text = (block.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 80)
        if (anchorKey(text).length < 4) continue
        const height = document.documentElement.scrollHeight
        return { text, top: Math.round(block.getBoundingClientRect().top), at: height > 0 ? Math.round(window.scrollY / height * 1000) / 1000 : 0 }
    }
    return null
}

/**
 * The scroll offset that puts the anchor's block of the built document where it was in the window, or null (no such
 * block found). The blocks are looked through from where the page was through the document.
 */
export function scrollForAnchor(anchor: ScrollAnchor | null | undefined): number | null {
    const key = anchorKey(anchor?.text ?? '').slice(0, 40)
    const editor = document.getElementById('ag-editor-id')
    if (!anchor || key.length < 4 || !editor) return null
    const blocks = editor.querySelectorAll<HTMLElement>('.ag-paragraph')
    const matches = (k: number) => {
        const text = anchorKey(blocks[k].textContent ?? '')
        const n = Math.min(key.length, text.length)
        return n >= 4 && key.slice(0, n) === text.slice(0, n)
    }
    const from = Math.max(0, Math.min(blocks.length - 1, Math.round(anchor.at * blocks.length)))
    for (let d = 0; d < blocks.length; d++) {
        const k = from + d < blocks.length && matches(from + d) ? from + d : d > 0 && from - d >= 0 && matches(from - d) ? from - d : -1
        if (k >= 0) return Math.max(0, window.scrollY + blocks[k].getBoundingClientRect().top - anchor.top)
    }
    return null
}
