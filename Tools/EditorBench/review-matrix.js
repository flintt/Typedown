// Review mode across documents, edits, places and modes (docs/todo.md "审阅模式"; the user, 2026-11-06: "经常出现修订了
// 一个小地方，出现大量删除和新加的内容"). Each cell loads a small document in review (its baseline the text as the host
// has it - LF, no BOM, as TextFileFormat reads a file), makes one small edit at one place, and judges:
//   1 nothing edited, nothing revised (after the load);
//   2 the revisions are this edit: few, and no more marked than the edit's size and a little;
//   3 (visual mode) each revision's marks are where its text is: an insertion's marked text is its text, a deletion's
//     mark is on a character that shows (not inside a hidden ** ` [ ...);
//   4 reject all gives the baseline back byte for byte (and, for the insertion, accept all makes the baseline the text);
//   5 the text has nothing of the marks;
//   6 the document's other lines are as the source wrote them (the edit's own line and the next one aside, and an
//     ordered list's numbers, which follow the items): a fence's ~~~, a setext underline, a lazy line, __ and _, the
//     list markers, the end without a line break.
// It prints a table (one line a cell: ok or FAIL, revisions, characters marked, the diff's ways - lines paired, lines
// whole, groups unpaired, comparisons given up - and what failed) and a summary.
// Options: --only <doc,doc>  --modes visual,source  --edits ins,del,...  --json <file>
//   --quick    one place a document and two of the edits each (every document, edit and mode at least once), and the
//              ways through: the CI's set, a few minutes.
//   --workers N  the documents shared among N browsers at once (each its own process; "auto", the default without
//              --quick: one a core but one, at most 8).
//   A failure listed in review-matrix-known.json (doc|mode|place|edit) is known: only a new one fails the run (exit 1).
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
const docIndexes = arg('--docs')?.split(',').map(Number);
const noFlows = process.argv.includes('--no-flows');
const workersArg = arg('--workers');
// The whole matrix shared among as many browsers as the machine takes (vm103: 8) unless told; the quick one in one.
const auto = () => Math.max(1, Math.min(8, require('os').cpus().length - 1));
const workers = workersArg === 'auto' || (workersArg == null && !quick) ? auto() : Math.max(1, parseInt(workersArg ?? '1', 10) || 1);
const KNOWN = require('path').join(__dirname, 'review-matrix-known.json');
const cellKey = row => `${row.doc}|${row.mode}|${row.place}|${row.edit}`;

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
    doc('footnote', '# F\n\nText with note[^1].\n\n[^1]: The note.\n\nAfter the note here.\n', [['text', 'after', 'Text w'], ['note', 'after', 'The n'], ['after the note', 'after', 'After the']]),
    // Drawn blocks: their text in no leaf the page shows (the diagram drawn instead); what follows them was left unmarked
    // on x280 (1.4.1.29).
    doc('mermaid', '# M\n\n```mermaid\ngraph TD\n  A-->B\n```\n\nAfter the diagram here.\n\nLast one.\n', [['after the diagram', 'after', 'After the'], ['last block', 'after', 'Last']]),
    doc('plantuml', '# P\n\n```plantuml\n@startuml\nA -> B\n@enduml\n```\n\nAfter the drawing here.\n', [['after the drawing', 'after', 'After the']]),
    doc('vega-lite', '# V\n\n```vega-lite\n{"mark": "bar", "data": {"values": [{"a": 1}]}, "encoding": {"x": {"field": "a"}}}\n```\n\nAfter the chart here.\n', [['after the chart', 'after', 'After the']]),
    doc('math', '# M\n\nInline $a+b$ here.\n\n$$\nx^2\n$$\n\nAfter.\n', [['near math', 'after', 'Inline'], ['after', 'after', 'After']]),
    doc('yaml', '---\ntitle: x\n---\n\n# Y\n\nBody here.\n', [['in yaml', 'after', 'tit'], ['body', 'after', 'Body']]),
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
    // Backspace at a list item's start makes it a paragraph of the item before: its marker and a blank line.
    merge: { size: 3, count: 2, keys: async (p) => p.keyboard.press('Backspace'), only: ['block start', 'start'] },
    word: { size: 14, count: 2, keys: async (p) => { await p.keyboard.down('Control'); await p.keyboard.down('Shift'); await p.keyboard.press('ArrowLeft'); await p.keyboard.up('Shift'); await p.keyboard.up('Control'); await p.keyboard.type('Word'); } },
    ime: { size: 1, count: 1, keys: async (p) => p.keyboard.sendCharacter('中') },
    enter: { size: 3, count: 1, keys: async (p) => p.keyboard.press('Enter') },
    paste: { size: 8, count: 2, host: (p) => p.evaluate(() => window.__deliver('Paste', { type: 'normal', text: 'P1\nP2', html: '' })) },
    bold: { size: 6, count: 2, visualOnly: true, keys: async (p) => { await p.keyboard.down('Control'); await p.keyboard.down('Shift'); await p.keyboard.press('ArrowLeft'); await p.keyboard.up('Shift'); await p.keyboard.up('Control'); await p.evaluate(() => window.__deliver('Format', 'strong')); } },
    undo: { size: 0, count: 0, undo: true },
    ext: { size: 4, count: 1, ext: true },
};

