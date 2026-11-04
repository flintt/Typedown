// Switching from a Markdown tab to a plain text one and back shows no frame of the other document (a reader, 2026-11-04:
// from a .md to a .txt the Markdown file's source flashed before the .txt). As the host does it: the mode first - its
// PlainTextShown, SourceCode and PlainTextProse, one message each, set as the tab's path changes - and the file's text a
// little after. Every frame painted meanwhile is looked at: to the .txt, the source editor never holds the Markdown file,
// nor is the page left blank; back to the .md, the visual editor never holds the plain text, nor is the page blank. And
// Save as turning a .md into a .txt (the mode, no load) still ends in the plain view with the text.
//
//   node md-txt-flash-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const md = '# MDHEAD Markdown file\n\nSome **bold** words MDBODY.\n\n- an item\n';
  const txt = 'TXTLINE plain text file, # not a heading\nsecond line TXTBODY\n';
  let id = 1000;
  const mode = plain => page.evaluate(async plain => {
    // One message each, as SettingsViewModel notifies them.
    for (const o of [{ plainTextShown: plain }, { sourceCode: plain }, { plainTextProse: plain }]) {
      window.__deliver('SettingsChanged', o);
      await new Promise(r => setTimeout(r, 5));
    }
  }, plain);
  const watch = () => page.evaluate(() => {
    window.__frames = [];
    // Its own: the last round's loop, still waiting for a frame, ends there.
    const watching = window.__watching = {};
    const look = () => {
      if (window.__watching !== watching) return;
      const cm = document.querySelector('.CodeMirror');
      const muya = document.querySelector('#ag-editor-id');
      window.__frames.push({
        cm: cm ? cm.CodeMirror.getValue().slice(0, 40) : null,
        muya: muya ? (muya.textContent || '').slice(0, 40) : null,
      });
      requestAnimationFrame(look);
    };
    requestAnimationFrame(look);
  });
  const frames = async () => page.evaluate(() => { window.__watching = null; return window.__frames; });
  const switchTo = async (plain, text) => {
    await watch();
    await mode(plain);
    // The host reads the tab's text and posts it a little after the mode.
    await pause(30);
    await page.evaluate(({ text, id }) => window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp' }), { text, id: ++id });
    await pause(700);
    return frames();
  };
  const show = f => JSON.stringify(f.slice(0, 12));
  try {
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: false, plainTextShown: false, plainTextProse: false }));
    await load(md);
    await pause(1000);
    report(/MDHEAD/.test(await page.evaluate(() => document.querySelector('#ag-editor-id')?.textContent || '')), 'the Markdown file is shown in the visual editor', '');

    for (let round = 1; round <= 3; round++) {
      let f = await switchTo(true, txt);
      const mdInSource = f.filter(x => x.cm && /MDHEAD|MDBODY/.test(x.cm));
      const blank = f.filter(x => x.cm == null && x.muya == null);
      report(mdInSource.length === 0, `round ${round}, .md to .txt: no frame of the source editor holding the Markdown file`, `${mdInSource.length} of ${f.length}: ${show(f)}`);
      report(blank.length === 0, `round ${round}, .md to .txt: no blank frame`, `${blank.length} of ${f.length}: ${show(f)}`);
      report(/TXTLINE/.test(f[f.length - 1].cm || ''), `round ${round}, .md to .txt: the plain text file ends up shown`, show(f.slice(-2)));

      f = await switchTo(false, md);
      const txtInMuya = f.filter(x => x.muya && /TXTLINE|TXTBODY/.test(x.muya));
      const blankBack = f.filter(x => x.cm == null && x.muya == null);
      report(txtInMuya.length === 0, `round ${round}, .txt to .md: no frame of the visual editor holding the plain text`, `${txtInMuya.length} of ${f.length}: ${show(f)}`);
      report(blankBack.length === 0, `round ${round}, .txt to .md: no blank frame`, `${blankBack.length} of ${f.length}: ${show(f)}`);
      report(/MDHEAD/.test(f[f.length - 1].muya || ''), `round ${round}, .txt to .md: the Markdown file ends up shown`, show(f.slice(-2)));
    }

    // Save as: the .md becomes a .txt - the mode, no load. The plain view comes, with the text.
    await mode(true);
    await pause(1600);
    const after = await page.evaluate(() => document.querySelector('.CodeMirror')?.CodeMirror.getValue() || null);
    report(/MDHEAD/.test(after || ''), 'Save as .txt: the plain view comes with the text', JSON.stringify(after));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
