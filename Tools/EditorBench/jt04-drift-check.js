// A long document switched back to near its end, nobody at it while the rest is drawn above (a part at a time): the
// block at the top of the window stays where it is, frame after frame, for 15 s (Automation JT04: 2 px at most). w10,
// a 1.5 MB document of paragraphs pasted from Word, a maximized window: the block went up 33-34 px once, 10 s after the
// switch, with the height prediction on and off - the notice that the first edit may rewrite part of the document going
// away above it while the browser's scroll anchoring was off for the drawing.
// node jt04-drift-check.js [nopredict] [plain] [nonotice] [short=1500] [ms=15000]   (HEIGHT=1047 WIDTH=1600 DELAY=0)
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const args = process.argv.slice(2);
const short = Number((args.find(a => /^short=\d+$/.test(a)) || 'short=1500').slice(6));
const ms = Number((args.find(a => /^ms=\d+$/.test(a)) || 'ms=15000').slice(3));
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
// Paragraphs pasted from Word (an HTML block each) with a heading every so often, a list, a table and a code block now
// and then; "plain": Markdown paragraphs instead of the HTML blocks.
const zh = '清晨六点窗外的天色还是灰蓝的楼下早点铺的蒸笼已经冒起白汽豆浆的香味顺着风飘上来混着一点桂花的甜我泡了一杯茶坐在窗边看街上的人一个个醒过来';
const N = 2940;
const html = !args.includes('plain');
const block = i => {
  if (i % 60 === 0) return `## 第 ${i / 60} 节\n\n`;
  if (i % 97 === 0) return `| a | b | c |\n| --- | --- | --- |\n| ${i} | ${zh.slice(0, 12)} | x |\n| y | z | ${zh.slice(5, 30)} |\n\n`;
  if (i % 131 === 0) return '```js\n' + Array.from({ length: 4 + i % 7 }, (_, k) => `const v${k} = ${i} + ${k} // ${zh.slice(k, k + 10)}`).join('\n') + '\n```\n\n';
  if (i % 17 === 0) return `- 一项 ${i}\n- 两项 ${zh.slice(0, 30)}\n- 三项\n\n`;
  if (html) {
    const t = zh.repeat(3).slice(i % 20, i % 20 + 70 + (i % 5) * 25);
    const style = i % 3 ? 'margin:0pt 0pt 6pt;text-align:justify;line-height:150%;font-size:12pt' : 'margin:0pt 0pt 0.0001pt 20pt;text-indent:-20pt;line-height:150%';
    return `<p STYLE="${style}"><span style="font-family:'宋体';font-size:12.0pt">${t}</span><span lang=EN-US style="font-family:'Times New Roman';font-size:12pt">Item ${i}</span><span style="font-family:'宋体'">${t.slice(0, 40 + i % 30)}</span></p>\n\n`;
  }
  const t = zh.repeat(6).slice(i % 20, i % 20 + 40 + (i % 9) * 45);
  return `Item ${i} ${t}\n\n`;
};
// At its end raw HTML the first edit would lose (CommonMark example 91), as a page pasted from Word has: the notice that
// the first edit may rewrite part of the document comes with the load, above the document, and goes 10 s later.
const lossy = '`Foo\n----\n`\n\n<a title="a lot\n---\nof dashes"/>\n';
const doc = Array.from({ length: N }, (_, i) => block(i)).join('') + (args.includes('nonotice') ? '' : lossy);
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: Number(process.env.WIDTH || 1600), height: Number(process.env.HEIGHT || 1047) });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '1200px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  if (process.env.DELAY) await page.evaluateOnNewDocument(`window.__typedownProgressiveDelayMs=${Number(process.env.DELAY)}`);
  if (args.includes('nopredict')) await page.evaluateOnNewDocument('window.__typedownPredictHeights=false');
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  // The long document first drawn whole and read to its very end, as before the switch: where the page reports it is
  // (OnScroll: the offset and the block at the top of the window) is what the host gives back with the next load.
  const load = (loadId, extra) => page.evaluate((text, loadId, extra) => { window.__msgs = []; window.__deliver('LoadFile', Object.assign({ text, basePath: '/tmp', cursor: null, loadId, hold: 0 }, extra)) }, doc, loadId, extra);
  await load(5, {});
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 5), { timeout: 60000 });
  await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 300000, polling: 200 });
  await new Promise(r => setTimeout(r, 1000));
  // Somewhat above the very end (`short` px): a page at its very end hides a change above it here (the browser keeps it
  // at the end); on w10 it came back 46 px short of it, the blocks below the window laid out taller than as left.
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  await new Promise(r => setTimeout(r, 1500));
  await page.evaluate(short => window.scrollBy(0, -short), short);
  await new Promise(r => setTimeout(r, 1500));
  const left = await page.evaluate(() => { const m = window.__msgs.filter(m => m.name === 'OnScroll').pop(); return m && { y: m.args.scrollY, anchor: m.args.anchor, max: m.args.maximumY } });
  if (!left || !left.anchor) throw new Error('no scroll report at the end: ' + JSON.stringify(left));
  // Another document between (the switch away), then the long one back.
  await load(6, { text: '# other\n\nshort\n' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 6), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  // Every frame from the load on: the block at the top of the window 300 ms after the first part is drawn (as JT04 takes
  // it), then its top whenever it moves; and when the first-edit notice comes and goes (the page telling the host).
  await page.evaluate(ms => {
    const s = window.__jh = { rec: [], ref: null, t0: performance.now(), notes: [] };
    const topBlock = () => { const root = document.getElementById('ag-editor-id'); if (!root) return null; const kids = root.children; let lo = 0, hi = kids.length - 1; while (lo < hi) { const m = (lo + hi) >> 1; if (kids[m].getBoundingClientRect().bottom <= 0) lo = m + 1; else hi = m } for (let k = lo; k < kids.length; k++) { const e = kids[k]; if (e.id && e.textContent.trim()) return e } return null };
    let drawnAt = null;
    const tick = () => {
      const now = Math.round(performance.now() - s.t0);
      if (now > ms + 3000) return;
      const y = Math.round(window.scrollY), h = document.documentElement.scrollHeight;
      // The notice is the host's card now (the page says FirstEditNotice): shown for this load or not.
      { let n = null; for (let k = window.__msgs.length - 1; k >= 0; k--) if (window.__msgs[k].name === 'FirstEditNotice') { n = window.__msgs[k]; break } const w = !!(n && n.args && n.args.show && n.args.loadId === 7); if (w !== !!s.notice) { s.notes.push([now, w ? 1 : 0, y, h]) } s.notice = w }
      if (drawnAt === null && window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 7)) drawnAt = now;
      if (!s.ref) { if (drawnAt !== null && now > drawnAt + 300) { const e = topBlock(); if (e) s.ref = { id: e.id, top: Math.round(e.getBoundingClientRect().top), y, h, at: now } } }
      else if (now - s.ref.at <= ms) {
        const e = document.getElementById(s.ref.id); const top = e ? Math.round(e.getBoundingClientRect().top) : null;
        const last = s.rec.length ? s.rec[s.rec.length - 1] : [0, s.ref.top];
        if (last[1] !== top && s.rec.length < 400) s.rec.push([now, top, y, h]);
      }
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }, ms);
  await load(7, { scrollTop: left.y, scrollAnchor: left.anchor });
  await new Promise(r => setTimeout(r, ms + 2500));
  const r = await page.evaluate(() => { const s = window.__jh; let max = 0, gone = 0; for (const x of s.rec) { if (x[1] === null) gone++; else max = Math.max(max, Math.abs(x[1] - s.ref.top)) } return { notes: s.notes, ref: s.ref, max, gone, rec: s.rec, pending: !!window.__typedownMuya?.renderPending, endY: Math.round(window.scrollY), endH: document.documentElement.scrollHeight } });
  // The notice went while the block was watched (the case w10 failed in), unless the document has none.
  const went = !!r.ref && r.notes.some(n => n[1] === 0 && n[0] > r.ref.at && n[0] <= r.ref.at + ms);
  const ok = !!r.ref && r.gone === 0 && r.max <= 2 && (went || args.includes('nonotice'));
  const moves = r.rec.filter((x, i) => Math.abs(x[1] - (i ? r.rec[i - 1][1] : r.ref.top)) > 2).slice(0, 8);
  console.log(`${N} blocks, ${doc.length} characters, ${args.includes('nopredict') ? 'not predicted' : 'predicted'}; left at ${left.y} of ${left.max}, block ${left.anchor.block} at ${left.anchor.top}`);
    console.log((ok ? 'PASS' : 'FAIL') + ` back at the end, nobody at it for ${ms / 1000} s${went ? ', the first-edit notice gone meanwhile' : args.includes('nonotice') ? '' : ', the first-edit notice NOT gone meanwhile'}: the block at the top of the window moved up to ${r.max} px (${r.gone} frames gone; ${r.rec.length} moves; ref ${JSON.stringify(r.ref)}; end ${r.endY} of ${r.endH}${r.pending ? ', still drawing' : ''}); moves over 2 px [ms, top, y, h] ${JSON.stringify(moves)}; the notice [ms, height, y, h] ${JSON.stringify(r.notes)}`);
    if (process.env.DEBUG) console.log(JSON.stringify(r.rec));
  if (!ok) process.exitCode = 1;
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
