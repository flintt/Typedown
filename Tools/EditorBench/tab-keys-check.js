// Tab and Shift+Tab in the visual editor: what they do reaches the host (the text it gets back, FlushContent) - the
// editor handled the key and told nothing, so the host kept the text from before and a save at once lost the indent.
// In a code block Shift+Tab takes one level of indent off the caret's line, or off each line a selection touches, and
// Tab over a selection puts one on each (they did nothing).
//
//   node tab-keys-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  try {
    // The caret (or a selection from `offset` to `to`) in the text block whose text starts with `starts`.
    const caretAt = async (starts, offset, to = offset) => {
      const ok = await page.evaluate((starts, offset, to) => {
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
        const start = { key: block.key, offset }, end = { key: block.key, offset: to };
        state.cursor = { start, end, anchor: start, focus: end };
        state.render();
        return true;
      }, starts, offset, to);
      if (!ok) throw new Error(`no block starting ${JSON.stringify(starts)}`);
      await pause(200);
    };
    const nb = ' '.repeat(4);
    const cases = [
      // [what, document, block start, caret offset, selection end, Shift, typed after, expected]
      ['Tab indents a list item', '- one\n- two\n', 'two', 1, 1, false, '', '- one\n  - two\n'],
      ['Shift+Tab unindents a list item', '- one\n    - two\n', 'two', 1, 1, true, '', '- one\n- two\n'],
      ['Tab at the start of a code line', 'Para\n\n```\nline\n```\n', 'line', 0, 0, false, '', `Para\n\n\`\`\`\n${nb}line\n\`\`\`\n`],
      ['Tab in a paragraph', 'Para one\n', 'Para', 4, 4, false, '', `Para${nb} one\n`],
      ['Shift+Tab in an indented code line, caret after the indent', 'Para\n\n```\n    indented\n```\n', '    indented', 6, 6, true, 'X', 'Para\n\n```\ninXdented\n```\n'],
      ['Shift+Tab with only two spaces of indent', 'Para\n\n```\n  two\n```\n', '  two', 3, 3, true, 'X', 'Para\n\n```\ntXwo\n```\n'],
      ['Shift+Tab with the caret inside the indent', 'Para\n\n```\n    deep\n```\n', '    deep', 2, 2, true, 'X', 'Para\n\n```\nXdeep\n```\n'],
      ['Shift+Tab takes a tab character', 'Para\n\n```\n\ttabbed\n```\n', '\ttabbed', 1, 1, true, '', 'Para\n\n```\ntabbed\n```\n'],
      ['Shift+Tab over two selected code lines', 'Para\n\n```\n    a\n    b\n    c\n```\n', '    a', 5, 11, true, '', 'Para\n\n```\na\nb\n    c\n```\n'],
      ['Tab over two selected code lines', 'Para\n\n```\na\nb\nc\n```\n', 'a', 0, 3, false, '', `Para\n\n\`\`\`\n${nb}a\n${nb}b\nc\n\`\`\`\n`],
      ['Shift+Tab on an unindented code line changes nothing', 'Para\n\n```\nflat\n```\n', 'flat', 2, 2, true, 'X', 'Para\n\n```\nflXat\n```\n'],
      ['Shift+Tab in a paragraph with no space before the caret changes nothing', 'Para one\n\nPara two\n', 'Para two', 4, 4, true, 'X', 'Para one\n\nParaX two\n'],
      // Tab's space taken back in a paragraph (a reader's report: Shift+Tab could not take out what Tab put in).
      ['Shift+Tab right after the space Tab put in a paragraph', `Para${nb} one\n`, 'Para', 8, 8, true, 'X', 'ParaX one\n'],
      ['Shift+Tab in a paragraph Tab indented, the caret in its text', `${nb}Para one\n`, `${nb}Para`, 8, 8, true, 'X', 'ParaX one\n'],
    ];
    for (const [what, md, starts, offset, to, shift, typed, expected] of cases) {
      await mode(true, false);
      await load(md);
      await mode(false, false);
      await caretAt(starts, offset, to);
      if (shift) await page.keyboard.down('Shift');
      await page.keyboard.press('Tab');
      if (shift) await page.keyboard.up('Shift');
      await pause(200);
      if (typed) await page.keyboard.type(typed);
      await pause();
      // What the host gets - and, so a failure says which half is wrong, what the editor itself holds.
      const actual = await flush();
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const ok = actual === expected;
      console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': host ' + firstDifference(expected, actual) + (held === expected ? ' (the editor holds the right text)' : '; editor ' + firstDifference(expected, held))}`);
      if (!ok) failures.push(what);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
