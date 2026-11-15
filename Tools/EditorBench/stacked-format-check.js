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
    // The marks selected too: Ctrl+A in the paragraph, or a selection dragged past the word's ends. What is pressed is
    // what every letter selected has (the marks are not letters): all the word's formats. Only the outermost showed,
    // its range alone holding the marks (the user, 2026-11-15); pressing an inner one then put it back where it was
    // or moved it outside. The press takes off a format shown pressed, from the whole selection, leaving the others;
    // a second press puts it back, and the word then has them all again.
    const formatsAt = (from, to) => page.evaluate((from, to) => {
      const state = window.__typedownMuya.contentState;
      let span; (function walk(bs) { for (const b of bs) { if (b.type === 'span' && !span) span = b; if (b.children) walk(b.children); } })(state.blocks);
      const formats = state.selectionFormats({ start: { key: span.key, offset: from }, end: { key: span.key, offset: to } }).formats.map(t => t.type === 'html_tag' ? t.tag : t.type);
      return [...new Set(formats)].sort().join('+');
    }, from, to);
    const selectAll = async () => { await page.keyboard.down('Control'); await page.keyboard.press('KeyA'); await page.keyboard.up('Control'); await pause(300); };
    // [stack, the formats it has]
    const stacks = [
      ['***xyz***', 'em+strong'],
      ['**_xyz_**', 'em+strong'],
      ['<u>**xyz**</u>', 'strong+u'],
      ['**<u>xyz</u>**', 'strong+u'],
      ['~~**xyz**~~', 'del+strong'],
      ['***<u>xyz</u>***', 'em+strong+u'],
      ['<u>***xyz***</u>', 'em+strong+u'],
      ['~~***xyz***~~', 'del+em+strong'],
    ];
    for (const [stacked, all] of stacks) {
      // Ctrl+A in a paragraph of the word alone: the menu shows them all.
      await visual(`${stacked}\n`);
      await place(stacked.indexOf('xyz') + 1, stacked.indexOf('xyz') + 1);
      await selectAll();
      let now = await where();
      report(now.selected === stacked && now.formats === all, `${stacked}, Ctrl+A: ${all} shown on`, JSON.stringify(now));

      // In a sentence, the marks dragged in at the start, at the end or both; with a letter or a space of the
      // sentence too, none of them.
      const sentence = `Some ${stacked} here`;
      const from = sentence.indexOf(stacked), to = from + stacked.length;
      const at = sentence.indexOf('xyz');
      await visual(`${sentence}\n`);
      for (const [a, b, expected, what] of [
        [from, to, all, 'the marks at both ends'],
        [from, at + 3, all, 'the marks at the start'],
        [at, to, all, 'the marks at the end'],
        [from + 1, to - 1, all, 'some of the marks at both ends'],
        [from - 1, to, '', 'the space before too'],
        [from, to + 1, '', 'the space after too'],
        [0, sentence.length, '', 'the whole sentence'],
      ]) {
        const shown = await formatsAt(a, b);
        report(shown === expected, `${stacked}, ${what}: ${expected || 'none'} shown on`, shown || 'none');
      }

      // Each format pressed after Ctrl+A: off, the others kept, the word still selected; pressed again, back.
      for (const type of all.split('+')) {
        const left = all.split('+').filter(t => t !== type).join('+');
        await visual(`${stacked}\n`);
        await place(stacked.indexOf('xyz') + 1, stacked.indexOf('xyz') + 1);
        await selectAll();
        await format(type);
        let held = await markdown();
        now = await where();
        const word = held.trim();
        const xyz = word.indexOf('xyz');
        const atWord = await formatsAt(xyz + 1, xyz + 1);
        report(atWord === left && !/\*{4}|_{3}|~{3}/.test(word), `${stacked}, Ctrl+A, ${type} pressed: ${left} left`, `${JSON.stringify(held)} has ${atWord || 'none'}`);
        report(now.selected.includes('xyz') && now.formats === left, `${stacked}, Ctrl+A, ${type} pressed: the word still selected, ${left} shown on`, JSON.stringify(now));
        const host = await flush();
        report(host === held, `${stacked}, Ctrl+A, ${type} pressed: the host has it`, firstDifference(held, host));
        await format(type);
        held = await markdown();
        now = await where();
        const again = held.trim();
        const xyz2 = again.indexOf('xyz');
        const back = await formatsAt(xyz2 + 1, xyz2 + 1);
        report(back === all && !/\*{4}|_{3}|~{3}/.test(again) && now.formats === all && now.selected.includes('xyz'),
          `${stacked}, Ctrl+A, ${type} pressed twice: ${all} again, shown on`, `${JSON.stringify(held)} has ${back || 'none'}, ${JSON.stringify(now)}`);
      }
    }

    // A format not on the word, pressed after Ctrl+A, goes on once (around it or inside the others), not doubled.
    for (const [stacked, type, all] of [['***xyz***', 'u', 'em+strong+u'], ['<u>**xyz**</u>', 'em', 'em+strong+u'], ['~~**xyz**~~', 'em', 'del+em+strong']]) {
      await visual(`${stacked}\n`);
      await place(stacked.indexOf('xyz') + 1, stacked.indexOf('xyz') + 1);
      await selectAll();
      await format(type);
      const held = (await markdown()).trim();
      const back = await formatsAt(held.indexOf('xyz') + 1, held.indexOf('xyz') + 1);
      const now = await where();
      report(back === all && !/\*{4}|_{3}|~{3}/.test(held) && now.formats === all, `${stacked}, Ctrl+A, ${type} pressed: ${all}`, `${JSON.stringify(held)} has ${back || 'none'}, ${JSON.stringify(now)}`);
    }

    // Plain and formatted text together, Ctrl+A: none shown on.
    for (const mixed of ['a ***xyz*** b', '***xyz*** b', 'a <u>**xyz**</u>']) {
      await visual(`${mixed}\n`);
      await place(mixed.indexOf('xyz') + 1, mixed.indexOf('xyz') + 1);
      await selectAll();
      const now = await where();
      report(now.selected === mixed && now.formats === '', `${mixed}, Ctrl+A: none shown on`, JSON.stringify(now));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
