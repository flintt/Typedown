// An automation write (ApplyDocumentEdit) while a long document is still being drawn a part at a time: it is applied
// exactly, the host gets that text, and once drawn the page shows the changes at both ends - with the changes
// highlighted and scrolled to as well. The drawing is slowed by a test switch (__typedownProgressiveSliceMs) so the
// write lands in the middle of it; nothing covered this before (docs/todo.md, the progressive render's open risks).
//
//   node progressive-apply-check.js
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

const sha = text => crypto.createHash('sha256').update(text, 'utf8').digest('hex');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    const doc = fs.readFileSync(path.join(__dirname, 'doc300k.md'), 'utf8');
    let op = 0;
    for (const [what, settings, scrollToChange] of [
      ['a write', { highlightAutomationChanges: false }, false],
      ['a write highlighted and scrolled to', { highlightAutomationChanges: true }, true],
    ]) {
      await page.evaluate(s => window.__deliver('SettingsChanged', s), settings);
      await mode(false, false);
      // Slices of a millisecond: drawing 300,000 characters takes many seconds.
      await page.evaluate(() => { window.__typedownProgressiveSliceMs = 1; delete window.__typedownProgressiveDone; });
      await load(doc);
      await pause(300);
      const drawing = await page.evaluate(() => !window.__typedownProgressiveDone);
      check(drawing, `${what}: the document is still being drawn when the write comes`);
      const lines = doc.split('\n');
      const first = lines.findIndex(l => l.trim() && !l.startsWith('#'));
      lines[first] = 'EDITEDSTART ' + lines[first];
      const text = lines.join('\n').replace(/\n*$/, '\n\nEDITEDEND paragraph written by a program.\n');
      const operationId = `op${++op}`;
      await page.evaluate(({ operationId, baseContentHash, text, scrollToChange }) => {
        // Every element given the highlight while the write is applied (it fades in two seconds; the reply can come later).
        window.__lit = new Set();
        window.__litObserver?.disconnect();
        window.__litObserver = new MutationObserver(records => records.forEach(r => { if (r.target.classList?.contains('td-external-change')) window.__lit.add(r.target); }));
        window.__litObserver.observe(document.querySelector('#editor') || document.body, { subtree: true, attributes: true, attributeFilter: ['class'] });
        delete window.__last.DocumentEditApplied;
        window.__deliver('ApplyDocumentEdit', { operationId, baseContentHash, text, scrollToChange });
      }, { operationId, baseContentHash: sha(doc), text, scrollToChange });
      // The rest drawn at the usual pace from here: a write scrolled to waits for its change to be drawn.
      await page.evaluate(() => { delete window.__typedownProgressiveSliceMs; });
      await page.waitForFunction(id => window.__last.DocumentEditApplied?.operationId === id, { timeout: 60000 }, operationId);
      const reply = await page.evaluate(() => window.__last.DocumentEditApplied);
      // Highlighted: the blocks changed, not every block between the two changes (a layout for each of them took the
      // write past a minute, and all of them flashed).
      if (settings.highlightAutomationChanges) {
        const lit = await page.evaluate(() => window.__lit.size);
        check(lit >= 1 && lit <= 3, `${what}: only the changed blocks highlighted (${lit})`);
      }
      check(reply.outcome === 'applied' && reply.sourceHash === sha(text), `${what}: applied, the reply hashes the text written (${reply.outcome})`);
      const done = await page.waitForFunction(() => !!window.__typedownProgressiveDone, { timeout: 120000 }).then(() => true, () => false);
      check(done, `${what}: the drawing finishes`);
      await pause(500);
      check(await flush() === text, `${what}: the host gets the text written, exactly`);
      const shown = await page.evaluate(() => { const t = document.querySelector('#ag-editor-id').textContent; return [t.includes('EDITEDSTART'), t.includes('EDITEDEND')]; });
      check(shown[0] && shown[1], `${what}: the page shows both changes once drawn (start ${shown[0]}, end ${shown[1]})`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
