// The editing keys and the host's editing commands, each in each kind of block at its start, middle and end, an X
// typed after to show where the caret went. Two things are checked for each:
//  - the host has what the editor holds right after (the page tells every change; Tab and Format did not), and the
//    page throws nothing (Paragraph > Duplicate threw in 1.4.1);
//  - what the document became is what key-matrix-known.json says it becomes. A difference is a change of behaviour:
//    look at it, and if it is meant, `--update` writes the new results. The table was read through once, by a person,
//    when it was first made (2026-11-01).
// The editor's key handling is MarkText's Muya of 2022, and a key does something of its own in each kind of block and
// place; reports came one combination at a time. This goes over all of them.
//
//   node key-matrix-check.js [--update] [--only <context>]
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

const KNOWN = path.join(__dirname, 'key-matrix-known.json');
const update = process.argv.includes('--update');
const only = process.argv.includes('--only') ? process.argv[process.argv.indexOf('--only') + 1] : null;

// [name, document, the text block's start, the offset of "the middle"]
const contexts = [
  ['paragraph', 'Before\n\nSome **bold** text here\n\nAfter\n', 'Some', 8],
  ['heading', 'Before\n\n## Heading **bold** text\n\nAfter\n', '## Heading', 13],
  ['list item', '- one\n- Some **bold** text\n- three\n', 'Some', 8],
  ['nested item', '- one\n    - Some **bold** text\n- three\n', 'Some', 8],
  ['task item', '- [ ] one\n- [ ] Some **bold** text\n- [ ] three\n', 'Some', 8],
  ['quote', 'Before\n\n> Some **bold** text\n\nAfter\n', 'Some', 8],
  ['table cell', '| a | b |\n| --- | --- |\n| Some **bold** | x |\n', 'Some', 8],
  ['code block', 'Before\n\n```\nline one\nline two\n```\n', 'line one', 4],
];
const keys = ['Enter', 'Shift+Enter', 'Backspace', 'Delete', 'Tab', 'Shift+Tab', 'ArrowUp', 'ArrowDown'];
const commands = [
  ['Format strong', 'Format', 'strong'],
  ['heading 2', 'UpdateParagraph', 'heading 2'],
  ['bullet list', 'UpdateParagraph', 'ul-bullet'],
  ['quote', 'UpdateParagraph', 'blockquote'],
  ['Duplicate', 'Duplicate', null],
  ['DeleteParagraph', 'DeleteParagraph', null],
  ['InsertParagraph after', 'InsertParagraph', 'after'],
];

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const known = fs.existsSync(KNOWN) ? JSON.parse(fs.readFileSync(KNOWN, 'utf8')) : {};
  const results = update ? { ...known } : {};
  const failures = [];
  let changed = 0;
  try {
    const caretAt = async (starts, where, middle) => page.evaluate((starts, where, middle) => {
      const state = window.__typedownMuya.contentState;
      const find = bs => { for (const b of bs) { if (b.type === 'span' && b.text.startsWith(starts)) return b; const f = b.children && find(b.children); if (f) return f; } };
      const b = find(state.blocks);
      const marks = b.functionType === 'atxLine' ? (/^ {0,3}#{1,6}[ \t]+/.exec(b.text) || [''])[0].length : 0;
      const offset = where === 'start' ? marks : where === 'end' ? b.text.length : middle;
      const p = { key: b.key, offset };
      state.cursor = { start: p, end: p, anchor: p, focus: p };
      state.render();
    }, starts, where, middle);
    // Visual mode once; each case loads its document there (a switch to source mode and back for each of 360 cases
    // took most of the time).
    await mode(false, false);
    for (const [context, doc, starts, middle] of contexts) {
      if (only && context !== only) continue;
      for (const where of ['start', 'middle', 'end']) {
        for (const op of [...keys, ...commands]) {
          const name = Array.isArray(op) ? op[0] : op;
          const id = `${context} | ${where} | ${name}`;
          await load(doc);
          await caretAt(starts, where, middle);
          await pause(150);
          const errorsBefore = errors.length;
          if (Array.isArray(op)) {
            await page.evaluate((message, args) => window.__deliver(message, args), op[1], op[2]);
          } else {
            const parts = op.split('+');
            for (const p of parts.slice(0, -1)) await page.keyboard.down(p);
            await page.keyboard.press(parts[parts.length - 1]);
            for (const p of parts.slice(0, -1).reverse()) await page.keyboard.up(p);
          }
          await pause(250);
          // What the host has right after, against what it has once the page is made to tell it the editor's state
          // (Muya.dispatchChange, as an edit typed does): different, and the key or command left the host behind. The
          // editor's own export is not the yardstick - the page keeps the reader's source where nothing was edited.
          const told = async () => {
            const before = await flush();
            await page.evaluate(() => window.__typedownMuya.dispatchChange());
            await pause(150);
            return [before, await flush()];
          };
          const [hostAfter, toldAfter] = await told();
          await page.keyboard.type('X');
          await pause(250);
          const [host, toldX] = await told();
          const held = host;
          const problems = [];
          if (hostAfter !== toldAfter) problems.push('the host did not get the change');
          if (host !== toldX) problems.push('the host did not get the X');
          if (errors.length > errorsBefore) problems.push('the page threw: ' + errors.slice(errorsBefore).join(' | ').slice(0, 160));
          if (problems.length) failures.push(`${id}: ${problems.join('; ')}`);
          results[id] = held;
          if (!update && id in known && known[id] !== held) {
            changed++;
            failures.push(`${id}: became ${JSON.stringify(held)}, known ${JSON.stringify(known[id])}`);
          }
        }
      }
      console.log(`  ${context}: done`);
    }
  } finally {
    await close();
  }
  if (update) {
    const sorted = Object.fromEntries(Object.keys(results).sort().map(k => [k, results[k]]));
    fs.writeFileSync(KNOWN, JSON.stringify(sorted, null, 1) + '\n');
    console.log(`wrote ${Object.keys(sorted).length} results to ${path.basename(KNOWN)}`);
  }
  const missing = Object.keys(results).filter(id => !(id in known)).length;
  if (!update && missing) failures.push(`${missing} combinations not in ${path.basename(KNOWN)} (run with --update once read)`);
  for (const f of failures) console.log('  FAIL ' + f);
  console.log(failures.length ? `${failures.length} FAILED${changed ? ` (${changed} changed from the known results)` : ''}` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
