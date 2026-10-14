// The "show extra blank lines" setting (Settings > Editor, `showBlankLines`): an empty paragraph - an extra blank line of
// the file, or an empty line made with Enter - has a line's height while editing; with the setting it keeps that height
// in reading mode and in the HTML (and PDF) export too. Off, the default, everything is as before: reading mode and the
// export collapse it. The text the editor hands back is the same either way.
//
//   node blanklines-show-check.js
const { openEditor } = require('./harness');

const spaced = 'First\n\n\n\n\n\nSecond\n'; // five blank lines: two empty paragraphs
const plain = 'First\n\nSecond\n';

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const setting = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { showBlankLines: on }), on);
    await new Promise(r => setTimeout(r, 300));
  };
  // The distance from the bottom of "First" to the top of "Second".
  const gap = async (md, readOnly) => {
    await mode(true, false);
    await load(md);
    await mode(false, readOnly);
    return page.evaluate(() => {
      const blocks = [...document.querySelectorAll('#ag-editor-id > p')];
      const first = blocks.find(p => p.textContent.includes('First')).getBoundingClientRect();
      const second = blocks.find(p => p.textContent.includes('Second')).getBoundingClientRect();
      return Math.round(second.top - first.bottom);
    });
  };
  const exported = md => page.evaluate(md => {
    const muya = window.__typedownMuya;
    muya.markdown = md;
    return muya.exportHtml();
  }, md);
  try {
    const lineHeight = 16 * 1.6;
    for (const on of [false, true]) {
      await setting(on);
      const state = on ? 'on' : 'off';
      const base = await gap(plain, true);
      const reading = await gap(spaced, true);
      const editing = await gap(spaced, false);
      const editingBase = await gap(plain, false);
      console.log(`${state}: reading gap ${reading} (one blank line ${base}), editing gap ${editing} (one blank line ${editingBase})`);
      check(editing - editingBase >= 2 * lineHeight * 0.9, `${state}: while editing the two empty lines take room`);
      if (on) check(reading - base >= 2 * lineHeight * 0.9 && Math.abs(reading - editing) <= 2, `${state}: in reading mode they take the same room as while editing`);
      else check(reading === base, `${state}: in reading mode they take none (as before)`);
      const html = await exported(spaced);
      const empty = (html.match(/<p><br><\/p>/g) || []).length;
      check(on ? empty === 2 : empty === 0 && /<p><\/p>/.test(html), `${state}: the export writes them as ${on ? 'lines (<p><br></p>)' : 'collapsed paragraphs (<p></p>)'} (${empty} <p><br></p>)`);
      await mode(false, false);
      await load(spaced);
      check(await flush() === spaced, `${state}: the text handed back is the file as it was`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`FAIL: ${failures.length} problem(s)`); process.exit(1); }
  console.log('PASS: extra blank lines show in reading mode and the export with the setting on, and only then');
})().catch(e => { console.error(e); process.exit(1); });