// The documents shared among workers: each a process of its own with a browser of its own, the rows put together here.
async function runWorkers() {
    const { spawn } = require('child_process');
    const os = require('os');
    const started = Date.now();
    const chosen = docs.map((d, i) => i).filter(i => !only || only.some(o => docs[i].name.startsWith(o)));
    const parts = Array.from({ length: workers }, () => []);
    chosen.forEach((d, i) => parts[i % workers].push(d));
    const passOn = process.argv.slice(2).filter((a, i, all) => !['--workers', '--json', '--only', '--docs'].includes(a) && !['--workers', '--json', '--only', '--docs'].includes(all[i - 1]));
    const results = await Promise.all(parts.filter(p => p.length).map((part, w) => new Promise(resolve => {
        const out = require('path').join(os.tmpdir(), `review-matrix-${process.pid}-${w}.json`);
        const args = [__filename, ...passOn, '--docs', part.join(','), '--json', out, ...(w > 0 ? ['--no-flows'] : [])];
        const child = spawn(process.execPath, args, { stdio: ['ignore', 'ignore', 'inherit'], env: process.env });
        child.on('exit', () => {
            try { resolve(JSON.parse(fs.readFileSync(out, 'utf8'))); } catch { resolve({ rows: [], errors: [`worker ${w} wrote nothing`] }); }
            try { fs.unlinkSync(out); } catch { }
        });
    })));
    const rows = results.flatMap(r => r.rows);
    const errors = results.flatMap(r => r.errors);
    report(rows, errors, Date.now() - started);
}

const pathText = p => p ? (p.local ? 'words' : `p${p.pairs}/w${p.wholeLines}/u${p.unpairedGroups}`) + (p.gaveUp ? '/gave up' : '') : '';

function report(rows, errors, ms) {
    let known = [];
    try { known = JSON.parse(fs.readFileSync(KNOWN, 'utf8')).cells || []; } catch { }
    const knownSet = new Set(known);
    console.log('doc | mode | place | edit | result | revisions | marked | diff | why');
    for (const row of rows) console.log([row.doc, row.mode, row.place, row.edit, row.result === 'FAIL' && knownSet.has(cellKey(row)) ? 'known' : row.result, row.n, row.marked, pathText(row.paths), row.why.join('; ')].join(' | '));
    const fails = rows.filter(x => x.result === 'FAIL');
    const fresh = fails.filter(x => !knownSet.has(cellKey(x)));
    const healed = known.filter(k => rows.some(r => cellKey(r) === k && r.result === 'ok'));
    for (const e of [...new Set(errors)].slice(0, 8)) console.log('page error: ' + String(e).slice(0, 300));
    console.log(`\n${rows.length} rows: ${rows.filter(x => x.result === 'ok').length} ok, ${fails.length} FAIL (${fails.length - fresh.length} known, ${fresh.length} new), ${rows.filter(x => x.result === 'skip').length} skip; page errors ${errors.length}; ${Math.round(ms / 1000)} s`);
    if (healed.length) console.log(`known failures that pass now (take them out of review-matrix-known.json): ${healed.join(', ')}`);
    if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(rows, null, 1));
    process.exit(fresh.length || errors.length ? 1 : 0);
}

