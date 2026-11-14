import { EXPORT_DOMPURIFY_CONFIG } from 'components/Muya/lib/config';
import Slugger from 'components/Muya/lib/parser/marked/slugger';
import { sanitize, getHeadingPlainText } from 'components/Muya/lib/utils';
import execAll from 'execall'
import { setSearchGivenUp } from './searchGivenUp'
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
        else if (line.trim() && /^ {0,3}(=+|-+)\s*$/.test(lines[index + 1] ?? '')) {
            // A setext heading is the paragraph's lines above its underline, as the lexer reads it now ("Three\nlines\n---"
            // is one heading): its line is the first of them.
            let first = index
            while (first > 0 && lines[first - 1].trim() && !/^ {0,3}(#{1,6}(\s|$)|>|[*+-] |\d{1,9}[.)] |(`{3,}|~{3,})|(=+|-+)\s*$)/.test(lines[first - 1])) first--
            candidates.push({ line: first, setext: true, text: lines.slice(first, index + 1).map(l => l.trim()).join('\n') })
        }
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

// A regular expression the reader types can take minutes on one line ("(a+)+$" on a long run of a's), and a search runs
// on the page's thread, which nothing can stop then: the window hung (the audit's P07). So one is first tried in a worker,
// on the texts it will be run over, for SLOW_REGEX_MS at most; until it has come through, a search with it finds nothing,
// and one that took longer finds nothing for good (the worker is stopped). A plain search is not tried: its pattern has
// no backtracking to speak of.
// A verdict is for an expression on the texts it was tried on: one that came through on "aaa" took over the thread again
// once forty a's and a "!" were loaded (the 2026-11-11 recheck of P07). Each search says what it will run over; changed
// texts (their fingerprint, below) are tried again.
const SLOW_REGEX_MS = 2000
type RegexVerdict = 'ok' | 'slow' | 'pending'
const regexVerdicts = new Map<string, RegexVerdict>()
const regexKey = (re: RegExp) => `${re.flags}/${re.source}`
// The texts an expression was last asked to run over, by expression: what searchMayRun holds its verdict to.
const regexTexts = new Map<string, string>()
const fingerprint = (texts: string[]) => {
    let hash = 0x811c9dc5
    let length = 0
    for (const text of texts) {
        length += text.length + 1
        for (let i = 0; i < text.length; i++) hash = Math.imul(hash ^ text.charCodeAt(i), 16777619)
        hash = Math.imul(hash ^ 10, 16777619)
    }
    return `${length}:${(hash >>> 0).toString(36)}`
}
let regexWorker: Worker | null = null

const regexWorkerSource = `onmessage = e => {
    const { id, source, flags, texts } = e.data
    const re = new RegExp(source, flags)
    for (const text of texts) {
        re.lastIndex = 0
        let m
        while ((m = re.exec(text))) if (m[0] === '') re.lastIndex++
    }
    postMessage(id)
}`

/** Whether a search with these options may run now: a plain one always, a regular expression once tried (see above). */
export const searchMayRun = (value: string, options: any) => {
    if (!options?.searchIsRegexp || typeof Worker === 'undefined') return noteGivenUp(true)
    const re = searchRegExp(value, options)
    if (!re) return noteGivenUp(true)
    const pattern = regexKey(re)
    const verdict = regexTexts.has(pattern) ? regexVerdicts.get(`${pattern}#${regexTexts.get(pattern)}`) : undefined
    setSearchGivenUp(verdict === 'slow')
    return verdict === 'ok'
}

const noteGivenUp = (mayRun: boolean) => {
    setSearchGivenUp(false)
    return mayRun
}

/**
 * Tries a regular expression search on `texts` (what it will be run over) in the worker; `done` is called when it came
 * through in time, for the search to be done again. Asked by every search: nothing when these texts were tried already
 * or are being tried.
 */
export const trySearch = (value: string, options: any, texts: () => string[], done: () => void) => {
    if (!options?.searchIsRegexp || typeof Worker === 'undefined') return
    const re = searchRegExp(value, options)
    if (!re) return
    const pattern = regexKey(re)
    const over = texts()
    const seen = fingerprint(over)
    regexTexts.set(pattern, seen)
    if (regexTexts.size > 200) regexTexts.delete(regexTexts.keys().next().value as string)
    const key = `${pattern}#${seen}`
    if (regexVerdicts.has(key)) return
    if (regexVerdicts.size >= 200) regexVerdicts.delete(regexVerdicts.keys().next().value as string)
    regexVerdicts.set(key, 'pending')
    try {
        regexWorker ||= new Worker(URL.createObjectURL(new Blob([regexWorkerSource], { type: 'text/javascript' })))
    } catch (err) {
        // No worker to try it in: run as before.
        regexVerdicts.set(key, 'ok')
        done()
        return
    }
    const worker = regexWorker
    const id = Math.random()
    const finish = (verdict: RegexVerdict) => {
        window.clearTimeout(timer)
        worker.removeEventListener('message', onMessage)
        regexVerdicts.set(key, verdict)
        if (verdict === 'ok') {
            done()
            return
        }
        console.log(`search: the regular expression ${pattern} took over ${SLOW_REGEX_MS} ms on these texts; it finds nothing`)
        worker.terminate()
        if (regexWorker === worker) regexWorker = null
        // Searched again (finding nothing), for the bar to be told it was given up on.
        done()
    }
    const onMessage = (e: MessageEvent) => { if (e.data === id) finish('ok') }
    const timer = window.setTimeout(() => finish('slow'), SLOW_REGEX_MS)
    worker.addEventListener('message', onMessage)
    worker.postMessage({ id, source: re.source, flags: re.flags, texts: over })
}

export const matchString = (text: string, value: string, options: any) => {
    // A regular expression not tried yet, or too slow: nothing (see trySearch).
    if (!searchMayRun(value, options)) return []
    const SEARCH_REG = searchRegExp(value, options)
    // Not all strings make a valid RegExp, for example `\`.
    return SEARCH_REG ? execAll(SEARCH_REG, text) : []
}

// The search as a regular expression: the bar's text as written for a regular expression search, escaped otherwise.
// Null when it is not one (an unfinished "(" or "\" as it is typed).
function searchRegExp(value: string, options: any): RegExp | null {
    const { searchIsCaseSensitive, searchIsWholeWord, searchIsRegexp } = options
    /* eslint-disable no-useless-escape */
    const SPECIAL_CHAR_REG = /[\[\]\\^$.\|\?\*\+\(\)\/]{1}/g
    /* eslint-enable no-useless-escape */
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
        return new RegExp(regStr, flag)
    } catch (err) {
        return null
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