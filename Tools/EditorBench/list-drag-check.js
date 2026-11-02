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
    report(overHandle === 'grab' && pressed.every(c => c === 'grabbing'), 'the pointer: grab over the handle, grabbing as soon as it is pressed, over the text too', JSON.stringify({ overHandle, pressed }));
    await page.mouse.move(nestedAt.x + 2, nestedAt.y - 6, { steps: 2 });
    await page.mouse.move(nestedAt.x, one.top + 2, { steps: 8 });
    await page.mouse.up();
    await pause(300);
    const nestedMd = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    const released = await page.evaluate(() => [document.body.classList.contains('ag-list-pressed') || document.body.classList.contains('ag-list-dragging'), getComputedStyle(document.querySelector('#ag-editor-id p, #ag-editor-id li')).cursor]);
    report(!released[0] && released[1] !== 'grabbing', 'released: the pointer as before', JSON.stringify(released));
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
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
