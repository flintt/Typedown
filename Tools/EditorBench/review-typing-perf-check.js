// Typing with review mode on and many revisions (the user, 2026-11-24: typing lagged with the revision marks shown;
// the performance review's P14 measured 344 ms a key at 300 revisions, 75 with review off): a document of 300
// paragraphs, each one word off its baseline, a few code fences among them; twenty letters typed in the middle with
// review on and with it off, each key timed from its press to two frames drawn. Review on may cost a little, not a
// multiple.
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const lines = n => Array.from({ length: n }, (_, i) => i);
    const fence = i => i % 60 === 30 ? `\`\`\`js\nconst x${i} = ${i};\nconsole.log(x${i});\n\`\`\`\n\n` : '';
    const doc = (word) => '# Review typing\n\n' + lines(300).map(i => `${fence(i)}Line ${i} has a ${word(i)} word and some more text after it.`).join('\n\n') + '\n';
    const baseline = doc(() => 'plain');
    const text = doc(i => `changed${i}`);
    const load = async (review) => {
        await page.evaluate(({ text, baseline, review }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: review ? 61 : 60, basePath: '/tmp', review: review ? { on: true, baseline, revisions: [], labels: { accept: 'Accept', reject: 'Reject' } } : undefined });
        }, { text, baseline, review });
        await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, review ? 61 : 60);
        await pause(1500);
    };
    const caretInMiddle = async () => {
        await page.evaluate(() => {
            const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent.startsWith('Line 150 '));
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            el.closest('[contenteditable]')?.focus();
        });
        await pause(300);
    };
    // Twenty keys typed, sampled: the page's busy time a key (not its idle: the frames it waits for came in whole steps of
    // 16.7 ms, and work taken off did not show), and where it went (REVIEW_PROFILE, or review-profile.flag beside this).
    const profiling = process.env.REVIEW_PROFILE || require('fs').existsSync(require('path').join(__dirname, 'review-profile.flag'));
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
        await caretInMiddle();
        const off = await typeTimed('review off');
        await load(true);
        const revisions = await page.evaluate(() => window.__typedownReview().revisions.length);
        check(revisions >= 250, 'review on: the document has its revisions', { revisions });
        await caretInMiddle();
        const on = await typeTimed('review on');
        console.log(`busy a key: review off ${off} ms, review on (${revisions} revisions) ${on} ms`);
        // Before: 39.9 against 23.3 here (1.7 times; P14 measured 344 against 75 on the reviewer's machine); now about the
        // same. As a ratio, so a slower machine (the CI's) is held to the same.
        check(on <= off * 1.3 + 5, 'a key with review on keeps the page busy hardly longer than with it off', { off, on });

        // In an item of a long numbered list (the user, 2026-11-24, hp88: still lagging with the marks shown, typing in
        // item 41 of a list): the line's marks changed at each key and the whole list was drawn again, the caret put back.
        const list = word => '# Review typing in a list\n\n' + lines(150).map(i => `${i + 1}. Item ${i} has a ${word(i)} word and more text after it`).join('\n') + '\n';
        const listLoad = async (review) => {
            await page.evaluate(({ text, baseline, review }) => {
                delete window.__last.FileLoaded;
                window.__deliver('LoadFile', { text, loadId: review ? 63 : 62, basePath: '/tmp', review: review ? { on: true, baseline, revisions: [], labels: { accept: 'Accept', reject: 'Reject' } } : undefined });
            }, { text: list(i => `changed${i}`), baseline: list(() => 'plain'), review });
            await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, review ? 63 : 62);
            await pause(1500);
        };
        const caretInItem = async () => {
            await page.evaluate(() => {
                const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent.startsWith('Item 75 '));
                el.scrollIntoView({ block: 'center' });
                const range = document.createRange();
                range.selectNodeContents(el);
                range.collapse(false);
                const sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(range);
                el.closest('[contenteditable]')?.focus();
            });
            await pause(300);
        };
        await listLoad(false);
        await caretInItem();
        const listOff = await typeTimed('list, review off');
        await listLoad(true);
        await caretInItem();
        const listOn = await typeTimed('list, review on');
        const typed = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent.startsWith('Item 75 '))?.textContent);
        check(/after itabcdefghijklmnopqrst$/.test(typed || ''), 'the letters went into item 75, in order', typed);
        const marked = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(e => e.textContent.startsWith('Item 75 '))?.querySelectorAll('.ag-review-ins, .ag-review-mark').length);
        check(marked > 0, 'item 75 shows its marks', { marked });
        console.log(`busy a key in a list's item: review off ${listOff} ms, on ${listOn} ms`);
        check(listOn <= listOff * 1.3 + 5, 'in a long list\'s item too, review on keeps the page busy hardly longer', { off: listOff, on: listOn });
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        check(false, 'ran to the end', String(e && e.stack || e));
    } finally {
        await close();
        process.exit(ok ? 0 : 1);
    }
})();
