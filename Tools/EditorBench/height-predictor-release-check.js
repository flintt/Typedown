// The experimental height prediction's page watchers released with its editor (the audit of 2026-11-24, P05): with it on,
// five switches between the visual editor and source mode added a font listener each and removed none, and its
// MutationObservers stayed connected, each holding an editor gone. Now each switch's listener and watchers go with it:
// after five rounds, at most one listener is left (the editor showing), and no size rules of an editor gone.
//
//   node height-predictor-release-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what} - ${detail}`); if (!ok) failures.push(what); };
  try {
    await page.evaluate(() => {
      const life = window.__life = { added: 0, removed: 0, observers: 0, disconnected: 0 };
      const fonts = document.fonts, add = fonts.addEventListener, remove = fonts.removeEventListener;
      fonts.addEventListener = function (t, ...a) { if (t === 'loadingdone') life.added++; return add.call(this, t, ...a) };
      fonts.removeEventListener = function (t, ...a) { if (t === 'loadingdone') life.removed++; return remove.call(this, t, ...a) };
      const Original = MutationObserver;
      window.MutationObserver = class extends Original {
        constructor(cb) { super(cb); this.counted = new Error().stack.includes('watch'); if (this.counted) life.observers++ }
        disconnect() { if (this.counted && !this.gone) { this.gone = true; life.disconnected++ } return super.disconnect() }
      };
    });
    let doc = '# Lifecycle\n\n';
    for (let i = 0; doc.length < 160000; i++) doc += `Paragraph ${i} with a set of ordinary words written for this test.\n\n`;
    await page.evaluate(() => window.__deliver('SettingsChanged', { predictHeights: true, keepSwitchedDocuments: false }));
    for (let i = 0; i < 5; i++) {
      await mode(false, false);
      await load(doc + i);
      await page.waitForFunction(() => !window.__typedownMuya.renderPending, { timeout: 60000 });
      await pause(800);
      await mode(true, false);
      await pause(300);
    }
    const life = await page.evaluate(() => ({ ...window.__life, styles: document.querySelectorAll('style[data-td-heights]').length }));
    check(life.added >= 5, 'the prediction watched each visual editor', JSON.stringify(life));
    check(life.added - life.removed <= 1, 'font listeners released with their editors', `${life.added} added, ${life.removed} removed`);
    check(life.observers - life.disconnected <= 2, 'page watchers disconnected with their editors', `${life.observers} made, ${life.disconnected} disconnected`);
    check(life.styles === 0, 'no size rules of an editor gone (in source mode)', `${life.styles} style elements`);
    // Back to the visual editor: it still predicts, its own listener and watchers in place.
    await mode(false, false);
    await load(doc + 'again');
    await page.waitForFunction(() => !window.__typedownMuya.renderPending, { timeout: 60000 });
    await pause(800);
    const back = await page.evaluate(() => ({ ...window.__life, watching: window.__typedownMuya.contentState.stateRender.heights.watching }));
    check(back.watching && back.added - back.removed === 1, 'the editor made again watches again', JSON.stringify(back));
    check(errors.length === 0, 'no page errors', JSON.stringify(errors));
  } finally {
    await close();
  }
  process.exitCode = failures.length ? 1 : 0;
})().catch(e => { console.error(e); process.exitCode = 1 });
