import loadRenderer from '../../renderers'
import { CLASS_OR_ID } from '../../config'
import { conflict, mixins, camelToSnake } from '../../utils'
import { patch, toVNode, toHTML, h } from './snabbdom'
import { beginRules } from '../rules'
import renderInlines from './renderInlines'
import renderBlock from './renderBlock'

let mermaidRenderId = 0
const MERMAID_RENDER_CACHE_LIMIT = 32

class StateRender {
  constructor(muya) {
    this.muya = muya
    this.eventCenter = muya.eventCenter
    this.codeCache = new Map()
    this.loadImageMap = new Map()
    this.loadMathMap = new Map()
    this.mermaidCache = new Map()
    this.mermaidRenderCache = new Map()
    this.mermaidRenderTargets = new WeakMap()
    this.mermaidRenderRequests = new WeakMap()
    this.mermaidRenderRequestId = 0
    this.mermaidRenderQueue = Promise.resolve()
    this.diagramCache = new Map()
    this.tokenCache = new Map()
    this.labels = new Map()
    this.urlMap = new Map()
    this.renderingTable = null
    this.renderingRowContainer = null
    this.container = null
    // A whole document drawn a part at a time (renderProgressive): which top-level blocks are on the page, and the
    // timer drawing the rest. Null when the whole document is on the page.
    this.progressive = null
  }

  setContainer(container) {
    this.container = container
  }

  // collect link reference definition
  collectLabels(blocks) {
    this.labels.clear()

    const travel = block => {
      const { text, children } = block
      if (children && children.length) {
        children.forEach(c => travel(c))
      } else if (text) {
        const tokens = beginRules.reference_definition.exec(text)
        if (tokens) {
          const key = (tokens[2] + tokens[3]).toLowerCase()
          if (!this.labels.has(key)) {
            this.labels.set(key, {
              href: tokens[6],
              title: tokens[10] || ''
            })
          }
        }
      }
    }

    blocks.forEach(b => travel(b))
  }

  checkConflicted(block, token, cursor) {
    // Reading mode never reveals the Markdown source of a token.
    if (this.muya.options.readOnly) {
      return false
    }
    const { start, end } = cursor
    const key = block.key
    const { start: tokenStart, end: tokenEnd } = token.range

    if (key !== start.key && key !== end.key) {
      return false
    } else if (key === start.key && key !== end.key) {
      return conflict([tokenStart, tokenEnd], [start.offset, start.offset])
    } else if (key !== start.key && key === end.key) {
      return conflict([tokenStart, tokenEnd], [end.offset, end.offset])
    } else {
      return conflict([tokenStart, tokenEnd], [start.offset, start.offset]) ||
        conflict([tokenStart, tokenEnd], [end.offset, end.offset])
    }
  }

  getClassName(outerClass, block, token, cursor) {
    return outerClass || (this.checkConflicted(block, token, cursor) ? CLASS_OR_ID.AG_GRAY : CLASS_OR_ID.AG_HIDE)
  }

  getHighlightClassName(active) {
    return active ? CLASS_OR_ID.AG_HIGHLIGHT : CLASS_OR_ID.AG_SELECTION
  }

  getSelector(block, activeBlocks) {
    const { cursor, selectedBlock } = this.muya.contentState
    const type = block.type === 'hr' ? 'p' : block.type
    const isActive = !this.muya.options.readOnly &&
      (activeBlocks.some(b => b.key === block.key) || block.key === cursor.start.key)

    let selector = `${type}#${block.key}.${CLASS_OR_ID.AG_PARAGRAPH}`
    if (isActive) {
      selector += `.${CLASS_OR_ID.AG_ACTIVE}`
    }
    if (type === 'span') {
      selector += `.ag-${camelToSnake(block.functionType)}`
    }
    if (!block.parent && selectedBlock && block.key === selectedBlock.key) {
      selector += `.${CLASS_OR_ID.AG_SELECTED}`
    }
    return selector
  }

