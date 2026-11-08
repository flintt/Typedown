// Keys in headings and lists that the key matrix (key-matrix-check.js) found wrong, 2026-11-01:
//  - Enter in the middle of a heading: the new heading had the caret before its "#" marks (the next letter made it the
//    paragraph "X## rest"), and with the caret after a format's opening marks Enter threw.
//  - Enter at the start of a heading's text: an empty heading was left above and the caret put before the marks.
//  - Backspace at the start of a heading's text: the heading became the paragraph "##Heading", the marks in its text.
//  - Paragraph > Bullet list on a paragraph (or one in a quote): the caret one letter to the left.
//  - Delete at the end of a task item: the next item's text joined, its empty "- [ ]" left behind.
//
//   node heading-list-keys-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    const caretAt = async (starts, offset) => page.evaluate((starts, offset) => {
      const state = window.__typedownMuya.contentState;
      const find = bs => { for (const b of bs) { if (b.type === 'span' && b.text.startsWith(starts)) return b; const f = b.children && find(b.children); if (f) return f; } };
      const b = find(state.blocks);
      const p = { key: b.key, offset: offset < 0 ? b.text.length : offset };
      state.cursor = { start: p, end: p, anchor: p, focus: p };
      state.render();
    }, starts, offset);
    const heading = 'Before\n\n## Heading **bold** text\n\nAfter\n';
    const cases = [
      // [what, document, block start, offset (-1: end), what is done, expected (a string, or a RegExp)]
      ['Enter in the middle of a heading', heading, '## Heading', 5, 'Enter', 'Before\n\n## He\n\n## Xading **bold** text\n\nAfter\n'],
      ['Enter after a format\'s opening marks in a heading', heading, '## Heading', 13, 'Enter', /^Before\n\n## Heading \*\*\n\n## X/],
      ['Enter at the start of a heading\'s text', heading, '## Heading', 3, 'Enter', /^Before\n\n+## XHeading \*\*bold\*\* text\n\nAfter\n$/],
      ['Backspace at the start of a heading\'s text', heading, '## Heading', 3, 'Backspace', 'Before\n\nXHeading **bold** text\n\nAfter\n'],
      ['Bullet list from the menu at a paragraph\'s end', 'Before\n\nSome text here\n\nAfter\n', 'Some', -1, ['UpdateParagraph', 'ul-bullet'], 'Before\n\n- Some text hereX\n\nAfter\n'],
      ['Bullet list from the menu in a quote', 'Before\n\n> Some text\n\nAfter\n', 'Some', -1, ['UpdateParagraph', 'ul-bullet'], 'Before\n\n> - Some textX\n\nAfter\n'],
      ['"- " typed at a paragraph\'s start makes a list (as before)', 'Before\n\nitem\n\nAfter\n', 'item', 0, '- ', 'Before\n\n- Xitem\n\nAfter\n'],
      ['Delete at the end of a task item', '- [ ] one\n- [ ] two\n- [ ] three\n', 'two', -1, 'Delete', '- [ ] one\n- [ ] twoXthree\n'],
    ];
    for (const [what, md, starts, offset, action, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretAt(starts, offset);
      await pause(200);
      const errorsBefore = errors.length;
      if (Array.isArray(action)) await page.evaluate(([m, a]) => window.__deliver(m, a), action);
      else if (action === '- ') await page.keyboard.type('- ', { delay: 40 });
      else await page.keyboard.press(action);
      await pause(300);
      await page.keyboard.type('X');
      await pause();
      const actual = await flush();
      const threw = errors.length > errorsBefore ? errors.slice(errorsBefore).join(' | ') : null;
      const ok = !threw && (expected instanceof RegExp ? expected.test(actual) : actual === expected);
      console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + (threw ? 'threw ' + threw : expected instanceof RegExp ? `${JSON.stringify(actual)} is not ${expected}` : firstDifference(expected, actual))}`);
      if (!ok) failures.push(what);
    }
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
