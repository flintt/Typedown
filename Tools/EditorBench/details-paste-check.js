// A <details> block's summary is one line of text: nothing typed, pasted or sent in from the host turns the block into
// something else. E2E DB02 lost the block and what was in it ("TitleZ" alone, hp job 649): Enter, typed while the
// keyboard was outside the page, came as TypedElsewhere "\n" - a paste of "\n\n" - and the summary's line was taken for a
// paragraph: checkInlineUpdate made its parent, the block itself, a paragraph of the summary's text.
//
//   node details-paste-check.js
const { openEditor } = require('./harness');

const DOC = '<details>\n<summary>Title</summary>\n\nHello\n\n</details>\n';

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  // The caret in the summary at an offset, the summary being edited (as a double click leaves it).
  const caretInSummary = offset => page.evaluate(offset => {
    const muya = window.__typedownMuya, cs = muya.contentState;
    const figure = cs.blocks.find(b => b.functionType === 'details');
    const summary = figure.children[0];
    cs.cursor = { start: { key: summary.key, offset }, end: { key: summary.key, offset } };
    cs.render();
    return summary.text;
  }, offset);
  const caretIn = () => page.evaluate(() => {
    const s = getSelection(); const e = s.anchorNode && (s.anchorNode.nodeType === 1 ? s.anchorNode : s.anchorNode.parentElement);
    const b = e && e.closest('.ag-details-summary, .ag-paragraph-content');
    return !b ? null : b.classList.contains('ag-details-summary') ? 'summary' : b.textContent.trim();
  });
  try {
    await mode(false, false);

    // Enter typed with the keyboard outside the page: the host sends it as text (TypedElsewhere) - into the block, as Enter.
    await load(DOC); await pause(300);
    await caretInSummary(5);
    await page.evaluate(() => window.__deliver('TypedElsewhere', { text: '\n' }));
    await pause(400);
    let md = await markdown();
    check(md === DOC, `Enter sent in from the host keeps the block and what is in it (${JSON.stringify(md)})`);
    check(await caretIn() === 'Hello', `and the caret goes into the block, as Enter does (${await caretIn()})`);

    // A paste with line breaks into the summary: one line, the breaks as spaces; the block and its content kept.
    await load(DOC); await pause(300);
    await caretInSummary(5);
    await page.evaluate(() => window.__typedownMuya.contentState.pasteHandler('normal', ' one\ntwo\n\nthree', ''));
    await pause(300);
    md = await markdown();
    check(md === '<details>\n<summary>Title one two three</summary>\n\nHello\n\n</details>\n', `a paste of lines into the summary is one line in it (${JSON.stringify(md)})`);

    // A paste of a line break alone: as Enter, into the block.
    await load(DOC); await pause(300);
    await caretInSummary(5);
    await page.evaluate(() => window.__typedownMuya.contentState.pasteHandler('normal', '\n\n', ''));
    await pause(300);
    md = await markdown();
    check(md === DOC, `a paste of a line break alone keeps the block (${JSON.stringify(md)})`);

    // Block syntax typed at the summary's start is the summary's text: "# ", "- ", "> ", "1. ".
    for (const marks of ['# ', '- ', '> ', '1. ']) {
      await load(DOC); await pause(300);
      await caretInSummary(0);
      await page.keyboard.type(marks); await pause(300);
      md = await markdown();
      check(md === `<details>\n<summary>${marks}Title</summary>\n\nHello\n\n</details>\n`, `"${marks}" typed at the summary's start stays in it (${JSON.stringify(md)})`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
