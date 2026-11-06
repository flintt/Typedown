// Review mode on the page (services/reviewSession.ts, docs/todo.md "审阅模式"): a document loaded with its baseline,
// what is typed marked as the reader's revision, what a program writes marked with its name, a paragraph deleted shown
// as a strip, accept (into the baseline) and reject (the text put back) from the host's commands and the keys, the
// marks in source mode too, hidden and gone when the host says so - and nothing of it in the text.
const crypto = require('crypto');
const { openEditor } = require('./harness');

const sha = text => crypto.createHash('sha256').update(text, 'utf8').digest('hex');

(async () => {
    const { page, errors, mode, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const review = () => page.evaluate(() => window.__typedownReview());
    const state = () => page.evaluate(() => window.__last.ReviewState);
    let op = 0;
    const apply = async (text, source) => {
        const base = await flush();
        const operationId = `op${++op}`;
        await page.evaluate(({ operationId, baseContentHash, text, source }) => {
            delete window.__last.DocumentEditApplied;
            window.__deliver('ApplyDocumentEdit', { operationId, baseContentHash, text, source });
        }, { operationId, baseContentHash: sha(base), text, source });
        await page.waitForFunction(id => window.__last.DocumentEditApplied?.operationId === id, { timeout: 30000 }, operationId);
        await pause(500);
    };
    const loadInReview = async (text, extra = {}) => {
        await page.evaluate(({ text, extra }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: 50, basePath: '/tmp', review: { on: true, baseline: text, revisions: [], labels: { accept: 'Accept', reject: 'Reject' }, ...extra } });
        }, { text, extra });
        await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 50, { timeout: 60000 });
        await pause(500);
    };
    const caretAtEndOf = async selector => {
        await page.evaluate(selector => {
            const el = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content, #ag-editor-id .ag-atx-line')].find(e => e.textContent.includes(selector));
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            el.closest('[contenteditable]')?.focus();
        }, selector);
        await pause(100);
    };
    try {
        await mode(false, false);
        const base = '# Title\n\nThe cat sat.\n\nSecond paragraph.\n\nThird paragraph.\n';
        await loadInReview(base);
        check((await review()).on && (await review()).revisions.length === 0, 'loaded in review: no revisions');

        // The reader types: a revision of the reader's, marked green, not in the text.
        await caretAtEndOf('The cat sat.');
        await page.keyboard.type(' Indeed');
        await pause(500);
        let r = await review();
        check(r.revisions.length === 1 && r.revisions[0].newText === ' Indeed' && r.revisions[0].source === '', 'typing is the reader\'s revision', r.revisions);
        const ins = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-review-ins')].map(e => e.textContent).join('|'));
        check(ins.includes('Indeed'), 'the inserted text is marked', ins);
        check((await flush()) === base.replace('sat.', 'sat. Indeed'), 'the text has nothing of the marks');
        await pause(400);
        const reported = await state();
        check(reported && reported.inserted === 1 && reported.revisions.length === 1 && reported.revisions[0].line === 3, 'the host is told the revisions', reported);

        // A program writes: its revision has its name.
        const now = await flush();
        await apply(now.replace('Second paragraph.', 'Second paragraph, revised.'), { name: 'Bot', session: 's1' });
        r = await review();
        const bot = r.revisions.find(x => x.source === 'Bot');
        check(bot && bot.newText.includes('revised') && r.revisions.length === 2, 'the program\'s write is its revision', r.revisions);
        const tag = await page.evaluate(() => document.querySelector('#ag-editor-id .ag-review-tagged')?.getAttribute('data-review-source'));
        check(tag === 'Bot', 'its paragraph is tagged with the program', tag);

        // A paragraph deleted: a strip above the next one.
        await apply((await flush()).replace('Third paragraph.\n', '').replace(/\n\n$/, '\n'), { name: 'Bot', session: 's1' });
        const strip = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-review-del-above, #ag-editor-id .ag-review-del-below')].map(e => e.getAttribute('data-del-above') ?? e.getAttribute('data-del-below')));
        check(strip.some(s => s && s.includes('Third paragraph.')), 'the deleted paragraph is shown as a strip', { strip, r: await review(), text: await flush(), html: await page.evaluate(() => document.querySelector('#ag-editor-id').innerHTML.slice(-1500)) });

        // Accept all of the program's: into the baseline, the reader's stays.
        await page.evaluate(() => { delete window.__last.ReviewState; window.__deliver('ReviewCommand', { command: 'acceptAll', source: 'Bot' }) });
        await pause(400);
        r = await review();
        check(r.revisions.length === 1 && r.revisions[0].source === '', 'accept all of one source leaves the others', r.revisions);
        check(r.baseline.includes('revised') && !r.baseline.includes('Third'), 'accepted into the baseline', r.baseline);
        const afterAccept = await state();
        check(afterAccept && typeof afterAccept.baseline === 'string' && afterAccept.outcomes.length === 2 && afterAccept.outcomes.every(o => o.outcome === 'accepted' && o.source === 'Bot'), 'the host is told the new baseline and the outcomes', afterAccept && afterAccept.outcomes);

        // Next, then Alt+Backspace: the reader's revision rejected, the text as the baseline.
        await caretAtEndOf('Title');
        await page.evaluate(() => { delete window.__last.MarkdownChange });
        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'next' }));
        await pause(300);
        const actions = await page.evaluate(() => { const a = document.getElementById('td-review-actions'); return a && getComputedStyle(a).display !== 'none' ? a.querySelector('.td-review-accept').title : null });
        check(actions === 'Accept', 'the buttons show beside the revision the caret went to', { actions, r: await review(), sel: await page.evaluate(() => { const s = getSelection(); return s.anchorNode && (s.anchorNode.parentElement.outerHTML.slice(0, 200) + ' @' + s.anchorOffset) }) });
        await page.keyboard.down('Alt');
        await page.keyboard.press('Backspace');
        await page.keyboard.up('Alt');
        await pause(600);
        r = await review();
        const text = await flush();
        check(r.revisions.length === 0 && text === r.baseline, 'Alt+Backspace rejects it: the text is the baseline', { text, revisions: r.revisions });
        const change = await page.evaluate(() => window.__last.MarkdownChange);
        check(change && change.text === text && change.origin === 'user', 'the reject is told as the reader\'s edit', change);

        // Source mode: the same revisions marked there.
        await apply(text.replace('# Title', '# A Title'), { name: 'Bot', session: 's1' });
        await mode(true, false);
        await pause(400);
        const cm = await page.evaluate(() => [...document.querySelectorAll('.CodeMirror .cm-review-ins')].map(e => e.textContent).join('|'));
        check(cm.includes('A'), 'source mode marks the revision', cm);
        await mode(false, false);

        // Hidden, then review off: no marks.
        await page.evaluate(() => window.__deliver('ReviewSet', { review: { on: true, show: false } }));
        await pause(300);
        let marks = await page.evaluate(() => document.querySelectorAll('#ag-editor-id .ag-review-ins, #ag-editor-id .ag-review-tagged').length);
        check(marks === 0 && (await review()).revisions.length === 1, 'hidden: no marks, the revision still there', marks);
        await page.evaluate(() => window.__deliver('ReviewSet', { review: { on: false } }));
        await pause(300);
        marks = await page.evaluate(() => document.querySelectorAll('#ag-editor-id [class*="ag-review"]').length);
        check(marks === 0 && !(await review()).on, 'review off: nothing marked', marks);

        // Loaded again with the revisions the host kept: matched by their place, the program's name kept.
        const kept = afterAccept.revisions;
        await loadInReview('# T\n\nOne more.\n', { baseline: '# T\n\nOne.\n', revisions: [{ id: 'kept1', baseStart: 8, baseEnd: 8, newText: ' more', source: 'Bot', time: 5 }] });
        r = await review();
        check(r.revisions.length === 1 && r.revisions[0].id === 'kept1' && r.revisions[0].source === 'Bot', 'revisions kept by the host come back with their source', r.revisions);
        void kept;

        // Turned on by the host just after it sent a document, before the page took the load in: on for that document.
        await page.evaluate(() => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text: '# Late\n\nText.\n', loadId: 60, basePath: '/tmp', review: null });
            window.__deliver('ReviewSet', { loadId: 60, review: { on: true, baseline: '# Late\n\nText.\n', revisions: [] } });
        });
        await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 60, { timeout: 60000 });
        await pause(400);
        r = await review();
        check(r.on && r.baseline === '# Late\n\nText.\n' && r.revisions.length === 0, 'review turned on for a load not in yet goes with it', r);
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
