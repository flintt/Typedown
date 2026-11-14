// Table column widths (Settings > Editor > Table column widths, services/tableWidths): kept as the delimiter row's dashes.
// - off (the default): a table with unlike dashes is drawn as before, and written back aligned (no widths);
// - on: the same table is drawn at its widths (20% / 80%), and written back with them;
// - a column's edge dragged in the header row: the widths change, the document says so, and read again they are the same;
// - an aligned table (dashes padded to the cells) has no widths even with the setting on;
// - Fit columns to content: the widths gone.
// The user, 2026-11-13: nothing showed a column could be widened; the edge was hard to catch; letting go changed other
// columns; reading mode let long text out of a table with widths:
// - the pointer over the table: its header's column edges show (faintly); 6 px past an edge, in the next cell: the
//   resize cursor and a line down the table at the edge;
// - a table narrower than the page, dragged: as wide as before, the columns not dragged as before, and letting go
//   changes nothing; dragged again (it has widths now), the others exactly as they were;
// - it reads back as wide as it was (its widths together less than 100);
// - reading mode: a long address, code or word stays in its cell.
//
//   node table-widths-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = '# T\n\n| Name | Note |\n| -------------------- | -------------------------------------------------------------------------------- |\n| Ann | A note |\n| Bob | Another note |\n';
  const setting = on => page.evaluate(on => window.__deliver('SettingsChanged', { tableColumnWidths: on }), on);
  const open = async text => {
    await mode(true, false);
    await load(text);
    await mode(false, false);
    await pause(600);
  };
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  const shares = () => page.evaluate(() => {
    const ths = [...document.querySelectorAll('#ag-editor-id table th')];
    const total = ths.reduce((a, th) => a + th.getBoundingClientRect().width, 0);
    return ths.map(th => Math.round(th.getBoundingClientRect().width / total * 100));
  });
  const delimiter = md => (md.split('\n').find(l => /^\|\s*:?-{3,}/.test(l)) || '');
  const dashes = md => delimiter(md).split('|').slice(1, -1).map(s => (s.match(/-/g) || []).length);
  // Written without widths: the dashes all alike (compact) or padded to the cells (aligned) - not shares adding up to 100.
  const noWidths = md => { const d = dashes(md); return new Set(d).size === 1 || d.reduce((a, b) => a + b, 0) < 90 }
  try {
    await page.setViewport({ width: 1200, height: 900 });

    // Off: as before.
    await setting(false);
    await open(doc);
    let s = await shares();
    let md = await markdown();
    report(Math.abs(s[0] - 20) > 5, 'off: the table is not drawn at its widths', JSON.stringify(s));
    report(noWidths(md), 'off: written back as before (no widths)', delimiter(md));

    // On: drawn and written with them.
    await setting(true);
    await open(doc);
    s = await shares();
    md = await markdown();
    report(Math.abs(s[0] - 20) <= 2 && Math.abs(s[1] - 80) <= 2, 'on: drawn at 20% / 80%', JSON.stringify(s));
    report(JSON.stringify(dashes(md)) === JSON.stringify([20, 80]), 'on: written back with the widths', delimiter(md));

    // Dragged: the first column's edge 120 px to the right.
    const edge = await page.evaluate(() => {
      const th = document.querySelector('#ag-editor-id table th');
      const r = th.getBoundingClientRect();
      return { x: r.right - 1, y: r.top + r.height / 2 };
    });
    await page.mouse.move(edge.x - 20, edge.y);
    await page.mouse.move(edge.x, edge.y, { steps: 4 });
    await pause(100);
    const cursor = await page.evaluate(() => document.getElementById('ag-editor-id').style.cursor);
    report(cursor === 'col-resize', 'the column\'s edge shows the resize cursor', cursor);
    await page.mouse.down();
    await page.mouse.move(edge.x + 120, edge.y, { steps: 8 });
    await page.mouse.up();
    await pause(500);
    s = await shares();
    md = await markdown();
    const written = dashes(md);
    report(s[0] > 25 && written[0] > 25 && written[0] + written[1] >= 99, 'dragged: the first column wider, drawn and written', `${JSON.stringify(s)} ${delimiter(md)}`);
    await open(md);
    const again = await shares();
    report(Math.abs(again[0] - s[0]) <= 2, 'read again: the same widths', `${JSON.stringify(s)} -> ${JSON.stringify(again)}`);

    // Aligned (padded to the cells): no widths, setting on.
    await open('| Name  | A longer note |\n| ----- | ------------- |\n| Ann   | x             |\n');
    md = await markdown();
    report(noWidths(md), 'an aligned table has no widths (none read, none written)', delimiter(md));

    // A table narrower than the page: its edges shown, caught a little past them, dragged.
    const narrow = '# N\n\n| Name | Note | Other | Last |\n| --- | --- | --- | --- |\n| Ann | a note here | something | z |\n| Bob | another | more text here | y |\n';
    await open(narrow);
    const ths = () => page.evaluate(() => [...document.querySelectorAll('#ag-editor-id table th')].map(th => Math.round(th.getBoundingClientRect().width * 10) / 10));
    const tableW = () => page.evaluate(() => Math.round(document.querySelector('#ag-editor-id table').getBoundingClientRect().width));
    const edgeAt = i => page.evaluate(i => { const r = document.querySelectorAll('#ag-editor-id table th')[i].getBoundingClientRect(); return { x: r.right, y: r.top + r.height / 2 }; }, i);
    let at = await edgeAt(1);
    await page.mouse.move(at.x - 40, at.y + 40);
    await pause(250);
    const grip = await page.evaluate(() => Number(getComputedStyle(document.querySelector('#ag-editor-id table th'), '::after').opacity));
    report(grip > 0.2, 'the pointer over the table: its column edges show', String(grip));
    await page.mouse.move(at.x + 6, at.y, { steps: 4 });
    await pause(100);
    const caught = await page.evaluate(() => { const g = document.querySelector('.td-column-edge'); const t = document.querySelector('#ag-editor-id table').getBoundingClientRect(); const r = g && g.getBoundingClientRect(); return { cursor: document.getElementById('ag-editor-id').style.cursor, line: !!r && getComputedStyle(g).display !== 'none' && Math.abs(r.height - t.height) < 2 } });
    report(caught.cursor === 'col-resize' && caught.line, '6 px past an edge: the resize cursor and a line down the table', JSON.stringify(caught));
    const before = await ths(), wideBefore = await tableW();
    await page.mouse.down();
    await page.mouse.move(at.x + 106, at.y, { steps: 6 });
    await pause(100);
    const during = await ths();
    await page.mouse.up();
    await pause(500);
    const after = await ths();
    report(Math.abs(await tableW() - wideBefore) <= 8, 'dragged: the table as wide as before (not the page\'s width)', `${wideBefore} -> ${await tableW()}`);
    report([0, 3].every(i => Math.abs(during[i] - before[i]) <= 4) && during[1] > before[1] + 60, 'dragged: that edge\'s columns only', `${JSON.stringify(before)} -> ${JSON.stringify(during)}`);
    report(JSON.stringify(after) === JSON.stringify(during), 'letting go changes nothing', `${JSON.stringify(during)} -> ${JSON.stringify(after)}`);
    at = await edgeAt(2);
    await page.mouse.move(at.x - 3, at.y, { steps: 3 });
    await page.mouse.down();
    await page.mouse.move(at.x - 60, at.y, { steps: 6 });
    await page.mouse.up();
    await pause(500);
    const second = await ths();
    report(second[0] === after[0] && second[1] === after[1] && second[2] < after[2] && second[2] + second[3] === after[2] + after[3], 'dragged again: the others exactly as they were', `${JSON.stringify(after)} -> ${JSON.stringify(second)}`);
    md = await markdown();
    const together = dashes(md).reduce((a, b) => a + b, 0);
    await open(md);
    report(together < 100 && Math.abs(await tableW() - wideBefore) <= 8, 'read back: as wide as it was (its widths together less than 100)', `${together}: ${delimiter(md)}`);

    // Reading mode: a table with widths keeps long text in its cells.
    const long = '| Name | Note |\n| -------------------- | ------------------------------ |\n| Ann | https://example.com/a/very/long/path/without/any/spaces/index.html |\n| Bob | `inline_code_that_is_very_long_and_has_no_spaces_at_all` |\n| Cy | ' + 'x'.repeat(120) + ' |\n';
    await mode(true, false);
    await load(long);
    await mode(false, true);
    await pause(600);
    const out = await page.evaluate(() => Math.max(...[...document.querySelectorAll('#ag-editor-id td')].map(c => {
      const right = c.getBoundingClientRect().right;
      const range = document.createRange(); range.selectNodeContents(c);
      return Math.max(0, ...[...range.getClientRects()].map(r => r.right - right));
    })));
    report(out < 2, 'reading mode: long text stays in its cell', `${Math.round(out)} px out`);
    await mode(false, false);

    // Fit columns to content.
    await open(doc);
    await page.evaluate(() => {
      const cs = window.__typedownMuya.contentState;
      cs.contextMenuObject = { figure: document.querySelector('#ag-editor-id figure').id };
      window.__deliver('ContextMenuAction', { action: 'fitColumns' });
    });
    await pause(400);
    md = await markdown();
    report(noWidths(md), 'fit columns to content: the widths gone', delimiter(md));
    await setting(false);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
