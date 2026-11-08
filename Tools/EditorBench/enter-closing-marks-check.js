// Enter with the caret just before a format's closing marks splits the line after them: the marks stay with their text.
// A click past the end of "item **bold**" puts the caret before the hidden `**` (the browser lands on the last letter
// shown), and Enter took the `**` to the next line as text - "item **bold" and "**", both lines without the format;
// the reader had to click a second time to get past the marks. Splits anywhere else are as they were.
//
//   node enter-closing-marks-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(400); };
    // A click just right of the last letter shown on the first line, as the reader clicks the end of the line.
    const clickEnd = async () => {
      await page.mouse.click(5, 5);
      await pause(200);
      const at = await page.evaluate(() => {
        const span = document.querySelector('#ag-editor-id span.ag-paragraph');
        const walker = document.createTreeWalker(span, NodeFilter.SHOW_TEXT);
        let last = null, n;
        while ((n = walker.nextNode())) if (n.length && !n.parentElement.closest('.ag-hide')) last = n;
        const r = document.createRange(); r.setStart(last, last.length - 1); r.setEnd(last, last.length);
        const b = r.getBoundingClientRect();
        return { x: b.right + 3, y: b.top + b.height / 2 };
      });
      await page.mouse.click(at.x, at.y);
      await pause(350);
    };
    // The caret put at `offset` of the text block whose text starts with `starts`.
    const caretAt = async (starts, offset) => {
      await page.evaluate((starts, offset) => {
        const state = window.__typedownMuya.contentState;
        const find = bs => { for (const b of bs) { if (b.type === 'span' && b.text.startsWith(starts)) return b; const f = b.children && find(b.children); if (f) return f; } };
        const b = find(state.blocks);
        const p = { key: b.key, offset };
        state.cursor = { start: p, end: p, anchor: p, focus: p }; state.render();
      }, starts, offset);
      await pause(250);
    };
    const cases = [
      // [what, document, how the caret is put (null: a click at the line's end; else [block start, offset]), expected]
      ['a paragraph ending in bold', 'Text **bold**\n\nnext\n', null, 'Text **bold**\n\nX\n\nnext\n'],
      ['a paragraph ending in strikethrough', 'Text ~~gone~~\n\nnext\n', null, 'Text ~~gone~~\n\nX\n\nnext\n'],
      ['a paragraph ending in italics', 'Text *it*\n\nnext\n', null, 'Text *it*\n\nX\n\nnext\n'],
      ['a paragraph ending in inline code', 'Text `code`\n\nnext\n', null, 'Text `code`\n\nX\n\nnext\n'],
      ['a paragraph ending in a highlight', 'Text ==mark==\n\nnext\n', null, 'Text ==mark==\n\nX\n\nnext\n'],
      ['a list item ending in bold', '- item **bold**\n- two\n', null, '- item **bold**\n- X\n- two\n'],
      ['a list item ending in strikethrough', '- item ~~gone~~\n- two\n', null, '- item ~~gone~~\n- X\n- two\n'],
      ['a numbered item ending in bold', '1. item **bold**\n2. two\n', null, '1. item **bold**\n2. X\n3. two\n'],
      ['a task ending in bold', '- [ ] task **bold**\n- [ ] two\n', null, '- [ ] task **bold**\n- [ ] X\n- [ ] two\n'],
      ['a heading ending in bold', '# Title **bold**\n\nnext\n', null, '# Title **bold**\n\nX\n\nnext\n'],
      ['bold inside italics, the caret before both closings', 'Text ***both***\n\nnext\n', ['Text', 12], 'Text ***both***\n\nX\n\nnext\n'],
      ['a link, the caret at the end of its text', 'See [site](https://example.com)\n\nnext\n', ['See', 9], 'See [site](https://example.com)\n\nX\n\nnext\n'],
      ['bold mid-line, the caret before its closing', 'Text **bold** tail\n\nnext\n', ['Text', 11], 'Text **bold**\n\nX tail\n\nnext\n'],
      // As before: a split inside the bold's text, and in plain text.
      ['inside the bold\'s text (as before)', 'Text **bold**\n\nnext\n', ['Text', 9], 'Text **bo\n\nXld**\n\nnext\n'],
      ['in plain text (as before)', 'Plain text\n\nnext\n', ['Plain', 5], 'Plain\n\nX text\n\nnext\n'],
    ];
    for (const [what, md, at, expected] of cases) {
      await visual(md);
      if (at) await caretAt(at[0], at[1]); else await clickEnd();
      await page.keyboard.press('Enter');
      await pause(300);
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
