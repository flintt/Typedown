// An ordered list copied stays ordered (a reader's report, 2026-11-02: the outer list of a copy came out as bullets).
// The selected items' range has the list as its common ancestor, and its contents were bare <li>s, which the
// conversion to Markdown took for bullets. The whole list, from its second item (numbered from 2), and one with a
// bullet list inside it; a bullet list stays bullets.
//
//   node list-copy-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  // Copied from the start of one item's text to the end of another's: the text and HTML the clipboard gets.
  const copy = async (md, fromText, toText) => {
    await mode(true, false); await load(md); await mode(false, false); await pause(400);
    await page.click('#ag-editor-id p');
    return page.evaluate(({ fromText, toText }) => {
      const s = window.__typedownMuya.contentState;
      const spans = []; (function walk(bs) { for (const b of bs) { if (b.type === 'span') spans.push(b); if (b.children) walk(b.children); } })(s.blocks);
      const from = spans.find(b => b.text === fromText), to = spans.find(b => b.text === toText);
      const start = { key: from.key, offset: 0 }, end = { key: to.key, offset: to.text.length };
      s.cursor = { start, end, anchor: start, focus: end }; s.render();
      const { html, text } = s.getClipBoardData();
      return { html, text };
    }, { fromText, toText });
  };
  try {
    const cases = [
      ['an ordered list', '1. a\n2. b\n3. c\n\nafter\n', 'a', 'c', '1. a\n2. b\n3. c', /^<ol>/],
      ['an ordered list from its second item', '1. a\n2. b\n3. c\n\nafter\n', 'b', 'c', '2. b\n3. c', /^<ol start="2">/],
      ['an ordered list with bullets inside', '1. a\n2. b\n   - c\n   - d\n3. e\n\nafter\n', 'a', 'e', '1. a\n2. b\n   - c\n   - d\n3. e', /^<ol>/],
      ['a bullet list stays bullets', '- a\n- b\n\nafter\n', 'a', 'b', '- a\n- b', /^<ul>/],
    ];
    for (const [what, md, from, to, text, html] of cases) {
      const got = await copy(md, from, to);
      report(got.text === text, `${what}: the text copied`, firstDifference(text, got.text));
      report(html.test(got.html.trim()), `${what}: the HTML copied`, got.html.slice(0, 80));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
