// A section moved by dragging its heading in the outline (docs/todo.md 2026-11-20 用户清单 28, as Obsidian does): the host
// sends MoveSection { from, to, where } with the headings' slugs; the heading moves with everything under it up to the next
// heading of its level or higher, its sub-sections included - before or after the target as its sibling, or into it as its
// last sub-section - every heading in it raised or lowered by the same steps. One change for the host (one undo). Refused,
// with the text as it was: into its own section, or a heading past level 6. A '#' line in a code block is not a heading;
// an underlined heading turned past level 2 is written with '#'.
//
//   node outline-move-check.js
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, load, flush, pause, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(400); };
    const toc = () => page.evaluate(() => window.__typedownMuya.getTOC().toc.map(t => ({ content: t.content, lvl: t.lvl, slug: t.slug })));
    const slugOf = async content => (await toc()).find(t => t.content === content)?.slug;
    let sent = 0;
    const move = async (from, to, where) => {
        const f = await slugOf(from), t = await slugOf(to);
        const changes = await page.evaluate(() => window.__count.MarkdownChange || 0);
        const token = ++sent;
        await page.evaluate(({ f, t, where, token }) => { delete window.__last.SectionMoved; window.__deliver('MoveSection', { from: f, to: t, where, token }) }, { f, t, where, token });
        try { await page.waitForFunction(token => window.__last.SectionMoved?.token === token, { timeout: 5000 }, token); } catch { }
        await pause(300);
        const reply = await page.evaluate(() => window.__last.SectionMoved ?? null);
        return { reply, changes: await page.evaluate(() => window.__count.MarkdownChange || 0) - changes, text: await flush() };
    };
    try {
        const doc = 'Intro.\n\n# A\n\na text\n\n## A1\n\na1 text\n\n# B\n\nb text\n\n## B1\n\nb1\n';

        // After a sibling: the whole section, its sub-section with it.
        await visual(doc);
        let r = await move('A', 'B', 'after');
        check(r.reply?.ok === true, 'A after B: done', r.reply);
        check(r.text === 'Intro.\n\n# B\n\nb text\n\n## B1\n\nb1\n\n# A\n\na text\n\n## A1\n\na1 text\n', 'A after B: A and A1 after B\'s section', r.text);
        check(r.changes === 1, 'A after B: one change for the host (one undo)', r.changes);

        // Into another section: its last sub-section, every heading one level down.
        await visual(doc);
        r = await move('B', 'A', 'inside');
        check(r.text === 'Intro.\n\n# A\n\na text\n\n## A1\n\na1 text\n\n## B\n\nb text\n\n### B1\n\nb1\n', 'B into A: after A1, B and B1 a level down', r.text);
        const levels = (await toc()).map(t => `${t.content}:${t.lvl}`).join(' ');
        check(levels === 'A:1 A1:2 B:2 B1:3', 'B into A: the outline follows', levels);

        // Out of its section, before a heading of a higher level: up to that level.
        await visual(doc);
        r = await move('A1', 'A', 'before');
        check(r.text === 'Intro.\n\n# A1\n\na1 text\n\n# A\n\na text\n\n# B\n\nb text\n\n## B1\n\nb1\n', 'A1 before A: a level 1 section of its own', r.text);
        check(r.text.startsWith('Intro.\n\n'), 'what is before the first heading stays first');

        // A '#' line in a code block is not a heading: it goes with its section.
        await visual('# A\n\n```\n# not a heading\n```\n\n# B\n\nb\n');
        r = await move('A', 'B', 'after');
        check(r.text === '# B\n\nb\n\n# A\n\n```\n# not a heading\n```\n', 'a code block\'s # line moves inside its section', r.text);

        // An underlined heading made level 3: written with '#'; at level 2 it stays underlined.
        await visual('# Top\n\nTitle\n-----\n\nt\n\n# Next\n\n## Other\n\no\n');
        r = await move('Title', 'Other', 'inside');
        check(r.text === '# Top\n\n# Next\n\n## Other\n\no\n\n### Title\n\nt\n', 'an underlined heading moved to level 3 is written with #', r.text);
        await visual('# Top\n\nTitle\n-----\n\nt\n\n# Next\n\nn\n');
        r = await move('Title', 'Next', 'inside');
        check(r.text === '# Top\n\n# Next\n\nn\n\nTitle\n-----\n\nt\n', 'an underlined heading staying at level 2 stays underlined', r.text);

        // Refused: past level 6, into its own section. The text stays, no change sent.
        // (F not in E's section: a level 5 heading between ends it.)
        await visual('##### E\n\n###### E1\n\n##### G\n\n###### F\n\nf\n');
        const deep = await flush();
        r = await move('E', 'F', 'inside');
        check(r.reply?.ok === false && r.reply?.reason === 'tooDeep' && r.text === deep && r.changes === 0, 'past level 6: refused, nothing changed', r);
        await visual(doc);
        r = await move('A', 'A1', 'inside');
        check(r.reply?.ok === false && r.reply?.reason === 'ownSection' && r.text === doc && r.changes === 0, 'into its own section: refused, nothing changed', r);

        // Reading mode takes no move.
        await visual(doc);
        await mode(false, true);
        r = await move('A', 'B', 'after');
        check(r.reply?.ok === false && r.text === doc && r.changes === 0, 'reading mode: refused, nothing changed', r);
        await mode(false, false);

        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        console.log('FAIL ' + (e && e.stack || e));
        ok = false;
    } finally {
        await close();
    }
    process.exit(ok ? 0 : 1);
})();
