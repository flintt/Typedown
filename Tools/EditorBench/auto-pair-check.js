// An opening `*`, `_`, `~` or `$` typed in a paragraph gets its closing one put in after the caret; when the next key is
// a space, or a digit after `$`, it was no opening mark and the closing one is taken out again. It stayed: "2 * 3 = 6"
// was saved as "2 * 3 = 6*", "costs $5." as "costs $5.$". Emphasis and maths typed as usual still come out whole.
//
//   node auto-pair-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    const cases = [
      // [what, document, typed at its end, expected]
      ['a multiplication', 'Note\n', ' 2 * 3 = 6', 'Note 2 * 3 = 6\n'],
      // 1.4.1 wrote "2 * 3 * 4**": the second "*" put in another pair (a reader's report).
      ['two multiplications', 'Note\n', ' 2 * 3 * 4', 'Note 2 * 3 * 4\n'],
      ['one amount', 'Price\n', ' costs $5.', 'Price costs $5.\n'],
      ['two amounts', 'Price\n', ' $5 and $10.', 'Price $5 and $10.\n'],
      ['an underscore on its own', 'Note\n', ' a _ b', 'Note a _ b\n'],
      ['a tilde on its own', 'Note\n', ' about ~ 5', 'Note about ~ 5\n'],
      ['inline maths', 'Say\n', ' $x$ ok', 'Say $x$ ok\n'],
      ['emphasis', 'Note\n', ' *word* ok', 'Note *word* ok\n'],
      ['strong', 'Note\n', ' **bold** ok', 'Note **bold** ok\n'],
      ['inline code', 'Note\n', ' `a b` ok', 'Note `a b` ok\n'],
    ];
    for (const [what, md, typed, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await page.evaluate(() => {
        const state = window.__typedownMuya.contentState;
        const block = state.blocks[0].children[0];
        const position = { key: block.key, offset: block.text.length };
        state.cursor = { start: position, end: position, anchor: position, focus: position };
        state.render();
      });
      await pause(200);
      await page.keyboard.type(typed, { delay: 40 });
      await pause();
      const actual = await flush();
      const ok = actual === expected;
      console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + firstDifference(expected, actual)}`);
      if (!ok) failures.push(what);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
