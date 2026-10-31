// The find bar's results list ("Search in document" in the side pane), the page's half (services/findResults.ts,
// services/findPreview.ts): the host opens the list (FindResultsOpen), the page sends the bar's matches as previews
// (FindResults: the exact count, a line of plain text around each of the first 500 with the match's place in it) and
// where the bar is (FindState); an entry chosen (FindActivate) selects exactly that match and makes it the bar's current
// one - in visual, source and reading mode, and in a long document (gen-lt01.js) where the match is far below and not
// drawn yet, within LT01's limits. Edits and a new document send the list again.
//
//   node find-list-check.js [--only previews,cap,activate,modes,edit,long]
'use strict';
const { openEditor } = require('./harness');
const { generate } = require('./gen-lt01');

const args = process.argv.slice(2);
const only = (args.includes('--only') ? args[args.indexOf('--only') + 1] : 'previews,cap,activate,modes,edit,long').split(',');
const LIMIT = { answer: 1000, activate: 1000, gap: 200 };

// Matches of "kiwi" (any case) in a paragraph (3: bold, a link's text, a link's address), a heading, a list (2), a table
// (2) and a code block (2): 10.
const FIXTURE = [
  '# Kiwi heading here',
  '',
  'A paragraph with **kiwi** in bold and a [kiwi link](http://x.y/kiwi).',
  '',
  '- list item kiwi one',
  '- another `kiwi` code span',
  '',
  '| col | kiwi col |',
  '|-----|----------|',
  '| a kiwi cell | b |',
  '',
  '```js',
  'const kiwi = 1; // **kiwi**',
  '```',
  '',
].join('\n');

// What each entry must read (the match in brackets), in the document's order.
const EXPECTED = [
  '[Kiwi] heading here',
  'A paragraph with [kiwi] in bold and a kiwi link.',
  'A paragraph with kiwi in bold and a [kiwi] link.',
  null, // the address: shown as written, the match in it marked (checked below)
  'list item [kiwi] one',
  'another [kiwi] code span',
  '[kiwi] col',
  'a [kiwi] cell',
  'const [kiwi] = 1; // **kiwi**',
  'const kiwi = 1; // **[kiwi]**',
];

const OPT = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: { anchor: null, focus: null } };

