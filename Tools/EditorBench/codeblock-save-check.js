// Code typed into a code block is in what a save writes, with the caret still in the block. A Store review (on 1.2.18):
// "after writing code in a code block, clicking Save without doing anything else does not save the code block; some
// other edit has to come first". A save asks the page for its text (FlushContent) and writes what comes back, so the
// text has to be there the moment the last key is typed - in a short document, and in a long one, where reporting
// the text is throttled while typing and only the flush brings it up to date.
//
//   node codeblock-save-check.js
const { openEditor } = require('./harness');

const filler = Array.from({ length: 1200 }, (_, i) => `Paragraph ${i} with enough words in it to make the document long.`).join('\n\n');

(async () => {
  const { page, mode, load, flush, pause, close, errors } = await openEditor();
  let ok = true;
  const check = (c, label, text) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${c ? '' : '\n     ' + JSON.stringify(text.slice(-200))}`); ok = ok && c; };
  const lastParagraph = async () => {
    await page.evaluate(() => { const ps = document.querySelectorAll('#ag-editor-id > p.ag-paragraph'); ps[ps.length - 1].scrollIntoView(); });
    await page.click('#ag-editor-id > p.ag-paragraph:last-of-type');
    await page.keyboard.press('End');
  };
  try {
    await mode(false, false);
    for (const [size, prefix] of [['short', '# T\n\n'], ['long (throttled)', `# T\n\n${filler}\n\n`]]) {
      await load(`${prefix}last\n`); await pause(800);
      await lastParagraph();
      await page.keyboard.press('Enter');
      await page.keyboard.type('```js');
      await page.keyboard.press('Enter');
      await pause(300);
      await page.keyboard.type('const answer = 42;');
      let t = await flush();   // straight away, the caret still in the block
      check(t.includes('```js\nconst answer = 42;\n```'), `${size} document: a new code block's code is in the text a save writes`, t);

      await load(`${prefix}\`\`\`js\nlet a = 1;\n\`\`\`\n`); await pause(800);
      await page.evaluate(() => { const c = document.querySelectorAll('#ag-editor-id pre code'); c[c.length - 1].scrollIntoView(); });
      const codes = await page.$$('#ag-editor-id pre code');
      await codes[codes.length - 1].click();
      await page.keyboard.press('End');
      await page.keyboard.press('Enter');
      await page.keyboard.type('let b = 2;');
      t = await flush();
      check(t.includes('let a = 1;\nlet b = 2;\n```'), `${size} document: a line added to an existing code block is in the text a save writes`, t);
    }
    if (errors.length) { console.log('page errors:', errors); ok = false; }
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
