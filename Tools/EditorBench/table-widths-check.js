// Table column widths (Settings > Editor > Table column widths, services/tableWidths): kept as the delimiter row's dashes.
// - off (the default): a table with unlike dashes is drawn as before, and written back aligned (no widths);
// - on: the same table is drawn at its widths (20% / 80%), and written back with them;
// - a column's edge dragged in the header row: the widths change, the document says so, and read again they are the same;
// - an aligned table (dashes padded to the cells) has no widths even with the setting on;
// - Fit columns to content: the widths gone.
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
