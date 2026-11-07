// Blocks deleted whole, drawn in the visual editor where they were (services/reviewMap.ts ghostsOf): a block of their
// own kind - a paragraph, a heading at its level, a list item with its marker among its list's items, a table row among
// the rows, code with its lines, a card for a formula or a diagram - struck through, faded red, never inside another
// block or in its style (the user, 2026-11-07: a deleted paragraph took the next heading's size, a deleted row went into
// the next row's first cell, a strip put in a formula broke it).
//
// They are elements of the page beside Muya's, not of its blocks: Muya draws its blocks again from its model at an edit
// and takes away anything it did not draw, so they are put back whenever they are found gone (a MutationObserver).

import type { Ghost } from './reviewMap'

export interface GhostLabels {
    deletedFormula?: string
    deletedDiagram?: string
}

const GHOST = 'ag-review-ghost'

function element(g: Ghost, labels: GhostLabels): HTMLElement {
    const li = g.kind === 'li', row = g.kind === 'row'
    const el = document.createElement(li ? 'li' : row ? 'tr' : 'div')
    el.className = `${GHOST} ${GHOST}-${g.kind}`
    el.setAttribute('contenteditable', 'false')
    el.setAttribute('data-rid', g.rid)
    if (g.source) el.setAttribute('data-review-source', g.source)
    const text = (tag: string, t: string, cls?: string) => {
        const e = document.createElement(tag)
        if (cls) e.className = cls
        e.textContent = t
        return e
    }
    switch (g.kind) {
        case 'li':
            el.setAttribute('data-marker', g.marker ?? '•')
            if (g.depth) el.classList.add(`${GHOST}-nested`)
            el.append(text('span', g.marker ?? '•', `${GHOST}-marker`), text('span', g.lines.join('\n'), `${GHOST}-text`))
            break
        case 'row':
            for (const cell of g.cells?.[0] ?? []) el.append(text('td', cell))
            break
        case 'table': {
            const table = document.createElement('table')
            ;(g.cells ?? []).forEach((cells, i) => {
                const tr = document.createElement('tr')
                for (const cell of cells) tr.append(text(i === 0 ? 'th' : 'td', cell))
                table.append(tr)
            })
            el.append(table)
            break
        }
        case 'code': case 'html': case 'front':
            el.append(text('pre', g.lines.join('\n')))
            break
        case 'math': case 'diagram':
            el.append(text('div', g.kind === 'math' ? labels.deletedFormula || 'Formula deleted' : labels.deletedDiagram || 'Diagram deleted', `${GHOST}-card`))
            if (g.lines.length) el.append(text('pre', g.lines.join('\n')))
            break
        case 'hr':
            el.append(document.createElement('hr'))
            break
        case 'heading':
            el.classList.add(`${GHOST}-h${g.level ?? 1}`)
            el.append(text('span', g.lines.join('\n'), `${GHOST}-text`))
            break
        default:
            el.append(text('span', g.lines.join('\n'), `${GHOST}-text`))
    }
    return el
}

/**
 * The name of the program a block's revisions are from, in the page's left margin beside the block - not against its
 * text or over a list's marker (the user's 3.16, 2026-11-07: typeleafctl's tag was too close to the text). Where the
 * margin is too narrow for a name, a dot, the name on hover. Elements of their own beside the editor (not in Muya's
 * blocks), placed again whenever the page's blocks move.
 */
