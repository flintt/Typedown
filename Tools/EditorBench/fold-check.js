// A long code block or table folds to its first lines by its button (services/blockFold), and stays folded when the
// document is opened again; a short one has no button; the document's text is not changed, and another document has
// its own folds.
// - a code block of 20 lines and a table of 16 rows each have the button, a code block of 3 lines none;
// - a click on the code block's button folds it (shorter on screen), the Markdown the same;
// - the document loaded again (the same path): still folded; the table not;
// - another document with the same code block: not folded;
// - a second click opens it again, and that too is remembered.
//
//   node fold-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const code = Array.from({ length: 20 }, (_, i) => `line ${i + 1}`).join('\n');
  const rows = Array.from({ length: 15 }, (_, i) => `| r${i} | v${i} |`).join('\n');
  const doc = `# Fold\n\n\`\`\`js\n${code}\n\`\`\`\n\n| Name | Value |\n| --- | --- |\n${rows}\n\n\`\`\`\nshort\ncode\nhere\n\`\`\`\n`;
  const open = async path => {
    await mode(true, false);
    await load(doc, path);
    await mode(false, false);
    await pause(600);
  };
  const state = () => page.evaluate(() => {
    const pres = [...document.querySelectorAll('#ag-editor-id pre.ag-fence-code, #ag-editor-id pre')].filter(p => p.querySelector('code'));
    const figure = document.querySelector('#ag-editor-id figure[data-role="TABLE"], #ag-editor-id figure');
    return {
      buttons: [...document.querySelectorAll('#ag-editor-id .ag-block-fold')].length,
      longFolded: !!pres[0]?.classList.contains('ag-folded'),
      longHeight: Math.round(pres[0]?.getBoundingClientRect().height || 0),
      shortButton: !!pres[pres.length - 1]?.querySelector(':scope > .ag-block-fold'),
      tableFolded: !!document.querySelector('#ag-editor-id figure.ag-folded'),
      figure: !!figure,
    };
  });
  const clickFold = () => page.evaluate(() => {
    const b = document.querySelector('#ag-editor-id pre .ag-block-fold');
    const r = b.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }).then(async p => { await page.mouse.move(p.x, p.y); await pause(200); await page.mouse.click(p.x, p.y); await pause(400); });
  const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());
  try {
    await page.setViewport({ width: 1200, height: 1200 });
    await open('C:/docs/a.md');
    let s = await state();
    report(s.buttons === 2 && !s.shortButton, 'the long code block and the long table have the button, the short block none', JSON.stringify(s));
    const before = await markdown();
    const tall = s.longHeight;
    await clickFold();
    s = await state();
    report(s.longFolded && s.longHeight < tall * 0.7, 'a click folds the code block (shorter on screen)', `${tall} -> ${JSON.stringify(s)}`);
    report(await markdown() === before, 'the Markdown is the same', 'changed');

    await open('C:/docs/a.md');
    s = await state();
    report(s.longFolded && !s.tableFolded, 'opened again: the code block still folded, the table not', JSON.stringify(s));

    await open('C:/docs/b.md');
    s = await state();
    report(!s.longFolded, 'another document with the same block: not folded', JSON.stringify(s));

    await open('C:/docs/a.md');
    await clickFold();
    await open('C:/docs/a.md');
    s = await state();
    report(!s.longFolded, 'opened with a second click, and that is remembered', JSON.stringify(s));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