  renderMermaid() {
    if (!this.mermaidCache.size) return this.mermaidRenderQueue

    const theme = window.actualTheme == 'dark' ? 'dark' : 'default'
    // Rendering is asynchronous, while typing can replace this DOM node every 300 ms. Take ownership of the
    // current batch immediately: an older render must never clear work collected by a newer partial render.
    const requests = []
    for (const [key, { code }] of this.mermaidCache.entries()) {
      const target = document.querySelector(key)
      if (!target) continue
      const cacheKey = `${theme}\n${code}`
      if (this.mermaidRenderTargets.get(target) === cacheKey && target.querySelector('svg')) continue
      const requestId = ++this.mermaidRenderRequestId
      this.mermaidRenderRequests.set(target, requestId)
      requests.push({ target, code, cacheKey, requestId })
    }
    this.mermaidCache.clear()
    if (!requests.length) return this.mermaidRenderQueue

    // Mermaid is a stateful global. Concurrent calls interfere with its temporary DOM and can stall WebKit;
    // serializing them also lets queued edits discard intermediate nodes before doing expensive work.
    this.mermaidRenderQueue = this.mermaidRenderQueue.catch(() => {}).then(async () => {
      const isCurrent = request => request.target.isConnected &&
        this.mermaidRenderRequests.get(request.target) === request.requestId
      const current = requests.filter(isCurrent)
      if (!current.length) return

      let mermaid
      try {
        mermaid = await loadRenderer('mermaid')
        mermaid.initialize({
          startOnLoad: false,
          securityLevel: 'strict',
          theme
        })
      } catch (err) {
        console.error('Failed to load mermaid renderer', err)
        for (const request of current) {
          if (!isCurrent(request)) continue
          request.target.innerHTML = '< Invalid Mermaid Codes >'
          request.target.classList.add(CLASS_OR_ID.AG_MATH_ERROR)
        }
        return
      }

      for (const request of current) {
        if (!isCurrent(request)) continue
        const { target, code, cacheKey } = request
        try {
          let svg = this.mermaidRenderCache.get(cacheKey)
          if (svg == null) {
            const renderId = `mermaid-render-${mermaidRenderId++}`
            try {
              const result = await mermaid.render(renderId, code)
              svg = result.svg
            } finally {
              // Mermaid appends d<renderId> to <body> when parsing fails. The block below already shows the
              // inline error, so keeping that second diagnostic puts it below the document in every mode.
              // It also survives when the block or tab is removed and accumulates on every retry.
              document.getElementById(`d${renderId}`)?.remove()
            }
            this.mermaidRenderCache.set(cacheKey, svg)
            while (this.mermaidRenderCache.size > MERMAID_RENDER_CACHE_LIMIT) {
              this.mermaidRenderCache.delete(this.mermaidRenderCache.keys().next().value)
            }
          } else {
            // Refresh insertion order so the bounded cache keeps recently reused diagrams.
            this.mermaidRenderCache.delete(cacheKey)
            this.mermaidRenderCache.set(cacheKey, svg)
          }
          if (!isCurrent(request)) continue
          target.innerHTML = svg
          target.classList.remove(CLASS_OR_ID.AG_MATH_ERROR)
          this.mermaidRenderTargets.set(target, cacheKey)
        } catch (err) {
          if (!isCurrent(request)) continue
          target.innerHTML = '< Invalid Mermaid Codes >'
          target.classList.add(CLASS_OR_ID.AG_MATH_ERROR)
        }
      }
    })
    return this.mermaidRenderQueue
  }

