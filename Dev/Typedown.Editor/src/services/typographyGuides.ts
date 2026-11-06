import transport from './transport'

// The typography designer open (an edition's, beside the document): the caret's block - the one the small panel beside
// it is for - shows its spacing as the browser's developer tools do: the space before and after it and its indent
// tinted, the text's width marked, each with its value in em and px. Fixed there until the caret goes to another block
// (a click, the arrow keys), not following the pointer: the guides and the panel were often on two blocks, and on its
// way to a label the pointer took the guides to another (the user, 2026-11-06). Never in the way: only the labels take
// a click, typing hides them. The host turns it on and off (TypographyGuides { on }).

const BLOCKS = 'p, h1, h2, h3, h4, h5, h6, li, blockquote, pre, figure'

export interface GuideMeasure {
    kind: string
    before: number
    after: number
    indent: number
    firstLine: number
    width: number
    fontSize: number
}

let on = false
let layer: HTMLDivElement | null = null
let target: HTMLElement | null = null
let frame = 0
let measured: GuideMeasure | null = null

const px = (value: string) => parseFloat(value) || 0
const label = (value: number, fontSize: number) => `${+(value / fontSize).toFixed(2)}em · ${Math.round(value)}px`

/** The block a guide is for: the innermost paragraph-like block under the point, inside the visual editor. */
export const blockAt = (element: Element | null): HTMLElement | null => {
    const editor = document.getElementById('ag-editor-id')
    if (!editor || !element || !editor.contains(element)) return null
    const block = element.closest(BLOCKS) as HTMLElement | null
    return block && editor.contains(block) && block !== editor ? fieldBlock(block, editor) : null
}

/**
 * The element the designer's fields set for a block, which the guides measure: code's figure (block spacing is
 * around it, not its pre), a quote whole (its spacing and indent), a list item (the spacing between items) - a
 * paragraph's own inside them had values no field changes (the user, 2026-11-06).
 */
const fieldBlock = (block: HTMLElement, editor: HTMLElement): HTMLElement => {
    const code = block.closest('pre')
    if (code) {
        const figure = code.closest('figure') as HTMLElement | null
        return figure && editor.contains(figure) ? figure : code as HTMLElement
    }
    if (/^H[1-6]$/.test(block.tagName)) return block
    const quote = block.closest('#ag-editor-id blockquote') as HTMLElement | null
    if (quote) return quote
    const item = block.closest('#ag-editor-id li') as HTMLElement | null
    return item ?? block
}

/** What the guides show for a block: its space before and after, indent (the first line's too) and the text's width. */
export const measure = (block: HTMLElement): GuideMeasure => {
    const style = getComputedStyle(block)
    const editor = document.getElementById('ag-editor-id')!
    const page = getComputedStyle(editor)
    // The indent as it is on the page: from the text's left edge to where this block's text begins - a list's
    // indent is its list's padding, a quote's its own, a nested item's all of them.
    const textLeft = editor.getBoundingClientRect().left + px(page.paddingLeft)
    // A quote's or a list item's text starts inside its padding (Quote indent, List indent); code's and a table's padding
    // is their own inside, not an indent - shown as one, it was a value no field changes.
    const own = /^(PRE|FIGURE|TABLE)$/.test(block.tagName)
    const contentLeft = block.getBoundingClientRect().left + (own ? 0 : px(style.borderLeftWidth) + px(style.paddingLeft))
    return {
        kind: block.tagName.toLowerCase(),
        before: px(style.marginTop),
        after: px(style.marginBottom),
        indent: Math.max(0, Math.round(contentLeft - textLeft)),
        firstLine: px(style.textIndent),
        // The page's width as the designer's Page width sets it: the whole page, its margins each side with it (the
        // text's own was 100 px less, and the two read as one value disagreeing - the user, 2026-11-06).
        width: editor.offsetWidth,
        fontSize: px(style.fontSize) || 16,
    }
}

