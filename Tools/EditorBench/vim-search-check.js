// Vim's search in source mode (/ and a word): what is typed stays in sight at the bottom of the window while the
// search moves the page to the first match (incremental search), and Enter leaves the cursor on the match. The command
// line was at the bottom of the editor - the end of the document - and in a long document (the showcase) the letters
// typed went out of sight as soon as the page moved.
//
//   node vim-search-check.js
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const md = fs.readFileSync(path.join(__dirname, 'fixtures', 'showcase', 'showcase.md'), 'utf8');
  try {
    await page.evaluate(() => window.__deliver('SettingsChanged', { vimMode: true }));
    await mode(true, false);
    await load(md);
    await new Promise(r => setTimeout(r, 1500));
    await page.click('.CodeMirror');
    await page.keyboard.press('Escape');
    await page.keyboard.press('/');
    await new Promise(r => setTimeout(r, 300));
    const line = () => page.evaluate(() => {
      const d = document.querySelector('.CodeMirror-dialog');
      const i = d && d.querySelector('input');
      const r = d && d.getBoundingClientRect();
      return { value: i ? i.value : null, top: r ? Math.round(r.top) : null, bottom: r ? Math.round(r.bottom) : null, height: innerHeight, scrollY: Math.round(scrollY) };
    });
    let typed = '';
    let moved = false;
    for (const ch of 'mermaid') {
      await page.keyboard.type(ch);
      typed += ch;
      await new Promise(r => setTimeout(r, 150));
      const l = await line();
      moved = moved || l.scrollY > 0;
      console.log(`${typed}: ${JSON.stringify(l)}`);
      check(l.value === typed && l.top !== null && l.top >= 0 && l.bottom <= l.height, `after "${typed}" the command line shows it, in the window`);
    }
    check(moved, 'the page moved to the first match while typing (incremental search)');
    await page.keyboard.press('Enter');
    await new Promise(r => setTimeout(r, 300));
    const at = await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; const c = cm.getCursor(); return cm.getLine(c.line).slice(c.ch, c.ch + 7).toLowerCase(); });
    check(at === 'mermaid', `Enter leaves the cursor on the match (${at})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
