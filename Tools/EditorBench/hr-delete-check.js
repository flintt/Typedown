// Backspace at the start of the block after a thematic break, and Delete at the end of the one before it, take the break
// away and leave the caret where it was; the next one joins the two blocks as usual. The break became the text `---`
// joined to the paragraph ("---After", "Before---").
//
//   node hr-delete-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    // The caret in the text block whose text starts with `starts`, at `offset` (-1: its end).
    const caretAt = async (starts, offset) => {
      const ok = await page.evaluate((starts, offset) => {
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
        const position = { key: block.key, offset: offset < 0 ? block.text.length : offset };
        state.cursor = { start: position, end: position, anchor: position, focus: position };
        state.render();
        return true;
      }, starts, offset);
      if (!ok) throw new Error(`no block starting ${JSON.stringify(starts)}`);
      await pause(200);
    };
    const doc = 'Before\n\n---\n\nAfter\n';
    const cases = [
      // [what, document, block start, caret offset, key, presses, expected after an X typed]
      ['Backspace after the break takes it away', doc, 'After', 0, 'Backspace', 1, 'Before\n\nXAfter\n'],
      ['Delete before the break takes it away', doc, 'Before', -1, 'Delete', 1, 'BeforeX\n\nAfter\n'],
      ['a second Backspace joins the paragraphs', doc, 'After', 0, 'Backspace', 2, 'BeforeXAfter\n'],
      ['a second Delete joins the paragraphs', doc, 'Before', -1, 'Delete', 2, 'BeforeXAfter\n'],
      ['Backspace after a break that starts the document', '---\n\nAfter\n', 'After', 0, 'Backspace', 1, 'XAfter\n'],
      ['Backspace after a break written ***', 'Before\n\n***\n\nAfter\n', 'After', 0, 'Backspace', 1, 'Before\n\nXAfter\n'],
      ['Backspace at the start of a heading after a break', 'Before\n\n---\n\n# Title\n', '# Title', 2, 'Backspace', 1, null],
    ];
    for (const [what, md, starts, offset, key, presses, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretAt(starts, offset);
      for (let i = 0; i < presses; i++) {
        await page.keyboard.press(key);
        await pause(250);
      }
      await page.keyboard.type('X');
      await pause();
      const actual = await flush();
      // A heading's Backspace after its marks is the heading's own business (it becomes a paragraph); only the break
      // must not end up as text in it.
      const ok = expected === null ? !actual.includes('---X') && !actual.includes('X---') : actual === expected;
      console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + (expected === null ? JSON.stringify(actual) : firstDifference(expected, actual))}`);
      if (!ok) failures.push(what);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