  // Called without await: it never rejects. Each renderer is loaded when a diagram of its kind is there, so one that
  // cannot be loaded leaves only its own diagrams undrawn (loading all four first, one failing left every flowchart,
  // sequence and Vega-Lite block undrawn and the queue never cleared).
  async renderDiagram() {
    const cache = this.diagramCache
    if (!cache.size) return
    const entries = [...cache.entries()]
    this.diagramCache.clear()
    const NAMES = { flowchart: 'Flow Chart', sequence: 'Sequence', plantuml: 'PlantUML', 'vega-lite': 'Vega-Lite' }
    const renderers = {}
    for (const [key, value] of entries) {
      const target = document.querySelector(key)
      if (!target) {
        continue
      }
      const { code, functionType } = value
      const options = {}
      if (functionType === 'sequence') {
        Object.assign(options, { theme: this.muya.options.sequenceTheme })
      } else if (functionType === 'vega-lite') {
        Object.assign(options, {
          actions: false,
          tooltip: false,
          renderer: 'svg',
          theme: window.actualTheme == 'dark' ? 'dark' : 'latimes'
        })
      }
      try {
        if (!(functionType in renderers)) renderers[functionType] = await loadRenderer(functionType)
        const render = renderers[functionType]
        if (functionType === 'flowchart' || functionType === 'sequence') {
          const diagram = render.parse(code)
          // A container of its own for each drawing, in place of whatever the block held. The sequence library waits
          // for its font the first time and only then adds its picture: every draw in that wait (a document with many
          // diagrams draws a block again while the other renderers load) added one more to the same block - ten copies
          // of the showcase's sequence diagram (DG02). A drawing that ends after a newer one began now lands in a
          // container no longer on the page.
          const drawing = document.createElement('div')
          drawing.className = 'ag-diagram-drawing'
          target.replaceChildren(drawing)
          await diagram.drawSVG(drawing, options)
        } else if (functionType === 'plantuml') {
          target.innerHTML = ''
          if (this.muya.options.renderPlantUml) {
            render.parse(code).insertImgElement(target, this.muya.options.plantUmlServer)
          } else {
            // Off (the default): nothing goes to plantuml.com; the block says so (assets/styles/index.css).
            target.classList.add('ag-plantuml-off')
          }
        } else if (functionType === 'vega-lite') {
          await render(key, JSON.parse(code), options)
        }
      } catch (err) {
        console.log(`diagram: a ${functionType} diagram could not be drawn`, err)
        target.innerHTML = `< Invalid ${NAMES[functionType] || functionType} Codes >`
        target.classList.add(CLASS_OR_ID.AG_MATH_ERROR)
      }
    }
  }

  // `fresh` means the whole document is being replaced (a new file, not an edit). Diffing the outgoing
  // document against the incoming one is pure waste then, and on a long document it is most of the cost of
  // a tab switch: reading the old tree back with toVNode and deleting it node by node. Drop it in one go
  // instead and let the patch below build the new tree into an empty element.
  render(blocks, activeBlocks, matches, fresh = false) {
    // A new document replaces whatever was still being drawn; anything else is drawn over the whole document.
    if (fresh) this.cancelProgressive()
    else this.completeRender()
    const selector = `div#${CLASS_OR_ID.AG_EDITOR_ID}`
    const children = blocks.map(block => {
      return this.renderBlock(null, block, activeBlocks, matches, true)
    })
    // Its own root, not whatever the document happens to hold: more than one editor can exist at a time
    // (a document kept in memory while another is shown) and they all call their root ag-editor-id, so a
    // global lookup would let a new editor render itself into the document that is still on screen.
    const rootDom = this.container || document.querySelector(selector)
    if (fresh) {
      // Nothing of the old document survives, so there is nothing to diff: building the new one as a single
      // HTML string and letting the parser create the nodes is several times faster than creating a hundred
      // thousand elements one at a time — and it is what partialRender already does for the blocks an edit
      // touches, so every block type goes through this serializer on every keystroke anyway.
      const html = toHTML(h('section', children)).replace(/^<section>([\s\S]*?)<\/section>$/, '$1')
      rootDom.id = CLASS_OR_ID.AG_EDITOR_ID
      rootDom.innerHTML = html
    } else {
      patch(toVNode(rootDom), h(selector, children))
    }
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
  }

  /**
   * Replaces the DOM of some top-level blocks with other blocks, leaving every other block's DOM alone (Muya's
   * replaceMarkdownLocally). The new blocks go before beforeKey's element, or at the end.
   */
  replaceBlocks(removedKeys, blocks, beforeKey, activeBlocks, matches) {
    this.completeRender()
    const root = this.container || document.querySelector(`div#${CLASS_OR_ID.AG_EDITOR_ID}`)
    const byId = key => root.querySelector(`#${key}`)
    if (blocks.length) {
      const html = toHTML(h('section', blocks.map(block => this.renderBlock(null, block, activeBlocks, matches, true))))
        .replace(/^<section>([\s\S]*?)<\/section>$/, '$1')
      const before = beforeKey ? byId(beforeKey) : null
      if (before) before.insertAdjacentHTML('beforebegin', html)
      else root.insertAdjacentHTML('beforeend', html)
    }
    for (const key of removedKeys) {
      const dom = byId(key)
      if (dom) dom.remove()
    }
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
  }

