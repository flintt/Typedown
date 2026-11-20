// Reading mode hides what is done (docs/todo.md 2026-11-20 用户清单 18, the reading-mode half): with the setting on, a list
// item or paragraph struck through whole and a ticked task are folded away, each run of them one line "N hidden · Show"
// that opens it; an ordered list keeps its numbers (the hidden items still count); a paragraph with a few words struck
// stays. Off, or in the visual editor, nothing is hidden. The text is never touched.
//
//   node hide-done-check.js
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, load, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const settings = o => page.evaluate(o => window.__deliver('SettingsChanged', o), o);
    // What shows of each block by its own text: its height, and the line standing for a hidden run.
    const shown = () => page.evaluate(() => {
        // Its own text, without the lists in it and the marks reading mode hides (~~).
        // (Of its content lines: a paragraph's hidden "P" icon is text too.)
        const own = el => { const c = el.cloneNode(true); c.querySelectorAll('ul, ol, .ag-hide').forEach(x => x.remove()); return [...c.querySelectorAll('.ag-paragraph-content')].map(p => p.textContent).join('').trim(); };
        const out = {};
        for (const el of document.querySelectorAll('#ag-editor-id li, #ag-editor-id p:not(li p)')) {
            const r = el.getBoundingClientRect();
            out[own(el)] = { height: Math.round(r.height), display: getComputedStyle(el).display, run: el.dataset.hiddenRun || null };
        }
        return out;
    });
    const md = '1. one\n2. ~~two~~\n3. ~~three~~\n4. four\n\n- [x] done task\n- [ ] open task\n\nSome ~~partly~~ struck.\n\n~~Whole paragraph struck.~~\n\nEnd.\n';
    try {
        await mode(true, false);
        await load(md);
        await mode(false, true);
        await pause(400);
        const before = await shown();
        check(Object.values(before).every(b => b.height > 0 && !b.run), 'reading mode, setting off: nothing hidden', before);

        await settings({ hideDone: true, hideDoneLabel: '{0} hidden · Show' });
        await pause(500);
        let s = await shown();
        check(s['two']?.run === '2 hidden · Show', 'two struck items in a row: one line for both', s['two']);
        check(s['three']?.height === 0 && s['three']?.display === 'list-item', 'the second is hidden but still counted (list-item)', s['three']);
        check(s['one']?.height > 0 && s['four']?.height > 0, 'the items around stay', { one: s['one'], four: s['four'] });
        const four = await page.evaluate(() => { const li = [...document.querySelectorAll('#ag-editor-id ol > li')].find(l => l.textContent.trim() === 'four'); return [...li.parentElement.children].filter(c => c.tagName === 'LI').indexOf(li) + 1; });
        check(four === 4, 'four keeps its number (the hidden items are counted)', four);
        check(s['done task']?.run === '1 hidden · Show' && s['open task']?.height > 0, 'a ticked task hidden, an open one shown', { done: s['done task'], open: s['open task'] });
        const partly = Object.keys(s).find(k => k.startsWith('Some'));
        check(partly && s[partly].height > 0 && !s[partly].run, 'a paragraph with a few words struck stays', partly ? s[partly] : Object.keys(s));
        check(s['Whole paragraph struck.']?.run === '1 hidden · Show', 'a paragraph struck through whole is hidden', s['Whole paragraph struck.']);

        // The line opens its run.
        const at = await page.evaluate(() => { const r = [...document.querySelectorAll('#ag-editor-id li')].find(l => l.dataset.hiddenRun && l.textContent.includes('two')).getBoundingClientRect(); return { x: r.left + 10, y: r.top + r.height / 2 }; });
        await page.mouse.click(at.x, at.y);
        await pause(300);
        s = await shown();
        check(s['two']?.height > 0 && s['three']?.height > 0 && !s['two']?.run, 'clicked, the run opens', { two: s['two'], three: s['three'] });
        check(s['done task']?.run === '1 hidden · Show', 'the other runs stay folded', s['done task']);

        // The visual editor: nothing hidden. The text never changed.
        await mode(false, false);
        await pause(400);
        s = await shown();
        check(Object.values(s).every(b => b.height > 0 && !b.run), 'visual editor: nothing hidden', s);
        check((await flush()) === md, 'the text is unchanged', await flush());
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
