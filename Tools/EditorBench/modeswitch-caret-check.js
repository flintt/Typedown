// A switch between the visual editor and source mode keeps the caret where the reader was editing: on the same text, and
// at the same height in the window (a reader, 2026-11-05: in source mode the caret was not where they had been editing).
// For a caret in a paragraph in the middle of a long document, in a heading, a list item, a table cell, bold text, a
// code block and near the end: switched to source mode, the caret is on the same line of the Markdown and that line is
// within a few pixels of where the caret was on screen; switched back, the same in the visual editor.
//
//   node modeswitch-caret-check.js
const { openEditor } = require('./harness');

const filler = n => Array.from({ length: n }, (_, i) => `Paragraph ${i} of the filler, long enough to wrap once or twice in the editor at this width so that the two editors' line heights differ as they do for a reader.`).join('\n\n');
const doc = [
  '# Title', '', filler(40), '',
  '## Middle heading marker', '', 'A middle paragraph with the caret marker in it.', '',
  '- first item', '- list item marker here', '- third item', '',
  '| Name | Value |', '| --- | --- |', '| a | table cell marker |', '',
  'Text with **bold marker inside** it.', '',
  '```js', 'const code = "code block marker";', '```', '',
  filler(60), '', 'The last paragraph with the end marker.', '',
].join('\n');

const cases = [
  { name: 'a paragraph in the middle', word: 'caret marker' },
  { name: 'a heading', word: 'Middle heading marker' },
  { name: 'a list item', word: 'list item marker' },
  { name: 'a table cell', word: 'table cell marker' },
  { name: 'bold text', word: 'bold marker' },
  { name: 'a code block', word: 'code block marker' },
  { name: 'near the end of a long document', word: 'end marker' },
];

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const lineOf = word => doc.split('\n').findIndex(l => l.includes(word));
  try {
    await page.setViewport({ width: 1200, height: 800 });
    await mode(false, false);
    await load(doc);
    await pause(600);
    for (const c of cases) {
      // Visual: the caret put inside the word, the word scrolled to about 60% down the window.
      const before = await page.evaluate(word => {
        const root = document.getElementById('ag-editor-id');
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let node; while ((node = walker.nextNode()) && !node.textContent.includes(word));
        if (!node) return null;
        const at = node.textContent.indexOf(word) + 3;
        const range = document.createRange(); range.setStart(node, at); range.collapse(true);
        const box = range.getBoundingClientRect();
        window.scrollBy(0, box.top - window.innerHeight * 0.6);
        const el = node.parentElement.closest('[id]') || node.parentElement;
        const r = range.getBoundingClientRect();
        // A click where the caret goes, as a reader puts it there.
        return { x: r.left + 1, y: r.top + r.height / 2 };
      }, c.word);
      if (!before) { report(false, `${c.name}: found in the visual editor`, 'no such text'); continue; }
      await page.mouse.click(before.x, before.y);
      await pause(300);
      const visual = await page.evaluate(() => { const s = window.getSelection(); if (!s.rangeCount) return null; const r = s.getRangeAt(0).getBoundingClientRect(); return { top: r.top, text: (s.anchorNode.textContent || '').slice(0, 60) }; });
      await mode(true, false);
      await pause(500);
      const source = await page.evaluate(() => {
        const cm = document.querySelector('.CodeMirror').CodeMirror;
        const pos = cm.getCursor();
        const info = cm.getScrollInfo();
        return { line: pos.line, ch: pos.ch, top: cm.cursorCoords(pos, 'window').top, text: cm.getLine(pos.line),
          atTop: info.top <= 1, atEnd: info.top >= info.height - info.clientHeight - 1 };
      });
      const wanted = lineOf(c.word);
      report(source.line === wanted, `${c.name}: source mode's caret on the same line`, `line ${source.line} ('${source.text.slice(0, 40)}'), wanted ${wanted}`);
      // As near as the document lets it: scrolled to its very end (or top), the caret may only be lower (or higher).
      const near = visual && (Math.abs(source.top - visual.top) <= 6 || (source.atEnd && source.top > visual.top) || (source.atTop && source.top < visual.top));
      report(near, `${c.name}: and at the same height in the window`, `visual ${visual && visual.top.toFixed(0)}, source ${source.top.toFixed(0)}${source.atEnd ? ' (at the end)' : ''}`);
      // Back: the visual editor's caret on the same text, at the source caret's height.
      await mode(false, false);
      await pause(600);
      const back = await page.evaluate(() => { const s = window.getSelection(); if (!s.rangeCount) return null; const r = s.getRangeAt(0).getBoundingClientRect(); return { top: r.top, text: (s.anchorNode && s.anchorNode.textContent || '').slice(0, 80) }; });
      report(back && back.text.includes(c.word.split(' ')[0]), `${c.name}: back in the visual editor, the caret in the same text`, back ? `'${back.text}'` : 'no caret');
      report(back && Math.abs(back.top - source.top) <= 6, `${c.name}: and at the same height`, `source ${source.top.toFixed(0)}, visual ${back && back.top.toFixed(0)}`);
    }
    // Moved in source mode: the caret put on another line there, scrolled to near the top, and back in the visual editor
    // it is in that text, at that height (not where the visual editor was left).
    await mode(true, false);
    await pause(500);
    const moved = await page.evaluate(line => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      cm.focus();
      cm.setCursor({ line, ch: 5 });
      const y = cm.charCoords({ line, ch: 5 }, 'local').top;
      cm.scrollTo(null, y - 300 + cm.getScrollerElement().getBoundingClientRect().top);
      return new Promise(r => requestAnimationFrame(() => r({ top: cm.cursorCoords(null, 'window').top, text: cm.getLine(line) })));
    }, lineOf('Paragraph 20 of'));
    await mode(false, false);
    await pause(600);
    const after = await page.evaluate(() => { const s = window.getSelection(); if (!s.rangeCount) return null; const r = s.getRangeAt(0).getBoundingClientRect(); return { top: r.top, text: (s.anchorNode && s.anchorNode.textContent || '').slice(0, 80) }; });
    report(after && after.text.includes('Paragraph 20 of'), 'moved in source mode: back in the visual editor, the caret in that text', after ? `'${after.text}'` : 'no caret');
    report(after && Math.abs(after.top - moved.top) <= 6, 'moved in source mode: and at that height', `source ${moved.top.toFixed(0)}, visual ${after && after.top.toFixed(0)}`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
