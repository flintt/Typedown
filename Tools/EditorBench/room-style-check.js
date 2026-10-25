// While a long document is drawn a part at a time, the room kept for the rest (StateRender.estimateRest, written on the
// root every 100 ms) is the height of the root's ::before and ::after, and nothing under the root inherits it: as plain
// custom properties every element of the document was restyled each time it was written - four fifths of the time
// drawing a 1.5 MB document of HTML-heavy blocks.
//
//   node room-style-check.js
const { openEditor } = require('./harness');

const block = i => `<div style="margin:0 auto;width:600px"><p style="font-size:14px">Part ${i}: <b>HTML</b> <a href="http://example.com/${i}">link</a>.</p></div>\n\n` + `Paragraph ${i} with some words in it to wrap a little further than one line, maybe.\n\n`;
const doc = '# Room\n\n' + Array.from({ length: 3000 }, (_, i) => block(i + 1)).join('');

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await mode(false, false);
    await page.evaluate(() => { window.__typedownProgressiveDelayMs = 150; window.__typedownProgressiveSliceMs = 1; });
    await load(doc);
    await new Promise(r => setTimeout(r, 1500));
    const r = await page.evaluate(() => {
      const root = document.getElementById('ag-editor-id');
      const state = window.__typedownMuya.contentState.stateRender.progressive;
      const child = root.querySelector('p');
      return {
        drawing: !!state,
        below: root.style.getPropertyValue('--td-room-below'),
        after: getComputedStyle(root, '::after').height,
        child: getComputedStyle(child).getPropertyValue('--td-room-below').trim(),
      };
    });
    console.log(JSON.stringify(r));
    check(r.drawing, 'the document is still being drawn (the case this checks)');
    check(parseFloat(r.below) > 1000 && Math.abs(parseFloat(r.after) - parseFloat(r.below)) < 1, `the room below is the ::after's height (${r.below} / ${r.after})`);
    check(r.child === '0px', `a block under the root does not inherit it (${r.child || 'empty'})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
