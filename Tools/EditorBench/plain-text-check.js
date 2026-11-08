// A plain text file (.txt, .text, .log; Settings.PlainTextShown): nothing in it parsed as Markdown (a reader's request,
// 2026-11-02). In visual mode laid out as prose (PlainTextProse: no line numbers, the editor's width), in source mode as
// code (line numbers); no "#", "**" or "*" highlighted in either, and the text the host gets back is the file's, as it is.
//
//   node plain-text-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, load, flush, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const text = '# not a heading\n\n**not bold** and *not italic*   \n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- not\n- a list\n\n\n\nlast line';
  const state = () => page.evaluate(() => ({
    codeMirror: !!document.querySelector('.CodeMirror'),
    muya: !!document.querySelector('#ag-editor-id'),
    prose: !!document.querySelector('.plain-prose'),
    lineNumbers: !!document.querySelector('.CodeMirror-linenumber'),
    highlighted: document.querySelectorAll('.CodeMirror .cm-header, .CodeMirror .cm-strong, .CodeMirror .cm-em, .CodeMirror .cm-variable-2').length,
  }));
  try {
    // As the host sends it for a .txt with the reader in visual mode: SourceCode reads true, the prose flag on.
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: true, plainTextShown: true, plainTextProse: true }));
    await pause(400);
    await load(text);
    await pause(600);
    const prose = await state();
    report(prose.codeMirror && !prose.muya && prose.prose && !prose.lineNumbers, 'visual mode: prose, no line numbers, not the Markdown editor', JSON.stringify(prose));
    report(prose.highlighted === 0, 'visual mode: no Markdown highlighted', `${prose.highlighted} marks`);
    report(await flush() === text, 'visual mode: the host gets the text as it is', JSON.stringify(await flush()));

    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: false }));
    await pause(500);
    const code = await state();
    report(code.codeMirror && !code.prose && code.lineNumbers, 'source mode: code, with line numbers', JSON.stringify(code));
    report(code.highlighted === 0, 'source mode: no Markdown highlighted', `${code.highlighted} marks`);
    report(await flush() === text, 'source mode: the host gets the text as it is', JSON.stringify(await flush()));

    // Even told visual mode (a SourceCode false arriving late), a plain text file is not handed to the Markdown editor.
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextProse: true }));
    await pause(500);
    const late = await state();
    report(late.codeMirror && !late.muya, 'a late SourceCode false does not bring the Markdown editor', JSON.stringify(late));

    // A Markdown file again: the Markdown editor, highlighted as before.
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await pause(500);
    await load('# A heading\n\n**bold**\n');
    await pause(500);
    const markdown = await state();
    report(markdown.muya && !markdown.codeMirror, 'a Markdown file: the Markdown editor again', JSON.stringify(markdown));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
