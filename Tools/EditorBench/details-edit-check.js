// Editing <details> blocks (detailsCtrl; Typedown issue #1, its author's proposal): a double click on the summary edits
// it (a single click opens or closes the block, step 1), Enter in the summary goes into the block, opened; the arrows pass
// over a closed block's content; Backspace at the summary's start takes the block apart; Paragraph > Details block wraps
// the paragraph and takes the block apart again; "<details>" typed and Enter makes one; closed with the caret in it, the
// caret goes to the summary.
//
//   node details-edit-check.js
const { openEditor } = require('./harness');

const ISSUE = '## Before\n\n<details>\n<summary>example111</summary>\n\n- abc\n\n> 123\n\n</details>\n\n## After\n';

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  const state = () => page.evaluate(() => {
    const f = document.querySelector('#ag-editor-id figure[data-role="DETAILS"]');
    const sel = getSelection();
    const at = sel.anchorNode && (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement);
    const block = at && at.closest('.ag-details-summary, .ag-paragraph-content, li, h2');
    return {
      found: !!f,
      open: !!f && f.classList.contains('ag-details-open'),
      editing: !!f && f.classList.contains('ag-details-editing'),
      summary: f ? f.querySelector(':scope > .ag-details-summary').textContent : null,
      caretIn: !block ? null : block.classList.contains('ag-details-summary') ? 'summary' : (block.textContent || '').trim().slice(0, 20),
    };
  });
  const summaryBox = () => page.evaluate(() => {
    const r = document.querySelector('#ag-editor-id figure[data-role="DETAILS"] > .ag-details-summary').getBoundingClientRect();
    return { x: r.left + 20, y: r.top + r.height / 2 };
  });
  try {
    await mode(false, false);

    // A double click on the summary edits it; the block neither opens nor closes for it.
    await load(ISSUE);
    await pause(400);
    let s = await summaryBox();
    await page.mouse.click(s.x, s.y, { clickCount: 1 });
    await page.mouse.click(s.x, s.y, { clickCount: 2 });
    await pause(500);
    let st = await state();
    console.log('  after a double click:', JSON.stringify(st));
    check(st.editing && st.caretIn === 'summary' && !st.open, 'a double click on the summary edits it, the block left closed');
    await page.keyboard.press('End');
    await page.keyboard.type('X');
    await pause(300);
    check((await state()).summary === 'example111X' && (await markdown()).includes('<summary>example111X</summary>'), 'what is typed goes into the summary');

    // Enter in the summary: into the block, opened.
    await page.keyboard.press('Enter');
    await pause(400);
    st = await state();
    console.log('  after Enter:', JSON.stringify(st));
    check(st.open && st.caretIn === 'abc' && st.summary === 'example111X', 'Enter in the summary opens the block, the caret at the start of what is in it');
    check(!(await markdown()).includes('example111X\n'), 'and makes no line break in the summary');

    // Closed with the caret in it: the caret to the summary.
    await page.click('figure[data-role="DETAILS"] > .ag-details-toggle');
    await pause(400);
    st = await state();
    check(!st.open && st.caretIn === 'summary', 'closed with the caret in it, the caret goes to the summary');

    // The arrows: down from the heading before onto the summary, down again past the closed block.
    await load(ISSUE);
    await pause(400);
    await page.click('#ag-editor-id > h2');
    await page.keyboard.press('End');
    await page.keyboard.press('ArrowDown');
    await pause(300);
    check((await state()).caretIn === 'summary', 'Down from the heading before: the caret on the summary');
    await page.keyboard.press('ArrowDown');
    await pause(300);
    st = await state();
    check(st.caretIn && st.caretIn.includes('After') && !st.open, 'Down again: past the closed block, to the heading after');
    await page.keyboard.press('ArrowUp');
    await pause(300);
    check((await state()).caretIn === 'summary', 'Up from the heading after: back on the summary, nothing in the block');

    // Backspace at the summary's start: the block taken apart, the summary a paragraph.
    await page.keyboard.press('Home');
    await page.keyboard.press('Backspace');
    await pause(400);
    const apart = await markdown();
    console.log('  taken apart:', JSON.stringify(apart));
    check(!apart.includes('<details') && apart.includes('example111') && apart.includes('- abc') && apart.includes('> 123'), 'Backspace at the summary\'s start takes the block apart, its summary and content kept');

    // Paragraph > Details block: the paragraph wrapped, the caret in the empty summary; again, taken apart.
    await load('Hello\n');
    await pause(300);
    await page.click('#ag-editor-id .ag-paragraph-content');
    await page.evaluate(() => window.__deliver('UpdateParagraph', 'details'));
    await pause(400);
    const wrapped = await markdown();
    st = await state();
    console.log('  wrapped:', JSON.stringify(wrapped), JSON.stringify(st));
    check(wrapped === '<details>\n<summary></summary>\n\nHello\n\n</details>\n' && st.caretIn === 'summary' && st.open, 'Paragraph > Details block wraps the paragraph, the caret in its empty summary');
    await page.keyboard.type('Title');
    await pause(300);
    check((await markdown()).includes('<summary>Title</summary>'), 'the summary typed');
    await page.evaluate(() => window.__deliver('UpdateParagraph', 'details'));
    await pause(400);
    const unwrapped = await markdown();
    console.log('  unwrapped:', JSON.stringify(unwrapped));
    check(!unwrapped.includes('<details') && unwrapped.includes('Title') && unwrapped.includes('Hello'), 'again in the block: taken apart');

    // "<details>" typed alone on a line, Enter: a block, the caret in its summary.
    await load('\n');
    await pause(300);
    await page.click('#ag-editor-id .ag-paragraph-content');
    await page.keyboard.type('<details>');
    await page.keyboard.press('Enter');
    await pause(500);
    st = await state();
    const typed = await markdown();
    console.log('  typed:', JSON.stringify(typed), JSON.stringify(st));
    check(st.found && st.caretIn === 'summary' && typed.startsWith('<details>\n<summary></summary>\n'), '"<details>" and Enter make a block, the caret in its summary');

    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `\n${failures.length} FAILED` : '\nall passed');
  process.exit(failures.length ? 1 : 0);
})();
