// A link inside a raw HTML block (<p STYLE="...">...<a href="...">...</a>...</p>, as pasted content is) must never
// navigate the editor's own page: a plain click there opened the link in place, replacing the document with the
// external site, because the early return for a raw-HTML or container-preview block (clickEvent.js) stopped the
// click's propagation to Muya's own handler without stopping its default action - the click handler for <a> itself
// never ran. A reader's document pasted from a web page is full of such links.
//
//   node html-link-check.js
const { openEditor } = require('./harness');

const filler = Array.from({ length: 60 }, (_, i) => `Paragraph ${i} to push the heading well below the window.\n`).join('\n')
const doc = '# Pasted page\n\n<p STYLE="margin:0pt 0pt 6pt;">Some text and a <a href="https://example.com/baidu">link</a> in raw HTML, as pasted content is.</p>\n\n<p>Another <a href="#heading-two">anchor link</a> and <a href="https://example.com/ctrl-still-works">one more</a>.</p>\n\n' + filler + '\n## Heading Two\n\nAfter the raw HTML.\n';

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  try {
    // Every OpenNewWindow invoke from here on is recorded, on top of the harness's own stub (which still answers it).
    await page.evaluate(() => {
      window.__openNewWindowCalls = [];
      const orig = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => {
        const m = JSON.parse(raw);
        if (m.type === 'invoke' && m.name === 'OpenNewWindow') window.__openNewWindowCalls.push(m.args);
        return orig(raw);
      };
    });

    await mode(false, false);
    await load(doc);
    await pause(300);
    const before = page.url();

    // A plain click (no ctrl) on the link inside the raw HTML block.
    const box = await page.evaluate(() => {
      const html = document.querySelector('#ag-editor-id .ag-html-preview');
      const link = html && html.querySelector('a[href^="https://example.com/baidu"]');
      if (!link) return null;
      const r = link.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    });
    check(!!box, 'the raw HTML block and its link are on the page');
    if (box) {
      await page.mouse.click(box.x, box.y);
      await pause(400);
    }
    const after = page.url();
    check(after === before, `the page itself did not navigate away (before ${before}, after ${after})`);
    const stillThere = await page.evaluate(() => !!document.querySelector('#ag-editor-id'));
    check(stillThere, 'the editor is still on the page (not replaced by the external site)');
    const invoked = await page.evaluate(() => window.__openNewWindowCalls || []);
    console.log('  OpenNewWindow calls seen: ' + JSON.stringify(invoked));
    check(invoked.some(h => h === 'https://example.com/baidu'), `a plain click on the link in raw HTML opens it through the host (${JSON.stringify(invoked)})`);

    // A link to an in-page anchor (#heading-two) inside raw HTML: followed without going through the host.
    await page.evaluate(() => { window.__openNewWindowCalls = [] });
    const box2 = await page.evaluate(() => {
      const link = [...document.querySelectorAll('#ag-editor-id .ag-html-preview a')].find(a => a.getAttribute('href') === '#heading-two');
      const r = link.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    });
    const beforeScroll = await page.evaluate(() => window.scrollY);
    await page.mouse.click(box2.x, box2.y);
    await pause(400);
    const afterScroll = await page.evaluate(() => window.scrollY);
    check(page.url() === before, 'an in-page anchor link in raw HTML does not navigate the page either');
    check(afterScroll !== beforeScroll, `an in-page anchor link in raw HTML scrolls to it (${beforeScroll} -> ${afterScroll})`);

    // Ctrl+click still works as before (not broken by the fix). Back at the top first: the anchor click above
    // scrolled the link out of the window.
    await page.evaluate(() => window.scrollTo(0, 0));
    await pause(200);
    await page.evaluate(() => { window.__openNewWindowCalls = [] });
    const box3 = await page.evaluate(() => {
      const link = [...document.querySelectorAll('#ag-editor-id .ag-html-preview a')].find(a => a.href.includes('ctrl-still-works'));
      const r = link.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    });
    await page.keyboard.down('Control');
    await page.mouse.click(box3.x, box3.y);
    await page.keyboard.up('Control');
    await pause(400);
    const invoked2 = await page.evaluate(() => window.__openNewWindowCalls || []);
    check(invoked2.some(h => h.includes('ctrl-still-works')), `Ctrl+click on a link in raw HTML still opens it (${JSON.stringify(invoked2)})`);

    // A plain click on ordinary (non-HTML) Markdown text: unchanged - still just edits (places the caret), not
    // opened without Ctrl.
    await page.evaluate(() => { window.__openNewWindowCalls = [] });
    await load('# P\n\nA normal [markdown link](https://example.com/normal) in editable text.\n');
    await pause(300);
    const box4 = await page.evaluate(() => {
      const link = document.querySelector('#ag-editor-id a[href="https://example.com/normal"]');
      const r = link.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    });
    await page.mouse.click(box4.x, box4.y);
    await pause(300);
    const invoked3 = await page.evaluate(() => window.__openNewWindowCalls || []);
    check(invoked3.length === 0, `a plain click on an ordinary editable link still just edits, does not open it (${JSON.stringify(invoked3)})`);
    check(page.url() === before, 'and the page did not navigate for it either');
  } finally {
    await close();
  }
  console.log(ok ? 'OK: a link inside raw HTML opens through the host, never by navigating the editor itself' : 'FAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2) });
