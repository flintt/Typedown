// How review mode draws its revisions in the visual editor, checked by the page's own geometry and styles (not by
// pictures): the two sets of samples the user went through (2026-11-07) - twenty list edits and twenty edits of other
// blocks, each loaded with one baseline. For each: how many revisions and of what kind; an insertion marked on its
// item's text, not over its marker; a block deleted whole drawn where it was - its own block, between the one before
// and the one after, of its kind (a list item in its list, a row among the rows), a line each when several, never
// inside another block or in its style; what the block marks say changed (a task ticked, an item indented, made a
// heading or a paragraph) and a split marked at the block's left; two blocks joined a "↵", not the marks; the colours
// and the line-through there; the buttons and who/when beside the revision; a formula still drawn.
//
//   node review-layout-check.js [--shots <dir>]   --shots: a picture of each sample too (not compared: the reference
//                                                 pictures come once the user has said how they should look)
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

const arg = name => { const i = process.argv.indexOf(name); return i > 0 ? process.argv[i + 1] : null; };
const shots = arg('--shots');

// (Replacements by function: a "$$" in a replacement string is one "$".)
const swap = (text, from, to) => { if (!text.includes(from)) throw new Error('sample: ' + from); return text.replace(from, () => to); };

const lists = '# 列表\n\n- 苹果\n- 香蕉\n- 橙子\n\n1. 第一步\n2. 第二步\n3. 第三步\n\n- [ ] 待办甲\n- [x] 待办乙\n\n- 外层\n  - 内层一\n  - 内层二\n\n结尾一段。\n';
const blocks = '# 标题一\n\n第一段文字。\n\n第二段文字。\n\n## 标题二\n\n> 引用第一行\n>\n> 引用第二段\n\n| 名称 | 数量 |\n|---|---|\n| 苹果 | 3 |\n| 香蕉 | 5 |\n\n```js\nlet a = 1\nlet b = 2\n```\n\n---\n\n$$\nx^2\n$$\n\n结尾一段。\n';

