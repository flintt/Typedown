// Every value the typography guides show on a block, traced to the designer's field that sets it: the designer's CSS
// with a value of its own in each field (Tests: TypographyDesign.Css), a document with every kind of block, each block
// hovered; a value no field gives is one the small panel cannot change (the user, 2026-11-06: "values on the page I
// cannot find in the panel").
//
//   node typography-guides-audit.js [the designer's CSS file]
// fixtures/typography-audit.css is TypographyDesign.Css() with the values in FIELDS below (FontSize 16, LineHeight 1.6,
// Width 901, ParagraphBefore 1.1, ParagraphAfter 1.35, FirstLineIndent 2.5, HeadingBefore 0.7, HeadingAfter 0.9,
// BlockSpacing 1.7, ListIndent 3.1, ListItemSpacing 0.45, QuoteIndent 1.9, QuoteBarWidth 4): made again when the
// designer's rules change.
const fs = require('fs');
const { openEditor } = require('./harness');

const css = fs.readFileSync(process.argv[2] || require('path').join(__dirname, 'fixtures/typography-audit.css'), 'utf8').split('\n').filter(l => l.includes('#ag-editor-id')).join('\n');
// The fields' values in the CSS (em unless px), and where the small panel has them.
const FIELDS = [
  ['ParagraphBefore', 1.1], ['ParagraphAfter', 1.35], ['Indent (first line)', 2.5], ['HeadingBefore', 0.7],
  ['HeadingAfter', 0.9], ['BlockSpacing', 1.7], ['ListIndent', 3.1], ['ListItemSpacing', 0.45], ['QuoteIndent', 1.9],
];
const doc = [
  '# Heading 1', '', 'A paragraph of body text, long enough to wrap onto a second line in the editor at this width, with more words.', '',
  '## Heading 2', '', 'Another paragraph.', '', '### Heading 3', '', '#### Heading 4', '', '##### Heading 5', '', '###### Heading 6', '',
  '- An item', '- Another item', '  - A nested item', '  - Another nested', '- Back out', '',
  '1. First', '2. Second', '', '- [ ] A task', '- [x] Done', '',
  '- An item with two paragraphs', '', '  Its second paragraph.', '',
  '> A quote.', '>', '> Its second paragraph.', '', '> Outer', '>', '> > Nested quote', '', '> - A list in a quote', '',
  '```js', 'let code = 1', '```', '', '| A | B |', '| - | - |', '| 1 | 2 |', '', '$$', 'x^2', '$$', '', '---', '',
  '<div>An HTML block</div>', '', 'The last paragraph.', '',
].join('\n');

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  try {
    await mode(false, false);
    await load(doc);
    await pause(800);
    await page.addStyleTag({ content: css });
    await page.evaluate(() => window.__deliver('TypographyGuides', { on: true }));
    await pause(200);
    // Every block the guides can be on, by its innermost element under the pointer.
    const targets = await page.evaluate(() => [...document.getElementById('ag-editor-id')
      .querySelectorAll('p, h1, h2, h3, h4, h5, h6, li, blockquote, pre, figure, table, hr')]
      .filter(e => e.id && e.getBoundingClientRect().height >= 4)
      .map(e => ({ id: e.id, label: e.tagName.toLowerCase() + '#' + e.id + ' "' + (e.textContent || '').trim().slice(0, 24) + '"' })));
    const seen = new Set();
    let unmatched = 0;
    for (const t of targets) {
      // Into view, then a point in it and not in a block inside it.
      const at = await page.evaluate(id => {
        const e = document.getElementById(id);
        e.scrollIntoView({ block: 'center' });
        const r = e.getBoundingClientRect();
        return { x: Math.round(r.left + Math.min(30, r.width / 3)), y: Math.round(r.top + Math.min(8, r.height / 2)) };
      }, t.id);
      await pause(60);
      // The caret's block is the one shown.
      await page.mouse.click(at.x, at.y);
      await pause(80);
      const g = await page.evaluate(() => window.__typographyGuides && window.__typographyGuides());
      if (!g) { console.log(`  NO   ${t.label}: no guides`); unmatched++; continue; }
      const id = `${g.kind}:${g.before}:${g.after}:${g.indent}:${g.firstLine}`;
      if (seen.has(id)) continue;
      seen.add(id);
      const name = v => {
        if (!v) return '0';
        const em = v / g.fontSize;
        const near = (a, b) => Math.abs(a - b) * g.fontSize <= 0.75;
        const hit = FIELDS.find(([, f]) => near(em, f));
        // An indent sums: a nested list's, a list's in a quote.
        const sum = FIELDS.flatMap(([a, f]) => FIELDS.map(([b, h]) => [`${a}+${b}`, f + h])).find(([, s]) => near(em, s));
        const twice = FIELDS.find(([, f]) => near(em, 2 * f));
        return hit ? hit[0] : twice ? `2×${twice[0]}` : sum ? sum[0] : `?? ${+em.toFixed(2)}em (${Math.round(v)}px)`;
      };
      const parts = { before: name(g.before), after: name(g.after), indent: name(g.indent), firstLine: name(g.firstLine) };
      const bad = Object.entries(parts).filter(([, v]) => v.startsWith('??'));
      unmatched += bad.length;
      console.log(`  ${bad.length ? 'NO  ' : 'ok  '} ${g.kind.padEnd(10)} ${t.label.padEnd(40)} before ${parts.before}; after ${parts.after}; indent ${parts.indent}; first line ${parts.firstLine}`);
    }
    console.log(unmatched ? `${unmatched} value(s) no field sets` : 'every value a field sets');
    process.exitCode = unmatched ? 1 : 0;
  } finally {
    await close();
  }
})();