(async () => {
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const { page, errors, mode, load, close, pause } = await openEditor();
  try {
    await page.setViewport({ width: 1280, height: 900 });
    // Every list the page sends (FindResults goes as a plain message, FindState as a diff the harness puts together).
    await page.evaluate(() => {
      const send = window.chrome.webview.postMessage;
      window.__lists = [];
      window.chrome.webview.postMessage = raw => {
        send(raw);
        const m = JSON.parse(raw);
        if (m.type === 'message' && m.name === 'FindResults') window.__lists.push({ at: performance.now(), ...m.args });
      };
    });
    const lists = () => page.evaluate(() => window.__lists);
    const lastList = () => page.evaluate(() => window.__lists[window.__lists.length - 1] || null);
    const state = () => page.evaluate(() => window.__last.FindState || null);
    /** The next list sent after `count` lists, once `test` holds for it. */
    const nextList = async (count, test = () => true, timeout = 5000) => {
      const started = Date.now();
      while (Date.now() - started < timeout) {
        const all = await lists();
        const found = all.slice(count).reverse().find(test);
        if (found) return found;
        await pause(30);
      }
      return null;
    };
    const search = (value, opt = OPT) => page.evaluate((value, opt) => window.__deliver('Search', { value, opt }), value, opt);
    const openBar = () => page.evaluate(() => window.__deliver('SearchOpenChange', { open: 1 }));
    const openList = open => page.evaluate(open => window.__deliver('FindResultsOpen', { open }), open);
    const activate = (index, version) => page.evaluate((index, version) => window.__deliver('FindActivate', { index, version }), index, version);
    const show = e => e.text.slice(0, e.start) + '[' + e.text.slice(e.start, e.end) + ']' + e.text.slice(e.end);

    await mode(false, false);
    await load(FIXTURE);
    await openBar();
    await search('kiwi');
    let count = (await lists()).length;
    await openList(true);
    let list = await nextList(count);

    if (only.includes('previews')) {
      check(!!list, 'opening the list sends it');
      check(list && list.total === 10 && list.entries.length === 10 && !list.capped, `the count is exact: 10 matches (${list && list.total}, ${list && list.entries.length} entries)`);
      const shown = list ? list.entries.map(show) : [];
      console.log('  ' + shown.join('\n  '));
      EXPECTED.forEach((want, i) => { if (want) check(shown[i] === want, `entry ${i + 1}: ${JSON.stringify(shown[i])}`); });
      const address = list && list.entries[3];
      check(!!address && address.text.includes('http://x.y/kiwi') && address.text.slice(address.start, address.end) === 'kiwi' && address.text.indexOf('http://x.y/kiwi') + 11 === address.start,
        `entry 4 (the match in a link's address) shows the address with its match marked (${address && show(address)})`);
      check(!!list && list.entries.every(e => e.text.slice(e.start, e.end).toLowerCase() === 'kiwi'), 'every entry marks exactly its match');
      check(!!list && list.index === 0 && (await state())?.total === 10 && (await state())?.index === 0, `the bar's place: 1 of 10 (${JSON.stringify(await state())})`);
      // The context: 40 characters each side, cut with an ellipsis.
      const long = `${'alpha '.repeat(20)}kiwi${' omega'.repeat(20)}`;
      count = (await lists()).length;
      await load(`${long}\n`);
      // A new document: the list comes again for it (the bar's search done again on it).
      const again = await nextList(count, l => l.total === 1);
      check(!!again, 'a new document sends the list again, for its own matches');
      const e = again && again.entries[0];
      check(!!e && e.text.startsWith('…') && e.text.endsWith('…') && e.start === 41 && e.end === 45 && e.text.length === 1 + 40 + 4 + 40 + 1,
        `the context is cut at 40 characters each side (${e && JSON.stringify(e)})`);
      count = (await lists()).length;
      await load(FIXTURE);
      list = await nextList(count, l => l.total === 10);
    }

    if (only.includes('cap')) {
      const many = Array.from({ length: 300 }, (_, i) => `Line ${i}: kiwi and kiwi, kiwi again and KIWI.`).join('\n\n') + '\n';
      count = (await lists()).length;
      await load(many);
      const capped = await nextList(count, l => l.total === 1200);
      check(!!capped && capped.entries.length === 500 && capped.capped, `1200 matches: 500 entries, the count exact and the list capped (${capped && `${capped.total}/${capped.entries.length}/${capped.capped}`})`);
      const st = await state();
      check(!!st && st.total === 1200, `the bar counts all 1200 (${JSON.stringify(st)})`);
      count = (await lists()).length;
      await load(FIXTURE);
      list = await nextList(count, l => l.total === 10);
    }

    // Visual mode: entry 3 chosen - its match selected exactly, the bar on it.
    const chosen = async () => page.evaluate(() => {
      const cs = window.__typedownMuya.contentState;
      const { matches, index } = cs.searchMatches;
      const m = matches[index];
      const c = cs.cursor;
      const sel = getSelection();
      return {
        index, total: matches.length,
        cursor: !!m && c.start.key === m.key && c.end.key === m.key && c.start.offset === m.start && c.end.offset === m.end,
        selection: sel.toString(),
        inEditor: !!sel.anchorNode && document.getElementById('ag-editor-id').contains(sel.anchorNode),
        mark: (document.querySelector('#ag-editor-id .ag-highlight') || {}).textContent || null,
      };
    });
    if (only.includes('activate')) {
      // Whatever the load before still sends, sent first (a load searches again once its scroll settles).
      await pause(600);
      list = await lastList();
      count = (await lists()).length;
      // The blocks drawn again for the step: those of the match chosen before and now (and the caret's), not every block
      // with a mark.
      const marked = await page.evaluate(() => {
        const m = window.__typedownMuya; const cs = m.contentState; const sr = cs.stateRender;
        window.__redrawn = new Set();
        const wrap = name => { const f = sr[name]; sr[name] = function (blocks, ...a) { for (const b of blocks) window.__redrawn.add(b.key); return f.call(this, blocks, ...a) } };
        wrap('redrawNow'); wrap('redrawBlocks');
        return new Set(cs.searchMatches.matches.map(x => cs.findOutMostBlock(cs.getBlock(x.key)).key)).size;
      });
      await activate(2, list.version);
      await pause(100);
      const now = await chosen();
      console.log('  ' + JSON.stringify(now));
      check(now.index === 2 && now.cursor, `entry 3 chosen: the third match is the one chosen and the caret is exactly on it (${now.index}, ${now.cursor})`);
      check(now.selection === 'kiwi' && now.inEditor, `and its text is selected in the editor ("${now.selection}")`);
      const st = await state();
      check(!!st && st.index === 2 && st.total === 10, `the bar says 3 of 10 (${JSON.stringify(st)})`);
      const redrawn = await page.evaluate(() => window.__redrawn.size);
      check(redrawn <= 2 && redrawn < marked, `only the blocks of the match chosen before and now are drawn again (${redrawn} of the ${marked} with marks)`);
      // The matches are the same: no new list (the host's list moves its mark on FindState; a list sent again rebuilt it
      // under the click, and it flashed).
      await pause(300);
      check((await lists()).length === count, `choosing an entry sends no new list (${(await lists()).length - count} sent: ${JSON.stringify((await lists()).slice(count).map(l => ({ v: l.version, at: Math.round(l.at), total: l.total, index: l.index })))}, before: ${JSON.stringify((await lists()).slice(Math.max(0, count - 2), count).map(l => ({ v: l.version, at: Math.round(l.at), total: l.total })))})`);
      // Find next goes on from there.
      await page.evaluate(() => window.__deliver('Find', { action: 'next' }));
      await pause(50);
      check((await state())?.index === 3, `Find next after it goes to the fourth (${JSON.stringify(await state())})`);
      // An entry from a list sent before the matches changed still finds its match (by the token), not by the index alone:
      // entry 7 ("kiwi col") of the list of 10, chosen after the search narrowed to "kiwi co" (1 match).
      const older = await lastList();
      await search('kiwi co');
      await pause(300);
      const left = await state();
      check(!!left && left.total === 1, `a narrower search: 1 match left (${JSON.stringify(left)})`);
      await activate(6, older.version);
      await pause(100);
      const kept = await chosen();
      check(kept.index === 0 && kept.cursor && kept.selection === 'kiwi co', `an entry of the list before still selects its own match (${JSON.stringify(kept)})`);
      await search('kiwi');
      await pause(300);
      list = await lastList();
    }

    if (only.includes('modes')) {
      // Source mode: the same search, its matches from the text's lines.
      count = (await lists()).length;
      await mode(true, false);
      const source = await nextList(count, l => l.total === 10, 8000);
      check(!!source, `source mode sends the list for its own matches (${source && source.total})`);
      if (source) {
        const shown = source.entries.map(show);
        console.log('  ' + shown.join('\n  '));
        const want = ['[Kiwi] heading here', 'A paragraph with [kiwi] in bold and a kiwi link.', 'A paragraph with kiwi in bold and a [kiwi] link.', null,
          'list item [kiwi] one', 'another [kiwi] code span', 'col [kiwi] col', 'a [kiwi] cell b', 'const [kiwi] = 1; // **kiwi**', 'const kiwi = 1; // **[kiwi]**'];
        want.forEach((w, i) => { if (w) check(shown[i] === w, `source mode entry ${i + 1}: ${JSON.stringify(shown[i])}`); });
        await activate(5, source.version);
        await pause(150);
        const cm = await page.evaluate(() => {
          const cm = document.querySelector('.CodeMirror').CodeMirror;
          const value = cm.getValue();
          const all = [...value.matchAll(/kiwi/gi)].map(m => m.index);
          return { from: cm.indexFromPos(cm.getCursor('from')), to: cm.indexFromPos(cm.getCursor('to')), want: all[5], text: cm.getSelection() };
        });
        check(cm.from === cm.want && cm.to === cm.want + 4 && cm.text.toLowerCase() === 'kiwi', `source mode: entry 6 selects the sixth match exactly (${JSON.stringify(cm)})`);
        check((await state())?.index === 5, `and the bar says 6 of 10 (${JSON.stringify(await state())})`);
      }
      // Reading mode: put in the window and marked, no caret.
      count = (await lists()).length;
      await mode(false, true);
      const reading = await nextList(count, l => l.total === 10, 8000);
      check(!!reading, 'reading mode sends the list');
      if (reading) {
        await page.evaluate(() => getSelection().removeAllRanges());
        await activate(7, reading.version);
        await pause(150);
        const now = await chosen();
        check(now.index === 7 && now.mark && now.mark.toLowerCase() === 'kiwi' && !now.inEditor, `reading mode: entry 8 is the match chosen and marked, with no selection in the page (${JSON.stringify(now)})`);
      }
      await mode(false, false);
      await search('kiwi');
      await pause(300);
      list = await lastList();
    }

    if (only.includes('edit')) {
      // Typing a match into the document while the list is open: the list and the count follow, the caret where it was.
      count = (await lists()).length;
      const before = (await state())?.total;
      // The caret at the end of the paragraph, the keyboard in the editor; then real keys. After a moment, as a reader would:
      // a search just made puts the caret on its first match (the page's own scroll settling after a load searches again).
      await pause(500);
      await page.evaluate(() => {
        const cs = window.__typedownMuya.contentState;
        const find = list => { for (const b of list) { if (typeof b.text === 'string' && b.text.startsWith('A paragraph')) return b; const f = find(b.children || []); if (f) return f } return null };
        const block = find(cs.blocks);
        document.getElementById('ag-editor-id').focus();
        cs.cursor = { start: { key: block.key, offset: block.text.length }, end: { key: block.key, offset: block.text.length } };
        cs.setCursor();
      });
      await page.keyboard.type(' more kiwi');
      const edited = await nextList(count, l => l.total === before + 1, 5000);
      check(!!edited, `typing "kiwi" into the document sends the list again with ${before + 1} (${(await lastList())?.total})`);
      // The caret in the model and on the page: at the end of what was typed, after the marks were drawn again.
      const caret = await page.evaluate(() => {
        const cs = window.__typedownMuya.contentState;
        const block = cs.getBlock(cs.cursor.start.key);
        const s = getSelection();
        const r = s.rangeCount ? s.getRangeAt(0).cloneRange() : null;
        let before = '';
        if (r) { const all = document.createRange(); all.selectNodeContents(document.getElementById(block.key)); all.setEnd(r.startContainer, r.startOffset); before = all.toString(); }
        return { text: block.text, offset: cs.cursor.start.offset, collapsed: s.isCollapsed, before };
      });
      check(caret.text.endsWith(' more kiwi') && caret.offset === caret.text.length && caret.collapsed && caret.before.endsWith('more kiwi'),
        `the caret stays at the end of what was typed (${JSON.stringify(caret)})`);
      check((await state())?.total === before + 1, `the bar counts ${before + 1} (${JSON.stringify(await state())})`);
      // Closing the list: nothing more is sent.
      await openList(false);
      count = (await lists()).length;
      await page.keyboard.type(' kiwi');
      await pause(800);
      check((await lists()).length === count, 'with the list closed, an edit sends no list');
      await openList(true);
      await pause(200);
    }

    if (only.includes('long')) {
      // A long document right after opening: the matches of "see above" all through it; the last one far below, not drawn.
      const { text } = generate();
      await openList(true);
      await page.evaluate(() => window.__deliver('SearchOpenChange', { open: 0 }));
      await load(text);
      const drawing = await page.evaluate(() => !!window.__typedownMuya.renderPending);
      // Frame gaps from here on.
      await page.evaluate(() => { const f = window.__flFrames = { max: 0, last: performance.now(), on: true, long: [] }; const tick = t => { if (!f.on) return; const g = t - f.last; if (g > f.max) f.max = g; if (g > 100) f.long.push(`${Math.round(g)} ms ending at ${Math.round(t)}`); f.last = t; requestAnimationFrame(tick) }; requestAnimationFrame(tick) });
      count = (await lists()).length;
      const sentAt = await page.evaluate(opt => {
        window.__deliver('SearchOpenChange', { open: 1 });
        window.__deliver('Search', { value: 'see above', opt });
        return performance.now();
      }, OPT);
      const big = await nextList(count, l => l.total > 20, 10000);
      const answer = big ? Math.round(big.at - sentAt) : Infinity;
      console.log(`  long document: ${text.length} characters, ${drawing ? 'still drawing' : 'drawn'}; ${big && big.total} matches, list after ${answer} ms`);
      check(!!big && answer <= LIMIT.answer, `the list of a long document's matches comes within ${LIMIT.answer} ms (${answer} ms)`);
      if (big) {
        const k = Math.min(big.total, big.entries.length) - 1;
        const far = await page.evaluate(k => {
          const cs = window.__typedownMuya.contentState;
          const m = cs.searchMatches.matches[k];
          const top = cs.findOutMostBlock(cs.getBlock(m.key));
          return { drawn: !!document.getElementById(top.key), block: cs.blocks.indexOf(top), blocks: cs.blocks.length };
        }, k);
        console.log(`  entry ${k + 1}: block ${far.block} of ${far.blocks}, ${far.drawn ? 'drawn' : 'not drawn yet'}`);
        check(!far.drawn, `the last entry's match is far below, not drawn yet (block ${far.block} of ${far.blocks})`);
        const t = await page.evaluate((k, version) => new Promise(done => {
          const t0 = performance.now();
          window.__deliver('FindActivate', { index: k, version });
          const look = () => {
            const cs = window.__typedownMuya.contentState;
            const m = cs.searchMatches.matches[k];
            const mark = document.querySelector('#ag-editor-id .ag-highlight');
            const r = mark && mark.getBoundingClientRect();
            const sel = getSelection().toString();
            const ok = cs.searchMatches.index === k && !!r && r.bottom > 0 && r.top < innerHeight && sel.toLowerCase() === 'see above' &&
              cs.cursor.start.key === m.key && cs.cursor.start.offset === m.start && cs.cursor.end.offset === m.end;
            if (ok) done(Math.round(performance.now() - t0)); else if (performance.now() - t0 > 5000) done(null); else requestAnimationFrame(look);
          };
          look();
        }), k, big.version);
        check(t !== null && t <= LIMIT.activate, `the last entry chosen: its match in the window and selected within ${LIMIT.activate} ms (${t} ms)`);
        check((await state())?.index === k, `the bar is on it (${JSON.stringify(await state())})`);
        await pause(1000);
        // Still there a second later, while the rest is drawn around it.
        const still = await page.evaluate(() => { const m = document.querySelector('#ag-editor-id .ag-highlight'); const r = m && m.getBoundingClientRect(); return !!r && r.bottom > 0 && r.top < innerHeight });
        check(still, 'and it stays in the window while the rest is drawn');
      }
      const gap = await page.evaluate(() => { window.__flFrames.on = false; return Math.round(window.__flFrames.max) });
      check(gap <= LIMIT.gap, `no frame gap over ${LIMIT.gap} ms meanwhile (longest ${gap} ms)`);
      const log = await page.evaluate(() => JSON.stringify(window.__typedownFindResultsLog.slice(-3)));
      console.log(`  lists built: ${log}`);
      console.log(`  frames over 100 ms: ${await page.evaluate(() => window.__flFrames.long.join(', '))}`);
      console.log(`  searches: ${await page.evaluate(() => JSON.stringify((window.__typedownSearchLog || []).slice(-6)))}`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\nfind-list-check: ${failures.length} failed`); process.exit(1); }
  console.log('\nfind-list-check: all passed');
})().catch(e => { console.error(e); process.exit(2); });
