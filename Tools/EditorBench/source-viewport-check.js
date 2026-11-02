// The source editor scrolls itself and draws the lines on screen, not the whole text (a reader: a long .txt was slow -
// the page scrolled and the editor's viewport was every line, a 40,000-line file took 20 s to open). A 40,000-line plain
// text file in its prose and source views and a long Markdown file in source mode open fast with a few hundred lines
// drawn; the page itself does not scroll; a scroll position given with a load is where it goes; Ctrl+End shows the end.
//
//   node source-viewport-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  let id = 500;
  const open = async (text, settings, scrollTop) => {
    await page.evaluate(s => window.__deliver('SettingsChanged', s), settings);
    await pause(300);
    const t0 = Date.now();
    await page.evaluate(({ text, id, scrollTop }) => window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp', scrollTop }), { text, id: ++id, scrollTop });
    await page.waitForFunction(n => { const cm = document.querySelector('.CodeMirror'); return cm && cm.CodeMirror.getValue().length === n; }, { timeout: 60000, polling: 50 }, text.length);
    await page.evaluate(() => new Promise(requestAnimationFrame));
    return Date.now() - t0;
  };
  const state = () => page.evaluate(() => {
    const el = document.querySelector('.CodeMirror');
    return { drawn: el.querySelectorAll('.CodeMirror-line').length, pageScrolls: document.scrollingElement.scrollHeight > innerHeight + 4, top: el.CodeMirror.getScrollInfo().top };
  });
  try {
    const txt = Array.from({ length: 40000 }, (_, i) => `2026-11-02 12:00:${String(i % 60).padStart(2, '0')} line ${i} of a long log with some text`).join('\n');
    for (const [label, settings] of [['prose', { sourceCode: true, plainTextShown: true, plainTextProse: true }], ['source', { sourceCode: true, plainTextShown: true, plainTextProse: false }]]) {
      const ms = await open(txt, settings);
      const s = await state();
      report(ms < 1500, `a 40,000-line plain text file, ${label} view: open in ${ms} ms (was 6-20 s)`, `${ms} ms`);
      report(s.drawn < 400, `${label} view: only the lines on screen drawn (${s.drawn})`, `${s.drawn}`);
      report(!s.pageScrolls, `${label} view: the page does not scroll, the editor does`, JSON.stringify(s));
    }
    // The host's scroll bar is the editor's (a reader: a long .txt had a thick bar of its own, beside the column, not
    // at the window's edge): its own bars hidden, its scroller reported and moved by the host.
    await page.evaluate(() => { document.querySelector('.CodeMirror-scroll').scrollTop = 5000; });
    await pause(300);
    const reported = await page.evaluate(() => ({ ...window.__last.OnScroll, bar: getComputedStyle(document.querySelector('.CodeMirror-vscrollbar')).display }));
    report(Math.abs(reported.scrollY - 5000) < 2 && reported.maximumY > 100000 && reported.bar === 'none', "the editor's scrolling reported to the host's scroll bar, its own bar hidden", JSON.stringify(reported));
    await page.evaluate(() => window.__deliver('OnScroll', { scrollX: 0, scrollY: 20000 }));
    await pause(200);
    const moved = await page.evaluate(() => document.querySelector('.CodeMirror-scroll').scrollTop);
    report(Math.abs(moved - 20000) < 2, `the host's scroll bar moves the editor (${moved})`, `${moved}`);
    // A scroll position with the load (a tab switched back to): there.
    await open(txt.replace('line 1 ', 'line one '), { sourceCode: true, plainTextShown: true, plainTextProse: false }, 30000);
    await pause(200);
    const restored = await state();
    report(Math.abs(restored.top - 30000) < 50, `the scroll position given with a load is where it goes (${restored.top})`, `${restored.top}`);
    // Ctrl+End: the last line on screen.
    await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.focus());
    await page.keyboard.down('Control'); await page.keyboard.press('End'); await page.keyboard.up('Control');
    await pause(300);
    const end = await page.evaluate(() => { const lines = [...document.querySelectorAll('.CodeMirror-line')]; const last = lines.find(l => l.textContent.includes('line 39999 ')); if (!last) return null; const r = last.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, h: innerHeight }; });
    report(end && end.top >= 0 && end.bottom <= end.h, 'Ctrl+End shows the last line', JSON.stringify(end));
    // A long Markdown file in source mode.
    const md = Array.from({ length: 3000 }, (_, i) => `## Section ${i}\n\nParagraph ${i} with **bold** text.\n`).join('\n');
    const mdMs = await open(md, { sourceCode: true, plainTextShown: false, plainTextProse: false });
    const mdState = await state();
    report(mdMs < 1500 && mdState.drawn < 400, `a long Markdown file in source mode: open in ${mdMs} ms, ${mdState.drawn} lines drawn`, `${mdMs} ms, ${mdState.drawn}`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
