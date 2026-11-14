// A picture kept in the document as an HTML <img src="data:...;base64,..."> (another editor writes them so) at the start
// of a document is a raw HTML block, whose source shows while the caret is in it. The user (2026-11-13): switched
// from source mode with the caret on the line below it, the whole base64 showed; so did the caret moved up onto it.
// The encoded part is a short label there, as in source mode (services/dataUriFold):
// - the visual editor's text is short, with the label (kind and size) in it;
// - a character typed at the end of the source goes in, the address whole in the document;
// - the block's preview: the picture drawn.
//
//   node data-uri-html-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  // A small valid PNG, padded with a tEXt chunk to a screenshot's size of encoding (about 140 KB).
  const zlib = require('zlib');
  const crc = buf => { let c, crc = 0xffffffff; for (const b of buf) { c = (crc ^ b) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; } return (crc ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(40, 0); ihdr.writeUInt32BE(30, 4); ihdr[8] = 8; ihdr[9] = 2;
  const rows = Buffer.concat(Array.from({ length: 30 }, (_, y) => Buffer.concat([Buffer.from([0]), Buffer.alloc(120, y * 8)])));
  const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('tEXt', Buffer.from('pad\0' + 'x'.repeat(140000))), chunk('IDAT', zlib.deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
  const address = 'data:image/png;base64,' + png.toString('base64');
  const doc = `<img title="" src="${address}" alt="" style="zoom:100%;">\n`;
  const seen = () => page.evaluate(() => {
    const e = document.getElementById('ag-editor-id');
    const fold = e.querySelector('.ag-data-uri-fold');
    return { text: e.innerText.length, sample: e.innerText.slice(0, 80), label: fold ? getComputedStyle(fold, '::after').content : '' };
  });
  try {
    await page.setViewport({ width: 1200, height: 900 });
    // Source mode, the caret on the line below the picture; then visual.
    await mode(true, false);
    await load(doc);
    await pause(400);
    await page.evaluate(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; cm.focus(); cm.setCursor({ line: 1, ch: 0 }); });
    await mode(false, false);
    await pause(800);
    let s = await seen();
    report(s.text < 400, 'switched to visual: the base64 is not shown', `${s.text} characters: ${s.sample}`);
    report(/PNG · \d+ KB/.test(s.label), 'in its place a label with its kind and size', s.label);

    // Up onto it, and again.
    for (const n of [1, 2]) {
      await page.keyboard.press('ArrowUp');
      await pause(300);
      s = await seen();
      report(s.text < 400, `up ${n}: the base64 is not shown`, `${s.text} characters`);
    }

    // A character typed at the end of the source: in the document, the address whole.
    await page.evaluate(() => {
      const line = document.querySelector('#ag-editor-id figure .ag-code-content');
      const r = document.createRange(); r.selectNodeContents(line); r.collapse(false);
      const sel = getSelection(); sel.removeAllRanges(); sel.addRange(r);
    });
    // (The line's text ends with its line break: back over it.)
    await page.keyboard.press('ArrowLeft');
    await page.keyboard.type('Z');
    await pause(500);
    const md = await page.evaluate(() => window.__typedownMuya.getMarkdown());
    report(md.includes(address) && /zoom:100%;">Z/.test(md), 'typed at the end of the source: the character in, the address whole', md.length > 300 ? md.slice(0, 60) + ' … ' + md.slice(-60) : md);
    s = await seen();
    report(s.text < 400, 'and the base64 still not shown', `${s.text} characters`);

    // The block's preview: the picture drawn.
    const drawn = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id figure img')].map(i => `${i.closest('[class]').className}:${i.naturalWidth}`).join(' '));
    report(/:40\b/.test(drawn), 'the block\'s preview: the picture drawn', drawn);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
