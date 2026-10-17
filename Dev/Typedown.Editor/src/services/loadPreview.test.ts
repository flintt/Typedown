import { anchorKey, findAnchorLine, inlineHtml, previewHtml, previewStart } from './loadPreview'

describe('previewStart', () => {
    const lines = ['# Title', '', 'one', 'two', '', '```', 'code', '', 'more', '```', '', 'after']

    it('starts at the block the line is in', () => {
        expect(previewStart(lines, 3)).toBe(2)
        expect(previewStart(lines, 11)).toBe(11)
        expect(previewStart(lines, 0)).toBe(0)
    })

    it('starts a line inside a fence at the fence, blank lines in it or not', () => {
        expect(previewStart(lines, 8)).toBe(5)
        expect(previewStart(lines, 6)).toBe(5)
    })

    it('keeps to the document for a line past its end', () => {
        expect(previewStart(lines, 999)).toBe(11)
        expect(previewStart(lines, -3)).toBe(0)
    })
})

describe('inlineHtml', () => {
    it('draws emphasis, code and links, and drops pictures', () => {
        expect(inlineHtml('a **b** and *c* `d<e>` [f](http://x) ![g](y.png) ~~h~~'))
            .toBe('a <strong>b</strong> and <em>c</em> <code>d&lt;e&gt;</code> f  <del>h</del>')
    })

    it('shows raw HTML as its text, never as markup', () => {
        expect(inlineHtml('<span style="color:red">red</span> <script>x</script> 1 < 2')).toBe('red x 1 &lt; 2')
    })
})

describe('previewHtml', () => {
    it('draws the common blocks', () => {
        const html = previewHtml('# H1\n\npara one\nline two\n\n- a\n- b\n\n1. c\n\n> quote\n\n---\n\nSetext\n===\n\n```js\nlet x = 1 < 2\n```\n')
        expect(html).toBe([
            '<h1>H1</h1>',
            '<p>para one<br>line two</p>',
            '<ul><li>a</li><li>b</li></ul>',
            '<ol><li>c</li></ol>',
            '<blockquote><p>quote</p></blockquote>',
            '<hr>',
            '<h1>Setext</h1>',
            '<pre><code>let x = 1 &lt; 2</code></pre>',
        ].join('\n'))
    })

    it('starts at the caret\'s block and leaves the front matter out', () => {
        expect(previewHtml('---\ntitle: x\n---\n# Top\n')).toBe('<h1>Top</h1>')
        expect(previewHtml('# Top\n\nfirst\n\nsecond\nstill', 5)).toBe('<p>second<br>still</p>')
    })

    it('stops about the given length in', () => {
        const text = Array.from({ length: 2000 }, (_, i) => `Paragraph ${i} with some words.`).join('\n\n')
        const html = previewHtml(text, 0, 3000)
        expect(html.length).toBeLessThan(4000)
        expect(html).toContain('Paragraph 0 ')
        expect(html).not.toContain('Paragraph 500 ')
    })
})

describe('scroll anchor', () => {
    const lines = ['# Title', '', 'First **para**graph here.', '', '## Second heading', '', '- item one', '- item two', '', 'Same text', '', 'Same text']

    it('compares the page\'s text and the source without marks, spaces or case', () => {
        expect(anchorKey('## Second  Heading')).toBe('second heading')
        expect(anchorKey('First **para**graph here.')).toBe(anchorKey('First paragraph here.'))
        expect(anchorKey('- item one')).toBe('item one')
    })

    it('finds the block\'s line, the nearest to where the page was', () => {
        expect(findAnchorLine(lines, { text: 'Second heading', top: 0, at: 0 })).toBe(4)
        expect(findAnchorLine(lines, { text: 'First paragraph here. More', top: 0, at: 0.9 })).toBe(2)
        expect(findAnchorLine(lines, { text: 'item two', top: 0, at: 0.5 })).toBe(7)
        expect(findAnchorLine(lines, { text: 'Same text', top: 0, at: 1 })).toBe(11)
        expect(findAnchorLine(lines, { text: 'Same text', top: 0, at: 0.7 })).toBe(9)
    })

    it('finds nothing for text not there, or too short to tell', () => {
        expect(findAnchorLine(lines, { text: 'Nowhere to be found', top: 0, at: 0 })).toBe(-1)
        expect(findAnchorLine(lines, { text: 'ab', top: 0, at: 0 })).toBe(-1)
        expect(findAnchorLine(lines, null)).toBe(-1)
    })

    it('marks the block holding the line, a list\'s on its item', () => {
        expect(previewHtml(lines.join('\n'), 0, 12000, 4)).toContain('<h2 data-anchor>Second heading</h2>')
        expect(previewHtml(lines.join('\n'), 0, 12000, 7)).toContain('<li>item one</li><li data-anchor>item two</li>')
        expect(previewHtml(lines.join('\n'), 0, 12000, 2)).toContain('<p data-anchor>First')
        expect((previewHtml(lines.join('\n'), 0, 12000, 4).match(/data-anchor/g) || []).length).toBe(1)
    })

    it('draws as far as the marked line whatever the length', () => {
        const text = Array.from({ length: 400 }, (_, i) => `Paragraph ${i} with some words in it.`).join('\n\n')
        expect(previewHtml(text, 0, 1000, 600)).toContain('<p data-anchor>Paragraph 300 ')
    })
})
