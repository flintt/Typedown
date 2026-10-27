// A diagram's copy button (ContentState.copyDiagram): it shows when the pointer is over a drawn diagram of each kind the
// editor draws (Mermaid, flowchart, sequence, Vega-Lite; PlantUML too, as a block), in visual and reading mode, and not
// while the diagram's source is being edited (ag-active). A click sends the host one CopyBlock message with the
// block's Markdown fenced with its language and a PNG of the diagram: twice the size of the picture the exports make
// of it (RenderXhtml diagramsAsPictures), opaque, mostly white and with little dark in it - the light theme on white -
// though the editor shows the diagram in the dark theme. A diagram that could not be drawn (bad Mermaid, PlantUML with
// drawing off) sends its code only. The document's text, the caret and the selection stay as they were, and no change
// is reported (the host's history and unsaved state come from those reports).
const { openEditor } = require('./harness');

const blocks = {
  mermaid: '```mermaid\ngraph TD\n  A[Start] --> B{Is it?}\n  B -->|Yes| C[OK]\n  B -->|No| D[End]\n```',
  flowchart: '```flowchart\nst=>start: Start\nop=>operation: Do it\ne=>end: End\nst->op->e\n```',
  sequence: '```sequence\nAlice->Bob: Hello Bob\nBob-->Alice: Hi Alice\n```',
  'vega-lite': '```vega-lite\n' + JSON.stringify({ $schema: 'https://vega.github.io/schema/vega-lite/v5.json', data: { values: [{ a: 'A', b: 28 }, { a: 'B', b: 55 }, { a: 'C', b: 43 }] }, mark: 'bar', encoding: { x: { field: 'a', type: 'nominal' }, y: { field: 'b', type: 'quantitative' } } }) + '\n```',
};
const failed = {
  'bad mermaid': '```mermaid\ngraph TD\n  A -->\n```',
  'plantuml (drawing off)': '```plantuml\n@startuml\nAlice -> Bob: hi\n@enduml\n```',
};
const doc = '# Diagrams\n\nFirst paragraph.\n\n' + [...Object.values(blocks), ...Object.values(failed)].join('\n\n') + '\n\nLast paragraph.\n';

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  try {
    await page.setViewport({ width: 1200, height: 900 });
    await mode(false, false);
    // The dark theme: the editor draws its diagrams in it; the copy must not.
    await page.evaluate(() => window.__deliver('ThemeChanged', { theme: 'Dark', accentColor: { r: 0, g: 120, b: 212, a: 1 }, background: { R: 32, G: 32, B: 32, A: 255 } }));
    await load(doc);
    // The host's text for a PlantUML block while drawing it is off (GetStringResources, empty in this stub): without it
    // the block has no height, and nothing to hover.
    await page.evaluate(() => document.documentElement.style.setProperty('--PlantUmlOff', '"PlantUML drawing is off"'));
    // The host's side of CopyBlock: each message kept, answered "written". Any change report is counted.
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
    const drawnKinds = ['MERMAID', 'FLOWCHART', 'SEQUENCE', 'VEGA-LITE'];
    await page.waitForFunction(kinds => kinds.every(k => document.querySelector(`#ag-editor-id figure[data-role="${k}"] .ag-container-preview svg`)), { timeout: 60000 }, drawnKinds)
      .catch(() => {});
    await pause(800);
    const darkCheck = await page.evaluate(() => window.actualTheme);
    check(darkCheck === 'dark', `the editor is in the dark theme (${darkCheck})`);

    // What the exports make of each drawn diagram (RenderXhtml diagramsAsPictures: each picture's width, in CSS pixels):
    // the copy is twice that. Asked again in each mode - a sequence diagram's size depends on its font having loaded.
    const exportPictures = async () => {
      const xhtml = await page.evaluate(async () => {
        delete window.__last.RenderedXhtml;
        window.__deliver('RenderXhtml', { token: 7, diagramsAsPictures: true });
        for (let i = 0; i < 600 && !window.__last.RenderedXhtml; i++) await new Promise(r => setTimeout(r, 100));
        return window.__last.RenderedXhtml?.xhtml || '';
      });
      return [...xhtml.matchAll(/<img\b[^>]*>/g)].map(m => m[0])
        .filter(t => /src="data:image\/png/.test(t)).map(t => ({ width: +(t.match(/width="(\d+)"/) || [])[1], alt: (t.match(/alt="([^"]*)"/) || [])[1] || '' }));
    };
    let exportWidths = [];

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
      for (let i = 0; i < data.length; i += 4 * 7) {
        all++;
        if (data[i + 3] < 250) transparent++;
        const lum = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
        if (lum >= 235) white++;
        else if (lum < 80) dark++;
      }
      return { width: image.naturalWidth, height: image.naturalHeight, white: white / all, dark: dark / all, transparent: transparent / all };
    }, png);

    const figureSel = role => `#ag-editor-id figure[data-role="${role}"]`;
    const hoverOpacity = async selector => {
      await page.mouse.move(5, 5);
      await pause(300);
      const box = await page.evaluate(sel => {
        const f = document.querySelector(sel);
        f.scrollIntoView({ block: 'center' });
        const r = f.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + Math.min(r.height / 2, 40) };
      }, selector);
      await pause(200);
      await page.mouse.move(box.x, box.y, { steps: 4 });
      await pause(400);
      return page.evaluate(sel => {
        const b = document.querySelector(sel + ' > .ag-diagram-copy');
        if (!b) return { opacity: -1, display: 'none' };
        const s = getComputedStyle(b);
        const r = b.getBoundingClientRect();
        return { opacity: +s.opacity, display: s.display, x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width };
      }, selector);
    };
    const clickCopy = async (selector, label) => {
      const shown = await hoverOpacity(selector);
      check(shown.opacity >= 0.4 && shown.display !== 'none' && shown.w > 0, `${label}: the pointer over it shows the copy button (opacity ${shown.opacity})`);
      const before = await page.evaluate(() => window.__copies.length);
      await page.mouse.move(shown.x, shown.y, { steps: 3 });
      await pause(150);
      await page.mouse.click(shown.x, shown.y);
      await page.waitForFunction(n => window.__copies.length > n, { timeout: 60000 }, before).catch(() => {});
      await pause(200);
      const copied = await page.evaluate(sel => !!document.querySelector(sel + ' > .ag-diagram-copy.ag-code-copied'), selector);
      const message = await page.evaluate(n => window.__copies[n], before);
      return { message, copied };
    };

    const runKinds = async modeLabel => {
      exportWidths = await exportPictures();
      check(exportWidths.length === 4, `${modeLabel}: the export makes a picture of each drawn diagram (${exportWidths.map(e => e.alt.split(':')[0]).join(', ')})`);
      const textBefore = await flush();
      const stateBefore = await state();
      console.log(`  ${modeLabel}: before ${stateBefore}`);
      const changesBefore = await page.evaluate(() => window.__changes);
      let index = 0;
      for (const [kind, fenced] of Object.entries(blocks)) {
        const role = kind.toUpperCase();
        const label = `${modeLabel} ${kind}`;
        const { message, copied } = await clickCopy(figureSel(role), label);
        check(!!message, `${label}: a click sends CopyBlock`);
        if (!message) { index++; continue; }
        check(message.text === fenced, `${label}: its text is the block's Markdown fenced with its language${message.text === fenced ? '' : ` (${JSON.stringify(message.text).slice(0, 120)})`}`);
        check(typeof message.png === 'string' && message.png.length > 100, `${label}: with a PNG`);
        check(copied, `${label}: the button shows it copied`);
        if (typeof message.png === 'string') {
          const p = await inspect(message.png);
          if (process.env.DUMP) require('fs').writeFileSync(`${process.env.DUMP}/${modeLabel}-${kind}.png`, Buffer.from(message.png, 'base64'));
          const expected = exportWidths[index] ? exportWidths[index].width * 2 : NaN;
          console.log(`  ${label}: ${p.width}x${p.height}, export picture ${exportWidths[index]?.width} wide; white ${(p.white * 100).toFixed(0)}%, dark ${(p.dark * 100).toFixed(0)}%, transparent ${(p.transparent * 100).toFixed(0)}%`);
          // A few pixels either way: the sequence library measures its text again for each drawing (411 against 402 here).
          check(Math.abs(p.width - expected) <= Math.max(2, expected * 0.03), `${label}: twice as wide as the export's picture of it (${p.width} vs ${expected})`);
          check(p.transparent === 0, `${label}: opaque (on white, not on nothing)`);
          check(p.white >= 0.5 && p.dark <= 0.15, `${label}: light: mostly white, little dark, in the dark theme`);
        }
        index++;
      }
      for (const [kind, fenced] of Object.entries(failed)) {
        const role = kind.startsWith('plantuml') ? 'PLANTUML' : 'MERMAID';
        const selector = role === 'MERMAID' ? `#ag-editor-id figure[data-role="MERMAID"]:has(.ag-math-error)` : figureSel(role);
        const label = `${modeLabel} ${kind}`;
        const { message } = await clickCopy(selector, label);
        check(!!message && message.text === fenced && message.png == null, `${label}: its code only${message ? ` (png ${message.png == null ? 'none' : 'sent'}, text ${JSON.stringify(message.text).slice(0, 60)})` : ''}`);
      }
      await pause(500);
      check(await flush() === textBefore, `${modeLabel}: the document's text is as it was`);
      check(await page.evaluate(() => window.__changes) === changesBefore, `${modeLabel}: no change reported (nothing for the host's history or its unsaved state)`);
      const stateAfter = await state();
      check(stateAfter === stateBefore, `${modeLabel}: the caret, selection and active block are as they were${stateAfter === stateBefore ? '' : ` (${stateBefore} -> ${stateAfter})`}`);
    };

    // The caret somewhere first, put there with the mouse: in the first paragraph, after its third letter.
    const placeCaret = async () => {
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
    };
    await placeCaret();
    await runKinds('visual');

    // While its source is edited (ag-active) the diagram has no copy button.
    await page.evaluate(sel => window.__typedownMuya.contentState.handleContainerBlockClick(document.querySelector(sel)), figureSel('MERMAID'));
    await pause(500);
    const active = await page.evaluate(sel => document.querySelector(sel).classList.contains('ag-active'), figureSel('MERMAID'));
    check(active, 'the Mermaid source opened for editing (ag-active)');
    const editing = await page.evaluate(sel => {
      const b = document.querySelector(sel + ' > .ag-diagram-copy');
      return b ? getComputedStyle(b).display : 'gone';
    }, figureSel('MERMAID'));
    const hovered = await hoverOpacity(figureSel('MERMAID') + '.ag-active');
    check((editing === 'none' || editing === 'gone') && (hovered.display === 'none' || hovered.opacity === -1), `no diagram copy button while its source is edited (${editing}, hovered ${hovered.display})`);

    await mode(false, true);
    await page.waitForFunction(kinds => kinds.every(k => document.querySelector(`#ag-editor-id figure[data-role="${k}"] .ag-container-preview svg`)), { timeout: 60000 }, drawnKinds).catch(() => {});
    await pause(800);
    await runKinds('reading');

    check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join('; ') : ''}`);
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
