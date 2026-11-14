import { CLASS_OR_ID } from '../../../config'
import { renderTableTools } from './renderToolBar'
import { footnoteJumpIcon } from './renderFootnoteJump'
import { renderEditIcon } from './renderContainerEditIcon'
import renderLineNumberRows from './renderLineNumber'
import renderCopyButton from './renderCopyButton'
import { renderLeftBar, renderBottomBar } from './renderTableDargBar'
import { h } from '../snabbdom'
import { detectTextDirection, findBlockText } from '../../../utils/detectTextDirection'
import { FOLD_MIN_LINES, blockFingerprint, isFolded } from 'services/blockFold'

// A table's widths when they are drawn: the setting on, one share for each column.
const tableWidthsOf = (muya, table) =>
  muya.options.tableColumnWidths && Array.isArray(table.widths) && table.widths.length === table.column + 1 ? table.widths : null

// A long code block's or table's fold button (services/blockFold), by its copy button.
const renderFoldButton = (fingerprint, folded) => h(`a.ag-block-fold${folded ? '.folded' : ''}`, {
  attrs: { contenteditable: 'false', 'data-fold': fingerprint, 'aria-expanded': folded ? 'false' : 'true' }
}, folded ? '\u25B8' : '\u25BE')

const PRE_BLOCK_HASH = {
  fencecode: `.${CLASS_OR_ID.AG_FENCE_CODE}`,
  indentcode: `.${CLASS_OR_ID.AG_INDENT_CODE}`,
  html: `.${CLASS_OR_ID.AG_HTML_BLOCK}`,
  frontmatter: `.${CLASS_OR_ID.AG_FRONT_MATTER}`,
  multiplemath: `.${CLASS_OR_ID.AG_MULTIPLE_MATH}`,
  flowchart: `.${CLASS_OR_ID.AG_FLOWCHART}`,
  sequence: `.${CLASS_OR_ID.AG_SEQUENCE}`,
  plantuml: `.${CLASS_OR_ID.AG_PLANTUML}`,
  mermaid: `.${CLASS_OR_ID.AG_MERMAID}`,
  'vega-lite': `.${CLASS_OR_ID.AG_VEGA_LITE}`
}

// Block types that hold user-typed prose and should get a `dir` attribute so
// Arabic/Hebrew paragraphs mixed with Latin ones each get correct direction
// and alignment, instead of the whole document being forced to LTR.
const TEXT_DIRECTION_TYPES = /^(?:p|h1|h2|h3|h4|h5|h6|li|blockquote|td|th)$/

