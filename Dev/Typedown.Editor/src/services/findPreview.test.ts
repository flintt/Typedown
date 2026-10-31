import { FENCE, isTableRow, lineMarksLength, previewOf, PREVIEW_CONTEXT, stripMarks } from './findPreview';

/** The preview with its match in brackets, to read at a glance. */
const shown = (text: string, word: string, opts = {}) => {
    const at = text.indexOf(word);
    if (at < 0) throw new Error(`no ${word} in ${text}`);
    const p = previewOf(text, at, at + word.length, opts);
    return p.text.slice(0, p.start) + '[' + p.text.slice(p.start, p.end) + ']' + p.text.slice(p.end);
};

describe('stripMarks', () => {
    it.each([
        ['a **bold** and *em* word', 'a bold and em word'],
        ['__strong__ snake_case_name', 'strong snake_case_name'],
        ['~~gone~~ and ==marked==', 'gone and marked'],
        ['a `code *star*` span', 'a code *star* span'],
        ['see [the site](https://example.com/a_b "T") now', 'see the site now'],
        ['an ![alt text](img.png) here', 'an alt text here'],
        ['ref [text][id] and footnote[^1]', 'ref text and footnote[^1]'],
        ['press <kbd>Ctrl</kbd> or <https://auto.link>', 'press Ctrl or https://auto.link'],
        ['\\*not em\\* 2 * 3 and a == b', '*not em* 2 * 3 and a == b'],
        ['Hello![x] stays', 'Hello![x] stays'],
    ])('%s', (text, plain) => {
        expect(stripMarks(text).plain).toBe(plain);
    });

    it('drops a heading\'s hashes when asked', () => {
        expect(stripMarks('## A **title**', true).plain).toBe('A title');
        expect(stripMarks('## A title').plain).toBe('## A title');
    });

    it('counts the kept characters before each place', () => {
        const s = stripMarks('a **b** c');
        expect(s.plain).toBe('a b c');
        expect(s.kept[4]).toBe(2); // before "b"
        expect(s.kept[5]).toBe(3); // after "b"
    });
});

describe('previewOf', () => {
    it('marks the match in the text without its marks', () => {
        expect(shown('Some **needle** here', 'needle')).toBe('Some [needle] here');
        expect(shown('# The needle heading', 'needle', { heading: true })).toBe('The [needle] heading');
        expect(shown('a [needle link](http://x/needle) b', 'needle')).toBe('a [needle] link b');
    });

    it('keeps code as written', () => {
        expect(shown('let **needle** = 1', 'needle', { raw: true })).toBe('let **[needle]** = 1');
    });

    it('shows a match inside the marks in the text as written', () => {
        // The match is in the link's address only: nothing of it is left in the plain text.
        const text = 'see [site](http://needle.example)';
        expect(shown(text, 'needle')).toBe(text.replace('needle', '[needle]'));
    });

    it('cuts the context at PREVIEW_CONTEXT characters each side, with an ellipsis', () => {
        const left = 'x'.repeat(100);
        const right = 'y'.repeat(100);
        const p = previewOf(`${left}needle${right}`, 100, 106);
        expect(p.text).toBe(`…${'x'.repeat(PREVIEW_CONTEXT)}needle${'y'.repeat(PREVIEW_CONTEXT)}…`);
        expect(p.text.slice(p.start, p.end)).toBe('needle');
    });

    it('puts runs of whitespace as one space', () => {
        expect(shown('a\n\n   b   needle\tc', 'needle', { raw: true })).toBe('a b [needle] c');
    });

    it('leaves a source line\'s own marks out', () => {
        const line = '> - [ ] a **needle** item';
        const lead = lineMarksLength(line);
        expect(line.slice(lead)).toBe('a **needle** item');
        expect(shown(line, 'needle', { lead })).toBe('a [needle] item');
        expect(lineMarksLength('### Heading')).toBe(4);
        expect(lineMarksLength('12. item')).toBe(4);
        expect(lineMarksLength('plain')).toBe(0);
    });

    it('leaves a source table row\'s cell walls out', () => {
        expect(shown('| a needle cell | b |', 'needle', { table: true })).toBe('a [needle] cell b');
        expect(isTableRow('| a | b |')).toBe(true);
        expect(isTableRow('a | b')).toBe(false);
        expect(FENCE.test('```js')).toBe(true);
    });

    it('cuts a very long match', () => {
        const long = 'z'.repeat(500);
        const p = previewOf(`a ${long} b`, 2, 502);
        expect(p.end - p.start).toBe(120);
        expect(p.text.endsWith('…')).toBe(true);
    });
});
