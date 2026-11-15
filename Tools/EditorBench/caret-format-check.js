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
      const formats = state.selectionFormats({ start: { ...start }, end: { ...end } }, { pending: true }).formats.map(t => t.type === 'html_tag' ? t.tag : t.type);
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
      const typedOn = await where();
      report(typedOn.formats.split('+').includes(type), `${type}, pressed then "ab" typed: the caret still in the format`, JSON.stringify(typedOn));

      // Pressed twice, then typing: plain text.
      await visual(original);
      await place(at);
      await format(type);
      await format(type);
      await page.keyboard.type('ab');
      await pause(300);
      held = await markdown();
      report(held === 'Some abtext here\n\nOther line\n', `${type}, pressed twice then "ab" typed: plain`, firstDifference('Some abtext here\n\nOther line\n', held));

      // Pressed, then the caret moved by an arrow and typing: plain, where the caret went.
      await visual(original);
      await place(at);
      await format(type);
      await page.keyboard.press('ArrowRight');
      await pause(300);
      const moved = await where();
      report(!moved.formats.split('+').includes(type), `${type}, pressed then an arrow: shown off`, JSON.stringify(moved));
      await page.keyboard.type('ab');
      await pause(300);
      held = await markdown();
      report(held === 'Some tabext here\n\nOther line\n', `${type}, pressed, an arrow, then "ab" typed: plain`, firstDifference('Some tabext here\n\nOther line\n', held));
    }

    // Bold then Italic, then typing: both. Between spaces: this editor does not read ***ab*** with a letter straight
    // before or after it (typed by hand the same; CommonMark does).
    await visual('Some  here\n\nOther line\n');
    await place(at);
    await format('strong');
    await format('em');
    let both = await where();
    report(both.formats === 'em+strong', 'Bold then Italic at the caret: both shown pressed', JSON.stringify(both));
    await page.keyboard.type('ab');
    await pause(300);
    let held = await markdown();
    report(held === 'Some ***ab*** here\n\nOther line\n', 'Bold then Italic, then "ab" typed: bold and italic', firstDifference('Some ***ab*** here\n\nOther line\n', held));
    both = await where();
    report(both.formats === 'em+strong', 'Bold then Italic, then "ab" typed: the caret still in both', JSON.stringify(both));

    // Bold, then Italic pressed twice: bold only.
    await visual(original);
    await place(at);
    await format('strong'); await format('em'); await format('em');
    await page.keyboard.type('ab');
    await pause(300);
    held = await markdown();
    report(held === 'Some **ab**text here\n\nOther line\n', 'Bold, Italic twice, then "ab" typed: bold only', firstDifference('Some **ab**text here\n\nOther line\n', held));

    // An IME's text (Chinese, composed then committed) after Bold: inside the marks.
    await visual(original);
    await place(at);
    await format('strong');
    const cdp = await page.target().createCDPSession();
    await page.evaluate(() => { window.__compositionEnds = 0; document.addEventListener('compositionend', () => window.__compositionEnds++, true); });
    await cdp.send('Input.imeSetComposition', { text: 'zhong', selectionStart: 5, selectionEnd: 5 });
    await pause(150);
    await cdp.send('Input.imeSetComposition', { text: '中文', selectionStart: 2, selectionEnd: 2 });
    await pause(150);
    await cdp.send('Input.insertText', { text: '中文' });
    await pause(400);
    held = await markdown();
    const ends = await page.evaluate(() => window.__compositionEnds);
    report(ends > 0 && held === 'Some **中文**text here\n\nOther line\n', 'Bold, then Chinese by an IME: bold', `${ends} compositionend; ${firstDifference('Some **中文**text here\n\nOther line\n', held)}`);
    await cdp.detach();

    // The host is told no change for the press alone, and the typing as letters typed (its undo step starts there and
    // takes the marks out with the letters: no "****" in between).
    await visual(original);
    await place(at);
    const reports = () => page.evaluate(() => window.__last.MarkdownChange ? { text: window.__last.MarkdownChange.text, edit: window.__last.MarkdownChange.edit } : null);
    await pause(600);
    const beforePress = await reports();
    await format('strong');
    await pause(800);
    const afterPress = await reports();
    report(JSON.stringify(afterPress) === JSON.stringify(beforePress), 'Bold at the caret alone: no change reported to the host', `${JSON.stringify(beforePress)} -> ${JSON.stringify(afterPress)}`);
    await page.keyboard.type('ab', { delay: 40 });
    await pause(800);
    const afterTyping = await reports();
    report(afterTyping && afterTyping.text === 'Some **ab**text here\n\nOther line\n' && afterTyping.edit && afterTyping.edit.kind === 'insert',
      'Bold then "ab" typed: reported as letters typed', JSON.stringify(afterTyping));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
