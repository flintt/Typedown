// A document long by its length keeps the long-document class (#editor.ag-long-document: what is off screen is not
// laid out) while it is still drawn a part at a time, after its first report too. It was given by the length when the
// document came in and taken away on the first report, where only the blocks counted: a 1.5 MB document of 3000
// blocks was laid out whole after every part drawn, half a second each and growing, and closing its tab waited behind
// them. Once drawn whole, the blocks decide again: kept by its length, a document switched back to was laid out from
// the stand-ins' heights and moved as the real ones came in (E2E JU01). A short document does not get the class.
//
//   node long-document-class-check.js
const { openEditor } = require('./harness');

// 1000 blocks of about 400 characters: long by its length (over 300,000), not by its blocks (under 6,000).
const para = i => `Paragraph ${i}: ` + 'lorem ipsum dolor sit amet, consectetur adipiscing elit, '.repeat(7) + '\n\n';
const long = Array.from({ length: 1000 }, (_, i) => para(i + 1)).join('');
const short = '# Short\n\nA few words.\n';

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const marked = () => page.evaluate(() => document.getElementById('editor').classList.contains('ag-long-document'));
  const drawing = () => page.evaluate(() => !!window.__typedownMuya.contentState.stateRender.progressive);
  try {
    await mode(false, false);
    // Slow enough parts that the document is still being drawn a while after it is reported loaded.
    await page.evaluate(() => { window.__typedownProgressiveDelayMs = 150; window.__typedownProgressiveSliceMs = 1; });
    console.log(`long document: ${long.length} characters`);
    await load(long);
    // load() returns on FileLoaded, which the page sends with its first report.
    await new Promise(r => setTimeout(r, 1500));
    check(await drawing(), 'it is still being drawn after its first report (the case this checks)');
    check(await marked(), 'and still marked, while it is drawn');
    await page.evaluate(() => { window.__typedownProgressiveDelayMs = 0; window.__typedownProgressiveSliceMs = 50; });
    await page.waitForFunction(() => !window.__typedownMuya.contentState.stateRender.progressive, { timeout: 60000 });
    await new Promise(r => setTimeout(r, 300));
    check(!(await marked()), 'drawn whole, it is decided by its blocks again (under 6,000: not marked)');
    await load(short);
    check(!(await marked()), 'a short document is not marked');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