const band = (left: number, top: number, width: number, height: number, colour: string, text?: string, field?: string | null) => {
    const box = document.createElement('div')
    box.style.cssText = `position:fixed;left:${left}px;top:${top}px;width:${Math.max(0, width)}px;height:${Math.max(0, height)}px;background:${colour};`
    if (text) {
        const tag = document.createElement('span')
        tag.textContent = text
        tag.style.cssText = 'position:absolute;right:2px;top:50%;transform:translateY(-50%);font:11px/1.2 system-ui,sans-serif;'
            + 'color:#fff;background:rgba(0,0,0,.62);padding:1px 4px;border-radius:3px;white-space:nowrap;'
        if (height < 14) tag.style.top = '0', tag.style.transform = 'translateY(-100%)'
        // A value a field sets: the label takes a click (the bands take none), and the small panel shows that field.
        if (field) {
            tag.dataset.field = field
            tag.style.pointerEvents = 'auto'
            tag.style.cursor = 'pointer'
            tag.style.textDecoration = 'underline dotted'
        }
        box.appendChild(tag)
    }
    layer!.appendChild(box)
    // Not under the host's small panel: there a click meant for the label went into the panel's first field (hp job
    // 274, w10 job 276) - moved to the panel's left. At the band's left it lay over the text's start instead.
    const tag = box.firstElementChild as HTMLElement | null
    if (tag && panelRect) {
        const t = tag.getBoundingClientRect()
        if (t.right > panelRect.left - 4 && t.left < panelRect.right && t.bottom > panelRect.top && t.top < panelRect.bottom) {
            tag.style.right = 'auto'
            tag.style.left = `${Math.max(2 - left, panelRect.left - 6 - t.width - left)}px`
        }
    }
}

// The host's small panel beside the block, in the page's px (TypographyPanel; null when it is not shown).
let panelRect: { left: number, top: number, right: number, bottom: number } | null = null

/**
 * The designer's field (the small panel's field id) that sets a value the guides show on a block of this kind, or null
 * for one no field sets (a list item's space after): the same as typography-guides-audit traces them.
 */
export const fieldOf = (value: 'before' | 'after' | 'indent' | 'firstLine' | 'width', kind: string): string | null => {
    if (value === 'width') return 'Width'
    if (value === 'firstLine') return kind === 'p' ? 'Indent' : null
    const heading = /^h[1-6]$/.test(kind)
    if (value === 'indent') return kind === 'li' ? 'ListIndent' : kind === 'blockquote' ? 'QuoteIndent' : null
    if (kind === 'p') return value === 'before' ? 'ParagraphBefore' : 'ParagraphAfter'
    if (heading) return value === 'before' ? 'HeadingBefore' : 'HeadingAfter'
    if (kind === 'li') return value === 'before' ? 'ListItemSpacing' : null
    if (/^(blockquote|pre|figure)$/.test(kind)) return 'BlockSpacing'
    return null
}

const draw = () => {
    frame = 0
    if (!layer) return
    layer.textContent = ''
    // Drawn anew as an element of the same id (typed in, its styles changed): that one.
    if (target && !target.isConnected && target.id) target = document.getElementById(target.id)
    if (!on || !target || !target.isConnected) { measured = null; return }
    const r = target.getBoundingClientRect()
    if (!r.width && !r.height) { measured = null; return }
    const m = measured = measure(target)
    const editor = document.getElementById('ag-editor-id')!
    const page = editor.getBoundingClientRect()
    const pageStyle = getComputedStyle(editor)
    const textLeft = page.left + px(pageStyle.paddingLeft)
    // The page's width: a dashed edge each side, its value at the top; its margins (inside the width, outside the
    // text) lightly tinted.
    const edge = 'position:fixed;top:0;bottom:0;width:0;border-left:1px dashed rgba(0,120,212,.55);'
    for (const x of [page.left, page.left + m.width]) {
        const line = document.createElement('div')
        line.style.cssText = edge + `left:${Math.round(x)}px;`
        layer.appendChild(line)
    }
    const margin = 'rgba(0,120,212,.07)'
    band(page.left, 0, px(pageStyle.paddingLeft), window.innerHeight, margin)
    band(page.right - px(pageStyle.paddingRight), 0, px(pageStyle.paddingRight), window.innerHeight, margin)
    band(page.left, Math.max(0, r.top - m.before - 18), m.width, 0, 'transparent', `↔ ${Math.round(m.width)}px`, fieldOf('width', m.kind))
    const orange = 'rgba(246,178,107,.42)', green = 'rgba(147,196,125,.45)'
    if (m.before) band(r.left, r.top - m.before, r.width, m.before, orange, `↑ ${label(m.before, m.fontSize)}`, fieldOf('before', m.kind))
    if (m.after) band(r.left, r.bottom, r.width, m.after, orange, `↓ ${label(m.after, m.fontSize)}`, fieldOf('after', m.kind))
    const style = getComputedStyle(target)
    const padding = px(style.borderLeftWidth) + px(style.paddingLeft)
    if (m.indent) band(textLeft, r.top, m.indent, r.height, green, `→ ${label(m.indent, m.fontSize)}`, fieldOf('indent', m.kind))
    if (m.firstLine) {
        const line = px(style.lineHeight) || m.fontSize * 1.6
        band(r.left + padding, r.top + px(style.paddingTop), m.firstLine, line, green, `⇥ ${label(m.firstLine, m.fontSize)}`, fieldOf('firstLine', m.kind))
    }
}

