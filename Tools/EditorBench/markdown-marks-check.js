// The "always show Markdown marks" setting (Settings > Editor, `showMarkdownMarks`): the marks of every block - the
// heading's #, the ** of bold, the backticks of code, a link's brackets and address - are shown as the block with the
// caret shows them, not only there. What is drawn in place of its source (a formula, an emoji) is not shown twice.
// Reading mode hides them either way. Off, the default, a block without the caret hides its marks as before. The text handed back is the same either way.
//
//   node markdown-marks-check.js
const { openEditor } = require('./harness');

const doc = '# Heading\n\nSome **bold** and `code` and a [link](https://example.com) here.\n\nMaths $x^2$ and :smile: too.\n\nThe caret is here.\n';

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const setting = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { showMarkdownMarks: on }), on);
    await new Promise(r => setTimeout(r, 300));
  };
  // The width the marks take: the heading's, the inline ones (**, backticks, a link's brackets and address), and the
  // source and marks of what is drawn instead (a formula's $, an emoji's :name:).
  const widths = () => page.evaluate(() => {
    const width = selector => Math.round([...document.querySelectorAll(selector)].reduce((sum, e) => sum + e.getBoundingClientRect().width, 0));
    return {
      heading: width('#ag-editor-id h1 .ag-hide.ag-remove'),
      inline: width('#ag-editor-id p .ag-hide.ag-remove'),
      drawn: width('#ag-editor-id .ag-math-marker, #ag-editor-id .ag-emoji-marker, #ag-editor-id .ag-emoji-marked-text'),
    };
  });
  try {
    await mode(false, false);
    await load(doc);
    await new Promise(r => setTimeout(r, 500));
    for (const on of [false, true, false]) {
      await setting(on);
      const state = on ? 'on' : 'off';
      const w = await widths();
      console.log(`${state}: ${JSON.stringify(w)}`);
      if (on) {
        check(w.heading > 0, `${state}: the heading's # is shown`);
        check(w.inline > 200, `${state}: the inline marks (**, backticks, the link's) are shown`);
      } else {
        check(w.heading === 0 && w.inline === 0, `${state}: the marks take no room (as before)`);
      }
      check(w.drawn === 0, `${state}: a formula's and an emoji's source are not shown beside what is drawn`);
      check(await flush() === doc, `${state}: the text handed back is the document`);
      if (on) {
        // Reading mode reads the document, not its source: no marks there.
        await mode(false, true);
        const reading = await widths();
        console.log(`reading: ${JSON.stringify(reading)}`);
        check(reading.heading === 0 && reading.inline === 0 && reading.drawn === 0, `${state}: in reading mode the marks are hidden`);
        await mode(false, false);
      }
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
