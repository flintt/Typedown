// A document past 150,000 characters, edited in one place: while typing the host gets the editor's own export (the
// whole document in the editor's way of writing it - keeping the reader's source reads the result back, a fifth of a
// second at 300,000 characters, too much for every change); asked with keepSource (a save the reader made, a close) it
// gets the reader's own text with only the edit in it. Before, the save wrote the whole document reformatted.
//
//   node keep-source-on-save-check.js
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    // Written as the editor does not write it: "*" bullets and a "***" break; then a long document after.
    const head = '# Kept\n\n* star one\n* star two\n\n***\n\nEDITHERE paragraph.\n\n';
    const doc = head + fs.readFileSync(path.join(__dirname, 'doc300k.md'), 'utf8');
    const flush = async keepSource => {
      const started = Date.now();
      await page.evaluate(k => { delete window.__last.ContentFlushed; window.__deliver('FlushContent', { token: 7, keepSource: k }); }, keepSource);
      await page.waitForFunction(() => window.__last.ContentFlushed, { timeout: 60000 });
      return { text: await page.evaluate(() => window.__last.ContentFlushed.text), ms: Date.now() - started };
    };
    await mode(false, false);
    await page.evaluate(() => { delete window.__typedownProgressiveDone; });
    await load(doc);
    await page.waitForFunction(() => !!window.__typedownProgressiveDone, { timeout: 120000 });
    await pause(300);
    // One letter typed at the start of "EDITHERE".
    await page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const find = bs => { for (const b of bs) { if (b.type === 'span' && b.text.startsWith('EDITHERE')) return b; const f = b.children && find(b.children); if (f) return f; } };
      const b = find(state.blocks);
      const p = { key: b.key, offset: 0 };
      state.cursor = { start: p, end: p, anchor: p, focus: p }; state.render();
    });
    await pause(200);
    await page.keyboard.type('X');
    await pause(800);
    const expected = doc.replace('EDITHERE', 'XEDITHERE');
    const typing = await flush(false);
    check(typing.text.includes('XEDITHERE') && typing.text !== expected, `while typing, the editor's export (the "*" bullets ${typing.text.includes('* star one') ? 'kept' : 'rewritten'}), as before`);
    const saved = await flush(true);
    if (saved.text !== expected) {
      const i = [...saved.text].findIndex((c, i) => c !== expected[i]);
      console.log('    expected', JSON.stringify(expected.slice(i - 60, i + 60)));
      console.log('    got     ', JSON.stringify(saved.text.slice(i - 60, i + 60)));
      console.log('    kept:', JSON.stringify(await page.evaluate(() => window.__typedownSourceKept)));
    }
    check(saved.text === expected, `asked to keep the source: the reader's text with only the edit in it (${saved.text === expected ? 'so' : 'differs at ' + [...saved.text].findIndex((c, i) => c !== expected[i])}), in ${saved.ms} ms`);
    const after = await flush(false);
    check(after.text === expected, 'a flush after it gives the same text (nothing new typed)');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
