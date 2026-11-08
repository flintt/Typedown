// "~~" typed at a line's start for a strikethrough was auto-paired into the line "~~~~" - a code fence once the file was
// read again (opened, source mode and back, an automation write): the rest of the document became a code block, and
// the closing "```" the editor then wrote stayed at the document's end (a reader's report, 2026-11-01). Now "~" and "`"
// at a line's start get no closing mark, and a paragraph's line that reads as a fence is written with its first mark
// escaped. "```" and Enter still make a code block.
//
//   node fence-line-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail = '') => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const blocks = () => page.evaluate(() => window.__typedownMuya.contentState.blocks.map(b => b.type).join(' '));
  try {
    const typeAfterBefore = async typed => {
      await mode(true, false);
      await load('Before\n\nAfter\n');
      await mode(false, false);
      await pause(300);
      await page.evaluate(() => {
        const state = window.__typedownMuya.contentState;
        const b = state.blocks[0].children[0];
        const p = { key: b.key, offset: b.text.length };
        state.cursor = { start: p, end: p, anchor: p, focus: p }; state.render();
      });
      await pause(200);
      await page.keyboard.press('Enter');
      await pause(200);
      for (const ch of typed) {
        if (ch === '\n') await page.keyboard.press('Enter'); else await page.keyboard.type(ch);
        await pause(80);
      }
      await pause(400);
    };
    for (const [typed, expected] of [
      ['~~gone~~ ok', 'Before\n\n~~gone~~ ok\n\nAfter\n'],
      ['`code` ok', 'Before\n\n`code` ok\n\nAfter\n'],
      ['~~', 'Before\n\n~~\n\nAfter\n'],
    ]) {
      await typeAfterBefore(typed);
      const text = await flush();
      check(text === expected, `"${typed}" typed at a line's start: as typed, no mark put in`, firstDifference(expected, text));
    }
    // A paragraph whose text reads as a fence (as typing made one before): written escaped, and read back a paragraph.
    await typeAfterBefore('x');
    await page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const b = state.blocks[1].children[0];
      b.text = '~~~~';
      const p = { key: b.key, offset: 4 };
      state.cursor = { start: p, end: p, anchor: p, focus: p }; state.render();
      window.__typedownMuya.dispatchChange();
    });
    await pause(400);
    const written = await flush();
    check(written === 'Before\n\n\\~~~~\n\nAfter\n', 'a paragraph "~~~~" is written escaped', firstDifference('Before\n\n\\~~~~\n\nAfter\n', written));
    await mode(true, false);
    await mode(false, false);
    await pause(400);
    check(await blocks() === 'p p p', `source mode and back: still three paragraphs, no code block (${await blocks()})`);
    await mode(true, false);
    await load(written);
    await mode(false, false);
    await pause(400);
    check(await blocks() === 'p p p', `opened again: still three paragraphs (${await blocks()})`);
    // As before: "```" and Enter make a code block.
    await typeAfterBefore('```\n');
    check((await blocks()).includes('pre'), `"\`\`\`" and Enter make a code block (${await blocks()})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
