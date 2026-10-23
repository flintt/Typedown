import loadRenderer from '../../renderers'
import { CLASS_OR_ID } from '../../config'
import { conflict, mixins, camelToSnake } from '../../utils'
import { patch, toVNode, toHTML, h } from './snabbdom'
import { beginRules } from '../rules'
import renderInlines from './renderInlines'
import renderBlock from './renderBlock'
import HeightPredictor, { account } from './predictHeights'

let mermaidRenderId = 0
const MERMAID_RENDER_CACHE_LIMIT = 32

// The room kept for the part of a document not drawn yet (estimateRest), as properties not inherited: written on the
// root, they restyle only it and its ::before and ::after (which take them explicitly, assets/styles/index.css). As
// plain custom properties every element of the document was restyled each time.
try {
  for (const name of ['--td-room-above', '--td-room-below', '--td-room-fraction']) window.CSS?.registerProperty?.({ name, syntax: '<length>', inherits: false, initialValue: '0px' })
} catch (err) {
  // Registered already (a second bundle on the page), or not supported: inherited, as before.
}

// How long a slice of a document drawn a part at a time may take: short while the reader is at the keys, the mouse or
// the wheel (a frame's worth, so what they do is answered at once), longer when they are not - each slice also costs a
// layout and a paint of its own, and 1.5 MB of HTML-heavy blocks took 38 s in 12 ms slices, 23 s in 40 ms ones.
let lastInputAt = -Infinity
for (const type of ['keydown', 'pointerdown', 'pointermove', 'wheel', 'touchstart', 'compositionstart'])
  window.addEventListener(type, () => { lastInputAt = performance.now() }, { capture: true, passive: true })
