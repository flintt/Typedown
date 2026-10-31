// While a long document is drawn a part at a time, the room kept for the rest (StateRender.estimateRest, written every
// 100 ms, its fraction of a pixel almost every slice) is the height of the root's ::before and ::after, written into a
// style sheet's rules for them (parser/render/roomStyle.js) - not on the root: as plain custom properties there every
// element of the document was restyled each time (four fifths of the time drawing a 1.5 MB document of HTML-heavy
// blocks), and as properties registered as not inherited still every block under the root, 35-60 ms a write on 2,900
// blocks (LT01: slices of 80-250 ms, frames 200-300 ms apart).
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
      return {
        drawing: !!state,
        rootStyle: root.getAttribute('style') || '',
        after: getComputedStyle(root, '::after').height,
        before: getComputedStyle(root, '::before').height,
      };
    });
    console.log(JSON.stringify(r));
    check(r.drawing, 'the document is still being drawn (the case this checks)');
    check(parseFloat(r.after) > 1000, `the room below is the root's ::after's height (${r.after})`);
    check(!/td-room|height|padding/.test(r.rootStyle), `the room is not written on the root's own style ("${r.rootStyle}")`);
    // Written again (a slice's estimate), and the fraction of a pixel (keepInPlace): still nothing on the root.
    const again = await page.evaluate(() => {
      const sr = window.__typedownMuya.contentState.stateRender; const state = sr.progressive;
      sr.estimateRest(state);
      const keep = sr.keepInPlace(state, state.root.firstElementChild); window.scrollBy(0, 0); keep();
      return { rootStyle: state.root.getAttribute('style') || '', after: getComputedStyle(state.root, '::after').height };
    });
    check(!/td-room|height|padding/.test(again.rootStyle) && parseFloat(again.after) > 1000, `written again, still on the ::after (${again.after}), not on the root ("${again.rootStyle}")`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
