// The caret in a table stays where it is when a press's release was missed (a reader's report: editing a table, the
// caret now and then vanished). A press in a cell listens for the pointer until the release; a release that never came
// (let go outside the window, or taken by another handler) left it listening, and the pointer later merely passing over
// another cell, no button held, blurred the editor and started a cell selection nobody made.
// - a press in a cell, no release, then the pointer over the other cells with no button: the caret stays in the cell, no
//   cell is selected, typing goes into the cell;
// - a drag across cells still selects them (the range as before);
// - a drag whose release was missed is taken as released at the next move: the range kept, the pointer then free.
//
//   node table-caret-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = '| Name | Age | City |\n| --- | --- | --- |\n| Ann | 31 | Oslo |\n| Bob | 42 | Rome |\n';
  try {
    await page.setViewport({ width: 1200, height: 900 });
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(500);
    const cell = text => page.evaluate(text => {
      const td = [...document.querySelectorAll('#ag-editor-id th, #ag-editor-id td')].find(c => c.textContent.trim() === text);
      const r = td.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    }, text);
    // A pointer event as the browser sends it, with the buttons held at that moment.
    const fire = (type, at, buttons) => page.evaluate((type, at, buttons) => {
      const target = document.elementFromPoint(at.x, at.y);
      target.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, clientX: at.x, clientY: at.y, buttons, button: 0 }));
    }, type, at, buttons);
    const state = () => page.evaluate(() => {
      const cs = window.__typedownMuya.contentState;
      const sel = window.getSelection();
      const node = sel.rangeCount ? sel.getRangeAt(0).startContainer : null;
      const td = node && (node.nodeType === 1 ? node : node.parentElement).closest('td, th');
      return {
        selected: document.querySelectorAll('#ag-editor-id .ag-cell-selected').length,
        range: cs.selectedTableCells ? cs.selectedTableCells.cells.length : 0,
        caretIn: td ? td.textContent.trim() : null,
        listening: cs.cellSelectEventIds.length
      };
    });

    // A click in "31" whose release is lost: the press only.
    const a = await cell('31');
    await page.mouse.click(a.x, a.y);
    await pause(200);
    await fire('mousedown', a, 1);
    await pause(100);
    // Later, no button: the pointer over Oslo, Bob, Rome.
    for (const t of ['Oslo', 'Bob', 'Rome']) { await fire('mousemove', await cell(t), 0); await pause(60); }
    await pause(300);
    const s = await state();
    report(s.selected === 0 && s.range === 0, 'the pointer passing over other cells with no button selects nothing', JSON.stringify(s));
    report(s.caretIn === '31', 'the caret stays in the cell it was in', JSON.stringify(s));
    report(s.listening === 0, 'and the press stops listening', JSON.stringify(s));
    await page.keyboard.type('5');
    await pause(300);
    const md = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(/\|\s*315\s*\|/.test(md) || /\|\s*531\s*\|/.test(md), 'typing goes into the cell', md);

    // A real drag still selects a range.
    const b = await cell('Ann'), c = await cell('Rome');
    await page.mouse.move(b.x, b.y);
    await page.mouse.down();
    await page.mouse.move(c.x, c.y, { steps: 6 });
    await page.mouse.up();
    await pause(300);
    const d = await state();
    report(d.range === 6, 'a drag across cells still selects the range (2 x 3)', JSON.stringify(d));

    // A drag whose release was lost: taken as released at the next move with no button.
    await page.mouse.click(5, 5);
    await pause(200);
    const e = await cell('Ann'), f = await cell('42');
    await fire('mousedown', e, 1);
    await fire('mousemove', f, 1);
    await pause(100);
    await fire('mousemove', await cell('Rome'), 0);
    await pause(300);
    const g = await state();
    report(g.range === 4 && g.listening === 0, 'a drag with a lost release keeps its range (2 x 2) and stops listening', JSON.stringify(g));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
