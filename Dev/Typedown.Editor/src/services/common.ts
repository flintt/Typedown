import { EXPORT_DOMPURIFY_CONFIG } from 'components/Muya/lib/config';
import Slugger from 'components/Muya/lib/parser/marked/slugger';
import { sanitize, getHeadingPlainText } from 'components/Muya/lib/utils';
import execAll from 'execall'
import { Lexer } from 'components/Muya/lib/parser/marked'

/** The slug of the heading on a line in source mode's outline: the line it is on (sourceHeadingLine reads it back). */
export const sourceHeadingSlug = (line: number) => `src:${line}`
export const sourceHeadingLine = (slug: unknown): number | null => {
    const match = /^src:(\d+)$/.exec(String(slug))
    return match ? Number(match[1]) : null
}

// Source mode's outline, the same as the visual editor's (contentState getTOC: the document's top-level headings, setext
// ones too): read by the same lexer. A line scan found "#" lines only - a shell comment in a code block was a heading, an
// underlined one was not, and the two modes' outlines differed (the user, 2026-11-05, on the showcase). Each heading's
// slug is its line (a random number before: a click in the outline asked for a slug the editor could not find).
export const getTOC = (markdown: string) => {
    const tokens: any[] = new Lexer({ disableInline: true }).lex(markdown)
    const headings: { depth: number, text: string, setext: boolean }[] = []
    let nested = 0
    for (const token of tokens) {
        if (/_start$/.test(token.type)) nested++
        else if (/_end$/.test(token.type)) nested--
        else if (token.type === 'heading' && nested === 0) headings.push({ depth: token.depth, text: token.text, setext: token.headingStyle === 'setext' })
    }
    // Each to its line, in order: the lexer keeps no positions. The lines that can be one (an ATX heading's "#" line, a
    // setext one's text above its underline), outside fenced code and the front matter; each heading takes the next
    // whose text is its own (the first such line when none is: a heading in a list's continuation looks like one too).
    const lines = markdown.split('\n')
    const candidates: { line: number, setext: boolean, text: string }[] = []
    let fence: string | null = null
    let index = 0
    if (/^---\s*$/.test(lines[0] ?? '')) {
        index = 1
        while (index < lines.length && !/^(---|\.\.\.)\s*$/.test(lines[index])) index++
        index++
    }
    for (; index < lines.length; index++) {
        const line = lines[index]
        const opens = /^ {0,3}(`{3,}|~{3,})/.exec(line)
        if (fence) {
            if (opens && opens[1][0] === fence[0] && opens[1].length >= fence.length && !line.slice(opens[0].length).trim()) fence = null
            continue
        }
        if (opens) { fence = opens[1]; continue }
        if (/^ {0,3}#{1,6}(\s|$)/.test(line))
            candidates.push({ line: index, setext: false, text: line.replace(/^ {0,3}#{1,6}/, '').replace(/\s#+\s*$/, '').trim() })
        else if (line.trim() && /^ {0,3}(=+|-+)\s*$/.test(lines[index + 1] ?? ''))
            candidates.push({ line: index, setext: true, text: line.trim() })
    }
    const toc = []
    let next = 0
    for (const heading of headings) {
        const text = heading.text.trim()
        let found = -1
        for (let i = next; i < candidates.length; i++) {
            if (candidates[i].setext !== heading.setext) continue
            if (candidates[i].text === text) { found = i; break }
            if (found < 0) found = i
        }
        const line = found < 0 ? 0 : candidates[found].line
        if (found >= 0) next = found + 1
        toc.push({
            content: getHeadingPlainText(text),
            lvl: heading.depth,
            slug: sourceHeadingSlug(line),
        })
    }
    return { toc }
}

export const matchString = (text: string, value: string, options: any) => {
    const { searchIsCaseSensitive, searchIsWholeWord, searchIsRegexp } = options
    /* eslint-disable no-useless-escape */
    const SPECIAL_CHAR_REG = /[\[\]\\^$.\|\?\*\+\(\)\/]{1}/g
    /* eslint-enable no-useless-escape */
    let SEARCH_REG = null
    let regStr = value
    let flag = 'g'

    if (!searchIsCaseSensitive) {
        flag += 'i'
    }

    if (!searchIsRegexp) {
        regStr = value.replace(SPECIAL_CHAR_REG, (p) => {
            return p === '\\' ? '\\\\' : `\\${p}`
        })
    }

    if (searchIsWholeWord) {
        regStr = `\\b${regStr}\\b`
    }

    try {
        // Add try catch expression because not all string can generate a valid RegExp. for example `\`.
        SEARCH_REG = new RegExp(regStr, flag)
        return execAll(SEARCH_REG, text)
    } catch (err) {
        return []
    }
}

export const generateHtmlToc = (tocList: any, slugger: any, currentLevel: any, options: any): string => {
    if (!tocList || tocList.length === 0) {
        return ''
    }

    const topLevel = tocList[0].lvl
    if (!options.tocIncludeTopHeading && topLevel <= 1) {
        tocList.shift()
        return generateHtmlToc(tocList, slugger, currentLevel, options)
    } else if (topLevel <= currentLevel) {
        return ''
    }

    const { content, lvl } = tocList.shift()
    const slug = slugger.slug(content)

    let html = `<li><span><a class="toc-h${lvl}" href="#${slug}">${content}</a><span class="dots"></span></span>`

    // Generate sub-items
    if (tocList.length !== 0 && tocList[0].lvl > lvl) {
        html += '<ul>' + generateHtmlToc(tocList, slugger, lvl, options) + '</ul>'
    }

    html += '</li>' + generateHtmlToc(tocList, slugger, currentLevel, options)
    return html
}

export const getHtmlToc = (toc: any, options: any = {}) => {
    const list = JSON.parse(JSON.stringify(toc))
    const slugger = new Slugger()
    const tocList = generateHtmlToc(list, slugger, 0, options)
    if (!tocList) {
        return ''
    }

    const title = options.tocTitle ? options.tocTitle : 'Table of Contents'
    const html = `<div class="toc-container"><p class="toc-title">${title}</p><ul class="toc-list">${tocList}</ul></div>`
    return sanitize(html, EXPORT_DOMPURIFY_CONFIG)
}