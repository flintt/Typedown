// One selection in one colour in a plain text file (a reader: selecting a second passage in a .txt, the first stayed
// highlighted and the second was another colour). A press in the space around the editor - the prose view's margins
// either side of its column - never reached the editor: the browser made a selection of its own over the lines, in
// CodeMirror's default lilac, while the editor still painted the one before.
// - a passage selected inside, then a drag from the left margin into the text: the editor's selection is the new
//   passage, the old one no longer painted, no selection of the browser's own over the lines, the keyboard in the editor;
// - the same from the right margin, and a Shift+drag from the margin extending the selection;
// - the browser's selection colour over the lines, should one arise, is the editor's own selection colour.
//
//   node txt-selection-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    await page.setViewport({ width: 1400, height: 800 });
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: true, plainTextShown: true, plainTextProse: true }));
    await pause(300);
    const lines = [];
    for (let i = 0; i < 60; i++) lines.push(`Line ${i}: the quick brown fox jumps over the lazy dog, again and again and again.`);
    await load(lines.join('\n'));
    await pause(700);
    const pos = (line, ch) => page.evaluate((line, ch) => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      const c = cm.charCoords({ line, ch }, 'window');
      return { x: c.left + 1, y: (c.top + c.bottom) / 2 };
    }, line, ch);
    const box = await page.evaluate(() => { const r = document.querySelector('.CodeMirror').getBoundingClientRect(); return { left: r.left, right: r.right }; });
    const state = () => page.evaluate(() => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      const sel = window.getSelection();
      const native = sel.rangeCount && !sel.getRangeAt(0).collapsed ? sel.toString() : '';
      const r = cm.listSelections()[0];
      const from = r.anchor.line < r.head.line || (r.anchor.line === r.head.line && r.anchor.ch <= r.head.ch) ? r.anchor : r.head;
      const to = from === r.anchor ? r.head : r.anchor;
      const layerTops = [...document.querySelectorAll('.CodeMirror-selected')].map(e => e.getBoundingClientRect().top);
      const lineTop = n => cm.charCoords({ line: n, ch: 0 }, 'window').top;
      return { ranges: cm.listSelections().length, from: [from.line, from.ch], to: [to.line, to.ch], native, active: document.activeElement.tagName, layerTops, top3: lineTop(3), top8: lineTop(8) };
    });
    const drag = async (a, b, shift) => {
      if (shift) await page.keyboard.down('Shift');
      await page.mouse.move(a.x, a.y); await page.mouse.down();
      await page.mouse.move((a.x + b.x) / 2, (a.y + b.y) / 2, { steps: 5 });
      await page.mouse.move(b.x, b.y, { steps: 5 }); await page.mouse.up();
      if (shift) await page.keyboard.up('Shift');
      await pause(250);
    };

    // A passage inside, then a drag from the left margin.
    await drag(await pos(3, 5), await pos(5, 20));
    const p8 = await pos(8, 0), p10 = await pos(10, 25);
    await drag({ x: box.left - 40, y: p8.y }, p10);
    let s = await state();
    report(!s.native, 'from the left margin: no selection of the browser\'s own over the lines', JSON.stringify(s.native.slice(0, 40)));
    report(s.ranges === 1 && s.from[0] === 8 && s.to[0] === 10, 'the editor\'s selection is the new passage (lines 8-10)', JSON.stringify(s));
    report(!s.layerTops.some(t => Math.abs(t - s.top3) < 3), 'the passage before is no longer painted', JSON.stringify(s.layerTops));
    report(s.active === 'TEXTAREA', 'the keyboard is in the editor', s.active);

    // From the right margin.
    await drag(await pos(3, 5), await pos(4, 20));
    const p12 = await pos(12, 0), p13 = await pos(13, 15);
    await drag({ x: box.right + 40, y: p12.y }, p13);
    s = await state();
    report(!s.native && s.from[0] === 12 && s.to[0] === 13, 'from the right margin: the editor\'s selection, lines 12-13', JSON.stringify(s));

    // Shift+drag from the margin extends.
    await drag(await pos(20, 4), await pos(20, 10));
    await drag({ x: box.left - 40, y: (await pos(23, 0)).y }, await pos(23, 12), true);
    s = await state();
    report(!s.native && s.from[0] === 20 && s.to[0] === 23, 'a Shift+drag from the margin extends the selection (20-23)', JSON.stringify(s));

    // The colours: the browser's selection over the lines and the editor's own the same.
    const colours = await page.evaluate(() => {
      const line = document.querySelector('.CodeMirror-line > span') || document.querySelector('.CodeMirror-line');
      const layer = document.querySelector('.CodeMirror-selected');
      return { native: getComputedStyle(line, '::selection').backgroundColor, layer: layer && getComputedStyle(layer).backgroundColor };
    });
    report(colours.layer && colours.native === colours.layer, 'one selection colour: the browser\'s over the lines is the editor\'s', JSON.stringify(colours));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
