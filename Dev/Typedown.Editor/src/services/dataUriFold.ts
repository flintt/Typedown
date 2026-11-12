// Pictures kept in the document as base64 (#52): in source mode the encoded part of a data: address is one short label
// - the address's start, its kind and size - that the caret steps over and a selection takes whole. The text is not
// changed: a copy, a save and a search see the whole address. A line hundreds of KB long was also slow to draw.

export interface DataUriRun {
    /** Where the label starts in the line: after the first few characters of the encoded part. */
    start: number
    /** Where the encoded part ends. */
    end: number
    /** "PNG · 245 KB" */
    label: string
}

// Folded only when long enough to be worth it; a short one (an icon) is left as text.
export const MIN_FOLDED = 200
const SHOWN = 8
const DATA_URI = /data:image\/([\w.+-]+)((?:;[\w.+-]+=[^;,)\s]*)*);base64,([A-Za-z0-9+/=]+)/g

export function sizeLabel(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** The kind as a reader knows it: jpeg is JPEG, svg+xml SVG. */
export function kindLabel(subtype: string): string {
    const kind = subtype.toLowerCase().replace(/^x-/, '').replace(/\+xml$/, '')
    return (kind === 'jpg' ? 'jpeg' : kind).toUpperCase()
}

/** The base64 runs of data: picture addresses in a line long enough to fold. */
export function findDataUris(line: string): DataUriRun[] {
    if (!line.includes('data:image/')) return []
    const runs: DataUriRun[] = []
    DATA_URI.lastIndex = 0
    let m: RegExpExecArray | null
    while ((m = DATA_URI.exec(line))) {
        const encoded = m[3]
        if (encoded.length < MIN_FOLDED) continue
        const encodedStart = m.index + m[0].length - encoded.length
        const padding = encoded.endsWith('==') ? 2 : encoded.endsWith('=') ? 1 : 0
        const bytes = Math.floor(encoded.length * 3 / 4) - padding
        runs.push({ start: encodedStart + SHOWN, end: encodedStart + encoded.length, label: `${kindLabel(m[1])} · ${sizeLabel(bytes)}` })
    }
    return runs
}

const FOLD_CLASS = 'cm-data-uri-fold'

/**
 * Keeps a CodeMirror 5 editor's data: addresses folded: the lines a change touches are looked at again (the whole text
 * when it is replaced). Returns what stops it.
 */
export function foldDataUris(editor: any): () => void {
    const refold = (fromLine: number, toLine: number) => {
        const last = Math.min(toLine, editor.lastLine())
        for (const mark of editor.findMarks({ line: fromLine, ch: 0 }, { line: last + 1, ch: 0 }))
            if (mark.dataUriFold) mark.clear()
        for (let n = fromLine; n <= last; n++) {
            const text: string = editor.getLine(n)
            for (const run of findDataUris(text)) {
                const label = document.createElement('span')
                label.className = FOLD_CLASS
                label.textContent = `…[${run.label}]`
                label.title = run.label
                const mark = editor.markText({ line: n, ch: run.start }, { line: n, ch: run.end },
                    { replacedWith: label, atomic: true, inclusiveLeft: false, inclusiveRight: false, handleMouseEvents: true })
                mark.dataUriFold = true
            }
        }
    }
    const onChange = (_: any, change: any) => {
        const from = change.from.line
        refold(from, from + change.text.length - 1)
    }
    editor.operation(() => refold(editor.firstLine(), editor.lastLine()))
    editor.on('change', onChange)
    return () => {
        editor.off('change', onChange)
        for (const mark of editor.getAllMarks()) if (mark.dataUriFold) mark.clear()
    }
}
