// Review mode across documents, edits, places and modes (docs/todo.md "审阅模式"; the user, 2026-11-06: "经常出现修订了
// 一个小地方，出现大量删除和新加的内容"). Each cell loads a small document in review (its baseline the text as the host
// has it - LF, no BOM, as TextFileFormat reads a file), makes one small edit at one place, and judges:
//   1 nothing edited, nothing revised (after the load);
//   2 the revisions are this edit: few, and no more marked than the edit's size and a little;
//   3 (visual mode) each revision's marks are where its text is: an insertion's marked text is its text, a deletion's
//     mark is on a character that shows (not inside a hidden ** ` [ ...);
//   4 reject all gives the baseline back byte for byte (and, for the insertion, accept all makes the baseline the text);
//   5 the text has nothing of the marks.
// It prints a table (one line a cell: ok or FAIL, revisions, characters marked, the diff's ways - lines paired, lines
// whole, groups unpaired, comparisons given up - and what failed) and a summary.
// Options: --only <doc,doc>  --modes visual,source  --edits ins,del,...  --quick (one place a document)  --json <file>
const crypto = require('crypto');
const fs = require('fs');
const { openEditor } = require('./harness');

const sha = text => crypto.createHash('sha256').update(text, 'utf8').digest('hex');
const arg = name => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : null; };
const only = arg('--only')?.split(',');
const modes = (arg('--modes') ?? 'visual,source').split(',');
const editsWanted = arg('--edits')?.split(',');
const quick = process.argv.includes('--quick');
const jsonOut = arg('--json');

