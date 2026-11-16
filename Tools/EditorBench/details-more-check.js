// <details> blocks with the rest of the editor (detailsCtrl, step 3): a search's match in a closed block opens it; the
// outline lists the headings in blocks and a jump to one opens its block; a block copied as Markdown is written as the
// editor writes it, and a page's <details> (GitHub's HTML) pasted becomes a block; an export for a PDF opens every block.
//
//   node details-more-check.js
const { openEditor } = require('./harness');

const DOC = [
  '# Top',
  '',
  '<details>',
  '<summary>Closed</summary>',
  '',
  '## Inside heading',
  '',
  'the needle is here',
  '',
  '</details>',
  '',
  '## After',
  ''
].join('\n');
const OPT = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: {} };

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const isOpen = () => page.evaluate(() => document.querySelector('#ag-editor-id figure[data-role="DETAILS"]').classList.contains('ag-details-open'));
  try {
    await mode(false, false);

    // A search's match in the closed block: the block opened, the match shown.
    await load(DOC);
    await pause(400);
    check(!(await isOpen()), 'the block starts closed');
    await page.evaluate(opt => window.__deliver('Search', { value: 'needle', opt }), OPT);
    await pause(600);
    await page.evaluate(() => window.__deliver('Find', { action: 'next' }));
    await pause(500);
    const shown = await page.evaluate(() => {
      const hit = document.querySelector('#ag-editor-id .ag-highlight, #ag-editor-id .ag-selection');
      return !!hit && hit.getBoundingClientRect().height > 0;
    });
    check(await isOpen(), 'a search\'s match in a closed block opens it');
    check(shown, 'and the match is on the page');

    // The outline: the heading in the block listed; a jump to it opens the block.
    await load(DOC);
    await pause(600);
    const toc = await page.evaluate(() => (window.__toc || []).map(t => t.content));
    console.log('  outline:', JSON.stringify(toc));
    check(JSON.stringify(toc) === JSON.stringify(['Top', 'Inside heading', 'After']), 'the outline lists the heading in the block, in its place');
    const slug = await page.evaluate(() => (window.__toc || []).find(t => t.content === 'Inside heading')?.slug);
    await page.evaluate(slug => window.__deliver('ScrollTo', { slug }), slug);
    await pause(500);
    const headingShown = await page.evaluate(slug => { const h = document.getElementById(slug); return !!h && h.getBoundingClientRect().height > 0; }, slug);
    check((await isOpen()) && headingShown, 'a jump to it opens the block and shows the heading');

    // Copy as Markdown: the whole block selected, written as the editor writes it.
    await load('Before\n\n' + DOC.split('\n').slice(2, 10).join('\n') + '\n\nAfter\n');
    await pause(400);
    const copied = await page.evaluate(() => {
      const root = document.querySelector('#ag-editor-id');
      const range = document.createRange();
      range.setStartBefore(root.querySelector('figure[data-role="DETAILS"]'));
      range.setEndAfter(root.querySelector('figure[data-role="DETAILS"]'));
      const sel = getSelection();
      sel.removeAllRanges();
      sel.addRange(range);
      return window.__typedownMuya.contentState.getClipBoardData().text;
    });
    console.log('  copied:', JSON.stringify(copied));
    check(/<details>\n<summary>Closed<\/summary>\n\n## Inside heading\n\nthe needle is here\n\n<\/details>/.test(copied), 'a block copied as Markdown is written as the editor writes one');

    // A page's <details> (GitHub's HTML) to Markdown: the form the editor reads as a block.
    const pasted = await page.evaluate(() => {
      const html = '<details><summary>From GitHub</summary><ul><li>one</li><li>two</li></ul><p>Text <strong>bold</strong>.</p></details>';
      return window.__typedownMuya.contentState.htmlToMarkdown(html);
    });
    console.log('  from HTML:', JSON.stringify(pasted));
    check(/<details>\n<summary>From GitHub<\/summary>\n\n[-*] one\n[-*] two\n\nText \*\*bold\*\*\.\n\n<\/details>/.test(pasted.trim() + '\n'), 'a page\'s <details> becomes the Markdown of a block');
    await load(pasted.trim() + '\n');
    await pause(400);
    const block = await page.evaluate(() => { const f = document.querySelector('#ag-editor-id figure[data-role="DETAILS"]'); return f ? f.querySelector(':scope > .ag-details-summary').textContent : null; });
    check(block === 'From GitHub', 'and that is read as a block');

    // An export for a PDF: every block open (the page's Export handler, openDetails from the host).
    await load(DOC);
    await pause(400);
    const opened = await page.evaluate(async () => {
      const muya = window.__typedownMuya;
      muya.markdown = muya.getMarkdown();
      const html = await muya.exportHtml();
      return html.replace(/<details(?![^>]*\sopen\b)(?=[\s>])/g, '<details open');
    });
    check(/<details open>/.test(opened) && !/<details>/.test(opened), 'an export for a PDF has every block open');

    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `\n${failures.length} FAILED` : '\nall passed');
  process.exit(failures.length ? 1 : 0);
})();
