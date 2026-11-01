// Up on the first line of a document goes to the start of that line, as in any text box - the visual editor took the
// key and did nothing, so the caret stayed where it was. Up and Down into another block keep the caret's place across
// the window (they went to the end of the block above and the start of the one below); into a heading, the caret is
// never put before its # marks.
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
      ['Up from the end of a longer paragraph (the shorter one before, at its end)', 'First\n\nSecond line\n', 1, 11, 'FirstX\n\nSecond line\n'],
      ['Up from offset 8 keeps the column', 'abcdefghijklmnop\n\nabcdefghijklmnop\n', 1, 8, 'abcdefghXijklmnop\n\nabcdefghijklmnop\n'],
      ['Down from offset 8 keeps the column', 'abcdefghijklmnop\n\nabcdefghijklmnop\n', 0, 8, 'abcdefghijklmnop\n\nabcdefghXijklmnop\n', 'ArrowDown'],
      ['Down from the start of a paragraph into a heading: after its marks', 'abc\n\n# Heading\n', 0, 0, 'abc\n\n# XHeading\n', 'ArrowDown'],
      ['Up from the start of a paragraph into a heading: after its marks', '# Heading\n\nabc\n', 1, 0, '# XHeading\n\nabc\n'],
      // The item's text starts further right than the paragraph's: the same place on the screen is a few letters in.
      ['Up into a list item keeps the place on the screen', '- abcdefghijklmnop\n\nabcdefghijklmnop\n', 1, 8, /^- [a-h]{2,7}X[a-p]+\n\nabcdefghijklmnop\n$/],
    ];
    for (const [what, md, index, offset, expected, key = 'ArrowUp'] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretIn(index, offset);
      await page.keyboard.press(key);
      await pause(200);
      await page.keyboard.type('X');
      await pause();
      const actual = await flush();
      const ok = expected instanceof RegExp ? expected.test(actual) : actual === expected;
      console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + (expected instanceof RegExp ? `${JSON.stringify(actual)} is not ${expected}` : firstDifference(expected, actual))}`);
      if (!ok) failures.push(what);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
