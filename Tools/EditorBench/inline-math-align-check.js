// Inline formulas sit on the line's baseline (the user, 2026-11-26): span.ag-math was an inline-block aligned to the
// line's top, and its KaTeX to the middle, so a tall formula hung down - "对称轴 $y=x$ 与 $y=\dfrac{4}{3}$" had its two
// equals signs about 8 CSS px apart, a matrix's, a display sum's and an overbrace's 11, 15 and 19 px below the text.
// Each line here is "对称轴 $y=x$ 与 $y=<formula>$": its two equals signs (KaTeX's, the same font) at the same height,
// within 2 px, in the visual editor and reading mode, at zoom 1 and 1.5. Controls: plain formulas stay as they were, a
// ruby's base is level with the text, a block formula is a block. A formula clicked shows its source with the preview
// below it; at a narrow width a formula is not broken across lines. The user's sample: /root/Documents/Typeleaf-
// 行内公式对齐复现.md (the same formulas).
//
//   node inline-math-align-check.js
const { openEditor } = require('./harness');

const cases = [
  ['simple', 'x'], ['sup', 'x^2'], ['sub', 'x_i'], ['frac', '\\frac{4}{3}'], ['root', '\\sqrt{x}'],
  ['dfrac', '\\dfrac{4}{3}'], ['nested-root', '\\sqrt{\\dfrac{4}{3}}'],
  ['matrix', '\\begin{pmatrix}1&2\\\\3&4\\end{pmatrix}'], ['cases', '\\begin{cases}x & x\\ge 0\\\\-x & x<0\\end{cases}'],
  ['sum-limits', '\\displaystyle\\sum_{i=1}^{n}i'], ['int-large', '\\displaystyle\\int_0^1 x\\,dx'],
  ['overbrace', '\\overbrace{a+b+c}^{n}'],
];

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what} - ${detail}`); if (!ok) failures.push(what); };
  const md = cases.map(([, f]) => `对称轴 $y=x$ 与 $y=${f}$`).join('\n\n')
    + '\n\n对称轴 <ruby>对称轴<rt>duì chèn zhóu</rt></ruby> 对称轴\n\n$$\ny=\\dfrac{4}{3}\n$$\n\n结束\n';
  // The two equals signs' centres in each paragraph that has two formulas; the ruby's base against the text after it.
  const measure = () => page.evaluate(() => {
    const centre = r => r.top + r.height / 2;
    const eqOf = k => [...k.querySelectorAll('.mrel')].find(n => n.textContent === '=');
    const rows = [];
    for (const p of document.querySelectorAll('#ag-editor-id p')) {
      const ks = [...p.querySelectorAll('.ag-math .katex')];
      if (ks.length !== 2) continue;
      const [a, b] = ks.map(eqOf);
      if (!a || !b) { rows.push({ diff: null }); continue; }
      rows.push({ diff: centre(b.getBoundingClientRect()) - centre(a.getBoundingClientRect()) });
    }
    let ruby = null;
    const rb = document.querySelector('#ag-editor-id ruby');
    if (rb) {
      const base = document.createRange();
      const t = [...rb.childNodes].find(n => n.nodeType === 3 && n.textContent.trim());
      const host = rb.closest('p');
      const walker = document.createTreeWalker(host, NodeFilter.SHOW_TEXT);
      let after = null;
      while (walker.nextNode()) { const n = walker.currentNode; if (!rb.contains(n) && n.textContent.includes('对称轴')) after = n; }
      if (t && after) {
        base.selectNodeContents(t);
        const r2 = document.createRange(); const i = after.textContent.lastIndexOf('对称轴'); r2.setStart(after, i); r2.setEnd(after, i + 3);
        ruby = centre(base.getBoundingClientRect()) - centre(r2.getBoundingClientRect());
      }
    }
    const block = document.querySelector('#ag-editor-id .katex-display');
    return { rows, ruby, block: block ? getComputedStyle(block).display : null };
  });
  try {
    await page.setViewport({ width: 900, height: 1500, deviceScaleFactor: 1 });
    for (const [name, read] of [['visual', false], ['reading', true]]) {
      for (const zoom of [1, 1.5]) {
        await mode(false, read);
        await page.evaluate(z => { document.body.style.zoom = String(z) }, zoom);
        await load(md);
        await page.waitForFunction(n => document.querySelectorAll('#ag-editor-id .ag-math .katex').length >= n, { timeout: 15000 }, cases.length * 2);
        await page.evaluate(() => document.fonts.ready);
        await pause(500);
        // The visual editor: the caret in the last paragraph, every formula drawn (none with its source shown).
        if (!read) {
          await page.evaluate(() => { const ps = document.querySelectorAll('#ag-editor-id p'); const last = ps[ps.length - 1]; const r = last.getBoundingClientRect(); window.scrollTo(0, 0); return [r.right - 2, r.top + r.height / 2]; })
            .then(([x, y]) => page.mouse.click(x, y));
          await pause(400);
        }
        const m = await measure();
        const where = `${name}, zoom ${zoom}`;
        m.rows.forEach((r, i) => {
          const [id] = cases[i] || ['?'];
          check(r.diff !== null && Math.abs(r.diff) <= 2, `${where}: ${id} - its equals sign level with y=x's on the line`, r.diff === null ? 'no equals sign found' : `${r.diff.toFixed(2)} px`);
        });
        check(m.rows.length === cases.length, `${where}: every line measured`, `${m.rows.length} of ${cases.length}`);
        check(m.ruby !== null && Math.abs(m.ruby) <= 2, `${where}: a ruby's base level with the text beside it`, m.ruby === null ? 'not found' : `${m.ruby.toFixed(2)} px`);
        check(m.block === 'block', `${where}: a block formula is a block`, String(m.block));
      }
      await page.evaluate(() => { document.body.style.zoom = '' });
    }

    // Clicked into (the visual editor): the source shown, its preview below it.
    await mode(false, false);
    await load('对称轴 $y=\\dfrac{4}{3}$ 结束\n');
    await page.waitForSelector('#ag-editor-id .ag-math .katex');
    await pause(400);
    const box = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id .ag-math .katex').getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; });
    await page.mouse.click(box[0], box[1]);
    await pause(500);
    const opened = await page.evaluate(() => {
      const m = document.querySelector('#ag-editor-id span.ag-math');
      const text = m.querySelector('.ag-math-text'), render = m.querySelector('.ag-math-render');
      const t = text.getBoundingClientRect(), r = render.getBoundingClientRect();
      return { hidden: m.classList.contains('ag-hide'), textWidth: t.width, gap: r.top - t.bottom };
    });
    check(!opened.hidden && opened.textWidth > 20, 'clicked: the formula shows its source', JSON.stringify(opened));
    check(opened.gap >= -1, 'clicked: the preview below the source', `${opened.gap.toFixed(1)} px`);

    // Narrow: a formula is not broken across lines.
    await page.setViewport({ width: 360, height: 900, deviceScaleFactor: 1 });
    await load('对称轴 对称轴 对称轴 对称轴 $y=\\begin{pmatrix}1&2\\\\3&4\\end{pmatrix}+\\overbrace{a+b+c}^{n}$ 对称轴 $y=\\dfrac{4}{3}$ 对称轴 对称轴\n\n结束\n');
    await page.waitForSelector('#ag-editor-id .ag-math .katex');
    await pause(500);
    // One line: all its parts overlap in height (on two lines, the second's are wholly below the first's).
    const lines = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-math .katex-html')].map(k => {
      const rs = [...k.querySelectorAll(':scope > .base')].map(b => b.getBoundingClientRect());
      return Math.min(...rs.map(r => r.bottom)) - Math.max(...rs.map(r => r.top));
    }));
    check(lines.length >= 2 && lines.every(n => n > 0), 'narrow: no formula broken across lines', JSON.stringify(lines.map(n => Math.round(n))));
    check(errors.length === 0, 'no page errors', JSON.stringify(errors));
  } finally {
    await close();
  }
  process.exitCode = failures.length ? 1 : 0;
})().catch(e => { console.error(e); process.exitCode = 1 });
