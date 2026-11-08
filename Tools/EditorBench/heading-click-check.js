// A click into a heading changes nothing of the text, however the headings are laid out: centred, letter-spaced, in
// another font (a typography preset's, "my manuscript" - the store pictures, 2026-11-07: "## 午后" became "##  午后",
// the document unsaved, after a click into the heading with that preset on). Clicked at its text's left, middle and
// right, and to the left and right of its text; each level, an ATX heading and a setext one.
const { openEditor } = require('./harness');

const presets = [
  ['default', ''],
  ['centred, letter-spaced, KaiTi', `#ag-editor-id h1, #ag-editor-id h2, #ag-editor-id h3, #ag-editor-id h4, #ag-editor-id h5, #ag-editor-id h6 {
     text-align: center; letter-spacing: 0.03em; font-family: KaiTi, STKaiti, "AR PL UKai CN", serif; }
   #ag-editor-id p, #ag-editor-id li { letter-spacing: 0.03em; font-family: KaiTi, STKaiti, serif; text-indent: 2em; }`],
  ['right-aligned', '#ag-editor-id h1, #ag-editor-id h2, #ag-editor-id h3 { text-align: right; }'],
];
const text = '# 秋日\n\n## 早晨\n\n清晨的风已经有了凉意。\n\n### 午后小记\n\n午后的阳光斜斜地照进来。\n\n## 午后\n\n- 多走路，少坐车\n- 每天写一页\n\n#### 四级\n\n##### 五级\n\n###### 六级\n\nSetext 标题\n---\n\n结尾。\n';

(async () => {
  const { page, errors, mode, flush, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${!c && detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
  try {
    await mode(false, false);
    let loadId = 300;
    for (const [name, css] of presets) {
      await page.evaluate(css => {
        let el = document.getElementById('heading-click-css');
        if (!el) { el = document.createElement('style'); el.id = 'heading-click-css'; document.head.append(el); }
        el.textContent = css;
      }, css);
      const id = ++loadId;
      await page.evaluate(({ text, id }) => { delete window.__last.FileLoaded; window.__deliver('LoadFile', { text, loadId: id, basePath: '/tmp' }); }, { text, id });
      await page.waitForFunction(id => window.__last.FileLoaded?.loadId === id, { timeout: 60000 }, id);
      await pause(500);
      const headings = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id h1, #ag-editor-id h2, #ag-editor-id h3, #ag-editor-id h4, #ag-editor-id h5, #ag-editor-id h6')].map((h, i) => i));
      const changed = [];
      for (const i of headings) {
        for (const where of ['left of text', 'text start', 'text middle', 'text end', 'right of text']) {
          // A paragraph first (as the reader did), then the heading.
          const para = await page.evaluate(() => { const e = [...document.querySelectorAll('#ag-editor-id .ag-paragraph-content')].find(x => x.textContent.includes('清晨')); const r = e.getBoundingClientRect(); return { x: r.left + 20, y: r.top + r.height / 2 } });
          await page.mouse.click(para.x, para.y);
          await pause(120);
          const at = await page.evaluate(({ i, where }) => {
            const h = document.querySelectorAll('#ag-editor-id h1, #ag-editor-id h2, #ag-editor-id h3, #ag-editor-id h4, #ag-editor-id h5, #ag-editor-id h6')[i];
            h.scrollIntoView({ block: 'center' });
            // The heading's own text (not its hidden marks).
            const w = document.createTreeWalker(h, NodeFilter.SHOW_TEXT);
            const rects = [];
            for (let n = w.nextNode(); n; n = w.nextNode()) {
              if (n.parentElement.closest('.ag-hide, .ag-front-icon')) continue;
              const r = document.createRange(); r.selectNodeContents(n);
              for (const b of r.getClientRects()) if (b.width) rects.push(b);
            }
            const box = h.getBoundingClientRect();
            const left = Math.min(...rects.map(r => r.left)), right = Math.max(...rects.map(r => r.right)), y = rects[0] ? (rects[0].top + rects[0].bottom) / 2 : box.top + box.height / 2;
            const x = where === 'left of text' ? Math.max(box.left + 2, left - 12) : where === 'text start' ? left + 1 : where === 'text middle' ? (left + right) / 2 : where === 'text end' ? right - 1 : Math.min(box.right - 2, right + 12);
            return { x, y, tag: h.tagName };
          }, { i, where });
          await pause(80);
          await page.evaluate(() => { delete window.__last.MarkdownChange; });
          await page.mouse.click(at.x, at.y);
          await pause(250);
          const change = await page.evaluate(() => window.__last.MarkdownChange);
          const now = await flush();
          if (change || now !== text) {
            changed.push({ heading: `${at.tag} #${i}`, where, change: change ? true : false, diff: now === text ? null : now.split('\n').filter((l, k) => l !== text.split('\n')[k]).slice(0, 2) });
            // Back to the text for the next click.
            const rid = ++loadId;
            await page.evaluate(({ text, rid }) => { delete window.__last.FileLoaded; window.__deliver('LoadFile', { text, loadId: rid, basePath: '/tmp' }); }, { text, rid });
            await page.waitForFunction(rid => window.__last.FileLoaded?.loadId === rid, { timeout: 60000 }, rid);
            await pause(300);
          }
        }
      }
      check(!changed.length, `${name}: a click into each heading, at its text and beside it, changes nothing of the text`, changed.slice(0, 6));
    }
    check(errors.length === 0, 'no page errors', errors);
  } catch (e) {
    console.log('FAIL ' + (e && e.stack || e));
    ok = false;
  } finally {
    await close();
  }
  process.exit(ok ? 0 : 1);
})();
