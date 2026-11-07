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
        // The tag in the left margin, clear of the text (the user's 3.16); a dot when the window is narrow.
        const tagPlace = async () => { await pause(200); return page.evaluate(() => {
            const t = document.querySelector('.td-review-tag'), leaf = document.querySelector('#ag-editor-id .ag-review-tagged');
            if (!t || !leaf) return null;
            const a = t.getBoundingClientRect(), b = leaf.getBoundingClientRect();
            return { text: t.textContent, title: t.title, dot: t.classList.contains('td-review-tag-dot'), right: a.right, textLeft: b.left, top: a.top, leafTop: b.top, leafBottom: b.bottom };
        }); };
        const wide = page.viewport();
        await page.setViewport({ width: 1400, height: wide.height });
        let placed = await tagPlace();
        check(placed && placed.text === 'Bot' && placed.right <= placed.textLeft - 8 && placed.top >= placed.leafTop - 4 && placed.top < placed.leafBottom,
            'the program\'s name is in the left margin beside its block, clear of the text', placed);
        await page.setViewport({ width: 600, height: wide.height });
        placed = await tagPlace();
        check(placed && placed.dot && placed.title === 'Bot' && placed.right <= placed.textLeft && placed.right > 0, 'in a narrow window a dot, the name on hover', placed);
        await page.setViewport(wide);

        // A paragraph deleted: a block of its own where it was (a ghost).
        await apply((await flush()).replace('Third paragraph.\n', '').replace(/\n\n$/, '\n'), { name: 'Bot', session: 's1' });
        const strip = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-review-ghost')].map(e => e.textContent));
        check(strip.some(s => s && s.includes('Third paragraph.')), 'the deleted paragraph is shown where it was', { strip, r: await review(), text: await flush(), html: await page.evaluate(() => document.querySelector('#ag-editor-id').innerHTML.slice(-1500)) });

        // Each revision its own buttons, with who made it and when beside them: the program's says its name and a time.
        let meta = null;
        for (let i = 0; i < 6 && !(meta && meta.includes('Bot')); i++) {
            await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'next' }));
            await pause(250);
            meta = await page.evaluate(() => document.querySelector('#td-review-actions .td-review-meta')?.textContent ?? null);
        }
        check(meta && meta.includes('Bot') && /\d{1,2}:\d\d/.test(meta), 'the buttons say the revision\'s source and time', meta);
        const outlined = await page.evaluate(() => [...document.querySelectorAll('.td-review-current')].map(e => e.getAttribute('data-rid') || e.getAttribute('data-del-above-rid') || e.getAttribute('data-del-below-rid')));
        const forId = await page.evaluate(() => document.getElementById('td-review-actions').dataset.for);
        check(outlined.length > 0 && outlined.every(id => id === forId), 'the revision the buttons are for is outlined, and only it', { outlined, forId });

        // Rendered for Word: the revisions as <ins> and <del> with their source and time.
        await page.evaluate(() => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token: 77, revisions: true }) });
        await page.waitForFunction(() => window.__last.RenderedXhtml?.token === 77, { timeout: 30000 });
        const xhtml = await page.evaluate(() => window.__last.RenderedXhtml.xhtml);
        check(/<ins[^>]*data-rev-source="Bot"/.test(xhtml) && /data-rev-para="del"|<del[^>]*data-rev-source="Bot"/.test(xhtml) && !/[\ue000-\ue005]/.test(xhtml), 'rendered for Word: <ins> and <del> with the source', xhtml.slice(0, 400));
        await page.evaluate(() => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token: 78 }) });
        await page.waitForFunction(() => window.__last.RenderedXhtml?.token === 78, { timeout: 30000 });
        check(!/data-rev/.test(await page.evaluate(() => window.__last.RenderedXhtml.xhtml)), 'rendered without revisions when not asked');

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
        check(typeof actions === 'string' && actions.startsWith('Accept'), 'the buttons show beside the revision the caret went to', { actions, r: await review(), sel: await page.evaluate(() => { const s = getSelection(); return s.anchorNode && (s.anchorNode.parentElement.outerHTML.slice(0, 200) + ' @' + s.anchorOffset) }) });
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

        // A word deleted right before bold, a link, code: drawn on a character that shows, not inside the hidden marks.
        await loadInReview('Plain **bold** and [a link](http://e.com) end.\n', { baseline: 'Plain x **bold** and y [a link](http://e.com) end z `c`.\n' });
        await loadInReview('Plain **bold** and [a link](http://e.com) end `c`.\n', { baseline: 'Plain x **bold** and y [a link](http://e.com) end z `c`.\n' });
        const hidden = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id [data-del-before], #ag-editor-id [data-del-after]')].map(e => ({ text: e.textContent, hidden: !!e.closest('.ag-hide') })));
        check(hidden.length === 3 && hidden.every(h => !h.hidden), 'deletions beside hidden marks are drawn where they show', hidden);

        // Three links deleted together (the user, 2026-11-07): a strip each, a line each, a long one broken onto the next
        // line rather than cut off, and each its own buttons - the middle one rejected alone.
        const long = 'https://example.com/' + 'a-very-long-path-segment/'.repeat(12) + 'end';
        const links = `# Links\n\nBefore.\n\n${long}\n\nhttps://example.com/two\n\nhttps://example.com/three\n\nAfter.\n`;
        await loadInReview('# Links\n\nBefore.\n\nAfter.\n', { baseline: links });
        r = await review();
        check(r.revisions.length === 3 && r.revisions.every(x => x.kind === 'delete'), 'three blocks deleted together are three deletions', r.revisions);
        const strips = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-review-ghost')].map(e => {
            const box = e.getBoundingClientRect(), page = document.querySelector('#ag-editor-id').getBoundingClientRect();
            return { rid: e.getAttribute('data-rid'), text: e.textContent, height: box.height, right: box.right, leafRight: page.right, white: getComputedStyle(e).whiteSpace };
        }));
        check(strips.length === 3 && new Set(strips.map(s => s.rid)).size === 3, 'a strip each, each its own revision', strips);
        check(strips.every(s => !s.text.includes('\n')) && strips[0].text === long, 'each strip its paragraph, on a line of its own', strips.map(s => s.text));
        check(strips[0].height > strips[1].height * 1.5 && strips.every(s => s.right <= s.leafRight + 1), 'a long link breaks onto the next line inside the page', strips);
        const middle = await page.evaluate(() => { const b = document.querySelectorAll('#ag-editor-id .ag-review-ghost')[1].getBoundingClientRect(); return { x: b.left + 20, y: b.top + b.height / 2 }; });
        await page.mouse.move(middle.x, middle.y);
        // (The buttons follow the mouse after half a second.)
        await pause(900);
        const shownFor = await page.evaluate(() => document.getElementById('td-review-actions')?.dataset.for);
        check(shownFor === strips[1].rid, 'over the middle strip, the buttons are for it', { shownFor, strips });
        const cross = await page.evaluate(() => { const b = document.querySelector('#td-review-actions .td-review-reject').getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; });
        await page.mouse.click(cross.x, cross.y);
        await pause(500);
        const afterReject = await flush();
        check(afterReject === '# Links\n\nBefore.\n\nhttps://example.com/two\n\nAfter.\n', 'its ✗ puts back that link alone, in its place', afterReject);
        r = await review();
        check(r.revisions.length === 2 && r.revisions.map(x => x.id).join() === [strips[0].rid, strips[2].rid].join(), 'the other two are still their own deletions', r.revisions);

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
