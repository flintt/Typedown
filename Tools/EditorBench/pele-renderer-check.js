// Settings > Editor > Diagram renderer (public/lazy-mermaid.js): Mermaid by default, Pele when chosen. With Pele, the
// kinds this Mermaid cannot draw (agentflow, usecase) are drawn too, an event model as plain SVG (no HTML in it), and
// the exports and copy button make pictures of them (RenderXhtml diagramsAsPictures); choosing Mermaid again draws the
// open document's diagrams with Mermaid again. Neither library is loaded before a diagram is drawn.
const { openEditor } = require('./harness');

const doc = [
  '# Diagrams',
  '```mermaid\ngraph TD\n  A[Start] --> B[End]\n```',
  "```mermaid\neventmodeling\n\ntimeframe 01 ui OrderForm\ntimeframe 02 command PlaceOrder [[Order01]]\ntimeframe 03 event OrderPlaced [[Order01]]\n\ndata Order01 {\n  customer: 'ann'\n}\n```",
  '```mermaid\nagentflow-beta TB\n  flow inbox["Inbox"]\n    ask["Read the question"]@{ shape: input }\n  end\n\n  flow work["Work"]\n    plan["Make a plan"]@{ shape: task }\n    answer["Answer"]@{ shape: action }\n    plan --> answer\n  end\n\n  ask --> plan\n```',
  'end',
].join('\n\n') + '\n';

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  const scripts = () => page.evaluate(() => [...document.scripts].map(s => s.src.split('/').pop()).filter(n => /mermaid\.min|pele\.global/.test(n)));
  const drawn = () => page.evaluate(() => [...document.querySelectorAll('#ag-editor-id figure[data-role="MERMAID"] .ag-container-preview')].map(p => {
    const svg = p.querySelector('svg');
    return svg ? { fo: !!svg.querySelector('foreignObject'), pele: /--pele-/.test(svg.getAttribute('style') || '') } : null;
  }));
  try {
    await mode(false, false);
    check((await scripts()).length === 0, 'nothing loaded before a diagram is drawn');
    await page.evaluate(() => window.__deliver('SettingsChanged', { mermaidRenderer: 'pele' }));
    await load(doc);
    await page.waitForFunction(() => document.querySelectorAll('#ag-editor-id figure[data-role="MERMAID"] .ag-container-preview svg').length >= 3, { timeout: 30000 }).catch(() => {});
    await pause(500);
    let shown = await drawn();
    console.log('pele:', JSON.stringify(shown), (await scripts()).join(', '));
    check(shown.length === 3 && shown.every(s => s && s.pele), 'Pele draws all three, the agentflow too');
    check(shown.every(s => s && !s.fo), 'no HTML inside any of them (the event model included)');
    check(!(await scripts()).includes('mermaid.min.js'), 'Mermaid itself is not loaded while Pele draws');

    await page.evaluate(() => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token: 1, diagramsAsPictures: true }); });
    await page.waitForFunction(() => window.__last.RenderedXhtml, { timeout: 60000 });
    const { xhtml } = await page.evaluate(() => window.__last.RenderedXhtml);
    const pictures = (xhtml.match(/<img\b[^>]*src="data:image\/png/g) || []).length;
    check(pictures === 3 && !/<svg\b/.test(xhtml), `exported as pictures (${pictures} of 3, ${/<svg\b/.test(xhtml) ? 'SVG left' : 'no SVG left'})`);

    await page.evaluate(() => window.__deliver('SettingsChanged', { mermaidRenderer: 'mermaid' }));
    await page.waitForFunction(() => {
      const svgs = [...document.querySelectorAll('#ag-editor-id figure[data-role="MERMAID"] .ag-container-preview svg')];
      return svgs.length >= 1 && !/--pele-/.test(svgs[0].getAttribute('style') || '');
    }, { timeout: 30000 }).catch(() => {});
    await pause(800);
    shown = await drawn();
    console.log('mermaid:', JSON.stringify(shown), (await scripts()).join(', '));
    check(shown[0] && !shown[0].pele, 'Mermaid again draws the flowchart');
    check((await scripts()).includes('mermaid.min.js'), 'and Mermaid is loaded for it');
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
