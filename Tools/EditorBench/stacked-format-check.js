// A word carrying several formats at once - bold and italic, bold and underlined, struck through and bold - and a format
// button pressed there (the context menu's, the Format menu's or its shortcut: all are the host's Format): the format
// pressed comes off, whichever of them it is, with the caret in the word or the word selected; pressed again on the
// selection it comes back. Only the outermost one came off: the inner one was taken out of its parent's children, but
// the text was put back together from the outer tokens' raw text, so nothing changed (the user, 2026-11-14). Clear
// format took the outer one alone the same way. `_**x**_` was not read as formatted at all: the italic's text may not
// end in "*", a rule meant for "*" marks only.
//
//   node stacked-format-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  try {
    const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(300); };
    // The caret at `at` in the first paragraph's text, or the text from..to selected.
    const place = async (from, to) => {
      await page.evaluate((from, to) => {
        const state = window.__typedownMuya.contentState;
        let span; (function walk(bs) { for (const b of bs) { if (b.type === 'span' && !span) span = b; if (b.children) walk(b.children); } })(state.blocks);
        const start = { key: span.key, offset: from }, end = { key: span.key, offset: to };
        state.cursor = { start, end, anchor: start, focus: end }; state.render();
      }, from, to);
      await pause(200);
    };
    const format = async type => { await page.evaluate(f => window.__deliver('Format', f), type); await pause(300); };
    // The formats where the caret is, as the menu shows them (strong, em, u, del), and the text selected.
    const where = () => page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const { start, end } = state.cursor;
      const block = state.getBlock(start.key);
      const formats = state.selectionFormats({ start: { ...start }, end: { ...end } }).formats.map(t => t.type === 'html_tag' ? t.tag : t.type);
      return { formats: [...new Set(formats)].sort().join('+'), selected: block.text.slice(start.offset, end.offset), offset: start.offset, text: block.text };
    });
    const markdown = () => page.evaluate(() => window.__typedownMuya.getMarkdown());

    // [document, format pressed, the document after, the formats left]
    const cases = [
      ['***xyz***', 'strong', '*xyz*', 'em'],
      ['***xyz***', 'em', '**xyz**', 'strong'],
      ['**_xyz_**', 'em', '**xyz**', 'strong'],
      ['**_xyz_**', 'strong', '_xyz_', 'em'],
      ['_**xyz**_', 'strong', '_xyz_', 'em'],
      ['_**xyz**_', 'em', '**xyz**', 'strong'],
      ['*__xyz__*', 'strong', '*xyz*', 'em'],
      ['<u>**xyz**</u>', 'strong', '<u>xyz</u>', 'u'],
      ['<u>**xyz**</u>', 'u', '**xyz**', 'strong'],
      ['**<u>xyz</u>**', 'u', '**xyz**', 'strong'],
      ['~~**xyz**~~', 'strong', '~~xyz~~', 'del'],
      ['**~~xyz~~**', 'del', '**xyz**', 'strong'],
      ['***<u>xyz</u>***', 'strong', '*<u>xyz</u>*', 'em+u'],
      ['***<u>xyz</u>***', 'em', '**<u>xyz</u>**', 'strong+u'],
      ['***<u>xyz</u>***', 'u', '***xyz***', 'em+strong'],
      ['<u>***xyz***</u>', 'em', '<u>**xyz**</u>', 'strong+u'],
      ['<u>***xyz***</u>', 'strong', '<u>*xyz*</u>', 'em+u'],
      ['~~***xyz***~~', 'em', '~~**xyz**~~', 'del+strong'],
    ];
    for (const [stacked, type, expected, left] of cases) {
      const before = `Some ${stacked} here\n`, after = `Some ${expected} here\n`;
      const at = before.indexOf('xyz');

      // The caret inside the word.
      await visual(before);
      await place(at + 1, at + 1);
      const shown = await where();
      report(shown.formats.split('+').includes(type), `${stacked}, caret: ${type} shown on`, shown.formats);
      await format(type);
      let held = await markdown();
      report(held === after, `${stacked}, caret: ${type} comes off`, firstDifference(after, held));
      let now = await where();
      report(now.formats === left && now.text.slice(now.offset - 1, now.offset + 2) === 'xyz', `${stacked}, caret: ${left} left, the caret still after "x"`, JSON.stringify(now));
      const host = await flush();
      report(host === held, `${stacked}, caret: the host has it`, firstDifference(held, host));

      // The word selected: off, and on again with a second press.
      await visual(before);
      await place(at, at + 3);
      await format(type);
      held = await markdown();
      report(held === after, `${stacked}, selected: ${type} comes off`, firstDifference(after, held));
      now = await where();
      report(now.formats === left && now.selected === 'xyz', `${stacked}, selected: ${left} left, "xyz" still selected`, JSON.stringify(now));
      await format(type);
      now = await where();
      const all = [...new Set([...left.split('+'), type])].sort().join('+');
      report(now.formats === all && now.selected === 'xyz', `${stacked}, selected: pressed again ${type} is back`, JSON.stringify(now));
    }

    // Clear format takes every format off the word, not only the outermost one.
    for (const stacked of ['***xyz***', '<u>**xyz**</u>', '~~***xyz***~~']) {
      const before = `Some ${stacked} here\n`;
      const at = before.indexOf('xyz');
      await visual(before);
      await place(at, at + 3);
      await format('clear');
      const held = await markdown();
      report(held === 'Some xyz here\n', `${stacked}: clear format takes them all off`, firstDifference('Some xyz here\n', held));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
