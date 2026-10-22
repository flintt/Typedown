// A long document keeps the long-document class (#editor.ag-long-document: what is off screen is not laid out) after
// it is edited. It was given by the document's length when it came in and taken away again on its first report, where
// only the number of blocks counted: a 1.5 MB document of 3000 blocks was laid out whole after every part drawn, half a
// second each and growing, and closing its tab waited behind them. A short document does not get the class.
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
  try {
    await mode(false, false);
    console.log(`long document: ${long.length} characters`);
    await load(long);
    check(await marked(), 'the long document is marked when it comes in');
    await page.click('#ag-editor-id > p');
    await page.keyboard.type('x');
    await new Promise(r => setTimeout(r, 800));
    check(await marked(), 'and still after an edit (its first report)');
    await load(short);
    await page.click('#ag-editor-id > p:last-of-type');
    await page.keyboard.type('y');
    await new Promise(r => setTimeout(r, 300));
    check(!(await marked()), 'a short document is not marked');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
