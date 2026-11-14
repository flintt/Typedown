jest.mock('execall', () => (re: RegExp, s: string) => [...s.matchAll(re)].map(m => ({ match: m[0], index: m.index, subMatches: m.slice(1) })))
const CodeMirror = require('codemirror')
import { findDataUris, foldDataUris, foldDataUrisInHtml, kindLabel, MIN_FOLDED, sizeLabel } from './dataUriFold'

const encoded = (n: number) => 'iVBORw0K' + 'A'.repeat(n - 8)

test('a long data address is folded after its first characters, labelled with its kind and size', () => {
    const data = encoded(4000)
    const line = `Before ![shot](data:image/png;base64,${data}) after`
    const [run, ...rest] = findDataUris(line)
    expect(rest).toEqual([])
    const start = line.indexOf(data)
    expect(run.start).toBe(start + 8)
    expect(run.end).toBe(start + data.length)
    expect(line.slice(run.end, run.end + 1)).toBe(')')
    expect(run.label).toBe('PNG · 3 KB')
})

test('in the visual editor\'s HTML of a code line (a raw HTML block\'s <img>): the same label, the text whole', () => {
    const data = encoded(4000)
    // As Prism highlights it: the address inside an attribute value's span.
    const html = `<span class="token attr-value">="data:image/png;base64,${data}"</span> alt=""&gt;`
    const div = document.createElement('div')
    div.innerHTML = foldDataUrisInHtml(html)
    const fold = div.querySelector('.ag-data-uri-fold') as HTMLElement
    expect(fold.getAttribute('contenteditable')).toBe('false')
    expect(fold.dataset.label).toBe('…[PNG · 3 KB]')
    expect(fold.textContent).toBe(data.slice(8))
    const plain = document.createElement('div')
    plain.innerHTML = html
    expect(div.textContent).toBe(plain.textContent)
    expect(foldDataUrisInHtml('<span>no picture</span>')).toBe('<span>no picture</span>')
})

test('a short one (an icon) is left as text, and other addresses are not touched', () => {
    expect(findDataUris(`![i](data:image/gif;base64,${encoded(MIN_FOLDED - 1)})`)).toEqual([])
    expect(findDataUris('![a](images/a.png) data:text/plain;base64,' + 'A'.repeat(500))).toEqual([])
})

test('two in a line, with parameters and other kinds', () => {
    const line = `![a](data:image/jpeg;base64,${encoded(400)}) ![b](data:image/svg+xml;charset=utf-8;base64,${encoded(300)}==)`
    expect(findDataUris(line).map(r => r.label)).toEqual(['JPEG · 300 B', 'SVG · 224 B'])
})

test('labels', () => {
    expect(kindLabel('jpg')).toBe('JPEG')
    expect(kindLabel('x-icon')).toBe('ICON')
    expect(sizeLabel(1536 * 1024)).toBe('1.5 MB')
})

test('in the editor: folded, kept whole in the text, and folded again after an edit', () => {
    // jsdom lays nothing out; CodeMirror measures text with ranges.
    const empty = () => ({ left: 0, right: 0, top: 0, bottom: 0, width: 0, height: 0, x: 0, y: 0, toJSON() { } })
    ;(document as any).createRange ??= () => new Range()
    Range.prototype.getBoundingClientRect = empty as any
    Range.prototype.getClientRects = (() => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] })) as any
    const host = document.createElement('div')
    document.body.appendChild(host)
    const data = encoded(1000)
    const text = `# T\n\n![p](data:image/png;base64,${data})\n`
    const editor = CodeMirror(host, { value: text })
    const stop = foldDataUris(editor)
    const folds = () => editor.getAllMarks().filter((m: any) => m.dataUriFold)
    expect(folds()).toHaveLength(1)
    expect(editor.getValue()).toBe(text)
    // Typed before the picture on its line: the line looked at again, still one fold, the text whole.
    editor.replaceRange('x', { line: 2, ch: 0 })
    expect(folds()).toHaveLength(1)
    expect(editor.getValue()).toBe('# T\n\nx' + text.slice(5))
    // The address removed: no fold left.
    editor.setValue('plain\n')
    expect(folds()).toHaveLength(0)
    editor.setValue(text)
    expect(folds()).toHaveLength(1)
    stop()
    expect(folds()).toHaveLength(0)
})

test('a search does not find what is inside a picture\'s base64, and a count does not count it', () => {
    const { matchString } = require('./common')
    const { wordCount } = require('components/Muya/lib/utils/wordCount')
    // "AAAA" and "iVBO" are in the encoding; "AAAA" is also in the text after it.
    const text = `Before ![shot](data:image/png;base64,${encoded(400)}) AAAA after`
    const found = matchString(text, 'AAAA', { searchIsCaseSensitive: true })
    expect(found.map((m: any) => m.index)).toEqual([text.lastIndexOf('AAAA')])
    expect(matchString(text, 'iVBO', {})).toEqual([])
    expect(matchString(text, 'shot', {}).length).toBe(1)
    const counted = wordCount(text)
    const plain = wordCount('Before shot AAAA after')
    expect(counted.word).toBe(plain.word)
    expect(counted.character).toBe(plain.character)
})
