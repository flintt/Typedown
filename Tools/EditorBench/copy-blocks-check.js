// The copy buttons of a display formula and of a table (ContentState.copyBlock), and the menu's Copy table. In the dark
// theme, in visual and reading mode:
// - each display formula and each table has a copy button, shown when the pointer is over the block; an inline formula
//   has none; a table's sits just above its top right corner, over no cell;
// - a click sends the host one CopyBlock message: for a formula its Markdown as the document has it ($$ ... $$), its
//   MathML (Word makes an editable equation of it) and a PNG of it - opaque, mostly white with dark ink (black on white though the editor is dark), twice the size of the
//   formula drawn at 16px; a formula KaTeX cannot read sends its text only; for a table its Markdown as the document has
//   it (written compact, it stays compact) and HTML: a <table> with the header row as <th>, the rows and columns of the
//   table, each column's alignment, bold, italic, code and links in the cells, a formula in a cell as its TeX;
// - the button shows the check mark;
// - while a formula's source or a table's cell is being edited (ag-active) the block has no copy button;
// - the menu's Copy table (the host's CopyTable message after a right-click in a table) copies the table right-clicked,
//   and the right-click told the host whether it was in a table as the button went down (ContextMenuTarget, before the
//   host's menu opens on the button's release: its Copy table shows by it);
// - the document's text, the caret and the selection stay as they were, and no change is reported.
const { openEditor } = require('./harness');