// [name, baseline, text, what is expected]. ghosts: the deleted blocks [kind, text in it, the text before, the text after]
// (before/after: the neighbours' text it is drawn between, null at an end); ins: a text marked inserted; structure,
// split, join: that mark; n: the number of revisions.
const L = (name, text, want) => [name, lists, text, want];
const B = (name, text, want) => [name, blocks, text, want];
const samples = [
    L('L01 无序列表末尾加一项', swap(lists, '- 橙子\n', '- 橙子\n- 葡萄\n'), { n: 1, ins: '葡萄' }),
    L('L02 无序列表中间加一项', swap(lists, '- 香蕉\n', '- 香蕉\n- 芒果\n'), { n: 1, ins: '芒果' }),
    L('L03 无序列表开头加一项', swap(lists, '- 苹果\n', '- 草莓\n- 苹果\n'), { n: 1, ins: '草莓' }),
    L('L04 删掉中间一项', swap(lists, '- 香蕉\n', ''), { n: 1, ghosts: [['li', '香蕉', '苹果', '橙子']] }),
    L('L05 删掉最后一项', swap(lists, '- 橙子\n', ''), { n: 1, ghosts: [['li', '橙子', '香蕉', '第一步']] }),
    L('L06 连删两项', swap(lists, '- 香蕉\n- 橙子\n', ''), { n: 2, ghosts: [['li', '香蕉', '苹果', '第一步'], ['li', '橙子', '苹果', '第一步']] }),
    L('L07 改一项里的字', swap(lists, '香蕉', '香蕉和芭蕉'), { n: 1, ins: '和芭蕉' }),
    L('L08 把一项整个换掉', swap(lists, '- 香蕉', '- 西瓜'), { n: 1, ins: '西瓜' }),
    L('L09 有序列表中间加一项', swap(swap(lists, '2. 第二步\n', '2. 第二步\n3. 新的一步\n'), '3. 第三步', '4. 第三步'), { n: 1, ins: '新的一步' }),
    L('L10 有序列表删掉一项', swap(swap(lists, '2. 第二步\n', ''), '3. 第三步', '2. 第三步'), { n: 1, ghosts: [['li', '第二步', '第一步', '第三步']] }),
    L('L11 待办勾上', swap(lists, '- [ ] 待办甲', '- [x] 待办甲'), { n: 1, structure: '待办甲' }),
    L('L12 待办加一项', swap(lists, '- [x] 待办乙\n', '- [x] 待办乙\n- [ ] 待办丙\n'), { n: 1, ins: '待办丙' }),
    L('L13 内层加一项', swap(lists, '  - 内层二\n', '  - 内层二\n  - 内层三\n'), { n: 1, ins: '内层三' }),
    L('L14 内层删一项', swap(lists, '  - 内层一\n', ''), { n: 1, ghosts: [['li', '内层一', '外层', '内层二']] }),
    L('L15 一项缩进成子项', swap(lists, '- 香蕉\n', '  - 香蕉\n'), { n: 1, structure: '香蕉' }),
    L('L16 子项提升为一项', swap(lists, '  - 内层二\n', '- 内层二\n'), { n: 1, structure: '内层二' }),
    L('L17 把一项拆成两项', swap(lists, '- 香蕉\n', '- 香\n- 蕉\n'), { n: 1, split: '蕉' }),
    L('L18 两项合成一项', swap(lists, '- 香蕉\n- 橙子\n', '- 香蕉橙子\n'), { n: 1, join: '香蕉橙子' }),
    L('L19 列表后加一段', swap(lists, '结尾一段。', '列表后新加的一段。\n\n结尾一段。'), { n: 1, ins: '列表后新加的一段。' }),
    L('L20 一项变成普通段落', swap(lists, '- 橙子\n', '\n橙子\n'), { structure: '橙子' }),
    B('B01 删一段（下一块是段落）', swap(blocks, '第一段文字。\n\n', ''), { n: 1, ghosts: [['p', '第一段文字。', '标题一', '第二段文字。']] }),
    B('B02 删一段（下一块是标题）', swap(blocks, '第二段文字。\n\n', ''), { n: 1, ghosts: [['p', '第二段文字。', '第一段文字。', '标题二']] }),
    B('B03 删一个标题', swap(blocks, '## 标题二\n\n', ''), { n: 1, ghosts: [['heading', '标题二', '第二段文字。', '引用第一行']] }),
    B('B04 删整个引用', swap(blocks, '> 引用第一行\n>\n> 引用第二段\n\n', ''), { n: 1, ghosts: [['quote', '引用第一行', '标题二', '名称']] }),
    B('B05 删引用里一段', swap(blocks, '>\n> 引用第二段\n', ''), { n: 1, ghosts: [['p', '引用第二段', '引用第一行', '名称']], inQuote: true }),
    B('B06 引用里加一段', swap(blocks, '> 引用第二段\n', '> 引用第二段\n>\n> 引用第三段\n'), { n: 1, ins: '引用第三段' }),
    B('B07 表格删一行', swap(blocks, '| 苹果 | 3 |\n', ''), { n: 1, ghosts: [['row', '苹果', '名称', '香蕉']] }),
    B('B08 表格加一行', swap(blocks, '| 香蕉 | 5 |\n', '| 香蕉 | 5 |\n| 橙子 | 7 |\n'), { n: 1, ins: '橙子', rowOnly: '橙子' }),
    B('B09 表格改一格', swap(blocks, '| 香蕉 | 5 |', '| 香蕉 | 50 |'), { n: 1, ins: '0' }),
    B('B10 删整个表格', swap(blocks, '| 名称 | 数量 |\n|---|---|\n| 苹果 | 3 |\n| 香蕉 | 5 |\n\n', ''), { n: 1, ghosts: [['table', '苹果', '引用第二段', 'let a']] }),
    B('B11 代码块里改一行', swap(blocks, 'let b = 2', 'let b = 20'), { n: 1 }),
    B('B12 删整个代码块', swap(blocks, '```js\nlet a = 1\nlet b = 2\n```\n\n', ''), { n: 1, ghosts: [['code', 'let a = 1\nlet b = 2', '香蕉', null]] }),
    B('B13 删分隔线', swap(blocks, '---\n\n$$', '$$'), { n: 1, ghosts: [['hr', '', 'let b', null]] }),
    B('B14 删公式块', swap(blocks, '$$\nx^2\n$$\n\n', ''), { n: 1, ghosts: [['math', '', null, '结尾一段。']], noMath: true }),
    B('B15 删最后一段', swap(blocks, '$$\n\n结尾一段。\n', '$$\n'), { n: 1, ghosts: [['p', '结尾一段。', null, null]] }),
    B('B16 连删两段', swap(blocks, '第一段文字。\n\n第二段文字。\n\n', ''), { n: 2, ghosts: [['p', '第一段文字。', '标题一', '标题二'], ['p', '第二段文字。', '标题一', '标题二']] }),
    B('B17 标题改字', swap(blocks, '## 标题二', '## 新的标题二'), { n: 1, ins: '新的' }),
    B('B18 段落改成标题', swap(blocks, '第二段文字。', '### 第二段文字。'), { n: 1, structure: '第二段文字。' }),
    B('B19 两段合成一段', swap(blocks, '第一段文字。\n\n第二段文字。', '第一段文字。第二段文字。'), { n: 1, join: '第一段文字。第二段文字。' }),
    B('B20 一段拆成两段', swap(blocks, '第一段文字。', '第一段\n\n文字。'), { n: 1, split: '文字。' }),
];

