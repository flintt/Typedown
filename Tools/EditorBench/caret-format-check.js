// A format pressed with the caret alone in plain text (nothing selected) - Bold, Italic, Strikethrough, Underline,
// Inline code, by the context menu, the Format menu or a shortcut (all are the host's Format). Pressed a second time
// at the same caret it takes back what the first press did: the text is as it was, no doubled marks (********). Pressed
// and the caret then moved away by a click, without typing, nothing is left in the document (no empty **** saved into
// the file). Typing after the press types formatted text. The user's question (2026-11-15): the press puts empty
// marks at the caret, which are not read as a format, so the second press added another pair and a click elsewhere
// left them.
//
//   node caret-format-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(300); };
    // The caret at `at` in the first paragraph's text.
    const place = async at => {
      await page.evaluate(at => {
        const state = window.__typedownMuya.contentState;
        let span; (function walk(bs) { for (const b of bs) { if (b.type === 'span' && !span) span = b; if (b.children) walk(b.children); } })(state.blocks);
        const start = { key: span.key, offset: at }, end = { key: span.key, offset: at };
        state.cursor = { start, end, anchor: start, focus: end }; state.render();
      }, at);
      await pause(200);
    };
    const format = async type => { await page.evaluate(f => window.__deliver('Format', f), type); await pause(300); };
    // The formats the menu shows where the caret is, and the caret's block text.
    const where = () => page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const { start, end } = state.cursor;
      const formats = state.selectionFormats({ start: { ...start }, end: { ...end } }).formats.map(t => t.type === 'html_tag' ? t.tag : t.type);
      return { formats: [...new Set(formats)].sort().join('+'), offset: start.offset, text: state.getBlock(start.key).text };
    });
    const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
    // A real click in the middle of the second paragraph's text.
    const clickSecond = async () => {
      const point = await page.evaluate(() => {
        const p = document.querySelectorAll('#ag-editor-id p')[1];
        const walker = document.createTreeWalker(p, NodeFilter.SHOW_TEXT);
        let node; while ((node = walker.nextNode()) && !node.textContent.includes('Other')) {}
        const range = document.createRange(); range.setStart(node, 2); range.setEnd(node, 3);
        const r = range.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
      });
      await page.mouse.click(point.x, point.y);
      await pause(400);
    };

    const original = 'Some text here\n\nOther line\n';
    const at = 'Some '.length;
    // [format, what typing "ab" after the press gives]
    for (const [type, typed] of [
      ['strong', 'Some **ab**text here'],
      ['em', 'Some *ab*text here'],
      ['del', 'Some ~~ab~~text here'],
      ['u', 'Some <u>ab</u>text here'],
      ['inline_code', 'Some `ab`text here'],
    ]) {
      // Pressed, then pressed again at the same caret: as it was.
      await visual(original);
      await place(at);
      await format(type);
      const once = await where();
      console.log(`       ${type} pressed once: ${JSON.stringify(once)}`);
      report(once.formats.split('+').includes(type), `${type}, the caret alone: shown pressed after the press`, JSON.stringify(once));
      await format(type);
      let held = await markdown();
      report(held === original, `${type}, the caret alone, pressed twice: the text as it was`, firstDifference(original, held));
      const twice = await where();
      report(!twice.formats.split('+').includes(type) && twice.offset === at, `${type}, pressed twice: shown off, the caret where it was`, JSON.stringify(twice));

      // Pressed, then a click elsewhere without typing: nothing left behind, in the page or the host.
      await visual(original);
      await place(at);
      await format(type);
      await clickSecond();
      held = await markdown();
      report(held === original, `${type}, pressed then a click elsewhere: nothing left in the text`, firstDifference(original, held));
      const host = await flush();
      report(host === original, `${type}, pressed then a click elsewhere: nothing left in the host's text`, firstDifference(original, host));

      // Pressed, then typing: formatted text (as today).
      await visual(original);
      await place(at);
      await format(type);
      await page.keyboard.type('ab');
      await pause(300);
      held = await markdown();
      const expected = `${typed}\n\nOther line\n`;
      report(held === expected, `${type}, pressed then "ab" typed: "ab" formatted`, firstDifference(expected, held));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
