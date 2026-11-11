// Bold, italic, strikethrough and highlight show their markers only with the caret at one of them (the user's list,
// 2026-11-11, 9): a click in the middle of a bold word showed the ** on either side and the text moved under the
// pointer. With the caret in the middle the markers stay hidden; at the token's edge, between a marker and the text,
// or with a selection touching it they show; a link's and a code span's are as before (shown with the caret anywhere
// in them). Each position clicked as a reader does, with the real mouse, after placing the caret by the model too.
//
//   node inline-marks-edge-check.js
const { openEditor } = require('./harness');

const line = 'Start **bold word** then *italic word* and ~~struck word~~ and ==marked word== and `code word` end.';

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${seen === undefined ? '' : ': ' + seen}`); if (!ok) failures.push(what); };
  // The caret (or a selection) at offsets of the paragraph's text, drawn; then whether the markers of the token whose
  // text holds `word` are shown (not .ag-hide).
  const place = (start, end = start) => page.evaluate(({ start, end }) => {
    const state = window.__typedownMuya.contentState;
    const block = state.blocks[0].children[0];
    const a = { key: block.key, offset: start }, b = { key: block.key, offset: end };
    state.cursor = { start: a, end: b, anchor: a, focus: b };
    state.render();
  }, { start, end });
  const shown = tag => page.evaluate(tag => {
    const element = document.querySelector(`#ag-editor-id ${tag}.ag-inline-rule`) || document.querySelector(`#ag-editor-id ${tag}`);
    const markers = [element.previousElementSibling, element.nextElementSibling].filter(m => m && m.classList.contains('ag-remove'));
    return markers.length === 2 && markers.every(m => !m.classList.contains('ag-hide'));
  }, tag);
  try {
    await mode(false, false);
    await load(line + '\n');
    await pause(400);
    for (const [tag, open, word] of [['strong', '**', 'bold word'], ['em', '*', 'italic word'], ['del', '~~', 'struck word'], ['mark', '==', 'marked word']]) {
      const tokenStart = line.indexOf(open + word);
      const textStart = tokenStart + open.length;
      const textEnd = textStart + word.length;
      const middle = textStart + 3;
      await place(middle); await pause(80);
      check(!(await shown(tag)), `${tag}: the caret in the middle of its text leaves the markers hidden`);
      await place(textStart); await pause(80);
      check(await shown(tag), `${tag}: the caret right after the opening marker shows them`);
      await place(textEnd); await pause(80);
      check(await shown(tag), `${tag}: the caret right before the closing marker shows them`);
      await place(tokenStart); await pause(80);
      check(await shown(tag), `${tag}: the caret right before the opening marker shows them`);
      await place(middle - 1, middle + 2); await pause(80);
      check(await shown(tag), `${tag}: a selection in its text shows them`);
      await place(0); await pause(80);
      check(!(await shown(tag)), `${tag}: the caret elsewhere hides them`);
    }
    // A code span: shown with the caret anywhere in it, as before.
    const code = line.indexOf('`code word`') + 4;
    await place(code); await pause(80);
    const codeShown = await page.evaluate(() => { const c = document.querySelector('#ag-editor-id code'); const m = c && c.previousElementSibling; return !!m && !m.classList.contains('ag-hide'); });
    check(codeShown, 'a code span: the caret in its middle still shows its backticks');

    // A real click in the middle of the bold word: the markers stay hidden and the word does not move.
    await place(0); await pause(150);
    const before = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id strong').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
    await page.mouse.click(before.x + before.w / 2, before.y + before.h / 2);
    await pause(250);
    const after = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id strong').getBoundingClientRect(); return { x: r.left }; });
    check(!(await shown('strong')), 'clicked in the middle of the bold word: its markers stay hidden');
    check(Math.abs(after.x - before.x) < 1, 'and the word does not move under the pointer', `${before.x} -> ${after.x}`);
    check((await flush()) === line + '\n', 'the document is unchanged');
    if (errors.length) failures.push(`page errors: ${errors.slice(0, 3).join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
