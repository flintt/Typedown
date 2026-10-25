// One edit in the visual editor changes the lines it edited, not the whole document (services/sourceMerge.ts): the
// reader's text is kept where they did not edit. Before, the first edit made Muya's whole export the document -
// tables padded, footnotes moved to the end, markers made uniform - and a version history or a diff showed nearly
// every line changed for one keystroke. On the showcase (raw HTML, tables, footnotes, every kind of block):
//
//   1. a letter typed at the end of a paragraph: only that line differs from the file, the footnotes stay where they
//      were written, and Muya reads the result back as exactly what it would have written itself;
//   2. typed and undone: the file again, byte for byte;
//   3. an edit in a block the file wrote otherwise than Muya does (a compact table): that block is written as Muya
//      writes it, the rest as it was.
//
//   node source-keep-check.js
const fs = require('fs');
const path = require('path');
const { firstDifference, openEditor } = require('./harness');

const showcase = fs.readFileSync(path.join(__dirname, 'fixtures/showcase/showcase.md'), 'utf8');
const lines = t => t.split('\n');
const changedLines = (a, b) => {
  // Lines of b not in a at the same place, after the common start and end (a single edit's span).
  const x = lines(a), y = lines(b);
  let s = 0; while (s < x.length && s < y.length && x[s] === y[s]) s++;
  let e = 0; while (e < x.length - s && e < y.length - s && x[x.length - 1 - e] === y[y.length - 1 - e]) e++;
  return Math.max(x.length - s - e, y.length - s - e);
};

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail = '') => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok || !detail ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const caretAtEndOf = async startText => page.evaluate(startText => {
    const state = window.__typedownMuya.contentState;
    const find = blocks => { for (const b of blocks) { if (b.text && b.text.startsWith(startText)) return b; const f = b.children && find(b.children); if (f) return f; } return null; };
    const block = find(state.blocks);
    if (!block) return false;
    const position = { key: block.key, offset: block.text.length };
    state.cursor = { start: position, end: position, anchor: position, focus: position };
    state.render();
    return true;
  }, startText);
  try {
    // 1. A letter at the end of a paragraph.
    await mode(true, false); await load(showcase); await mode(false, false); await pause(1500);
    check(await caretAtEndOf('Markdown was created in 2004'), 'the paragraph is there');
    await pause(200);
    await page.keyboard.type('X');
    await pause(500);
    let t = await flush();
    const counts = await page.evaluate(() => ({ ...window.__typedownSourceKept }));
    const n = changedLines(showcase, t);
    console.log(`  lines changed: ${n}; ${JSON.stringify(counts)}`);
    check(n === 1 && t.includes('X'), 'one letter typed changes one line of the file', firstDifference(showcase, t));
    check(t.indexOf('[^1]: The original') === showcase.indexOf('[^1]: The original') + 1 || t.indexOf('[^1]: The original') < t.indexOf('[^unused-footnote]'), 'the footnotes stay where they were written');
    const readBack = await page.evaluate(t => window.__typedownMuya.normalizeMarkdown(t) === window.__typedownMuya.getMarkdown(), t);
    check(readBack, 'Muya reads the text back as exactly what it would have written');

    // 2. Undone: the file again (the page's own undo here, as in reading the export back - the app's goes through the host).
    await page.keyboard.press('Backspace');
    await pause(500);
    t = await flush();
    check(t === showcase, 'typed and deleted again: the file, byte for byte', firstDifference(showcase, t));

    // 3. An edit in a table the file writes compactly: that table as Muya writes it, the rest as it was.
    const compact = '# T\n\n|a|b|\n|-|-|\n|1|22|\n\nSome text.\n\n|c|d|\n|-|-|\n|3|4|\n';
    await mode(true, false); await load(compact); await mode(false, false); await pause(800);
    check(await caretAtEndOf('22'), 'the table cell is there');
    await page.keyboard.type('3');
    await pause(500);
    t = await flush();
    console.log('  ' + JSON.stringify(t));
    check(t.includes('223') && t.includes('\n|c|d|\n|-|-|\n|3|4|\n') && t.includes('\n\nSome text.\n'), 'the table edited is written as Muya writes it, the other one and the paragraph as they were', t);

    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
