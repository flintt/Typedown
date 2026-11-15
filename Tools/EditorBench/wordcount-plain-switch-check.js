// A long plain text left quickly for a Markdown file: every count the page reports under a load is of that load's text
// (the user, 2026-11-15: the writing timeline had a 14,740-character Markdown file written by some 527,000 characters
// in one minute, nothing typed - a 628,000-character .txt had just been shown and left for it). The host takes a count
// moving within one load for writing (WritingStats), so one report counting another file's text under the new load's
// id is that file's length written, and the next, the document's own again, as much deleted.
//
// As the host switches tabs (md-txt-flash-check): the mode first, one message each, then the file's text. Between the
// .txt arriving and the next .md, several waits - before the .txt is drawn, just after, a moment on - and back to the
// .txt from the .md too. With nothing typed every report of a load counts the same, and never more characters than its
// text has.
//
//   node wordcount-plain-switch-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  // Every StateChange as the page sent it (the harness keeps only the last; a diff message is put together there first).
  await page.evaluate(() => {
    window.__states = [];
    const post = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = raw => {
      post(raw);
      const m = JSON.parse(raw);
      if (m.name === 'StateChange') {
        const s = window.__last.StateChange;
        window.__states.push({ loadId: s.loadId, character: s.state?.wordCount?.character, word: s.state?.wordCount?.word, at: performance.now() });
      }
    };
  });
  const para = n => `Paragraph ${n} of a Markdown file, with **some** words in it and a [link](https://example.com).\n\n`;
  const mdA = '# Word export\n\n' + Array.from({ length: 50 }, (_, i) => para(i)).join('');
  const mdB = '# Typeleaf\n\n' + Array.from({ length: 140 }, (_, i) => para(i)).join('');
  // About 628,000 characters, as the user's Tmp.txt.
  const line = n => `${String(n).padStart(6, '0')} a line of a long plain text file, log output or notes kept as they came\n`;
  const txt = Array.from({ length: 7950 }, (_, i) => line(i)).join('');
  console.log(`  .md ${mdA.length} and ${mdB.length} characters, .txt ${txt.length}`);
  let id = 1000;
  const texts = {};
  const mode = plain => page.evaluate(async plain => {
    for (const o of [{ plainTextShown: plain }, { sourceCode: plain }, { plainTextProse: plain }]) {
      window.__deliver('SettingsChanged', o);
      await new Promise(r => setTimeout(r, 5));
    }
  }, plain);
  // A tab switched to: the mode, the text 30 ms after it (as the host reads and posts it); the load's id.
  const switchTo = async (plain, text) => {
    await mode(plain);
    await pause(30);
    const loadId = ++id;
    texts[loadId] = text;
    await page.evaluate(({ text, loadId }) => window.__deliver('LoadFile', { text, loadId, basePath: '/tmp' }), { text, loadId });
    return loadId;
  };
  const loaded = loadId => page.waitForFunction(loadId => window.__last.FileLoaded?.loadId === loadId, { timeout: 60000 }, loadId);
  try {
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await loaded(await switchTo(false, mdA));
    await pause(800);
    // The wait on the .txt: from its text sent (0-400 ms), or once it is drawn (FileLoaded, then 0-500 ms).
    const waits = [{ sent: 100 }, { sent: 250 }, { sent: 400 }, { drawn: 0 }, { drawn: 100 }, { drawn: 250 }, { drawn: 500 }];
    for (const wait of waits) {
      const plainLoad = await switchTo(true, txt);
      if (wait.drawn != null) { await loaded(plainLoad); await pause(wait.drawn); } else await pause(wait.sent);
      const mdLoad = await switchTo(false, wait.drawn === 250 ? mdA : mdB);
      await loaded(mdLoad);
      await pause(1200);
      // And back to the .txt and on to the .md once more, as quickly: the other way round.
      const plainAgain = await switchTo(true, txt);
      await pause(150);
      const mdAgain = await switchTo(false, mdB);
      await loaded(mdAgain);
      await pause(1200);
      const states = await page.evaluate(() => window.__states.splice(0));
      const what = JSON.stringify(wait);
      for (const loadId of [plainLoad, mdLoad, plainAgain, mdAgain]) {
        const text = texts[loadId];
        const of = states.filter(s => s.loadId === loadId);
        const counts = [...new Set(of.map(s => s.character))];
        const name = text === txt ? '.txt' : text === mdA ? 'first .md' : 'second .md';
        const over = of.filter(s => s.character > text.length);
        report(over.length === 0, `${what}, load ${loadId} (${name}, ${text.length} chars): no report counts more characters than its text`,
          `${over.length} of ${of.length}: ${over.map(s => s.character).join(', ')}`);
        report(counts.length <= 1, `${what}, load ${loadId} (${name}): nothing typed, every report counts the same`, `counts ${counts.join(', ')} in ${of.length} reports`);
      }
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
