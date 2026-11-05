// The typography designer's guides (services/typographyGuides.ts): with them on, the block under the pointer shows its
// space before and after, its indent and the text's width, the values those of its computed style; a list item's
// paragraph its item's indent; the marks take no click; typing hides them; off, nothing is left.
//
//   node typography-guides-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    await mode(false, false);
    await load('# A heading\n\nA paragraph of text, long enough to wrap a little in the editor, with more words after it.\n\n- An item in a list\n- Another item\n\n> A quote.\n\nThe last paragraph.\n');
    await pause(600);
    // A preset's rules, as the designer writes them.
    await page.addStyleTag({ content: '#ag-editor-id p.ag-paragraph { margin-top: 12px !important; margin-bottom: 20px !important; text-indent: 2em; } #ag-editor-id ul { padding-left: 40px !important; }' });
    await pause(200);
    const centre = selector => page.evaluate(sel => {
      const e = document.querySelector('#ag-editor-id ' + sel);
      const r = e.getBoundingClientRect();
      return { x: Math.round(r.left + Math.min(60, r.width / 2)), y: Math.round(r.top + r.height / 2) };
    }, selector);
    const guide = () => page.evaluate(() => window.__typographyGuides());
    const marks = () => page.evaluate(() => document.querySelectorAll('#td-typography-guides > *').length);

    let at = await centre('p.ag-paragraph:not(li p)');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    report(await guide() === null && await marks() === 0, 'off: no guides', JSON.stringify(await guide()));

    await page.evaluate(() => window.__deliver('TypographyGuides', { on: true }));
    await page.mouse.move(at.x + 1, at.y);
    await pause(150);
    let g = await guide();
    report(g && g.kind === 'p' && g.before === 12 && g.after === 20, 'a paragraph: its space before and after', JSON.stringify(g));
    report(g && Math.round(g.firstLine) === Math.round(2 * g.fontSize), 'and its first line\'s indent (2em)', JSON.stringify(g));
    const width = await page.evaluate(() => { const e = document.getElementById('ag-editor-id'); const s = getComputedStyle(e); return e.clientWidth - parseFloat(s.paddingLeft) - parseFloat(s.paddingRight); });
    report(g && Math.abs(g.width - width) < 1, 'the text\'s width', `${g && g.width} / ${width}`);
    const labels = await page.evaluate(() => [...document.querySelectorAll('#td-typography-guides span')].map(s => s.textContent));
    report(labels.some(l => l.startsWith('↑') && l.includes('12px')) && labels.some(l => l.startsWith('↓') && l.includes('20px')) && labels.some(l => l.startsWith('↔')),
      'the marks name the values', JSON.stringify(labels));

    at = await centre('ul li p');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    g = await guide();
    report(g && g.kind === 'p' && g.indent >= 40, 'a list item\'s paragraph: the item\'s indent', JSON.stringify(g));

    // A click through the marks reaches the text: the caret goes there.
    at = await centre('blockquote p');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    await page.mouse.click(at.x, at.y);
    await pause(150);
    const inQuote = await page.evaluate(() => !!window.getSelection().anchorNode?.parentElement?.closest('blockquote'));
    report(inQuote, 'a click through the marks puts the caret in the text under them', String(inQuote));

    await page.keyboard.press('ArrowRight');
    await pause(150);
    report(await marks() === 0, 'a key pressed hides them', String(await marks()));

    // The caret's block for the small panel beside it (TypographyBlock): its rect and kind, a heading's level; none
    // while typing, back at a click.
    const block = () => page.evaluate(() => window.__last.TypographyBlock);
    const rectOf = selector => page.evaluate(sel => { const r = document.querySelector('#ag-editor-id ' + sel).getBoundingClientRect(); return { x: r.left, y: r.top, width: r.width, height: r.height }; }, selector);
    at = await centre('h1');
    await page.mouse.click(at.x, at.y);
    await pause(200);
    let b = await block();
    const h1 = await rectOf('h1');
    report(b && b.kind === 'h1' && b.level === 1 && Math.abs(b.y - h1.y) < 1 && Math.abs(b.height - h1.height) < 1, 'a click into a heading: its block and level', JSON.stringify(b));
    at = await centre('p.ag-paragraph:not(li p)');
    await page.mouse.click(at.x, at.y);
    await pause(200);
    b = await block();
    report(b && b.kind === 'p' && b.level === 0, 'into a paragraph: that block', JSON.stringify(b));
    await page.keyboard.type('x');
    await pause(200);
    report(await block() === null, 'typing: no block (the panel goes)', JSON.stringify(await block()));
    await page.keyboard.press('ArrowLeft');
    await pause(200);
    report(await block() === null, 'the caret moved along the same paragraph: still none', JSON.stringify(await block()));
    await page.mouse.click(at.x, at.y);
    await pause(200);
    b = await block();
    report(b && b.kind === 'p', 'a click: the block again', JSON.stringify(b));

    await page.evaluate(() => window.__deliver('TypographyGuides', { on: false }));
    await pause(150);
    report(await block() === null, 'off: no block', JSON.stringify(await block()));
    await page.mouse.move(at.x + 2, at.y);
    await pause(150);
    report(await page.evaluate(() => !document.getElementById('td-typography-guides')) && await guide() === null, 'off again: nothing left', '');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