export function tagLayer(root: HTMLElement) {
    const host = root.parentElement ?? root
    if (getComputedStyle(host).position === 'static') host.style.position = 'relative'
    const layer = document.createElement('div')
    layer.className = 'td-review-tags'
    layer.setAttribute('aria-hidden', 'true')
    host.append(layer)
    let on = false
    let frame = 0
    const place = () => {
        frame = 0
        layer.textContent = ''
        if (!on) return
        const base = host.getBoundingClientRect()
        // Where the text starts: the top-level blocks' left (a list's items further in still get their tag there).
        // (A top-level paragraph's or heading's text: the block itself starts further left, where Muya draws its icons.)
        // (A paragraph's or a heading's text not in a list, a quotation or a table: the block itself starts further left,
        // where Muya draws its icons.)
        const lefts = Array.from(root.querySelectorAll<HTMLElement>('.ag-paragraph-content, .ag-atx-line'))
            .filter(e => !e.closest('li, blockquote, td, th, .ag-review-ghost')).slice(0, 20).map(e => e.getBoundingClientRect().left).filter(x => x > 0)
        const textLeft = lefts.length ? Math.min(...lefts) : root.getBoundingClientRect().left + parseFloat(getComputedStyle(root).paddingLeft || '0')
        const room = textLeft - Math.max(0, base.left)
        const dot = room < 100
        let lastTop = -Infinity
        for (const el of Array.from(root.querySelectorAll<HTMLElement>('.ag-review-tagged[data-review-source], .ag-review-ghost[data-review-source]'))) {
            const name = el.getAttribute('data-review-source') ?? ''
            const r = el.getBoundingClientRect()
            if (!name || !r.height) continue
            const top = r.top - base.top
            // One a line: two blocks on one line (a table's cells) get one tag.
            if (Math.abs(top - lastTop) < 4) continue
            lastTop = top
            const tag = document.createElement('div')
            tag.className = dot ? 'td-review-tag td-review-tag-dot' : 'td-review-tag'
            tag.textContent = dot ? '' : name
            tag.title = name
            tag.style.top = `${Math.round(top)}px`
            // A name right-aligned a little left of the text; a dot at the window's edge, far from the text (beside it, it read
            // as a list's bullet).
            if (dot) tag.style.left = `${Math.round(Math.max(0, -base.left) + 4)}px`
            else tag.style.right = `${Math.round(base.right - textLeft + 12)}px`
            layer.append(tag)
        }
    }
    const later = () => { if (on && !frame) frame = requestAnimationFrame(place) }
    const observer = new MutationObserver(later)
    observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-review-source', 'class'] })
    const resize = new ResizeObserver(later)
    resize.observe(root)
    window.addEventListener('resize', later)
    return {
        draw() { on = true; later() },
        clear() { on = false; layer.textContent = '' },
        dispose() { observer.disconnect(); resize.disconnect(); window.removeEventListener('resize', later); layer.remove() },
    }
}

/** Draws the ghosts in Muya's root (none: takes them away); they come back when an edit takes them away. */
export function ghostLayer(root: HTMLElement) {
    let ghosts: Ghost[] = []
    let labels: GhostLabels = {}
    let drawn: HTMLElement[] = []
    let syncing = false
    const place = () => {
        syncing = true
        try {
            for (const e of drawn) e.remove()
            root.querySelectorAll(`.${GHOST}`).forEach(e => e.remove())
            drawn = []
            const lastAfter = new Map<string, HTMLElement>()
            for (const g of ghosts) {
                const anchor = root.querySelector<HTMLElement>(`[id="${g.key}"]`)
                if (!anchor) continue
                const el = element(g, labels)
                if (g.where === 'before') anchor.before(el)
                else {
                    // Several after one block: in their order.
                    const after = lastAfter.get(g.key) ?? anchor
                    after.after(el)
                    lastAfter.set(g.key, el)
                }
                drawn.push(el)
            }
        } finally {
            observer.takeRecords()
            syncing = false
        }
    }
    let queued = false
    const observer = new MutationObserver(() => {
        if (syncing || queued || !ghosts.length) return
        if (drawn.length && drawn.every(e => e.isConnected)) return
        queued = true
        // After Muya is done drawing (a redraw comes in several patches).
        Promise.resolve().then(() => { queued = false; place() })
    })
    observer.observe(root, { childList: true, subtree: true })
    return {
        draw(next: Ghost[], nextLabels: GhostLabels) {
            ghosts = next
            labels = nextLabels
            place()
        },
        clear() {
            ghosts = []
            place()
        },
        dispose() {
            observer.disconnect()
            for (const e of drawn) e.remove()
        },
    }
}
