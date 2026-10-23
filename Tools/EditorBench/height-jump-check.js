// How much the scroll bar jumps while a long document is drawn a part at a time (Muya's renderProgressive). The part not
// drawn yet stands in as room above and below what is drawn (StateRender.estimateRest), and in a document past the
// long-document size a block drawn off screen stands at a size of its own until it is laid out (contain-intrinsic-size,
// assets/styles/index.css): wherever those stand-ins are off, the page's height - the thumb's size - and the reader's
// place in it - the thumb's position - move while nobody touches anything, and again once the drawing ends.
//
// Mixed Chinese / English / Japanese / Korean prose with headings, lists, code, quotes and tables, about 300k and 1M
// characters (fixtures/mixed), each opened at the top and at a block in the middle (a tab put back where it was left).
// Every frame from the load to a second after the drawing ends: the page's height and the thumb's position. Compared
// with the page wholly laid out at the end (content-visibility forced visible):
//   height  - how far the page's height was from its real one while drawn, in per cent: on average over the time
//             (mean), at worst, and at worst once the heights of all of what was not drawn were predicted (settled;
//             predictHeights.js - before that, the first second or so, the rest is guessed from a sample of it)
//   thumb   - how far the thumb was from where it belongs for the place being read, at worst, in per cent of the track
//   end     - the page's height once drawn against its real one (a 1M document keeps the stand-ins once drawn)
//   drift   - how far the block being read moved in the window (keepInPlace; must stay within 2 px)
//   stall   - the longest gap between two frames while drawing
//
//   node height-jump-check.js [--only 300k|1m] [--max-mean 2] [--max-settled 1.5] [--max-worst 10] [--max-thumb 1] [--max-end 1]
//   PRETEXT_OFF=1 node height-jump-check.js   - the old per-character estimate (window.__typedownPretextOff): must fail
//   FRAMES_OUT=/tmp/f node height-jump-check.js - every frame, per case, to /tmp/f-<doc>-<top|middle>.json
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const arg = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? args[i + 1] : fallback; };
const maxMean = Number(arg('--max-mean', 2));
const maxSettled = Number(arg('--max-settled', 1.5));
const maxWorst = Number(arg('--max-worst', 10));
const maxThumb = Number(arg('--max-thumb', 1));
const maxEnd = Number(arg('--max-end', 1));
const only = arg('--only', null);
const fixture = name => zlib.gunzipSync(fs.readFileSync(path.join(__dirname, 'fixtures/mixed', name))).toString('utf8');
const docs = [['300k', 'mixed-300k.md.gz'], ['1m', 'mixed-1m.md.gz']].filter(([label]) => !only || only === label);

const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

