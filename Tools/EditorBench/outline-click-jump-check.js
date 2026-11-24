// A heading chosen in the outline, then clicked in the document, then Ctrl+A: the page stays where the outline put it
// (the user, 2026-11-24: the outline's "上架检查清单", then the same heading clicked in the document, the page jumped -
// 4509 and back to 8177 in hp88's log, where the caret had been - and Ctrl+A jumped too). The outline only scrolled; the
// caret stayed far below, and the editor taking the keyboard again went back to it.
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, load, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const filler = (n, tag) => Array.from({ length: n }, (_, i) => `${tag} paragraph ${i} with enough words to wrap a little on a narrow page.`).join('\n\n');
    // The page's scroll at every frame while something is done.
    const watch = () => page.evaluate(() => { window.__ys = []; window.__yOn = true; const tick = () => { if (!window.__yOn) return; window.__ys.push(Math.round(window.scrollY)); requestAnimationFrame(tick); }; requestAnimationFrame(tick); });
    const stop = () => page.evaluate(() => { window.__yOn = false; return [...new Set(window.__ys)]; });
    try {
        await mode(false, false);
        await load(`# Doc\n\n${filler(20, 'Top')}\n\n## Checklist\n\n${filler(60, 'Middle')}\n\n## Far below\n\nThe caret is here.\n\n${filler(20, 'End')}\n`);
        await mode(false, false);
        // The caret far below, the page there.
        await page.evaluate(() => {
            const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent === 'The caret is here.');
            el.scrollIntoView({ block: 'center' });
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            el.closest('[contenteditable]')?.focus();
        });
        await pause(400);
        // The outline: its row clicked takes the keyboard from the page (the host's pane), then the page scrolls there.
        const slug = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id h2')].find(h => h.textContent.includes('Checklist')).id);
        await page.evaluate(() => { document.activeElement?.blur(); });
        await page.evaluate(slug => window.__deliver('ScrollTo', { slug }), slug);
        await pause(600);
        const there = await page.evaluate(() => Math.round(window.scrollY));
        // The heading clicked, as the reader does.
        const at = await page.evaluate(() => { const h = [...document.querySelectorAll('#ag-editor-id h2')].find(h => h.textContent.includes('Checklist')); const r = h.getBoundingClientRect(); return { x: r.left + 60, y: r.top + r.height / 2 }; });
        await watch();
        await page.mouse.click(at.x, at.y);
        await pause(600);
        const clicked = await stop();
        check(clicked.every(y => Math.abs(y - there) <= 2), 'the heading clicked after the outline went there: the page stays', { there, clicked });
        // Back to the outline, then Ctrl+A as the app sends it.
        await page.evaluate(() => { document.activeElement?.blur(); });
        await page.evaluate(slug => window.__deliver('ScrollTo', { slug }), slug);
        await pause(600);
        const again = await page.evaluate(() => Math.round(window.scrollY));
        await watch();
        await page.evaluate(() => window.__deliver('SelectAll'));
        await pause(600);
        const all = await stop();
        check(all.every(y => Math.abs(y - again) <= 2), 'Ctrl+A after the outline went there: the page stays', { again, all });
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        check(false, 'ran to the end', String(e && e.stack || e));
    } finally {
        await close();
        process.exit(ok ? 0 : 1);
    }
})();
