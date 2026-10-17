// Times a LoadFile of a long document in the built page, phase by phase, with a CPU profile.
const B = __dirname + '/';
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(__dirname, '../../Dev/Typedown/Resources/Statics');
const variant = process.argv[2] || 'h3'; const prof = process.argv.includes('--prof');
const para = i => `Paragraph ${i} of the long document, with a few words to make it long enough to take a while.\n\n`;
const docs = {
  h3: '# TL01 C\n\ngamma\n\n' + Array.from({ length: 3000 }, (_, i) => (i % 1000 === 0 ? `## Part ${i / 1000}\n\n` : '') + para(i)).join(''),
  h3000: '# TL01 B\n\nbeta\n\n' + Array.from({ length: 3000 }, (_, i) => `## Part ${i}\n\n` + para(i)).join(''),
  doc300k: (fs.existsSync(__dirname + '/doc300k.md') ? fs.readFileSync(__dirname + '/doc300k.md', 'utf8') : ''),
};
const text = docs[variant];
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'], protocolTimeout: 120000 });
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 600 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: 'x\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__log=[];
    window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;window.__log.push([performance.now(),m.name,raw.length,m.type]);if(m.name==="FileLoaded"||m.name==="StateChange")(window.__raw=window.__raw||{})[m.name]=raw;
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})();
    new PerformanceObserver(l=>{for(const e of l.getEntries())(window.__lt=window.__lt||[]).push([e.startTime,e.duration])}).observe({type:'longtask',buffered:true});`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 60000 });
  await new Promise(r => setTimeout(r, 1500));
  const cdp = await page.target().createCDPSession();
  if (process.argv.includes('--warm')) { for (const t of [text, '# small\n\nx\n']) { await page.evaluate((t) => { window.__log = []; window.__deliver('LoadFile', { text: t, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 2, hold: 0 }) }, t); await page.waitForFunction(() => window.__log.some(x => x[1] === 'StateChange'), { timeout: 60000 }); await new Promise(r => setTimeout(r, 1500)); } }
  if (process.argv.includes('--trace')) await page.tracing.start({ path: `t-${variant}.json`, categories: ['devtools.timeline','disabled-by-default-devtools.timeline','blink','v8'] });
  if (prof) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 100 }); await cdp.send('Profiler.start'); }
  if (process.argv.includes('--ph')) await page.evaluate(() => { const m = window.__typedownMuya; const cs = m.contentState; window.__ph = {};
    const wrap = (o, k, label) => { const f = o[k]; o[k] = function (...a) { const t = performance.now(); try { return f.apply(this, a) } finally { window.__ph[label] = (window.__ph[label] || 0) + performance.now() - t } } };
    wrap(cs, 'importMarkdown', 'importMarkdown'); wrap(cs.stateRender, 'render', 'stateRender.render (tokenize+toHTML+innerHTML)'); { const f = cs.stateRender.renderBlock; let d = 0; cs.stateRender.renderBlock = function (...a) { const t = performance.now(); d++; try { return f.apply(this, a) } finally { d--; if (!d) window.__ph['top-level renderBlock (inline tokenize+vnodes)'] = (window.__ph['top-level renderBlock (inline tokenize+vnodes)'] || 0) + performance.now() - t } } }
    { const desc = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML'); Object.defineProperty(Element.prototype, 'innerHTML', { ...desc, set(v) { const t = performance.now(); desc.set.call(this, v); if (v.length > 10000) window.__ph['innerHTML (parse+DOM build) ' ] = (window.__ph['innerHTML (parse+DOM build) '] || 0) + performance.now() - t } }) }
    const sc = cs.setCursor; cs.setCursor = function () { let t = performance.now(); void document.body.offsetHeight; window.__ph['forced layout before setCursor'] = performance.now() - t; t = performance.now(); sc.apply(this); window.__ph['setCursor (addRange)'] = performance.now() - t };
    wrap(m, 'getMarkdown', 'getMarkdown'); wrap(m, 'setMarkdown', 'setMarkdown total'); wrap(m, 'getWordCount', 'getWordCount'); wrap(m, 'dispatchLoadChange', 'dispatchLoadChange');
    wrap(JSON, 'stringify', 'JSON.stringify'); });
  const t0 = await page.evaluate((text) => { window.__log = []; window.__lt = []; const t = performance.now();
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 3, hold: 0 }); window.__afterDeliver = performance.now(); return t; }, text);
  await page.waitForFunction(() => window.__log.some(x => x[1] === 'StateChange'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 2500));
  if (process.argv.includes('--trace')) await page.tracing.stop();
  if (prof) { const { profile } = await cdp.send('Profiler.stop'); 0 && fs.writeFileSync(`p-${variant}.cpuprofile`, JSON.stringify(profile)); }
  const r = await page.evaluate(() => ({ raw: window.__raw, ph: window.__ph, log: window.__log, lt: window.__lt, ad: window.__afterDeliver }));
  console.log(`${variant}: ${text.length} chars; deliver (JSON.parse+dispatch) ${(r.ad - t0).toFixed(0)} ms`);
  for (const [t, n, len, ty] of r.log) console.log(`  +${(t - t0).toFixed(0).padStart(5)} ms ${n} ${ty} ${len} bytes`);
  console.log('  long tasks: ' + r.lt.filter(x => x[0] >= t0 - 5).map(([s, d]) => `@+${(s - t0).toFixed(0)} ${d.toFixed(0)}ms`).join(', '));
  for (const [k, v] of Object.entries(r.ph || {})) console.log(`  ${v.toFixed(0).padStart(5)} ms  ${k}`);
  if (0 && r.raw) for (const k in r.raw) fs.writeFileSync(`raw-${variant}-${k}.json`, r.raw[k]);
  await browser.close(); server.close();
})();
