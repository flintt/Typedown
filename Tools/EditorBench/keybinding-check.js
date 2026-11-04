// The keys the page acts on itself that Settings > Shortcuts can change (SettingsViewModel.KeyBindings, sent with
// GetSettings and SettingsChanged): each works on the key it is given, and the key it had before does nothing more -
// indent and outdent, the line break in a paragraph and in a table cell, the document's start and end, select all in
// reading mode, and undo and redo in the source editor (they ask the host, HistoryRequested). Without bindings from the
// host the keys are the ones they always were.
//
//   node keybinding-check.js
const { firstDifference, openEditor } = require('./harness');

const F6 = 117, F7 = 118, F8 = 119, F9 = 120;

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (what, ok, detail = '') => {
    console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`);
    if (!ok) failures.push(what);
  };
  const bind = async keyBindings => {
    await page.evaluate(k => window.__deliver('SettingsChanged', { keyBindings: k }), keyBindings);
    await pause(200);
  };
  // The caret in the text block whose text starts with `starts`, at `offset`.
  const caretAt = async (starts, offset) => {
    const ok = await page.evaluate((starts, offset) => {
      const state = window.__typedownMuya.contentState;
      const find = blocks => {
        for (const b of blocks) {
          if (b.type === 'span' && b.text.startsWith(starts)) return b;
          const f = b.children && find(b.children);
          if (f) return f;
        }
      };
      const block = find(state.blocks);
      if (!block) return false;
      const point = { key: block.key, offset };
      state.cursor = { start: point, end: point, anchor: point, focus: point };
      state.render();
      return true;
    }, starts, offset);
    if (!ok) throw new Error(`no block starting ${JSON.stringify(starts)}`);
    await pause(200);
  };
  const press = async (key, modifiers = []) => {
    for (const m of modifiers) await page.keyboard.down(m);
    await page.keyboard.press(key);
    for (const m of modifiers.slice().reverse()) await page.keyboard.up(m);
    await pause(200);
  };
  // The document after a key in the visual editor, and `typed` after it.
  const after = async (md, starts, offset, key, modifiers = [], typed = '') => {
    await mode(true, false);
    await load(md);
    await mode(false, false);
    await caretAt(starts, offset);
    await press(key, modifiers);
    if (typed) await page.keyboard.type(typed);
    await pause();
    return flush();
  };
  const same = (what, expected, actual) => check(what, actual === expected, firstDifference(expected, actual));
  try {
    const list = '- one\n- two\n';
    const para = 'Para one\n\nPara two\n';
    const table = '| a | b |\n| --- | --- |\n| cell | x |\n';
    // What the keys did before they could be changed: what they must do with the host's bindings left out, and what
    // the keys given to them must do.
    const indented = await after(list, 'two', 1, 'Tab');
    same('Tab indents (no bindings from the host)', '- one\n  - two\n', indented);
    const outdented = await after('- one\n    - two\n', 'two', 1, 'Tab', ['Shift']);
    same('Shift+Tab outdents (no bindings from the host)', '- one\n- two\n', outdented);
    const softBreak = await after(para, 'Para one', 4, 'Enter', ['Shift'], 'X');
    const enter = await after(para, 'Para one', 4, 'Enter', [], 'X');
    const cellBreak = await after(table, 'cell', 2, 'Enter', ['Alt'], 'X');
    const toEnd = await after(para, 'Para one', 2, 'End', ['Control'], 'X');
    same('Ctrl+End goes to the document end (no bindings from the host)', 'Para one\n\nPara twoX\n', toEnd);
    check('Shift+Enter is not a new paragraph', softBreak !== enter, JSON.stringify(softBreak));

    await bind({
      indent: { keyCode: F8, ctrl: false, alt: false, shift: false },
      outdent: { keyCode: F8, ctrl: false, alt: false, shift: true },
      softLineBreak: { keyCode: F9, ctrl: false, alt: false, shift: false },
      cellLineBreak: { keyCode: F9, ctrl: true, alt: false, shift: false },
      documentStart: { keyCode: F7, ctrl: false, alt: false, shift: true },
      documentEnd: { keyCode: F7, ctrl: false, alt: false, shift: false },
      selectAll: { keyCode: F6, ctrl: false, alt: false, shift: false },
      undo: { keyCode: F9, ctrl: true, alt: false, shift: true },
      redo: { keyCode: F8, ctrl: true, alt: false, shift: false },
    });
    same('F8 given to indent indents', indented, await after(list, 'two', 1, 'F8'));
    same('Tab given away does nothing', list, await after(list, 'two', 1, 'Tab'));
    same('Shift+F8 given to outdent outdents', outdented, await after('- one\n    - two\n', 'two', 1, 'F8', ['Shift']));
    same('Shift+Tab given away does nothing', '- one\n    - two\n', await after('- one\n    - two\n', 'two', 1, 'Tab', ['Shift']));
    same('F9 given to the line break in a paragraph breaks the line', softBreak, await after(para, 'Para one', 4, 'F9', [], 'X'));
    same('Shift+Enter given away is an Enter', enter, await after(para, 'Para one', 4, 'Enter', ['Shift'], 'X'));
    same('Ctrl+F9 given to the line break in a cell breaks the line', cellBreak, await after(table, 'cell', 2, 'F9', ['Control'], 'X'));
    same('F7 given to the document end goes there', toEnd, await after(para, 'Para one', 2, 'F7', [], 'X'));
    same('Shift+F7 given to the document start goes there', 'XPara one\n\nPara two\n', await after(para, 'Para two', 2, 'F7', ['Shift'], 'X'));
    same('Ctrl+End given away does nothing', 'PaXra one\n\nPara two\n', await after(para, 'Para one', 2, 'End', ['Control'], 'X'));

    // Out of a table: Ctrl+Enter by default; on F4 given to it, and Ctrl+Enter then an Enter (a new row).
    const F4 = 115;
    const left = 'X\n';
    await bind(null);
    const leftByCtrl = await after(table, 'cell', 2, 'Enter', ['Control'], 'X');
    check('Ctrl+Enter in a cell leaves the table (no bindings from the host)', leftByCtrl.startsWith(table) && leftByCtrl.endsWith(left), JSON.stringify(leftByCtrl));
    const newRow = await after(table, 'cell', 2, 'Enter', [], 'X');
    await bind({ leaveTable: { keyCode: F4, ctrl: false, alt: false, shift: false } });
    same('F4 given to leaving a table leaves it', leftByCtrl, await after(table, 'cell', 2, 'F4', [], 'X'));
    same('Ctrl+Enter given away is an Enter in a cell (a new row)', newRow, await after(table, 'cell', 2, 'Enter', ['Control'], 'X'));
    same('F4 outside a table is what it otherwise is (nothing)', para, await after(para, 'Para one', 2, 'F4'));

    // A link while editing: Ctrl+click opens it by default; with Alt given to it, Alt+click does and Ctrl+click not;
    // cleared, no click does. A plain click never does.
    const linkDoc = 'First para\n\n[site](https://example.com/kb) after\n';
    await page.evaluate(() => {
      window.__opened = [];
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => {
        const m = JSON.parse(raw);
        if (m.type === 'invoke' && m.name === 'OpenNewWindow') window.__opened.push(m.args);
        return post(raw);
      };
    });
    const opens = async modifiers => {
      await mode(true, false);
      await load(linkDoc);
      await mode(false, false);
      await page.evaluate(() => { window.__opened.length = 0; });
      const at = await page.evaluate(() => {
        const a = document.querySelector('#ag-editor-id a[href="https://example.com/kb"]');
        if (!a) return null;
        const r = a.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
      });
      if (!at) throw new Error('no link drawn');
      for (const m of modifiers) await page.keyboard.down(m);
      await page.mouse.click(at.x, at.y);
      for (const m of modifiers.slice().reverse()) await page.keyboard.up(m);
      await pause(300);
      return page.evaluate(() => window.__opened.length > 0);
    };
    await bind(null);
    check('Ctrl+click opens a link (no bindings from the host)', await opens(['Control']), 'not opened');
    check('a plain click while editing opens no link', !(await opens([])), 'opened');
    await bind({ openLink: { keyCode: 0xE9, ctrl: false, alt: true, shift: false } });
    check('Alt+click, given to opening a link, opens it', await opens(['Alt']), 'not opened');
    check('Ctrl+click given away opens no link', !(await opens(['Control'])), 'opened');
    await bind({ openLink: null });
    check('Ctrl+click with opening a link cleared opens nothing', !(await opens(['Control'])), 'opened');

    // A key cleared (null): nothing does it, the old key included.
    await bind({ indent: null });
    same('Tab with indent cleared does nothing', list, await after(list, 'two', 1, 'Tab'));

    // Reading mode: select all on the key it is given, the document only; Ctrl+A given away selects nothing.
    await bind({ selectAll: { keyCode: F6, ctrl: false, alt: false, shift: false } });
    await mode(true, false);
    await load(para);
    await mode(false, true);
    const selected = () => page.evaluate(() => window.getSelection().toString());
    await page.evaluate(() => window.getSelection().removeAllRanges());
    await press('a', ['Control']);
    check('Ctrl+A given away selects nothing in reading mode', (await selected()) === '', JSON.stringify(await selected()));
    await press('F6');
    const all = await selected();
    check('F6 given to select all selects the document in reading mode', all.includes('Para one') && all.includes('Para two'), JSON.stringify(all));

    // Reading mode: the document's end on the key it is given; Ctrl+End given away scrolls nowhere.
    await bind({ documentEnd: { keyCode: F7, ctrl: false, alt: false, shift: false } });
    await mode(true, false);
    await load(Array.from({ length: 120 }, (_, i) => `reading paragraph ${i + 1}`).join('\n\n') + '\n');
    await mode(false, true);
    const scrollY = () => page.evaluate(() => window.scrollY);
    await page.evaluate(() => window.scrollTo(0, 0));
    await press('End', ['Control']);
    check('Ctrl+End given away scrolls nowhere in reading mode', (await scrollY()) === 0, String(await scrollY()));
    await press('F7');
    check('F7 given to the document end scrolls there in reading mode', (await scrollY()) > 500, String(await scrollY()));

    // The source editor: undo and redo on their keys ask the host; Ctrl+Z, Ctrl+Y given away ask nothing.
    // (Not Alt+F6: Chrome's own, it takes the focus out of the page.)
    await bind({ undo: { keyCode: F9, ctrl: true, alt: false, shift: true }, redo: { keyCode: F8, ctrl: true, alt: false, shift: false } });
    await mode(true, false);
    await load(para);
    await page.evaluate(() => {
      window.__history = [];
      const post = window.chrome.webview.postMessage;
      // Sent as a difference from the one before (transport.postMessageDiff): read once the harness has put it together.
      window.chrome.webview.postMessage = raw => {
        const result = post(raw);
        if (JSON.parse(raw).name === 'HistoryRequested') window.__history.push(window.__last.HistoryRequested.redo);
        return result;
      };
      document.querySelector('.CodeMirror').CodeMirror.focus();
    });
    const asked = async (key, modifiers) => {
      await page.evaluate(() => { window.__history.length = 0; });
      await press(key, modifiers);
      return page.evaluate(() => window.__history.slice());
    };
    same('Ctrl+Shift+F9 given to undo asks the host for an undo', '[false]', JSON.stringify(await asked('F9', ['Control', 'Shift'])));
    same('Ctrl+F8 given to redo asks the host for a redo', '[true]', JSON.stringify(await asked('F8', ['Control'])));
    same('Ctrl+Z given away asks nothing', '[]', JSON.stringify(await asked('z', ['Control'])));
    same('Ctrl+Y given away asks nothing', '[]', JSON.stringify(await asked('y', ['Control'])));
    await bind(null);
    await mode(false, false);
    await mode(true, false);
    await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.focus());
    same('Ctrl+Z with no bindings asks the host for an undo', '[false]', JSON.stringify(await asked('z', ['Control'])));
    same('Ctrl+Shift+Z with no bindings asks for a redo', '[true]', JSON.stringify(await asked('z', ['Control', 'Shift'])));

    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
