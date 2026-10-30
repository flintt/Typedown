// Diagrams whose labels Mermaid writes as HTML inside the drawing (<foreignObject>) - a user journey, an event model, a
// Venn diagram - become pictures too (RenderXhtml diagramsAsPictures, what Word export and the copy button use): a
// canvas such a drawing was drawn on cannot be read back, so they stayed SVG and Word showed "diagram not exported".
// Their labels are SVG text in the picture now. DIAGRAM_PICTURES=<folder> saves the pictures, to look at.
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

const diagrams = {
  journey: '```mermaid\njourney\n    title Writing a report\n    section Draft\n      Gather notes: 4: Me\n      Write the outline: 3: Me, Editor\n    section Review\n      Read it through: 2: Editor\n      Send it: 5: Me\n```',
  eventmodeling: "```mermaid\neventmodeling\n\ntimeframe 01 ui OrderForm\ntimeframe 02 command PlaceOrder [[Order01]]\ntimeframe 03 event OrderPlaced [[OrderPlaced]]\n\ndata Order01 {\n  customer: 'ann'\n  total: 12.5\n}\n\ndata OrderPlaced {\n  customer: string\n  total: number\n}\n```",
  venn: '```mermaid\nvenn-beta\n  set A["Reading"]\n    text A1["Novels"]\n  set B["Writing"]\n    text B1["Reports"]\n  union A,B["Both"]\n    text AB1["Notes"]\n```',
};

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  const saveTo = process.env.DIAGRAM_PICTURES;
  try {
    await mode(true, false);
    for (const [name, block] of Object.entries(diagrams)) {
      await load('# ' + name + '\n\n' + block + '\n');
      await pause(500);
      await page.evaluate(() => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token: 1, diagramsAsPictures: true }); });
      await page.waitForFunction(() => window.__last.RenderedXhtml, { timeout: 60000 });
      const { xhtml, error } = await page.evaluate(() => window.__last.RenderedXhtml);
      const src = ((xhtml || '').match(/<img\b[^>]*src="(data:image\/png;base64,[^"]+)"/) || [])[1];
      check(!error && !!src && !/<svg\b/.test(xhtml), `${name}: a picture, no SVG left${error ? ` (error ${error})` : ''}`);
      if (!src) continue;
      // Something drawn: the picture has dark pixels where the labels are, not only white.
      const dark = await page.evaluate(async src => {
        const image = new Image();
        await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; image.src = src; });
        const canvas = document.createElement('canvas');
        canvas.width = image.width; canvas.height = image.height;
        const context = canvas.getContext('2d');
        context.drawImage(image, 0, 0);
        const data = context.getImageData(0, 0, canvas.width, canvas.height).data;
        let count = 0;
        for (let i = 0; i < data.length; i += 4) if (data[i] + data[i + 1] + data[i + 2] < 200) count++;
        return count / (data.length / 4);
      }, src);
      check(dark > 0.002, `${name}: the picture has dark marks in it (${(dark * 100).toFixed(2)}%)`);
      if (saveTo) {
        fs.mkdirSync(saveTo, { recursive: true });
        fs.writeFileSync(path.join(saveTo, name + '.png'), Buffer.from(src.slice('data:image/png;base64,'.length), 'base64'));
      }
    }
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