if (workers > 1 && !docIndexes) runWorkers();
else (async () => {
    const started = Date.now();
    const { page, errors, mode, flush, pause, close } = await openEditor();
    // Where each page error happened, with its stack: the harness keeps the message only.
    let currentCell = 'start';
    const errorPlaces = [];
    page.on('pageerror', e => errorPlaces.push(`${currentCell}: ${String(e.stack || e.message).split('\n').slice(0, 9).join(' | ')}`));
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
        await pause(80);
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
        // Not past a table's | or a formula's $: neither is in the leaf's text as written there.
        const right = text.slice(index, Math.min(lineEnd, index + 10)).split(/[|$]/)[0];
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
            // Code is drawn again by its highlighting: a bar beside it, not its letters (parser/render/review.js).
            const inCode = [...document.querySelectorAll('#ag-editor-id .ag-review-whole')].some(e => e.matches('.ag-code-content, pre *'))
            if (inCode) continue
            if (rev.newText && /\S/.test(rev.newText)) {
                const spans = [...document.querySelectorAll(`#ag-editor-id .ag-review-ins[data-rid="${rev.id}"]`)];
                const whole = document.querySelector(`#ag-editor-id .ag-review-whole[data-rid="${rev.id}"]`);
                const marked = spans.map(s => s.textContent).join('').replace(/\s+/g, '');
                // Without the block marks at its lines' starts (a quotation's >, a list's -), which no leaf holds.
                const want = rev.newText.replace(/(^|\n)[ \t]*(?:>[ \t]?)*(?:[*+-][ \t]|\d+[.)][ \t])?/g, '$1').replace(/\s+/g, '');
                // Only block marks added (a new list item's "* "): nothing of them in the leaves to mark.
                if (/^[*+\-#>|\d.)[\]xX`~]*$/.test(want)) continue
                if (!whole && !want.includes(marked)) problems.push(`ins "${marked}"≠"${want}"`);
                if (!whole && !marked) problems.push(`ins "${want}" unmarked`);
            }
            if (rev.oldText && /\S/.test(rev.oldText)) {
                const host = document.querySelector(`#ag-editor-id [data-rid="${rev.id}"][data-del-before], #ag-editor-id [data-rid="${rev.id}"][data-del-after], #ag-editor-id [data-del-above-rid="${rev.id}"], #ag-editor-id [data-del-below-rid="${rev.id}"], #ag-editor-id .ag-review-strip[data-rid="${rev.id}"]`)
                    ?? [...document.querySelectorAll('#ag-editor-id [data-del-before], #ag-editor-id [data-del-after]')].find(e => (e.getAttribute('data-del-before') ?? e.getAttribute('data-del-after') ?? '').includes(rev.oldText.replace(/\n/g, ' ¶ ').trim()));
                if (!host) problems.push(`del "${rev.oldText.trim()}" not drawn`);
                else if (host.closest('.ag-hide')) problems.push(`del "${rev.oldText.trim()}" inside a hidden mark`);
            }
        }
        return problems;
    });
    const summary = { cells: 0, ok: 0, fail: 0, skip: 0 };
    const fail = (row, why) => { row.result = 'FAIL'; row.why.push(why); };
    for (const [di, d] of docs.entries()) {
        if (docIndexes ? !docIndexes.includes(di) : only && !only.some(o => d.name.startsWith(o))) continue;
        for (const m of modes) {
            const visual = m === 'visual';
            currentCell = `${d.name}|${m}|load`;
            await mode(!visual, false);
            // 1: loaded, nothing revised.
            await load(d.text, d.text);
            let r = await review();
            rows.push({ doc: d.name, mode: m, place: '-', edit: 'load', result: r.revisions.length ? 'FAIL' : 'ok', n: r.revisions.length, marked: r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0), paths: r.paths, why: r.revisions.length ? ['revisions after a load: ' + JSON.stringify(r.revisions.map(x => [x.oldText, x.newText]))] : [] });
            for (const [place, index] of quick ? d.places.slice(0, 1) : d.places) {
                // Quick: two edits a document (three in the visual editor), the documents taking them in turn.
                const names = Object.keys(edits)
                const quickNames = new Set([0, 1, 2].slice(0, visual ? 3 : 2).map(k => names[(di * 3 + k) % names.length]))
                for (const [name, e] of Object.entries(edits)) {
                    if (editsWanted && !editsWanted.includes(name)) continue;
                    if (quick && !editsWanted && !quickNames.has(name)) continue;
                    if (e.only && !e.only.includes(place)) continue;
                    if (e.not && e.not.includes(place)) continue;
                    if (e.visualOnly && !visual) continue;
                    const row = { doc: d.name, mode: m, place, edit: name, result: 'ok', n: 0, marked: 0, paths: null, why: [] };
                    const errorsBefore = errors.length;
                    currentCell = cellKey(row);
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
                            // A key that changes nothing: the visual editor's model takes the caret from the page then
                            // (a paste from the host went where the caret was before, as no person's would).
                            await page.keyboard.press('Shift');
                            await pause(40);
                            if (e.undo) {
                                await page.keyboard.type('Z');
                                await pause(200);
                                // The host's undo: the text before, as SetMarkdown brings it.
                                await page.evaluate(text => window.__deliver('SetMarkdown', { text, cursor: null, basePath: '/tmp' }), before);
                            } else if (e.host) await e.host(page);
                            else await e.keys(page);
                        }
                        await pause(120);
                        const after = await flush();
                        await pause(30);
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
                        // 6: the rest as the source wrote it.
                        if (!e.undo) {
                            const lines = d.text.split('\n')
                            let at = 0, editLine = 0
                            for (let i = 0; i < lines.length; i++) { if (index <= at + lines[i].length) { editLine = i; break } at += lines[i].length + 1 }
                            const have = new Set(after.split('\n'))
                            // Two lines either side: Backspace at a block's start joins it to the one before.
                            const lost = lines.filter((l, i) => l.trim() && Math.abs(i - editLine) > 2 && !/^\s*\d+[.)]\s/.test(l) && !have.has(l))
                            if (lost.length) fail(row, 'rewritten: ' + JSON.stringify(lost).slice(0, 120))
                            if (!d.text.endsWith('\n') && after.endsWith('\n') && !['enter', 'paste'].includes(name)) fail(row, 'a line break added at the end')
                        }
                        // 4: reject all - the baseline again; for the insertion, accept all on another load.
                        await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'rejectAll' }));
                        await page.waitForFunction(() => window.__typedownReview().revisions.length === 0, { timeout: 3000 }).catch(() => { });
                        await pause(60);
                        const rejected = await flush();
                        // The page's baseline: writing alone was taken into it (not a revision, so not rejected).
                        const pageBaseline = (await review()).baseline;
                        if (rejected !== pageBaseline) fail(row, 'reject all ≠ baseline: ' + JSON.stringify(firstDiff(rejected, pageBaseline)));
                        else if (rejected !== d.text) row.why.push('writing taken into the baseline');
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
                    if (errors.length > errorsBefore) row.why.push('page error: ' + String(errors[errors.length - 1]).split('\n')[0].slice(0, 80));
                    if (row.result === 'ok') summary.ok++;
                    else if (row.result === 'FAIL') summary.fail++;
                }
            }
        }
    }

    // The ways through: after an edit, a mode switch, a save, review off and on, a reject then another edit.
    const flows = [];
    const runFlows = !noFlows && (!only || only.includes('flows'));
    const flow = async (name, run) => {
        if (!runFlows) return;
        currentCell = 'flow: ' + name;
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
    // Two list items (and two paragraphs) written a while apart, the later one typed on at the end of the first: two
    // revisions; rejecting the later leaves the first, accepting the first leaves the later (the user, 2026-11-07).
    for (const [what, text, first, second] of [
        ['list items', '# L\n\n- one\n', '\n- two', '\n- three'],
        ['paragraphs', '# P\n\nOne.\n', '\n\nTwo.', '\n\nThree.'],
    ]) {
        await flow(`${what} written a while apart: one revision each, the later rejected alone`, async row => {
            await mode(false, false);
            await page.evaluate(() => { window.__typedownReviewClockOffset = 0 });
            await load(text, text);
            // Enter then the words, as typed: the parts split on the line breaks.
            const typeLines = async s => { const parts = s.split('\n'); for (let i = 0; i < parts.length; i++) { if (i > 0) await page.keyboard.press('Enter'); if (parts[i]) await page.keyboard.type(parts[i]); } await pause(200); };
            let t = await flush();
            await caretAt(t, t, t.replace(/\n+$/, '').length, true);
            await page.keyboard.press('Shift');
            await typeLines(first.replace(/^\n/, '\n'));
            await page.evaluate(() => { window.__typedownReviewClockOffset = 10 * 60 * 1000 });
            await typeLines(second);
            const r = await review();
            row.n = r.revisions.length;
            const words = r.revisions.map(x => x.newText.replace(/[\s\-]/g, ''));
            if (r.revisions.length !== 2) { row.result = 'FAIL'; row.why.push('revisions: ' + JSON.stringify(r.revisions.map(x => x.newText))); return; }
            // Reject the later one with the caret in it.
            t = await flush();
            const later = r.revisions[1];
            await caretAt(t, t, later.curStart + later.newText.replace(/\s+$/, '').length, true);
            await page.keyboard.press('Shift');
            await pause(80);
            await page.evaluate(() => window.__deliver('ReviewCommand', { command: 'reject' }));
            await pause(400);
            const afterReject = await review();
            t = await flush();
            if (afterReject.revisions.length !== 1 || afterReject.revisions[0].id !== r.revisions[0].id || !t.includes(first.trim().replace(/^- /, ''))) {
                row.result = 'FAIL'; row.why.push('after reject: ' + JSON.stringify({ revisions: afterReject.revisions.map(x => x.newText), text: t, words }));
            }
            await page.evaluate(() => { window.__typedownReviewClockOffset = 0 });
        });
    }
    // Several paragraphs (links), several list items selected and deleted at once: a deletion each, each drawn as its
    // own strip, the middle one rejected alone (the user, 2026-11-07: three links deleted were one revision).
    for (const [what, text, from, count, blocks] of [
        ['paragraphs (links)', '# D\n\nBefore.\n\nhttps://example.com/one\n\nhttps://example.com/two\n\nhttps://example.com/three\n\nAfter.\n', 'https://example.com/one', 3, ['https://example.com/one', 'https://example.com/two', 'https://example.com/three']],
        ['list items', '# D\n\n- keep\n- one\n- two\n- three\n- last\n', '- one', 3, ['one', 'two', 'three']],
    ]) {
        for (const visual of [true, false]) {
            await flow(`${what} deleted together (${visual ? 'visual' : 'source'}): a deletion each, the middle rejected alone`, async row => {
                await mode(!visual, false);
                await load(text, text);
                // At the first block's start (a list item's text, in visual mode: its mark is not in the leaf).
                const at = text.indexOf(from) + (from.startsWith('- ') && visual ? 2 : 0);
                await caretAt(text, text, at, visual);
                await pause(80);
                // To the start of the block after the last one: the whole blocks selected, as with Shift+Down.
                await page.keyboard.down('Shift');
                // (In source mode a line at a time: the blank lines between paragraphs too.)
                const lines = visual ? count : text.slice(at, text.indexOf(blocks[blocks.length - 1]) + blocks[blocks.length - 1].length).split('\n').length + (text.includes('\n\n' + blocks[1]) ? 1 : 0);
                for (let i = 0; i < lines; i++) await page.keyboard.press('ArrowDown');
                await page.keyboard.up('Shift');
                await page.keyboard.press('Backspace');
                await pause(500);
                const r = await review();
                const t = await flush();
                row.n = r.revisions.length;
                const dels = r.revisions.filter(x => x.oldText && /\w/.test(x.oldText));
                const each = blocks.map(b => dels.filter(x => x.oldText.includes(b)).length);
                if (dels.length !== blocks.length || each.some(n => n !== 1)) { row.result = 'FAIL'; row.why.push('deletions: ' + JSON.stringify({ revisions: r.revisions.map(x => [x.oldText, x.newText]), text: t, at })); return; }
                if (visual) {
                    const strips = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-review-strip')].map(e => e.getAttribute('data-rid')));
                    if (dels.some(x => !strips.includes(x.id))) { row.result = 'FAIL'; row.why.push('strips: ' + JSON.stringify({ strips, ids: dels.map(x => x.id) })); }
                }
                // The middle one rejected with its own ✗: the mouse over its strip, then the button.
                const middle = dels[1];
                const over = await page.evaluate(id => {
                    const e = document.querySelector(`#ag-editor-id .ag-review-strip[data-rid="${id}"], .CodeMirror [data-rid="${id}"]`);
                    if (!e) return null;
                    const b = e.getBoundingClientRect();
                    return { x: b.left + 10, y: b.top + b.height / 2 };
                }, middle.id);
                if (!over) { row.result = 'FAIL'; row.why.push('the middle deletion is not drawn'); return; }
                await page.mouse.move(over.x, over.y);
                await pause(900);
                const cross = await page.evaluate(id => {
                    const a = document.getElementById('td-review-actions');
                    if (!a || a.dataset.for !== id || a.style.display === 'none') return null;
                    const b = a.querySelector('.td-review-reject').getBoundingClientRect();
                    return { x: b.left + b.width / 2, y: b.top + b.height / 2 };
                }, middle.id);
                if (!cross) { row.result = 'FAIL'; row.why.push('no buttons for the middle deletion'); return; }
                await page.mouse.click(cross.x, cross.y);
                await pause(400);
                const back = await flush();
                const after = await review();
                const left = after.revisions.filter(x => x.oldText && /\w/.test(x.oldText));
                if (!back.includes(blocks[1]) || back.includes(blocks[0]) || back.includes(blocks[2]) || left.length !== 2) {
                    row.result = 'FAIL'; row.why.push('after reject: ' + JSON.stringify({ text: back, left: left.map(x => x.oldText) }));
                }
            });
        }
    }
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
    // After the long document's load (its id 9001): a lower id is a stale load the page drops.
    loadId = 10000;
    for (const d of docs.filter((x, i) => x.raw && (docIndexes ? docIndexes.includes(i) : !only || only.some(o => x.name.startsWith(o))))) {
        const raw = (d.raw.bom ? '﻿' : '') + (d.raw.crlf ? d.text.replace(/\n/g, '\r\n') : d.text);
        await mode(false, false);
        try { await load(d.text, raw); } catch (err) {
            rows.push({ doc: d.name + ' (baseline raw)', mode: 'visual', place: '-', edit: 'load', result: 'FAIL', n: 0, marked: 0, paths: null, why: ['the load never finished; page errors: ' + errors.slice(-2).join(' / ').slice(0, 200)] });
            continue;
        }
        const r = await review();
        rows.push({ doc: d.name + ' (baseline raw)', mode: 'visual', place: '-', edit: 'load', result: r.revisions.length ? 'FAIL' : 'ok', n: r.revisions.length, marked: r.revisions.reduce((s, x) => s + x.newText.length + x.oldText.length, 0), paths: r.paths, why: r.revisions.length ? ['the raw file against the editor\'s text'] : [] });
    }

    await close();
    if (docIndexes) {
        // A worker's: the rows to the process that started it.
        fs.writeFileSync(jsonOut, JSON.stringify({ rows, errors: errorPlaces.length ? errorPlaces : errors }));
        process.exit(0);
    }
    report(rows, errorPlaces.length ? errorPlaces : errors, Date.now() - started);
})();

function firstDiff(a, b) {
    let i = 0;
    while (i < a.length && a[i] === b[i]) i++;
    return { at: i, got: a.slice(Math.max(0, i - 10), i + 20), want: b.slice(Math.max(0, i - 10), i + 20) };
}
