// The document rendered as XHTML for another format (RenderXhtml) keeps the place of its [TOC] line: an empty
// div.table-of-contents where the table of contents goes, which a host fills with that format's own (Word's). It was
// rendered as nothing, and the exported document had no table of contents.
const { openEditor } = require('./harness');

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  try {
    await mode(true, false);
    await load('# Title\n\n[TOC]\n\n## One\n\ntext\n');
    await pause(300);
    await page.evaluate(() => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token: 1 }); });
    await page.waitForFunction(() => window.__last.RenderedXhtml, { timeout: 60000 });
    const { xhtml, error } = await page.evaluate(() => window.__last.RenderedXhtml);
    check(!error, `rendered${error ? ` (error ${error})` : ''}`);
    const at = (xhtml || '').indexOf('<div class="table-of-contents"></div>');
    check(at > (xhtml || '').indexOf('Title') && at < (xhtml || '').indexOf('One'), 'the [TOC] line is an empty div.table-of-contents between the title and the first section');
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
