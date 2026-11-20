// Hide Done Items in the visual editor too (docs/todo.md 2026-11-20 用户清单 18, as the user settled it): the done items fold
// as in reading mode, the run with the caret in it shown (an item just ticked does not vanish under it); Up and Down go past
// a fold; Enter at the end of the item before a fold adds the new item before it; Backspace at the start of the item after
// a fold does not join it to a hidden item, the caret goes to the end of the item before the fold; a selection across a fold
// deleted takes the hidden items with it.
//
//   node hide-done-edit-check.js
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, load, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const md = '1. one\n2. ~~two~~\n3. ~~three~~\n4. four\n\nEnd.\n';
    const shown = () => page.evaluate(() => {
        const own = el => { const c = el.cloneNode(true); c.querySelectorAll('ul, ol, .ag-hide, .ag-gray').forEach(x => x.remove()); return [...c.querySelectorAll('.ag-paragraph-content')].map(p => p.textContent).join('').trim(); };
        const out = {};
        for (const el of document.querySelectorAll('#ag-editor-id li, #ag-editor-id p:not(li p)'))
            out[own(el)] = { height: Math.round(el.getBoundingClientRect().height), run: el.dataset.hiddenRun || null };
        return out;
    });
    // The caret put in the block whose text has `needle`, at its start or end.
    const caretAt = async (needle, end) => {
        await page.evaluate(({ needle, end }) => {
            const state = window.__typedownMuya.contentState;
            const leaves = [];
            const walk = list => list.forEach(b => { if (b.functionType === 'paragraphContent') leaves.push(b); if (b.children) walk(b.children); });
            walk(state.blocks);
            const leaf = leaves.find(b => b.text.includes(needle));
            const position = { key: leaf.key, offset: end ? leaf.text.length : 0 };
            state.cursor = { start: position, end: position, anchor: position, focus: position };
            state.render();
        }, { needle, end });
        await pause(300);
    };
    const fresh = async () => {
        await mode(true, false);
        await load(md);
        await mode(false, false);
        await page.evaluate(() => window.__deliver('SettingsChanged', { hideDone: true, hideDoneLabel: '{0} hidden · Show' }));
        await caretAt('End.', true);
    };
    const lineOf = (text, word) => text.split('\n').find(l => l.includes(word)) ?? '';
    try {
        await fresh();
        let s = await shown();
        check(s['two']?.run === '2 hidden · Show' && s['three']?.height === 0, 'visual editor: the struck run folds', { two: s['two'], three: s['three'] });

        // Enter at the end of the item before the fold: the new item before it, the fold kept.
        await caretAt('one', true);
        await page.keyboard.press('Enter');
        await page.keyboard.type('new');
        await pause(400);
        let text = await flush();
        check(/one\n\d+\. new\n\d+\. ~~two~~/.test(text), 'Enter after "one": the new item before the fold', text);
        await caretAt('End.', true);
        s = await shown();
        check(s['two']?.run != null, 'the fold is still there', s['two']);

        // Down and Up go past the fold.
        await fresh();
        await caretAt('one', true);
        await page.keyboard.press('ArrowDown');
        await pause(200);
        await page.keyboard.type('X');
        await pause(300);
        text = await flush();
        check(lineOf(text, '4.').includes('X') && !lineOf(text, '2.').includes('X') && !lineOf(text, '3.').includes('X'), 'Down from "one" goes past the fold into "four"', text);
        await fresh();
        await caretAt('four', false);
        await page.keyboard.press('ArrowUp');
        await pause(200);
        await page.keyboard.type('X');
        await pause(300);
        text = await flush();
        check(lineOf(text, '1.').includes('X') && !lineOf(text, '3.').includes('X'), 'Up from "four" goes past the fold into "one"', text);

        // Backspace at the start of the item after the fold: no join with a hidden item, the caret to the end of "one".
        await fresh();
        await caretAt('four', false);
        await page.keyboard.press('Backspace');
        await pause(200);
        await page.keyboard.type('X');
        await pause(300);
        text = await flush();
        check(text === '1. oneX\n2. ~~two~~\n3. ~~three~~\n4. four\n\nEnd.\n', 'Backspace at the start of "four": the caret to the end of "one", nothing joined', text);

        // A selection across the fold deleted takes the hidden items with it (the user's call).
        await fresh();
        await page.evaluate(() => {
            const state = window.__typedownMuya.contentState;
            const leaves = [];
            const walk = list => list.forEach(b => { if (b.functionType === 'paragraphContent') leaves.push(b); if (b.children) walk(b.children); });
            walk(state.blocks);
            const one = leaves.find(b => b.text === 'one'), four = leaves.find(b => b.text === 'four');
            state.cursor = { start: { key: one.key, offset: 1 }, end: { key: four.key, offset: 3 }, anchor: { key: one.key, offset: 1 }, focus: { key: four.key, offset: 3 } };
            state.render();
        });
        await pause(300);
        await page.keyboard.press('Backspace');
        await pause(400);
        text = await flush();
        check(!text.includes('two') && !text.includes('three') && text.includes('or'), 'a selection across the fold deleted: the hidden items with it', text);

        // The caret in a run: that run shown; gone out of it, folded again.
        await fresh();
        await caretAt('three', true);
        s = await shown();
        check(s['two']?.run == null && s['two']?.height > 0 && s['three']?.height > 0, 'the caret in the run: shown', { two: s['two'], three: s['three'] });
        await caretAt('End.', true);
        s = await shown();
        check(s['two']?.run != null, 'the caret gone out: folded again', s['two']);

        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
