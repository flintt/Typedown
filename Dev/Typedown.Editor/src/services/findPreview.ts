/**
 * The find bar's results list (the side pane's "Search in document"): a line of text around each match, as the reader
 * reads it - without the Markdown marks (`**`, `` ` ``, `[`…`](url)`, raw HTML tags, a heading's `#`) - and where the match
 * is in that line, for the host to show it marked.
 *
 * Built from the text of the document's model (a block's text in the visual editor, a line in source mode), never from
 * what is drawn: a long document is drawn a part at a time, and the list must not wait for, or force, the rest.
 */

/** Characters of context on each side of a match. */
export const PREVIEW_CONTEXT = 40;

/** A match longer than this (a regular expression can match a whole paragraph) is shown cut, with "…". */
export const PREVIEW_MATCH_MAX = 120;

export interface FindPreview {
    /** The line shown: plain text, whitespace runs as one space, "…" where it was cut. */
    text: string;
    /** The match in `text`: [start, end). */
    start: number;
    end: number;
}

/** A text with its Markdown marks left out: `plain`, and for each of its characters the index it came from. */
export interface Stripped {
    plain: string;
    /** `kept[i]`: the number of characters of the source before `i` that are kept (length: source length + 1). */
    kept: Int32Array;
}

const PUNCTUATION = /[!-/:-@[-`{-~]/;
const WORD = /[\p{L}\p{N}]/u;

/** The index of the `]` closing the `[` at `open`, or -1. Escapes and code spans inside are skipped. */
function closeBracket(text: string, open: number): number {
    let depth = 0;
    for (let i = open; i < text.length; i++) {
        const c = text[i];
        if (c === '\\') { i++; continue; }
        if (c === '[') depth++;
        else if (c === ']' && --depth === 0) return i;
    }
    return -1;
}

/** The index of the `)` closing the `(` at `open`, or -1. */
function closeParen(text: string, open: number): number {
    let depth = 0;
    for (let i = open; i < text.length; i++) {
        const c = text[i];
        if (c === '\\') { i++; continue; }
        if (c === '(') depth++;
        else if (c === ')' && --depth === 0) return i;
    }
    return -1;
}

const TAG = /^<\/?[A-Za-z][A-Za-z0-9-]*(?:\s[^<>]*)?\/?>/;
const AUTOLINK = /^<(?:[A-Za-z][A-Za-z0-9+.-]{1,31}:[^<>\s]*|[^<>\s@]+@[^<>\s@]+)>/;

/**
 * The inline Markdown of `text` without its marks: emphasis (`*`, `_` at a word's edge, `~~`, `==`), code span
 * backticks (their content kept as it is), a link's or an image's brackets and destination (its text kept), raw HTML
 * tags (an autolink keeps its address), backslash escapes. `heading`: a leading `#`… too.
 */
export function stripMarks(text: string, heading = false, table = false): Stripped {
    const n = text.length;
    const keep = new Uint8Array(n).fill(1);
    let i = 0;
    if (heading) {
        const m = /^ {0,3}#{1,6}(?:[ \t]+|$)/.exec(text);
        if (m) { keep.fill(0, 0, m[0].length); i = m[0].length; }
    }
    while (i < n) {
        const c = text[i];
        if (c === '\\' && i + 1 < n && PUNCTUATION.test(text[i + 1])) {
            keep[i] = 0;
            i += 2;
            continue;
        }
        if (c === '`') {
            let run = 1;
            while (text[i + run] === '`') run++;
            const fence = '`'.repeat(run);
            let close = text.indexOf(fence, i + run);
            while (close >= 0 && text[close + run] === '`') close = text.indexOf(fence, close + run + 1);
            if (close < 0) { i += run; continue; }
            keep.fill(0, i, i + run);
            keep.fill(0, close, close + run);
            i = close + run;
            continue;
        }
        // A table row's cell walls (source mode; the visual editor has each cell's text on its own).
        if (c === '|' && table) { keep[i] = 0; i++; continue; }
        if (c === '*' || c === '_' || ((c === '~' || c === '=') && text[i + 1] === c)) {
            let run = 1;
            while (text[i + run] === c) run++;
            const before = i > 0 ? text[i - 1] : ' ';
            const after = i + run < n ? text[i + run] : ' ';
            // An emphasis mark stands at a word's edge: "2 * 3" and "a == b" keep theirs (space on both sides), and
            // snake_case its underscores (a letter on both sides).
            const loose = /\s/.test(before) && /\s/.test(after);
            const inWord = c === '_' && WORD.test(before) && WORD.test(after);
            if (!loose && !inWord && (c !== '~' && c !== '=' ? true : run === 2)) keep.fill(0, i, i + run);
            i += run;
            continue;
        }
        if (c === '!' && text[i + 1] === '[') {
            const close = closeBracket(text, i + 1);
            if (close > i && text[close + 1] === '(') keep[i] = 0;
            i++;
            continue;
        }
        if (c === '[' && text[i + 1] !== '^') {
            const close = closeBracket(text, i);
            if (close > i) {
                if (text[close + 1] === '(') {
                    const end = closeParen(text, close + 1);
                    if (end > close) { keep[i] = 0; keep.fill(0, close, end + 1); i++; continue; }
                } else if (text[close + 1] === '[') {
                    const end = closeBracket(text, close + 1);
                    if (end > close) { keep[i] = 0; keep.fill(0, close, end + 1); i++; continue; }
                }
            }
            i++;
            continue;
        }
        if (c === '<') {
            const rest = text.slice(i, i + 2000);
            const auto = AUTOLINK.exec(rest);
            if (auto) { keep[i] = 0; keep[i + auto[0].length - 1] = 0; i += auto[0].length; continue; }
            const tag = TAG.exec(rest);
            if (tag) { keep.fill(0, i, i + tag[0].length); i += tag[0].length; continue; }
        }
        i++;
    }
    const kept = new Int32Array(n + 1);
    let plain = '';
    for (let k = 0; k < n; k++) {
        kept[k + 1] = kept[k] + keep[k];
        if (keep[k]) plain += text[k];
    }
    return { plain, kept };
}

/** A line's own leading marks in source mode: quote, list bullet or number, task box, heading. */
const LINE_MARKS = /^(?:[ \t]*(?:>[ \t]?|[-*+][ \t]+(?:\[[ xX]\][ \t]+)?|\d{1,9}[.)][ \t]+(?:\[[ xX]\][ \t]+)?))*[ \t]*(?:#{1,6}(?:[ \t]+|$))?/;

/** A source line that is a table row (its cells' walls are marks). */
export const isTableRow = (line: string) => /^[ \t]*\|.*\|[ \t]*$/.test(line);

/** A fence opening or closing a code block in source mode. */
export const FENCE = /^[ \t]{0,3}(`{3,}|~{3,})/;

/** How long the leading marks of a source line are (LINE_MARKS). */
export function lineMarksLength(line: string): number {
    const m = LINE_MARKS.exec(line);
    return m ? m[0].length : 0;
}

/**
 * The preview of the match [start, end) of `text`. `raw`: the text is code (a code block, a formula, raw HTML) and is
 * shown as it is; otherwise without its marks (stripMarks; `heading`: a heading's line, `table`: a table row's line with
 * its cell walls, `lead`: that many characters at its start are marks too - a source line's quote and list marks). A match made only of marks (a search for `**`) is
 * shown in the text as written.
 */
export function previewOf(text: string, start: number, end: number, opts: { raw?: boolean, heading?: boolean, table?: boolean, lead?: number, stripped?: Stripped } = {}): FindPreview {
    let plain = text;
    let ps = start;
    let pe = end;
    if (!opts.raw) {
        const lead = Math.min(opts.lead ?? 0, start);
        const s = opts.stripped ?? stripMarks(lead ? text.slice(lead) : text, opts.heading, opts.table);
        const a = s.kept[Math.max(0, start - lead)];
        const b = s.kept[Math.max(0, Math.min(end, text.length) - lead)];
        if (b > a) {
            plain = s.plain;
            ps = a;
            pe = b;
        }
    }
    pe = Math.min(pe, plain.length);
    const cut = pe - ps > PREVIEW_MATCH_MAX;
    const me = cut ? ps + PREVIEW_MATCH_MAX : pe;
    let from = Math.max(0, ps - PREVIEW_CONTEXT);
    const to = cut ? me : Math.min(plain.length, me + PREVIEW_CONTEXT);
    // Not half a character (a surrogate pair) at either edge.
    if (from > 0 && /[\uDC00-\uDFFF]/.test(plain[from])) from--;
    let out = '';
    let os = -1;
    let oe = -1;
    let space = true; // no space at the start
    for (let k = from; k < to; k++) {
        if (k === ps) os = out.length;
        if (k === me) oe = out.length;
        const ch = plain[k];
        if (/\s/.test(ch)) {
            // Whitespace inside the match is kept as one space all the same: the match stays marked as one.
            if (!space || (k > ps && k < me)) out += ' ';
            space = true;
        } else {
            out += ch;
            space = false;
        }
    }
    if (os < 0) os = out.length;
    if (oe < 0) oe = out.length;
    // A trailing space after the match goes; one inside it stays.
    while (out.length > oe && out.endsWith(' ')) out = out.slice(0, -1);
    const before = from > 0 ? '…' : '';
    const after = to < plain.length ? '…' : '';
    return { text: before + out + after, start: os + before.length, end: oe + before.length };
}
