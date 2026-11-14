// A picture's zoom is of the screen's pixels (services/imageZoom): at 150% display scaling a picture at zoom:100% is drawn
// with one picture pixel to one screen pixel, two thirds of its size in CSS pixels. The user (2026-11-13): a screenshot
// set to 100% was right, but back from source mode it was large again until the caret went over it - the picture, loading
// anew, was put on the page with the file's zoom as it is (loadImageAsync), not as drawn on the screen.
// - back from source mode, the picture loaded anew: drawn at 40 CSS px (60 px wide, zoom:100%, pixel ratio 1.5);
// - the caret over it: the same;
// - source mode and back again (the picture known): the same;
// - at 200% zoom: 80.
//
//   node image-zoom-check.js
const zlib = require('zlib');
const { openEditor } = require('./harness');

const crc = buf => { let c, r = 0xffffffff; for (const b of buf) { c = (r ^ b) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; r = (r >>> 8) ^ c; } return (r ^ 0xffffffff) >>> 0; };
const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
// A 60 x 40 PNG, its colour (seed) making each address different, so each loads anew.
const png = seed => {
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(60, 0); ihdr.writeUInt32BE(40, 4); ihdr[8] = 8; ihdr[9] = 2;
  const rows = Buffer.concat(Array.from({ length: 40 }, () => Buffer.concat([Buffer.from([0]), Buffer.alloc(180, seed)])));
  return 'data:image/png;base64,' + Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]).toString('base64');
};

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const width = () => page.evaluate(() => { const i = document.querySelector('#ag-editor-id img'); return i && i.naturalWidth ? Math.round(i.getBoundingClientRect().width) : -1; });
  const visual = async () => { await mode(false, false); await pause(800); };
  try {
    await page.setViewport({ width: 1000, height: 800, deviceScaleFactor: 1.5 });
    for (const [zoom, expected, seed] of [['100%', 40, 90], ['200%', 80, 170]]) {
      const doc = `# T\n\n<img src="${png(seed)}" style="zoom:${zoom}">\n\nNext line\n`;
      await mode(true, false);
      await load(doc);
      await pause(300);
      await visual();
      let w = await width();
      report(w === expected, `zoom:${zoom}, back from source mode (loaded anew): ${expected} CSS px`, String(w));

      const at = await page.evaluate(() => { const r = document.querySelector('#ag-editor-id img').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
      await page.mouse.click(at.x, at.y);
      await pause(400);
      w = await width();
      report(w === expected, `zoom:${zoom}, clicked: ${expected} CSS px`, String(w));

      await mode(true, false);
      await pause(300);
      await visual();
      w = await width();
      report(w === expected, `zoom:${zoom}, source mode and back again: ${expected} CSS px`, String(w));
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
