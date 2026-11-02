// Two editing behaviours readers reported elsewhere (docs/todo.md, 在线讨论汇总 #7 and #8), checked again:
// - "$" amounts are not formulas: "价格 $5 和 $10" shown as text, not math (render), and typed the "$" auto-paired
//   before a digit takes its pair back (input);
// - "---" typed in a new paragraph after Enter is a rule and leaves the line above as it was; only "---" on the next
//   line of the same paragraph makes that paragraph a level 2 heading (CommonMark's setext heading).
//
//   node money-rule-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(400); };
  const caretAtEnd = async text => {
    await page.evaluate(text => {
      const s = window.__typedownMuya.contentState;
      const spans = []; (function walk(bs) { for (const b of bs) { if (b.type === 'span') spans.push(b); if (b.children) walk(b.children); } })(s.blocks);
      const span = spans.find(b => b.text.includes(text));
      const p = { key: span.key, offset: span.text.length };
      s.cursor = { start: p, end: p, anchor: p, focus: p }; s.render();
    }, text);
    await pause(200);
  };
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  try {
    // Render: amounts as text.
    await visual('价格 $5 和 $10 元，合计 $15。\n');
    const shown = await page.evaluate(() => ({ math: document.querySelectorAll('#ag-editor-id .ag-inline-math, #ag-editor-id .katex').length, text: document.querySelector('#ag-editor-id p').innerText }));
    report(shown.math === 0 && /\$5/.test(shown.text) && /\$10/.test(shown.text), 'amounts in dollars are shown as text, not formulas', JSON.stringify(shown));

    // Input: "$" then a digit.
    await visual('Start\n');
    await caretAtEnd('Start');
    await page.keyboard.press('Enter');
    await page.keyboard.type('价格 $5 和 $10', { delay: 30 });
    await pause(400);
    const typed = await markdown();
    report(/价格 \$5 和 \$10\n?$/.test(typed.trim() + '\n') && !/\$\$|\$5\$|\$10\$/.test(typed), 'typed: "$" before a digit keeps no auto-paired "$"', JSON.stringify(typed));

    // "---" after Enter: a rule, the line above unchanged.
    await visual('Title line\n');
    await caretAtEnd('Title line');
    await page.keyboard.press('Enter');
    await page.keyboard.type('---', { delay: 40 });
    await page.keyboard.press('Enter');
    await pause(400);
    // A rule is a block of its own in Muya (functionType thematicBreakLine), not an <hr> on the page.
    const afterEnter = await page.evaluate(() => {
      const rules = []; (function walk(bs) { for (const b of bs) { if (b.functionType === 'thematicBreakLine') rules.push(b); if (b.children) walk(b.children); } })(window.__typedownMuya.contentState.blocks);
      return { md: window.__typedownMuya.getMarkdown(), h2: document.querySelectorAll('#ag-editor-id h2').length, hr: rules.length };
    });
    report(afterEnter.h2 === 0 && afterEnter.hr === 1 && /^Title line\n/.test(afterEnter.md), '"---" in a new paragraph is a rule, the line above stays a paragraph', JSON.stringify(afterEnter));

    // "---" on the next line of the same paragraph (a file written so): a level 2 heading, as CommonMark has it.
    await visual('Title line\n---\n\nText.\n');
    const setext = await page.evaluate(() => document.querySelectorAll('#ag-editor-id h2').length);
    report(setext === 1, '"---" right under a line of the same paragraph makes it a level 2 heading (CommonMark)', `${setext} h2`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