const schedule = () => { if (!frame) frame = requestAnimationFrame(draw) }

const hide = () => { target = null; schedule() }
// The caret's block, shown: not while typing.
const show = () => { target = typing ? null : caretBlock; schedule() }

/**
 * The designer's group for a block, as the designer groups its fields (code, then headings, a quote, a list, the body):
 * from the page, so reading mode has it too - the editor's paragraph state, which the host had it from, is not sent
 * there (the user, 2026-11-05: in reading mode the panel came but kept the kind it had).
 */
export const groupOf = (block: HTMLElement): string => {
    if (block.closest('pre') || (block.tagName === 'FIGURE' && block.querySelector('pre'))) return 'code'
    if (/^H[1-6]$/.test(block.tagName)) return 'headings'
    if (block.closest('#ag-editor-id blockquote')) return 'quote'
    if (block.closest('#ag-editor-id li')) return 'lists'
    return 'body'
}

/**
 * What each of the designer's fields is on the page now, by the automation id the panel's field has (Size, H2, ...):
 * an empty field shows it, greyed - "as it is" with its value in it; cleared, the value is the page's own again (the
 * user, 2026-11-06). px for sizes and widths, a factor for line heights, em for the rest; rounded to two places.
 */
export const effectiveValues = (_block?: HTMLElement): Record<string, number> => {
    const editor = document.getElementById('ag-editor-id')
    if (!editor) return {}
    const round = (v: number) => Math.round(v * 100) / 100
    const out: Record<string, number> = {}
    const em = (value: string, of: Element) => round(px(value) / (px(getComputedStyle(of).fontSize) || 16))
    // Every field's, whatever the document holds: a sample of each kind of block at the editor's end, hidden, read and
    // taken out at once (a document with no third-level heading had no H3 to read, and the designer's fields stayed
    // empty - the user, 2026-11-06). Muya's observer minds a table removed or the editor emptied, neither of which this is.
    const probe = document.createElement('div')
    probe.setAttribute('data-typography-probe', '')
    probe.innerHTML = '<p class="ag-paragraph">x</p><h1>x</h1><h2>x</h2><h3>x</h3><h4>x</h4><h5>x</h5><h6>x</h6>'
        + '<ul><li class="ag-list-item"><p class="ag-paragraph">x</p></li><li class="ag-list-item"><p class="ag-paragraph">x</p></li></ul>'
        + '<blockquote><p class="ag-paragraph">x</p></blockquote>'
    const samples = Array.from(probe.children) as HTMLElement[]
    for (const sample of samples) {
        sample.style.visibility = 'hidden'
        sample.style.position = 'absolute'
        sample.style.left = '-10000px'
        sample.setAttribute('contenteditable', 'false')
        editor.appendChild(sample)
    }
    try {
        const [body, ...rest] = samples
        const headings = rest.slice(0, 6)
        const list = rest[6], quote = rest[7]
        const s = getComputedStyle(body)
        const bodySize = px(s.fontSize) || 16
        out.Size = round(bodySize)
        const line = px(s.lineHeight)
        if (line) out.LineHeight = round(line / bodySize)
        out.ParagraphBefore = em(s.marginTop, body)
        out.ParagraphAfter = em(s.marginBottom, body)
        out.Indent = em(s.textIndent, body)
        out.LetterSpacing = s.letterSpacing === 'normal' ? 0 : em(s.letterSpacing, body)
        out.Width = Math.round(editor.offsetWidth)
        out.BlockSpacing = em(getComputedStyle(list).marginTop, list)
        headings.forEach((h, i) => { out['H' + (i + 1)] = round(px(getComputedStyle(h).fontSize) / bodySize) })
        const h = getComputedStyle(headings[0])
        out.HeadingBefore = em(h.marginTop, headings[0])
        out.HeadingAfter = em(h.marginBottom, headings[0])
        const hLine = px(h.lineHeight)
        if (hLine) out.HeadingLineHeight = round(hLine / (px(h.fontSize) || 16))
        out.ListIndent = em(getComputedStyle(list).paddingInlineStart, list)
        const second = list.children[1] as HTMLElement
        out.ListItemSpacing = em(getComputedStyle(second).marginTop, second)
        out.QuoteIndent = em(getComputedStyle(quote).paddingLeft, quote)
        const bar = px(getComputedStyle(quote, '::before').width)
        if (bar) out.QuoteBarWidth = round(bar)
    } finally {
        for (const sample of samples) sample.remove()
    }
    return out
}

