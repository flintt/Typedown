// The source of a raw HTML block is on the page only while the caret is in it (the preview stands for it otherwise), and
// only then is it highlighted: highlighting the hidden source of every HTML block was most of what drawing a document of
// raw HTML cost (LT01: a 1.5 MB one, 20 MB of HTML for the page and 430,000 elements, 60% of the drawing time).
// Out of the block its source is there as plain text (the same text: copying, the caret and the model read it); the caret
// going in draws it highlighted, going out plain again; a search match in it is marked either way.
//
//   node html-source-check.js
const { openEditor } = require('./harness');

const doc = [
  '# HTML', 'Before the blocks.',
  '<div style="color: red" class="note">\n<p>First <b>block</b></p>\n</div>',
  'Between them.',
  '<table>\n<tr><td style="padding: 4px">cell</td><td>two</td></tr>\n</table>',
  'After the blocks.', '',
].join('\n\n');

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const blocks = () => page.evaluate(() => Array.from(document.querySelectorAll('#ag-editor-id > figure[data-role="HTML"]'), f => {
    const code = f.querySelector('pre.ag-html-block code');
    return { id: f.id, active: f.classList.contains('ag-active'), tokens: code ? code.querySelectorAll('.token').length : -1, text: code ? code.textContent : null, highlight: f.querySelectorAll('pre .ag-highlight, pre .ag-selection').length };
  }));
  try {
    await mode(false, false);
    await load(doc);
    let b = await blocks();
    console.log(JSON.stringify(b));
    check(b.length === 2, `two HTML blocks (${b.length})`);
    check(b.every(x => !x.active && x.tokens === 0), `out of them, their source is not highlighted (${b.map(x => x.tokens).join(', ')} highlighted parts)`);
    check(b[0] && b[0].text.startsWith('<div style="color: red" class="note">') && b[1].text.startsWith('<table>'), 'it is there as their text');

    // The caret into the first (as the editor puts it there, ContentState.handleContainerBlockClick): highlighted.
    await page.evaluate(id => window.__typedownMuya.contentState.handleContainerBlockClick(document.getElementById(id)), b[0].id);
    b = await blocks();
    check(b[0].active && b[0].tokens > 0, `the caret in the first: it is shown and highlighted (${b[0].tokens} parts)`);
    check(!b[1].active && b[1].tokens === 0, 'the other one is still plain');
    // And out again, to the paragraph after them: plain again.
    await page.evaluate(() => {
      const cs = window.__typedownMuya.contentState;
      const p = Array.from(document.querySelectorAll('#ag-editor-id > p')).pop();
      const key = p.querySelector('.ag-paragraph-content, span[id]').id;
      cs.cursor = { start: { key, offset: 0 }, end: { key, offset: 0 } };
      cs.partialRender();
    });
    b = await blocks();
    check(b.every(x => !x.active && x.tokens === 0), `the caret out of it: plain again (${b.map(x => x.tokens).join(', ')})`);

    // A search match in a block's source is marked whether the block is highlighted or not.
    await page.evaluate(() => window.__deliver('Search', { value: 'padding', opt: { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: {} } }));
    await new Promise(r => setTimeout(r, 500));
    b = await blocks();
    check(b[1].highlight === 1, `a search marks a match in the plain source (${b[1].highlight})`);
    await page.evaluate(() => window.__deliver('SearchOpenChange', { open: 0 }));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