(async () => {
    const { page, errors, mode, pause, close } = await openEditor();
    let ok = true;
    const fail = (name, what, detail) => { ok = false; console.log(`FAIL ${name}: ${what}${detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); };
    try {
        await page.setViewport({ width: 900, height: 1000, deviceScaleFactor: 1 });
        await mode(false, false);
        if (shots) fs.mkdirSync(shots, { recursive: true });
        let loadId = 900;
        for (const [name, baseline, text, want] of samples) {
            const problems = [];
            const id = ++loadId;
            await page.evaluate(({ text, baseline, id }) => {
                delete window.__last.FileLoaded;
                window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp', review: { on: true, baseline, revisions: [] } });
            }, { text, baseline, id });
            await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
            await pause(700);
            const seen = await page.evaluate(want => {
                const root = document.querySelector('#ag-editor-id');
                const review = window.__typedownReview();
                const box = e => { const r = e.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, left: r.left, right: r.right }; };
                // A leaf's or a ghost's element by the text in it (the first leaf, a cell, a code line).
                const leafOf = t => t == null ? null : [...root.querySelectorAll('.ag-paragraph-content, .ag-atx-line, th, td, .ag-code-content, .ag-math-render, span.ag-paragraph')]
                    .find(e => !e.closest('.ag-review-ghost') && e.textContent.includes(t));
                const del = getComputedStyle(document.body).getPropertyValue('--reviewDel').trim();
                const ghosts = [...root.querySelectorAll('.ag-review-ghost')].map(g => {
                    const s = getComputedStyle(g);
                    const struck = [g, ...g.querySelectorAll('*')].some(e => getComputedStyle(e).textDecorationLine.includes('line-through'));
                    return { rid: g.getAttribute('data-rid'), kind: [...g.classList].find(c => /^ag-review-ghost-(?!marker|text|nested|card|h\d)/.test(c))?.replace('ag-review-ghost-', ''),
                        text: g.textContent, box: box(g), inLeaf: !!g.parentElement.closest('.ag-paragraph-content, .ag-atx-line, p, h1, h2, h3, h4, h5, h6, pre, td, th, .ag-math-render'),
                        parentTag: g.parentElement.tagName, struck, inQuote: !!g.closest('blockquote'), card: !!g.querySelector('.ag-review-ghost-card'), color: s.color, fontSize: parseFloat(s.fontSize), lines: g.getClientRects().length };
                });
                const near = t => { const e = leafOf(t); return e ? box(e) : null; };
                const marks = sel => [...root.querySelectorAll(sel)].map(e => ({ text: e.textContent, rid: e.getAttribute('data-rid'), box: box(e) }));
                return {
                    revisions: review.revisions, ghosts, del,
                    bodyFont: parseFloat(getComputedStyle(root.querySelector('.ag-paragraph-content') || root).fontSize),
                    before: (want.ghosts || []).map(g => near(g[2])), after: (want.ghosts || []).map(g => near(g[3])),
                    ins: marks('.ag-review-ins, .ag-review-whole'), structure: marks('.ag-review-structure'), split: marks('.ag-review-split'),
                    joins: [...root.querySelectorAll('[data-del-before], [data-del-after]')].map(e => e.getAttribute('data-del-before') ?? e.getAttribute('data-del-after')),
                    shownMarks: [...root.querySelectorAll('[data-del-before], [data-del-after]')].map(e => e.getAttribute('data-del-before') ?? e.getAttribute('data-del-after')).filter(t => /(^|\s)[-*+>](\s|$)|¶/.test(t)),
                    math: root.querySelectorAll('.katex').length,
                    // A table's bars: the rows with one (their first cell's), and whether the table itself has one.
                    rowBars: [...root.querySelectorAll('tr')].filter(tr => tr.firstElementChild && getComputedStyle(tr.firstElementChild).boxShadow !== 'none').map(tr => tr.textContent),
                    cellBars: [...root.querySelectorAll('td, th')].filter(c => c !== c.parentElement.firstElementChild && getComputedStyle(c).boxShadow !== 'none').length,
                    tableBar: [...root.querySelectorAll('figure')].some(f => f.querySelector('table') && getComputedStyle(f).boxShadow !== 'none'),
                    bullets: [...root.querySelectorAll('li.ag-list-item')].length,
                };
            }, want);
            if (want.n !== undefined && seen.revisions.length !== want.n) problems.push(['revisions', seen.revisions.map(r => [r.kind, r.oldText, r.newText])]);
            if (!want.n && !seen.revisions.length) problems.push(['no revision', null]);
            for (const [i, [kind, inside, before, after]] of (want.ghosts || []).entries()) {
                const g = seen.ghosts.filter(x => x.kind === kind && x.text.replace(/\s+/g, '').includes(inside.replace(/\s+/g, '')))[i - (want.ghosts.slice(0, i).filter(w => w[0] !== kind).length)] ?? seen.ghosts.find(x => x.kind === kind && x.text.replace(/\s+/g, '').includes(inside.replace(/\s+/g, '')));
                if (!g) { problems.push([`no ${kind} ghost with "${inside}"`, seen.ghosts.map(x => [x.kind, x.text])]); continue; }
                if (!g.rid || !seen.revisions.some(r => r.id === g.rid)) problems.push([`ghost "${inside}" not its revision's`, g.rid]);
                if (g.inLeaf) problems.push([`ghost "${inside}" inside a block`, g.parentTag]);
                if (!g.struck && kind !== 'hr') problems.push([`ghost "${inside}" not struck through`, null]);
                if (kind !== 'heading' && Math.abs(g.fontSize - seen.bodyFont) > 1.5 && kind !== 'code' && kind !== 'table' && kind !== 'row') problems.push([`ghost "${inside}" in another size`, [g.fontSize, seen.bodyFont]]);
                const b = seen.before[i], a = seen.after[i];
                if (b && g.box.top < b.bottom - 2) problems.push([`ghost "${inside}" not after "${before}"`, [g.box, b]]);
                if (a && g.box.bottom > a.top + 2) problems.push([`ghost "${inside}" not before "${after}"`, [g.box, a]]);
                if (!!want.inQuote !== g.inQuote) problems.push([`ghost "${inside}" ${want.inQuote ? 'not in' : 'in'} a quotation`, null])
                if ((kind === 'math' || kind === 'diagram') && !g.card) problems.push([`no card for the deleted ${kind}`, g.text])
                if (kind === 'li' && g.parentTag !== 'UL' && g.parentTag !== 'OL' && g.parentTag !== 'LI') problems.push([`item "${inside}" not in a list`, g.parentTag]);
                if (kind === 'row' && g.parentTag !== 'TBODY' && g.parentTag !== 'THEAD' && g.parentTag !== 'TABLE') problems.push([`row "${inside}" not among rows`, g.parentTag]);
            }
            // Several deleted: one line (box) each, none over another.
            const tops = seen.ghosts.map(g => g.box.top).sort((x, y) => x - y);
            for (let k = 1; k < tops.length; k++) if (tops[k] - tops[k - 1] < 8) problems.push(['two ghosts on one line', tops]);
            if (want.ins && !seen.ins.some(m => m.text.includes(want.ins) || want.ins.includes(m.text) && m.text)) problems.push([`"${want.ins}" not marked inserted`, seen.ins.map(m => m.text)]);
            if (want.structure && !seen.structure.some(m => m.text.includes(want.structure) && m.rid)) problems.push([`no structure mark on "${want.structure}"`, seen.structure]);
            if (want.split && !seen.split.some(m => m.text.includes(want.split) && m.rid)) problems.push([`no split mark on "${want.split}"`, seen.split]);
            if (want.join && !seen.joins.includes('↵')) problems.push(['no "↵" where two blocks were joined', seen.joins]);
            if (want.rowOnly && (seen.rowBars.length !== 1 || !seen.rowBars[0].includes(want.rowOnly) || seen.cellBars || seen.tableBar)) problems.push(['a bar beside other rows or the table', [seen.rowBars, seen.cellBars, seen.tableBar]])
            if (seen.shownMarks.length) problems.push(['block marks shown as deleted text', seen.shownMarks]);
            if (baseline === blocks && !want.noMath && seen.math < 1) problems.push(['the formula is not drawn', null]);
            // The buttons beside the first revision, with who and when.
            const first = seen.revisions[0];
            if (first) {
                const at = await page.evaluate(id => {
                    const e = document.querySelector(`#ag-editor-id [data-rid="${id}"]`);
                    if (!e) return null;
                    const r = e.getBoundingClientRect();
                    return { x: r.left + Math.min(10, r.width / 2), y: r.top + Math.min(r.height / 2, 10) };
                }, first.id);
                if (!at) problems.push(['the first revision has no element to point at', first]);
                else {
                    await page.mouse.move(at.x, at.y);
                    await pause(700);
                    const actions = await page.evaluate(() => {
                        const a = document.getElementById('td-review-actions');
                        if (!a || a.style.display === 'none') return null;
                        const r = a.getBoundingClientRect();
                        return { for: a.dataset.for, top: r.top, meta: a.querySelector('.td-review-meta')?.textContent ?? '' };
                    });
                    if (!actions || actions.for !== first.id) problems.push(['no buttons for the revision under the mouse', actions]);
                    else {
                        if (Math.abs(actions.top - at.y) > 40) problems.push(['the buttons are not beside it', [actions.top, at.y]]);
                        if (!/\d{1,2}:\d\d/.test(actions.meta)) problems.push(['no time beside the buttons', actions.meta]);
                    }
                    await page.mouse.move(5, 990);
                }
            }
            if (shots) {
                const el = await page.$('#ag-editor-id');
                await el.screenshot({ path: path.join(shots, name.split(' ')[0] + '.png') });
            }
            if (problems.length) for (const [what, detail] of problems) fail(name, what, detail);
            else console.log(`PASS ${name}`);
        }
        if (errors.length) fail('page', 'errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
