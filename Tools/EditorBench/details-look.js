// How <details> blocks look, opened, with what may sit at their left edge in them: lists (bullets, numbers, tasks), a
// quotation, code, a table, a nested block. A picture of the page, printed as base64 (SHOT:...) for the box that ran
// this to look at; and checks that nothing in a block reaches left of its line, and that the line is one piece.
//
//   node details-look.js
const { openEditor } = require('./harness');

const DOC = [
  '# Details',
  '',
  'A paragraph before.',
  '',
  '<details open>',
  '<summary>Lists and more</summary>',
  '',
  '- bullet one',
  '- bullet two',
  '',
  '1. first',
  '2. second',
  '',
  '- [ ] a task',
  '',
  '> a quotation',
  '',
  '```js',
  'const x = 1',
  '```',
  '',
  '| A | B |',
  '| --- | --- |',
  '| 1 | 2 |',
  '',
  '<details open>',
  '<summary>Nested</summary>',
  '',
  '- inside',
  '',
  '</details>',
  '',
  '</details>',
  '',
  '- a list outside, for comparison',
  '',
].join('\n');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await page.setViewport({ width: 1000, height: 900 });
    await mode(false, false);
    await load(DOC);
    await pause(800);
    const geometry = await page.evaluate(() => {
      const outer = document.querySelector('#ag-editor-id > figure[data-role="DETAILS"]');
      const line = getComputedStyle(outer, '::after');
      const box = outer.getBoundingClientRect();
      const lineX = box.left + parseFloat(line.left) + parseFloat(line.borderLeftWidth);
      // The leftmost thing drawn for each block in it: a list item's marker sits left of its text.
      const lefts = [...outer.querySelectorAll(':scope > :not(.ag-details-toggle):not(.ag-details-summary):not(.ag-front-icon)')].map(el => {
        let left = el.getBoundingClientRect().left;
        for (const li of el.querySelectorAll('li')) {
          const marker = getComputedStyle(li, '::marker');
          const r = li.getBoundingClientRect();
          // The marker's box is not exposed: taken as the list's own padding to the left of the item.
          left = Math.min(left, r.left - (parseFloat(getComputedStyle(li.parentElement).paddingLeft) || 0) + 2);
        }
        for (const box of el.querySelectorAll('input[type=checkbox], .ag-task-list-item > input')) left = Math.min(left, box.getBoundingClientRect().left);
        return { tag: el.tagName.toLowerCase(), left: Math.round(left) };
      });
      return { lineX: Math.round(lineX), lineContent: line.content, lineTop: line.top, lineBottom: line.bottom, lefts };
    });
    console.log('  geometry:', JSON.stringify(geometry));
    check(geometry.lineContent !== 'none', 'an open block draws its line');
    check(geometry.lefts.every(l => l.left > geometry.lineX), 'nothing in it, a list\'s markers included, reaches left of its line');
    const shot = await page.screenshot({ clip: { x: 0, y: 0, width: 1000, height: 900 } });
    // In pieces: a line that long did not come back whole from the runner.
    const b64 = Buffer.from(shot).toString('base64');
    for (let i = 0; i < b64.length; i += 1000) console.log('SHOT ' + String(i / 1000).padStart(4, '0') + ' ' + b64.slice(i, i + 1000));
    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `\n${failures.length} FAILED` : '\nall passed');
  process.exit(failures.length ? 1 : 0);
})();
