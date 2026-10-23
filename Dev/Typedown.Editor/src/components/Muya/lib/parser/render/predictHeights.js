import { CLASS_OR_ID } from '../../config'
import { tokenizer } from '../'

/**
 * How tall a top-level block will be once drawn, before it is: for a long document drawn a part at a time
 * (StateRender.renderProgressive), the blocks not drawn yet stand in as room above and below what is (estimateRest), and
 * in a document past the long-document size a block drawn off screen stands at a size of its own until it is laid out
 * (contain-intrinsic-size, assets/styles/index.css). Guessed per character, those were off by tens of per cent and the
 * scroll bar jumped as the real blocks came in; laid out with pretext (github.com/chenglou/pretext, the browser's line
 * breaking redone over canvas measurements) they are within a pixel or so for text.
 *
 * Built from the block tree, not from elements (the blocks it is for have none yet): each block is mirrored as the
 * renderer would draw it - its elements' tags and classes, and for a line of text the renderer's own inline vnodes -
 * and every element's style is read once from a sample element of the same kind (the same tags and classes under the
 * same ancestors) in a hidden copy of the root, so the theme, an edition's CSS and the reader's settings all count as
 * they do on the page. The text of each element with inline content goes to pretext in its font; the boxes stack as
 * CSS stacks them (margins collapsing between blocks and through empty ones, a code block being inline-flex, a table's
 * columns shared out as an automatic table layout shares them).
 *
 * What it cannot size - pictures, diagrams, display maths, HTML blocks, front matter, emoji, inline maths, raw HTML with
 * attributes - it says nothing about (null), and the per-character guess stands for those. Nothing here throws to the
 * caller: any failure is the same null.
 */

// Where the time goes, per phase (ms and how many), for checks in the real app (Tools/AutomationE2E HJ01/HJ02 read it).
const stats = window.__typedownPretextStats = window.__typedownPretextStats || {}
const account = (name, started, count = 1) => {
  const s = stats[name] || (stats[name] = { ms: 0, n: 0 })
  s.ms = Math.round((s.ms + performance.now() - started) * 10) / 10
  s.n += count
}

// pretext, loaded on first use (it is only for long documents). Null until loaded, or if it failed to load.
let pretext = null
let pretextLoading = null
const loadPretext = () => {
  if (!pretextLoading) {
    pretextLoading = Promise.all([import('@chenglou/pretext/rich-inline'), import('@chenglou/pretext')])
      .then(([rich, layout]) => {
        pretext = { prepareRichInline: rich.prepareRichInline, measureRichInlineStats: rich.measureRichInlineStats, clearCache: layout.clearCache }
      })
      .catch(err => {
        console.log('pretext not available', err)
      })
  }
  return pretextLoading
}

// Inline tokens drawn as text whose height pretext can tell; any other (a picture, maths, an emoji drawn as a picture)
// leaves the block to the per-character guess.
const TEXT_TOKENS = new Set([
  'text', 'strong', 'em', 'del', 'mark', 'inline_code', 'link', 'auto_link', 'auto_link_extension', 'reference_link',
  'backlash', 'html_escape', 'hard_line_break', 'soft_line_break', 'header', 'tail_header', 'hr', 'footnote_identifier',
  'super_sub_script', 'html_tag'
])
// Raw inline HTML drawn as text: these tags, without attributes (a style or class could make them anything).
const TEXT_HTML_TAGS = new Set(['kbd', 'sup', 'sub', 'b', 'i', 'u', 's', 'em', 'strong', 'span', 'mark', 'small', 'code', 'br', 'del', 'ins', 'abbr', 'q', 'cite', 'var', 'samp', 'tt', 'strike'])
// Leaf spans whose text the renderer tokenizes and draws as inline vnodes.
const TEXT_LEAVES = new Set(['paragraphContent', 'atxLine', 'cellContent', 'thematicBreakLine'])
const PRE_CLASS = { fencecode: CLASS_OR_ID.AG_FENCE_CODE, indentcode: CLASS_OR_ID.AG_INDENT_CODE }

const tokensAreText = tokens => {
  for (const token of tokens) {
    if (!TEXT_TOKENS.has(token.type)) return false
    if (token.type === 'html_tag') {
      if (!token.tag || !TEXT_HTML_TAGS.has(token.tag.toLowerCase())) return false
      if (token.attrs && Object.keys(token.attrs).length) return false
    }
    if (Array.isArray(token.children) && token.children.length && !tokensAreText(token.children)) return false
  }
  return true
}

const hasReference = tokens => tokens.some(t => /reference_image|reference_link/.test(t.type) || (Array.isArray(t.children) && hasReference(t.children)))

const px = v => parseFloat(v) || 0
const collapseList = list => {
  let pos = 0
  let neg = 0
  for (const m of list) {
    if (m > pos) pos = m
    if (m < neg) neg = m
  }
  return pos + neg
}

