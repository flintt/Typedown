// A list item dragged by the handle shown beside it (a reader's request, 2026-11-02; the handle alone, not the marker):
// up and down its list, deeper under the item above (dragged right) and out again (left), with the items nested in
// it; an ordered list renumbered; Escape drops a drag; a drag from the marker moves nothing. The host gets the new
// text at once.
//
//   node list-drag-check.js
const { firstDifference, openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  // Where an item's marker and its text line are (by the item's own text), and the list's indent.
  const geometry = text => page.evaluate(text => {
    const li = [...document.querySelectorAll('#ag-editor-id li')].find(el => {
      const own = [...el.children].find(c => !/^(INPUT|UL|OL)$/.test(c.tagName))
      return own && own.textContent.trim() === text
    })
    const own = [...li.children].find(c => !/^(INPUT|UL|OL)$/.test(c.tagName)).getBoundingClientRect()
    return { markerX: own.left - 8, textX: own.left + 4, y: own.top + own.height / 2, top: own.top, bottom: own.bottom }
  }, text);
  const visual = async md => { await mode(true, false); await load(md); await mode(false, false); await pause(400); };
  const drag = async (item, to, { before = true, dx = 0, escape = false } = {}) => {
    const from = await geometry(item);
    const target = await geometry(to);
    const y = before ? target.top + 2 : target.bottom - 2;
    // The pointer over the item's text brings the handle; the drag is from there.
    await page.mouse.move(from.textX, from.y);
    await pause(150);
    const h = await page.evaluate(() => { const r = document.getElementById('mu-list-drag-handle').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
    await page.mouse.move(h.x, h.y);
    await page.mouse.down();
    await page.mouse.move(h.x + 2, h.y + 6, { steps: 2 });
    await page.mouse.move(h.x + dx, y, { steps: 8 });
    await pause(100);
    if (escape) await page.keyboard.press('Escape');
    await page.mouse.up();
    await pause(400);
  };
  const cases = [
    ['the last item to the top', '- a\n- b\n- c\n', ['c', 'a', {}], '- c\n- a\n- b\n'],
    ['the first item below the second', '- a\n- b\n- c\n', ['a', 'c', {}], '- b\n- a\n- c\n'],
    ['an item dragged right goes under the one above', '- a\n- b\n- c\n', ['c', 'c', { before: false, dx: 40 }], '- a\n- b\n  - c\n'],
    ['a nested item dragged left comes out a level', '- a\n  - b\n- c\n', ['b', 'b', { before: false, dx: -40 }], '- a\n- b\n- c\n'],
    ['an item goes with what is nested in it', '- a\n  - a1\n- b\n', ['a', 'b', { before: false }], '- b\n- a\n  - a1\n'],
    ['an ordered list is renumbered', '1. a\n2. b\n3. c\n', ['c', 'a', {}], '1. c\n2. a\n3. b\n'],
    ['Escape drops the drag', '- a\n- b\n- c\n', ['c', 'a', { escape: true }], '- a\n- b\n- c\n'],
  ];
  try {
    for (const [what, md, [item, to, how], expected] of cases) {
      await visual(md);
      await drag(item, to, how);
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const host = await flush();
      report(held === expected, `${what}: the document`, firstDifference(expected, held));
      report(host === held, `${what}: the host has it`, firstDifference(held, host));
    }
    // From a loose list into a list made for it: alone there it is tight, so an item added after it is too - it kept the
    // loose list's spacing, and a list grown from it had blank lines between its items (a reader, 2026-11-03).
    await visual('- a\n\n- b\n\n- c\n');
    await drag('c', 'c', { before: false, dx: 40 });
    const moved = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(moved === '- a\n\n- b\n\n  - c\n', 'from a loose list into a new nested list: the document', firstDifference('- a\n\n- b\n\n  - c\n', moved));
    const cAt = await geometry('c');
    await page.mouse.click(cAt.textX + 30, cAt.y);
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
    await page.keyboard.type('d');
    await pause(300);
    const grown = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(/ {2}- c\n {2}- d\n/.test(grown), 'and an item added after it keeps the nested list tight (no blank line between)', JSON.stringify(grown));
    // A drag keeps the spacing of the list it lands in: tight stays tight, across levels too.
    for (const [md, how, expected] of [
      ['- a\n  - a1\n  - a2\n- b\n- c\n', ['c', 'a2', { before: false, dx: 40 }], null],
      ['1. A\n\n   1. a1\n   2. a2\n\n2. B\n\n   1. b1\n   2. b2\n', ['b2', 'a2', {}], '1. A\n\n   1. a1\n   2. b2\n   3. a2\n\n2. B\n\n   1. b1\n'],
    ]) {
      await visual(md);
      await drag(...how);
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const blankIn = (text, from) => text.split('\n').filter(l => l === '').length - from.split('\n').filter(l => l === '').length;
      report(expected ? held === expected : blankIn(held, md) <= 0, `a drag adds no blank line (${JSON.stringify(md)})`, JSON.stringify(held));
    }

    // Into another list and out of the lists (docs/todo.md "列表拖动：跨列表与拖出列表", A and B). The pointer goes to a
    // place given as [text of a block or item, where]: 'before' (its first line's top), 'after' (its bottom), 'gap'
    // (halfway between it and the next block), 'end' (below the document's last block).
    const blockAt = (text, where) => page.evaluate(({ text, where }) => {
      const root = document.querySelector('#ag-editor-id');
      const own = el => el.tagName === 'LI' ? [...el.children].find(c => !/^(INPUT|UL|OL)$/.test(c.tagName)) : el;
      const words = e => { const c = e.cloneNode(true); c.querySelectorAll('.ag-front-icon').forEach(i => i.remove()); return c.textContent.trim(); };
      const el = [...root.querySelectorAll('li, p, h1, h2, h3')].find(e => {
        if (e.tagName === 'P' && e.closest('li')) return false;
        const o = own(e);
        return o && words(o) === text;
      });
      if (!el) return null;
      const r = own(el).getBoundingClientRect();
      if (where === 'before') return r.top + 2;
      if (where === 'after') return r.bottom - 2;
      if (where === 'end') return root.lastElementChild.getBoundingClientRect().bottom + 12;
      // 'gap': between this top-level block and the one after it.
      let block = el;
      while (block.parentElement && block.parentElement !== root && block.parentElement.tagName !== 'BLOCKQUOTE') block = block.parentElement;
      const below = block.nextElementSibling;
      return (block.getBoundingClientRect().bottom + below.getBoundingClientRect().top) / 2;
    }, { text, where });
    // The document's structure as the page shows it: lists with their kind, items with their text and box.
    const shape = () => page.evaluate(() => {
      const words = e => { const c = e.cloneNode(true); c.querySelectorAll('.ag-front-icon').forEach(i => i.remove()); return c.textContent.trim(); };
      const walk = el => {
        const kids = [...el.children].filter(c => /^(UL|OL|LI|BLOCKQUOTE|P|H\d)$/.test(c.tagName));
        if (el.tagName === 'LI') {
          const box = el.querySelector(':scope > input');
          const own = [...el.children].find(c => !/^(INPUT|UL|OL)$/.test(c.tagName));
          return `li${box ? (box.checked ? '[x]' : '[ ]') : ''}(${own ? words(own) : ''}${[...el.children].filter(c => /^(UL|OL)$/.test(c.tagName)).map(walk).join('')})`;
        }
        if (/^(UL|OL)$/.test(el.tagName)) return `${el.tagName.toLowerCase()}${el.tagName === 'OL' ? `@${el.getAttribute('start') || 1}` : ''}{${kids.map(walk).join(',')}}`;
        if (el.tagName === 'BLOCKQUOTE') return `quote{${kids.map(walk).join(',')}}`;
        return `${el.tagName.toLowerCase()}(${words(el)})`;
      };
      return [...document.querySelector('#ag-editor-id').children].map(walk).join(' ');
    });
    const placeLine = () => page.evaluate(() => { const g = document.getElementById('mu-list-drag-ghost'); return g ? { place: g.dataset.place, left: parseFloat(g.style.left) } : null; });
    const dragTo = async (item, [to, where], { dx = 0, hold } = {}) => {
      const from = await geometry(item);
      await page.mouse.move(from.textX, from.y);
      await pause(150);
      const h = await page.evaluate(() => { const r = document.getElementById('mu-list-drag-handle').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
      await page.mouse.move(h.x, h.y);
      await page.mouse.down();
      await page.mouse.move(h.x + 2, h.y + 6, { steps: 2 });
      const y = await blockAt(to, where);
      if (y == null) throw new Error(`no block "${to}"`);
      await page.mouse.move(h.x + dx, y, { steps: 10 });
      await pause(120);
      const line = await placeLine();
      if (hold) await hold(line);
      await page.mouse.up();
      await pause(400);
      return line;
    };
    // The Markdown the editor writes for a text: loaded and read back.
    const canonical = async md => { await visual(md); return page.evaluate(() => window.__typedownMuya.getMarkdown()); };
    const across = [
      ['A: into another list two paragraphs below (its marker taken)', '- a\n- b\n\npara one\n\npara two\n\n* c\n* d\n', ['a', ['d', 'before']], '- b\n\npara one\n\npara two\n\n* c\n* a\n* d\n', 'list'],
      ['A: up into another list above', '- a\n- b\n\npara\n\n- c\n- d\n', ['d', ['b', 'after'], { dx: 0 }], '- a\n- b\n- d\n\npara\n\n- c\n', 'list'],
      ["A: into a quotation's list", '- a\n- b\n\ntext\n\n> quote\n>\n> - q1\n> - q2\n', ['a', ['q2', 'before']], '- b\n\ntext\n\n> quote\n>\n> - q1\n> - a\n> - q2\n', 'list'],
      ['A: an ordered item into an unordered list', '1. one\n2. two\n\npara\n\n- x\n- y\n', ['two', ['y', 'before']], '1. one\n\npara\n\n- x\n- two\n- y\n', 'list'],
      ['A: an unordered item into an ordered list keeps its start number', '- a\n- b\n\npara\n\n3. x\n4. y\n', ['a', ['x', 'before']], '- b\n\npara\n\n3. a\n4. x\n5. y\n', 'list'],
      ['A: a task item into a task list keeps its box, takes the marker', '- [ ] t1\n- [x] t2\n\npara\n\n* [ ] u1\n* [ ] u2\n', ['t2', ['u2', 'before']], '- [ ] t1\n\npara\n\n* [ ] u1\n* [x] t2\n* [ ] u2\n', 'list'],
      // Muya reads tasks among bulleted items as lists of their own (MarkText #870): shown so, as it will read back.
      ['A: a task item among bulleted ones keeps its box, a list of tasks between them', '- [ ] t1\n- [x] t2\n\npara\n\n* p\n* q\n', ['t2', ['q', 'before']], '- [ ] t1\n\npara\n\n* p\n\n* [x] t2\n\n* q\n', 'list'],
      ['A: a loose item into a tight list is tight', '- a\n\n- b\n\n- c\n\npara\n\n+ x\n+ y\n', ['b', ['y', 'before']], '- a\n\n- c\n\npara\n\n+ x\n+ b\n+ y\n', 'list'],
      ['A: a tight item into a loose list is loose', '- a\n\n- c\n\npara\n\n+ x\n+ y\n', ['x', ['c', 'before']], '- a\n\n- x\n\n- c\n\npara\n\n+ y\n', 'list'],
      ['A: with its nested items, nested in the target', '- a\n  - a1\n- b\n\npara\n\n- c\n  - c1\n', ['a', ['c1', 'after'], { dx: 40 }], '- b\n\npara\n\n- c\n  - c1\n  - a\n    - a1\n', 'list'],
      ['A: a list left empty goes', '- only\n\npara\n\n- x\n', ['only', ['x', 'after']], 'para\n\n- x\n- only\n', 'list'],
      ['B: out of the list between two paragraphs, a list of its own', '- a\n- b\n\npara one\n\npara two\n', ['a', ['para one', 'gap']], '- b\n\npara one\n\n- a\n\npara two\n', 'out'],
      ["B: at the document's end", '- a\n- b\n\npara one\n\npara two\n', ['a', ['para two', 'end']], '- b\n\npara one\n\npara two\n\n- a\n', 'out'],
      ['B: keeps its nested items and its box', '- [x] a\n  - a1\n- b\n\np1\n\np2\n', ['a', ['p1', 'gap']], '- b\n\np1\n\n- [x] a\n  - a1\n\np2\n', 'out'],
      ['B: an ordered item keeps its marker', '1) a\n2) b\n\np1\n\np2\n', ['b', ['p1', 'gap']], '1) a\n\np1\n\n1) b\n\np2\n', 'out'],
      ['B: between a heading and a paragraph', '# Title\n\nbody\n\n- a\n- b\n', ['b', ['body', 'before']], '# Title\n\n- b\n\nbody\n\n- a\n', 'out'],
      ['B: next to a list of the same kind and marker it joins that list', '- a\n- b\n\npara\n\n- x\n- y\n\nend\n', ['a', ['y', 'gap']], '- b\n\npara\n\n- x\n- y\n- a\n\nend\n', 'list'],
      ['B: next to a list of another marker it stays a list of its own', '- a\n- b\n\npara\n\n* x\n* y\n\nend\n', ['a', ['y', 'gap']], '- b\n\npara\n\n* x\n* y\n- a\n\nend\n', 'out'],
      ['B: a task next to a bulleted list of its marker stays a list of its own', '- [x] t\n\npara\n\n- x\n\nend\n', ['t', ['x', 'gap']], 'para\n\n- x\n\n- [x] t\n\nend\n', 'out'],
      ['B: 1. and 1) are lists apart', '1. a\n2. b\n\npara\n\n1) x\n\nend\n', ['a', ['x', 'gap']], '1. b\n\npara\n\n1) x\n1. a\n\nend\n', 'out'],
      ['B: the only item out of its list, which goes; lists either side of it the same kind become one', '- a\n\n1. x\n\n- b\n\npara\n', ['x', ['para', 'end']], '- a\n- b\n\npara\n\n1. x\n', 'out'],
    ];
    for (const [what, md, [item, to, how], expectedMd, place] of across) {
      const expected = await canonical(expectedMd);
      const expectedShape = await shape();
      await visual(md);
      const changes = await page.evaluate(() => window.__count.MarkdownChange || 0);
      const line = await dragTo(item, to, how);
      const held = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      const shown = await shape();
      const host = await flush();
      const sent = await page.evaluate(() => window.__count.MarkdownChange || 0) - changes;
      report(held === expected, `${what}: the document`, firstDifference(expected, held));
      report(shown === expectedShape, `${what}: the page shows it`, `${shown} | expected ${expectedShape}`);
      report(host === held, `${what}: the host has it`, firstDifference(held, host));
      // One change: the host's undo takes it back in one step.
      report(sent === 1, `${what}: one change for the host (one undo)`, `${sent} changes`);
      if (place) report(line && line.place === place, `${what}: the line shows it ${place === 'out' ? 'out of the lists' : 'in a list'}`, JSON.stringify(line));
      // What is saved reads back the same: the same lists, the same text.
      await visual(held);
      const again = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      report(again === held && await shape() === shown, `${what}: saved, it reads back the same`, `${again === held ? '' : firstDifference(held, again)} ${await shape()} | ${shown}`);
    }
    // B's line is drawn at the level of the blocks, left of where an item's text would be (A's line at level 0).
    await visual('- a\n- b\n\npara one\n\npara two\n\n- c\n- d\n');
    let lines = [];
    await dragTo('a', ['para one', 'gap'], { hold: async l => lines.push(l) });
    await visual('- a\n- b\n\npara one\n\npara two\n\n- c\n- d\n');
    await dragTo('a', ['d', 'before'], { hold: async l => lines.push(l) });
    report(lines[0] && lines[1] && lines[0].place === 'out' && lines[1].place === 'list' && lines[0].left < lines[1].left - 8, "B's line starts left of A's (at the blocks' edge, not an item's text)", JSON.stringify(lines));
    // Escape drops a drag to another list; not into a code block or a table.
    await visual('- a\n- b\n\npara\n\n- c\n');
    await dragTo('a', ['c', 'after'], { hold: async () => { await page.keyboard.press('Escape'); } });
    report(await page.evaluate(() => window.__typedownMuya.getMarkdown()) === '- a\n- b\n\npara\n\n- c\n', 'Escape drops a drag to another list', await page.evaluate(() => window.__typedownMuya.getMarkdown()));
    // Near the window's bottom edge the page scrolls, and the place follows it with the pointer still: dropped there,
    // the item joins the list at the document's end (same marker).
    const filler = Array.from({ length: 60 }, (_, i) => `paragraph ${i}`).join('\n\n');
    await visual(`- a\n- b\n\n${filler}\n\n- z1\n- z2\n`);
    await page.evaluate(() => window.scrollTo(0, 0));
    const viewH = await page.evaluate(() => window.innerHeight);
    let scrolled = null;
    await dragTo('a', ['paragraph 3', 'gap'], { hold: async () => {
      await page.mouse.move(60, viewH - 4, { steps: 4 });
      const before = await page.evaluate(() => ({ y: window.scrollY, line: document.getElementById('mu-list-drag-ghost')?.style.top }));
      await pause(4000);
      const after = await page.evaluate(() => ({ y: window.scrollY, max: document.documentElement.scrollHeight - window.innerHeight, line: document.getElementById('mu-list-drag-ghost')?.dataset.place }));
      scrolled = { before, after };
    } });
    report(scrolled.after.y > scrolled.before.y + 500 && scrolled.after.y >= scrolled.after.max - 2, 'near the bottom edge the page scrolls to the end, the pointer still', JSON.stringify(scrolled));
    const tail = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(tail.endsWith('- z1\n- z2\n- a\n') && tail.startsWith('- b\n\nparagraph 0'), 'and dropped there the item goes where the page scrolled to', JSON.stringify(tail.slice(-40)));
    // Faster nearer the edge: a short wait near it scrolls less than at it.
    await visual(`- a\n- b\n\n${filler}\n\n- z1\n- z2\n`);
    await page.evaluate(() => window.scrollTo(0, 0));
    const speeds = [];
    for (const off of [40, 2]) {
      await page.evaluate(() => window.scrollTo(0, 0));
      await pause(200);
      await dragTo('a', ['paragraph 3', 'gap'], { hold: async () => {
        await page.mouse.move(60, viewH - off, { steps: 2 });
        const y0 = await page.evaluate(() => window.scrollY);
        await pause(500);
        speeds.push(await page.evaluate(() => window.scrollY) - y0);
        await page.keyboard.press('Escape');
      } });
    }
    report(speeds[0] > 0 && speeds[1] > speeds[0] * 1.5, 'faster nearer the edge', JSON.stringify(speeds));
    // And up at the top edge.
    await visual(`- a\n- b\n\n${filler}\n\n- z1\n- z2\n`);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    await pause(300);
    await dragTo('z2', ['paragraph 58', 'gap'], { hold: async () => {
      await page.mouse.move(60, 3, { steps: 4 });
      await pause(4000);
    } });
    const head = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(head.startsWith('- z2\n- a\n- b\n') && await page.evaluate(() => window.scrollY) < 2, 'at the top edge it scrolls up, and the item goes to the top', JSON.stringify(head.slice(0, 40)));
    // A long document drawn a part at a time: pressing the handle draws the rest (as any press in the page does, so the
    // whole document is there to drop in - and blocks drawn in during a drag would be places too: the drag watches for
    // them); dragged to the bottom edge, the page scrolls to the list at its end, which takes the item.
    const longFiller = Array.from({ length: 700 }, (_, i) => `long paragraph number ${i} with some words in it`).join('\n\n');
    await page.evaluate(() => { window.__typedownProgressiveDelayMs = 60; window.__typedownProgressiveSliceMs = 1; });
    // Loaded in the visual editor (a switch from source mode draws it whole).
    await mode(false, false);
    await load(`- a\n- b\n\n${longFiller}\n\n- z1\n- z2\n- z3\n`);
    await pause(100);
    let drawnAtStart = null;
    let progressive = null;
    try {
      await dragTo('a', ['long paragraph number 2 with some words in it', 'gap'], { hold: async () => {
        drawnAtStart = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id li')].some(li => li.textContent.trim() === 'z2'));
        await page.mouse.move(60, viewH - 3, { steps: 3 });
        try {
          await page.waitForFunction(() => [...document.querySelectorAll('#ag-editor-id li')].some(li => li.textContent.trim() === 'z2') && window.scrollY >= document.documentElement.scrollHeight - window.innerHeight - 2, { timeout: 90000 });
        } catch { progressive = 'the page did not scroll to the list at the end'; return; }
        await pause(300);
        const z = await geometry('z2');
        await page.mouse.move(60, z.top + 2, { steps: 4 });
        await pause(150);
        progressive = await placeLine();
      } });
    } finally {
      await page.evaluate(() => { delete window.__typedownProgressiveDelayMs; delete window.__typedownProgressiveSliceMs; });
    }
    const longMd = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(longMd.endsWith('- z1\n- a\n- z2\n- z3\n'), `a long document drawn a part at a time: dragged to the bottom edge, into the list at its end (${drawnAtStart ? 'drawn whole by the press' : 'drawn in during the drag'})`, JSON.stringify({ progressive, tail: longMd.slice(-50) }));

    // The marker is no handle: a drag from it moves nothing.
    await visual('- a\n- b\n- c\n');
    const b = await geometry('b');
    const a0 = await geometry('a');
    await page.mouse.move(b.markerX, b.y); await page.mouse.down(); await page.mouse.move(b.markerX, a0.top + 2, { steps: 8 }); await page.mouse.up();
    await pause(300);
    const after = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(after === '- a\n- b\n- c\n', 'a drag from the marker moves nothing', after);
    // The handle: shown beside the item under the pointer, and a drag by it moves the item (a reader saw no sign an item
    // could be dragged).
    await visual('- a\n- b\n- c\n');
    const c = await geometry('c');
    const cText = await page.evaluate(() => { const r = [...document.querySelectorAll('#ag-editor-id li p')][2].getBoundingClientRect(); return { x: r.left + 10, y: r.top + r.height / 2 }; });
    await page.mouse.move(cText.x, cText.y);
    await pause(150);
    const handle = await page.evaluate(() => { const h = document.getElementById('mu-list-drag-handle'); const r = h.getBoundingClientRect(); return { shown: getComputedStyle(h).display !== 'none', x: r.left + r.width / 2, y: r.top + r.height / 2, right: r.right }; });
    report(handle.shown && Math.abs(handle.y - c.y) < 8, 'hovering an item shows the drag handle beside it', JSON.stringify(handle));
    // Near the marker, not over it (a reader asked for it nearer): its right edge 8-30 px left of the text.
    const gap = (cText.x - 10) - handle.right;
    report(gap >= 8 && gap <= 30, `the handle is just left of the marker (${gap.toFixed(0)} px from the text)`, `${gap}`);
    const top = await geometry('a');
    await page.mouse.move(handle.x, handle.y);
    await page.mouse.down();
    await page.mouse.move(handle.x + 2, handle.y - 6, { steps: 2 });
    await page.mouse.move(handle.x, top.top + 2, { steps: 8 });
    await page.mouse.up();
    await pause(400);
    report(await page.evaluate(() => window.__typedownMuya.getMarkdown()) === '- c\n- a\n- b\n', 'a drag by the handle moves the item', await page.evaluate(() => window.__typedownMuya.getMarkdown()));
    // On the way from the text to the handle - over the marker, the list's margin, out of the editor - it stays (a
    // reader saw it go before the pointer got there); moved away, it goes after a moment.
    await visual('- alpha\n- beta\n');
    const beta = await geometry('beta');
    await page.mouse.move(beta.textX, beta.y);
    await pause(150);
    const at = await page.evaluate(() => { const r = document.getElementById('mu-list-drag-handle').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
    for (let i = 1; i <= 12; i++) {
      await page.mouse.move(beta.textX + (at.x - beta.textX) * i / 12, beta.y + (at.y - beta.y) * i / 12);
      await pause(40);
    }
    await pause(500);
    const stayed = await page.evaluate(() => getComputedStyle(document.getElementById('mu-list-drag-handle')).display !== 'none');
    report(stayed, 'the handle stays on the pointer\'s way to it', '');
    await page.mouse.move(at.x, at.y + 200, { steps: 4 });
    await pause(700);
    report(await page.evaluate(() => getComputedStyle(document.getElementById('mu-list-drag-handle')).display) === 'none', 'moved away, the handle goes after a moment', '');
    // A nested item: its handle sits in the parent's indent, which the pointer crosses on its way - the handle went to
    // the parent before it was reached (a reader's report). It stays on the item's line all the way, and drags it.
    await visual('- parent\n  - child one\n  - child two\n- next\n');
    const two = await geometry('child two');
    await page.mouse.move(two.textX, two.y);
    await pause(150);
    const nestedAt = await page.evaluate(() => { const r = document.getElementById('mu-list-drag-handle').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
    let jumped = null;
    for (let i = 1; i <= 24; i++) {
      await page.mouse.move(two.textX + (nestedAt.x - two.textX) * i / 24, two.y + (nestedAt.y - two.y) * i / 24);
      await pause(30);
      const now = await page.evaluate(() => { const h = document.getElementById('mu-list-drag-handle'); const r = h.getBoundingClientRect(); return { shown: getComputedStyle(h).display !== 'none', y: r.top + r.height / 2 }; });
      if (!now.shown || Math.abs(now.y - nestedAt.y) > 4) { jumped = { step: i, ...now }; break; }
    }
    report(!jumped, "a nested item's handle stays on its line on the pointer's way through the parent's indent", JSON.stringify({ jumped, nestedAt, two }));
    // Anywhere on an item's line - its grandparent's and parent's indent included - the handle is that item's (a
    // reader saw a parent's or grandparent's handle at some places on a line).
    await page.mouse.move(5, 600);
    await pause(700);
    await visual('- top\n  - middle\n    - deep item\n- after\n');
    const deep = await geometry('deep item');
    const topText = await geometry('top');
    const wrong = [];
    for (let x = topText.markerX - 6; x <= deep.textX + 20; x += 6) {
      await page.mouse.move(5, 600);
      await pause(450);
      await page.mouse.move(x, deep.y);
      await pause(120);
      const h = await page.evaluate(() => { const e = document.getElementById('mu-list-drag-handle'); const r = e.getBoundingClientRect(); return { shown: getComputedStyle(e).display !== 'none', y: r.top + r.height / 2 }; });
      if (h.shown && Math.abs(h.y - deep.y) > 4) wrong.push({ x: Math.round(x), y: Math.round(h.y) });
    }
    report(wrong.length === 0, "on a nested item's line, wherever the pointer is, the handle is that item's", JSON.stringify({ wrong, deepY: deep.y }));
    await visual('- parent\n  - child one\n  - child two\n- next\n');
    const one = await geometry('child one');
    await page.mouse.move(nestedAt.x, nestedAt.y);
    // The pointer: grab over the handle, grabbing from the press (before it moves) over the text too, back after.
    const cursorAt = (x, y) => page.evaluate(({ x, y }) => getComputedStyle(document.elementFromPoint(x, y)).cursor, { x, y });
    const overHandle = await cursorAt(nestedAt.x, nestedAt.y);
    await page.mouse.down();
    await pause(50);
    const pressed = [await cursorAt(nestedAt.x, nestedAt.y), await cursorAt(one.textX + 10, one.y)];
    report(overHandle === 'move' && pressed.every(c => c === 'move'), "the pointer: Windows' move cursor over the handle and as soon as it is pressed, over the text too (grab and grabbing are not system cursors: the app's web view showed the arrow)", JSON.stringify({ overHandle, pressed }));
    await page.mouse.move(nestedAt.x + 2, nestedAt.y - 6, { steps: 2 });
    await page.mouse.move(nestedAt.x, one.top + 2, { steps: 8 });
    await page.mouse.up();
    await pause(300);
    const nestedMd = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    const released = await page.evaluate(() => [document.body.classList.contains('ag-list-pressed') || document.body.classList.contains('ag-list-dragging'), getComputedStyle(document.querySelector('#ag-editor-id p, #ag-editor-id li')).cursor]);
    report(!released[0] && released[1] !== 'move', 'released: the pointer as before', JSON.stringify(released));
    report(/child two[\s\S]*child one/.test(nestedMd) && /parent[\s\S]*child two[\s\S]*next/.test(nestedMd), 'and drags the nested item, not its parent', nestedMd);
    await visual('- a\n- b\n- c\n');
    await page.mouse.move(cText.x, cText.y);
    await pause(150);
    await page.keyboard.type('x');
    await pause(100);
    report(await page.evaluate(() => getComputedStyle(document.getElementById('mu-list-drag-handle')).display) === 'none', 'typing hides the handle', '');

    // A long list: the pointer moving over it costs little (each move read every item before).
    await visual(Array.from({ length: 3000 }, (_, i) => `- item ${i}`).join('\n') + '\n');
    const cost = await page.evaluate(async () => {
      const lis = document.querySelectorAll('#ag-editor-id li p');
      const r = lis[Math.min(40, lis.length - 1)].getBoundingClientRect();
      const t0 = performance.now();
      for (let i = 0; i < 200; i++) {
        const target = document.elementFromPoint(r.left - 12, r.top + r.height / 2) || lis[40];
        target.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: r.left - 12 + (i % 5), clientY: r.top + r.height / 2 }));
        await new Promise(requestAnimationFrame);
      }
      const frames = performance.now() - t0;
      const t1 = performance.now();
      for (let i = 0; i < 200; i++) await new Promise(requestAnimationFrame);
      return { perMove: (frames - (performance.now() - t1)) / 200 };
    });
    report(cost.perMove < 2, `the pointer over a 3000-item list costs little a move (${cost.perMove.toFixed(2)} ms beyond a frame)`, JSON.stringify(cost));

    // The text is not a handle: a drag in an item's text selects text, moves nothing.
    await visual('- alpha\n- beta\n');
    const t = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id li p').getBoundingClientRect(); return { x: r.left + 4, y: r.top + r.height / 2 }; });
    await page.mouse.move(t.x, t.y); await page.mouse.down(); await page.mouse.move(t.x + 30, t.y + 40, { steps: 6 }); await page.mouse.up();
    await pause(300);
    report(await page.evaluate(() => window.__typedownMuya.getMarkdown()) === '- alpha\n- beta\n', 'a drag in the text moves nothing', '');

    // The handle in one column for the lists of one level, whatever their marker: a bullet's, narrower than "1." or a
    // task's box, put it 8 px to the right of theirs (a reader, 2026-11-17). A marker wider than the room ("100.") still
    // pushes it left, off the marker.
    await visual('1. one\n2. two\n\n- bullet\n- bullet\n\n- [ ] task\n- [ ] task\n\n98. a\n99. b\n100. wide\n');
    const handles = [];
    for (const text of ['one', 'bullet', 'task', 'wide']) {
      const g = await geometry(text);
      await page.mouse.move(g.textX, g.y); await pause(200);
      handles.push(await page.evaluate(text => {
        const h = document.getElementById('mu-list-drag-handle').getBoundingClientRect();
        const li = [...document.querySelectorAll('#ag-editor-id li')].find(el => el.textContent.trim() === text);
        return { text, left: Math.round(h.left), right: Math.round(h.right), textLeft: Math.round(li.querySelector('p, span, div').getBoundingClientRect().left) };
      }, text));
    }
    const [ordered, bullet, task, wide] = handles;
    report(bullet.left === ordered.left && task.left === ordered.left, 'the handle in one column for an ordered, a bullet and a task list', JSON.stringify(handles));
    report(wide.left < ordered.left, 'a marker wider than the room ("100.") pushes the handle left of the column', JSON.stringify(wide));
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
