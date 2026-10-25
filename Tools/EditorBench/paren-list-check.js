// An ordered list numbered 1) 2) shows its numbers as written, in the editor, in reading mode and in the HTML (and PDF)
// export - not as 1. 2., which HTML lists draw by themselves. A list numbered 1. 2. is as before; the text is kept.
//
//   node paren-list-check.js
const { openEditor } = require('./harness');

const doc = '1) one\n2) two\n\nBetween.\n\n3. three\n4. four\n';

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  // What each list's first item draws as its number: the ::marker's content, or the list's own style.
  const markers = () => page.evaluate(() => [...document.querySelectorAll('#ag-editor-id ol')].map(ol => {
    const li = ol.querySelector('li');
    const content = getComputedStyle(li, '::marker').content;
    return content === 'normal' ? getComputedStyle(ol).listStyleType : content;
  }));
  try {
    for (const readOnly of [false, true]) {
      await mode(false, readOnly);
      await load(doc);
      await new Promise(r => setTimeout(r, 500));
      const shown = await markers();
      const where = readOnly ? 'reading mode' : 'editing';
      console.log(`${where}: ${JSON.stringify(shown)}`);
      check(/\)/.test(shown[0] || ''), `${where}: the 1) list is numbered with )`);
      check(shown[1] === 'decimal', `${where}: the 3. list is numbered as before`);
    }
    await mode(false, false);
    await load(doc);
    check(await flush() === doc, 'the text handed back is the document');

    // The export: the attribute and the style that reads it, measured in a page of its own.
    const html = await page.evaluate(md => { const muya = window.__typedownMuya; muya.markdown = md; return muya.exportHtml(); }, doc);
    check(/<ol data-delimiter="\)">/.test(html) && !/<ol start="3" data-delimiter/.test(html), 'the export marks the 1) list, not the 3. one');
    const exported = await page.browser().newPage();
    const page_ = await page.evaluate(async md => { const muya = window.__typedownMuya; muya.markdown = md; return await muya.exportStyledHTML({ title: 'paren' }); }, doc);
    await exported.setContent(page_);
    const drawn = await exported.evaluate(() => [...document.querySelectorAll('.markdown-body ol')].map(ol => getComputedStyle(ol.querySelector('li'), '::marker').content));
    console.log(`export: ${JSON.stringify(drawn)}`);
    check(/\)/.test(drawn[0]) && drawn[1] === 'normal', 'the exported page numbers the 1) list with ) and the 3. list as before');
    await exported.close();
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
