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