  // Only render the blocks which you updated
  partialRender(blocks, activeBlocks, matches, startKey, endKey) {
    this.completeRender()
    const cursorOutMostBlock = activeBlocks[activeBlocks.length - 1]
    // If cursor is not in render blocks, need to render cursor block independently
    const needRenderCursorBlock = blocks.indexOf(cursorOutMostBlock) === -1
    const newVnode = h('section', blocks.map(block => this.renderBlock(null, block, activeBlocks, matches)))
    const html = toHTML(newVnode).replace(/^<section>([\s\S]+?)<\/section>$/, '$1')

    const needToRemoved = []
    const root = this.container || document.querySelector(`div#${CLASS_OR_ID.AG_EDITOR_ID}`)
    const firstOldDom = startKey ? root.querySelector(`#${startKey}`) : root.firstElementChild
    if (!firstOldDom) {
      // TODO@Jocs Just for fix #541, Because I'll rewrite block and render method, it will nolonger have this issue.
      return
    }
    needToRemoved.push(firstOldDom)
    let nextSibling = firstOldDom.nextElementSibling
    while (nextSibling && nextSibling.id !== endKey) {
      needToRemoved.push(nextSibling)
      nextSibling = nextSibling.nextElementSibling
    }
    nextSibling && needToRemoved.push(nextSibling)

    firstOldDom.insertAdjacentHTML('beforebegin', html)

    Array.from(needToRemoved).forEach(dom => dom.remove())

    // Render cursor block independently
    if (needRenderCursorBlock) {
      const { key } = cursorOutMostBlock
      const cursorDom = root.querySelector(`#${key}`)
      if (cursorDom) {
        const oldCursorVnode = toVNode(cursorDom)
        const newCursorVnode = this.renderBlock(null, cursorOutMostBlock, activeBlocks, matches)
        patch(oldCursorVnode, newCursorVnode)
      }
    }

    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
  }

  /**
   * Only render one block.
   *
   * @param {object} block
   * @param {array} activeBlocks
   * @param {array} matches
   */
  singleRender(block, activeBlocks, matches) {
    this.completeRender()
    const selector = `#${block.key}`
    const newVdom = this.renderBlock(null, block, activeBlocks, matches, true)
    const rootDom = (this.container || document).querySelector(selector)
    const oldVdom = toVNode(rootDom)
    patch(oldVdom, newVdom)
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
  }

  /**
   * Draws a new document a part at a time (docs/todo.md, "方案 A"): the top-level blocks around `center` at once, the
   * rest above and below in small slices while the page is otherwise idle, so the reader sees and can use the part of
   * a long document they are at long before the whole of it would have been drawn. Everything that works on the page's
   * elements - an edit's redraw, the caret, a search - first draws what is left (completeRender), so the page is only
   * ever partly drawn while nobody does anything with it. Blocks inserted above the window keep it still (the
   * browser's scroll anchoring). `done` runs once the whole document is on the page, whichever way it got there.
   */
  renderProgressive(blocks, activeBlocks, matches, center, done) {
    this.cancelProgressive()
    const rootDom = this.container || document.querySelector(`div#${CLASS_OR_ID.AG_EDITOR_ID}`)
    const before = 30
    const after = 70
    const lo = Math.max(0, center - before)
    const hi = Math.min(blocks.length, center + after)
    rootDom.id = CLASS_OR_ID.AG_EDITOR_ID
    rootDom.innerHTML = this.blocksHtml(blocks.slice(lo, hi), activeBlocks, matches)
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
    const state = { blocks, activeBlocks, matches, lo, hi, root: rootDom, timer: null, done, slices: 0, startedAt: performance.now() }
    this.progressive = state
    this.holdAnchoring(true)
    if (lo === 0 && hi === blocks.length) return this.finishProgressive(state)
    this.estimateRest(state)
    this.watchForBlank(state)
    // The rest only once this part is on the screen: drawn before the first paint, it held that paint back. A page not
    // shown (a window minimized) has no frames: the rest goes on at once.
    if (document.hidden) return this.scheduleProgressive(state)
    state.timer = -1
    requestAnimationFrame(() => {
      if (this.progressive === state && state.timer === -1) this.scheduleProgressive(state)
    })
  }

