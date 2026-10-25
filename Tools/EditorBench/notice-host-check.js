// The notice that the first visual edit may rewrite part of a document is the host's card over the window (like the one an
// export leaves), not anything in the page: in the page it pushed the document down as it came and up as it went, or lay
// over its first line. The page works it out a frame after the document is on the screen and tells the host once per load
// (FirstEditNotice: show, the load, the text it has for it); it says it is gone after 10 s or in reading mode. The
// document never moves for it.
//
//   node notice-host-check.js
const { openEditor } = require('./harness');

// CommonMark example 31 (as F02): Muya rewrites the entity in this HTML block on the first visual edit - 'unsafe'.
const doc = '<a href="&ouml;&ouml;.html">\n\n# Title\n\n' + Array.from({ length: 40 }, (_, i) => `Paragraph ${i} with a few words in it.`).join('\n\n') + '\n';
const safe = '# Safe\n\nNothing the first edit would lose.\n';

(async () => {
  const { page, errors, mode, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await mode(false, false);
    // Every FirstEditNotice the page sends, and the text the page has for the notice (localization.ts sets it from the host).
    await page.evaluate(() => {
      document.documentElement.style.setProperty('--FirstEditWarning', JSON.stringify('The first edit may rewrite some raw HTML.'));
      window.__notices = [];
      const post = window.chrome.webview.postMessage;
      let prev = '';
      window.chrome.webview.postMessage = raw => {
        try {
          const m = JSON.parse(raw);
          // Sent as a whole or as a change to the one before (services/transport postMessageDiff).
          if (m.name === 'FirstEditNotice' && m.type === 'diffmsg') { prev = m.diff ? prev.slice(0, m.start) + m.args + prev.slice(m.end) : m.args; window.__notices.push(JSON.parse(prev)) }
        } catch { }
        return post(raw);
      };
    });
    const r = await page.evaluate(doc => new Promise(done => {
      const frames = [];
      const top = () => { const h = Array.from(document.querySelectorAll('#ag-editor-id h1')).find(e => e.textContent.includes('Title')); return h ? Math.round(h.getBoundingClientRect().top * 10) / 10 : null };
      const t0 = performance.now();
      const tick = () => {
        frames.push([Math.round(performance.now() - t0), top(), window.__notices.length]);
        if (performance.now() - t0 > 1500) return done(frames);
        requestAnimationFrame(tick);
      };
      window.__deliver('LoadFile', { text: doc, loadId: 7, basePath: '/tmp' });
      requestAnimationFrame(tick);
    }), doc);
    const notices = await page.evaluate(() => window.__notices.slice());
    console.log(JSON.stringify(notices));
    const shown = notices.filter(n => n.show && n.loadId === 7);
    check(shown.length === 1, `the page tells the host once that this load's first edit may rewrite part of it (${shown.length} times)`);
    check(shown.length === 1 && shown[0].text === 'The first edit may rewrite some raw HTML.', `with the text it has for it (${JSON.stringify(shown[0] && shown[0].text)})`);
    const firstWithText = r.find(f => f[1] != null);
    check(!!firstWithText && firstWithText[2] === 0, 'told after the document is on the screen, not before');
    check(!await page.evaluate(() => !!document.querySelector('.td-first-edit-warning, .td-first-edit-warning-slot, [class*="first-edit"]')), 'nothing of the notice is in the page');
    const tops = [...new Set(r.filter(f => f[1] != null).map(f => f[1]))];
    check(tops.length === 1, `the document stays where it is meanwhile (its title at ${tops.join(', ')})`);
    // Reading mode: the notice goes (the page says so), and comes back with visual mode.
    await mode(false, true);
    let last = await page.evaluate(() => window.__notices[window.__notices.length - 1]);
    check(!!last && last.show === false && last.loadId === 7, `reading mode: the page tells the host it is gone (${JSON.stringify(last)})`);
    await mode(false, false);
    // A document the first edit loses nothing of: no notice for it.
    const before = await page.evaluate(() => window.__notices.length);
    await page.evaluate(text => window.__deliver('LoadFile', { text, loadId: 8, basePath: '/tmp' }), safe);
    await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 8, { timeout: 30000 });
    await new Promise(r => setTimeout(r, 500));
    const later = await page.evaluate(n => window.__notices.slice(n), before);
    const current = 8;
    check(!later.some(n => n.show && n.loadId === current), `a document with nothing to lose: no notice for it (${JSON.stringify(later)})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
