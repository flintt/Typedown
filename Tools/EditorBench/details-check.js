// <details> blocks (Settings > Editor > Details blocks, on unless turned off; Typedown issue #1): a <details> with its
// summary and the Markdown in it is one block - its summary a line of its own, the blocks in it shown while it is open -
// opened and closed by its toggle or a click on its summary, the document not changed by that; written back exactly as
// it was read, in every form taken; the exports render it as a <details> with the Markdown in it rendered. Turned off: as
// CommonMark reads it, an HTML block and the blocks after it, the text unchanged.
//
//   node details-check.js
const { openEditor } = require('./harness');

const ISSUE = [
  '## 参考点1',
  '',
  '<details>',
  '<summary>example111</summary>',
  '',
  '- abc',
  '',
  '> 123',
  '',
  '</details>',
  '',
  '## 参考点2',
  ''
].join('\n');

// Each form read and written back as it is.
const FORMS = {
  'the issue\'s example': ISSUE,
  'open': '<details open>\n<summary>Shown</summary>\n\nText **bold**.\n\n</details>\n',
  'summary on the details line': '<details><summary>Inline</summary>\n\n1. one\n2. two\n\n</details>\n',
  'no summary': '<details>\n\nNo summary here.\n\n</details>\n',
  'no blank lines': '<details>\n<summary>Tight</summary>\nA paragraph.\n</details>\n',
  'empty': '<details>\n<summary>Empty</summary>\n\n</details>\n',
  'nested, with code': '<details>\n<summary>Out</summary>\n\n<details>\n<summary>In</summary>\n\ninner\n\n</details>\n\n```js\nconst x = 1\n```\n\n</details>\n',
  'with other attributes (an HTML block)': '<details class="note">\n<summary>Kept</summary>\n\nAfter.\n\n</details>\n',
};

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  const look = () => page.evaluate(() => {
    const root = document.querySelector('#ag-editor-id');
    return [...root.querySelectorAll('figure[data-role="DETAILS"]')].map(f => ({
      summary: f.querySelector(':scope > .ag-details-summary')?.textContent ?? null,
      open: f.classList.contains('ag-details-open'),
      toggle: f.querySelector(':scope > .ag-details-toggle')?.textContent ?? null,
      // What is shown of it: its blocks after the summary, as laid out.
      // (Its front icon, the editor's for every top-level block, is none of its blocks.)
      shown: [...f.children].filter(c => !c.matches('.ag-details-toggle, .ag-details-summary, .ag-front-icon') && getComputedStyle(c).display !== 'none').length,
      inside: [...f.children].filter(c => !c.matches('.ag-details-toggle, .ag-details-summary, .ag-front-icon')).map(c => c.tagName.toLowerCase()),
    }));
  });
  const setting = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { detailsBlocks: on }), on);
    await pause(500);
  };
  try {
    await mode(false, false);

    // Each form written back as it was read.
    for (const [name, text] of Object.entries(FORMS)) {
      await load(text);
      await pause(300);
      const back = await markdown();
      check(back === text, `${name}: written back as it was read${back === text ? '' : ` - got ${JSON.stringify(back)}`}`);
    }

    // The issue's example: one block, closed, its summary shown, a list and a quotation in it.
    await load(ISSUE);
    await pause(400);
    let blocks = await look();
    console.log('  the issue\'s example:', JSON.stringify(blocks));
    check(blocks.length === 1 && blocks[0].summary === 'example111', 'the issue\'s example is one block, its summary "example111"');
    check(blocks[0] && JSON.stringify(blocks[0].inside) === JSON.stringify(['ul', 'blockquote']), 'with the list and the quotation in it');
    check(blocks[0] && !blocks[0].open && blocks[0].shown === 0 && blocks[0].toggle === '▸', 'closed as the file has it: nothing in it shown, the toggle pointing right');
    const headings = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id > h2')].length);
    check(headings === 2, 'the headings before and after it stay outside it');

    // Opened by its toggle, closed by its summary; the document not changed.
    await page.evaluate(() => { delete window.__last.MarkdownChange; });
    await page.click('figure[data-role="DETAILS"] > .ag-details-toggle');
    await pause(300);
    blocks = await look();
    check(blocks[0].open && blocks[0].shown === 2 && blocks[0].toggle === '▾', 'a click on the toggle opens it: the list and the quotation shown');
    await page.click('figure[data-role="DETAILS"] > .ag-details-summary');
    await pause(300);
    blocks = await look();
    check(!blocks[0].open && blocks[0].shown === 0, 'a click on its summary closes it');
    const changed = await page.evaluate(() => !!window.__last.MarkdownChange);
    check(!changed && (await markdown()) === ISSUE, 'opening and closing it changes nothing in the document');

    // <details open>: shown open.
    await load(FORMS.open);
    await pause(300);
    blocks = await look();
    console.log('  <details open>:', JSON.stringify(blocks));
    check(blocks.length === 1 && blocks[0].open && blocks[0].shown === 1, '<details open> is shown open');

    // Other attributes: as before, an HTML block.
    await load(FORMS['with other attributes (an HTML block)']);
    await pause(300);
    check((await look()).length === 0, '<details class="note"> is left an HTML block');

    // The exports: a <details> with the Markdown in it rendered.
    await load(ISSUE);
    await pause(300);
    const exported = await page.evaluate(async () => {
      const muya = window.__typedownMuya;
      muya.markdown = muya.getMarkdown();
      const html = await muya.exportHtml();
      const holder = document.createElement('div');
      holder.innerHTML = html;
      const d = holder.querySelector('details');
      return d ? { summary: d.querySelector('summary')?.textContent, list: !!d.querySelector('ul li'), quote: !!d.querySelector('blockquote') } : null;
    });
    console.log('  exported:', JSON.stringify(exported));
    check(exported && exported.summary === 'example111' && exported.list && exported.quote, 'the HTML export has a <details> with its summary, the list and the quotation in it');

    // Turned off: as CommonMark reads it, the text unchanged; on again: one block.
    await setting(false);
    blocks = await look();
    const html = await page.evaluate(() => document.querySelectorAll('#ag-editor-id figure[data-role="HTML"]').length);
    check(blocks.length === 0 && html === 2, 'turned off: no block of its own, two HTML blocks as before');
    check((await markdown()) === ISSUE, 'and the text unchanged');
    await setting(true);
    blocks = await look();
    check(blocks.length === 1 && (await markdown()) === ISSUE, 'on again: one block, the text unchanged');

    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `\n${failures.length} FAILED` : '\nall passed');
  process.exit(failures.length ? 1 : 0);
})();
