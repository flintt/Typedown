// Review mode's revisions carried into an export that can hold them (Word: its own revisions, docs/todo.md "审阅模式").
// The Markdown is rendered as always, with marks around each revision's text - characters of the private use area, which
// no Markdown rule takes - and the deleted text put back where it was, escaped so it renders as written; the rendered
// page then has the marks turned into <ins> and <del> elements (data-rev, data-rev-source, data-rev-time), or for a
// paragraph added or deleted whole, its element marked data-rev-para. No HTML goes into the Markdown (it would break a
// code span or a code block), and the text exported without them is the document's, untouched.
import type { Revision } from './review'

const INS = '', DEL = '', SEP = '', CLOSE_INS = '', CLOSE_DEL = '', PARA = ''
const ANY_MARK = /[-]/

// The deleted text as it reads: Markdown's punctuation escaped, its lines one.
const literal = (text: string) => text.replace(/\n+/g, ' ').replace(/([\\`*_{}[\]()#+\-.!|~<>$=^&])/g, '\\$1')

const wholeLines = (text: string, start: number, end: number) =>
    (start === 0 || text[start - 1] === '\n') && (end === text.length || text[end - 1] === '\n')

/**
 * The text with the marks in: each revision by its index in `revisions`. A whole-line insertion or deletion is marked as
 * paragraphs (a mark at the end of each of its paragraphs' first line); the rest in the line.
 */
export function markedMarkdown(text: string, revisions: Revision[]): string {
    // A format's marks (Ctrl+B's **) are left as they are: marks around them would break the format. The text goes to
    // Word formatted as it is now (Word's own format revisions are for later).
    const sorted = revisions.map((r, i) => ({ r, i })).filter(({ r }) => !r.format).sort((a, b) => b.r.curStart - a.r.curStart || b.r.curEnd - a.r.curEnd || b.r.baseStart - a.r.baseStart)
    // (Blocks deleted together, a deletion each at one place: the later in the baseline put in first, so they read in order.)
    let out = text
    for (const { r, i } of sorted) {
        const tag = `${i}${SEP}`
        const before = out.slice(0, r.curStart), inserted = out.slice(r.curStart, r.curEnd), after = out.slice(r.curEnd)
        let ins = inserted
        if (inserted) {
            if (wholeLines(out, r.curStart, r.curEnd) && /\S/.test(inserted)) ins = paragraphs(inserted, PARA + 'i' + tag)
            else ins = INS + tag + inserted + CLOSE_INS
        }
        let del = ''
        if (r.oldText && /\S/.test(r.oldText)) {
            if (wholeLines(out, r.curStart, r.curStart) && r.oldText.endsWith('\n') && !inserted) del = paragraphs(r.oldText.replace(/\n*$/, '\n\n'), PARA + 'd' + tag)
            else del = DEL + tag + literal(r.oldText) + CLOSE_DEL
        }
        out = before + del + ins + after
    }
    return out
}

// Each paragraph of whole lines with the mark at the end of its first line (not at its start, where it would hide a
// list marker, a heading's #, a fence).
function paragraphs(text: string, mark: string): string {
    return text.split(/(\n{2,})/).map(part => {
        if (!/\S/.test(part) || /^\n+$/.test(part)) return part
        const nl = part.indexOf('\n')
        return nl < 0 ? part + mark : part.slice(0, nl) + mark + part.slice(nl)
    }).join('')
}

const BLOCK = new Set(['P', 'LI', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'PRE', 'TABLE', 'TR', 'DIV', 'FIGURE'])

/**
 * The rendered page's marks turned into elements. Inside code, formulas and diagrams the revision is not split out: the
 * text there is the document's (what was deleted left out).
 */
export function markRendered(body: HTMLElement, revisions: Revision[]) {
    const doc = body.ownerDocument
    const meta = (el: Element, r: Revision | undefined) => {
        el.setAttribute('data-rev', r?.id ?? '')
        el.setAttribute('data-rev-source', r?.source ?? '')
        el.setAttribute('data-rev-time', String(r?.time ?? ''))
    }
    // Marks in attributes (a link's address an edit went through): taken out.
    for (const el of Array.from(body.querySelectorAll('*')))
        for (const a of Array.from(el.attributes)) if (ANY_MARK.test(a.value)) el.setAttribute(a.name, a.value.replace(/[][di]?\d+|[]/g, ''))
    // A revision open across elements (an insertion with a bold word in it): every text node until it closes is in it.
    let open: { kind: 'ins' | 'del', r: Revision | undefined } | null = null
    const walker = doc.createTreeWalker(body, NodeFilter.SHOW_TEXT)
    const texts: Text[] = []
    for (let n = walker.nextNode(); n; n = walker.nextNode()) texts.push(n as Text)
    for (const node of texts) {
        const inCode = !!node.parentElement?.closest('pre, code, .math-tex, .math-tex-display, .katex, svg')
        const data = node.data
        if (!ANY_MARK.test(data) && !open) continue
        const parts: (string | { kind: 'ins' | 'del', r: Revision | undefined, text: string })[] = []
        let i = 0
        let buffer = ''
        const push = () => {
            if (!buffer) return
            parts.push(open ? { kind: open.kind, r: open.r, text: buffer } : buffer)
            buffer = ''
        }
        while (i < data.length) {
            const c = data[i]
            if (c === INS || c === DEL || c === PARA) {
                const kind = c === PARA ? data[i + 1] : null
                const from = i + 1 + (kind ? 1 : 0)
                const sep = data.indexOf(SEP, from)
                const index = sep > 0 ? parseInt(data.slice(from, sep), 10) : NaN
                const r = Number.isFinite(index) ? revisions[index] : undefined
                i = sep > 0 ? sep + 1 : i + 1
                if (c === PARA) {
                    const block = (node.parentElement?.closest('p, li, h1, h2, h3, h4, h5, h6, pre, blockquote, td, th') ?? node.parentElement) as Element | null
                    if (block && !block.hasAttribute('data-rev-para')) { block.setAttribute('data-rev-para', kind === 'd' ? 'del' : 'ins'); meta(block, r) }
                    continue
                }
                push()
                open = { kind: c === INS ? 'ins' : 'del', r }
                continue
            }
            if (c === CLOSE_INS || c === CLOSE_DEL) {
                push()
                open = null
                i++
                continue
            }
            buffer += c
            i++
        }
        push()
        const fragment = doc.createDocumentFragment()
        for (const p of parts) {
            if (typeof p === 'string') fragment.append(p)
            else if (inCode) { if (p.kind === 'ins') fragment.append(p.text) }
            else {
                const el = doc.createElement(p.kind)
                meta(el, p.r)
                el.textContent = p.text
                fragment.append(el)
            }
        }
        node.replaceWith(fragment)
    }
    // Only blocks are marked whole.
    for (const el of Array.from(body.querySelectorAll('[data-rev-para]'))) if (!BLOCK.has(el.tagName)) el.removeAttribute('data-rev-para')
}
