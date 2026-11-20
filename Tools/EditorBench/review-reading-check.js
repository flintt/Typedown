// Review mode in reading mode (docs/todo.md 2026-11-20 用户清单 17): the revisions are marked and can be gone through,
// but nothing there accepts or rejects one - no buttons beside it (hover, caret or the host's next), not Alt+Enter or
// Alt+Backspace, not the host's commands, not the right-click menu (the page reports no revision under the pointer).
// Buttons already up when reading mode comes go at once; back in the visual editor they work again.
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const review = () => page.evaluate(() => window.__typedownReview());
    const buttonsShown = () => page.evaluate(() => { const a = document.getElementById('td-review-actions'); return !!a && getComputedStyle(a).display !== 'none'; });
    const markBox = () => page.evaluate(() => {
        const b = document.querySelector('#ag-editor-id .ag-review-ins').getBoundingClientRect();
        return { x: b.left + Math.min(10, b.width / 2), y: b.top + b.height / 2 };
    });
    const hover = async () => { const p = await markBox(); await page.mouse.move(p.x, p.y); await pause(900); };
    const away = async () => { await page.mouse.move(5, 5); await pause(300); };
    try {
        await mode(false, false);
        const baseline = '# Title\n\nThe cat sat.\n\nSecond paragraph.\n';
        const text = '# Title\n\nThe cat sat today.\n\nSecond paragraph, again.\n';
        await page.evaluate(({ text, baseline }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: 70, basePath: '/tmp', review: { on: true, baseline, revisions: [], labels: { accept: 'Accept', reject: 'Reject' } } });
        }, { text, baseline });
        await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 70, { timeout: 60000 });
        await pause(500);
        const ids = (await review()).revisions.map(r => r.id);
        const id = ids[0];
        check(ids.length === 2, 'two revisions to review', (await review()).revisions);
        const outlined = () => page.evaluate(() => [...new Set([...document.querySelectorAll('.td-review-current')].map(e => e.getAttribute('data-rid') || e.getAttribute('data-del-above-rid') || e.getAttribute('data-del-below-rid')))]);

        // The visual editor first: the buttons come on hover (what reading mode must not have).
        await hover();
        check(await buttonsShown(), 'visual editor: the buttons come over a revision');

        // Reading mode with the buttons up: they go at once.
        await mode(false, true);
        await pause(400);
        check(!(await buttonsShown()), 'reading mode: buttons already up are gone');
        check(await page.evaluate(() => document.querySelectorAll('#ag-editor-id .ag-review-ins').length) > 0, 'reading mode: the revision is still marked');

        await away();
        await hover();
        check(!(await buttonsShown()), 'reading mode: no buttons over a revision');
        // Next and previous go through them (no caret in reading mode: on from the last one gone to), outlined, no buttons.
        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'next' }));
        await pause(400);
        check(!(await buttonsShown()), 'reading mode: no buttons after the host\'s next');
        check(JSON.stringify(await outlined()) === JSON.stringify([ids[0]]), 'reading mode: next goes to the first, outlined', await outlined());
        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'next' }));
        await pause(400);
        check(JSON.stringify(await outlined()) === JSON.stringify([ids[1]]), 'reading mode: next again goes to the second', await outlined());
        await page.keyboard.down('Alt'); await page.keyboard.press('ArrowUp'); await page.keyboard.up('Alt');
        await pause(400);
        check(JSON.stringify(await outlined()) === JSON.stringify([ids[0]]), 'reading mode: Alt+Up goes back to the first', await outlined());

        // Keys and the host's commands change nothing.
        const before = await flush();
        await page.keyboard.down('Alt'); await page.keyboard.press('Enter'); await page.keyboard.up('Alt');
        await pause(300);
        await page.keyboard.down('Alt'); await page.keyboard.press('Backspace'); await page.keyboard.up('Alt');
        await pause(300);
        for (const command of ['accept', 'reject', 'acceptAll', 'rejectAll']) {
            await page.evaluate(command => window.__deliver('ReviewCommand', { command }), command);
            await pause(300);
        }
        let r = await review();
        check(r.revisions.length === 2 && r.revisions[0].id === id && r.baseline === baseline, 'reading mode: keys and commands accept or reject nothing', r);
        check((await flush()) === before, 'reading mode: the text is unchanged', await flush());

        // The right-click menu: no revision under the pointer, so the host shows no accept or reject.
        const p = await markBox();
        await page.mouse.click(p.x, p.y, { button: 'right' });
        await pause(300);
        const target = await page.evaluate(() => window.__typedownContextMenuTarget);
        check(target && (!target.review || target.review.revision === null), 'reading mode: the right-click reports no revision', target);
        await page.keyboard.press('Escape');

        // Back in the visual editor: the buttons and the keys work again.
        await mode(false, false);
        await pause(400);
        await away();
        await hover();
        check(await buttonsShown(), 'visual editor again: the buttons come back');
        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'acceptAll' }));
        await pause(400);
        r = await review();
        check(r.revisions.length === 0 && r.baseline === text, 'visual editor again: accept all works', r);
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
