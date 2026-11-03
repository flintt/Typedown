// Empty task items in reading mode (a reader, 2026-11-03: "the box shows twice"): reading mode leaves an empty paragraph
// without height, and an empty list item's own paragraph went with it - its box then sat beside the first box of the list
// nested under it, and the last one's over the next item's. Each item keeps its line: no two boxes on one line, in
// reading mode as while editing.
//
//   node task-reading-check.js
const { openEditor } = require('./harness');

const md = '- [ ] one\n\n  - [ ] two\n\n  - [ ] \n\n    - [ ] three\n\n  - [ ] 1\n\n  - [ ] \n\n- [ ] four\n';

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    for (const readOnly of [false, true]) {
      await mode(true, false);
      await load(md);
      await mode(false, readOnly);
      await pause(400);
      const boxes = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id li.ag-task-list-item > input')].map(i => {
        const r = i.getBoundingClientRect();
        return { top: Math.round(r.top), left: Math.round(r.left) };
      }));
      const tops = boxes.map(b => b.top);
      const shared = tops.filter((t, i) => tops.findIndex(u => Math.abs(u - t) < 6) !== i);
      const state = readOnly ? 'reading mode' : 'editing';
      check(boxes.length === 7 && shared.length === 0, `${state}: each of the 7 task boxes on a line of its own`, JSON.stringify(boxes));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
