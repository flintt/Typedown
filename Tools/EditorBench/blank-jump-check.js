// The reader scrolls up into the part not drawn yet while a long document is still drawn (back at its end, as a tab
// switched back to): the rest is drawn at once (completeRender), and the window must then show what the reader scrolled
// to - not jump 100,000 px on (hp, 1.4.0: a 1.5 MB document back at its end, the wheel turned up 18,000 px, the page went
// from 371k to 272k 5 ms after the drawing ended, and was put back there on every later switch).
// node blank-jump-check.js [at=0.996] [up=18000]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const args = process.argv.slice(2);
const at = Number(args.find(a => /^0?\.\d+$/.test(a)) || 0.996);
const up = Number((args.find(a => /^up=\d+$/.test(a)) || 'up=18000').slice(3));
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
// Paragraphs of different lengths with a heading every so often and a list now and then: the heights guessed for the
// part not drawn yet are off from what it lays out at, as in a real long document.
const zh = '清晨六点窗外的天色还是灰蓝的楼下早点铺的蒸笼已经冒起白汽豆浆的香味顺着风飘上来混着一点桂花的甜我泡了一杯茶坐在窗边看街上的人一个个醒过来';
const N = 2940;
const html = args.includes('html');
const block = i => {
  if (i % 60 === 0 && !args.includes('noheadings')) return `## 第 ${i / 60} 节\n\n`;
  // Paragraphs pasted from Word (an HTML block each), as a long document a reader keeps often is.
  if (html) {
    const t = zh.repeat(2).slice(i % 20, i % 20 + (process.env.SHORT ? 20 : 70) + (i % 5) * 15);
    const style = i % 3 ? 'margin:0pt 0pt 6pt;text-align:justify;line-height:150%;font-size:12pt' : 'margin:0pt 0pt 0.0001pt 20pt;text-indent:-20pt;line-height:150%';
    return `<p STYLE="${style}"><span style="font-family:'宋体';font-size:12.0pt">${t}</span><span lang=EN-US style="font-family:'Times New Roman';font-size:12pt">Item ${i}</span>${process.env.SHORT ? '' : `<span style="font-family:'宋体'">${t.slice(0, 40)}</span>`}</p>\n\n`;
  }
  if (i % 17 === 0) return `- 一项 ${i}\n- 两项 ${zh.slice(0, 30)}\n- 三项\n\n`;
  const t = zh.repeat(6).slice(i % 20, i % 20 + 40 + (i % 9) * 45);
  return `Item ${i} ${t}\n\n`;
};
const doc = Array.from({ length: N }, (_, i) => block(i)).join('');
console.log(`${N} blocks, ${doc.length} characters`);
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1402, height: Number(process.env.HEIGHT || 1287) });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '1200px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  // Slices drawn slowly, so the reader gets there while most of the document is still to come: a pause between slices
  // and a few blocks a slice (the shortest slice the drawing takes). With the slice's own length the document was drawn
  // whole in under 2.5 s once drawing got faster (2026-10-25), before the reader got there, half the runs.
  await page.evaluateOnNewDocument(`window.__typedownProgressiveDelayMs=${Number(process.env.DELAY || 40)};window.__typedownProgressiveSliceMs=${Number(process.env.SLICE || 1)}`);
  // "nopredict": the layout not predicted (the app's default), the heights guessed from the blocks' text.
  if (args.includes('nopredict')) await page.evaluateOnNewDocument('window.__typedownPretextOff=true');
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  const anchorIdx = Math.round(N * at);
// "caret=<block>": the caret left in that block, far above where the document was read (each block is two lines).
const caretBlock = Number((args.find(a => /^caret=\d+$/.test(a)) || 'caret=-1').slice(6));
const caretLine = caretBlock >= 0 ? caretBlock * 2 : -1;
  await page.evaluate((text, anchorIdx, caretLine) => {
    window.__msgs = [];
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: caretLine >= 0 ? { anchor: { line: caretLine, ch: 0 }, focus: { line: caretLine, ch: 0 } } : null, scrollTop: 99999, scrollAnchor: { block: anchorIdx, top: 120 }, loadId: 7, hold: 0 });
  }, doc, anchorIdx, caretLine);
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 7), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 2500));
  // Which block of the document is at the top of the window: its index among all blocks (the item number in its text).
  const where = () => page.evaluate(() => {
    const root = document.getElementById('ag-editor-id');
    const kids = root.children; let lo = 0; let hi = kids.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (kids[mid].getBoundingClientRect().bottom <= 0) lo = mid + 1; else hi = mid }
    const el = kids[lo]; const r = el && el.getBoundingClientRect();
    const m = el && /Item (\d+)|第 (\d+) 节|一项 (\d+)/.exec(el.textContent);
    const n = m ? (m[1] ? +m[1] : m[2] ? +m[2] * 60 : +m[3]) : null;
    return { y: Math.round(window.scrollY), item: r && r.top < window.innerHeight && r.bottom > 0 ? n : null, top: r ? Math.round(r.top) : null, pending: !!window.__typedownMuya?.renderPending, drawn: kids.length };
  });
  const before = await where();
  // The reader: a wheel turn (what arms the "drawn at once" path), then the page up by `up` px in one go.
  await page.mouse.move(700, 500);
  await page.evaluate(() => {
    window.__trail = []; const t0 = performance.now();
    const tick = () => { window.__trail.push([Math.round(performance.now() - t0), Math.round(window.scrollY)]); if (performance.now() - t0 < 15000) requestAnimationFrame(tick) }; requestAnimationFrame(tick);
    window.addEventListener('scroll', () => window.__trail.push([Math.round(performance.now() - t0), Math.round(window.scrollY), 's']), { passive: true });
  });
  // "notches": the wheel turned up a notch at a time (the reader's own scrolling, as on hp), else the page put up by
  // `up` in one go after a single notch.
  // "afterdrawn": nobody at it, the drawing ends, and 300 ms later a script moves the page 100,000 px up (as on hp, 98,600
  // px just after a 1.5 MB document was drawn whole): the page reports it (DrawJump), with the page's own scrolls just before it - a
  // keep of the drawing (`keep`) is told here, made by the check itself, so the report has one to name.
  if (args.includes('afterdrawn')) {
    await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 180000, polling: 50 });
    await new Promise(r => setTimeout(r, 300));
    const report = await page.evaluate(async () => {
      window.__msgs = window.__msgs.filter(m => m.name !== 'DrawJump');
      const from = window.scrollY;
      window.scrollTo(0, Math.max(0, from - 100000));
      await new Promise(r => setTimeout(r, 300));
      return { from: Math.round(from), to: Math.round(window.scrollY), reports: window.__msgs.filter(m => m.name === 'DrawJump').map(m => m.args) };
    });
    const r0 = report.reports[0];
    const ok = !!r0 && r0.dy < -90000 && r0.afterDrawn != null && Array.isArray(r0.writers);
    console.log((ok ? 'PASS' : 'FAIL') + ` a jump of the page 300 ms after the document was drawn whole is reported (${report.from} -> ${report.to}): ${JSON.stringify(report.reports.slice(0, 1))}`);
    if (!ok) process.exitCode = 1;
    await browser.close(); server.close(); return;
  }
  // "take": nobody scrolls; the host hands the page the keyboard (TakeKeyboard: a menu closed, the window activated).
  if (args.includes('take')) {
    // The load's hold over (it gives way to the reader's first wheel or key: here a wheel that moves nothing).
    if (args.includes('hold-over')) await page.evaluate(() => window.dispatchEvent(new WheelEvent('wheel', { deltaY: 0 })));
    await new Promise(r => setTimeout(r, 200));
    await page.evaluate(() => { if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur(); window.__deliver('TakeKeyboard', null) });
    const after0 = await where();
    await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 180000, polling: 100 });
    await new Promise(r => setTimeout(r, 2000));
    const after = await where();
    if (process.env.DEBUG) console.log(JSON.stringify(await page.evaluate(() => { const s = document.getSelection(); const n = s.anchorNode; const el = n && (n.nodeType === 1 ? n : n.parentElement); const top = el && el.closest('#ag-editor-id > *'); return { active: document.activeElement && (document.activeElement.id || document.activeElement.tagName), ranges: s.rangeCount, caretBlock: top && (/Item (\d+)/.exec(top.textContent) || [])[1], tag: top && top.tagName + '.' + top.className, caretTop: top && Math.round(top.getBoundingClientRect().top), cursor: window.__typedownMuya?.contentState?.cursor } })));
    const trail = await page.evaluate(() => window.__trail);
    const jumps = []; for (let i = 1; i < trail.length; i++) { const dy = trail[i][1] - trail[i - 1][1]; if (Math.abs(dy) > 2000) jumps.push([trail[i][0], dy, trail[i][2] || 'f']) }
    const ok = before.item != null && after.item != null && Math.abs(after.item - before.item) <= 40;
    console.log((ok ? 'PASS' : 'FAIL') + ` the keyboard handed to the page while drawing (${before.drawn} of ${N} drawn, the caret in block ${caretBlock}): the window had item ${before.item} @${before.y}, right after ${after0.item} @${after0.y}, once drawn ${after.item} @${after.y}; frame jumps over 2000 px ${JSON.stringify(jumps.slice(0, 5))}`);
    if (!ok) process.exitCode = 1;
    await browser.close(); server.close(); return;
  }
  const notches = Number((args.find(a => /^notches=\d+$/.test(a)) || 'notches=0').slice(8));
  if (notches) {
    for (let k = 0; k < notches; k++) { await page.mouse.wheel({ deltaY: -up / notches }); await new Promise(r => setTimeout(r, 60)); }
  } else {
    await page.mouse.wheel({ deltaY: -120 });
    await page.evaluate(up => window.scrollBy(0, -up), up);
  }
  await new Promise(r => setTimeout(r, 30));
  const scrolled = await where();
  await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 180000, polling: 100 });
  await new Promise(r => setTimeout(r, 3000));
  const after = await where();
  const trail = await page.evaluate(() => window.__trail);
  const jumps = []; for (let i = 1; i < trail.length; i++) { const dy = trail[i][1] - trail[i - 1][1]; if (Math.abs(dy) > 2000) jumps.push([trail[i][0], dy, trail[i][2] || 'f']) }
  const reports = await page.evaluate(() => window.__msgs.filter(m => m.name === 'DrawJump').map(m => m.args));
  // What the reader scrolled to stays in the window once the rest is drawn (a block or so either way: the heights guessed
  // for the blocks around it are put right).
  const ok = before.pending && scrolled.item != null && after.item != null && Math.abs(after.item - scrolled.item) <= 40;
  console.log((ok ? 'PASS' : 'FAIL') + ` up ${up}px from the end while drawing (${before.pending ? 'still drawing' : 'drawn already'}, ${before.drawn} of ${N} drawn): the window had item ${before.item} @${before.y}, scrolled to item ${scrolled.item} @${scrolled.y}, after the rest was drawn item ${after.item} @${after.y}; frame jumps over 2000 px ${JSON.stringify(jumps.slice(0, 5))}; DrawJump ${JSON.stringify(reports.slice(0, 2))}`);
  if (!ok) process.exitCode = 1;
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