async function open(browser, port) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 800 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  const init = process.env.PRETEXT_OFF ? 'window.__typedownPretextOff=true;' : '';
  await page.evaluateOnNewDocument(`(()=>{${init};const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  return { page, errors };
}

// One load: every frame's height, scroll position and the read block's place, then the page wholly laid out.
async function measure(page, text, at) {
  await page.evaluate(() => {
    window.__frames = []; window.__watchKey = null; window.__sampling = true;
    const tick = () => {
      if (!window.__sampling) return;
      const el = window.__watchKey ? document.getElementById(window.__watchKey) : null;
      window.__frames.push({ t: performance.now(), h: document.documentElement.scrollHeight, y: window.scrollY, top: el ? el.getBoundingClientRect().top : null, pending: !!window.__typedownMuya?.renderPending });
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });
  const blocksGuess = text.split(/\n{2,}/).length;
  const anchor = at > 0 ? { block: Math.round(blocksGuess * at), text: '', top: 120 } : null;
  await page.evaluate((text, anchor) => {
    window.__msgs = []; window.__typedownProgressiveDone = null; window.__t0 = performance.now();
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: anchor ? 99999 : 0, scrollAnchor: anchor, loadId: 2, hold: 0 });
  }, text, anchor);
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 2), { timeout: 120000 });
  // The block being read: the anchor's, or the first one.
  await page.evaluate(i => { const b = window.__typedownMuya.contentState.blocks; window.__watchKey = (b[Math.min(b.length - 1, i)] || b[0]).key; }, anchor ? anchor.block : 0);
  await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 300000, polling: 200 });
  const doneAt = await page.evaluate(() => (window.__typedownProgressiveDone ?? performance.now()) - window.__t0);
  await new Promise(r => setTimeout(r, 1000));
  const frames = await page.evaluate(() => { window.__sampling = false; return window.__frames; });
  const end = await page.evaluate(() => ({ h: document.documentElement.scrollHeight, y: window.scrollY, top: document.getElementById(window.__watchKey)?.getBoundingClientRect().top, vh: window.innerHeight, long: !!document.querySelector('#editor.ag-long-document'), blocks: window.__typedownMuya.contentState.blocks.length, log: window.__typedownPretextLog || null }));
  // Wholly laid out: every block at its real size. Where the read block then is (its distance from the top of the page)
  // is where the thumb belongs.
  // A document that keeps the long-document rule once drawn keeps its blocks contained (content-visibility: auto lays
  // each out on its own): laid out for real as such.
  const real = await page.evaluate(long => new Promise(done => {
    const style = document.createElement('style');
    style.textContent = `#ag-editor-id > * { content-visibility: visible !important; contain-intrinsic-size: none !important; ${long ? 'contain: layout style paint !important;' : ''} }`;
    document.head.appendChild(style);
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const el = document.getElementById(window.__watchKey);
      done({ h: document.documentElement.scrollHeight, abs: el.getBoundingClientRect().top + window.scrollY });
    }));
  }), end.long);
  const t0 = await page.evaluate(() => window.__t0);
  return { frames, end, real, doneAt, t0, watchTop: anchor ? anchor.top : null };
}

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  const results = [];
  try {
    for (const [label, file] of docs) {
      const text = fixture(file);
      for (const at of [0, 0.5]) {
        const { page, errors } = await open(browser, port);
        const r = await measure(page, text, at);
        await page.close();
        const { frames, end, real } = r;
        const vh = end.vh;
        // The reader's place: the block read stays at its height in the window (drift), so the thumb belongs where that
        // block's real distance from the top puts it.
        const readTop = r.watchTop ?? (frames.find(f => f.top != null)?.top ?? 0);
        const thumbReal = Math.max(0, real.abs - readTop) / Math.max(1, real.h - vh);
        const shown = frames.filter(f => f.top != null);
        const firstAt = shown.length ? shown[0].t : 0;
        const settledAt = end.log && end.log.doneAtTime !== undefined ? end.log.doneAtTime : null;
        let worst = 0, settled = settledAt === null ? null : 0, weighted = 0, span = 0, thumbErr = 0, drift = 0, stall = 0, prev = null;
        for (const f of shown) {
          const err = Math.abs(f.h - real.h) / real.h * 100;
          if (f.pending) {
            worst = Math.max(worst, err);
            if (settledAt !== null && f.t >= settledAt) settled = Math.max(settled, err);
            if (prev) { weighted += err * (f.t - prev.t); span += f.t - prev.t; }
          }
          const thumb = f.h > vh ? f.y / (f.h - vh) : 0;
          if (at > 0) thumbErr = Math.max(thumbErr, Math.abs(thumb - thumbReal) * 100);
          if (f.t > firstAt + 50) drift = Math.max(drift, Math.abs(f.top - readTop));
          if (prev && f.pending) stall = Math.max(stall, f.t - prev.t);
          prev = f;
        }
        const mean = span ? weighted / span : worst;
        const endErr = Math.abs(end.h - real.h) / real.h * 100;
        if (process.env.FRAMES_OUT) fs.writeFileSync(`${process.env.FRAMES_OUT}-${label}-${at ? 'middle' : 'top'}.json`, JSON.stringify({ frames: frames.map(f => ({ ...f, t: Math.round(f.t - frames[0].t) })), end, real, readTop }));
        const name = `${label} ${at ? 'middle' : 'top'}`;
        console.log(`${name}: ${text.length} chars, ${end.blocks} blocks, drawn in ${Math.round(r.doneAt)} ms over ${shown.length} frames, long-document class at the end ${end.long ? 'on' : 'off'}`);
        console.log(`  height off by ${mean.toFixed(2)}% on average, ${worst.toFixed(2)}% at worst, ${settled === null ? 'never all predicted' : settled.toFixed(2) + '% at worst once all predicted (' + Math.round(settledAt - r.t0) + ' ms after the load)'}; real ${real.h} px, at the end ${end.h} px (${endErr.toFixed(2)}%)`);
        console.log(`  thumb ${thumbErr.toFixed(2)}% of the track at worst, drift ${drift.toFixed(1)} px, longest frame gap ${Math.round(stall)} ms`);
        if (end.log) console.log(`  pretext: ${JSON.stringify(end.log)}`);
        results.push({ name, mean, worst, settled, thumbErr, endErr, drift, stall, doneAt: r.doneAt, log: end.log });
        check(mean <= maxMean, `${name}: the page's height is within ${maxMean}% of its real one on average while drawn (${mean.toFixed(2)}%)`);
        check(worst <= maxWorst, `${name}: ... and within ${maxWorst}% at worst (${worst.toFixed(2)}%)`);
        check(settled !== null && settled <= maxSettled, `${name}: ... and within ${maxSettled}% once the heights of all of the rest are predicted (${settled === null ? 'never' : settled.toFixed(2) + '%'})`);
        if (at > 0) check(thumbErr <= maxThumb, `${name}: the thumb stays within ${maxThumb}% of where it belongs while drawn (${thumbErr.toFixed(2)}%)`);
        check(endErr <= maxEnd, `${name}: once drawn, the page's height is within ${maxEnd}% of its real one (${endErr.toFixed(2)}%)`);
        check(drift <= 2, `${name}: the block read stays where it is (${drift.toFixed(1)} px)`);
        check(!errors.length, `${name}: no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
      }
    }
  } finally {
    await browser.close(); server.close();
  }
  if (process.env.JSON_OUT) fs.writeFileSync(process.env.JSON_OUT, JSON.stringify(results, null, 1));
  console.log(ok ? '\nall passed' : '\nFAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
