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
            out[own(el)] = { height: Math.round(el.getBoundingClientRect().height), run: el.dataset.hiddenRun || null, done: el.classList.contains('td-done') };
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

        // Every frame watched (a loop of animation frames, which run before each paint): a frame where "three" was not folded,
        // or where "End." stood elsewhere, is one the reader saw jump or flash (the user, 2026-11-21, trial 1.4.3.2: Ctrl+A
        // made the document jump; a click now and then flashed).
        const watch = () => page.evaluate(() => {
            window.__hdFrames = [];
            const end = () => [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent === 'End.');
            const tick = () => {
                if (!window.__hdWatching) return;
                const li = [...document.querySelectorAll('#ag-editor-id li')].find(l => l.textContent.includes('three'));
                const e = end();
                const sel = getSelection(), at = sel.anchorNode && (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement);
                const folded = !!li && li.classList.contains('td-done');
                window.__hdFrames.push({ folded, end: e ? Math.round(e.getBoundingClientRect().top + window.scrollY) : null,
                    why: folded ? undefined : { caret: at ? (at.closest('.ag-paragraph-content') || at).textContent.slice(0, 20) : null, collapsed: sel.isCollapsed, liClass: li ? li.className : 'gone' } });
                requestAnimationFrame(tick);
            };
            window.__hdWatching = true;
            requestAnimationFrame(tick);
        });
        const watched = () => page.evaluate(() => { window.__hdWatching = false; return window.__hdFrames; });
        const summary = frames => ({ frames: frames.length, unfolded: frames.filter(f => !f.folded).length, ends: [...new Set(frames.map(f => f.end))], why: [...new Set(frames.filter(f => !f.folded).map(f => JSON.stringify(f.why)))] });

        // Ctrl+A step by step (the line, then the whole) changes no fold: a selection is not the caret.
        await fresh();
        await watch();
        for (let i = 0; i < 3; i++) {
            await page.keyboard.down('Control'); await page.keyboard.press('KeyA'); await page.keyboard.up('Control');
            await pause(300);
        }
        let frames = summary(await watched());
        check(frames.frames > 10 && frames.unfolded === 0 && frames.ends.length === 1, 'Ctrl+A three times: the fold never opens, nothing moves at any frame', frames);

        // Clicks in other paragraphs draw blocks again: the folded items stay folded at every frame.
        await fresh();
        await watch();
        for (const word of ['one', 'four', 'End.', 'one', 'four']) {
            const at = await page.evaluate(word => { const e = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(x => x.textContent.includes(word)); const r = e.getBoundingClientRect(); return { x: r.left + 4, y: r.top + r.height / 2 }; }, word);
            await page.mouse.click(at.x, at.y);
            await pause(250);
        }
        frames = summary(await watched());
        check(frames.frames > 10 && frames.unfolded === 0 && frames.ends.length === 1, 'clicks in other paragraphs: folded at every frame, nothing moves', frames);

        // Turned on (and off), the caret's line stays where it is on the screen: the items folding above it took it up (the
        // user, 2026-11-21). A long document, the caret on "End." in the middle of the window; reading mode keeps its top
        // line the same way.
        const filler = n => Array.from({ length: n }, (_, i) => `Filler line ${i + 1}.`).join('\n\n');
        const long = `${filler(30)}\n\n1. one\n2. ~~two~~\n3. ~~three~~\n4. ~~more~~\n5. four\n\nEnd.\n\n${filler(30)}\n`;
        await mode(true, false);
        await load(long);
        await mode(false, false);
        await page.evaluate(() => window.__deliver('SettingsChanged', { hideDone: false }));
        await caretAt('End.', true);
        await page.evaluate(() => { const e = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(x => x.textContent === 'End.'); window.scrollBy(0, e.getBoundingClientRect().top - window.innerHeight / 2); });
        await pause(300);
        const endOnScreen = () => page.evaluate(() => Math.round([...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(x => x.textContent === 'End.').getBoundingClientRect().top));
        const before = await endOnScreen();
        await page.evaluate(() => window.__deliver('SettingsChanged', { hideDone: true, hideDoneLabel: '{0} hidden · Show' }));
        await pause(400);
        const on = await endOnScreen();
        check((await shown())['three']?.height === 0 && Math.abs(on - before) <= 1, 'turned on: the folds made, the caret\'s line where it was on the screen', { before, on });
        await page.evaluate(() => window.__deliver('SettingsChanged', { hideDone: false }));
        await pause(400);
        const off = await endOnScreen();
        check(Math.abs(off - before) <= 1, 'turned off: the caret\'s line where it was', { before, off });

        // A search whose match is folded away opens that run (the reading mode's too).
        // (A word in the middle of the item too: "hre" in "three", the highlight inside the struck text.)
        for (const [reading, word] of [[false, 'three'], [true, 'three'], [false, 'hre'], [true, 'hre']]) {
            await fresh();
            if (reading) await mode(false, true);
            await pause(300);
            check((await shown())['three']?.height === 0, `${reading ? 'reading mode' : 'visual editor'}: "three" folded before the search`);
            await page.evaluate(word => window.__typedownMuya.search(word, { isCaseSensitive: false, isWholeWord: false, isRegexp: false, selectHighlight: true }), word);
            await pause(500);
            s = await shown();
            const highlight = await page.evaluate(() => { const h = document.querySelector('#ag-editor-id .ag-highlight, #ag-editor-id .ag-selection'); return h ? Math.round(h.getBoundingClientRect().height) : null; });
            check(s['three']?.height > 0 && s['three']?.done === false && highlight > 0, `${reading ? 'reading mode' : 'visual editor'}, "${word}": the search's match folded away is shown`, { two: s['two'], three: s['three'], highlight });
            await page.evaluate(() => window.__typedownMuya.search('', { isCaseSensitive: false, isWholeWord: false, isRegexp: false }));
            if (reading) await mode(false, false);
        }

        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