// The documents: their text (as the host hands it to the page) and the places to edit, each a [label, index] - the
// index in the text the caret goes to. `after(s)`: just after the first s; `before(s)`: just before it.
const doc = (name, text, places, raw) => {
    const at = (s, side) => { const i = text.indexOf(s); if (i < 0) throw new Error(`${name}: no ${s}`); return side === 'after' ? i + s.length : i; };
    return { name, text, raw, places: places.map(([label, side, s]) => [label, at(s, side)]) };
};
const docs = [
    doc('para', '# Title\n\nFirst paragraph here.\n\nMiddle paragraph with words.\n\nLast paragraph ends.\n',
        [['first block', 'after', 'Tit'], ['block start', 'before', 'Middle'], ['block mid', 'after', 'Middle para'], ['block end', 'after', 'with words.'], ['last block', 'after', 'Last para']]),
    doc('star list', '# L\n\n* one item\n* two item\n* three item\n', [['start', 'before', 'two'], ['mid', 'after', 'tw'], ['end', 'after', 'two item']]),
    doc('plus list', '# L\n\n+ one item\n+ two item\n+ three item\n', [['mid', 'after', 'tw'], ['end', 'after', 'two item']]),
    doc('dash list', '# L\n\n- one item\n- two item\n- three item\n', [['mid', 'after', 'tw'], ['end', 'after', 'two item']]),
    doc('ordered from 3', '# L\n\n3. three\n4. four\n5. five\n', [['mid', 'after', 'fo'], ['end', 'after', 'four']]),
    doc('nested list', '# L\n\n- top\n  - inner one\n  - inner two\n- next\n', [['mid', 'after', 'inner o'], ['end', 'after', 'inner two']]),
    doc('task list', '# L\n\n- [ ] todo one\n- [x] done two\n', [['mid', 'after', 'todo o'], ['end', 'after', 'done two']]),
    doc('underscore emphasis', '# E\n\nSome _em text_ and __strong text__ here.\n', [['in em', 'after', '_em t'], ['in strong', 'after', '__strong t'], ['after', 'after', 'here']]),
    doc('setext heading', 'Title\n=====\n\nBody text here.\n', [['in heading', 'after', 'Tit'], ['body', 'after', 'Body']]),
    doc('tilde fence', '# C\n\n~~~js\nlet a = 1;\n~~~\n\nAfter code.\n', [['in code', 'after', 'let a'], ['after', 'after', 'After']]),
    doc('indented code', '# C\n\n    code line\n    second line\n\nAfter code.\n', [['in code', 'after', 'code l'], ['after', 'after', 'After']]),
    doc('unaligned table', '# T\n\n|a|b|\n|-|-|\n|cell one|x|\n|two|y|\n\nAfter.\n', [['in cell', 'after', 'cell o'], ['after', 'after', 'After']]),
    doc('aligned table', '# T\n\n| Left | Centre | Right |\n| :--- | :----: | ----: |\n| l1   | c1     | r1    |\n\nAfter.\n', [['in cell', 'after', 'c1'], ['after', 'after', 'After']]),
    doc('lazy quote', '# Q\n\n> quoted line\nlazy continuation\n\nAfter.\n', [['in quote', 'after', 'quoted l'], ['lazy line', 'after', 'lazy c'], ['after', 'after', 'After']]),
    doc('html block', '# H\n\n<div>\nhtml text\n</div>\n\nAfter html.\n', [['after', 'after', 'After']]),
    doc('footnote', '# F\n\nText with note[^1].\n\n[^1]: The note.\n', [['text', 'after', 'Text w'], ['note', 'after', 'The n']]),
    doc('math', '# M\n\nInline $a+b$ here.\n\n$$\nx^2\n$$\n\nAfter.\n', [['near math', 'after', 'Inline'], ['after', 'after', 'After']]),
    doc('yaml', '---\ntitle: x\n---\n\n# Y\n\nBody here.\n', [['body', 'after', 'Body']]),
    doc('hard break', '# B\n\nline one  \nline two\n\nAfter.\n', [['first line', 'after', 'line o'], ['second line', 'after', 'line t']]),
    doc('backslash escape', '# S\n\nA \\* star and \\_ under.\n', [['mid', 'after', 'star'], ['end', 'after', 'under']]),
    doc('trailing spaces', '# W\n\nwords with trailing   \n\nnext para\n', [['mid', 'after', 'words w'], ['next', 'after', 'next']]),
    doc('many blank lines', '# B\n\nfirst\n\n\n\nsecond\n', [['first', 'after', 'firs'], ['second', 'after', 'seco']]),
    doc('tab indent', '# T\n\n-\titem one\n-\titem two\n', [['mid', 'after', 'item o']]),
    doc('reference link', '# R\n\nSee [the site][s] now.\n\n[s]: http://example.com\n', [['before link', 'before', '[the'], ['in link', 'after', '[the s'], ['after link', 'after', '[s] n']]),
    doc('bold and link', '# I\n\nPlain **bold words** and [a link](http://e.com) end.\n', [['before bold', 'before', '**bold'], ['in bold', 'after', '**bold w'], ['after bold', 'after', 'words** '], ['in link', 'after', '[a l'], ['after link', 'after', 'e.com) ']]),
    doc('chinese mixed', '# 中文\n\n今天天气很好，we go out 一起玩。\n', [['mid', 'after', '今天'], ['english', 'after', 'we g'], ['end', 'after', '一起玩']]),
    doc('crlf file', '# CR\n\nline one\n\nline two\n', [['mid', 'after', 'line o']], { crlf: true }),
    doc('bom file', '# BOM\n\nline one\n', [['mid', 'after', 'line o']], { bom: true }),
    doc('no final newline', '# N\n\nlast line', [['end', 'after', 'last line'], ['mid', 'after', 'last ']]),
    doc('repeated lines', '# D\n\nsame line\n\n---\n\nsame line\n\n---\n\nsame line\n', [['second copy', 'after', 'same line\n\n---\n\nsame']]),
];

// The edits: how much they change (the characters a revision may mark), how many revisions they may make, what to do.
const edits = {
    ins: { size: 1, count: 1, keys: async (p) => p.keyboard.type('Z') },
    del: { size: 1, count: 1, keys: async (p) => p.keyboard.press('Backspace'), not: ['block start', 'start'] },
    merge: { size: 2, count: 1, keys: async (p) => p.keyboard.press('Backspace'), only: ['block start', 'start'] },
    word: { size: 14, count: 2, keys: async (p) => { await p.keyboard.down('Control'); await p.keyboard.down('Shift'); await p.keyboard.press('ArrowLeft'); await p.keyboard.up('Shift'); await p.keyboard.up('Control'); await p.keyboard.type('Word'); } },
    ime: { size: 1, count: 1, keys: async (p) => p.keyboard.sendCharacter('中') },
    enter: { size: 3, count: 1, keys: async (p) => p.keyboard.press('Enter') },
    paste: { size: 8, count: 2, host: (p) => p.evaluate(() => window.__deliver('Paste', { type: 'normal', text: 'P1\nP2', html: '' })) },
    bold: { size: 6, count: 2, visualOnly: true, keys: async (p) => { await p.keyboard.down('Control'); await p.keyboard.down('Shift'); await p.keyboard.press('ArrowLeft'); await p.keyboard.up('Shift'); await p.keyboard.up('Control'); await p.evaluate(() => window.__deliver('Format', 'strong')); } },
    undo: { size: 0, count: 0, undo: true },
    ext: { size: 4, count: 1, ext: true },
};

