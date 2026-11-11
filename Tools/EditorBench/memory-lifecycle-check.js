// Memory the editor keeps after it is done with something (the audit's 9 and 28; the store's two Memory failures on
// 1.3.9.0, 2026-11-10):
// - visual and source mode switched back and forth on a long document with no footnotes: one footnote tool and one of
//   each floating tool on the page, and the JS heap after a GC not growing round after round (each switch left a
//   footnote tool and about 4.7 MB behind, fixed in 34a8341e);
// - 25 documents with 20 formulas each, then one with none: no formula kept (500 were, 10.9 MB of a 22.6 MB heap).
// Documents kept for their tab coming back are dropped before each measure, as they are a bounded cache of their own.
//
//   node memory-lifecycle-check.js
const { openEditor } = require('./harness');

const ROUNDS = 6;
// Heap growth allowed from the second round to the last, all of it (one round leaked about 4.7 MB).
const GROWTH_MAX = 2.5e6;

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor({ browserArgs: ['--js-flags=--expose-gc'] });
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${seen === undefined ? '' : ': ' + seen}`); if (!ok) failures.push(what); };
  const cdp = await page.target().createCDPSession();
  const heap = async () => {
    await page.evaluate(() => window.__typedownKeptDocuments?.clear());
    for (let i = 0; i < 3; i++) { await cdp.send('HeapProfiler.collectGarbage'); await pause(200); }
    return (await cdp.send('Runtime.getHeapUsage')).usedSize;
  };
  try {
    // About 160,000 characters of ordinary writing: headings, paragraphs with emphasis and links, lists.
    const section = i => `## Section ${i}\n\nA paragraph with **bold**, *emphasis* and a [link](https://example.com/${i}) in it, long enough to wrap once or twice in the editor window at its usual width.\n\n- one item ${i}\n- another item ${i}\n\n`;
    const long = Array.from({ length: 640 }, (_, i) => section(i)).join('');
    await mode(false, false);
    await load(long);
    await pause(1500);
    // The floating tools (each its own wrapper at the body's end): as many after the switches as after the first.
    const floats = () => page.evaluate(() => ({
      footnote: document.querySelectorAll('.ag-footnote-tool').length,
      wrappers: document.querySelectorAll('body > .ag-float-wrapper').length,
    }));
    const first = await floats();
    const heaps = [];
    for (let round = 0; round < ROUNDS; round++) {
      await mode(true, false);
      await mode(false, false);
      await pause(800);
      heaps.push(await heap());
    }
    const tools = await floats();
    console.log(`  long document ${long.length} chars; heap after each round (MB): ${heaps.map(h => (h / 1e6).toFixed(1)).join(' ')}`);
    check(tools.footnote <= 1, 'one footnote tool on the page after the switches', tools.footnote);
    check(tools.wrappers === first.wrappers, 'as many floating tools as before the switches', `${tools.wrappers} (${first.wrappers} before)`);
    const growth = heaps[heaps.length - 1] - heaps[1];
    check(growth < GROWTH_MAX, `the heap does not grow round after round (+${(growth / 1e6).toFixed(1)} MB over ${ROUNDS - 2} rounds)`);

    // Formulas: 25 documents of 20 different ones each, then a document with none.
    for (let d = 0; d < 25; d++) {
      const text = Array.from({ length: 20 }, (_, i) => `Formula ${i}: $x_{${d}}^{${i}} + \\frac{${d}}{${i + 1}}$\n\n$$\n\\sum_{k=0}^{${d * 20 + i}} k^2\n$$\n`).join('\n');
      await load(text);
      await pause(150);
    }
    const before = await page.evaluate(() => window.__typedownMuya.contentState.stateRender.loadMathMap.size);
    await load('# No formulas\n\nJust text.\n');
    await pause(300);
    const after = await page.evaluate(() => window.__typedownMuya.contentState.stateRender.loadMathMap.size);
    const mathHeap = await heap();
    console.log(`  formulas kept: ${before} with the last formula document shown, ${after} after one with none; heap ${(mathHeap / 1e6).toFixed(1)} MB`);
    check(before > 0 && before <= 40, 'with a formula document shown, its own formulas are kept', before);
    check(after === 0, 'none kept once a document with no formulas is shown', after);
    if (errors.length) failures.push(`page errors: ${errors.slice(0, 3).join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
