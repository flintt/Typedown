// Settings > Editor > Scroll past the end: scrolled all the way down, the last line comes up to about the middle of the
// window, in editing and reading; off (the default), it stays at the window's bottom as before.
//
//   node scroll-past-end-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    const doc = Array.from({ length: 60 }, (_, i) => `Paragraph ${i + 1} of a document long enough to scroll.`).join('\n\n') + '\n';
    // Scrolled to the bottom: where the last block's bottom is, as a share of the window's height.
    const lastAtBottom = () => page.evaluate(async () => {
      window.scrollTo(0, document.documentElement.scrollHeight);
      await new Promise(r => setTimeout(r, 300));
      const blocks = document.querySelectorAll('#ag-editor-id > *');
      return blocks[blocks.length - 1].getBoundingClientRect().bottom / window.innerHeight;
    });
    // Off, the page already ends a little below the last line (its own margin): on, the last line comes up a quarter of
    // the window or more from there, to the window's upper part.
    for (const [what, readOnly] of [['editing', false], ['reading', true]]) {
      const shares = {};
      for (const on of [false, true]) {
        await page.evaluate(o => window.__deliver('SettingsChanged', o), { scrollPastEnd: on });
        await pause(300);
        await mode(true, false);
        await load(doc);
        await mode(false, readOnly);
        await pause(500);
        shares[on] = await lastAtBottom();
      }
      check(shares.false > 0.75, `${what}, off: the last line near the window's bottom scrolled all the way down (${(shares.false * 100).toFixed(0)}%)`);
      check(shares.true < 0.65 && shares.false - shares.true > 0.25, `${what}, on: the last line comes up to the window's middle (${(shares.true * 100).toFixed(0)}%)`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