(async () => {
    const { page, errors, mode, flush, pause, close } = await openEditor();
    const rows = [];
    let loadId = 1000;
    const review = () => page.evaluate(() => window.__typedownReview());
    const load = async (text, baseline) => {
        const id = ++loadId;
        await page.evaluate(({ text, baseline, id }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp', review: { on: true, baseline, revisions: [] } });
        }, { text, baseline, id });
        await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
        await pause(250);
        return id;
    };
    // The caret at an index of the source: in source mode CodeMirror's; in the visual editor the leaf whose text holds the
    // source around it (Muya's leaves have every character of their source, the hidden marks too).
    const caretAt = async (source, text, index, visual) => {
        if (!visual) return page.evaluate(i => {
            const cm = document.querySelector('.CodeMirror').CodeMirror;
            cm.focus(); cm.setCursor(cm.posFromIndex(i)); return true;
        }, index);
        const lineStart = text.lastIndexOf('\n', index - 1) + 1;
        const lineEnd = (text.indexOf('\n', index) + 1 || text.length + 1) - 1;
        const left = text.slice(Math.max(lineStart, index - 10), index);
        const right = text.slice(index, Math.min(lineEnd, index + 10));
        return page.evaluate(({ left, right }) => {
            const leaves = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content, #ag-editor-id .ag-atx-line, #ag-editor-id .ag-cell-content, #ag-editor-id .ag-code-content, #ag-editor-id .ag-setext-line, #ag-editor-id span.ag-paragraph')];
            // The longest left context (the markers before a leaf's text are not in it) that some leaf has with the right.
            for (let k = 0; k <= left.length; k++) {
              const l = left.slice(k);
              if (l.length + right.length < 2) break;
              for (const el of leaves) {
                const t = el.textContent;
                let at = t.indexOf(l + right);
                if (at < 0) continue;
                at += l.length;
                const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
                let n, seen = 0;
                while ((n = walker.nextNode())) {
                    if (seen + n.data.length >= at) {
                        const r = document.createRange(); r.setStart(n, at - seen); r.collapse(true);
                        const s = getSelection(); s.removeAllRanges(); s.addRange(r);
                        el.closest('[contenteditable="true"]')?.focus();
                        return true;
                    }
                    seen += n.data.length;
                }
              }
            }
            return false;
        }, { left, right });
    };
    const marksOk = () => page.evaluate(() => {
        const r = window.__typedownReview();
        const problems = [];
        for (const rev of r.revisions) {
            if (rev.newText && /\S/.test(rev.newText)) {
                const spans = [...document.querySelectorAll(`#ag-editor-id .ag-review-ins[data-rid="${rev.id}"]`)];
                const whole = document.querySelector(`#ag-editor-id .ag-review-whole[data-rid="${rev.id}"]`);
                const marked = spans.map(s => s.textContent).join('').replace(/\s+/g, '');
                const want = rev.newText.replace(/\s+/g, '');
                // Only block marks added (a new list item's "* "): nothing of them in the leaves to mark.
                if (/^[*+\-#>|\d.)[\]xX`~]*$/.test(want)) continue
                if (!whole && !want.includes(marked)) problems.push(`ins "${marked}"≠"${want}"`);
                if (!whole && !marked) problems.push(`ins "${want}" unmarked`);
            }
            if (rev.oldText && /\S/.test(rev.oldText)) {
                const host = document.querySelector(`#ag-editor-id [data-rid="${rev.id}"][data-del-before], #ag-editor-id [data-rid="${rev.id}"][data-del-after], #ag-editor-id [data-del-above-rid="${rev.id}"], #ag-editor-id [data-del-below-rid="${rev.id}"]`)
                    ?? [...document.querySelectorAll('#ag-editor-id [data-del-before], #ag-editor-id [data-del-after]')].find(e => (e.getAttribute('data-del-before') ?? e.getAttribute('data-del-after') ?? '').includes(rev.oldText.replace(/\n/g, ' ¶ ').trim()));
                if (!host) problems.push(`del "${rev.oldText.trim()}" not drawn`);
                else if (host.closest('.ag-hide')) problems.push(`del "${rev.oldText.trim()}" inside a hidden mark`);
            }
        }
        return problems;
    });
    const summary = { cells: 0, ok: 0, fail: 0, skip: 0 };
    const fail = (row, why) => { row.result = 'FAIL'; row.why.push(why); };
    for (const d of docs) {
        if (only && !only.some(o => d.name.startsWith(o))) continue;
        for (const m of modes) {
            const visual = m === 'visual';
            await mode(!visual, false);
            // 1: loaded, nothing revised.
            await load(d.text, d.text);
            let r = await review();
            rows.push({ doc: d.name, mode: m, place: '-', edit: 'load', result: r.revisions.length ? 'FAIL' : 'ok', n: r.revisions.length, marked: r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0), paths: r.paths, why: r.revisions.length ? ['revisions after a load: ' + JSON.stringify(r.revisions.map(x => [x.oldText, x.newText]))] : [] });
            for (const [place, index] of quick ? d.places.slice(0, 1) : d.places) {
                for (const [name, e] of Object.entries(edits)) {
                    if (editsWanted && !editsWanted.includes(name)) continue;
                    if (e.only && !e.only.includes(place)) continue;
                    if (e.not && e.not.includes(place)) continue;
                    if (e.visualOnly && !visual) continue;
                    const row = { doc: d.name, mode: m, place, edit: name, result: 'ok', n: 0, marked: 0, paths: null, why: [] };
                    rows.push(row);
                    summary.cells++;
                    try {
                        await load(d.text, d.text);
                        const before = await flush();
                        if (e.ext) {
                            const text = before.slice(0, index) + 'Ext ' + before.slice(index);
                            await page.evaluate(({ baseContentHash, text }) => {
                                delete window.__last.DocumentEditApplied;
                                window.__deliver('ApplyDocumentEdit', { operationId: 'mx', baseContentHash, text, source: { name: 'Bot', session: 's' } });
                            }, { baseContentHash: sha(before), text });
                            await page.waitForFunction(() => window.__last.DocumentEditApplied, { timeout: 30000 });
                        } else {
                            if (!(await caretAt(d.text, before, Math.min(index, before.length), visual))) { row.result = 'skip'; row.why.push('no caret place'); summary.skip++; continue; }
                            await pause(80);
                            if (e.undo) {
                                await page.keyboard.type('Z');
                                await pause(200);
                                // The host's undo: the text before, as SetMarkdown brings it.
                                await page.evaluate(text => window.__deliver('SetMarkdown', { text, cursor: null, basePath: '/tmp' }), before);
                            } else if (e.host) await e.host(page);
                            else await e.keys(page);
                        }
                        await pause(350);
                        const after = await flush();
                        r = await review();
                        row.n = r.revisions.length;
                        row.marked = r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0);
                        row.paths = r.paths;
                        if (after === before && !e.undo) { row.result = 'skip'; row.why.push('the edit changed nothing'); summary.skip++; continue; }
                        if (row.n > e.count || (e.count > 0 && row.n === 0)) fail(row, `${row.n} revisions`);
                        if (row.marked > e.size + 4) fail(row, `${row.marked} marked for an edit of ${e.size}`);
                        if (row.result === 'FAIL') row.why.push(JSON.stringify(r.revisions.map(x => [x.oldText, x.newText])).slice(0, 220));
                        // 3: where the marks are.
                        if (visual) for (const p of await marksOk()) fail(row, p);
                        // 5: nothing of the marks in the text.
                        if (/¶|data-del|ag-review/.test(after)) fail(row, 'marks in the text');
                        // 4: reject all - the baseline again; for the insertion, accept all on another load.
                        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'rejectAll' }));
                        await pause(350);
                        const rejected = await flush();
                        if (rejected !== d.text) fail(row, 'reject all ≠ baseline: ' + JSON.stringify(firstDiff(rejected, d.text)));
                        if (name === 'ins') {
                            await load(d.text, d.text);
                            const b2 = await flush();
                            if (await caretAt(d.text, b2, Math.min(index, b2.length), visual)) {
                                await pause(80);
                                await page.keyboard.type('Z');
                                await pause(300);
                                await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'acceptAll' }));
                                await pause(300);
                                const t = await flush();
                                const rr = await review();
                                if (rr.baseline !== t || rr.revisions.length) fail(row, 'accept all: baseline ≠ text');
                            }
                        }
                    } catch (err) {
                        fail(row, 'error ' + String(err.message || err).slice(0, 120));
                    }
                    if (row.result === 'ok') summary.ok++;
                    else if (row.result === 'FAIL') summary.fail++;
                }
            }
        }
    }

    // The ways through: after an edit, a mode switch, a save, review off and on, a reject then another edit.
    const flows = [];
    const runFlows = !only || only.includes('flows');
    const flow = async (name, run) => {
        if (!runFlows) return;
        const row = { doc: 'para', mode: 'flow', place: '-', edit: name, result: 'ok', n: 0, marked: 0, paths: null, why: [] };
        try { await run(row); } catch (err) { row.result = 'FAIL'; row.why.push('error ' + String(err.message || err).slice(0, 120)); }
        rows.push(row); flows.push(row);
    };
    const para = docs[0];
    const typeAt = async (s, text, visual) => { await caretAt(para.text, await flush(), para.text.indexOf(s) + s.length, visual); await pause(80); await page.keyboard.type(text); await pause(350); };
    await flow('edit, then to source mode and back', async row => {
        await mode(false, false); await load(para.text, para.text);
        await typeAt('Middle para', 'Z', true);
        const a = (await review()).revisions.map(x => x.newText);
        await mode(true, false); await pause(300);
        const b = (await review()).revisions.map(x => x.newText);
        await mode(false, false); await pause(300);
        const c = (await review()).revisions.map(x => x.newText);
        row.n = c.length;
        if (JSON.stringify(a) !== JSON.stringify(b) || JSON.stringify(a) !== JSON.stringify(c)) { row.result = 'FAIL'; row.why.push(JSON.stringify({ a, b, c })); }
    });
    await flow('switch modes with no edit', async row => {
        await mode(false, false); await load(para.text, para.text);
        await mode(true, false); await pause(300); await mode(false, false); await pause(300);
        const n = (await review()).revisions.length; row.n = n;
        if (n) { row.result = 'FAIL'; row.why.push(JSON.stringify((await review()).revisions.map(x => [x.oldText, x.newText]))); }
    });
    await flow('save (flush), then edit again', async row => {
        await mode(false, false); await load(para.text, para.text);
        await typeAt('First para', 'A', true);
        await flush();
        await typeAt('Last para', 'B', true);
        const r = await review(); row.n = r.revisions.length;
        if (r.revisions.length !== 2 || r.revisions.some(x => x.newText.length > 1 || x.oldText)) { row.result = 'FAIL'; row.why.push(JSON.stringify(r.revisions.map(x => [x.oldText, x.newText]))); }
    });
    await flow('review off and on again', async row => {
        await mode(false, false); await load(para.text, para.text);
        await typeAt('Middle para', 'Z', true);
        await page.evaluate(() => window.__deliver('FlushContent', { token: 9 }));
        await pause(300);
        const state = await page.evaluate(() => window.__last.ReviewState);
        await page.evaluate(() => window.__deliver('ReviewSet', { review: { on: false } }));
        await pause(200);
        await page.evaluate(s => window.__deliver('ReviewSet', { review: { on: true, baseline: s.baseline, revisions: s.revisions } }), { baseline: para.text, revisions: state.revisions });
        await pause(300);
        const r = await review(); row.n = r.revisions.length;
        if (r.revisions.length !== 1 || r.revisions[0].id !== state.revisions[0].id) { row.result = 'FAIL'; row.why.push(JSON.stringify(r.revisions)); }
    });
    await flow('reject one, then edit', async row => {
        await mode(false, false); await load(para.text, para.text);
        await typeAt('First para', 'A', true);
        await typeAt('Last para', 'B', true);
        await caretAt(para.text, await flush(), para.text.indexOf('Last para') + 'Last para'.length, true);
        await pause(100);
        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'reject' }));
        await pause(400);
        await typeAt('Middle para', 'C', true);
        const r = await review(); row.n = r.revisions.length;
        const texts = r.revisions.map(x => x.newText).sort().join(',');
        if (texts !== 'A,C') { row.result = 'FAIL'; row.why.push(texts); }
    });
    await flow('long document, many revisions far away, one letter', async row => {
        const words = 'alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu'.split(' ');
        const paras = Array.from({ length: 600 }, (_, i) => `Paragraph ${i} ${words.map((w, j) => words[(i + j) % words.length]).join(' ')}.`);
        const baseline = '# Long\n\n' + paras.join('\n\n') + '\n';
        // Forty revisions already, spread through it.
        const current = '# Long\n\n' + paras.map((p, i) => i % 15 === 0 ? p.replace('beta', 'BETA') : p).join('\n\n') + '\n';
        await mode(true, false);
        await page.evaluate(({ text, baseline }) => {
            delete window.__last.FileLoaded;
            window.__deliver('LoadFile', { text, loadId: 9001, basePath: '/tmp', review: { on: true, baseline, revisions: [] } });
        }, { text: current, baseline });
        await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 9001, { timeout: 60000 });
        await pause(600);
        const n0 = (await review()).revisions.length;
        await caretAt(current, current, current.indexOf('Paragraph 300 ') + 'Paragraph 300 '.length, false);
        await page.keyboard.type('Z'); await pause(600);
        const r = await review(); row.n = r.revisions.length; row.paths = r.paths;
        row.marked = r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0);
        row.why.push(`chars ${current.length}, revisions before ${n0}`);
        if (r.revisions.length !== n0 + 1 || row.marked > n0 * 8 + 1 + 8) { row.result = 'FAIL'; row.why.push('after ' + r.revisions.length); }
        if (r.paths.gaveUp) { row.result = 'FAIL'; row.why.push('the diff gave up'); }
    });

    // CRLF, a BOM: the baseline as the file is on disk (what the host would have if it did not normalize).
    for (const d of docs.filter(x => x.raw && (!only || only.some(o => x.name.startsWith(o))))) {
        const raw = (d.raw.bom ? '﻿' : '') + (d.raw.crlf ? d.text.replace(/\n/g, '\r\n') : d.text);
        await mode(false, false);
        try { await load(d.text, raw); } catch (err) {
            rows.push({ doc: d.name + ' (baseline raw)', mode: 'visual', place: '-', edit: 'load', result: 'FAIL', n: 0, marked: 0, paths: null, why: ['the load never finished; page errors: ' + errors.slice(-2).join(' / ').slice(0, 200)] });
            continue;
        }
        const r = await review();
        rows.push({ doc: d.name + ' (baseline raw)', mode: 'visual', place: '-', edit: 'load', result: r.revisions.length ? 'FAIL' : 'ok', n: r.revisions.length, marked: r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0), paths: r.paths, why: r.revisions.length ? ['the raw file against the editor\'s text'] : [] });
    }

    const pathText = p => p ? `p${p.pairs}/w${p.wholeLines}/u${p.unpairedGroups}/g${p.gaveUp}` : '';
    console.log('doc | mode | place | edit | result | revisions | marked | diff ways (pairs/whole lines/unpaired groups/gave up) | why');
    for (const row of rows) console.log([row.doc, row.mode, row.place, row.edit, row.result, row.n, row.marked, pathText(row.paths), row.why.join('; ')].join(' | '));
    const fails = rows.filter(x => x.result === 'FAIL');
    console.log(`\n${rows.length} rows: ${rows.filter(x => x.result === 'ok').length} ok, ${fails.length} FAIL, ${rows.filter(x => x.result === 'skip').length} skip; page errors ${errors.length}`);
    for (const e of [...new Set(errors)].slice(0, 8)) console.log('page error: ' + String(e).slice(0, 300))
    if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(rows, null, 1));
    await close();
    process.exit(fails.length || errors.length ? 1 : 0);
})();

function firstDiff(a, b) {
    let i = 0;
    while (i < a.length && a[i] === b[i]) i++;
    return { at: i, got: a.slice(Math.max(0, i - 10), i + 20), want: b.slice(Math.max(0, i - 10), i + 20) };
}
