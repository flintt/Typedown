// Enter in the find bar (Find) right behind the last letters typed (Search): the step goes to the match of the text in the
// bar, never to a match of what was typed before. w10, LT01: the page was busy, a Search for a shorter prefix had just been
// handled, and the last letters' Searches and the Find came together in one task; the prefix's search was done again
// after the last one (its React effect, flushed late), the Find stepped through the prefix's six matches and went to the
// first, near the top - the word, found once near the end, never came on screen.
//
//   node search-race-check.js
const { openEditor } = require('./harness');

const filler = i => `Paragraph ${i} with a few words, and quillwork ${i} in it.`;
const doc = '# Race\n\n' + Array.from({ length: 300 }, (_, i) => filler(i)).join('\n\n') + '\n\nThe Quillowmarsh entry near the end.\n';

(async () => {
  const { page, errors, mode, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    await mode(false, false);
    await load(doc);
    await page.evaluate(() => window.__deliver('SearchOpenChange', { open: 1 }));
    const result = await page.evaluate(() => new Promise(done => {
      const opt = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: { anchor: null, focus: null } };
      const search = value => window.__deliver('Search', { value, opt });
      // A prefix handled on its own, as the page catches up with the typing ...
      search('Quil');
      setTimeout(() => {
        // ... and in the very next task the last letters and Enter, all at once.
        for (const value of ['Quillo', 'Quillowm', 'Quillowmars', 'Quillowmarsh']) search(value);
        // What the step itself works on, as it runs (not what a search done after it leaves).
        const m = window.__typedownMuya; const find = m.find;
        m.find = function (...a) { window.__raceFind = { value: this.contentState.searchMatches.value, matches: this.contentState.searchMatches.matches.length }; return find.apply(this, a) };
        window.__deliver('Find', { action: 'next' });
        m.find = find;
        setTimeout(() => setTimeout(() => {
          const cs = window.__typedownMuya.contentState;
          const { value, matches, index } = cs.searchMatches;
          const mark = document.querySelector('#ag-editor-id .ag-highlight');
          const r = mark && mark.getBoundingClientRect();
          done({ find: window.__raceFind, value, matches: matches.length, index, mark: mark && mark.textContent, inWindow: !!r && r.bottom > 0 && r.top < innerHeight });
        }, 50), 0);
      }, 0);
    }));
    console.log(JSON.stringify(result));
    check(!!result.find && result.find.value === 'Quillowmarsh' && result.find.matches === 1, `Enter steps through the matches of the text in the bar (${JSON.stringify(result.find)})`);
    check(result.value === 'Quillowmarsh' && result.matches === 1, `and they are the matches left (${result.value}: ${result.matches})`);
    check(result.mark === 'Quillowmarsh' && result.inWindow, `Enter goes to its match, in the window (${result.mark}, ${result.inWindow ? 'in' : 'not in'} the window)`);
    await page.evaluate(() => window.__deliver('SearchOpenChange', { open: 0 }));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
