// A table's and a long code block's buttons (the user, 2026-11-13): the fold button beside the copy button, for a table
// both above its top right corner; a table's copy button shown while the caret is in the table, as its fold button is;
// a folded wide table keeps its right border (it was cut off), its buttons and its tool bar.
// - pointer over a table: fold and copy side by side, the copy button at the table's right edge;
// - pointer over the code block: the same, inside its top right corner;
// - the caret in the table: the copy button shown, beside the fold button;
// - the caret in a one-column table: the buttons right of its tool bar, not over it;
// - the wide table folded: its right border, its buttons and, with the caret in it, its tool bar inside what is drawn.
//
//   node block-buttons-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const rows = (n, row) => Array.from({ length: n }, (_, i) => row(i)).join('\n');
  const code = rows(30, i => `line ${i}`);
  const doc = '# T\n\nIntro\n\n' +
    `| N | A | B |\n| --- | --- | --- |\n${rows(20, i => `| r${i} | a | b |`)}\n\nOne\n\n` +
    `| N |\n| --- |\n${rows(20, i => `| r${i} |`)}\n\nWide\n\n` +
    `| N | Wide A | Wide B | C |\n| --- | --- | --- | --- |\n${rows(20, i => `| w${i} | ${'long cell text '.repeat(6)} | ${'more words here '.repeat(6)} | x |`)}\n\nAfter\n\n` +
    '```js\n' + code + '\n```\n\nEnd\n';
  // The buttons and boxes of the n-th table (or the code block, n = 'pre'), on screen.
  const geometry = n => page.evaluate(n => {
    const block = n === 'pre' ? document.querySelector('#ag-editor-id pre.ag-fence-code') : document.querySelectorAll('#ag-editor-id figure[data-role="TABLE"]')[n];
    // Its top (where the buttons are) 80 px below the top of the view.
    window.scrollBy(0, block.getBoundingClientRect().top - 80);
    const box = x => {
      if (!x) return null;
      const r = x.getBoundingClientRect(), s = getComputedStyle(x);
      return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, shown: s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity) >= 0.4 };
    };
    const tools = block.querySelector(':scope > .ag-tool-bar');
    return {
      block: box(block),
      table: box(block.querySelector(':scope > table')),
      fold: box(block.querySelector(':scope > .ag-block-fold')),
      copy: box(block.querySelector(':scope > .ag-diagram-copy, :scope > .ag-code-copy')),
      tools: tools && getComputedStyle(tools).display !== 'none' ? box(tools) : null,
      toolsEnd: tools && tools.querySelector('li:last-child') ? tools.querySelector('li:last-child').getBoundingClientRect().right : null,
      folded: block.classList.contains('ag-folded'),
    };
  }, n);
  const near = (a, b, d = 3) => Math.abs(a - b) <= d;
  const beside = g => g.fold && g.copy && near(g.fold.top, g.copy.top) && g.copy.left - g.fold.right >= 0 && g.copy.left - g.fold.right <= 8;
  const hover = async n => {
    const g = await geometry(n);
    const r = g.table || g.block;
    await page.mouse.move(r.left + 20, r.top + 20);
    await pause(350);
    return geometry(n);
  };
  const clickCell = async (n, text) => {
    const at = await page.evaluate((n, text) => {
      const f = document.querySelectorAll('#ag-editor-id figure[data-role="TABLE"]')[n];
      const cell = [...f.querySelectorAll('td')].find(td => td.textContent.trim() === text);
      cell.scrollIntoView({ block: 'center' });
      const r = cell.getBoundingClientRect();
      return { x: r.left + 8, y: r.top + r.height / 2 };
    }, n, text);
    await page.mouse.click(at.x, at.y);
    await pause(400);
  };
  const show = g => JSON.stringify({ fold: g.fold && [Math.round(g.fold.left), Math.round(g.fold.top)], copy: g.copy && [Math.round(g.copy.left), Math.round(g.copy.right), Math.round(g.copy.top), g.copy.shown], table: g.table && [Math.round(g.table.left), Math.round(g.table.right), Math.round(g.table.top)], block: [Math.round(g.block.left), Math.round(g.block.right), Math.round(g.block.top)], toolsEnd: g.toolsEnd && Math.round(g.toolsEnd) });
  try {
    await page.setViewport({ width: 1000, height: 900 });
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(800);

    let g = await hover(0);
    report(beside(g) && near(g.copy.right, g.table.right) && g.copy.bottom <= g.table.top + 1, 'pointer over a table: fold beside copy, above its top right corner', show(g));
    g = await hover('pre');
    report(beside(g) && g.copy.top >= g.block.top, 'pointer over the code block: fold beside copy, in its corner', show(g));

    await clickCell(0, 'r3');
    g = await geometry(0);
    report(g.copy && g.copy.shown && beside(g) && near(g.copy.right, g.table.right), 'the caret in the table: its copy button shown, beside the fold button', show(g));

    await clickCell(1, 'r3');
    g = await geometry(1);
    report(g.copy && g.copy.shown && beside(g) && g.toolsEnd != null && g.fold.left >= g.toolsEnd, 'the caret in a one-column table: the buttons right of its tool bar', show(g));

    // The wide table folded by its button (a real click).
    g = await hover(2);
    await page.mouse.move(g.fold.left + 10, g.fold.top + 10);
    await pause(150);
    await page.mouse.click(g.fold.left + 10, g.fold.top + 10);
    await pause(500);
    g = await hover(2);
    report(g.folded, 'the wide table folded by its button', show(g));
    report(g.block.right >= g.table.right + 1 && g.block.left <= g.table.left - 1, 'folded: its side borders inside what is drawn', show(g));
    report(beside(g) && g.fold.top >= g.block.top && g.copy.top >= g.block.top, 'folded: its buttons inside what is drawn, side by side', show(g));
    await clickCell(2, 'w1');
    g = await geometry(2);
    report(g.folded && g.tools && g.tools.top >= g.block.top && g.copy.shown && beside(g), 'folded, the caret in it: its tool bar and buttons inside what is drawn', show(g));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