// Every field's value on the page, for the designer's empty fields (TypographyValues): when the guides come on and
// whenever the styles change.
let sentValues = ''
const sendValues = () => {
    if (!on) return
    const values = effectiveValues()
    const text = JSON.stringify(values)
    if (text === sentValues) return
    sentValues = text
    transport.postMessage('TypographyValues', values)
}

// The caret's block for the designer's small panel beside it (TypographyBlock: its rect in the page's px, its kind and
// a heading's level; null for none): not while typing - a key pressed hides it until a click or another block.
let caretBlock: HTMLElement | null = null
let typing = false
let sent = ''
// moved: sent for a click or the caret moved (the host gives the keyboard back to the document then); not for the
// styles changed or a scroll - while a value was typed in the panel, its own change took the keyboard from it.
const sendCaretBlock = (moved = false) => {
    const block = on && !typing ? caretBlock : null
    const report = block ? reportOf(block) : null
    const args = report ? { ...report, moved } : null
    const text = JSON.stringify(args)
    if (text === sent) return
    sent = text
    transport.postMessage('TypographyBlock', args)
}
const sameBlock = (a: HTMLElement | null, b: HTMLElement | null) => a === b || (!!a && !!b && !!a.id && a.id === b.id)
/** A block as the host's small panel takes it: its rect, kind, a heading's level, group and its fields' values. */
const reportOf = (block: HTMLElement): Record<string, unknown> | null => {
    if (!block.isConnected) return null
    const r = block.getBoundingClientRect()
    const kind = block.tagName.toLowerCase()
    return r.height > 0
        ? { x: r.left, y: r.top, width: r.width, height: r.height, kind, level: /^h[1-6]$/.test(kind) ? +kind[1] : 0, group: groupOf(block) }
        : null
}

// A label clicked: its block and its field to the host (TypographyField), which shows the field in the small panel -
// where a value the guides show is to be changed (the user, 2026-11-06). Taken before the editor sees the press.
let labelPressed = false
const onLabel = (event: PointerEvent) => {
    const tag = event.target instanceof HTMLElement ? event.target.closest('[data-field]') as HTMLElement | null : null
    if (!tag || !layer?.contains(tag) || !target) return
    event.preventDefault()
    event.stopImmediatePropagation()
    // Its release sends no caret's block: the panel stays on this block and field.
    labelPressed = true
    const report = reportOf(target)
    if (report) transport.postMessage('TypographyField', { ...report, field: tag.dataset.field })
}

