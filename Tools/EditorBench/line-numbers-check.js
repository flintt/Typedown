// Settings > Editor > Line numbers in code blocks: a number beside each line of a fenced or indented code block - level
// with its line, as many as its lines after Enter and Backspace there - and none for a diagram's or maths' source; the
// lines do not wrap (a long one scrolls across); the numbers are not text (not in the file, not in a copied selection);
// off, there are none. MarkText had them and turned them off (#1648): numbers measured against wrapped lines went wrong.
//
//   node line-numbers-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    const long = 'const veryLongName = ' + '"abcdefghij", '.repeat(30) + 'end;';
    const doc = '```js\nconst a = 1;\nconst b = 2;\n' + long + '\n```\n\n    indented one\n    indented two\n\n```mermaid\ngraph TD; A-->B\n```\n\n$$\nx^2\n$$\n';
    const setting = async on => { await page.evaluate(on => window.__deliver('SettingsChanged', { codeBlockLineNumbers: on }), on); await pause(600); };
    const open = async () => { await mode(true, false); await load(doc); await mode(false, false); await pause(600); };
    // Each code block's number rows and, for each line of its text, how far the row's top is from the line's.
    const blocks = () => page.evaluate(() => [...document.querySelectorAll('#ag-editor-id pre')].map(pre => {
      const rows = pre.querySelector(':scope > code > .line-numbers-rows');
      const content = pre.querySelector('.ag-code-content');
      const text = content ? content.textContent : '';
      const lineTops = [];
      if (content) {
        // The top of each line's first character, from the text's own boxes.
        const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
        let at = 0, wantLine = 0;
        const starts = [0];
        for (let i = 0; i < text.length; i++) if (text[i] === '\n' && i + 1 < text.length) starts.push(i + 1);
        let node;
        while ((node = walker.nextNode()) && wantLine < starts.length) {
          while (wantLine < starts.length && starts[wantLine] < at + node.length) {
            const r = document.createRange();
            r.setStart(node, starts[wantLine] - at); r.setEnd(node, Math.min(starts[wantLine] - at + 1, node.length));
            lineTops.push(r.getBoundingClientRect().top);
            wantLine++;
          }
          at += node.length;
        }
      }
      return {
        cls: pre.className,
        rows: rows ? [...rows.children].map(s => s.getBoundingClientRect().top) : null,
        lineTops,
        lineHeight: content ? parseFloat(getComputedStyle(content).lineHeight) : 0,
        height: content ? content.getBoundingClientRect().height : 0,
        scrolls: content ? content.scrollWidth > content.clientWidth : false,
        rowsText: rows ? rows.textContent : '',
      };
    }));
    const caretAtEndOf = async starts => page.evaluate(starts => {
      const state = window.__typedownMuya.contentState;
      const find = bs => { for (const b of bs) { if (b.type === 'span' && b.functionType === 'codeContent' && b.text.startsWith(starts)) return b; const f = b.children && find(b.children); if (f) return f; } };
      const b = find(state.blocks);
      const p = { key: b.key, offset: b.text.indexOf('\n') };
      state.cursor = { start: p, end: p, anchor: p, focus: p }; state.render();
    }, starts);

    await setting(false);
    await open();
    check((await blocks()).every(b => b.rows === null && !/line-numbers/.test(b.cls)), 'off: no line numbers');

    await setting(true);
    let all = await blocks();
    const fence = all.find(b => /ag-fence-code/.test(b.cls) && !/mermaid/.test(b.cls));
    const indent = all.find(b => /ag-indent-code/.test(b.cls));
    check(fence && fence.rows && fence.rows.length === 3, `on: the fenced block has a number for each of its 3 lines (${fence && fence.rows && fence.rows.length})`);
    check(indent && indent.rows && indent.rows.length === 2, `on: the indented block has 2 (${indent && indent.rows && indent.rows.length})`);
    for (const [what, b] of [['fenced', fence], ['indented', indent]]) {
      if (!b || !b.rows) continue;
      const off = b.rows.map((t, i) => Math.abs(t - b.lineTops[i]));
      check(off.every(d => d < b.lineHeight / 2), `on: each ${what} number level with its line (off by ${off.map(d => d.toFixed(1)).join(', ')} px)`);
    }
    check(fence && fence.scrolls && Math.abs(fence.height - 3 * fence.lineHeight) < fence.lineHeight / 2, `on: the long line does not wrap, it scrolls (block ${fence && fence.height.toFixed(0)} px for 3 lines of ${fence && fence.lineHeight} px)`);
    check(all.filter(b => /mermaid|multiple-math/.test(b.cls)).every(b => b.rows === null), 'on: no numbers for a diagram\'s or maths\' source');
    check(all.every(b => !/\d/.test(b.rowsText)), 'on: the numbers are not text');

    // A copy of a selection over the whole code block - the editor's own copy (contentState/copyCutCtrl.js), asked for as
    // the host asks (Copy) and answered with SetClipboard - is the same with the numbers as without.
    await page.evaluate(() => {
      window.__clip = [];
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => { const m = JSON.parse(raw); if (m.name === 'SetClipboard') window.__clip.push(m.args); return post(raw); };
    });
    const copyBlock = async () => {
      await page.evaluate(() => {
        window.__clip = [];
        const r = document.createRange(); r.selectNodeContents(document.querySelector('#ag-editor-id pre.ag-fence-code'));
        const s = window.getSelection(); s.removeAllRanges(); s.addRange(r);
      });
      await pause(400); // the editor takes the selection on selectionchange
      await page.evaluate(() => window.__deliver('Copy', { type: 'normal', copyInfo: null }));
      await pause(300);
      return page.evaluate(() => JSON.stringify(window.__clip));
    };
    const copiedOn = await copyBlock();
    await setting(false);
    const copiedOff = await copyBlock();
    check(copiedOn === copiedOff && !copiedOn.includes('line-numbers'), `on: a copy of the block is what it is without the numbers (${copiedOn.slice(0, 90)})`);
    await setting(true);

    // Enter in the block: a number more; Backspace at the start of the new line: one fewer.
    await caretAtEndOf('const a');
    const fenceRows = async () => ((await blocks()).find(b => /ag-fence-code/.test(b.cls) && !/mermaid/.test(b.cls)).rows || []).length;
    await page.keyboard.press('Enter'); await pause(300);
    let n = await fenceRows();
    check(n === 4, `on: Enter in the block, a number more (${n})`);
    await page.keyboard.press('Backspace'); await pause(300);
    n = await fenceRows();
    check(n === 3, `on: Backspace joining the line back, one fewer (${n})`);
    check(await flush() === doc, 'on: the document is as it was (the numbers are not in it)');

    await setting(false);
    check((await blocks()).every(b => b.rows === null && !/line-numbers/.test(b.cls)), 'off again: the numbers are gone');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
