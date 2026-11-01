// A triple click selects a block up to the start of the next one (the browser takes its line break too); Bold or
// Strikethrough then (the context menu, a shortcut: the host's Format) formats that block only. The next block's start
// got an empty pair of marks ("****Second one"), and in a heading the marks went before the "#". A format also reaches
// the host at once, as typing does: the host kept the text from before it, so a save at once lost the bold.
//
//   node triple-click-format-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(400); };
    const tripleClick = async selector => {
      const at = await page.evaluate(s => { const r = document.querySelector(s).getBoundingClientRect(); return { x: r.left + 20, y: r.top + r.height / 2 }; }, selector);
      await page.mouse.click(at.x, at.y, { clickCount: 3 });
      await pause(400);
    };
    const select = async (from, to) => {
      await page.evaluate((from, to) => {
        const state = window.__typedownMuya.contentState;
        const spans = []; (function walk(bs) { for (const b of bs) { if (b.type === 'span') spans.push(b); if (b.children) walk(b.children); } })(state.blocks);
        const start = { key: spans[from[0]].key, offset: from[1] }, end = { key: spans[to[0]].key, offset: to[1] };
        state.cursor = { start, end, anchor: start, focus: end }; state.render();
      }, from, to);
      await pause(300);
    };
    const cases = [
      // [what, document, triple-clicked element or a selection [[span, offset], [span, offset]], format, expected]
      ['a paragraph, bold', 'First paragraph here\n\nSecond one\n', '#ag-editor-id p span.ag-paragraph', 'strong', '**First paragraph here**\n\nSecond one\n'],
      ['a paragraph, strikethrough', 'First paragraph here\n\nSecond one\n', '#ag-editor-id p span.ag-paragraph', 'del', '~~First paragraph here~~\n\nSecond one\n'],
      ['a list item, bold', '- item one\n- item two\n', '#ag-editor-id li p span.ag-paragraph', 'strong', '- **item one**\n- item two\n'],
      ['a heading, bold', '# A heading\n\nAfter\n', '#ag-editor-id h1 span.ag-paragraph', 'strong', '# **A heading**\n\nAfter\n'],
      ['the last paragraph, bold', 'Last paragraph only\n', '#ag-editor-id p span.ag-paragraph', 'strong', '**Last paragraph only**\n'],
      // As before: a selection across two blocks formats both, a word alone - and both reach the host.
      ['a selection across two paragraphs (as before)', 'First paragraph\n\nSecond one\n', [[0, 6], [1, 3]], 'strong', 'First **paragraph**\n\n**Sec**ond one\n'],
      ['a word in a paragraph', 'Some words here\n', [[0, 5], [0, 10]], 'strong', 'Some **words** here\n'],
      // A paragraph holding the marks in code, selected whole and struck through (or made bold): the marks in the code
      // span closed nothing, and the strikethrough took "符号前`" alone (a reader's report, 2026-11-01).
      ['a paragraph with "~~" in code, strikethrough', '符号前`~~, ** `符号后\n', [[0, 0], [0, 15]], 'del', '~~符号前`~~, ** `符号后~~\n', 'del'],
      ['a paragraph with "**" in code, bold', 'before `**` after\n', [[0, 0], [0, 17]], 'strong', '**before `**` after**\n', 'strong'],
    ];
    for (const [what, md, how, format, expected, around] of cases) {
      await visual(md);
      if (typeof how === 'string') await tripleClick(how); else await select(how[0], how[1]);
      await page.evaluate(f => window.__deliver('Format', f), format);
      await pause(500);
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const host = await flush();
      report(held === expected, `${what}: the document`, firstDifference(expected, held));
      report(host === held, `${what}: the host has it at once`, firstDifference(held, host));
      if (around) {
        // Drawn as the format around the whole text, the code span inside it.
        await mode(true, false); await mode(false, false); await pause(300);
        const drawn = await page.evaluate(tag => [...document.querySelectorAll(`#ag-editor-id ${tag}.ag-inline-rule`)].map(e => `${e.textContent}|${e.querySelectorAll('code').length}`), around);
        const text = md.trim();
        report(drawn.length === 1 && drawn[0] === `${text}|1`, `${what}: drawn around the whole text with its code inside`, JSON.stringify(drawn));
      }
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