// tag#id.class.class -> { tag, cls }
const parseSel = sel => {
  const m = /^([a-zA-Z][a-zA-Z0-9-]*)/.exec(sel || '')
  const tag = m ? m[1].toLowerCase() : 'span'
  const cls = []
  const re = /\.([^.#]+)/g
  let c
  while ((c = re.exec(sel))) cls.push(c[1])
  return { tag, cls }
}

// What an element is for its style: its tag, classes and the attributes the style sheets select on; for the elements a
// block is made of also whether it is its parent's first or last element (ul:last-child has no bottom margin).
const descKey = d => `${d.tag}${d.cls.length ? '.' + d.cls.slice().sort().join('.') : ''}${d.attrs ? Object.keys(d.attrs).sort().map(k => `[${k}=${d.attrs[k]}]`).join('') : ''}${d.first ? ':f' : ''}${d.last ? ':l' : ''}`

// One element's computed style, as much of it as the layout here uses.
function readStyle(s, el) {
  const pseudo = which => {
    const p = getComputedStyle(el, which)
    if (p.display === 'none' || p.position === 'absolute' || p.position === 'fixed') return null
    const m = /^"([\s\S]*)"$/.exec(p.content)
    return m ? { text: m[1] || '​', font: fontOf(p) } : null
  }
  const lineHeight = s.lineHeight === 'normal' ? px(s.fontSize) * 1.2 : px(s.lineHeight)
  return {
    display: s.display,
    out: s.display === 'none' || s.position === 'absolute' || s.position === 'fixed' || (s.float && s.float !== 'none'),
    ws: s.whiteSpace,
    font: fontOf(s),
    lh: lineHeight,
    mt: px(s.marginTop),
    mb: px(s.marginBottom),
    ml: px(s.marginLeft),
    mr: px(s.marginRight),
    pt: px(s.paddingTop),
    pb: px(s.paddingBottom),
    pl: px(s.paddingLeft),
    pr: px(s.paddingRight),
    bt: px(s.borderTopWidth),
    bb: px(s.borderBottomWidth),
    bl: px(s.borderLeftWidth),
    br: px(s.borderRightWidth),
    minW: /px$/.test(s.minWidth) ? px(s.minWidth) : 0,
    minH: /px$/.test(s.minHeight) ? px(s.minHeight) : 0,
    zeroWidth: px(s.width) === 0 && s.overflowX !== 'visible',
    bfc: s.overflowX !== 'visible' || s.overflowY !== 'visible' || /^(flow-root|inline-block|inline-flex|flex|inline-grid|grid|table-cell|table|inline-table)$/.test(s.display),
    collapse: s.borderCollapse === 'collapse',
    spacing: px(s.borderSpacing),
    before: pseudo('::before'),
    after: pseudo('::after')
  }
}
const fontOf = s => `${s.fontStyle} ${s.fontWeight} ${s.fontSize} ${s.fontFamily}`

export default class HeightPredictor {
  constructor(render) {
    this.render = render
    // Per style generation: an element kind's style (descKey path -> readStyle), and each block's layout tree.
    this.styles = new Map()
    this.trees = new WeakMap()
    this.generation = 0
    // pretext's prepared paragraphs, by their text and fonts: a document drawn again (a reload, a tab put back) does
    // not prepare them again. Bounded: the oldest go first.
    this.prepared = new Map()
    this.canvas = null
    this.watching = false
    // The rules giving a long document's blocks drawn off screen their predicted size (contain-intrinsic-size), in
    // style elements of a few hundred rules each; the blocks they are for, to write them again at another width.
    this.sheets = []
    this.ruled = new Map()
    this.resting = null
    this.fontsLoading = 0
    // Each element kind's chain of sample elements (resolve), to read its style again (checkStyles).
    this.chains = new Map()
    this.fontsSeen = new Set()
    this.families = new Set()
    // pretext loaded once the editor is there, not when the first long document comes (its first estimate can use it).
    if (HeightPredictor.enabled()) loadPretext()
    this.stats = { prepared: 0, unsupported: 0, prepareMs: 0, chars: 0, failed: 0 }
  }

  static enabled() {
    return !window.__typedownPretextOff
  }

  /** Whether predictions can be made now: pretext is loaded and the page's fonts are. Starts loading it otherwise. */
  ready() {
    if (!HeightPredictor.enabled()) return false
    if (!pretext) {
      loadPretext()
      return false
    }
    this.watch()
    // Not while a font the blocks are drawn in is still coming (resolve asks for those it meets).
    return this.fontsLoading === 0
  }

  // The styles read are good until the page's style changes: a theme, an edition's CSS, the reader's CSS, the font
  // settings (style elements in the head, properties on the body), or a web font arriving.
  watch() {
    if (this.watching) return
    this.watching = true
    try {
      // Style elements coming and going in the head, the body's and the page's own style and classes: the styles read so
      // far are read again, and only if one of them changed do the predictions start over (a library putting its own
      // style element in, the scroll anchoring held off while a document is drawn, changes none of them).
      const ours = node => node.nodeType === 1 && node.hasAttribute && node.hasAttribute('data-td-heights')
      new MutationObserver(records => {
        if (records.some(r => !(r.type === 'childList' && [...r.addedNodes, ...r.removedNodes].every(ours)))) this.checkStyles()
      }).observe(document.head, { childList: true, subtree: true, characterData: true, attributes: true })
      const attrs = new MutationObserver(() => this.checkStyles())
      for (const el of [document.documentElement, document.body]) {
        if (el) attrs.observe(el, { attributes: true, attributeFilter: ['style', 'class'] })
      }
      if (document.fonts) {
        // A font the blocks are drawn in arriving: everything measured in its stand-in is measured again.
        document.fonts.addEventListener('loadingdone', e => {
          const faces = e.fontfaces || []
          if (faces.some(f => this.families.has(String(f.family).replace(/^["']|["']$/g, '').toLowerCase()))) this.fontsArrived()
        })
      }
    } catch (err) {
      console.log(err)
    }
  }

  // The styles read so far read again (in a moment, once for all the changes of the moment); any different, all start over.
  checkStyles() {
    if (this.checking) return
    this.checking = true
    Promise.resolve().then(() => {
      this.checking = false
      const root = this.observed
      if (!this.styles.size || !root || !root.isConnected || !root.parentNode) return
      const before = new Map(this.styles)
      const width = this.rootWidth || root.clientWidth || 800
      this.styles.clear()
      try {
        this.resolve([...this.chains.entries()].filter(([key]) => before.has(key)).map(([key, chain]) => ({ key, chain })), { root, width })
      } catch (err) {
        this.restyled()
        return
      }
      for (const [key, rec] of before) {
        const now = this.styles.get(key)
        if (!now || JSON.stringify(now) !== JSON.stringify(rec)) {
          this.restyled()
          return
        }
      }
    })
  }

  restyled() {
    this.generation++
    this.styles.clear()
    this.reruleSoon()
  }

  // A document drawn and kept at its predicted sizes (ruleRest): those rules again for the page as it is now, a moment
  // after the styles or the width changed - not while a document is drawn (its own drawing rules what it draws).
  reruleSoon() {
    const last = this.lastRuled
    if (!last) return
    clearTimeout(this.reruleTimer)
    this.reruleTimer = setTimeout(() => {
      if (this.render.progressive || !last.root.isConnected) return
      this.ruleRest(last.blocks, last.root)
    }, 300)
  }

  fontsArrived() {
    if (pretext) pretext.clearCache()
    this.prepared.clear()
    this.restyled()
  }

  // The fonts of styles just read: those not loaded yet are asked for, and nothing is predicted until they are here.
  loadFonts(fonts) {
    if (!document.fonts) return
    for (const font of fonts) {
      if (this.fontsSeen.has(font)) continue
      this.fontsSeen.add(font)
      const family = /\d(?:px|pt|em|rem|%)\s+(.*)$/.exec(font)
      if (family) for (const f of family[1].split(',')) this.families.add(f.trim().replace(/^["']|["']$/g, '').toLowerCase())
      const sample = 'Aa1 \u4e2d\u3042\ud55c'
      let missing = false
      try {
        missing = !document.fonts.check(font, sample)
      } catch (err) {
        missing = false
      }
      if (!missing) continue
      this.fontsLoading++
      // Not waited for past a few seconds: a load that never ends must not keep the predictions off for good.
      let over = false
      const done = () => {
        if (over) return
        over = true
        this.fontsLoading--
        this.fontsArrived()
      }
      setTimeout(done, 3000)
      document.fonts.load(font, sample).catch(() => {}).then(done)
    }
  }

  /**
   * The page as layouts are made for it now: the root's content width and whether its blocks are laid out contained
   * (content-visibility: auto, the long-document rule: a block's children's margins stay inside it).
   */
  environment(root) {
    // The root's content width as the last layout left it (a ResizeObserver's): reading it from the root would lay the
    // page out, on every slice of a drawing.
    if (this.observed !== root) {
      this.observed = root
      this.rootWidth = null
      try {
        if (!this.sizes) this.sizes = new ResizeObserver(entries => {
          for (const e of entries) {
            if (e.target !== this.observed) continue
            const was = this.rootWidth
            this.rootWidth = e.contentRect.width
            if (was !== null && was !== undefined && was !== this.rootWidth) this.reruleSoon()
          }
        })
        this.sizes.disconnect()
        this.sizes.observe(root)
      } catch (err) {
        this.sizes = null
      }
    }
    let width = this.rootWidth
    if (width === null || width === undefined) {
      const rs = getComputedStyle(root)
      width = root.clientWidth - px(rs.paddingLeft) - px(rs.paddingRight)
    }
    const editor = document.getElementById('editor')
    const contained = !!(editor && editor.classList.contains('ag-long-document'))
    // The reader's font settings are on an element above the root (components/Muya/index.tsx), seen by nothing above:
    // the root's own font, read each time, tells.
    const s = getComputedStyle(root)
    const font = `${fontOf(s)}/${s.lineHeight}/${s.letterSpacing}`
    if (this.rootFont !== undefined && this.rootFont !== font) this.restyled()
    this.rootFont = font
    return { width, contained, generation: this.generation, root }
  }

  /** A block's layout tree for this style generation: built (and its text prepared) the first time it is asked for. */
  tree(block, env) {
    let entry = this.trees.get(block)
    if (entry && entry.generation === env.generation) return entry.tree
    const started = performance.now()
    let tree = null
    try {
      const node = this.mirror(block, true)
      if (node) tree = this.build(node, [], env)
    } catch (err) {
      tree = null
      this.stats.failed++
      if (this.stats.failed <= 3) console.log('height prediction failed', err)
    }
    entry = { generation: env.generation, tree, memo: null }
    this.trees.set(block, entry)
    account('predict', started)
    if (tree) this.stats.prepared++
    else this.stats.unsupported++
    this.stats.prepareMs += performance.now() - started
    return tree
  }

  /** Whether the block's tree is built for this generation (a prediction is at hand without work). */
  has(block, env) {
    const entry = this.trees.get(block)
    return !!entry && entry.generation === env.generation
  }

  /**
   * The block's predicted box at the environment's width: { h, mt, mb, inline, through, hC, ptb } (h: border box laid
   * out uncontained, hC: contained, ptb: its vertical padding and border), or null where it cannot be told or is not
   * prepared yet (`build`: prepare it now).
   */
  box(block, env, build = false) {
    if (!build && !this.has(block, env)) return null
    const tree = this.tree(block, env)
    if (!tree) return null
    const entry = this.trees.get(block)
    if (entry.memo && entry.memo.width === env.width) return entry.memo.box
    let box = null
    try {
      const open = this.layout(tree, env.width, false)
      const shut = this.layout(tree, env.width, true)
      if (open && shut) {
        const r = tree.rec
        // An inline-level block laid out contained has no baseline of its own: it stands on the line's baseline by the
        // bottom of its margin box, and the line goes on below that by the root's strut (a code block, inline-flex).
        const below = shut.inline ? this.strut(env) : 0
        box = { h: open.h, mt: open.mt, mb: open.mb, inline: open.inline, through: open.through, hC: shut.h, mtC: shut.mt, mbC: shut.mb + below, ptb: r.pt + r.pb + r.bt + r.bb }
      }
    } catch (err) {
      box = null
      this.stats.failed++
      if (this.stats.failed <= 3) console.log('height layout failed', err)
    }
    entry.memo = { width: env.width, box }
    return box
  }

  /**
   * Reads the styles of the usual kinds of block once the page is idle: the fonts they use (bold, italic, code, headings)
   * are asked for then, not when the first long document is opened and its heights wait for them.
   */
  warm(root) {
    if (this.warmed || !HeightPredictor.enabled() || !root) return
    this.warmed = true
    const idle = window.requestIdleCallback ? cb => window.requestIdleCallback(cb, { timeout: 3000 }) : cb => setTimeout(cb, 500)
    idle(() => {
      try {
        if (!root.isConnected || !root.parentNode) {
          this.warmed = false
          return
        }
        const env = { root, width: root.clientWidth || 800, generation: this.generation }
        const d = (tag, cls, first = true, last = true) => ({ tag, cls, attrs: null, first, last })
        const para = [d('p', [CLASS_OR_ID.AG_PARAGRAPH], false, true), d('span', [CLASS_OR_ID.AG_PARAGRAPH, 'ag-paragraph-content'])]
        const chains = [
          [...para, d('strong', [CLASS_OR_ID.AG_INLINE_RULE])],
          [...para, d('em', [CLASS_OR_ID.AG_INLINE_RULE])],
          [...para, d('code', [CLASS_OR_ID.AG_INLINE_RULE])],
          [d('h2', [CLASS_OR_ID.AG_PARAGRAPH, 'atx'], false, true), d('span', [CLASS_OR_ID.AG_PARAGRAPH, 'ag-atx-line'])],
          [d('pre', [CLASS_OR_ID.AG_PARAGRAPH, PRE_CLASS.fencecode], false, true), d('code', [CLASS_OR_ID.AG_PARAGRAPH], false, true), d('span', [CLASS_OR_ID.AG_PARAGRAPH, 'ag-code-content'])],
          [d('figure', [CLASS_OR_ID.AG_PARAGRAPH], false, true), d('table', [CLASS_OR_ID.AG_PARAGRAPH], false, true), d('thead', [CLASS_OR_ID.AG_PARAGRAPH]), d('tr', [CLASS_OR_ID.AG_PARAGRAPH]), d('th', [CLASS_OR_ID.AG_PARAGRAPH]), d('span', [CLASS_OR_ID.AG_PARAGRAPH, 'ag-cell-content'])]
        ]
        this.resolve(chains.map(chain => ({ key: 'warm:' + chain.map(descKey).join('>'), chain })), env)
      } catch (err) {
        console.log(err)
      }
    })
  }

  // ---- the block as the renderer draws it ----

  /**
   * A mirror of the elements renderBlock makes for a block: { tag, cls, attrs, kids } for a container, with `vnode`
   * (the renderer's own, for a line of text) or `text` (a code block's) for a leaf; null for what cannot be sized.
   */
  mirror(block, top) {
    const { type, functionType } = block
    const container = Array.isArray(block.children) && block.children.length > 0
    if (container) {
      if (type === 'pre' && !PRE_CLASS[functionType]) return null
      if (type === 'figure' && functionType !== 'table') return null
      if (type === 'div') return null
      const { tag, cls } = parseSel(this.render.getSelector(block, []))
      const attrs = {}
      if (/^(pre|code)$/.test(type) && typeof block.lang === 'string' && block.lang) cls.push(`language-${block.lang.replace(/[#.]{1}/g, '')}`)
      if (/^h/.test(type)) {
        // A heading, and a thematic break (renderContainerBlock: /^h/).
        if (/^h\d$/.test(type)) {
          if (block.headingStyle) cls.push(block.headingStyle)
          attrs['data-head'] = type
        }
        attrs['data-role'] = type
      } else if (type === 'figure' && functionType) {
        attrs['data-role'] = functionType.toUpperCase()
      } else if (/^(ul|ol)$/.test(type) && block.listType) {
        cls.push(`ag-${block.listType}-list`)
      } else if (type === 'li' && block.listItemType) {
        attrs['data-marker'] = block.bulletMarkerOrDelimiter
        cls.push(CLASS_OR_ID.AG_LIST_ITEM, `ag-${block.listItemType}-list-item`, block.isLooseListItem ? CLASS_OR_ID.AG_LOOSE_LIST_ITEM : CLASS_OR_ID.AG_TIGHT_LIST_ITEM)
      } else if (type === 'pre') {
        attrs['data-role'] = functionType
        cls.push(PRE_CLASS[functionType])
      }
      const kids = []
      if (top) kids.push({ tag: 'a', cls: [CLASS_OR_ID.AG_FRONT_ICON], decor: true })
      if (type === 'pre') kids.push({ tag: 'a', cls: ['ag-code-copy'], decor: true })
      for (const child of block.children) {
        const kid = this.mirror(child, false)
        if (!kid) return null
        kids.push(kid)
      }
      return { tag, cls, attrs, kids }
    }
    if (type === 'div') return null
    if (type === 'span' && functionType === 'codeContent') return { tag: 'span', cls: [CLASS_OR_ID.AG_PARAGRAPH, 'ag-code-content'], text: block.text || '' }
    if (type === 'span' && functionType === 'languageInput') return { tag: 'span', cls: [CLASS_OR_ID.AG_PARAGRAPH, 'ag-language-input'], text: block.text || '' }
    if (type === 'span' && !TEXT_LEAVES.has(functionType)) return null
    if (type === 'span' && block.text) {
      const render = this.render
      let tokens = render.tokenCache.get(block.text)
      if (!tokens) {
        tokens = tokenizer(block.text, { highlights: [], hasBeginRules: /paragraphContent|atxLine/.test(functionType), labels: render.labels, options: render.muya.options })
        if (!tokensAreText(tokens)) return null
        // For renderLeafBlock below, and for drawing the block later (it draws from the same cache), as it would have
        // cached them itself - not where a reference's label could change what they are.
        if (!hasReference(tokens)) render.tokenCache.set(block.text, tokens)
      } else if (!tokensAreText(tokens)) return null
    }
    // A leaf as renderLeafBlock draws it (a text line's inline vnodes, a task's checkbox, an empty block).
    if (type !== 'span' && type !== 'input' && !/^(p|h\d|li|blockquote|th|td)$/.test(type)) return null
    const vnode = this.render.renderLeafBlock(null, block, [], [], true)
    const { tag, cls } = parseSel(vnode.sel)
    // A task's checkbox is styled by its type (input[type=checkbox]: out of the flow).
    const type_ = vnode.data && vnode.data.attrs && vnode.data.attrs.type
    return { tag, cls, attrs: type_ ? { type: type_ } : null, vnode, leaf: true }
  }

  // ---- styles ----

  /** How far a line of the root goes below its baseline (its strut: the root's font and line height). */
  strut(env) {
    if (this.strutFor && this.strutFor.generation === env.generation && this.strutFor.width === env.width) return this.strutFor.value
    const root = env.root
    const s = getComputedStyle(root)
    const probe = document.createElement(root.tagName)
    probe.id = root.id
    probe.className = root.className
    probe.setAttribute('aria-hidden', 'true')
    probe.setAttribute('data-td-heights', '')
    probe.style.cssText = `position:absolute;left:0;top:0;width:${env.width}px;height:0;overflow:hidden;visibility:hidden;contain:strict;pointer-events:none`
    const line = document.createElement('div')
    line.style.cssText = `display:block;content-visibility:visible;contain:none;margin:0;padding:0;border:0;height:auto;font:${s.fontStyle} ${s.fontWeight} ${s.fontSize}/${s.lineHeight} ${s.fontFamily}`
    const box = document.createElement('span')
    box.style.cssText = 'display:inline-block;contain:layout;vertical-align:baseline;width:1px;height:40px;margin:0;padding:0;border:0'
    line.appendChild(box)
    probe.appendChild(line)
    root.parentNode.insertBefore(probe, root.nextSibling)
    let value = 0
    try {
      value = Math.max(0, line.getBoundingClientRect().height - 40)
    } finally {
      probe.remove()
    }
    this.strutFor = { generation: env.generation, width: env.width, value }
    return value
  }

  /** The styles of the element kinds named (descKey paths), read from sample elements in a hidden copy of the root. */
  resolve(paths, env) {
    const missing = paths.filter(p => !this.styles.has(p.key))
    if (!missing.length) return
    const root = env.root
    const probe = document.createElement(root.tagName)
    probe.id = root.id
    probe.className = root.className
    probe.setAttribute('aria-hidden', 'true')
    probe.setAttribute('data-td-heights', '')
    probe.style.cssText = `position:absolute;left:0;top:0;width:${env.width}px;height:0;overflow:hidden;visibility:hidden;contain:strict;pointer-events:none`
    const made = []
    for (const p of missing) {
      // Each sample on its own chain of ancestors, each as its parent's first or last element as the real one is.
      let parent = probe
      let el = null
      for (const d of p.chain) {
        if (!d.first) parent.appendChild(document.createElement('template'))
        el = document.createElement(d.tag)
        if (d.cls.length) el.className = d.cls.join(' ')
        if (d.attrs) for (const k of Object.keys(d.attrs)) el.setAttribute(k, d.attrs[k])
        parent.appendChild(el)
        if (!d.last) parent.appendChild(document.createElement('template'))
        parent = el
      }
      made.push([p.key, el])
      this.chains.set(p.key, p.chain)
    }
    // Next to the root, so it inherits what the root does; out again at once (nothing else runs in between).
    const probed = performance.now()
    root.parentNode.insertBefore(probe, root.nextSibling)
    const fonts = new Set()
    try {
      for (const [key, el] of made) {
        const rec = readStyle(getComputedStyle(el), el)
        this.styles.set(key, rec)
        fonts.add(rec.font)
        if (rec.before) fonts.add(rec.before.font)
        if (rec.after) fonts.add(rec.after.font)
      }
    } finally {
      probe.remove()
    }
    account('styleProbe', probed, made.length)
    this.loadFonts(fonts)
  }

  /**
   * The mirrored block made a layout tree: every element with its style, every run of inline content prepared by
   * pretext. Null where something in it cannot be laid out here.
   */
  build(node, chain, env) {
    // The element kinds first, all read in one go.
    const paths = []
    const collect = (n, ch, inLeaf) => {
      const d = { tag: n.tag, cls: n.cls, attrs: n.attrs || null, first: !!n.first, last: !!n.last }
      const next = ch.concat([inLeaf ? { ...d, first: true, last: true } : d])
      const key = next.map(descKey).join('>')
      n.key = key
      paths.push({ key, chain: next })
      if (n.kids) {
        const flow = n.kids
        flow.forEach((k, i) => {
          k.first = i === 0
          k.last = i === flow.length - 1
          collect(k, next, false)
        })
      }
      if (n.vnode) collectVnodes(n.vnode, next)
    }
    const collectVnodes = (v, ch) => {
      if (!Array.isArray(v.children)) return
      for (const c of v.children) {
        if (!c || !c.sel) continue
        const { tag, cls } = parseSel(c.sel)
        const next = ch.concat([{ tag, cls, attrs: null, first: true, last: true }])
        c.__key = next.map(descKey).join('>')
        paths.push({ key: c.__key, chain: next })
        collectVnodes(c, next)
      }
    }
    collect(node, chain, false)
    this.resolve(paths, env)
    return this.treeOf(node)
  }

  treeOf(n) {
    const rec = this.styles.get(n.key)
    if (!rec) return null
    if (rec.out || n.decor) return { kind: 'out', rec }
    if (n.text !== undefined || n.vnode) return this.inlineOf(n, rec)
    // A table: rows of cells, for the column widths to be shared out.
    if (rec.display === 'table') return this.tableOf(n, rec)
    const kids = []
    for (const k of n.kids || []) {
      const t = this.treeOf(k)
      if (!t) return null
      if (t.kind !== 'out') kids.push(t)
    }
    return { kind: /flex$/.test(rec.display) ? 'flex' : 'block', rec, kids }
  }

  inlineOf(n, rec) {
    const items = []
    const nowrap = rec.ws === 'pre' || rec.ws === 'nowrap'
    const pre = /^(pre|pre-wrap|break-spaces|pre-line)$/.test(rec.ws)
    let ok = true
    if (n.text !== undefined) {
      if (n.text) items.push({ text: n.text, font: rec.font })
    } else {
      const v = n.vnode
      // Horizontal padding, border and margin of an inline element (inline code): on its first run.
      const walk = (v, parentRec, extra) => {
        if (typeof v.text === 'string' && !Array.isArray(v.children)) {
          if (v.text) push(v.text, parentRec.font, extra)
          return
        }
        if (!Array.isArray(v.children)) return
        for (const c of v.children) {
          if (!c) continue
          if (!c.sel) {
            if (c.text) push(c.text, parentRec.font, extra)
            continue
          }
          const r = this.styles.get(c.__key)
          if (!r) { ok = false; continue }
          if (r.out) continue
          const { tag } = parseSel(c.sel)
          if (tag === 'br') { push('\n', r.font, extra); continue }
          if (/^inline-/.test(r.display) && r.zeroWidth) {
            // A hidden marker: an empty inline-block, a place a line can break.
            items.push({ width: r.ml + r.mr })
            continue
          }
          if (/^(img|svg|canvas|video|iframe|input)$/.test(tag)) { ok = false; continue }
          const chrome = r.pl + r.pr + r.bl + r.br + r.ml + r.mr
          const sub = { w: extra.w + chrome }
          walk(c, r, sub)
          extra.w = sub.w
        }
      }
      const push = (text, font, extra) => {
        const last = items[items.length - 1]
        if (last && last.text !== undefined && last.font === font && !last.extraWidth && !extra.w) last.text += text
        else items.push(extra.w ? { text, font, extraWidth: extra.w } : { text, font })
        extra.w = 0
      }
      walk(v, rec, { w: 0 })
    }
    if (!ok) return null
    let lines = 0
    let prepared = null
    if (!items.length || items.every(i => i.width !== undefined)) {
      // Empty: one line where the element shows something when empty (a zero-width space after it), else none.
      lines = rec.before || rec.after ? 1 : 0
    } else {
      prepared = this.prepare(items, pre)
      if (!prepared) return null
      this.stats.chars += items.reduce((a, i) => a + (i.text ? i.text.length : 0), 0)
    }
    return { kind: 'inline', rec, prepared, lines, nowrap }
  }

  prepare(items, pre) {
    const key = (pre ? 'p' : 'n') + items.map(i => i.text !== undefined ? `${i.font}\u0001${i.extraWidth || 0}\u0001${i.text}` : `\u0002${i.width}`).join('\u0003')
    let p = this.prepared.get(key)
    if (p) return p
    p = pretext.prepareRichInline(items, pre ? { whiteSpace: 'pre-wrap' } : undefined)
    this.prepared.set(key, p)
    if (this.prepared.size > 30000) {
      const drop = this.prepared.keys()
      for (let k = 0; k < 5000; k++) this.prepared.delete(drop.next().value)
    }
    return p
  }

  tableOf(n, rec) {
    const rows = []
    const walkRows = m => {
      for (const k of m.kids || []) {
        const r = this.styles.get(k.key)
        if (!r) return false
        if (r.out || k.decor) continue
        if (r.display === 'table-row') {
          const cells = []
          for (const c of k.kids || []) {
            const cr = this.styles.get(c.key)
            if (!cr) return false
            if (cr.out || c.decor) continue
            const inner = (c.kids || []).map(x => this.treeOf(x)).filter(t => t && t.kind !== 'out')
            if (inner.length !== 1 || inner[0].kind !== 'inline') return false
            cells.push({ rec: cr, inner: inner[0], text: (c.kids || []).map(x => this.plainText(x)).join('') })
          }
          rows.push({ rec: r, cells })
        } else if (!walkRows(k)) return false
      }
      return true
    }
    if (!walkRows(n)) return null
    return { kind: 'table', rec, rows }
  }

  plainText(n) {
    if (n.text !== undefined) return n.text
    const out = []
    const walk = v => {
      if (!v) return
      if (typeof v.text === 'string' && !Array.isArray(v.children)) {
        const r = v.__key ? this.styles.get(v.__key) : null
        if (!r || !(r.zeroWidth || r.out)) out.push(v.text)
        return
      }
      if (v.__key) {
        const r = this.styles.get(v.__key)
        if (r && (r.zeroWidth || r.out)) return
      }
      for (const c of v.children || []) walk(c)
    }
    if (n.vnode) walk(n.vnode)
    return out.join('')
  }

  // ---- layout ----

  lineCount(t, width) {
    if (!t.prepared) return t.lines
    return pretext.measureRichInlineStats(t.prepared, t.nowrap ? 1e7 : Math.max(1, width)).lineCount
  }

  /**
   * A tree laid out in `width` (the room its margin box has): { h: its border box's height, mt, mb: its margins once
   * collapsed with its children's where they go through it, inline: inline-level (in a line of its own, nothing
   * collapsing), through: empty and open, so margins collapse through it }. `contained`: the block is laid out on its
   * own (layout containment), its children's margins inside it.
   */
  layout(t, width, contained = false) {
    const r = t.rec
    const inner = width - r.ml - r.mr - r.pl - r.pr - r.bl - r.br
    const vert = r.pt + r.pb + r.bt + r.bb
    const inline = /^inline/.test(r.display)
    let mt = r.mt
    let mb = r.mb
    let content = 0
    let through = false
    if (t.kind === 'inline') {
      content = this.lineCount(t, inner) * r.lh
    } else if (t.kind === 'table') {
      content = this.tableHeight(t, width - r.ml - r.mr)
    } else if (t.kind === 'flex') {
      // A flex row (the editor's code block is an inline-flex): as tall as its tallest item; nothing collapses.
      for (const k of t.kids) {
        const b = this.layout(k, inner, false)
        if (b) content = Math.max(content, b.mt + b.h + b.mb)
      }
    } else {
      const boxes = t.kids.map(k => this.layout(k, inner, false)).filter(Boolean)
      const bfc = contained || r.bfc
      const s = stack(boxes)
      const openTop = !bfc && r.pt === 0 && r.bt === 0
      const openBottom = !bfc && r.pb === 0 && r.bb === 0 && r.minH === 0
      if (!s.any) {
        // No box with a height: the margins of what is in it meet, and go through it where it is open.
        content = 0
        if (openTop && openBottom) {
          through = true
          mt = collapseList([mt, ...s.lead])
        }
      } else {
        // Positions are from the top of the content box with the first margins inside it; where they go through the
        // parent's top instead, its content starts at the first box (an inline one's line).
        const start = openTop ? s.firstTop - (s.firstInline ? s.firstMt : 0) : 0
        const end = openBottom ? s.end : s.end + collapseList(s.trail)
        content = end - start
        if (openTop) mt = collapseList([mt, ...s.lead])
        if (openBottom) mb = collapseList([mb, ...s.trail])
      }
    }
    const h = Math.max(r.minH, content + vert)
    return { h, mt, mb, inline, through: through && h === 0 }
  }

  // An automatic table layout: each column between the widest of its cells' narrowest and widest contents, the room
  // shared out in proportion to how much more each would take; every row as tall as its tallest cell.
  tableHeight(t, width) {
    const r = t.rec
    const cols = []
    for (const row of t.rows) {
      row.cells.forEach((c, i) => {
        const cr = c.rec
        const ir = c.inner.rec
        const chrome = cr.pl + cr.pr + cr.bl + cr.br + ir.ml + ir.mr + ir.pl + ir.pr + ir.bl + ir.br
        if (c.max === undefined) {
          c.max = c.inner.prepared ? pretext.measureRichInlineStats(c.inner.prepared, 1e7).maxLineWidth : 0
          c.min = this.longestPiece(c.text, ir.font)
        }
        const min = Math.max(ir.minW, c.min) + chrome
        const max = Math.max(ir.minW, c.max, c.min) + chrome
        const col = cols[i] || (cols[i] = { min: 0, max: 0 })
        col.min = Math.max(col.min, min)
        col.max = Math.max(col.max, max)
      })
    }
    const spacing = r.collapse ? 0 : r.spacing
    const avail = width - r.pl - r.pr - r.bl - r.br - spacing * (cols.length + 1)
    const sumMin = cols.reduce((a, c) => a + c.min, 0)
    const sumMax = cols.reduce((a, c) => a + c.max, 0)
    const widths = sumMax <= avail ? cols.map(c => c.max)
      : sumMin >= avail ? cols.map(c => c.min)
        : cols.map(c => c.min + (avail - sumMin) * (sumMax > sumMin ? (c.max - c.min) / (sumMax - sumMin) : 1 / cols.length))
    let h = 0
    for (const row of t.rows) {
      let rowH = 0
      row.cells.forEach((c, i) => {
        const cr = c.rec
        const ir = c.inner.rec
        const w = widths[i] - cr.pl - cr.pr - cr.bl - cr.br
        // The cell's text is an inline-block filling it: alone on a line of the cell, as tall as that line or itself.
        const innerH = this.lineCount(c.inner, Math.max(ir.minW, w) - ir.pl - ir.pr - ir.bl - ir.br) * ir.lh + ir.pt + ir.pb + ir.bt + ir.bb
        const cellH = Math.max(cr.lh, innerH + ir.mt + ir.mb) + cr.pt + cr.pb + (r.collapse ? (cr.bt + cr.bb) / 2 : cr.bt + cr.bb)
        rowH = Math.max(rowH, cellH, cr.minH)
      })
      h += rowH
    }
    const first = t.rows[0] && t.rows[0].cells[0]
    const last = t.rows.length && t.rows[t.rows.length - 1].cells[0]
    const edge = r.collapse ? (first ? first.rec.bt / 2 : 0) + (last ? last.rec.bb / 2 : 0) : spacing * (t.rows.length + 1)
    return h + edge
  }

  // The widest piece of a text no line can break: a word, or one character of Chinese, Japanese or Korean.
  longestPiece(text, font) {
    if (!text) return 0
    if (!this.canvas) this.canvas = document.createElement('canvas').getContext('2d')
    const ctx = this.canvas
    ctx.font = font
    let widest = 0
    for (const piece of text.split(/\s+|(?=[⺀-鿿가-힯豈-﫿＀-￯])|(?<=[⺀-鿿가-힯豈-﫿＀-￯])/)) {
      if (piece) widest = Math.max(widest, ctx.measureText(piece).width)
    }
    return widest
  }

  // ---- a quick guess for the blocks not predicted yet ----

  /**
   * Per block, about how many lines its text takes - each line of each text in it its characters' width (narrow and
   * wide ones, measured once in the root's font) over the root's width - and its kind (its type, a code block's kind).
   * One pass over the characters, no tokenizing: about 10 ms for 1M characters. Counted once per drawing and width.
   */
  quick(state, env) {
    if (state.quickWidth === env.width && state.quickGeneration === env.generation) return state.quickLines
    const started = performance.now()
    if (!this.canvas) this.canvas = document.createElement('canvas').getContext('2d')
    const s = getComputedStyle(env.root)
    this.canvas.font = fontOf(s)
    const sample = 'The quick brown fox jumps over the lazy dog, 1234567890.'
    const narrow = this.canvas.measureText(sample).width / sample.length || 8
    const wide = this.canvas.measureText('\u4e2d\u6587\u5b57').width / 3 || 16
    const width = Math.max(50, env.width)
    const kinds = state.quickKinds || (state.quickKinds = new Uint8Array(state.blocks.length))
    const lines = new Float32Array(state.blocks.length)
    const kindIds = this.kindIds || (this.kindIds = new Map())
    const lh = state.quickLineHeight = px(s.lineHeight) || px(s.fontSize) * 1.6 || 25
    // Lines of a text, each as wide as its characters, over the root's width.
    const textLines = t => {
      let n = 0
      let w = 0
      for (let k = 0; k <= t.length; k++) {
        const code = k < t.length ? t.charCodeAt(k) : 10
        if (code === 10) {
          n += Math.max(1, Math.ceil(w / width))
          w = 0
        } else if (code >= 0x1100 && !(code >= 0xd800 && code < 0xe000)) w += wide
        else if (code < 0xdc00 || code >= 0xe000) w += narrow
      }
      return n
    }
    const linesOf = b => {
      // A table's rows side by side: each row as many lines as its fullest cell.
      if (b.type === 'tr') return Math.max(1, ...b.children.map(linesOf))
      // Only the leaves' text: a container's own (empty) text is no line.
      if (b.children && b.children.length) {
        let n = 0
        for (const c of b.children) n += linesOf(c)
        return n
      }
      return typeof b.text === 'string' && b.type !== 'input' ? textLines(b.text) : 0
    }
    state.blocks.forEach((block, i) => {
      // In the root's line heights (a code block's lines are a little shorter): a guess in pixels, which a fit made from
      // blocks of one kind carries to another better than a count of lines.
      lines[i] = linesOf(block) * lh * (block.type === 'pre' ? 0.85 : 1)
      const kind = block.type === 'pre' || block.type === 'figure' ? `${block.type}:${block.functionType}` : block.type
      let id = kindIds.get(kind)
      if (id === undefined) kindIds.set(kind, id = Math.min(255, kindIds.size))
      kinds[i] = id
    })
    state.quickLines = lines
    state.quickWidth = env.width
    state.quickGeneration = env.generation
    state.fitted = null
    account('quick', started, state.blocks.length)
    return lines
  }

  /**
   * The guess for blocks not predicted: per kind of block, its quick height (lines in the root's line height) plus an
   * offset - its margins, padding, a heading's taller lines - for its slot (for the room) and its content height (for
   * its size rule). The offsets start from what they are in the editor's own styles (PRIOR, measured on the mixed and
   * showcase documents, scaled to the root's line height) and move to what the predicted blocks of that kind show as
   * they come in; a table's rows (taller than a line by their padding) also get a slope from enough of them. Made again
   * whenever more blocks are predicted.
   */
  fit(state, env) {
    const x = this.quick(state, env)
    const made = this.stats.prepared + this.stats.unsupported
    if (state.fitted && state.fittedAt === made) return state.fitted
    const fitStarted = performance.now()
    const { blocks } = state
    const kinds = state.quickKinds
    const sums = new Map()
    for (let i = 0; i < blocks.length; i++) {
      const b = this.box(blocks[i], env)
      if (!b) continue
      const box = env.contained ? { h: b.hC, mt: b.mtC, mb: b.mbC, inline: b.inline } : b
      const slot = box.h + (box.inline ? box.mt + box.mb : Math.max(box.mt, box.mb))
      let a = sums.get(kinds[i])
      if (!a) sums.set(kinds[i], a = { n: 0, slot: 0, content: 0, x: 0, xx: 0, xs: 0 })
      a.n++
      a.slot += slot - x[i]
      a.content += b.hC - b.ptb - x[i]
      a.x += x[i]
      a.xx += x[i] * x[i]
      a.xs += x[i] * slot
    }
    const scale = (state.quickLineHeight || 25.6) / 25.6
    const fitted = new Map()
    for (const [kind, id] of this.kindIds) {
      const prior = PRIOR[kind] || PRIOR[kind.replace(/:.*/, '')] || PRIOR.other
      const a = sums.get(id) || { n: 0, slot: 0, content: 0 }
      const w = 2
      let slope = prior[2] || 1
      let slotOffset = (a.slot + w * prior[0] * scale) / (a.n + w)
      let contentOffset = (a.content + w * prior[1] * scale) / (a.n + w)
      if (prior[2] && a.n >= 4) {
        // A table: height against its quick height, straight through its own.
        const d = a.n * a.xx - a.x * a.x
        const sumSlot = a.slot + a.x
        const k = d > 1e-9 ? (a.n * a.xs - a.x * sumSlot) / d : 0
        if (k > 0.7 && k < 3) {
          slope = k
          slotOffset = (sumSlot - k * a.x) / a.n
          contentOffset = slotOffset - (a.slot - a.content) / a.n
        }
      }
      fitted.set(id, { slope, slot: slotOffset, content: contentOffset })
    }
    state.fitted = fitted
    state.fittedAt = made
    account('fit', fitStarted)
    return fitted
  }

  // The guess for block i: its slot (for the room) and its content height (for its size rule).
  guess(state, env, i) {
    const fitted = this.fit(state, env)
    const f = fitted.get(state.quickKinds[i])
    if (!f) return null
    const x = state.quickLines[i] * f.slope
    return { slot: Math.max(0, x + f.slot), content: Math.max(0, x + f.content) }
  }

  /**
   * The order blocks are predicted in: those next to what is drawn first (drawn next), then a sample spread over the
   * whole document (taken in an order that spreads any first part of it too: the bits of the count reversed) for the
   * guess (fit), then outwards from what is drawn, as the drawing goes.
   */
  plan(state) {
    const { blocks, lo, hi } = state
    const n = blocks.length
    const order = []
    const seen = new Uint8Array(n)
    const take = i => {
      if (i >= 0 && i < n && !seen[i]) {
        seen[i] = 1
        order.push(i)
      }
    }
    for (let k = 0; k < 12; k++) { take(hi + k); take(lo - 1 - k) }
    const bits = Math.max(4, Math.min(7, Math.round(Math.log2(Math.max(16, n / 16)))))
    const stride = Math.max(1, Math.floor(n / (1 << bits)))
    for (let k = 0; k < 1 << bits; k++) {
      let r = 0
      for (let b = 0; b < bits; b++) r |= ((k >> b) & 1) << (bits - 1 - b)
      take(r * stride)
    }
    for (let k = 0; hi + k < n || lo - 1 - k >= 0; k++) { take(hi + k); take(lo - 1 - k) }
    for (let i = lo; i < hi; i++) take(i)
    return order
  }

  /**
   * The room the blocks not drawn yet take, above and below what is drawn ([lo, hi) of the blocks): each block as
   * predicted where it is, as the fitted guess where not (or `guess(i)`, the per-character estimate from what is drawn,
   * before there is a fit); the margins between them collapsing as on the page. Above: from the top of the first
   * block's margin to where the first drawn block's margin starts; below: from the end of the last drawn block's margin
   * on.
   */
  room(state, env, guess, measureEdges = true) {
    const roomStarted = performance.now()
    const { blocks, lo, hi, root } = state
    const counted = { predicted: 0, fitted: 0, guessed: 0 }
    const fitted = this.fit(state, env)
    const x = state.quickLines
    const kinds = state.quickKinds
    // Stacked as stack() does, one block at a time and nothing kept: this runs every slice of a drawing.
    let y = 0
    let pending = []
    const add = i => {
      const b = this.box(blocks[i], env)
      let h, mt, mb, inline
      if (b) {
        counted.predicted++
        if (env.contained) { h = b.hC; mt = b.mtC; mb = b.mbC } else { h = b.h; mt = b.mt; mb = b.mb }
        inline = b.inline
        if (!inline && b.through && !env.contained) { pending.push(mt, mb); return }
      } else {
        const f = fitted && fitted.get(kinds[i])
        if (f) counted.fitted++
        else counted.guessed++
        h = f ? Math.max(0, x[i] * f.slope + f.slot) : guess(i)
        mt = mb = 0
        inline = false
      }
      if (inline) {
        y += collapseList(pending) + mt + h + mb
        pending = []
      } else {
        pending.push(mt)
        y += collapseList(pending) + h
        pending = [mb]
      }
    }
    // The drawn blocks' own margins at the edges (one in a line of its own, inline-level, has its margins in it): read
    // from the page where it is laid out anyway (`measureEdges`), as last read otherwise.
    const edge = (el, side) => {
      if (!el) return 0
      const st = getComputedStyle(el)
      return /^inline/.test(st.display) ? 0 : px(side === 'top' ? st.marginTop : st.marginBottom)
    }
    if (measureEdges || !state.edges) state.edges = { top: edge(root.firstElementChild, 'top'), bottom: edge(root.lastElementChild, 'bottom') }
    let above = 0
    if (lo > 0) {
      for (let i = 0; i < lo; i++) add(i)
      const mt = state.edges.top
      above = y + collapseList([...pending, mt]) - mt
    }
    let below = 0
    if (hi < blocks.length) {
      const mb = state.edges.bottom
      y = 0
      pending = [mb]
      for (let i = hi; i < blocks.length; i++) add(i)
      below = y + collapseList(pending) - mb
    }
    account('room', roomStarted)
    return { above: Math.max(0, above), below: Math.max(0, below), counted }
  }


  // ---- rules for a long document's blocks drawn off screen ----

  /** Whether the block's size rule is written from its prediction for the environment as it is. */
  isRuled(block, env) {
    return this.ruled.get(block.key) === `${env.width}|${env.generation}`
  }

  /**
   * The size rule for a block drawn off screen (contain-intrinsic-size): from its prediction where it is predicted
   * (`build`: predict it now where not), else from the fitted guess (block `i` of the drawing `state`; refined once
   * drawn, ruleRest), else none. Remembers which it was.
   */
  ruleText(block, env, state = null, i = -1, build = false) {
    const box = this.box(block, env, build)
    if (box) {
      this.ruled.set(block.key, `${env.width}|${env.generation}`)
      return ruleFor(block.key, box.hC - box.ptb)
    }
    if (!state || i < 0 || this.has(block, env)) return null
    const g = this.guess(state, env, i)
    if (!g) return null
    this.ruled.set(block.key, 'guess')
    return ruleFor(block.key, g.content)
  }

  /** Gives the blocks their predicted size while not laid out (contain-intrinsic-size), predicting them where not yet. */
  rule(blocks, env) {
    if (!env.contained) return
    const rules = []
    for (const block of blocks) {
      const text = this.ruleText(block, env, null, -1, true)
      if (text) rules.push(text)
    }
    this.addRules(rules)
  }

  addRules(rules) {
    if (!rules.length) return
    const started = performance.now()
    try {
      this.writeRules(rules)
    } finally {
      account('rules', started, rules.length)
    }
  }

  writeRules(rules) {
    // Rules for blocks long gone pile up over many documents: past a limit they all go (a block still on the page then
    // stands at the style sheet's size until it is ruled again).
    if (this.ruled.size > 60000) this.clearRules()
    if (rules.length > 40) {
      // Many at once (blocks predicted ahead of being drawn): a style sheet of their own.
      const sheet = document.createElement('style')
      sheet.setAttribute('data-td-heights', '')
      sheet.textContent = rules.join('\n')
      document.head.appendChild(sheet)
      this.sheets.push(sheet)
      return
    }
    for (const text of rules) {
      let sheet = this.small
      if (!sheet || !sheet.isConnected || sheet.sheet.cssRules.length >= 500) {
        sheet = this.small = document.createElement('style')
        sheet.setAttribute('data-td-heights', '')
        document.head.appendChild(sheet)
        this.sheets.push(sheet)
      }
      try {
        sheet.sheet.insertRule(text, sheet.sheet.cssRules.length)
      } catch (err) {
        // A key a selector cannot hold: that block keeps the style sheet's stand-in size.
      }
    }
  }

  /**
   * Once a document is drawn: its blocks without a size rule for the page as it is now get one, a few at a time while
   * the page is idle (a later rule for a block wins over an earlier one). Stops when another document is drawn.
   */
  ruleRest(blocks, root) {
    this.lastRuled = { blocks, root }
    if (this.resting) this.resting.stop = true
    const job = { stop: false, started: performance.now() }
    this.resting = job
    let i = 0
    const idle = window.requestIdleCallback
      ? (cb, opts) => window.requestIdleCallback(cb, opts)
      : cb => setTimeout(() => {
        const end = performance.now() + 8
        cb({ timeRemaining: () => Math.max(0, end - performance.now()) })
      }, 50)
    const finished = () => {
      if (this.resting === job) this.resting = null
      const log = window.__typedownPretextLog
      if (log && log.restDoneAt === undefined) log.restDoneAt = performance.now()
    }
    const step = deadline => {
      if (job.stop) return
      if (!root.isConnected) return finished()
      if (!this.ready()) {
        // Not ready for ten seconds (pretext not there, fonts never coming): given up, the guesses stay.
        if (performance.now() - job.started > 10000) return finished()
        return idle(step, { timeout: 1000 })
      }
      const stepStarted = performance.now()
      let env
      try {
        env = this.environment(root)
      } catch (err) {
        return finished()
      }
      if (!env.contained || !(env.width > 0)) return finished()
      const sig = `${env.width}|${env.generation}`
      // Blocks guessed while drawn (or ruled for another width or other styles) predicted and ruled again: in the page's
      // idle time, or a few milliseconds' worth when it has had none for a second. One style sheet change per step.
      const until = performance.now() + (deadline.didTimeout ? 6 : Math.max(0, deadline.timeRemaining() - 1))
      // Those around the window first, outwards from the block at its top.
      if (!job.order) job.order = outwards(blocks.length, topIndex(root))
      const rules = []
      while (i < blocks.length && performance.now() < until) {
        const block = blocks[job.order[i++]]
        if (this.ruled.get(block.key) === sig) continue
        const text = this.ruleText(block, env, null, -1, true)
        if (text) rules.push(text)
      }
      // What is at the top of the window stays where it is as blocks above it change size: the browser's scroll anchoring
      // did not hold it (a block read 1M characters in moved by a thousand pixels as those above were refined).
      const keep = rules.length ? keepTop(root, job) : null
      this.addRules(rules)
      if (keep) keep()
      account('refine', stepStarted, rules.length)
      if (i < blocks.length) idle(step, { timeout: 1000 })
      else finished()
    }
    idle(step, { timeout: 1000 })
  }

  clearRules() {
    for (const sheet of this.sheets) sheet.remove()
    this.sheets = []
    this.ruled.clear()
  }
}

// Offsets from a block's quick height (lines in the root's line height of 25.6 px) to its slot and its content height,
// in the editor's own styles: measured over the mixed 300k document and the showcase. A table also has a slope (its
// rows are taller than lines by their padding).
const PRIOR = {
  p: [6, -2], ul: [6, -2], ol: [12, 4], blockquote: [28, 20], hr: [8, 0],
  h1: [32, 16], h2: [24, 8], h3: [21, 5], h4: [18, 2], h5: [16, 0], h6: [13, -3],
  pre: [30, -23], 'figure:table': [40, 20, 1.5], figure: [10, 2], other: [10, 2]
}

// The index of the top-level block at the top of the window (its place among the root's elements: the whole document
// is drawn when this is asked), or 0.
function topIndex(root) {
  const kids = root.children
  let lo = 0
  let hi = kids.length - 1
  if (hi < 0) return 0
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    if (kids[mid].getBoundingClientRect().bottom <= 0) lo = mid + 1
    else hi = mid
  }
  return lo
}

// 0..n-1 outwards from `from`: from, from+1, from-1, from+2, ...
function outwards(n, from) {
  const order = new Int32Array(n)
  let k = 0
  for (let d = 0; k < n; d++) {
    if (from + d < n) order[k++] = from + d
    if (d > 0 && from - d >= 0 && k < n) order[k++] = from - d
  }
  return order
}

// The top-level block at the top of the window, and a function that scrolls the page by however far it has moved - the
// fraction of a pixel a scroll cannot take carried to the next (`carrier.carry`), or many small moves add up.
function keepTop(root, carrier) {
  const r = root.getBoundingClientRect()
  const hit = document.elementFromPoint(r.left + Math.min(r.width / 2, 200), Math.max(1, Math.min(window.innerHeight - 1, 60)))
  const el = hit && hit.closest ? hit.closest('#ag-editor-id > *') : null
  if (!el || el.parentNode !== root) return null
  const before = el.getBoundingClientRect().top
  return () => {
    if (!el.isConnected) return
    const moved = el.getBoundingClientRect().top - before + (carrier.carry || 0)
    const from = window.scrollY
    if (Math.round(moved)) window.scrollBy(0, Math.round(moved))
    carrier.carry = moved - (window.scrollY - from)
  }
}

// The block's content height as it stands contained: what contain-intrinsic-size gives (its padding and border on top).
const ruleFor = (key, height) => `#editor.ag-long-document #ag-editor-id>#${key}{contain-intrinsic-size:auto ${Math.max(0, Math.round(height * 100) / 100)}px}`

/**
 * Boxes stacked as blocks stack: margins between them collapsing, through an empty one too; an inline-level box (a code
 * block, inline-flex) on a line of its own whose margins collapse with nothing. { any: a box with a height, firstTop:
 * where the first such starts (its border box; an inline one's margin box after firstMt), lead: the margins before it
 * that could go through the parent's top, end: where the last ends, trail: the margins after it }.
 */
export function stack(boxes, pendingBefore = []) {
  let y = 0
  let pending = pendingBefore.slice()
  let any = false
  let firstTop = 0
  let firstInline = false
  let firstMt = 0
  let lead = []
  const tops = []
  for (const b of boxes) {
    if (b.inline) {
      if (!any) { any = true; lead = pending.slice(); firstInline = true; firstMt = b.mt; firstTop = y + collapseList(pending) + b.mt }
      y += collapseList(pending)
      pending = []
      tops.push(y + b.mt)
      y += b.mt + b.h + b.mb
    } else if (b.through) {
      tops.push(y + collapseList([...pending, b.mt]))
      pending.push(b.mt, b.mb)
    } else {
      const top = y + collapseList([...pending, b.mt])
      if (!any) { any = true; lead = [...pending, b.mt]; firstTop = top }
      tops.push(top)
      y = top + b.h
      pending = [b.mb]
    }
  }
  if (!any) lead = pending.slice()
  return { any, firstTop, firstInline, firstMt, lead, end: y, trail: pending, tops }
}

export { collapseList, loadPretext }
