// What the page tells the host about each edit (MarkdownChange's "edit": kind, moved, inserted), from which the host's
// undo history makes its steps (ContentHistory): letters typed are "insert" with the letters, Backspace "delete",
// Enter, a paste and a format "other", a caret moved first (an arrow, a click, a shortcut such as Ctrl+A) "moved" - in
// the visual editor and in source mode (the user, 2026-11-05, #39: steps by a pause or a line change alone merged a
// replaced selection, a format, a paste, a caret moved within the paragraph into the typing).
//
//   node undo-kinds-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const last = () => page.evaluate(() => window.__last.MarkdownChange ? { text: window.__last.MarkdownChange.text, edit: window.__last.MarkdownChange.edit } : null);
  // An action, then the report it caused (the page's reports are throttled: waited for, then a moment for the last).
  const act = async (what, action) => {
    const before = (await last())?.text;
    await action();
    try {
      await page.waitForFunction(t => window.__last.MarkdownChange && window.__last.MarkdownChange.text !== t, { timeout: 5000 }, before);
    } catch { return { what, edit: null }; }
    await pause(600);
    return { what, ...(await last()) };
  };
  const expect = (got, mode, kind, moved, inserted) => {
    const e = got.edit || {};
    const ok = e.kind === kind && (moved == null || !!e.moved === moved) && (inserted == null || (e.inserted || '').endsWith(inserted));
    report(ok, `${mode}: ${got.what} - ${kind}${moved ? ', moved' : ''}${inserted ? ` '${inserted}'` : ''}`, JSON.stringify(e));
  };
  try {
    await page.setViewport({ width: 1100, height: 800 });
    for (const source of [false, true]) {
      const name = source ? 'source' : 'visual';
      await mode(source, false);
      await load('# T\n\nfirst\n');
      await pause(400);
      // The caret at the end of the paragraph, as a click puts it (and Ctrl+End, which every case uses).
      const box = await page.evaluate(src => {
        const el = src ? [...document.querySelectorAll('.CodeMirror-line')].find(l => l.textContent.includes('first')) : [...document.querySelectorAll('#ag-editor-id p')].find(p => p.textContent.includes('first'));
        const r = el.getBoundingClientRect();
        return { x: r.left + 5, y: r.top + r.height / 2 };
      }, source);
      await page.mouse.click(box.x, box.y);
      await page.keyboard.down('Control'); await page.keyboard.press('End'); await page.keyboard.up('Control');
      await pause(300);
      // The first report after the click: its letters, after a caret put there - moved.
      expect(await act('letters typed after a click', () => page.keyboard.type(' abc', { delay: 40 })), name, 'insert', null, 'c');
      expect(await act('more letters typed on', () => page.keyboard.type('de', { delay: 40 })), name, 'insert', false, 'e');
      expect(await act('Backspace', () => page.keyboard.press('Backspace')), name, 'delete', false);
      expect(await act('a letter after Home', async () => { await page.keyboard.press('Home'); await page.keyboard.type('X'); }), name, 'insert', true, 'X');
      expect(await act('Enter', () => page.keyboard.press('Enter')), name, 'other');
      expect(await act("the host's Paste", () => page.evaluate(() => window.__deliver('Paste', { type: 'normal', text: 'pasted', html: '' }))), name, 'other');
      if (!source) {
        await page.keyboard.type('word', { delay: 30 });
        await pause(600);
        expect(await act("the host's Format (Ctrl+B) after typing", async () => {
          await page.keyboard.down('Shift');
          for (let i = 0; i < 4; i++) await page.keyboard.press('ArrowLeft');
          await page.keyboard.up('Shift');
          await page.evaluate(() => window.__deliver('Format', 'strong'));
        }), name, 'other');
      }
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
