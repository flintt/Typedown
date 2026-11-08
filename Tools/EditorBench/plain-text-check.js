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

    // Markdown blocks (md and markdown fences, CommonMark's rules) drawn in the prose view, the rest plain.
    const withBlocks = '# plain line\n\n````md\n# Block title\n\n**bold** text\n\n```js\nconst x = 1\n```\n````\n\nplain **again**\n\n~~~markdown\n- one\n- two\n~~~\n';
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: true, plainTextShown: true, plainTextProse: true }));
    await pause(300);
    await load(withBlocks);
    await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.focus(); cm.setCursor({ line: 0, ch: 0 }); });
    await pause(500);
    const drawn = () => page.evaluate(() => [...document.querySelectorAll('.plain-md-block')].map(b => ({ h1: b.querySelectorAll('h1').length, strong: b.querySelectorAll('strong').length, pre: b.querySelectorAll('pre').length, li: b.querySelectorAll('li').length })));
    const blocks = await drawn();
    report(blocks.length === 2 && blocks[0].h1 === 1 && blocks[0].strong === 1 && blocks[0].pre === 1 && blocks[1].li === 2,
      'prose view: both Markdown blocks drawn (a heading, bold, a code block inside the longer fence; a list)', JSON.stringify(blocks));
    report(await page.evaluate(() => document.querySelectorAll('.CodeMirror .cm-header, .CodeMirror .cm-strong').length) === 0, 'prose view: the text outside the blocks is still plain', '');
    report(await flush() === withBlocks, 'prose view: the host gets the text as it is, blocks and all', JSON.stringify(await flush()));
    await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.setCursor({ line: 5, ch: 2 }));
    await pause(400);
    const editing = await drawn();
    report(editing.length === 1 && editing[0].li === 2, 'the caret in a block shows its lines, the other block still drawn', JSON.stringify(editing));
    await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.setCursor({ line: 0, ch: 0 }));
    await pause(400);
    report((await drawn()).length === 2, 'the caret out of it: drawn again', JSON.stringify(await drawn()));
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: false }));
    await pause(400);
    report((await drawn()).length === 0, 'source mode: no block drawn, the lines as they are', JSON.stringify(await drawn()));

    // A picture in a block goes through the editor's own loading (relative paths resolved against the file's folder; a
    // data address here, the folder in E2E PT01).
    const pixel = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: true }));
    await load(`plain\n\n\`\`\`md\n![dot](${pixel})\n\`\`\`\n`);
    await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.focus(); cm.setCursor({ line: 0, ch: 0 }); });
    await pause(800);
    const picture = await page.evaluate(() => { const img = document.querySelector('.plain-md-block img'); return img ? { src: (img.getAttribute('src') || '').slice(0, 22), loaded: img.complete && img.naturalWidth > 0 } : null; });
    report(picture && picture.loaded, 'a picture in a block is loaded and shown', JSON.stringify(picture));

    // A Mermaid diagram in a block is drawn (a reader's report: it showed as its code).
    await load('plain\n\n````md\n# With a diagram\n\n```mermaid\ngraph TD\n  A-->B\n```\n````\n');
    await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.focus(); cm.setCursor({ line: 0, ch: 0 }); });
    let diagram = null;
    for (let i = 0; i < 40 && !diagram; i++) {
      await pause(250);
      diagram = await page.evaluate(() => { const d = document.querySelector('.plain-md-block .plain-md-diagram svg'); return d ? d.getBoundingClientRect().height : null; });
    }
    report(diagram > 20, 'a Mermaid diagram in a block is drawn', `svg height ${diagram}`);
    report(await flush() === 'plain\n\n````md\n# With a diagram\n\n```mermaid\ngraph TD\n  A-->B\n```\n````\n', 'the text with the diagram is the file\'s, as it is', '');

    // A long plain text file with blocks: a caret move costs little (it read the whole file on each move).
    const long = Array.from({ length: 20000 }, (_, i) => i % 2000 === 5 ? '```md\n# Block ' + i + '\n```' : `line ${i} of a long log`).join('\n');
    await load(long);
    await pause(800);
    const moves = () => page.evaluate(async () => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      const t0 = performance.now();
      for (let i = 0; i < 200; i++) cm.setCursor({ line: 1000 + i * 37, ch: 0 });
      return (performance.now() - t0) / 200;
    });
    const drawnCost = await moves();
    // The same moves in the source view (no blocks drawn): what CodeMirror itself takes.
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: false }));
    await pause(500);
    const bare = await moves();
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: true }));
    await pause(500);
    report(drawnCost - bare < 1, `a long file with blocks: a caret move costs little more than without them (${drawnCost.toFixed(2)} ms against ${bare.toFixed(2)} ms)`, `${drawnCost} / ${bare}`);

    // With Vim on (as the reader has it): not in the prose view - which is visual mode's, and with Vim looked like source
    // mode and took Ctrl+/ - but in the source view; switched back, off again.
    const vimState = () => page.evaluate(() => ({ keyMap: document.querySelector('.CodeMirror').CodeMirror.getOption('keyMap'), badge: document.querySelector('.vim-mode-badge')?.textContent || null }));
    await page.evaluate(() => window.__deliver('SettingsChanged', { vimMode: true, plainTextProse: true }));
    await pause(400);
    const proseVim = await vimState();
    report(proseVim.keyMap === 'default' && !proseVim.badge, 'Vim on, prose view: no Vim keys, no Vim badge', JSON.stringify(proseVim));
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: false }));
    await pause(400);
    const sourceVim = await vimState();
    report(sourceVim.keyMap === 'vim' && /NORMAL/.test(sourceVim.badge || ''), 'Vim on, source view: Vim keys and its badge', JSON.stringify(sourceVim));
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextProse: true }));
    await pause(400);
    const backVim = await vimState();
    report(backVim.keyMap === 'default' && !backVim.badge, 'back to the prose view: Vim off again', JSON.stringify(backVim));
    await page.evaluate(() => window.__deliver('SettingsChanged', { vimMode: false }));
    await pause(200);

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
