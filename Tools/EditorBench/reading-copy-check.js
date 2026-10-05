// Copying in reading mode, and copy as plain text. Reading mode has no caret, so Muya reported no selection and the
// host kept Copy disabled whatever was selected; its select-all needed a focus the page never has there, so it
// selected nothing and a copy after it copied nothing. Copy as plain text was the selection's toString(): a heading's
// `#` in visual mode, a formula three times over, no image, no list numbers. Checked here:
//  - reading mode: a drag reports a selection (ReadingSelectionChange), a click that clears it reports none;
//  - reading mode: Ctrl+A and SelectAll select the document (not the page around it); Copy then gives Markdown, copy as plain text the text below;
//  - visual mode, the caret in the heading (its markers shown): copy as plain text gives the same text.
const assert = require('assert/strict');
const { openEditor } = require('./harness');

const doc = `# Heading **bold**

Para with **bold**, *em*, ~~del~~, \`code\`, [link](https://e.com "T"), ![a picture](a.png) and $x^2$.

1. one
2. two
   - nested

- [x] done

> quote

| a | b |
|---|---|
| 1 | **2** |

\`\`\`js
const x = 1;
\`\`\`

Last line.
`;

const expected = [
  'Heading bold',
  '',
  'Para with bold, em, del, code, link, a picture and x^2.',
  '',
  '1. one',
  '2. two',
  '    • nested',
  '',
  '☑ done',
  '',
  'quote',
  '',
  'a\tb',
  '1\t2',
  '',
  'const x = 1;',
  '',
  'Last line.',
].join('\n');

(async () => {
  const { page, mode, load, pause, close, errors } = await openEditor();
  let failed = false;
  const check = (label, fn) => {
    try { fn(); console.log('PASS ' + label); } catch (e) { failed = true; console.log('FAIL ' + label + '\n' + e.message); }
  };
  try {
    await page.evaluate(() => {
      window.__clip = [];
      const post = window.chrome.webview.postMessage;
      window.chrome.webview.postMessage = raw => { const m = JSON.parse(raw); if (m.name === 'SetClipboard') window.__clip.push(m.args); return post(raw); };
    });
    const copy = async type => {
      await page.evaluate(() => { window.__clip = []; });
      await page.evaluate(t => window.__deliver('Copy', { type: t, copyInfo: null }), type);
      await pause(300);
      const clip = await page.evaluate(() => window.__clip);
      return clip.filter(c => c.type === 'text/plain').map(c => c.data).pop() ?? null;
    };
    const reported = () => page.evaluate(() => window.__last.ReadingSelectionChange?.selected);

    await mode(false, false);
    await load(doc);
    await mode(false, true);

    // A drag over the second paragraph.
    const p = await page.evaluate(() => { const r = document.querySelectorAll('#ag-editor-id > *')[1].getBoundingClientRect(); return { x: r.left + 5, y: r.top + 8 }; });
    await page.mouse.move(p.x, p.y); await page.mouse.down(); await page.mouse.move(p.x + 150, p.y, { steps: 8 }); await page.mouse.up();
    await pause(300);
    const afterDrag = await reported();
    check('reading mode: a drag reports a selection to the host', () => assert.equal(afterDrag, true));
    await page.mouse.click(p.x + 40, p.y + 200);
    await pause(300);
    const afterClick = await reported();
    check('reading mode: a click that clears it reports none', () => assert.equal(afterClick, false));

    // Ctrl+A as the reader presses it: the document, not the whole page.
    await page.keyboard.down('Control'); await page.keyboard.press('KeyA'); await page.keyboard.up('Control');
    await pause(300);
    // (Chrome puts the ends of a selection of the document's contents on its parent, #editor, which holds only it.)
    const byKey = await page.evaluate(() => { const r = window.getSelection().getRangeAt(0); return document.getElementById('editor').contains(r.commonAncestorContainer) && window.getSelection().toString().includes('Last line.'); });
    check('reading mode: Ctrl+A selects the document, not the page around it', () => assert.equal(byKey, true));
    await page.mouse.click(p.x + 40, p.y + 200);
    await pause(300);

    await page.evaluate(() => window.__deliver('SelectAll', null));
    await pause(300);
    const all = await page.evaluate(() => window.getSelection().toString());
    check('reading mode: SelectAll selects the document', () => assert.ok(all.includes('Heading') && all.includes('Last line.'), JSON.stringify(all.slice(0, 80))));
    const afterAll = await reported();
    check('reading mode: SelectAll reports a selection', () => assert.equal(afterAll, true));
    const markdown = await copy('normal');
    check('reading mode: Copy after SelectAll gives the Markdown', () => assert.ok(markdown && markdown.includes('**bold**') && markdown.includes('Last line.'), JSON.stringify(markdown)));
    const plain = await copy('copyAsPlainText');
    check('reading mode: copy as plain text', () => assert.equal(plain, expected));

    // Visual mode with the caret in the heading, where its markers are shown.
    await mode(false, false);
    const h = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id h1').getBoundingClientRect(); return { x: r.left + 40, y: r.top + r.height / 2 }; });
    await page.mouse.click(h.x, h.y);
    await pause(300);
    await page.evaluate(() => window.__deliver('SelectAll', null));
    await pause(200);
    // Muya's select-all selects the block the caret is in first; a second one takes the document.
    await page.evaluate(() => window.__deliver('SelectAll', null));
    await pause(300);
    const visualPlain = await copy('copyAsPlainText');
    check('visual mode: copy as plain text, the heading\'s markers shown', () => assert.equal(visualPlain, expected));

    check('no page errors', () => assert.deepEqual(errors, []));
  } finally {
    await close();
  }
  if (failed) process.exit(1);
})().catch(e => { console.error(e); process.exit(1); });
