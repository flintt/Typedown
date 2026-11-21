// A triple click selects the line with its line break, up to the start of the next line - kept so, as the user wants it
// (it deletes the whole line) - but the next line's marks are not shown: none of its text is taken in (docs/todo.md
// 2026-11-21 用户清单 31). (The selected line's own marks are as they were: the selection's ends do not touch them.)
//
//   node triple-click-marks-check.js
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, load, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    try {
        await mode(true, false);
        await load('First ~~struck~~ line\n\n~~Second~~ line here\n\nThird line.\n');
        await mode(false, false);
        await pause(400);
        const at = await page.evaluate(() => { const e = document.querySelectorAll('#ag-editor-id .ag-paragraph-content')[0]; const r = e.getBoundingClientRect(); return { x: r.left + 12, y: r.top + r.height / 2 }; });
        await page.mouse.click(at.x, at.y, { clickCount: 1 });
        await page.mouse.click(at.x, at.y, { clickCount: 2 });
        await page.mouse.click(at.x, at.y, { clickCount: 3 });
        await pause(400);
        const state = await page.evaluate(() => {
            const lines = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')];
            const marks = el => [...el.querySelectorAll('.ag-gray, .ag-hide')].filter(m => /~~/.test(m.textContent)).map(m => m.classList.contains('ag-gray') ? 'shown' : 'hidden');
            const { start, end } = window.__typedownMuya.contentState.cursor;
            return { first: marks(lines[0]), second: marks(lines[1]), start: [lines.findIndex(l => l.id === start.key), start.offset], end: [lines.findIndex(l => l.id === end.key), end.offset] };
        });
        check(state.start[0] === 0 && state.start[1] === 0 && state.end[0] === 1 && state.end[1] === 0, 'the triple click selects the first line up to the start of the next (kept)', state);
        check(state.second.length === 2 && state.second.every(m => m === 'hidden'), 'the next line\'s marks do not show', state.second);
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