export default function renderContainerBlock (parent, block, activeBlocks, matches, useCache = false) {
  let selector = this.getSelector(block, activeBlocks)
  const {
    key,
    align,
    type,
    headingStyle,
    editable,
    functionType,
    listType,
    listItemType,
    bulletMarkerOrDelimiter,
    isLooseListItem,
    lang,
    column
  } = block

  if (type === 'table') {
    this.renderingTable = block
  } else if (/thead|tbody/.test(type)) {
    this.renderingRowContainer = block
  }

  // The source of a raw HTML block is on the page only while the caret is in it (ag-active; the preview stands for it
  // otherwise, assets/styles/index.css): out of it, its text is not highlighted. Highlighting it was most of what drawing
  // a document of raw HTML cost (a 1.5 MB one: 60% of the time it took, LT01). The caret going in or out draws the
  // block again (its active class), and with that, highlighted or not.
  const htmlSource = type === 'pre' && functionType === 'html'
  const wasPlain = this.plainHtmlSource
  if (htmlSource) this.plainHtmlSource = !selector.includes(`.${CLASS_OR_ID.AG_ACTIVE}`)
  const children = block.children.map(child => this.renderBlock(block, child, activeBlocks, matches, useCache))
  if (htmlSource) this.plainHtmlSource = wasPlain
  const data = {
    attrs: {},
    dataset: {}
  }

  if (TEXT_DIRECTION_TYPES.test(type)) {
    const { textDirection } = this.muya.options
    // Native `dir="auto"` isn't reliably re-evaluated by the browser as text
    // is patched in by our virtual-dom renderer while typing, so detect the
    // direction ourselves from the block's own text and emit a literal
    // ltr/rtl value that snabbdom will keep in sync with the content.
    const dir = textDirection === 'ltr' || textDirection === 'rtl'
      ? textDirection
      : detectTextDirection(findBlockText(block)) || 'ltr'
    Object.assign(data.attrs, { dir })
  }

  if (editable === false) {
    Object.assign(data.attrs, {
      contenteditable: 'false',
      spellcheck: 'false'
    })
  }

  if (/code|pre/.test(type)) {
    if (typeof lang === 'string' && !!lang) {
      selector += `.language-${lang.replace(/[#.]{1}/g, '')}`
    }
    if (type === 'pre') {
      children.unshift(renderCopyButton())
      if (/^(?:fencecode|indentcode)$/.test(functionType)) {
        const code = block.children.find(child => child.type === 'code')
        const text = (code && findBlockText(code)) || ''
        if (text.split('\n').length >= FOLD_MIN_LINES) {
          const fingerprint = blockFingerprint('code', text)
          const folded = isFolded(fingerprint)
          children.unshift(renderFoldButton(fingerprint, folded))
          if (folded) selector += '.ag-folded'
        }
      }
    }
    // Settings > Editor > Line numbers in code blocks, for code (fenced or indented; not a diagram's or maths' source).
    // The lines do not wrap then (they scroll across, assets/styles/index.css), so each number stands by its line with
    // nothing measured - MarkText turned them off (#1648) as numbers measured against wrapped lines went wrong. The
    // numbers are CSS counters on empty spans: not text, not copied.
    if (this.muya.options.codeBlockLineNumbers) {
      if (type === 'pre' && /^(?:fencecode|indentcode)$/.test(functionType)) {
        selector += '.line-numbers'
      } else if (type === 'code' && parent && /^(?:fencecode|indentcode)$/.test(parent.functionType)) {
        children.unshift(renderLineNumberRows(block.children[0]))
      }
    }
    Object.assign(data.attrs, { spellcheck: 'false' })
  }

  // Column widths (Settings > Editor > Table column widths, services/tableWidths): the table laid out fixed, each header
  // cell its share.
  if (type === 'table' && tableWidthsOf(this.muya, block)) {
    Object.assign(data.attrs, { style: 'table-layout:fixed;width:100%' })
    selector += '.ag-table-widths'
  }

  if (/^(?:th|td)$/.test(type)) {
    const { cells } = this.muya.contentState.selectedTableCells || {}
    const styles = []
    if (align) styles.push(`text-align:${align}`)
    if (type === 'th' && typeof column === 'number') {
      const table = this.muya.contentState.closest(block, 'table')
      const widths = table && tableWidthsOf(this.muya, table)
      if (widths) styles.push(`width:${widths[column]}%`)
    }
    if (styles.length) {
      Object.assign(data.attrs, {
        style: styles.join(';')
      })
    }

    if (typeof column === 'number') {
      Object.assign(data.dataset, {
        column
      })
    }

    if (cells && cells.length) {
      const cell = cells.find(c => c.key === key)
      if (cell) {
        const { top, right, bottom, left } = cell
        selector += '.ag-cell-selected'
        if (top) {
          selector += '.ag-cell-border-top'
        }
        if (right) {
          selector += '.ag-cell-border-right'
        }
        if (bottom) {
          selector += '.ag-cell-border-bottom'
        }
        if (left) {
          selector += '.ag-cell-border-left'
        }
      }
    } else {
      // Judge whether to render the table drag bar.
      const { renderingTable, renderingRowContainer } = this

      const findTable = renderingTable ? activeBlocks.find(b => b.key === renderingTable.key) : null
      if (findTable && renderingRowContainer) {
        const { row: tableRow, column: tableColumn } = findTable
        const isLastRow = () => {
          if (renderingRowContainer.type === 'thead') {
            return tableRow === 0
          } else {
            return !parent.nextSibling
          }
        }
        if (block.parent === activeBlocks[1].parent && !block.preSibling && tableRow > 0) {
          children.unshift(renderLeftBar())
        }

        if (column === activeBlocks[1].column && isLastRow() && tableColumn > 0) {
          children.push(renderBottomBar())
        }
      }
    }
  } else if (/^h/.test(type)) {
    if (/^h\d$/.test(type)) {
      // TODO: This should be the best place to create and update the TOC.
      //       Cache `block.key` and title and update only if necessary.
      Object.assign(data.dataset, {
        head: type
      })
      selector += `.${headingStyle}`
    }
    Object.assign(data.dataset, {
      role: type
    })
  } else if (type === 'figure') {
    if (functionType) {
      Object.assign(data.dataset, { role: functionType.toUpperCase() })
      if (functionType === 'table' && activeBlocks[0] && activeBlocks[0].functionType === 'cellContent') {
        children.unshift(renderTableTools(activeBlocks))
      } else if (functionType !== 'footnote') {
        // children.unshift(renderEditIcon())
      } else {
        children.push(footnoteJumpIcon())
      }
    }

    if (
      /html|multiplemath|flowchart|mermaid|sequence|plantuml|vega-lite/.test(functionType)
    ) {
      selector += `.${CLASS_OR_ID.AG_CONTAINER_BLOCK}`
      Object.assign(data.attrs, { spellcheck: 'false' })
    }
    // A diagram or a display formula copies as its picture and its Markdown, a table as HTML and its Markdown
    // (ContentState.copyBlockToClipboard). Inline formulas have none.
    if (/^(?:flowchart|mermaid|sequence|plantuml|vega-lite|multiplemath|table)$/.test(functionType)) {
      children.push(renderCopyButton(true))
    }
    // A long table folds (services/blockFold): known by its header row.
    if (functionType === 'table' && block.children[0] && block.children[0].row + 1 >= FOLD_MIN_LINES) {
      const head = block.children[0].children[0]
      const header = head && head.children[0] ? head.children[0].children.map(cell => findBlockText(cell) || '').join('|') : ''
      const fingerprint = blockFingerprint('table', header)
      const folded = isFolded(fingerprint)
      children.push(renderFoldButton(fingerprint, folded))
      if (folded) selector += '.ag-folded'
    }
  } else if (/ul|ol/.test(type) && listType) {
    selector += `.ag-${listType}-list`
    if (type === 'ol') {
      Object.assign(data.attrs, { start: block.start })
    }
  } else if (type === 'li' && listItemType) {
    Object.assign(data.dataset, { marker: bulletMarkerOrDelimiter })
    selector += `.${CLASS_OR_ID.AG_LIST_ITEM}`
    selector += `.ag-${listItemType}-list-item`
    selector += isLooseListItem ? `.${CLASS_OR_ID.AG_LOOSE_LIST_ITEM}` : `.${CLASS_OR_ID.AG_TIGHT_LIST_ITEM}`
  } else if (type === 'pre') {
    Object.assign(data.attrs, { spellcheck: 'false' })
    Object.assign(data.dataset, { role: functionType })
    selector += PRE_BLOCK_HASH[block.functionType]

    if (/html|multiplemath|mermaid|flowchart|vega-lite|sequence|plantuml/.test(functionType)) {
      const codeBlock = block.children[0]
      const code = codeBlock.children.map(line => line.text).join('\n')
      this.codeCache.set(block.key, code)
    }
  }

  if (!block.parent) {
    children.unshift(this.renderIcon(block))
  }

  return h(selector, data, children)
}
