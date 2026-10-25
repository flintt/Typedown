// Code spans as CommonMark reads them, in the visual editor: a span runs from its run of backticks to the next run of
// exactly as many, so backticks of other lengths inside it are its text and three spans in one sentence stay three
// (the old rule ran on to the line's last matching run and made them one). In a table cell, \| in code is a pipe
// that does not end the cell: its backslash is hidden with the caret elsewhere, as an escape is, not shown as code.
//
//   node inline-code-check.js
const { openEditor } = require('./harness');

const sentence = 'Inline code: with a backtick inside: `` a`b ``, two backticks inside: ``` a``b ```, and a code span that is only a backtick: `` ` ``.';
const doc = `# Start\n\n${sentence}\n\n| a | b |\n| --- | --- |\n| \`x \\|\\| y\` | 1 |\n`;

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    // The caret in the heading: the paragraph and the table are drawn as they read.
    await page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const block = state.blocks[0].children[0];
      const position = { key: block.key, offset: 2 };
      state.cursor = { start: position, end: position, anchor: position, focus: position };
      state.render();
    });
    await pause(300);
    const seen = await page.evaluate(() => {
      const root = document.getElementById('ag-editor-id');
      const paragraph = [...root.querySelectorAll('p')].find(p => p.textContent.includes('Inline code'));
      const codes = paragraph ? [...paragraph.querySelectorAll('code')].map(c => c.textContent) : null;
      // What reads: the editor hides a marker by giving it no width (.ag-hide), which innerText still counts.
      const shown = element => { const copy = element.cloneNode(true); copy.querySelectorAll('.ag-hide').forEach(x => x.remove()); return copy.textContent; };
      const cellCode = root.querySelector('td code, th code');
      return { codes, cell: cellCode ? shown(cellCode) : null };
    });
    console.log('  spans: ' + JSON.stringify(seen.codes) + '; cell: ' + JSON.stringify(seen.cell));
    check(JSON.stringify(seen.codes) === JSON.stringify([' a`b ', ' a``b ', ' ` ']), 'three code spans, each with its own backticks inside');
    check(seen.cell === 'x || y', 'in a table cell, \\| in code reads as |');
    check((await flush()) === doc, 'the document is unchanged');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
