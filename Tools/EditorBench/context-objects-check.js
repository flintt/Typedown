// The context menu's items for the object right-clicked (the 2026-11-11 menu report): the page tells the host, as the
// right button goes down, whether it is on a table, a link (its address), a code block, a display formula or a diagram
// (ContextMenuTarget), and does the item's action on that element (ContextMenuAction): copy the code, copy the formula
// or diagram, remove a link keeping its text. Right-clicks with the real mouse.
//
//   node context-objects-check.js
const { openEditor } = require('./harness');

const doc = '# Objects\n\nA paragraph with a [link to the site](https://example.com/page) in it.\n\n```js\nconst answer = 42;\n```\n\n$$\nE = mc^2\n$$\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\nAfter.\n';

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${seen === undefined ? '' : ': ' + seen}`); if (!ok) failures.push(what); };
  // Every call the page makes of the host, kept: the clipboard's among them.
  await page.evaluate(() => {
    window.__calls = [];
    const post = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = raw => { try { const m = JSON.parse(raw); if (m.type === 'invoke') window.__calls.push({ name: m.name, args: m.args }); } catch { } return post(raw); };
  });
  const rightClick = async selector => {
    const box = await page.evaluate(s => { const e = document.querySelector(s); if (!e) return null; e.scrollIntoView({ block: 'center' }); const r = e.getBoundingClientRect(); return { x: r.left + Math.min(12, r.width / 2), y: r.top + r.height / 2 }; }, selector);
    if (!box) return null;
    await page.mouse.click(box.x, box.y, { button: 'right' });
    await pause(150);
    return page.evaluate(() => window.__typedownContextMenuTarget);
  };
  const act = async action => { await page.evaluate(a => { window.__calls.length = 0; window.__deliver('ContextMenuAction', { action: a }); }, action); await pause(600); return page.evaluate(() => window.__calls.slice()); };
  try {
    await mode(false, false);
    await load(doc);
    await pause(1500);

    const onText = await rightClick('#ag-editor-id h1');
    check(onText && !onText.table && onText.link === null && !onText.code && onText.block === null, 'on a heading: no object', JSON.stringify(onText));
    const onLink = await rightClick('#ag-editor-id a[href]');
    check(onLink && onLink.link === 'https://example.com/page', 'on a link: its address', JSON.stringify(onLink));
    const onCode = await rightClick('#ag-editor-id pre[data-role="fencecode"] code');
    check(onCode && onCode.code === true && onCode.block === null, 'in a code block: code', JSON.stringify(onCode));
    let calls = await act('copyCode');
    const copiedCode = calls.filter(c => c.name === 'SetClipboard').map(c => c.args?.data).join('|');
    check(copiedCode.includes('const answer = 42;'), "Copy Code: the block's code to the clipboard", copiedCode);
    const onMath = await rightClick('#ag-editor-id figure[data-role="MULTIPLEMATH"]');
    check(onMath && onMath.block === 'math' && !onMath.code, 'on a display formula: math', JSON.stringify(onMath));
    calls = await act('copyBlock');
    check(calls.some(c => c.name === 'CopyBlock' && JSON.stringify(c.args).includes('E = mc^2')), 'Copy Formula: the formula to the clipboard (CopyBlock, with its Markdown)', calls.map(c => c.name).join(','));
    const onTable = await rightClick('#ag-editor-id figure[data-role="TABLE"] tbody td');
    check(onTable && onTable.table === true, 'in a table cell: table', JSON.stringify(onTable));

    // Remove Link: the link's text stays, its address goes.
    await rightClick('#ag-editor-id a[href]');
    await act('removeLink');
    const text = await flush();
    check(text.includes('A paragraph with a link to the site in it.') && !text.includes('example.com'), 'Remove Link: the text stays, the address goes', JSON.stringify(text.split('\n')[2]));
    check(text.replace('A paragraph with a link to the site in it.', 'A paragraph with a [link to the site](https://example.com/page) in it.') === doc, 'and nothing else changed');
    if (errors.length) failures.push(`page errors: ${errors.slice(0, 3).join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