  // The next slice in a moment: below, then above, for about a frame's worth of time, then the page's turn again.
  scheduleProgressive(state) {
    const blocks = state.blocks
    const step = () => {
      state.timer = null
      if (this.progressive !== state) return
      const until = performance.now() + (window.__typedownProgressiveSliceMs ?? 12)
      const keep = this.keepInPlace(state)
      // A few blocks at a time, the clock read after each few: twenty code blocks of forty lines each, highlighted,
      // took half a second, and no frame was drawn in it.
      do {
        if (state.hi < blocks.length) this.drawMore(state, 'below', 4)
        if (state.lo > 0) this.drawMore(state, 'above', 4)
      } while ((state.lo > 0 || state.hi < blocks.length) && performance.now() < until)
      this.estimateRest(state)
      keep()
      state.slices++
      if (state.lo > 0 || state.hi < blocks.length) state.timer = setTimeout(step, window.__typedownProgressiveDelayMs ?? 0)
      else this.finishProgressive(state)
    }
    clearTimeout(state.timer)
    state.timer = setTimeout(step, window.__typedownProgressiveDelayMs ?? 0)
  }

  /**
   * Stops drawing and hands over how far it got, for a document put away half drawn (Muya.takeDocument): its elements
   * go with it, and resumeProgressive carries on where it stopped once it is shown again.
   */
  suspendProgressive() {
    const state = this.progressive
    if (!state) return null
    clearTimeout(state.timer)
    state.timer = null
    this.progressive = null
    this.holdAnchoring(false)
    return state
  }

  /** Carries on drawing a document put back half drawn (Muya.showDocument), on the root it is drawn in. */
  resumeProgressive(state) {
    if (!state || (state.lo === 0 && state.hi === state.blocks.length)) return
    this.cancelProgressive()
    state.root = this.container || state.root
    this.progressive = state
    this.holdAnchoring(true)
    this.estimateRest(state)
    this.watchForBlank(state)
    this.scheduleProgressive(state)
  }

  blocksHtml(blocks, activeBlocks, matches) {
    return toHTML(h('section', blocks.map(block => this.renderBlock(null, block, activeBlocks, matches, true))))
      .replace(/^<section>([\s\S]*?)<\/section>$/, '$1')
  }

  /**
   * The part not drawn yet takes the room it will need, as blank above and below what is drawn (the root's ::before and
   * ::after, assets/styles/index.css: not elements, so the blocks are still the root's children one for one): the
   * document is about as tall from the start as when it is all drawn. The scroll bar then neither shrinks nor wanders
   * while the rest comes, a block going in only replaces its own room (the window moves by what the guess was off, not
   * by the block), and dragging the scroll bar follows the hand. The guess is a height per character of what is drawn,
   * made again as more is.
   */
  estimateRest(state) {
    const { blocks, root } = state
    if (!state.chars) {
      const count = block => {
        let n = 0
        const walk = b => {
          if (typeof b.text === 'string') n += b.text.length + 1
          if (b.children) b.children.forEach(walk)
        }
        walk(block)
        return n
      }
      state.chars = blocks.map(count)
    }
    const first = root.firstElementChild
    const last = root.lastElementChild
    if (!first || !last) return
    const drawnHeight = last.getBoundingClientRect().bottom - first.getBoundingClientRect().top
    let drawnChars = 0
    for (let i = state.lo; i < state.hi; i++) drawnChars += state.chars[i]
    // A block has a height of its own (margins) besides its text's: half of it per block, the other half per character.
    const perBlock = drawnHeight / (state.hi - state.lo) / 2
    const perChar = drawnChars > 0 ? drawnHeight / 2 / drawnChars : 0
    const room = (from, to) => {
      let h = 0
      for (let i = from; i < to; i++) h += perBlock + state.chars[i] * perChar
      return Math.round(h)
    }
    root.style.setProperty('--td-room-above', `${room(0, state.lo)}px`)
    root.style.setProperty('--td-room-below', `${room(state.hi, blocks.length)}px`)
  }

  clearRoom(state) {
    state.root.style.removeProperty('--td-room-above')
    state.root.style.removeProperty('--td-room-below')
  }

  // The reader going where nothing is drawn yet (the scroll bar dragged far): the rest at once, not a blank page. Only a
  // move of the reader's own counts (the wheel, the scroll bar): the page put back where it was left scrolls too, with
  // the room above still in the window for a moment, and that drew it all at once on every load.
  watchForBlank(state) {
    let reader = false
    const arm = e => { if (e.type === 'wheel' || e.target === document.documentElement) reader = true }
    const stop = () => {
      window.removeEventListener('scroll', check)
      window.removeEventListener('wheel', arm, true)
      window.removeEventListener('pointerdown', arm, true)
    }
    const check = () => {
      if (this.progressive !== state) return stop()
      if (!reader) return
      const first = state.root.firstElementChild
      const last = state.root.lastElementChild
      if (!first || !last) return
      if ((state.lo > 0 && first.getBoundingClientRect().top > 0) || (state.hi < state.blocks.length && last.getBoundingClientRect().bottom < window.innerHeight)) {
        stop()
        this.completeRender()
      }
    }
    window.addEventListener('scroll', check, { passive: true })
    window.addEventListener('wheel', arm, { capture: true, passive: true })
    window.addEventListener('pointerdown', arm, true)
  }

