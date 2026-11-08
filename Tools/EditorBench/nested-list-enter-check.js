// Enter on the last, empty item of a nested list makes an item of the list around it - one level out, as in a word
// processor - and Enter on that one leaves the list. The empty item became a paragraph inside the item above first, so
// a second-level list took three Enters to leave.
//
//   node nested-list-enter-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    // The caret at the end of the text block whose text starts with `starts`.
    const caretAtEnd = async starts => {
      const ok = await page.evaluate(starts => {
        const state = window.__typedownMuya.contentState;
        const find = blocks => {
          for (const b of blocks) {
            if (b.type === 'span' && b.text.startsWith(starts)) return b;
            const f = b.children && find(b.children);
            if (f) return f;
          }
        };
        const block = find(state.blocks);
        if (!block) return false;
        const position = { key: block.key, offset: block.text.length };
        state.cursor = { start: position, end: position, anchor: position, focus: position };
        state.render();
        return true;
      }, starts);
      if (!ok) throw new Error(`no block starting ${JSON.stringify(starts)}`);
      await pause(200);
    };
    const cases = [
      // [what, document, item to end, Enters, expected after an X typed]
      ['second level, two Enters: an item one level out', '- one\n    - two\n', 'two', 2, '- one\n  - two\n- X\n'],
      ['second level, three Enters: out of the list', '- one\n    - two\n', 'two', 3, '- one\n  - two\n\nX\n'],
      ['third level, two Enters: a second-level item', '- one\n    - two\n        - three\n', 'three', 2, '- one\n  - two\n    - three\n  - X\n'],
      ['ordered, two Enters: the next number one level out', '1. one\n    1. two\n', 'two', 2, '1. one\n   1. two\n2. X\n'],
      ['task, two Enters: an unchecked task one level out', '- [ ] one\n    - [ ] two\n', 'two', 2, '- [ ] one\n  - [ ] two\n- [ ] X\n'],
      ['an item after: the new one goes between', '- one\n    - two\n- three\n', 'two', 2, '- one\n  - two\n- X\n- three\n'],
      ['a top-level list, two Enters: out of the list (as before)', '- one\n- two\n', 'two', 2, '- one\n- two\n\nX\n'],
    ];
    for (const [what, md, starts, enters, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretAtEnd(starts);
      for (let i = 0; i < enters; i++) {
        await page.keyboard.press('Enter');
        await pause(250);
      }
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
