// Settings > Editor > Select all in steps (a reader's request, 2026-11-02): Ctrl+A (the host's SelectAll) selects the
// caret's paragraph, then each block around it that holds more, then the document; in source mode the paragraph, then
// the document. Off (the default), the document at once as before.
//
//   node select-all-steps-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = 'Intro paragraph\n\n- one\n- two\n  - nested a\n  - nested b\n- three\n\n> quote line\n\nEnd paragraph\n';
  // What is selected, as the words in it (the page's own selection, so line breaks and markers do not matter).
  const selected = () => page.evaluate(() => window.getSelection().toString().split(/\s+/).filter(Boolean).join(' '));
  const selectAll = async () => { await page.evaluate(() => window.__deliver('SelectAll')); await pause(250); };
  const caretIn = async text => {
    await page.evaluate(text => {
      const s = window.__typedownMuya.contentState;
      const spans = []; (function walk(bs) { for (const b of bs) { if (b.type === 'span') spans.push(b); if (b.children) walk(b.children); } })(s.blocks);
      const span = spans.find(b => b.text.includes(text));
      const p = { key: span.key, offset: span.text.indexOf(text) + 2 };
      s.cursor = { start: p, end: p, anchor: p, focus: p }; s.render();
    }, text);
    await pause(200);
  };
  const visual = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { selectAllInSteps: on }), on);
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(400);
    await page.click('#ag-editor-id p');
    await pause(200);
  };
  const all = 'Intro paragraph one two nested a nested b three quote line End paragraph';
  try {
    await visual(true);
    for (const [where, steps] of [
      ['nested a', ['nested a', 'nested a nested b', 'two nested a nested b', 'one two nested a nested b three', all]],
      ['quote', ['quote line', all]],
      ['Intro', ['Intro paragraph', all]],
    ]) {
      await caretIn(where);
      const seen = [];
      for (const _ of steps) { await selectAll(); seen.push(await selected()); }
      report(JSON.stringify(seen) === JSON.stringify(steps), `on, the caret in "${where}": ${steps.length} steps out to the document`, JSON.stringify(seen));
    }
    // Once the document is selected it stays so.
    await selectAll();
    report(await selected() === all, 'on, Ctrl+A again on the whole document keeps it', await selected());

    await visual(false);
    await caretIn('nested a');
    await selectAll();
    report(await selected() === all, 'off: the document at once', await selected());

    // Source mode: the paragraph's lines, then the document.
    await page.evaluate(() => window.__deliver('SettingsChanged', { selectAllInSteps: true }));
    await mode(true, false);
    await load(doc);
    await pause(400);
    const cm = () => page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.getSelection().split(/\s+/).filter(Boolean).join(' '));
    await page.evaluate(() => { const e = document.querySelector('.CodeMirror').CodeMirror; e.focus(); e.setCursor({ line: 4, ch: 4 }); });
    await selectAll();
    const first = await cm();
    await selectAll();
    const second = await cm();
    report(first === '- one - two - nested a - nested b - three', 'source mode, on: first the paragraph (the list\'s lines)', first);
    report(second.startsWith('Intro paragraph') && second.endsWith('End paragraph'), 'source mode, on: then the document', second);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