  // The next `count` blocks on one side of what is drawn.
  drawMore(state, side, count) {
    const { blocks, activeBlocks, matches, root } = state
    if (side === 'below') {
      const to = Math.min(blocks.length, state.hi + count)
      root.insertAdjacentHTML('beforeend', this.blocksHtml(blocks.slice(state.hi, to), activeBlocks, matches))
      state.hi = to
    } else {
      const from = Math.max(0, state.lo - count)
      root.insertAdjacentHTML('afterbegin', this.blocksHtml(blocks.slice(from, state.lo), activeBlocks, matches))
      state.lo = from
    }
    // The caret waiting for its block (ContentState.setCursor), on the page as soon as the block is.
    if (state.cursorPending) {
      const { start, end } = this.muya.contentState.cursor || {}
      if (start && end && root.querySelector(`#${start.key}`) && root.querySelector(`#${end.key}`)) {
        state.cursorPending = false
        try {
          this.muya.contentState.setCursor()
        } catch (err) {
          console.log(err)
        }
      }
    }
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
  }

  finishProgressive(state) {
    this.clearRoom(state)
    if (this.progressive === state) this.progressive = null
    if (!this.progressive) this.holdAnchoring(false)
    clearTimeout(state.timer)
    ;(window.__typedownProgressiveLog = window.__typedownProgressiveLog || []).push({ slices: state.slices, ms: Math.round(performance.now() - state.startedAt), blocks: state.blocks.length })
    try {
      state.done && state.done()
    } catch (err) {
      console.log(err)
    }
  }

  /** Draws at once whatever of the document is not on the page yet. */
  completeRender() {
    const state = this.progressive
    if (!state) return
    clearTimeout(state.timer)
    const keep = this.keepInPlace(state)
    if (state.hi < state.blocks.length) this.drawMore(state, 'below', state.blocks.length - state.hi)
    if (state.lo > 0) this.drawMore(state, 'above', state.lo)
    this.clearRoom(state)
    keep()
    this.finishProgressive(state)
  }

  /**
   * What is in the window stays where it is while blocks go in above it: the first block drawn so far is measured
   * before and after, and the page scrolled by what it moved. The browser's own scroll anchoring did not hold it (a
   * long code block across the top of the window drifted thousands of pixels as the blocks above it came), and is off
   * meanwhile (holdAnchoring) so the two do not both move the page. Measured once a slice, not once a block.
   */
  keepInPlace(state) {
    const first = state.lo > 0 ? state.root.firstElementChild : null
    if (!first) return () => {}
    const before = first.getBoundingClientRect().top
    return () => {
      if (!first.isConnected) return
      // The page scrolls by whole pixels and the blocks' heights have fractions: what a scroll leaves over is carried
      // to the next, or a few dozen slices added up to pixels and the window crept (JU01 saw it move by two).
      const moved = first.getBoundingClientRect().top - before + (state.carry || 0)
      const by = Math.round(moved)
      if (by) {
        const from = window.scrollY
        window.scrollBy(0, by)
        state.carry = moved - (window.scrollY - from)
      } else {
        state.carry = moved
      }
    }
  }

  // The browser's scroll anchoring off while a document is drawn a part at a time (keepInPlace does its work), on after.
  holdAnchoring(hold) {
    const element = document.scrollingElement || document.documentElement
    if (element) element.style.overflowAnchor = hold ? 'none' : ''
  }

  /** Stops drawing the document (another takes its place): what is on the page stays as it is. */
  cancelProgressive() {
    const state = this.progressive
    if (!state) return
    clearTimeout(state.timer)
    this.progressive = null
    this.clearRoom(state)
    this.holdAnchoring(false)
  }

  invalidateImageCache() {
    this.loadImageMap.forEach((imageInfo, key) => {
      imageInfo.touchMsec = Date.now()
      this.loadImageMap.set(key, imageInfo)
    })
  }
}

mixins(StateRender, renderInlines, renderBlock)

export default StateRender