const math = '$$\n\\int_0^1 x^2 \\, dx = \\frac{1}{3}\n$$';
const badMath = '$$\n\\frac{1}{\n$$';
const table = '|Name|**Score**|Note|\n|:-|:-:|-:|\n|*Ann*|`10`|[site](https://example.com)|\n|Bob|$x^2$|~~old~~|';
const doc = '# Blocks\n\nFirst paragraph with $a+b$ inline.\n\n' + math + '\n\n' + table + '\n\n' + badMath + '\n\nLast paragraph.\n';

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  try {
    await page.setViewport({ width: 1200, height: 900 });
    await mode(false, false);
    await page.evaluate(() => window.__deliver('ThemeChanged', { theme: 'Dark', accentColor: { r: 0, g: 120, b: 212, a: 1 }, background: { R: 32, G: 32, B: 32, A: 255 } }));
    await load(doc);
    await page.evaluate(() => {
      window.__copies = [];
      window.__changes = 0;
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => {
        const m = JSON.parse(raw);
        if (m.type === 'invoke' && m.name === 'CopyBlock') { window.__copies.push(m.args); setTimeout(() => window.__deliver(m.id, { code: 0, data: true }), 0); return; }
        if (m.name === 'MarkdownChange') window.__changes++;
        post(raw);
      };
    });
    await pause(800);
    check(await page.evaluate(() => window.actualTheme) === 'dark', 'the editor is in the dark theme');

    const mathSel = '#ag-editor-id figure[data-role="MULTIPLEMATH"]:not(:has(.ag-math-error))';
    const badSel = '#ag-editor-id figure[data-role="MULTIPLEMATH"]:has(.ag-math-error)';
    const tableSel = '#ag-editor-id figure[data-role="TABLE"]';

    const state = () => page.evaluate(() => {
      const s = getSelection();
      const cs = window.__typedownMuya.contentState;
      return JSON.stringify({ cursor: cs.cursor, focused: document.activeElement?.id, anchor: s.anchorNode && s.anchorNode.textContent?.slice(0, 20), offset: s.anchorOffset, focus: s.focusOffset, active: [...document.querySelectorAll('#ag-editor-id .ag-active')].map(e => e.id) });
    });

    const inspect = png => page.evaluate(async png => {
      const image = new Image();
      await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; image.src = 'data:image/png;base64,' + png; });
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d');
      context.drawImage(image, 0, 0);
      const { data } = context.getImageData(0, 0, canvas.width, canvas.height);
      let white = 0, dark = 0, transparent = 0, all = 0;
      for (let i = 0; i < data.length; i += 4) {
        all++;
        if (data[i + 3] < 250) transparent++;
        const lum = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
        if (lum >= 235) white++;
        else if (lum < 80) dark++;
      }
      return { width: image.naturalWidth, height: image.naturalHeight, white: white / all, dark: dark / all, transparent: transparent / all };
    }, png);

    // The formula as KaTeX lays it out at 16px with the picture's 8px and 6px of white around it: the picture is twice that.
    const expectedSize = await page.evaluate(() => {
      const box = document.createElement('div');
      box.style.cssText = 'position:fixed;left:-50000px;top:0;display:inline-block;padding:6px 8px;font-size:16px;line-height:1.5';
      const preview = document.querySelector('#ag-editor-id figure[data-role="MULTIPLEMATH"] .ag-container-preview .katex-display');
      box.appendChild(preview.cloneNode(true));
      box.firstChild.style.margin = '0';
      document.body.appendChild(box);
      const r = box.getBoundingClientRect();
      box.remove();
      return { width: Math.ceil(r.width) * 2, height: Math.ceil(r.height) * 2 };
    });

    const hover = async selector => {
      await page.mouse.move(5, 5);
      await pause(300);
      const box = await page.evaluate(sel => {
        const f = document.querySelector(sel);
        f.scrollIntoView({ block: 'center' });
        const target = f.querySelector('table td') || f.querySelector('.ag-container-preview') || f;
        const r = target.getBoundingClientRect();
        return { x: r.left + Math.min(r.width / 2, 30), y: r.top + r.height / 2 };
      }, selector);
      await pause(200);
      await page.mouse.move(box.x, box.y, { steps: 4 });
      await pause(400);
      return page.evaluate(sel => {
        const f = document.querySelector(sel);
        const b = f.querySelector(':scope > .ag-diagram-copy');
        if (!b) return { opacity: -1, display: 'none' };
        const s = getComputedStyle(b);
        const r = b.getBoundingClientRect();
        const t = f.querySelector(':scope > table')?.getBoundingClientRect();
        return { opacity: +s.opacity, display: s.display, x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, right: r.right, bottom: r.bottom, tableRight: t?.right, tableTop: t?.top };
      }, selector);
    };
    const clickCopy = async (selector, label) => {
      const shown = await hover(selector);
      check(shown.opacity >= 0.4 && shown.display !== 'none' && shown.w > 0, `${label}: the pointer over it shows the copy button (opacity ${shown.opacity})`);
      const before = await page.evaluate(() => window.__copies.length);
      await page.mouse.move(shown.x, shown.y, { steps: 3 });
      await pause(150);
      await page.mouse.click(shown.x, shown.y);
      await page.waitForFunction(n => window.__copies.length > n, { timeout: 30000 }, before).catch(() => {});
      await pause(200);
      const copied = await page.evaluate(sel => !!document.querySelector(sel + ' > .ag-diagram-copy.ag-code-copied'), selector);
      const message = await page.evaluate(n => window.__copies[n], before);
      return { message, copied, shown };
    };

    const checkTableMessage = async (message, label) => {
      check(!!message && message.text === table, `${label}: its text is the table as the document has it, compact${message && message.text !== table ? ` (${JSON.stringify(message.text)})` : ''}`);
      check(!!message && message.png == null && message.mathml == null, `${label}: no picture, no MathML`);
      const html = message && message.html;
      check(typeof html === 'string' && html.startsWith('<table'), `${label}: with HTML, a table`);
      if (typeof html !== 'string') return;
      const t = await page.evaluate(html => {
        const box = document.createElement('div');
        box.innerHTML = html;
        const table = box.querySelector('table');
        const rows = [...table.querySelectorAll('tr')];
        return {
          rows: rows.length,
          columns: rows.map(r => r.children.length),
          head: [...table.querySelectorAll('thead th')].map(c => c.textContent),
          align: [...rows[1].children].map(c => c.getAttribute('align')),
          bold: !!table.querySelector('thead th strong'),
          italic: table.querySelector('tbody em')?.textContent,
          code: table.querySelector('tbody code')?.textContent,
          link: table.querySelector('tbody a')?.getAttribute('href'),
          struck: !!table.querySelector('tbody del, tbody s'),
          mathCell: rows[2].children[1].textContent,
          border: table.getAttribute('border'),
          katex: !!table.querySelector('.katex'),
        };
      }, html);
      console.log(`  ${label}: ${JSON.stringify(t)}`);
      check(t.rows === 3 && t.columns.every(n => n === 3), `${label}: 3 rows (the header and two) of 3 columns`);
      check(JSON.stringify(t.head) === '["Name","Score","Note"]' && t.bold, `${label}: the header row in <th>, its bold kept`);
      check(JSON.stringify(t.align) === '["left","center","right"]', `${label}: each column's alignment as an align attribute`);
      check(t.italic === 'Ann' && t.code === '10' && t.link === 'https://example.com' && t.struck, `${label}: italic, code, a link and strike-through in the cells`);
      check(t.mathCell === '$x^2$' && !t.katex, `${label}: a formula in a cell is its TeX, not KaTeX's drawing`);
      check(t.border === '1', `${label}: with borders`);
    };

    const runMode = async modeLabel => {
      const textBefore = await flush();
      const stateBefore = await state();
      const changesBefore = await page.evaluate(() => window.__changes);
      const buttons = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id .ag-diagram-copy')].map(b => b.parentElement.dataset.role));
      check(JSON.stringify(buttons) === '["MULTIPLEMATH","TABLE","MULTIPLEMATH"]', `${modeLabel}: a copy button on each display formula and table (${buttons.join(', ')})`);
      check(await page.evaluate(() => !document.querySelector('#ag-editor-id p .ag-diagram-copy, #ag-editor-id p .ag-code-copy')), `${modeLabel}: none on the inline formula`);

      let { message, copied } = await clickCopy(mathSel, `${modeLabel} formula`);
      check(!!message && message.text === math, `${modeLabel} formula: its text is the block as written ($$ ... $$)${message && message.text !== math ? ` (${JSON.stringify(message.text)})` : ''}`);
      check(!!message && typeof message.png === 'string' && message.png.length > 100 && message.html == null, `${modeLabel} formula: with a PNG, no HTML`);
      check(copied, `${modeLabel} formula: the button shows it copied`);
      const mathml = message && message.mathml;
      check(typeof mathml === 'string' && /^<math[^>]*display="block"/.test(mathml) && /<\/math>$/.test(mathml) && mathml.includes('<mfrac>') && mathml.includes('\\int_0^1'),
        `${modeLabel} formula: with its MathML (display, a fraction, its TeX as an annotation), for Word's equations${typeof mathml === 'string' ? '' : ` (${mathml})`}`);
      if (message && typeof message.png === 'string') {
        const p = await inspect(message.png);
        if (process.env.DUMP) require('fs').writeFileSync(`${process.env.DUMP}/${modeLabel}-formula.png`, Buffer.from(message.png, 'base64'));
        console.log(`  ${modeLabel} formula: ${p.width}x${p.height} (expected ${expectedSize.width}x${expectedSize.height}); white ${(p.white * 100).toFixed(0)}%, dark ${(p.dark * 100).toFixed(1)}%, transparent ${(p.transparent * 100).toFixed(0)}%`);
        check(Math.abs(p.width - expectedSize.width) <= 4 && Math.abs(p.height - expectedSize.height) <= 4, `${modeLabel} formula: twice the size of the formula at 16px (${p.width}x${p.height} vs ${expectedSize.width}x${expectedSize.height})`);
        check(p.transparent === 0, `${modeLabel} formula: opaque (on white)`);
        check(p.white >= 0.6 && p.dark >= 0.03, `${modeLabel} formula: black on white, in the dark theme (white ${(p.white * 100).toFixed(0)}%, dark ${(p.dark * 100).toFixed(1)}%)`);
      }

      ({ message } = await clickCopy(badSel, `${modeLabel} formula KaTeX cannot read`));
      check(!!message && message.text === badMath && message.png == null && message.html == null && message.mathml == null, `${modeLabel} formula KaTeX cannot read: its text only`);

      let shown;
      ({ message, copied, shown } = await clickCopy(tableSel, `${modeLabel} table`));
      check(Math.abs(shown.right - shown.tableRight) <= 2 && shown.bottom <= shown.tableTop, `${modeLabel} table: its button is just above the table's top right corner (button right ${shown.right?.toFixed(0)}, bottom ${shown.bottom?.toFixed(0)}; table right ${shown.tableRight?.toFixed(0)}, top ${shown.tableTop?.toFixed(0)})`);
      check(copied, `${modeLabel} table: the button shows it copied`);
      await checkTableMessage(message, `${modeLabel} table`);

      await pause(300);
      check(await flush() === textBefore, `${modeLabel}: the document's text is as it was`);
      check(await page.evaluate(() => window.__changes) === changesBefore, `${modeLabel}: no change reported`);
      const stateAfter = await state();
      check(stateAfter === stateBefore, `${modeLabel}: the caret, selection and active block are as they were${stateAfter === stateBefore ? '' : ` (${stateBefore} -> ${stateAfter})`}`);
    };

    // The menu's Copy table: a right-click on a cell, then the host's CopyTable.
    const menuCopy = async modeLabel => {
      const at = await page.evaluate(sel => {
        const cell = document.querySelector(sel + ' tbody td');
        cell.scrollIntoView({ block: 'center' });
        const r = cell.getBoundingClientRect();
        return { x: r.left + 8, y: r.top + r.height / 2 };
      }, tableSel);
      // The host opens its menu as the button comes up: the page must have said where the click is by then.
      const rightDown = async (x, y) => {
        await page.evaluate(() => { delete window.__last.ContextMenuTarget; });
        await page.mouse.move(x, y);
        await page.mouse.down({ button: 'right' });
        await pause(50);
        const report = await page.evaluate(() => window.__last.ContextMenuTarget);
        await page.mouse.up({ button: 'right' });
        await pause(300);
        return report;
      };
      const elsewhere = await page.evaluate(() => {
        const r = document.querySelector('#ag-editor-id p').getBoundingClientRect();
        return { x: r.left + 8, y: r.top + r.height / 2 };
      });
      const onParagraph = await rightDown(elsewhere.x, elsewhere.y);
      check(onParagraph && onParagraph.table === false, `${modeLabel} menu: a right-click on a paragraph tells the host, as the button goes down, it is not in a table (${JSON.stringify(onParagraph)})`);
      const inCell = await rightDown(at.x, at.y);
      check(inCell && inCell.table === true, `${modeLabel} menu: a right-click in a cell tells the host, as the button goes down, it is in a table (${JSON.stringify(inCell)})`);
      const before = await page.evaluate(() => window.__copies.length);
      await page.evaluate(() => window.__deliver('CopyTable', null));
      await page.waitForFunction(n => window.__copies.length > n, { timeout: 30000 }, before).catch(() => {});
      await checkTableMessage(await page.evaluate(n => window.__copies[n], before), `${modeLabel} menu Copy table`);
    };

    // The caret in the first paragraph, put there with the mouse.
    const at = await page.evaluate(() => {
      const text = document.querySelector('#ag-editor-id p .ag-paragraph-content').firstChild;
      const node = text.nodeType === 3 ? text : text.firstChild;
      const range = document.createRange();
      range.setStart(node, 3); range.setEnd(node, 3);
      const r = range.getBoundingClientRect();
      return { x: r.left, y: r.top + r.height / 2 };
    });
    await page.mouse.click(at.x, at.y);
    await pause(400);
    await runMode('visual');

    const textBeforeMenu = await flush();
    const changesBeforeMenu = await page.evaluate(() => window.__changes);
    await menuCopy('visual');
    await pause(300);
    check(await flush() === textBeforeMenu && await page.evaluate(() => window.__changes) === changesBeforeMenu, 'visual menu Copy table: the document as it was, no change reported');

    // While a formula's source is edited, or a cell, the block has no copy button.
    await page.evaluate(sel => window.__typedownMuya.contentState.handleContainerBlockClick(document.querySelector(sel)), mathSel);
    await pause(500);
    const mathEditing = await page.evaluate(sel => {
      const f = document.querySelector('#ag-editor-id figure[data-role="MULTIPLEMATH"]');
      const b = f.querySelector(':scope > .ag-diagram-copy');
      return { active: f.classList.contains('ag-active'), display: b ? getComputedStyle(b).display : 'gone' };
    }, mathSel);
    check(mathEditing.active && mathEditing.display === 'none', `no copy button on a formula while its source is edited (${JSON.stringify(mathEditing)})`);
    const cell = await page.evaluate(sel => {
      const r = document.querySelector(sel + ' tbody td').getBoundingClientRect();
      return { x: r.left + 8, y: r.top + r.height / 2 };
    }, tableSel);
    await page.mouse.click(cell.x, cell.y);
    await pause(500);
    const tableEditing = await hover(tableSel);
    const tableActive = await page.evaluate(sel => document.querySelector(sel).classList.contains('ag-active'), tableSel);
    check(tableActive && (tableEditing.display === 'none' || tableEditing.opacity === -1), `no copy button on a table while a cell is edited (active ${tableActive}, ${tableEditing.display})`);

    // Back to a caret in the first paragraph, then reading mode.
    await page.mouse.click(at.x, at.y);
    await pause(300);
    await mode(false, true);
    await pause(800);
    await runMode('reading');
    await menuCopy('reading');

    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join('; ') : ''}`);
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
