// Switching between a long Markdown tab and a plain text tab (a reader's report, 2026-11-02: switching to and from a long
// .txt was slow - back to the Markdown file took 3-4 s, against half a second between two Markdown files). As the host
// does it: PlainTextShown with the plain text file's load, PlainTextShown off just before the Markdown file's load.
// Back, the Markdown file is shown from what was kept when the visual editor went - never built again, and the plain
// text never given to the visual editor (it parsed and drew it, the seconds lost). And Save as turning a .txt into a .md
// (PlainTextShown off, no load): the visual editor comes, with the text.
//
//   node plain-switch-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const big = Array.from({ length: 3000 }, (_, i) => `## Section ${i}\n\nParagraph ${i} with **bold** and a [link](https://example.com/${i}).\n\n- item ${i}\n- item ${i + 1}\n`).join('\n');
  const txt = Array.from({ length: 2000 }, (_, i) => `2026-11-02 12:00 line ${i} of a log, # not a heading, *not emphasis*`).join('\n');
  let id = 1000;
  const loadPlain = async text => {
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: true, plainTextShown: true, plainTextProse: true }));
    await page.evaluate(({ text, id }) => window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp' }), { text, id: ++id });
    await page.waitForFunction(n => { const cm = document.querySelector('.CodeMirror'); return cm && cm.CodeMirror.getValue().length === n; }, { timeout: 60000 }, text.length);
  };
  const loadMarkdown = async text => {
    const t0 = Date.now();
    await page.evaluate(() => { delete window.__last.MarkdownChange; delete window.__last.FileLoaded; });
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await page.evaluate(({ text, id }) => window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp' }), { text, id: ++id });
    await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
    return Date.now() - t0;
  };
  try {
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await load(big);
    await pause(2000);
    await loadPlain(txt);
    await pause(800);
    report(await page.evaluate(b => window.__typedownKeptStore.has(b), big), 'the long Markdown file is kept while the plain text file is shown', '');
    const back = await loadMarkdown(big);
    const reported = await page.evaluate(() => ({ change: window.__last.MarkdownChange ? (window.__last.MarkdownChange.text || '').slice(0, 40) : null, loaded: window.__last.FileLoaded }));
    report(back < 1500, `back to the long Markdown file in ${back} ms (was 3-4 s)`, `${back} ms`);
    report(!reported.change, 'the plain text is never reported by the visual editor on the way back', JSON.stringify(reported.change));
    report(/Section 0$/.test(await page.evaluate(() => document.querySelector('#ag-editor-id h2')?.textContent.trim() || '')), 'the Markdown file is what is shown', '');

    // Save as: a .txt becomes a .md - PlainTextShown off and no load. The visual editor comes with the text.
    await loadPlain('# Now Markdown\n\n**bold**\n');
    await pause(300);
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await pause(1000);
    const after = await page.evaluate(() => ({ muya: !!document.querySelector('#ag-editor-id'), heading: document.querySelector('#ag-editor-id h1')?.textContent.trim() || null }));
    report(after.muya && /Now Markdown$/.test(after.heading || ''), 'Save as .md: the visual editor comes with the text, drawn as Markdown', JSON.stringify(after));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
