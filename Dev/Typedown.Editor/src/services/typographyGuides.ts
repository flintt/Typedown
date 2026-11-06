import transport from './transport'

// The typography designer open (an edition's, beside the document): the block under the pointer shows its spacing as
// the browser's developer tools do - the space before and after it and its indent tinted, the text's width marked,
// each with its value in em and px. Only shown, never in the way: the marks take no pointer, typing hides them. The
// host turns it on and off (TypographyGuides { on }).

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
let lastPointer: { x: number, y: number } | null = null
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

const band = (left: number, top: number, width: number, height: number, colour: string, text?: string) => {
    const box = document.createElement('div')
    box.style.cssText = `position:fixed;left:${left}px;top:${top}px;width:${Math.max(0, width)}px;height:${Math.max(0, height)}px;background:${colour};`
    if (text) {
        const tag = document.createElement('span')
        tag.textContent = text
        tag.style.cssText = 'position:absolute;right:2px;top:50%;transform:translateY(-50%);font:11px/1.2 system-ui,sans-serif;'
            + 'color:#fff;background:rgba(0,0,0,.62);padding:1px 4px;border-radius:3px;white-space:nowrap;'
        if (height < 14) tag.style.top = '0', tag.style.transform = 'translateY(-100%)'
        box.appendChild(tag)
    }
    layer!.appendChild(box)
}

const draw = () => {
    frame = 0
    if (!layer) return
    layer.textContent = ''
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
    band(page.left, Math.max(0, r.top - m.before - 18), m.width, 0, 'transparent', `↔ ${Math.round(m.width)}px`)
    const orange = 'rgba(246,178,107,.42)', green = 'rgba(147,196,125,.45)'
    if (m.before) band(r.left, r.top - m.before, r.width, m.before, orange, `↑ ${label(m.before, m.fontSize)}`)
    if (m.after) band(r.left, r.bottom, r.width, m.after, orange, `↓ ${label(m.after, m.fontSize)}`)
    const style = getComputedStyle(target)
    const padding = px(style.borderLeftWidth) + px(style.paddingLeft)
    if (m.indent) band(textLeft, r.top, m.indent, r.height, green, `→ ${label(m.indent, m.fontSize)}`)
    if (m.firstLine) {
        const line = px(style.lineHeight) || m.fontSize * 1.6
        band(r.left + padding, r.top + px(style.paddingTop), m.firstLine, line, green, `⇥ ${label(m.firstLine, m.fontSize)}`)
    }
}

const schedule = () => { if (!frame) frame = requestAnimationFrame(draw) }

const onPointer = (event: PointerEvent) => {
    lastPointer = { x: event.clientX, y: event.clientY }
    const block = blockAt(document.elementFromPoint(event.clientX, event.clientY))
    if (block === target) return
    target = block
    schedule()
}
const onScroll = () => {
    if (!lastPointer) return schedule()
    target = blockAt(document.elementFromPoint(lastPointer.x, lastPointer.y))
    schedule()
}
const hide = () => { target = null; schedule() }

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

// The caret's block for the designer's small panel beside it (TypographyBlock: its rect in the page's px, its kind and
// a heading's level; null for none): not while typing - a key pressed hides it until a click or another block.
let caretBlock: HTMLElement | null = null
let typing = false
let sent = ''
const sendCaretBlock = () => {
    const block = on && !typing ? caretBlock : null
    let args: unknown = null
    if (block && block.isConnected) {
        const r = block.getBoundingClientRect()
        const kind = block.tagName.toLowerCase()
        if (r.height > 0) args = { x: r.left, y: r.top, width: r.width, height: r.height, kind, level: /^h[1-6]$/.test(kind) ? +kind[1] : 0, group: groupOf(block) }
    }
    const text = JSON.stringify(args)
    if (text === sent) return
    sent = text
    transport.postMessage('TypographyBlock', args)
}
const sameBlock = (a: HTMLElement | null, b: HTMLElement | null) => a === b || (!!a && !!b && !!a.id && a.id === b.id)
const onSelection = () => {
    const node = window.getSelection()?.anchorNode
    const block = blockAt(node ? (node.nodeType === Node.ELEMENT_NODE ? node as Element : node.parentElement) : null)
    // Another block, by its id (its key): typing draws the block again as a new element with the same id, which is
    // still the block typed in - the panel came back at each key (w10 job 254).
    if (!sameBlock(block, caretBlock)) typing = false
    caretBlock = block
    sendCaretBlock()
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
    hide()
    if (event.key.length === 1 || event.key === 'Process' || event.key === 'Backspace' || event.key === 'Delete' || event.key === 'Enter') typed()
}
const onPress = () => { typing = false }
// A click closes the host's panel (a flyout gives way to a click elsewhere): the caret's block sent again once
// released, even unchanged - a click back into the block the caret was in left the panel closed.
const onRelease = () => setTimeout(() => { sent = ''; onSelection() }, 0)
let scrollFrame = 0
const onBlockScroll = () => {
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
        document.addEventListener('pointermove', onPointer, { passive: true })
        document.addEventListener('scroll', onScroll, { passive: true, capture: true })
        document.addEventListener('keydown', onKey, true)
        document.addEventListener('beforeinput', typed, true)
        document.addEventListener('compositionstart', typed, true)
        document.addEventListener('pointerdown', onPress, true)
        document.addEventListener('pointerup', onRelease, true)
        document.addEventListener('selectionchange', onSelection)
        document.addEventListener('scroll', onBlockScroll, { passive: true, capture: true })
        document.documentElement.addEventListener('pointerleave', hide)
        onSelection()
    } else {
        document.removeEventListener('pointermove', onPointer)
        document.removeEventListener('scroll', onScroll, { capture: true } as EventListenerOptions)
        document.removeEventListener('keydown', onKey, true)
        document.removeEventListener('beforeinput', typed, true)
        document.removeEventListener('compositionstart', typed, true)
        document.removeEventListener('pointerdown', onPress, true)
        document.removeEventListener('pointerup', onRelease, true)
        document.removeEventListener('selectionchange', onSelection)
        document.removeEventListener('scroll', onBlockScroll, { capture: true } as EventListenerOptions)
        document.documentElement.removeEventListener('pointerleave', hide)
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
    // For the page checks: what the guides show now (null: nothing).
    ;(window as any).__typographyGuides = () => (on ? measured : null)
}
