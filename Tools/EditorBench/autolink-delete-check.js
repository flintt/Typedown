// A line that is only an address the editor links by itself (a URL or an e-mail), its text selected whole and deleted, then
// a click elsewhere (the user's report 41, 2026-11-24): the address came back. The browser took the link out with the
// line's text element, the caret was left in the paragraph, the editor found no line to read the change from and kept the
// old text - FlushContent, a save and a version all had the address. Two addresses by two keys by review on and off, and
// a line of plain words beside them (it was fine): by real clicks and keys, the text the page hands over each time.
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    let loadId = 90;
    const load = async (text, review) => {
        const id = ++loadId;
        await page.evaluate(({ text, id, review }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp', review: review ? { on: true, baseline: text, revisions: [], labels: { accept: 'Accept', reject: 'Reject' } } : undefined });
        }, { text, id, review });
        await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
        await pause(600);
    };
    // A real click in the middle of the line whose text is this.
    const clickLine = async (text) => {
        const at = await page.evaluate(text => {
            const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent === text);
            if (!el) return null;
            const r = el.getBoundingClientRect();
            return { x: r.left + Math.min(r.width / 2, 30), y: r.top + r.height / 2 };
        }, text);
        if (!at) return false;
        await page.mouse.click(at.x, at.y);
        await pause(250);
        return true;
    };
    try {
        await mode(false, false);
        const lines = ['https://example.org/a', 'reader@example.org', 'Just some plain words'];
        for (const line of lines) for (const key of ['Backspace', 'Delete']) for (const review of [false, true]) {
            const label = `${line} / ${key} / review ${review ? 'on' : 'off'}`;
            await load(`Before\n\n${line}\n\nAfter\n`, review);
            if (!await clickLine(line)) { check(false, `${label}: the line is drawn`); continue; }
            await page.keyboard.press('Home');
            await page.keyboard.down('Shift');
            await page.keyboard.press('End');
            await page.keyboard.up('Shift');
            await pause(150);
            const selected = await page.evaluate(() => String(window.getSelection()));
            await page.keyboard.press(key);
            await pause(250);
            await clickLine('After');
            await pause(400);
            const text = await flush();
            const shown = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].map(e => e.textContent));
            check(selected === line, `${label}: Home, Shift+End selects the line's text`, selected);
            check(!text.includes(line.slice(0, 12)), `${label}: after the click the text handed over has no "${line}"`, text);
            check(!shown.includes(line), `${label}: and the page does not show it again`, shown);
            check(text === 'Before\n\n\n\nAfter\n', `${label}: an empty paragraph left, as for plain words`, text);
        }
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        check(false, 'ran to the end', String(e && e.stack || e));
    } finally {
        await close();
        process.exit(ok ? 0 : 1);
    }
})();
