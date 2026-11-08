// Two or three lines of plain text pasted (the host's Paste, as Ctrl+V sends it) at the start, the middle and the end of a
// heading, and in a paragraph, a list item and a quotation: the lines go in once, at the caret, nothing else changes.
// Pasted into a heading, the paragraph made for the lines after the first was put in the wrong way round (Muya's
// insertAfter with its two blocks swapped): the heading came out twice, the blocks' keys written into it as text
// ("# TitP1\n\n# Titag-0-...ag-1-...P1"), the rest of the heading and the last lines lost. Users met it in the app.
//
//   node paste-lines-check.js
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, flush, load, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const doc = '# Title here\n\nA paragraph of words.\n\n- one item\n- two item\n\n> a quoted line\n\nThe end.\n';
    // [place, the leaf's text to find, where in it]
    const places = [
        ['heading start', 'Title here', 0], ['heading middle', 'Title here', 3], ['heading end', 'Title here', 10],
        ['paragraph middle', 'A paragraph of words.', 2], ['list item', 'two item', 3], ['quotation', 'a quoted line', 2],
    ];
    try {
        await mode(false, false);
        for (const lines of [['P1', 'P2'], ['P1', 'P2', 'P3']]) {
            for (const [place, leafText, at] of places) {
                await load(doc);
                const placed = await page.evaluate(({ leafText, at }) => {
                    const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content, #ag-editor-id .ag-atx-line')].find(e => e.textContent.endsWith(leafText));
                    if (!el) return false;
                    const want = el.textContent.length - leafText.length + at;
                    const w = document.createTreeWalker(el, NodeFilter.SHOW_TEXT); let n, seen = 0;
                    while ((n = w.nextNode())) {
                        if (seen + n.data.length >= want) {
                            const r = document.createRange(); r.setStart(n, want - seen); r.collapse(true);
                            // Where a click puts the caret there, as a person puts it.
                            const box = r.getClientRects()[0] || r.getBoundingClientRect();
                            return { x: box.left + 0.5, y: box.top + box.height / 2 };
                        }
                        seen += n.data.length;
                    }
                    return false;
                }, { leafText, at });
                if (placed) await page.mouse.click(placed.x, placed.y);
                await pause(120);
                await page.evaluate(text => window.__deliver('Paste', { type: 'normal', text, html: '' }), lines.join('\n'));
                await pause(300);
                const text = await flush();
                const label = `${lines.length} lines pasted at the ${place}`;
                if (!placed) { check(false, label + ': the caret placed', place); continue; }
                const counts = lines.map(l => text.split(l).length - 1);
                check(!/ag-\d+-/.test(text) && counts.every(n => n === 1) && (text.match(/^# /gm) || []).length === 1 &&
                    ['paragraph of words.', 'one item', 'The end.'].every(w => text.includes(w)), label, text);
                // What was before and after the caret is still there, around the lines.
                const before = leafText.slice(0, at), after = leafText.slice(at);
                check(text.includes(before + lines[0]) && text.includes(lines[lines.length - 1] + after), label + ': in at the caret', text);
            }
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
