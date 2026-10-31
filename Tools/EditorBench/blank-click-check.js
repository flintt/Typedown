// Clicks are not changes: a document with a run of blank lines (kept as they were written, utils/blankLines.js), the
// caret put in one of the empty paragraphs that stand for them and in the blocks around. The empty paragraph with the
// caret was not counted as a blank line, so the run was written twice over (15 lines became 29) and back as the caret
// left - a change reported at every click, and with automatic saving a saved version every few seconds.
const { openEditor } = require('./harness');

const doc = '# Title\n\nFirst paragraph here.\n' + '\n'.repeat(15) + 'Afterwards text\n\n> A quote.\n\nLast line.\n';

(async () => {
  const { page, mode, load, flush, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  try {
    await mode(false, false);
    await load(doc);
    await pause(800);
    await page.evaluate(() => {
      window.__changes = 0;
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => { if (JSON.parse(raw).name === 'MarkdownChange') window.__changes++; post(raw); };
    });
    const targets = ['#ag-editor-id > p:nth-of-type(2)', '#ag-editor-id > p:nth-of-type(4)', '#ag-editor-id > h1', '#ag-editor-id > blockquote', '#ag-editor-id > p:nth-of-type(3)', '#ag-editor-id > p:nth-of-type(1)'];
    for (const selector of targets) {
      const element = await page.$(selector);
      if (!element) { check(false, `there is ${selector}`); continue; }
      await element.click();
      await pause(400);
      const md = await flush();
      const runs = (md.match(/\n{3,}/g) || []).map(s => s.length - 1);
      check(md === doc, `caret in ${selector}: the document as written (blank runs ${JSON.stringify(runs)})`);
    }
    const changes = await page.evaluate(() => window.__changes);
    check(changes === 0, `no change reported for the clicks (${changes})`);
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
