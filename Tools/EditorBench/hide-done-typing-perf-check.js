// Typing with Hide Done Items on (the user, 2026-11-24, trial 1.4.3.19 on hp88: "lag to much, a least 1~2 sec", review's
// marks off - a document of 26k characters, 37 runs folded, typed into a long numbered list): each key took every fold off
// the whole document and put them back, and the place of the caret's line then read - the page styled and laid out whole,
// 111 ms a key against 40 with it off there. A long document with many items struck and ticked, twenty letters typed in a
// numbered list's item with Hide Done Items on and with it off, the page's busy time a key compared.
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const items = n => Array.from({ length: n }, (_, i) => i);
    // Ten sections: a numbered list of 30 items, every third struck through, a task list half ticked, a paragraph.
    const text = '# Hide done typing\n\n' + items(10).map(s =>
        `## Section ${s}\n\n` +
        items(30).map(i => `${i + 1}. ${i % 3 === 0 ? `~~Item ${s}.${i} was done and struck through~~` : `Item ${s}.${i} still to do, with some words after it`}`).join('\n') + '\n\n' +
        items(8).map(i => `- [${i % 2 ? 'x' : ' '}] Task ${s}.${i} with a few words`).join('\n') + '\n\n' +
        `A paragraph in section ${s}, with a few more words to make it a line.`
    ).join('\n\n') + '\n';
    let loadId = 80;
    const load = async (hideDone) => {
        await page.evaluate(hideDone => window.__deliver('SettingsChanged', { hideDone, hideDoneLabel: '{0} hidden · Show' }), hideDone);
        const id = ++loadId;
        await page.evaluate(({ text, id }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp' });
        }, { text, id });
        await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
        await pause(2500);
    };
    const caretInItem = async () => {
        const found = await page.evaluate(() => {
            const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent.startsWith('Item 5.13 '));
            if (!el) return false;
            el.scrollIntoView({ block: 'center' });
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            el.closest('[contenteditable]')?.focus();
            return true;
        });
        await pause(400);
        return found;
    };
    // Twenty keys typed, sampled: the page's busy time a key (as review-typing-perf-check.js), and where it went
    // (hide-done-profile.flag beside this).
    const profiling = require('fs').existsSync(require('path').join(__dirname, 'hide-done-profile.flag'));
    const typeTimed = async (label) => {
        const cdp = await page.target().createCDPSession();
        await cdp.send('Profiler.enable');
        await cdp.send('Profiler.setSamplingInterval', { interval: 200 });
        await cdp.send('Profiler.start');
        for (const ch of 'abcdefghijklmnopqrst') {
            await page.keyboard.type(ch);
            await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
        }
        const { profile } = await cdp.send('Profiler.stop');
        await cdp.detach();
        await pause(800);
        const byId = new Map(profile.nodes.map(n => [n.id, n]));
        const self = new Map();
        let busy = 0;
        profile.samples.forEach((id, i) => {
            const n = byId.get(id);
            const ms = (profile.timeDeltas[i] || 0) / 1000;
            if (n.callFrame.functionName === '(idle)') return;
            busy += ms;
            const k = `${n.callFrame.functionName || '(anon)'} ${n.callFrame.url.split('/').pop()}:${n.callFrame.lineNumber + 1}:${n.callFrame.columnNumber}`;
            self.set(k, (self.get(k) || 0) + ms);
        });
        if (profiling) console.log(`PROFILE ${label}\nPROFILE ` + [...self.entries()].sort((a, b) => b[1] - a[1]).slice(0, 20).map(([k, v]) => `${v.toFixed(0)}ms ${k}`).join('\nPROFILE '));
        return Math.round(busy / 20 * 10) / 10;
    };
    try {
        await mode(false, false);
        await load(false);
        check(await caretInItem(), 'hide done off: the caret in item 5.13');
        const off = await typeTimed('hide done off');
        await load(true);
        const folded = await page.evaluate(() => document.querySelectorAll('#ag-editor-id .td-done-first').length);
        check(folded >= 50, 'hide done on: the struck and ticked items folded', { folded });
        check(await caretInItem(), 'hide done on: the caret in item 5.13');
        const on = await typeTimed('hide done on');
        const typed = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].some(e => e.textContent.includes('after itabcdefghijklmnopqrst')));
        check(typed, 'the letters went into the item');
        console.log(`busy a key: hide done off ${off} ms, on (${folded} runs folded) ${on} ms`);
        // As a ratio, so a slower machine (the CI's) is held to the same.
        check(on <= off * 1.3 + 5, 'a key with hide done on keeps the page busy hardly longer than with it off', { off, on });
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        check(false, 'ran to the end', String(e && e.stack || e));
    } finally {
        await close();
        process.exit(ok ? 0 : 1);
    }
})();
