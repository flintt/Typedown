// The notice that the first visual edit may rewrite part of a document lies over the top of the page and moves nothing:
// it is worked out a frame after the document is on the screen (it took a quarter of a second for a 1.5 MB document, in the
// task that put it there - LT01), so taking room it would push the document down as it came, and up again as it went.
// A document the first edit would lose something of is opened at the top of the page; every frame its first block's place
// is read, from the load until the notice is there, and again as the notice is closed.
//
//   node notice-move-check.js
const { openEditor } = require('./harness');

// CommonMark example 31 (as F02): Muya rewrites the entity in this HTML block on the first visual edit - 'unsafe'.
const doc = '<a href="&ouml;&ouml;.html">\n\n# Title\n\n' + Array.from({ length: 40 }, (_, i) => `Paragraph ${i} with a few words in it.`).join('\n\n') + '\n';

(async () => {
  const { page, errors, mode, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await mode(false, false);
    const r = await page.evaluate(doc => new Promise(done => {
      const frames = [];
      const top = () => { const h = Array.from(document.querySelectorAll('#ag-editor-id h1')).find(e => e.textContent.includes('Title')); return h ? Math.round(h.getBoundingClientRect().top * 10) / 10 : null };
      const t0 = performance.now();
      const tick = () => {
        const notice = !!document.querySelector('.td-first-edit-warning');
        frames.push([Math.round(performance.now() - t0), top(), notice, window.scrollY]);
        if (notice && frames.filter(f => f[2]).length >= 5) return done(frames);
        if (performance.now() - t0 > 5000) return done(frames);
        requestAnimationFrame(tick);
      };
      window.__deliver('LoadFile', { text: doc, loadId: 7, basePath: '/tmp' });
      requestAnimationFrame(tick);
    }), doc);
    const shown = r.filter(f => f[1] != null);
    const tops = [...new Set(shown.map(f => f[1]))];
    console.log(JSON.stringify(r.slice(0, 12)));
    check(r.some(f => f[2]), 'the notice comes');
    check(shown.length > 0 && tops.length === 1, `the document stays where it is as the notice comes (its title at ${tops.join(', ')})`);
    const closed = await page.evaluate(() => new Promise(done => {
      const h = Array.from(document.querySelectorAll('#ag-editor-id h1')).find(e => e.textContent.includes('Title'));
      const before = h.getBoundingClientRect().top;
      const slot = document.querySelector('.td-first-edit-warning-slot').getBoundingClientRect().height;
      document.querySelector('.td-first-edit-warning-close').click();
      requestAnimationFrame(() => requestAnimationFrame(() => done({ before, after: h.getBoundingClientRect().top, slot, gone: !document.querySelector('.td-first-edit-warning') })));
    }));
    check(closed.slot === 0, `the notice takes no room of its own (its slot ${closed.slot} px tall)`);
    check(closed.gone && Math.abs(closed.after - closed.before) < 0.5, `closed, it goes and the document stays where it is (${closed.before} -> ${closed.after})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