const onSelection = () => {
    const node = window.getSelection()?.anchorNode
    const block = blockAt(node ? (node.nodeType === Node.ELEMENT_NODE ? node as Element : node.parentElement) : null)
    // Another block, by its id (its key): typing draws the block again as a new element with the same id, which is
    // still the block typed in - the panel came back at each key (w10 job 254).
    if (!sameBlock(block, caretBlock)) typing = false
    caretBlock = block
    show()
    sendCaretBlock(true)
}
// Typing: what the page is given to put in (beforeinput, an input method's composition) - with an input method on
// a key comes as "Process", not its character, and typing Chinese never hid the panel (w10 job 255); and the keys
// the editor takes itself, which may give no beforeinput.
const typed = () => {
    hide()
    typing = true
    sendCaretBlock()
}
const onKey = (event: KeyboardEvent) => {
    if (event.key.length === 1 || event.key === 'Process' || event.key === 'Backspace' || event.key === 'Delete' || event.key === 'Enter') typed()
}
const onPress = () => { typing = false }
// A click closes the host's panel (a flyout gives way to a click elsewhere): the caret's block sent again once
// released, even unchanged - a click back into the block the caret was in left the panel closed.
const onRelease = () => {
    if (labelPressed) { labelPressed = false; return }
    setTimeout(() => { sent = ''; onSelection() }, 0)
}
// The styles changed (a preset saved, a field set or cleared): the caret's block sent again, its values with it.
let styles: MutationObserver | null = null
const onStyles = () => {
    sendValues()
    schedule()
    if (!caretBlock || scrollFrame) return
    scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; sendCaretBlock() })
}

let scrollFrame = 0
const onBlockScroll = () => {
    schedule()
    if (!caretBlock || scrollFrame) return
    scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; sendCaretBlock() })
}

export const setTypographyGuides = (value: boolean) => {
    if (value === on) return
    on = value
    if (on) {
        layer = document.createElement('div')
        layer.id = 'td-typography-guides'
        layer.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2147483000;'
        document.body.appendChild(layer)
        document.addEventListener('keydown', onKey, true)
        document.addEventListener('beforeinput', typed, true)
        document.addEventListener('compositionstart', typed, true)
        document.addEventListener('pointerdown', onLabel, true)
        document.addEventListener('pointerdown', onPress, true)
        document.addEventListener('pointerup', onRelease, true)
        document.addEventListener('selectionchange', onSelection)
        document.addEventListener('scroll', onBlockScroll, { passive: true, capture: true })
        styles = new MutationObserver(onStyles)
        styles.observe(document.head, { childList: true, subtree: true, characterData: true, attributes: true })
        sentValues = ''
        sendValues()
        onSelection()
    } else {
        document.removeEventListener('keydown', onKey, true)
        document.removeEventListener('beforeinput', typed, true)
        document.removeEventListener('compositionstart', typed, true)
        document.removeEventListener('pointerdown', onLabel, true)
        document.removeEventListener('pointerdown', onPress, true)
        document.removeEventListener('pointerup', onRelease, true)
        document.removeEventListener('selectionchange', onSelection)
        document.removeEventListener('scroll', onBlockScroll, { capture: true } as EventListenerOptions)
        styles?.disconnect()
        styles = null
        if (scrollFrame) cancelAnimationFrame(scrollFrame)
        scrollFrame = 0
        caretBlock = null
        typing = false
        sendCaretBlock()
        if (frame) cancelAnimationFrame(frame)
        frame = 0
        layer?.remove()
        layer = null
        target = null
        measured = null
    }
}

export const installTypographyGuides = () => {
    transport.addListener<{ on?: boolean }>('TypographyGuides', args => setTypographyGuides(!!args?.on))
    transport.addListener<{ x: number, y: number, width: number, height: number } | null>('TypographyPanel', args => {
        panelRect = args ? { left: args.x, top: args.y, right: args.x + args.width, bottom: args.y + args.height } : null
        schedule()
    })
    // For the page checks: what the guides show now (null: nothing).
    ;(window as any).__typographyGuides = () => (on ? measured : null)
}
