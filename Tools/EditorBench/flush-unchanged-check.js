// What the host asks for before it closes or leaves a tab (FlushContent) comes back at once for a long document the
// reader has not changed: the page does not write the whole document out again. A 1.5 MB document took seconds, and
// closing its tab waited with the document still on screen. A keystroke just made is still in the text it hands back,
// the long document's throttled report included.
//
//   node flush-unchanged-check.js
const { openEditor } = require('./harness');

const block = i => `## Part ${i}\n\n<div style="margin:0 auto;width:600px"><p style="font-size:14px">Paragraph ${i} in <b>HTML</b> with a <a href="http://example.com/${i}">link</a>.</p></div>\n\nSome **Markdown** text ${i}, a list:\n\n- one ${i}\n- two ${i}\n\n`;
const doc = Array.from({ length: 2500 }, (_, i) => block(i + 1)).join('') + 'The end.\n';

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  // How many times the page wrote the document out while answering (getMarkdownAndCursor, counted).
  const flush = async () => {
    const t = Date.now();
    await page.evaluate(() => {
      const muya = window.__typedownMuya;
      if (!muya.__counted) {
        const original = muya.getMarkdownAndCursor.bind(muya);
        muya.getMarkdownAndCursor = (...args) => { window.__writes = (window.__writes || 0) + 1; return original(...args); };
        muya.__counted = true;
      }
      window.__writes = 0;
      delete window.__last.ContentFlushed;
      window.__deliver('FlushContent', { token: 9 });
    });
    await page.waitForFunction(() => window.__last.ContentFlushed, { timeout: 60000, polling: 20 });
    return { ms: Date.now() - t, ...(await page.evaluate(() => ({ text: window.__last.ContentFlushed.text, writes: window.__writes }))) };
  };
  const difference = (a, b) => { let i = 0; while (i < a.length && a[i] === b[i]) i++; return JSON.stringify([a.slice(Math.max(0, i - 20), i + 20), b.slice(Math.max(0, i - 20), i + 20)]); };
  try {
    await mode(false, false);
    console.log(`document: ${doc.length} characters`);
    await load(doc);
    await new Promise(r => setTimeout(r, 3000));
    const unchanged = await flush();
    console.log(`unchanged: ${unchanged.ms} ms, written out ${unchanged.writes} times`);
    check(unchanged.text === doc, 'unchanged, the text handed back is the document');
    check(unchanged.writes === 0, `unchanged, the document is not written out again (${unchanged.writes} times, ${unchanged.ms} ms)`);

    // A keystroke in the text, flushed at once: within the long document's throttle.
    await page.evaluate(() => {
      const last = [...document.querySelectorAll('#ag-editor-id > p')].pop();
      const text = last.querySelector('.ag-paragraph, span') || last;
      const range = document.createRange();
      range.selectNodeContents(text);
      range.collapse(false);
      getSelection().removeAllRanges();
      getSelection().addRange(range);
      last.scrollIntoView();
    });
    await page.click('#ag-editor-id > p:last-of-type');
    await page.keyboard.press('End');
    await page.keyboard.type('X');
    const typed = await flush();
    console.log(`after a keystroke: ${typed.ms} ms, written out ${typed.writes} times; ${difference(doc, typed.text)}`);
    check(!doc.includes('X') && typed.text.includes('X') && typed.text.replace('X', '') === doc, 'a keystroke just made is in the text handed back');
    const again = await flush();
    check(again.text === typed.text && again.writes === 0, `asked again with nothing new, the same text, not written out again (${again.writes} times)`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
