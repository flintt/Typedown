// Paragraph > Duplicate puts a copy of the block with the caret after it, and the host has it. It failed outright from
// 7f4469dc (1.4.1) on: the copy buttons' clipboard copy was added as ContentState.prototype.copyBlock, over the class's
// own copyBlock - the deep copy of a block Duplicate uses - and Duplicate got a promise for a block.
//
//   node duplicate-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    const cases = [
      // [what, document, the element clicked into, expected (null: only that it does not fail)]
      ['a paragraph', 'First\n\nSecond paragraph\n', '#ag-editor-id > p:nth-of-type(2) span.ag-paragraph', 'First\n\nSecond paragraph\n\nSecond paragraph\n'],
      ['a heading', '# Title\n\nAfter\n', '#ag-editor-id h1 span.ag-paragraph', '# Title\n\n# Title\n\nAfter\n'],
      ['a code block', 'Before\n\n```js\nconst a = 1;\n```\n', '#ag-editor-id pre .ag-code-content', 'Before\n\n```js\nconst a = 1;\n```\n\n```js\nconst a = 1;\n```\n'],
      ['a table', 'Before\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n', '#ag-editor-id td span.ag-paragraph', 'Before\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n'],
      ['a list item (the list it is in)', '- one\n- two\n\nAfter\n', '#ag-editor-id li span.ag-paragraph', null],
    ];
    for (const [what, md, selector, expected] of cases) {
      await mode(true, false); await load(md); await mode(false, false); await pause(400);
      const at = await page.evaluate(s => { const r = document.querySelector(s).getBoundingClientRect(); return { x: r.left + 8, y: r.top + r.height / 2 }; }, selector);
      await page.mouse.click(at.x, at.y);
      await pause(300);
      const thrown = await page.evaluate(() => { try { window.__deliver('Duplicate'); return null } catch (e) { return e.message } });
      await pause(500);
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const host = await flush();
      report(!thrown, `${what}: Duplicate runs`, thrown);
      if (expected !== null) report(held === expected, `${what}: the block twice`, firstDifference(expected, held));
      else report(held !== md, `${what}: something duplicated (${JSON.stringify(held)})`, 'the document is unchanged');
      report(host === held, `${what}: the host has it`, firstDifference(held, host));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
