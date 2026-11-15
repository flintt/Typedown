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
      // An inline HTML format (underline, sub, sup, mark) the same: the caret before its closing tag, and before any
      // marks closing around it, splits after all of them. The `</u>~~` of "~~<u>下划线回车</u>~~" went to the next
      // line as text (a reader's report).
      ['the report\'s line, the caret before </u>', '~~<u>下划线回车</u>~~\n\nnext\n', ['~~', 10], '~~<u>下划线回车</u>~~\n\nX\n\nnext\n'],
      ['a paragraph ending in underline', 'Text <u>under</u>\n\nnext\n', null, 'Text <u>under</u>\n\nX\n\nnext\n'],
      ['underline inside strikethrough (the report)', '~~<u>下划线回车</u>~~\n\nnext\n', null, '~~<u>下划线回车</u>~~\n\nX\n\nnext\n'],
      ['underline inside bold', 'Text **<u>both</u>**\n\nnext\n', null, 'Text **<u>both</u>**\n\nX\n\nnext\n'],
      ['bold inside underline', 'Text <u>**both**</u>\n\nnext\n', null, 'Text <u>**both**</u>\n\nX\n\nnext\n'],
      ['a paragraph ending in subscript', 'H<sub>2</sub>\n\nnext\n', null, 'H<sub>2</sub>\n\nX\n\nnext\n'],
      ['a paragraph ending in superscript', 'x<sup>2</sup>\n\nnext\n', null, 'x<sup>2</sup>\n\nX\n\nnext\n'],
      ['a paragraph ending in a <mark>', 'Text <mark>lit</mark>\n\nnext\n', null, 'Text <mark>lit</mark>\n\nX\n\nnext\n'],
      ['a list item ending in underline', '- item <u>under</u>\n- two\n', null, '- item <u>under</u>\n- X\n- two\n'],
      ['underline inside strikethrough, the caret before both closings', '~~<u>gone</u>~~ tail\n\nnext\n', ['~~', 9], '~~<u>gone</u>~~\n\nX tail\n\nnext\n'],
      ['underline mid-line, the caret before its closing', 'Text <u>under</u> tail\n\nnext\n', ['Text', 13], 'Text <u>under</u>\n\nX tail\n\nnext\n'],
      ['inside the underline\'s text (as bold does)', 'Text <u>under</u>\n\nnext\n', ['Text', 10], 'Text <u>un\n\nXder</u>\n\nnext\n'],
      // Shift+Enter (a line break in the paragraph) the same.
      ['Shift+Enter before bold\'s closing', 'Text **bold**\n\nnext\n', ['Text', 11], 'Text **bold**\nX\n\nnext\n', 'Shift+Enter'],
      ['Shift+Enter before underline\'s closing', 'Text <u>under</u>\n\nnext\n', ['Text', 13], 'Text <u>under</u>\nX\n\nnext\n', 'Shift+Enter'],
      // As before: a split inside the bold's text, and in plain text.
      ['inside the bold\'s text (as before)', 'Text **bold**\n\nnext\n', ['Text', 9], 'Text **bo\n\nXld**\n\nnext\n'],
      ['in plain text (as before)', 'Plain text\n\nnext\n', ['Plain', 5], 'Plain\n\nX text\n\nnext\n'],
    ];
    for (const [what, md, at, expected, key] of cases) {
      await visual(md);
      if (at) await caretAt(at[0], at[1]); else await clickEnd();
      if (key === 'Shift+Enter') {
        await page.keyboard.down('Shift'); await page.keyboard.press('Enter'); await page.keyboard.up('Shift');
      } else await page.keyboard.press('Enter');
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
