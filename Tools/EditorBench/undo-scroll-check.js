// Undo and redo keep the page where the reader has it, and a plain text file opened after a Markdown one takes its own
// caret (readers, 2026-11-03). An undo or a redo comes from the host as the whole text and its caret (SetMarkdown):
// - the caret in view before: the page does not move at all, and the caret is still in view - in the visual editor and in
//   the source editor alike (the page went to the top and the caret was put a third of the way down, or out of sight);
// - the caret out of view: the least scroll that brings it in (at the window's edge, not a third of the way down);
// - a .txt after a .md: the host says plain text first (the source editor comes up holding the .md), then sends the .txt
//   with its caret - the caret is the .txt's (it took the .md's line and column, and drifted at every switch).
//
//   node undo-scroll-check.js
const { openEditor } = require('./harness');

(async () => {
  const failures = [];
  let page, errors, mode, load, pause, close;
  // A fresh page for each part (an undo leaves the bench's page waiting for its host before a mode switch).
  const fresh = async () => { if (close) await close(); ({ page, errors, mode, load, pause, close } = await openEditor()); await page.setViewport({ width: 1100, height: 800 }); };
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const doc = Array.from({ length: 160 }, (_, i) => `Paragraph ${i}: ${'words '.repeat(12).trim()}.`).join('\n\n') + '\n';
  // Where the page (or the source editor's scroller) is, and where the caret is in the window.
  const state = () => page.evaluate(() => {
    const cm = document.querySelector('.CodeMirror')?.CodeMirror;
    if (cm) {
      const c = cm.cursorCoords(null, 'window');
      const box = cm.getScrollerElement().getBoundingClientRect();
      return { scroll: cm.getScrollInfo().top, caretTop: c.top, caretBottom: c.bottom, viewTop: box.top, viewBottom: box.bottom, line: cm.getCursor().line };
    }
    const sel = window.getSelection();
    const r = sel.rangeCount ? sel.getRangeAt(0).getBoundingClientRect() : null;
    const rect = r && r.height ? r : (sel.anchorNode ? (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement).getBoundingClientRect() : null);
    return { scroll: window.scrollY, caretTop: rect ? rect.top : null, caretBottom: rect ? rect.bottom : null, viewTop: 0, viewBottom: window.innerHeight };
  });
  const visible = s => s.caretTop !== null && s.caretTop >= s.viewTop - 1 && s.caretBottom <= s.viewBottom + 1;
  const cursorNow = () => page.evaluate(() => window.__last.CursorChange?.cursor);
  const setMarkdown = (text, cursor) => page.evaluate(({ text, cursor }) => window.__deliver('SetMarkdown', { text, cursor, basePath: '/tmp', loadId: 900 + Math.floor(Math.random() * 99) }), { text, cursor });
  try {
    for (const source of [true, false]) {
      const label = source ? 'source editor' : 'visual editor';
      await fresh();
      await mode(source, false);
      await load(doc);
      await pause(500);
      // The page moved down; a paragraph in the middle of the window clicked and changed.
      if (source) await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.scrollTo(null, 1800));
      else await page.evaluate(() => window.scrollTo(0, 2600));
      await pause(300);
      const at = await page.evaluate(source => {
        const y = window.innerHeight / 2;
        if (source) { const cm = document.querySelector('.CodeMirror').CodeMirror; const c = cm.charCoords({ line: cm.lineAtHeight(y, 'window'), ch: 4 }, 'window'); return { x: c.left + 2, y: (c.top + c.bottom) / 2 }; }
        const p = [...document.querySelectorAll('#ag-editor-id p')].find(e => { const r = e.getBoundingClientRect(); return r.top > y - 60 && r.top < y + 60; });
        const r = p.getBoundingClientRect(); return { x: r.left + 40, y: r.top + r.height / 2 };
      }, source);
      await page.mouse.click(at.x, at.y);
      await pause(300);
      const caretBefore = await cursorNow();
      await page.keyboard.type('XYZ');
      await pause(500);
      const before = await state();
      // Undo: the text before the typing, with the caret where it was.
      await setMarkdown(doc, caretBefore);
      await pause(800);
      const after = await state();
      report(Math.abs(after.scroll - before.scroll) < 2, `${label}: an undo with the caret in view does not move the page`, `${before.scroll} -> ${after.scroll}`);
      report(visible(after), `${label}: and the caret is still in view`, JSON.stringify(after));
      // The page moved away (the caret below the window): the least scroll brings the caret in, at the window's bottom edge.
      if (source) await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.scrollTo(null, 0));
      else await page.evaluate(() => window.scrollTo(0, 0));
      await pause(300);
      await setMarkdown(doc + '\nmore\n', caretBefore);
      await pause(800);
      const away = await state();
      report(visible(away), `${label}: an undo with the caret out of view brings it into view`, JSON.stringify(away));
      const height = away.viewBottom - away.viewTop;
      report(away.caretTop - away.viewTop > height * 0.6, `${label}: by the least scroll (the caret near the window's bottom edge, not a third of the way down)`, JSON.stringify(away));
    }

    // A .txt after a .md: plain text said first, then the .txt with its own caret (line 30).
    await fresh();
    await mode(false, false);
    await load(doc);
    await page.evaluate(() => window.scrollTo(0, 1200));
    await pause(300);
    const p = await page.evaluate(() => { const e = [...document.querySelectorAll('#ag-editor-id p')][45]; const r = e.getBoundingClientRect(); return { x: r.left + 40, y: r.top + r.height / 2 }; });
    await page.mouse.click(p.x, p.y);
    await pause(300);
    const txt = Array.from({ length: 80 }, (_, i) => `line ${i} of the plain text`).join('\n') + '\n';
    await page.evaluate(() => window.__deliver('SettingsChanged', { plainTextShown: true }));
    await pause(100);
    await page.evaluate(text => {
      delete window.__last.FileLoaded;
      window.__deliver('LoadFile', { text, loadId: 950, basePath: '/tmp', cursor: { anchor: { line: 30, ch: 5 }, focus: { line: 30, ch: 5 } } });
    }, txt);
    await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 950, { timeout: 30000 });
    await pause(500);
    const t = await state();
    report(t.line === 30, 'a .txt after a .md: its own caret (line 30), not the .md\'s', JSON.stringify(t));
    report(visible(t), 'and in view', JSON.stringify(t));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    if (close) await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
