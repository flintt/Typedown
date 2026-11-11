// Source mode's Replace on the text as it is, and its undo history per document (the audit's E01, E02):
// - Replace All twice: "cat cat END" with cat -> X is "X X END", and the second time changes nothing (it cut the text at
//   the first search's positions, "X X");
// - a word typed before the matches after the search, then Replace: the match is replaced, not the text where it was;
// - Replace (the current one) twice: each time the next match;
// - document A loaded, then B: B's undo history is empty (Vim's u turned B into A);
// - a regular expression's groups in a replace, and one that backtracks for minutes not holding the page (P07), in both
//   editors.
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

    // A regular expression that backtracks for minutes on a line (the audit's P07): the page goes on (the search was
    // tried in a worker and given up), and finds nothing; a quick one after it finds its matches.
    const marks = () => page.evaluate(() => document.querySelectorAll('.ag-highlight, .ag-selection').length);
    const answers = async ms => Promise.race([page.evaluate(() => new Promise(r => setTimeout(() => r(true), 0))), new Promise(r => setTimeout(() => r(false), ms))]);
    for (const source of [true, false]) {
      const name = source ? 'source' : 'visual';
      await mode(source, false);
      await load('a'.repeat(40) + '!\n\nbaaa\n');
      await pause(300);
      await send('Search', { value: `(a+)+$${source ? '' : ' '}`, opt: { ...regexp, selection: {} } });
      check(await answers(1000), `a slow regular expression does not hold the page (${name} mode)`, 'no answer in 1 s');
      await pause(2500);
      check(await answers(1000), `nor after it was given up (${name} mode)`, 'no answer in 1 s');
      check((await marks()) === 0, `a slow regular expression finds nothing (${name} mode)`, await marks());
      await send('Search', { value: `b?a{3}${source ? '' : '(?:)'}`, opt: regexp });
      await pause(400);
      const found = await marks();
      check(found > 0, `a quick one after it finds its matches (${name} mode)`, found);
    }

    // The same expression through on a short text, then on one where it backtracks (the 2026-11-11 recheck: the
    // verdict was by expression alone, and the page hung on the second text); and a replace on a text edited into one
    // since the search.
    for (const source of [true, false]) {
      const name = source ? 'source' : 'visual';
      const slow = `(b+)+$${source ? '' : ' '}`;
      await mode(source, false);
      await load('bbb\n');
      await pause(300);
      await send('Search', { value: slow, opt: { ...regexp, selection: {} } });
      await pause(500);
      await load('b'.repeat(40) + '!\n');
      await pause(300);
      await send('Search', { value: slow, opt: { ...regexp, selection: {} } });
      check(await answers(1000), `an expression through on a short text does not hold the page on a long one (${name} mode)`, 'no answer in 1 s');
      await pause(2500);
      check(await answers(1000), `nor after it was given up (${name} mode)`, 'no answer in 1 s');

      const edited = `(c+)+$${source ? '' : ' '}`;
      await load('ccc\n');
      await pause(300);
      await send('Search', { value: edited, opt: { ...regexp, selection: {} } });
      await pause(500);
      if (source) await cm(() => { const e = document.querySelector('.CodeMirror').CodeMirror; e.setValue('c'.repeat(40) + '!\n'); });
      else await page.evaluate(() => { const s = window.__typedownMuya.contentState; const b = s.blocks[0].children[0]; b.text = 'c'.repeat(40) + '!'; s.render(); });
      await pause(300);
      await send('Replace', { value: 'X', opt: { ...regexp, isSingle: false } });
      check(await answers(1000), `a replace on a text edited since the search does not hold the page (${name} mode)`, 'no answer in 1 s');
      await pause(2500);
      check(await answers(1000), `nor after it was given up (${name} mode)`, 'no answer in 1 s');
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
