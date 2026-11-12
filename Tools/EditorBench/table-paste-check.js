// What is pasted into selected table cells fills them, as a spreadsheet does (a reader: a range of cells copied could not
// be pasted into an empty range of the same size - several cells selected took nothing).
// - a 2 x 2 range copied (the host's Copy: a Markdown table and an HTML table) and pasted into an empty 2 x 2 range:
//   the same four values there;
// - one value pasted into a selected 2 x 2 range: in all four;
// - 1 x 2 tab-separated text (Excel's) into a 2 x 2 range, a whole multiple: repeated over it;
// - 2 x 2 pasted with one cell selected at the table's last row and column: spread from it, the table given the row
//   and the column it needs, nothing cut;
// - a single cell selected and one value pasted: that cell replaced, as before.
//
//   node table-paste-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = '| Name | Age | X | Y |\n| --- | --- | --- | --- |\n| Ann | 31 |  |  |\n| Bob | 42 |  |  |\n';
  try {
    await page.setViewport({ width: 1200, height: 900 });
    const fresh = async () => {
      await mode(true, false);
      await load(doc);
      await mode(false, false);
      await pause(500);
    };
    await fresh();
    await page.evaluate(() => {
      window.__copies = [];
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => {
        const m = JSON.parse(raw);
        if (m.type === 'invoke' && m.name === 'CopyBlock') { window.__copies.push(m.args); setTimeout(() => window.__deliver(m.id, { code: 0, data: true }), 0); return; }
        post(raw);
      };
    });
    // A cell's centre by its row and column in the table (0: the header row).
    const at = (row, column) => page.evaluate((row, column) => {
      const tr = document.querySelectorAll('#ag-editor-id table tr')[row];
      const td = tr.children[column];
      const r = td.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    }, row, column);
    const select = async (from, to) => {
      const a = await at(...from), b = await at(...to);
      await page.mouse.click(5, 5);
      await pause(150);
      await page.mouse.move(a.x, a.y);
      await page.mouse.down();
      await page.mouse.move((a.x + b.x) / 2, (a.y + b.y) / 2, { steps: 4 });
      await page.mouse.move(b.x, b.y, { steps: 4 });
      await page.mouse.up();
      await pause(300);
    };
    const paste = async (text, html) => { await page.evaluate((text, html) => window.__deliver('Paste', { type: 'normal', text, html }), text, html || ''); await pause(500); };
    const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
    const rows = md => md.split('\n').filter(l => /^\|/.test(l) && !/^\|\s*-/.test(l)).map(l => l.replace(/^\||\|$/g, '').split('|').map(c => c.trim()));

    // 1. 2 x 2 copied by the host's Copy, pasted into the empty 2 x 2.
    await select([1, 0], [2, 1]);
    await page.evaluate(() => window.__deliver('Copy', { type: 'normal' }));
    await pause(400);
    const copied = (await page.evaluate(() => window.__copies.splice(0)))[0] || {};
    report(/Ann/.test(copied.text || '') && /<table/.test(copied.html || ''), 'the range is copied (Markdown and HTML)', JSON.stringify(copied).slice(0, 200));
    await select([1, 2], [2, 3]);
    await paste(copied.text, copied.html);
    let r = rows(await markdown());
    report(JSON.stringify(r.slice(1)) === JSON.stringify([['Ann', '31', 'Ann', '31'], ['Bob', '42', 'Bob', '42']]), 'pasted into an empty range of its size: the same values there', JSON.stringify(r));
    let selectedAfter = await page.evaluate(() => !!window.__typedownMuya.contentState.selectedTableCells);
    report(!selectedAfter, 'and the cells are no longer selected', 'still selected');

    // 2. One value into a 2 x 2 range.
    await fresh();
    await select([1, 2], [2, 3]);
    await paste('Zed');
    r = rows(await markdown());
    report(JSON.stringify(r.slice(1).map(x => x.slice(2))) === JSON.stringify([['Zed', 'Zed'], ['Zed', 'Zed']]), 'one value into a 2 x 2 range: in all four', JSON.stringify(r));

    // 3. 1 x 2 (tab separated) repeated over a 2 x 2 range.
    await fresh();
    await select([1, 2], [2, 3]);
    await paste('p\tq');
    r = rows(await markdown());
    report(JSON.stringify(r.slice(1).map(x => x.slice(2))) === JSON.stringify([['p', 'q'], ['p', 'q']]), '1 x 2 into a 2 x 2 range: repeated over it', JSON.stringify(r));

    // 4. 2 x 2 at a caret in the last cell (one cell is a caret or its text, not a range): spread from it, the table
    //    growing by a row and a column.
    await fresh();
    const c = await at(2, 3);
    await page.mouse.click(5, 5);
    await pause(150);
    await page.mouse.click(c.x, c.y);
    await pause(250);
    await paste('a\tb\nc\td');
    r = rows(await markdown());
    report(r.length === 4 && r[0].length === 5 && JSON.stringify(r[2].slice(3)) === JSON.stringify(['a', 'b']) && JSON.stringify(r[3].slice(3)) === JSON.stringify(['c', 'd']),
      '2 x 2 from the last cell: spread, the table given a row and a column', JSON.stringify(r));

    // 6. Pasted as plain text, tabs and all, at a caret in a cell: not split - the whole text in that cell, tabs as
    //    spaces (a reader: text with tabs is sometimes meant for one cell); a normal paste of the same text splits.
    await fresh();
    const p = await at(1, 2);
    await page.mouse.click(5, 5);
    await pause(150);
    await page.mouse.click(p.x, p.y);
    await pause(250);
    await page.evaluate(() => window.__deliver('Paste', { type: 'pasteAsPlainText', text: 'a\tb', html: '' }));
    await pause(500);
    r = rows(await markdown());
    report(r.length === 3 && r[0].length === 4 && r[1][2] === 'a b' && r[1][3] === '', 'pasted as plain text: tabs and all in one cell, as spaces', JSON.stringify(r));

    // 7. A spreadsheet's 300 rows pasted at a caret in the last cell: the table given 299 rows at once, quickly (the
    //    user, 2026-11-12: slow - each row added was a table edit of its own, drawn and reported, the page read again).
    await fresh();
    const lastCell = await at(2, 3);
    await page.mouse.click(5, 5);
    await pause(150);
    await page.mouse.click(lastCell.x, lastCell.y);
    await pause(250);
    const tsv = Array.from({ length: 300 }, (_, i) => `r${i}\ta${i}\tb${i}\tc${i}`).join('\n');
    const started = Date.now();
    await page.evaluate(text => window.__deliver('Paste', { type: 'normal', text, html: '' }), tsv);
    let big = [];
    while (Date.now() - started < 20000) {
      big = rows(await markdown());
      if (big.length >= 302) break;
      await pause(50);
    }
    const took = Date.now() - started;
    console.log(`  300 rows pasted in ${took} ms`);
    report(big.length === 302 && big[0].length === 7 && JSON.stringify(big[301].slice(3)) === JSON.stringify(['r299', 'a299', 'b299', 'c299']) && big[2][3] === 'r0',
      '300 rows from the last cell: all of them there, the table grown to fit', `${big.length} rows, last ${JSON.stringify(big[big.length - 1])}`);
    report(took < 2000, '300 rows pasted in under 2 seconds', `${took} ms`);

    // 5. A single cell, one value: replaced as before.
    await fresh();
    const cell = await at(1, 0);
    await page.mouse.click(cell.x, cell.y);
    await page.evaluate(() => window.__deliver('SelectAll'));
    await pause(250);
    await paste('Eve');
    r = rows(await markdown());
    report(r[1][0] === 'Eve' && r[2][0] === 'Bob', 'one cell, one value: replaced', JSON.stringify(r));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
