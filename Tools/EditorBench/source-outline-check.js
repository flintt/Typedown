// Source mode's outline (services/common.ts getTOC) and a heading clicked in it (the user, 2026-11-05: in source mode the
// outline showed, but a click did nothing - its slugs were random numbers the editor could not find, and only the visual
// editor listened for ScrollTo):
// - each heading's slug is its line; a "#" line inside a fenced code block is not a heading;
// - ScrollTo with a heading's slug puts the caret at the start of that line, the line in view near the top.
//
//   node source-outline-check.js
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const filler = n => Array.from({ length: n }, (_, i) => `Line ${i} of filler text.`).join('\n\n');
  const doc = `# First\n\n${filler(40)}\n\n\`\`\`sh\n# not a heading\necho hi\n\`\`\`\n\n## Second\n\n${filler(80)}\n\n## Third\n\n${filler(80)}\n`;
  try {
    await mode(true, false);
    await load(doc);
    await pause(800);
    const toc = await page.evaluate(() => window.__toc || []);
    const lines = doc.split('\n');
    report(JSON.stringify(toc.map(t => t.content)) === JSON.stringify(['First', 'Second', 'Third']), 'the outline: the three headings, not the code block\'s "#" line', JSON.stringify(toc));
    const third = toc.find(t => t.content === 'Third');
    const thirdLine = lines.indexOf('## Third');
    report(third && third.slug === `src:${thirdLine}`, 'a heading\'s slug is its line', JSON.stringify(third));

    await page.evaluate(slug => window.__deliver('ScrollTo', { slug }), third ? third.slug : 'src:0');
    await pause(400);
    const at = await page.evaluate(() => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      const cursor = cm.getCursor();
      const box = cm.getWrapperElement().getBoundingClientRect();
      const y = cm.cursorCoords(null, 'window').top;
      return { line: cursor.line, ch: cursor.ch, inView: y >= box.top && y <= box.top + box.height / 2, y: Math.round(y - box.top) };
    });
    report(at.line === thirdLine && at.ch === 0, 'ScrollTo: the caret at the start of the heading\'s line', JSON.stringify(at));
    report(at.inView, 'ScrollTo: the heading\'s line in view, in the upper half', JSON.stringify(at));

    // The showcase in both modes: the same outline, heading for heading (setext ones, ones in lists and quotes, "#"
    // lines in code).
    const showcase = fs.readFileSync(path.join(__dirname, 'fixtures/showcase/showcase.md'), 'utf8');
    const outline = async source => {
      await page.evaluate(() => { window.__toc = null; });
      await mode(source, false);
      await load(showcase + `\n<!-- ${source} -->\n`);
      await pause(1200);
      return page.evaluate(() => (window.__toc || []).map(t => `${t.lvl} ${t.content}`));
    };
    const visual = await outline(false);
    const sourced = await outline(true);
    const differ = visual.findIndex((t, i) => t !== sourced[i]);
    report(visual.length > 3 && visual.length === sourced.length && differ < 0, 'the showcase: the same outline in both modes',
      `visual ${visual.length}, source ${sourced.length}; first difference at ${differ}: ${JSON.stringify(visual[differ])} / ${JSON.stringify(sourced[differ])}`);
    // A long document (the outline read only once typing stops): right at once after its load, not the one before's;
    // a heading typed is in it a moment after.
    const long = `# Long\n\n${filler(2500)}\n\n## End\n`;
    await page.evaluate(() => { window.__toc = null; });
    await mode(true, false);
    await load(long);
    await pause(150);
    const loadedToc = await page.evaluate(() => (window.__toc || []).map(t => t.content));
    report(JSON.stringify(loadedToc) === JSON.stringify(['Long', 'End']), `a long document (${long.length} characters): its outline at once after the load`, JSON.stringify(loadedToc));
    await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.replaceRange('\n\n## Typed\n', { line: cm.lastLine(), ch: 0 }); });
    await pause(800);
    const typedToc = await page.evaluate(() => (window.__toc || []).map(t => t.content));
    report(JSON.stringify(typedToc) === JSON.stringify(['Long', 'End', 'Typed']), 'a heading typed in it: in the outline once typing stops', JSON.stringify(typedToc));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