const isLongDocument = () => !!document.getElementById('editor')?.classList.contains('ag-long-document')
const sliceBudget = () => performance.now() - lastInputAt < 1000 ? 12 : 40
// The share of each drawing slice spent predicting a document's heights (predictHeights.js). None: the guess from the
// blocks' text (predictHeights fit) is as close while drawn, and every share of a slice made the drawing slower (vm103,
// 1M characters: 8% of each slice 1.37 times the old estimate's time, 4% 1.32, none 1.15-1.23).
const HEIGHTS_SLICE_SHARE = 0

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
    // The heights of a long document's blocks not drawn yet, predicted from their text (predictHeights.js).
    this.heights = new HeightPredictor(this)
  }

  setContainer(container) {
    this.container = container
    // The fonts a long document's blocks are drawn in asked for early (predictHeights.js), so the first one opened can
    // have its heights predicted without waiting for them.
    this.heights.warm(container)
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
  // Guards an async draw against a library call that never settles (webfontloader is meant to time out and call its
  // own `inactive` fallback, but something about this page - layout, timing, or a progressive draw continuing under
  // it - has left the sequence diagram's font wait hanging indefinitely): a `for` loop awaiting each diagram in turn
  // would otherwise be stuck there forever, leaving it and every diagram after it in the same batch empty for good.
  diagramTimeout(result, ms = 8000) {
    // flowchart.js's drawSVG draws synchronously and returns nothing; sequence's returns a promise. Promise.resolve
    // makes either safe to race against the timeout.
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`timed out after ${ms} ms`)), ms)
      Promise.resolve(result).then(v => { clearTimeout(timer); resolve(v) }, e => { clearTimeout(timer); reject(e) })
    })
  }

  async renderDiagram() {
    const cache = this.diagramCache
    if (!cache.size) return
    const entries = [...cache.entries()]
    this.diagramCache.clear()
    const NAMES = { flowchart: 'Flow Chart', sequence: 'Sequence', plantuml: 'PlantUML', 'vega-lite': 'Vega-Lite' }
    // The promise, not its result: concurrent entries of the same kind (several diagrams in one batch) share the one
    // load instead of racing to start it each.
    const renderers = {}
    // In parallel, not one after another: a flowchart or sequence diagram stuck in its font wait (diagramTimeout,
    // below) used to hold up every other diagram in the same batch behind it in a `for` loop, each then needing its
    // own wait in turn - enough diagrams at once ran well past how long a reader (or a test) waits for the document
    // to settle.
    await Promise.all(entries.map(async ([key, value]) => {
      const target = document.querySelector(key)
      if (!target) {
        return
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
        if (!(functionType in renderers)) renderers[functionType] = loadRenderer(functionType)
        const render = await renderers[functionType]
        if (functionType === 'flowchart' || functionType === 'sequence') {
          const diagram = render.parse(code)
          // A container of its own for each drawing, in place of whatever the block held. The sequence library waits
          // for its font the first time and only then adds its picture: every draw in that wait (a document with many
          // diagrams draws a block again while the other renderers load) added one more to the same block - ten copies
          // of the showcase's sequence diagram (DG02). A drawing that ends after a newer one began now lands in a
          // container no longer on the page.
          // One retry, a fresh container each time: a timed-out first attempt (diagramTimeout) is retried once before
          // giving up - the same "another chance" that a document rendered more than once used to give it by accident
          // (the comment above once described exactly that, before drawing a part at a time stopped doing it).
          let lastErr
          for (let attempt = 0; attempt < 3; attempt++) {
            const drawing = document.createElement('div')
            drawing.className = 'ag-diagram-drawing'
            target.replaceChildren(drawing)
            try {
              await this.diagramTimeout(diagram.drawSVG(drawing, options))
              lastErr = null
              break
            } catch (err) {
              lastErr = err
            }
          }
          if (lastErr) throw lastErr
        } else if (functionType === 'plantuml') {
          target.innerHTML = ''
          if (this.muya.options.renderPlantUml) {
            render.parse(code).insertImgElement(target, this.muya.options.plantUmlServer)
          } else {
            // Off (the default): nothing goes to plantuml.com; the block says so (assets/styles/index.css).
            target.classList.add('ag-plantuml-off')
          }
        } else if (functionType === 'vega-lite') {
          await this.diagramTimeout(render(key, JSON.parse(code), options))
        }
      } catch (err) {
        console.log(`diagram: a ${functionType} diagram could not be drawn`, err)
        target.innerHTML = `< Invalid ${NAMES[functionType] || functionType} Codes >`
        target.classList.add(CLASS_OR_ID.AG_MATH_ERROR)
      }
    }))
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
    // The fraction of a pixel the document before was moved by (keepInPlace): it stays after a drawing (taken away, the
    // text would move by it once more), not into a new one.
    rootDom.style.removeProperty('--td-room-fraction')
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
    // Laid out for real until the whole document is in, not as the 50 px a long document's block off screen stands for
    // (content-visibility, assets/styles/index.css): the page is still at the top when the place to go back to is
    // measured, and the blocks around it, sized for real only once scrolled to, came out taller than that in the
    // very frame they were first drawn - the page was drawn 76 px off before it was put right (JU01).
    // Through the attribute, not element.style: a style changed through the CSSOM and then removed came back as an
    // empty style="" when the page was read as HTML (a kept document then differed from a new build of it).
    const pinned = Array.from(rootDom.children, element => [element, element.getAttribute('style')])
    for (const [element, was] of pinned) element.setAttribute('style', (was ? was.replace(/;?\s*$/, '; ') : '') + 'content-visibility: visible')
    const state = { blocks, activeBlocks, matches, lo, hi, root: rootDom, timer: null, done, slices: 0, startedAt: performance.now(), pinned, heightsAt: 0, heightsPlan: null, heightsGeneration: -1, ruleQueue: [] }
    this.progressive = state
    // Loads pretext if not yet. What the predictions cost and when they were all in, for the checks (EditorBench
    // height-jump-check.js) and DevTools.
    this.heights.ready()
    window.__typedownPretextLog = { blocks: blocks.length, prepareMs: 0 }
    this.holdAnchoring(true)
    if (lo === 0 && hi === blocks.length) return this.finishProgressive(state)
    // Nothing predicted while it is drawn: the room and the size rules are the guess from the blocks' text
    // (predictHeights quick and fit), the predictions come once it is drawn (ruleRest), those around the window first.
    // Predicted before and while it is drawn - even 8 ms before the first slice and the page's idle moments between
    // slices - the first predictions' warming up (reading styles, pretext's first measurements) made a 100k document take
    // twice as long to draw on Windows.
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
      // Waiting for the frame the drawing began in to be painted (Muya.afterThisFrame): before this slice, not after it.
      if (state.beforeFirstSlice) {
        const hooks = state.beforeFirstSlice
        state.beforeFirstSlice = null
        for (const hook of hooks) {
          try {
            hook()
          } catch (err) {
            console.log(err)
          }
        }
      }
      const budget = window.__typedownProgressiveSliceMs ?? sliceBudget()
      const sliceStarted = performance.now()
      const drawnBefore = state.hi - state.lo
      const until = sliceStarted + budget
      // The window not wholly within what is drawn (the page put by a script where the rest is still to come, or the
      // drawing near its edge): the blocks drawn into it are laid out for real in this slice (pinInView), and the window
      // held still by a block in it, not by the first block drawn (keepInPlace).
      const near = this.windowUncovered(state)
      const keep = this.keepInPlace(state, near ? this.blockInWindow(state.root) : null)
      // A small share of the slice predicts the heights of what is drawn next (predictHeights.js; the rest of them in
      // the page's idle time): predicting them all first, in slices of their own, made 300k characters take 2.7 times as
      // long to draw on Windows.
      this.heightsEnvironment(state)
      const share = window.__typedownHeightsShare ?? HEIGHTS_SLICE_SHARE
      if (share > 0) this.prepareHeights(state, performance.now() + budget * share)
      // A few blocks at a time, the clock read after each few: twenty code blocks of forty lines each, highlighted,
      // took half a second, and no frame was drawn in it.
      do {
        if (state.hi < blocks.length) this.drawMore(state, 'below', 4)
        if (state.lo > 0) this.drawMore(state, 'above', 4)
      } while ((state.lo > 0 || state.hi < blocks.length) && performance.now() < until)
      this.flushHeightRules(state)
      if (near) this.pinInView(state)
      // Once for the whole slice, not once per few blocks (drawMore does not call these itself - see its comment).
      this.renderMermaid()
      this.renderDiagram()
      this.codeCache.clear()
      // The room for what is left is only written every so often, not every slice: writing it resizes a pseudo-element
      // that can be thousands of pixels tall on a live, visible, scrolling page, and on a document of many small blocks
      // (a reader's pasted page) that forced a full-page reflow on every slice and took ten times as long to draw as
      // the whole document at once - the estimate itself (read-only) costs nothing like that.
      // Where the heights are predicted or guessed from predictions (predictHeights.js) the room is written every slice,
      // worked out from them with nothing on the page measured: the blocks just drawn stand at the size given them (off
      // screen, the long-document rule) and the room shrinks by as much in the same frame - written only every 100 ms,
      // the page grew by what a few slices drew and shrank back, the scroll bar's thumb pulsing.
      const now = performance.now()
      const writeRoom = state.lo === 0 && state.hi === blocks.length || now - (state.roomWrittenAt ?? -Infinity) >= 100
      if (writeRoom) state.roomWrittenAt = now
      // With the heights predicted or guessed from the blocks' text, nothing on the page is read for it at all - not even
      // the margins at the edges (as read when the drawing began): read after a slice's blocks went in, they were styled
      // there and then, inside the slice, and on Windows the slices drew half as many blocks each.
      if (state.heightsEnv && state.guess) this.estimateRest(state, true, false, false)
      else this.estimateRest(state, writeRoom)
      keep()
      // How many blocks each slice draws, and in how long (window.__typedownPretextStats: 'slice'), either way.
      account('slice', sliceStarted)
      account('sliceBlocks', performance.now(), state.hi - state.lo - drawnBefore)
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
  estimateRest(state, write = true, measure = true, edges = measure) {
    // Measuring is a layout of the page: not done for an estimate that would not be written (most slices), nor where the
    // rest is all predicted (`measure` false: the per-character guess as last measured, for what cannot be predicted).
    if (!write) return
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
    if (measure || !state.guess) {
      const drawnHeight = last.getBoundingClientRect().bottom - first.getBoundingClientRect().top
      let drawnChars = 0
      for (let i = state.lo; i < state.hi; i++) drawnChars += state.chars[i]
      // A block has a height of its own (margins) besides its text's: half of it per block, the other half per character.
      state.guess = { perBlock: drawnHeight / (state.hi - state.lo) / 2, perChar: drawnChars > 0 ? drawnHeight / 2 / drawnChars : 0, drawnHeight }
    }
    const { perBlock, perChar } = state.guess
    const room = (from, to) => {
      let h = 0
      for (let i = from; i < to; i++) h += perBlock + state.chars[i] * perChar
      return Math.round(h)
    }
    let above = null
    let below = null
    // Where pretext has laid a block out (predictHeights.js), its predicted height; the per-character guess for the
    // rest, and for all of it where pretext cannot be used (not loaded, fonts still loading, turned off by a test).
    const env = measure ? this.heightsEnvironment(state) : state.heightsEnv
    if (env) {
      try {
        const r = this.heights.room(state, env, i => perBlock + state.chars[i] * perChar, edges)
        above = Math.round(r.above)
        below = Math.round(r.below)
        state.heightsCounted = r.counted
      } catch (err) {
        console.log(err)
      }
    }
    if (above === null) above = room(0, state.lo)
    if (below === null) below = room(state.hi, blocks.length)
    if (write) {
      root.style.setProperty('--td-room-above', `${above}px`)
      root.style.setProperty('--td-room-below', `${below}px`)
    }
  }

  /**
   * The environment heights are predicted for now (predictHeights.js), or null where they are not predicted (pretext
   * not loaded or turned off, fonts still loading). Its width or styles changed since the last time: the predictions
   * start over, and the size rules already written for blocks drawn off screen are written again.
   */
  heightsEnvironment(state) {
    state.heightsEnv = null
    if (!this.heights.ready()) return null
    let env
    try {
      env = this.heights.environment(state.root)
    } catch (err) {
      return null
    }
    // Every document drawn a part at a time (from 10,000 characters); the size rules for blocks drawn off screen only
    // past the long-document size (#editor.ag-long-document), where blocks off screen are not laid out.
    if (!(env.width > 0)) return null
    // For drawMore, which must not read the page's layout itself (after every few blocks: a layout of the whole page each).
    state.heightsEnv = env
    if (state.heightsGeneration !== env.generation) {
      state.heightsGeneration = env.generation
      state.heightsAt = 0
    }
    return env
  }

  /**
   * Predicts the heights of blocks not drawn yet until `until` (performance.now()), or for `ms` from when the page's
   * environment is read: in the order predictHeights' plan gives (what is drawn next first, a sample spread over the
   * document for the guess of the rest, then outwards as the drawing goes).
   */
  prepareHeights(state, until, ms) {
    const env = this.heightsEnvironment(state)
    if (!env) return
    if (ms) until = performance.now() + ms
    const { blocks } = state
    const plan = state.heightsPlan || (state.heightsPlan = this.heights.plan(state))
    const started = performance.now()
    let count = 0
    while (state.heightsAt < plan.length && performance.now() < until) {
      const i = plan[state.heightsAt++]
      if (i >= state.lo && i < state.hi) continue
      this.heights.box(blocks[i], env, true)
      count++
    }
    const log = window.__typedownPretextLog
    if (log && count) {
      log.prepareMs = Math.round((log.prepareMs + performance.now() - started) * 10) / 10
      log.predicted = (log.predicted || 0) + count
      log.stats = this.heights.stats
    }
  }

  // The size rules of the blocks a slice drew, written together: one change of the style sheet a slice.
  flushHeightRules(state) {
    if (!state.ruleQueue.length) return
    try {
      this.heights.addRules(state.ruleQueue)
    } catch (err) {
      console.log(err)
    }
    state.ruleQueue = []
  }

  clearRoom(state) {
    state.root.style.removeProperty('--td-room-above')
    state.root.style.removeProperty('--td-room-below')
    if (state.pinned) for (const [element, was] of state.pinned) {
      if (was === null) element.removeAttribute('style')
      else element.setAttribute('style', was)
    }
    state.pinned = null
    state.pinnedSet = null
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
      this.pinOnScroll(state)
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
    // In a document past the long-document size, a block drawn off screen is not laid out (content-visibility): it
    // stands at the size predicted or guessed for it (predictHeights.js), not at the style sheet's 50 px: queued here,
    // written with the slice's others before the page is next laid out (flushHeightRules). The environment as the slice
    // found it (heightsEnvironment: drawMore must not read the page's layout).
    const env = state.heightsEnv
    if (env && env.contained) {
      const from = side === 'below' ? state.hi : Math.max(0, state.lo - count)
      const to = side === 'below' ? Math.min(blocks.length, state.hi + count) : state.lo
      // Predicted where it is, else the guess from the predictions so far (refined once drawn): nothing predicted here.
      try {
        for (let i = from; i < to; i++) {
          const text = this.heights.ruleText(blocks[i], env, state, i)
          if (text) state.ruleQueue.push(text)
        }
      } catch (err) {
        console.log(err)
      }
    }
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
    // Not here: Mermaid and the other diagram renderers are stateful libraries, not meant to be called again before
    // the call before them is done (Mermaid's own mermaidRenderQueue says so) - called after every few blocks, as a
    // slice draws many, they overlapped and a sequence or vega-lite diagram some slice away was left empty or stuck
    // on "Loading…" forever, the overlapping call's library having moved its state on under it. One call after the
    // whole slice (the step, or completeRender) is both correct and far cheaper.
  }

  finishProgressive(state) {
    // The blocks drawn first lose their pin (content-visibility visible) and are laid out as the rest from here on - on
    // their own, to whole pixels past the long-document size: what is in the window is held where it is meanwhile
    // (vm103: the text moved by a pixel as the drawing ended).
    const keep = state.root.isConnected ? this.keepInPlace(state, this.blockAtTop(state.root)) : () => {}
    this.clearRoom(state)
    keep()
    // Blocks drawn without a predicted size (drawn at once, or before fonts or pretext were there), or with one for
    // another width or other styles: theirs in idle time, now that the browser's scroll anchoring keeps the page still
    // as they change size off screen.
    if (HeightPredictor.enabled()) {
      try {
        this.heights.ruleRest(state.blocks, state.root)
      } catch (err) {
        console.log(err)
      }
    }
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
    this.heightsEnvironment(state)
    if (state.hi < state.blocks.length) this.drawMore(state, 'below', state.blocks.length - state.hi)
    if (state.lo > 0) this.drawMore(state, 'above', state.lo)
    this.flushHeightRules(state)
    this.renderMermaid()
    this.renderDiagram()
    this.codeCache.clear()
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
  // The top-level block at the top of the window (the first whose bottom is in it), or null.
  blockAtTop(root) {
    const kids = root.children
    let lo = 0
    let hi = kids.length - 1
    if (hi < 0) return null
    while (lo < hi) {
      const mid = (lo + hi) >> 1
      if (kids[mid].getBoundingClientRect().bottom <= 0) lo = mid + 1
      else hi = mid
    }
    return kids[lo]
  }

  /**
   * Whether the window, and a window's height above and below it, is not wholly within the part drawn so far. Read at
   * the start of a slice, before anything goes in (the page laid out already: no layout of its own); the edge drawn to
   * last is all it reads.
   */
  windowUncovered(state) {
    const { root, blocks } = state
    // Only past the long-document size are blocks off screen not laid out (content-visibility): below it nothing to do.
    if (!isLongDocument()) return false
    const h = window.innerHeight
    if (state.lo > 0) {
      const first = root.firstElementChild
      if (first && first.getBoundingClientRect().top > -h) return true
    }
    if (state.hi < blocks.length) {
      const last = root.lastElementChild
      if (last && last.getBoundingClientRect().bottom < 2 * h) return true
    }
    return false
  }

  // The top-level block at the top of the window if one is drawn there, or null.
  blockInWindow(root) {
    const el = this.blockAtTop(root)
    if (!el) return null
    const r = el.getBoundingClientRect()
    return r.bottom > 0 && r.top < window.innerHeight ? el : null
  }

  /**
   * The page scrolled while a document is drawn (the reader, the load putting it back, a script): the blocks it brought
   * into the window pinned at once (pinInView), the window held by a block in it as they change size. Drawn off screen
   * and scrolled to, a block stood at the size given it off screen for seconds on w10 before it was laid out, and the
   * text below it moved then (JT01: 140 px, a document of HTML blocks scrolled to its middle while drawn).
   */
  pinOnScroll(state) {
    if (!isLongDocument()) return
    const anchor = this.blockInWindow(state.root)
    const keep = anchor ? this.keepInPlace(state, anchor) : null
    if (this.pinInView(state) && keep) keep()
  }

  /**
   * The blocks in the window and a window's height around it laid out for real now (pinned, content-visibility
   * visible, until the whole document is drawn - as the part drawn first): past the long-document size a block drawn
   * there was laid out at the size given it off screen in the frame it came, and at its own only a frame or more later,
   * moving the text below it (JT01, w10: 395 px in a document of HTML blocks put at a place not drawn yet).
   */
  pinInView(state) {
    const kids = state.root.children
    if (!kids.length) return 0
    const h = window.innerHeight
    if (!state.pinned) state.pinned = []
    if (!state.pinnedSet) state.pinnedSet = new WeakSet(state.pinned.map(([element]) => element))
    let count = 0
    // Again while what was pinned brings more in: a block of HTML guessed at 181 px and laid out at 24 pulled the
    // blocks after it into the window, at their own guessed sizes (w10).
    for (let pass = 0; pass < 3; pass++) {
      let lo = 0
      let hi = kids.length - 1
      while (lo < hi) {
        const mid = (lo + hi) >> 1
        if (kids[mid].getBoundingClientRect().bottom <= -h) lo = mid + 1
        else hi = mid
      }
      // All read first, then all written: one layout a pass.
      const pin = []
      for (let k = lo; k < kids.length; k++) {
        const element = kids[k]
        if (element.getBoundingClientRect().top >= h * 2) break
        if (!state.pinnedSet.has(element)) pin.push(element)
      }
      if (!pin.length) break
      for (const element of pin) {
        const was = element.getAttribute('style')
        state.pinned.push([element, was])
        state.pinnedSet.add(element)
        element.setAttribute('style', (was ? was.replace(/;?\s*$/, '; ') : '') + 'content-visibility: visible')
      }
      count += pin.length
    }
    return count
  }

  keepInPlace(state, anchor = null) {
    const first = anchor || (state.lo > 0 ? state.root.firstElementChild : null)
    if (!first) return () => {}
    const before = first.getBoundingClientRect().top
    return () => {
      if (!first.isConnected) return
      // The page scrolls by whole pixels and the blocks' heights have fractions. What a scroll leaves over goes into a
      // padding above the blocks (--td-room-fraction, between 0 and 1 px), so the text in the window ends every slice
      // exactly where it was: carried to the next slice instead (JU01: dozens of slices added up to pixels), it sat up to
      // half a pixel off and back again, slice after slice - on Windows the text shook (JT01: 19 moves of 0.7-0.9 px).
      // The height above (--td-room-above) cannot take it: past the long-document size it is laid out to whole pixels.
      const root = state.root
      const fraction = parseFloat(root.style.getPropertyValue('--td-room-fraction')) || 0
      const moved = first.getBoundingClientRect().top - before
      const from = window.scrollY
      const by = Math.ceil(moved - fraction)
      if (by) window.scrollBy(0, by)
      let next = fraction - moved + (window.scrollY - from)
      // A scroll that did not go as far as asked (the end of the page): the rest stays as it is.
      if (next < 0 || next >= 2) next = fraction
      if (Math.abs(next - fraction) > 0.0005) root.style.setProperty('--td-room-fraction', `${Math.round(next * 10000) / 10000}px`)
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
