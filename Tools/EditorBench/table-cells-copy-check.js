// Cells selected in a table by a drag, copied and cut by the host's Copy and Cut (Ctrl+C, Ctrl+X and the menu: the host
// takes the shortcut, the page gets a "Copy" or "Cut" message). They were only copied on the browser's own copy event,
// which never came (a reader's report: the range was highlighted, Ctrl+C copied nothing).
// - a 2 x 2 range: the host's Copy sends one CopyBlock with the range as a Markdown table and as an HTML table (for Word
//   and Excel), only those four cells; Copy as Markdown sends the Markdown alone;
// - the host's Cut copies the range and empties the cells; the document says so.
//
//   node table-cells-copy-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = '| Name | Age | City |\n| --- | --- | --- |\n| Ann | 31 | Oslo |\n| Bob | 42 | Rome |\n| Cid | 27 | Lima |\n';
  try {
    await page.setViewport({ width: 1200, height: 900 });
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(500);
    await page.evaluate(() => {
      window.__copies = [];
      window.__clip = [];
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => {
        const m = JSON.parse(raw);
        if (m.type === 'invoke' && m.name === 'CopyBlock') { window.__copies.push(m.args); setTimeout(() => window.__deliver(m.id, { code: 0, data: true }), 0); return; }
        if (m.type === 'invoke' && /Clipboard/.test(m.name)) { window.__clip.push(m.args); setTimeout(() => window.__deliver(m.id, { code: 0, data: true }), 0); return; }
        post(raw);
      };
    });
    // A cell's centre by its text.
    const cell = text => page.evaluate(text => {
      const td = [...document.querySelectorAll('#ag-editor-id th, #ag-editor-id td')].find(c => c.textContent.trim() === text);
      const r = td.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    }, text);
    const select = async (from, to) => {
      const a = await cell(from), b = await cell(to);
      await page.mouse.move(a.x, a.y);
      await page.mouse.down();
      await page.mouse.move((a.x + b.x) / 2, (a.y + b.y) / 2, { steps: 4 });
      await page.mouse.move(b.x, b.y, { steps: 4 });
      await page.mouse.up();
      await pause(300);
      return page.evaluate(() => { const s = window.__typedownMuya.contentState.selectedTableCells; return s ? { row: s.row, column: s.column } : null; });
    };

    // A 2 x 2 range: Ann 31 / Bob 42.
    const range = await select('Ann', '42');
    report(range && range.row === 2 && range.column === 2, 'a drag across cells selects a 2 x 2 range', JSON.stringify(range));
    await page.evaluate(() => window.__deliver('Copy', { type: 'normal' }));
    await pause(400);
    const copies = await page.evaluate(() => window.__copies.splice(0));
    const c = copies[0] || {};
    report(copies.length === 1 && /\|\s*Ann\s*\|\s*31\s*\|/.test(c.text || '') && /\|\s*Bob\s*\|\s*42\s*\|/.test(c.text || '') && !/Oslo|Cid|Name/.test(c.text || ''),
      "the host's Copy: the range as a Markdown table, those cells only", JSON.stringify(c.text));
    report(/<table[\s\S]*Ann[\s\S]*31[\s\S]*Bob[\s\S]*42[\s\S]*<\/table>/.test(c.html || '') && !/Oslo/.test(c.html || ''), 'and as an HTML table for Word and Excel', (c.html || 'none').slice(0, 160));
    const stillSelected = await page.evaluate(() => !!window.__typedownMuya.contentState.selectedTableCells);
    report(stillSelected, 'the cells stay selected after the copy', 'cleared');

    // Copy as Markdown: the Markdown alone.
    await page.evaluate(() => window.__deliver('Copy', { type: 'copyAsMarkdown' }));
    await pause(400);
    const md = (await page.evaluate(() => window.__copies.splice(0)))[0] || {};
    report(/Ann/.test(md.text || '') && !md.html, 'Copy as Markdown: the Markdown table, no HTML', JSON.stringify(md));

    // The host's Cut: copied, then the cells emptied.
    await page.mouse.click(5, 5);
    await pause(200);
    await select('Bob', 'Rome');
    await page.evaluate(() => window.__deliver('Cut', { type: 'normal' }));
    await pause(500);
    const cut = (await page.evaluate(() => window.__copies.splice(0)))[0] || {};
    const after = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(/Bob[\s\S]*Rome/.test(cut.text || ''), "the host's Cut copies the range", JSON.stringify(cut.text));
    report(!/Bob|42|Rome/.test(after) && /Ann/.test(after) && /Cid/.test(after), 'and empties those cells, the rest kept', after);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
