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
    await load('# A heading\n\nA paragraph of text, long enough to wrap a little in the editor, with more words after it.\n\n- An item in a list\n- Another item\n\n> A quote.\n\n```js\nlet x = 1\n```\n\nThe last paragraph.\n');
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
    // The page's width as the designer's Page width sets it (the whole page, its margins with it): set to 800, 800.
    await page.evaluate(() => document.body.style.setProperty('--editorAreaWidth', '800px'));
    await page.mouse.move(at.x + 2, at.y);
    await pause(200);
    g = await guide();
    report(g && Math.round(g.width) === 800, 'the page\'s width as Page width sets it (800, its margins with it)', `${g && g.width}`);
    const margins = await page.evaluate(() => [...document.querySelectorAll('#td-typography-guides > div')].filter(d => d.style.background.includes('0.07')).length);
    report(margins === 2, 'its margins each side tinted', String(margins));
    const labels = await page.evaluate(() => [...document.querySelectorAll('#td-typography-guides span')].map(s => s.textContent));
    report(labels.some(l => l.startsWith('↑') && l.includes('12px')) && labels.some(l => l.startsWith('↓') && l.includes('20px')) && labels.some(l => l.startsWith('↔')),
      'the marks name the values', JSON.stringify(labels));

    // Each value a field sets is a label to click, naming that field; the pointer on it keeps the guides; a click sends
    // the block and the field for the small panel to show (the user: where is this value in the panel?).
    const fields = await page.evaluate(() => [...document.querySelectorAll('#td-typography-guides [data-field]')].map(t => t.dataset.field).sort());
    report(JSON.stringify(fields) === JSON.stringify(['Indent', 'ParagraphAfter', 'ParagraphBefore', 'Width']), 'a paragraph\'s labels name their fields', JSON.stringify(fields));
    // The host's small panel over a label: the label keeps out from under it, to its left (under it, a click meant for the
    // label went into the panel - hp jobs 274-277).
    const before = await page.evaluate(() => document.querySelector('#td-typography-guides [data-field="ParagraphBefore"]').getBoundingClientRect().toJSON());
    await page.evaluate(r => window.__deliver('TypographyPanel', { x: r.left - 20, y: r.top - 40, width: 260, height: 300 }), before);
    await pause(150);
    const moved = await page.evaluate(() => document.querySelector('#td-typography-guides [data-field="ParagraphBefore"]').getBoundingClientRect().toJSON());
    report(moved.right <= before.left - 20, 'a label under the small panel moves to its left', `${JSON.stringify(before)} -> ${JSON.stringify(moved)}`);
    await page.evaluate(() => window.__deliver('TypographyPanel', null));
    await pause(150);
    const labelAt = await page.evaluate(() => { const r = document.querySelector('#td-typography-guides [data-field="ParagraphBefore"]').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
    await page.mouse.move(labelAt.x, labelAt.y);
    await pause(150);
    report(await marks() > 0 && (await guide())?.kind === 'p', 'the pointer on a label keeps the guides', String(await marks()));
    await page.mouse.down();
    await page.mouse.up();
    await pause(200);
    const clicked = await page.evaluate(() => window.__last.TypographyField);
    report(clicked && clicked.field === 'ParagraphBefore' && clicked.kind === 'p' && clicked.group === 'body', 'a label clicked: its block and field sent', JSON.stringify(clicked && { field: clicked.field, kind: clicked.kind, group: clicked.group }));

    at = await centre('ul li p');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    g = await guide();
    report(g && g.kind === 'li' && g.indent >= 40, 'a list item\'s paragraph: the item measured (what List item spacing sets), its indent', JSON.stringify(g));
    const listFields = await page.evaluate(() => [...document.querySelectorAll('#td-typography-guides [data-field]')].map(t => t.dataset.field).sort());
    report(listFields.includes('ListIndent') && !listFields.includes('ParagraphAfter'), 'a list item\'s labels: List indent, none for what no field sets', JSON.stringify(listFields));

    // A quote measured whole: its spacing and its indent (Quote indent's padding), not its paragraph's.
    await page.addStyleTag({ content: '#ag-editor-id blockquote { padding-left: 30px !important; }' });
    at = await centre('blockquote p');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    g = await guide();
    report(g && g.kind === 'blockquote' && g.indent >= 30, 'a quote: measured whole, its indent as Quote indent sets it', JSON.stringify(g));
    // Code: the editor's pre, which Block spacing sets (a figure only for tables, formulas, diagrams).
    await page.addStyleTag({ content: '#ag-editor-id > pre { margin-top: 22px !important; margin-bottom: 22px !important; }' });
    at = await centre('pre');
    await page.mouse.move(at.x, at.y);
    await pause(150);
    g = await guide();
    report(g && g.kind === 'pre' && g.before === 22 && g.after === 22, 'code: its block measured, its spacing as Block spacing sets it', JSON.stringify(g));

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
    // What each field is on the page now, sent with it (an empty field shows it): the paragraph's own, the page's width.
    const e = b && b.effective;
    report(e && e.ParagraphBefore === 0.75 && e.ParagraphAfter === 1.25 && e.Indent === 2 && e.Width > 0 && e.Size > 0,
      'and what its fields are on the page now (12 px, 20 px, 2em at 16 px; the width)', JSON.stringify(e));
    // The styles changed: sent again, with the new value.
    await page.addStyleTag({ content: '#ag-editor-id p.ag-paragraph { margin-top: 24px !important; }' });
    await pause(250);
    report((await block())?.effective?.ParagraphBefore === 1.5, 'the styles changed: sent again with the new value (24 px = 1.5em)', JSON.stringify((await block())?.effective));
    // Several keys, a moment apart: each draws the paragraph again as a new element (the same id), still the block
    // typed in (w10 job 254: the panel came back at each key).
    await page.keyboard.type('xyz', { delay: 120 });
    await pause(400);
    report(await block() === null, 'typing: no block (the panel goes), and none again as the block is drawn anew', JSON.stringify(await block()));
    await page.keyboard.press('ArrowLeft');
    await pause(200);
    report(await block() === null, 'the caret moved along the same paragraph: still none', JSON.stringify(await block()));
    // Text from an input method: no key with its character (it comes as "Process"), only the text put in.
    await page.mouse.click(at.x, at.y);
    await pause(200);
    report((await block())?.kind === 'p', 'a click: the block (before the input method)', JSON.stringify(await block()));
    await page.keyboard.sendCharacter('中');
    await pause(300);
    report(await block() === null, 'text from an input method: no block (the panel goes)', JSON.stringify(await block()));
    await page.mouse.click(at.x, at.y);
    await pause(200);
    b = await block();
    report(b && b.kind === 'p', 'a click: the block again', JSON.stringify(b));
    // A click back into the same block sends it again (the host's panel gave way to the click and comes back).
    const sentBefore = await page.evaluate(() => window.__count.TypographyBlock || 0);
    await page.mouse.click(at.x + 5, at.y);
    await pause(200);
    const sentAfter = await page.evaluate(() => window.__count.TypographyBlock || 0);
    report(sentAfter > sentBefore && (await block())?.kind === 'p', 'a click into the block the caret is in: the block sent again', `${sentBefore} -> ${sentAfter}`);

    // Each block's group, as the designer groups its fields - in reading mode too, where the editor sends no
    // paragraph state (the user: there the panel came but kept the kind it had).
    for (const readOnly of [false, true]) {
      await mode(false, readOnly);
      await pause(300);
      const groups = [];
      for (const [selector, expected] of [['h1', 'headings'], ['ul li p', 'lists'], ['blockquote p', 'quote'], ['p.ag-paragraph:not(li p):not(blockquote p)', 'body']]) {
        const point = await centre(selector);
        await page.mouse.click(point.x, point.y);
        await pause(200);
        const got = (await block())?.group;
        groups.push(`${selector}: ${got}`);
        report(got === expected, `${readOnly ? 'reading mode' : 'edit mode'}: a click into ${selector} sends the group ${expected}`, String(got));
      }
    }
    await mode(false, false);

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
