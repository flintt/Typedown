// Refill bench: a long HTML-heavy document loaded anchored far down (as a tab switched back to), frame gaps while the
// blocks above are drawn a slice at a time. node refill-bench.js [at=0.95] [--profile out.cpuprofile]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const args = process.argv.slice(2); const at = Number(args.find(a => /^0?\.\d+$/.test(a)) || 0.95);
const prof = args.includes('--profile') ? args[args.indexOf('--profile') + 1] : null;
const trace = args.includes('--trace') ? args[args.indexOf('--trace') + 1] : null;
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const zh = '清晨六点窗外的天色还是灰蓝的楼下早点铺的蒸笼已经冒起白汽豆浆的香味顺着风飘上来混着一点桂花的甜我泡了一杯茶坐在窗边看街上的人一个个醒过来';
const block = i => {
  const t = zh.repeat(2).slice(i % 20, i % 20 + 70 + (i % 5) * 15);
  if (i % 60 === 0) return `## 第 ${i / 60} 节\n\n`;
  const style = i % 3 ? 'margin:0pt 0pt 6pt;text-align:justify;line-height:150%;font-size:12pt' : 'margin:0pt 0pt 0.0001pt 20pt;text-indent:-20pt;line-height:150%';
  return `<p STYLE="${style}"><span style="font-family:'宋体';font-size:12.0pt;mso-bidi-font-family:'Times New Roman'">${t}</span><span lang=EN-US style="font-family:'Times New Roman';font-size:12pt">Item ${i}</span><span style="font-family:'宋体'">${t.slice(0, 40)}</span></p>\n\n`;
};
const doc = Array.from({ length: 2940 }, (_, i) => block(i)).join('');
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1280, height: 800 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  const variants = { noscroll: "(()=>{const a=EventTarget.prototype.addEventListener;EventTarget.prototype.addEventListener=function(t,...r){if(t==='scroll')return;return a.call(this,t,...r)}})()", slice20: 'window.__typedownProgressiveSliceMs=20', slice12: 'window.__typedownProgressiveSliceMs=12', slice80: 'window.__typedownProgressiveSliceMs=80', nopause: 'window.__typedownProgressiveNoPause=true' };
  const flags = Object.keys(variants).filter(k => args.includes(k));
  const variant = flags.join('+');
  await page.evaluateOnNewDocument(flags.map(k => variants[k]).join(';'));
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  const anchorIdx = Math.round(2940 * at);
  const anchorText = `Item ${anchorIdx - (anchorIdx % 60 === 0 ? 1 : 0)}`;
  const cdp = await page.target().createCDPSession();
  if (prof) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 200 }); await cdp.send('Profiler.start'); }
  await page.evaluate((text, anchorText, anchorIdx) => {
    window.__gaps = []; window.__t0 = performance.now(); let last = performance.now();
    const tick = () => { const now = performance.now(); if (window.__typedownMuya?.renderPending) (window.__wheeling ? (window.__wgaps = window.__wgaps || []) : window.__gaps).push(now - last); last = now; if (now - window.__t0 < 120000) requestAnimationFrame(tick) }; requestAnimationFrame(tick);
    window.__msgs = [];
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 99999, scrollAnchor: { block: anchorIdx, top: 120 }, loadId: 7, hold: 0 });
  }, doc, anchorText, anchorIdx);
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 7), { timeout: 60000 });
  const t1 = Date.now();
  // With "wheel": the reader turns the wheel while it draws, a notch every 400 ms for 8 s (down, down, up, up).
  if (args.includes('wheel')) { await page.mouse.move(640, 400); await page.evaluate(() => { window.__wheeling = true }); for (let i = 0; i < 20; i++) { await page.mouse.wheel({ deltaY: i % 4 < 2 ? 120 : -120 }); await new Promise(r => setTimeout(r, 400)); } await page.evaluate(() => { window.__wheeling = false }); }
  if (trace) { await new Promise(r => setTimeout(r, 1500)); await page.tracing.start({ path: trace, categories: ['devtools.timeline', 'disabled-by-default-devtools.timeline', 'blink'] }); await new Promise(r => setTimeout(r, 4000)); await page.tracing.stop(); }
  // With "jumptest": the page moved 100,000 px up by a script 2 s in, nobody at it - the drawing must report it (DrawJump).
  let jumpReport = null;
  if (args.includes('jumptest')) {
    await new Promise(r => setTimeout(r, 2000));
    jumpReport = await page.evaluate(async () => { const pending = !!window.__typedownMuya?.renderPending; window.scrollTo(0, Math.max(0, window.scrollY - 100000)); await new Promise(r => setTimeout(r, 300)); return { pending, reports: window.__msgs.filter(m => m.name === 'DrawJump').map(m => m.args) } });
  }
  await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 180000, polling: 500 });
  if (args.includes('jumptest')) {
    // Reported first; the load's hold may put the page back after it (reported in turn).
    const ok = jumpReport.reports.length >= 1 && jumpReport.reports[0].dy < -90000;
    console.log((ok ? 'PASS' : 'FAIL') + ' the jump made while drawing (' + (jumpReport.pending ? 'still drawing' : 'drawn already') + ') is reported: ' + JSON.stringify(jumpReport.reports.slice(0, 2)));
    if (!ok) process.exitCode = 1;
  }
  const total = Date.now() - t1;
  if (prof) { const { profile } = await cdp.send('Profiler.stop'); fs.writeFileSync(prof, JSON.stringify(profile)); }
  const r = await page.evaluate(() => { const g = [...window.__gaps].sort((a, b) => a - b); const q = p => Math.round(g[Math.min(g.length - 1, Math.floor(g.length * p))]); const w = [...(window.__wgaps || [])].sort((a, b) => a - b); const wq = p => w.length ? Math.round(w[Math.min(w.length - 1, Math.floor(w.length * p))]) : null; return { wheelFrames: w.length, wheelP50: wq(0.5), wheelP90: wq(0.9), frames: g.length, p50: q(0.5), p90: q(0.9), max: Math.round(g[g.length - 1] || 0), stats: window.__typedownPretextStats, log: (window.__typedownProgressiveLog || []).slice(-1)[0], blocks: document.getElementById('ag-editor-id').children.length } });
  console.log(JSON.stringify({ variant: (variant || 'base') + (args.includes('wheel') ? ' wheel' : ''), at, docKB: Math.round(doc.length / 1024), drawAfterLoadMs: total, ...r }));
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
