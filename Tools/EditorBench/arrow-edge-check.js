// Up on the first line of a document goes to the start of that line, as in any text box - the visual editor took the
// key and did nothing, so the caret stayed where it was. Up elsewhere still goes to the block before, at its end.
//
//   node arrow-edge-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    const caretIn = async (index, offset) => {
      await page.evaluate((index, offset) => {
        const state = window.__typedownMuya.contentState;
        const block = state.blocks[index].children[0];
        const position = { key: block.key, offset };
        state.cursor = { start: position, end: position, anchor: position, focus: position };
        state.render();
      }, index, offset);
      await pause(200);
    };
    const cases = [
      // [what, document, block index, caret offset, expected after Up and an X typed]
      ['Up in the middle of the first paragraph', 'First line text\n\nSecond\n', 0, 5, 'XFirst line text\n\nSecond\n'],
      ['Up at the end of a first heading', '# Title\n\nSecond\n', 0, 5, '# XTitle\n\nSecond\n'],
      ['Up in the second paragraph (the block before, at its end)', 'First\n\nSecond line\n', 1, 3, 'FirstX\n\nSecond line\n'],
    ];
    for (const [what, md, index, offset, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretIn(index, offset);
      await page.keyboard.press('ArrowUp');
      await pause(200);
      await page.keyboard.type('X');
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
