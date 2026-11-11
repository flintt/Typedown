// Source mode's Replace on the text as it is, and its undo history per document (the audit's E01, E02):
// - Replace All twice: "cat cat END" with cat -> X is "X X END", and the second time changes nothing (it cut the text at
//   the first search's positions, "X X");
// - a word typed before the matches after the search, then Replace: the match is replaced, not the text where it was;
// - Replace (the current one) twice: each time the next match;
// - document A loaded, then B: B's undo history is empty (Vim's u turned B into A).
//
//   node source-replace-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  // As the host sends it: the search's options and the selection it starts from (none here).
  const opt = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: {} };
  const send = (name, args) => page.evaluate((n, a) => window.__deliver(n, a), name, args);
  const text = async () => (await flush()).replace(/\r\n/g, '\n');
  const cm = fn => page.evaluate(fn);
  try {
    await mode(true, false);

    await load('cat cat END\n');
    await pause(300);
    await send('Search', { value: 'cat', opt });
    await pause(100);
    await send('Replace', { value: 'X', opt: { ...opt, isSingle: false } });
    await pause(200);
    let t = await text();
    check(t === 'X X END\n', 'Replace All', JSON.stringify(t));
    await send('Replace', { value: 'X', opt: { ...opt, isSingle: false } });
    await pause(200);
    t = await text();
    check(t === 'X X END\n', 'Replace All again changes nothing', JSON.stringify(t));

    await load('one cat two cat\n');
    await pause(300);
    await send('Search', { value: 'cat', opt });
    await pause(100);
    // A word typed at the start after the search: the matches moved.
    await cm(() => { const e = document.querySelector('.CodeMirror').CodeMirror; e.replaceRange('NEW ', { line: 0, ch: 0 }); e.setCursor({ line: 0, ch: 0 }); });
    await pause(200);
    await send('Replace', { value: 'dog', opt: { ...opt, isSingle: true } });
    await pause(200);
    t = await text();
    check(t === 'NEW one dog two cat\n', 'Replace after typing before the matches', JSON.stringify(t));
    await send('Replace', { value: 'dog', opt: { ...opt, isSingle: true } });
    await pause(200);
    t = await text();
    check(t === 'NEW one dog two dog\n', 'Replace again takes the next match', JSON.stringify(t));

    // A regular expression's groups, each match its own, in both editors (the audit's E06: $2$1 went in as it was).
    const regexp = { ...opt, searchIsRegexp: true };
    for (const source of [true, false]) {
      await mode(source, false);
      await load('a1 b2 c3\n');
      await pause(300);
      await send('Search', { value: '([a-z])(\\d)', opt: regexp });
      await pause(150);
      await send('Replace', { value: '$2$1', opt: { ...regexp, isSingle: false } });
      await pause(300);
      t = await text();
      check(t === '1a 2b 3c\n', `regular expression groups (${source ? 'source' : 'visual'} mode)`, JSON.stringify(t));
    }
    await mode(true, false);

    await load('document A\n');
    await pause(300);
    await load('document B\n');
    await pause(300);
    const history = await cm(() => document.querySelector('.CodeMirror').CodeMirror.historySize());
    check(history.undo === 0, "another document's undo history is its own", JSON.stringify(history));
    await cm(() => document.querySelector('.CodeMirror').CodeMirror.undo());
    await pause(200);
    t = await text();
    check(t === 'document B\n', 'an undo after the load leaves B as it is', JSON.stringify(t));
  } finally {
    await close();
  }
  if (errors.length) { console.log('page errors:', errors.slice(0, 3)); failures.push('page errors'); }
  if (failures.length) { console.log(`${failures.length} FAILED`); process.exit(1); }
  console.log('all passed');
})();
