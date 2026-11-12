// Long code blocks and tables fold to their first lines (a button by the copy button), and stay folded when the document
// is opened again: a view of the document, not part of it - the file is not changed, and the export shows them whole.
// Remembered here, in the page's own storage, by the document's path and each block's kind and first line (a block whose
// first line is changed opens again). A document not saved yet has nothing remembered.

/** A code block or a table this long (lines, rows) has the button. */
export const FOLD_MIN_LINES = 15

const STORE = 'typedown.foldedBlocks'
const MAX_DOCUMENTS = 200

let documentKey = ''
let folded = new Set<string>()

type Stored = Record<string, { at: number, blocks: string[] }>

function read(): Stored {
    try { return JSON.parse(window.localStorage.getItem(STORE) || '{}') || {} } catch { return {} }
}

function write(all: Stored) {
    const keys = Object.keys(all)
    if (keys.length > MAX_DOCUMENTS)
        keys.sort((a, b) => all[a].at - all[b].at).slice(0, keys.length - MAX_DOCUMENTS).forEach(k => delete all[k])
    // Storage full or off: the folds are kept for this session only.
    try { window.localStorage.setItem(STORE, JSON.stringify(all)) } catch { return }
}

/** The document shown (its path; none for one not saved yet): its folded blocks read. */
export function setFoldDocument(path: string | null | undefined) {
    documentKey = (path || '').toLowerCase()
    folded = new Set(documentKey ? read()[documentKey]?.blocks ?? [] : [])
}

/** What a block is known by: its kind and its first line. */
export function blockFingerprint(kind: string, text: string | undefined): string {
    const first = (text || '').split('\n').find(l => l.trim().length > 0) ?? ''
    return `${kind}:${first.trim().slice(0, 120)}`
}

export const isFolded = (fingerprint: string) => folded.has(fingerprint)

/** The block folded or opened; remembered for the document. Returns whether it is folded now. */
export function toggleFold(fingerprint: string): boolean {
    const now = !folded.has(fingerprint)
    if (now) folded.add(fingerprint)
    else folded.delete(fingerprint)
    if (documentKey) {
        const all = read()
        if (folded.size) all[documentKey] = { at: Date.now(), blocks: [...folded] }
        else delete all[documentKey]
        write(all)
    }
    return now
}
