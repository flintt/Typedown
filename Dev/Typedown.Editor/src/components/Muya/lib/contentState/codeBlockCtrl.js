import { loadLanguage } from '../prism/index'
import { escapeHTML } from '../utils'
// import resizeCodeBlockLineNumber from '../utils/resizeCodeLineNumber'
import selection from '../selection'
import { CLASS_OR_ID } from '../config'
import ExportMarkdown from '../utils/exportMarkdown'
import remote from 'services/remote/common'
import { diagramPicture } from 'services/diagramPicture'
import { mathMl, mathPicture, tableHtml } from 'services/blockCopy'
import { blockSource } from 'services/sourceMerge'

const CODE_UPDATE_REP = /^`{3,}(.*)/

// Fence languages that are diagrams: typed as ```mermaid they must become the diagram block with its preview, the
// block a loaded document gets for the same fence. A plain code block with lang "mermaid" never renders; it only
// turned into a diagram after the document was loaded again (a tab or mode switch).
const DIAGRAM_LANGS = ['mermaid', 'flowchart', 'sequence', 'plantuml', 'vega-lite']

const codeBlockCtrl = ContentState => {
  /**
  * check edit language
  */
  ContentState.prototype.checkEditLanguage = function () {
    const { start } = selection.getCursorRange()
    if (!start) {
      return { lang: '', paragraph: null }
    }
    const startBlock = this.getBlock(start.key)
    const paragraph = document.querySelector(`#${start.key}`)
    let lang = ''
    const { text } = startBlock
    if (startBlock.type === 'span') {
      if (startBlock.functionType === 'languageInput') {
        lang = text.trim()
      } else if (startBlock.functionType === 'paragraphContent') {
        const token = text.match(/(^`{3,})([^`]+)/)
        if (token) {
          const len = token[1].length
          if (start.offset >= len) {
            lang = token[2].trim()
          }
        }
      }
    }
    return { lang, paragraph }
  }

  ContentState.prototype.selectLanguage = function (paragraph, lang) {
    const block = this.getBlock(paragraph.id)
    if (lang === 'math' && this.isGitlabCompatibilityEnabled && this.updateMathBlock(block)) {
      return
    }
    this.updateCodeLanguage(block, lang)
  }

  /**
   * Update the code block language or creates a new code block.
   *
   * @param block Language-input block or paragraph
   * @param lang Language identifier
   */
  ContentState.prototype.updateCodeLanguage = function (block, lang) {
    if (!lang || typeof lang !== 'string') {
      console.error('Invalid code block language string:', lang)

      // Use fallback language
      lang = ''
    }

    // Prevent possible XSS on language input when using lang attribute later on. The input is also sanitized before rendering.
    lang = escapeHTML(lang)
    if (lang !== '') {
      loadLanguage(lang)
    }

    if (block.functionType === 'languageInput') {
      const preBlock = this.getParent(block)
      const nextSibling = this.getNextSibling(block)

      // Only update code language if necessary
      if (block.text !== lang || preBlock.text !== lang || nextSibling.text !== lang) {
        block.text = lang
        preBlock.lang = lang
        preBlock.functionType = 'fencecode'
        nextSibling.lang = lang
        nextSibling.children.forEach(c => (c.lang = lang))
      }

      // Set cursor at the first line
      const { key } = nextSibling.children[0]
      const offset = 0
      this.cursor = {
        start: { key, offset },
        end: { key, offset }
      }
    } else {
      block.text = block.text.replace(/^(`+)([^`]+$)/g, `$1${lang}`)
      this.codeBlockUpdate(block)
    }
    this.partialRender()
  }

  /**
   * [codeBlockUpdate if block updated to `pre` return true, else return false]
   */
  ContentState.prototype.codeBlockUpdate = function (block, code = '', lang) {
    if (block.type === 'span') {
      block = this.getParent(block)
    }
    // If it's not a p block, no need to update
    if (block.type !== 'p') return false
    // If p block's children are more than one, no need to update
    if (block.children.length !== 1) return false

    const { text } = block.children[0]
    const match = CODE_UPDATE_REP.exec(text)
    if (match || lang) {
      const language = lang || (match ? match[1] : '')
      const diagram = language.trim().toLowerCase()
      if (DIAGRAM_LANGS.includes(diagram)) {
        // Turn the paragraph itself into the diagram figure, so callers holding this block keep a valid one.
        const figure = this.createContainerBlock(diagram, code)
        block.type = 'figure'
        block.functionType = diagram
        block.text = ''
        block.history = null
        delete block.lang
        block.children = []
        figure.children.forEach(child => this.appendChild(block, child))
        const codeContent = block.children[0].children[0].children[0]
        const offset = code.length
        this.cursor = {
          start: { key: codeContent.key, offset },
          end: { key: codeContent.key, offset }
        }
        return true
      }
      const codeBlock = this.createBlock('code', {
        lang: language
      })
      const codeContent = this.createBlock('span', {
        text: code,
        lang: language,
        functionType: 'codeContent'
      })
      const inputBlock = this.createBlock('span', {
        text: language,
        functionType: 'languageInput'
      })

      if (language) {
        loadLanguage(language)
      }

      block.type = 'pre'
      block.functionType = 'fencecode'
      block.lang = language
      block.text = ''
      block.history = null
      block.children = []

      this.appendChild(codeBlock, codeContent)
      this.appendChild(block, inputBlock)
      this.appendChild(block, codeBlock)
      const { key } = codeContent
      const offset = code.length
      this.cursor = {
        start: { key, offset },
        end: { key, offset }
      }
      return true
    }
    return false
  }

  /**
   * Copy the code block by click right-top copy icon in code block.
   */
  ContentState.prototype.copyCodeBlock = function (event) {
    const { target } = event
    const preEle = target.closest('pre')
    const preBlock = this.getBlock(preEle.id)
    const codeBlock = preBlock.children.find(c => c.type === 'code')
    const codeContent = codeBlock.children[0].text
    this.muya.clipboard.copy({ type: 'copyCodeContent', copyInfo: codeContent })
    // Confirm the copy: the button briefly shows a check instead of the copy icon. Without it there was no
    // sign the click did anything (a Store review).
    const button = target.closest('.ag-code-copy')
    if (button) {
      button.classList.add('ag-code-copied')
      clearTimeout(button._copiedTimer)
      button._copiedTimer = setTimeout(() => button.classList.remove('ag-code-copied'), 1500)
    }
  }

  /**
   * Copy a diagram, a display formula or a table by its copy button (top right, shown on hover while the block is not
   * being edited) or, for a table, by the menu's Copy table: one clipboard item holding the block's Markdown as text, as
   * the document saves it - for a text editor, and pasted back here it is the block again - and, for the programs that
   * take something richer (Word, OneNote, mail, chat, Excel):
   * - a diagram or a formula: its picture, drawn as a format that takes no SVG gets it - the light theme on white at
   *   twice the size (services/diagramPicture), a formula black on white (services/blockCopy), and a formula as MathML
   *   too, which Word makes an editable equation of. One that could not be drawn is copied as its text only.
   * - a table: its HTML (services/blockCopy), which they paste as a table, with its header row, cell formatting and
   *   column alignment.
   * The host writes the clipboard (CopyBlock). Nothing in the document, its history or the selection changes. True once
   * the clipboard holds the copy; the button, when there is one, shows the check mark.
   */
  ContentState.prototype.copyBlock = async function (figure, button = null) {
    const block = figure && this.getBlock(figure.id)
    if (!block || block.type !== 'figure') return false
    const { listIndentation, isGitlabCompatibilityEnabled } = this
    const exportOptions = { alignTableColumns: this.muya.options.tableAlignColumns !== false }
    const exported = new ExportMarkdown([block], listIndentation, isGitlabCompatibilityEnabled, exportOptions).generate().replace(/^\n+|\n+$/g, '')
    // As the document has it: a table written compact stays compact, a fence as it was written (sourceMerge.blockSource).
    let text = exported
    try {
      const whole = this.muya.getMarkdown()
      const source = this.muya.documentText ? this.muya.documentText(whole) : whole
      if (source.length <= 150000) text = blockSource(exported, whole, source, markdown => this.muya.normalizeMarkdown(markdown)) || exported
    } catch (e) {
      console.log('copy block: as exported', e)
    }
    const message = { text, png: null, html: null, mathml: null }
    if (block.functionType === 'table') {
      try {
        message.html = tableHtml(exported + '\n', this.muya.options)
      } catch (e) {
        console.log('copy table: no HTML', e)
      }
    } else {
      const preview = figure.querySelector(':scope > div.ag-container-preview')
      const drawn = !!preview && !preview.classList.contains(CLASS_OR_ID.AG_MATH_ERROR) &&
        !preview.classList.contains('ag-plantuml-off') && !preview.classList.contains(CLASS_OR_ID.AG_EMPTY)
      if (drawn) {
        try {
          if (block.functionType === 'multiplemath') {
            const code = block.children[0] && block.children[0].children[0]
            const tex = code ? code.children.map(line => line.text).join('\n') : ''
            message.png = await mathPicture(tex)
            // With the picture, the formula as MathML: Word makes an equation of it (an editable one).
            message.mathml = mathMl(tex)
          } else {
            // Mermaid is one global the editor's own drawing also uses: not both at once.
            await this.stateRender.mermaidRenderQueue.catch(() => {})
            message.png = await diagramPicture(exported + '\n', this.muya.options)
          }
        } catch (e) {
          console.log('copy block: no picture', e)
        }
      }
    }
    try {
      if (!await remote.copyBlock(message)) return false
    } catch (e) {
      console.log('copy block: the clipboard was not written', e)
      return false
    }
    if (button) {
      button.classList.add('ag-code-copied')
      clearTimeout(button._copiedTimer)
      button._copiedTimer = setTimeout(() => button.classList.remove('ag-code-copied'), 1500)
    }
    return true
  }

  /**
   * The menu's Copy table: the table right-clicked (the menu's own target), else the one the caret is in. False when
   * neither is a table.
   */
  ContentState.prototype.copyTableAtMenu = function () {
    const target = this.contextMenuTarget
    let figure = target && target.isConnected && target.closest ? target.closest('figure[data-role="TABLE"]') : null
    if (!figure && this.cursor && this.cursor.start) {
      let block = this.getBlock(this.cursor.start.key)
      while (block && !(block.type === 'figure' && block.functionType === 'table')) block = block.parent ? this.getBlock(block.parent) : null
      figure = block ? document.getElementById(block.key) : null
    }
    if (!figure) return Promise.resolve(false)
    return this.copyBlock(figure, figure.querySelector(':scope > .ag-diagram-copy'))
  }

  ContentState.prototype.resizeLineNumber = function () {
    // FIXME: Disabled due to #1648.
    // const { codeBlockLineNumbers } = this.muya.options
    // if (!codeBlockLineNumbers) {
    //   return
    // }

    // const codeBlocks = document.querySelectorAll('pre.line-numbers')
    // if (codeBlocks.length) {
    //   for (const ele of codeBlocks) {
    //     resizeCodeBlockLineNumber(ele)
    //   }
    // }
  }
}

export default codeBlockCtrl
