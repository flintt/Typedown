// How much the scroll bar jumps while a long document is drawn a part at a time (Muya's renderProgressive). The part not
// drawn yet stands in as room above and below what is drawn (StateRender.estimateRest), and in a document past the
// long-document size a block drawn off screen stands at a size of its own until it is laid out (contain-intrinsic-size,
// assets/styles/index.css): wherever those stand-ins are off, the page's height - the thumb's size - and the reader's
// place in it - the thumb's position - move while nobody touches anything, and again once the drawing ends.
//
// Mixed Chinese / English / Japanese / Korean prose with headings, lists, code, quotes and tables, about 100k, 300k and
// 1M characters (fixtures/mixed; the 100k one the first part of the 300k one), each opened at the top and at a block in the middle (a tab put back where it was left).
// Each is measured twice, with the old per-character estimate (window.__typedownPretextOff) and with the predicted
// heights (predictHeights.js), every frame from the load until a second after the drawing ends - and, with the
// predictions, until the blocks only guessed while drawn are predicted too (ruleRest). Compared with the page wholly
// laid out at the end (content-visibility forced visible, contained where the long-document rule stays):
//   height  - how far the page's height was from its real one while drawn, in per cent: on average over the time, and
//             at worst
//   thumb   - how far the thumb was from where it belongs for the place being read, at worst, in per cent of the track
//   end     - the page's height once drawn against its real one (a 1M document keeps the stand-ins once drawn)
//   drift   - how far the block being read moved in the window (keepInPlace; must stay within 2 px)
//   stall   - the longest gap between two frames while drawing
//   time    - how long the drawing took: with predictions at most 1.2 times the old estimate's, the longest frame gap
//             at most 50 ms longer
// The old estimate must miss at least one of the height thresholds, or the check measures nothing.
//
//   node height-jump-check.js [--only 100k|300k|1m] [--max-mean 2.5] [--max-worst 10] [--max-thumb 1] [--max-end 1] [--max-slower 1.2]
//   --init 'window.__typedownHeightsShare=0.05'  - script run in the page before it loads, with predictions (experiments)
//   PRETEXT_OFF=1 node height-jump-check.js   - the old estimate alone, held to the thresholds: must fail
//   FRAMES_OUT=/tmp/f node height-jump-check.js - every frame, per case, to /tmp/f-<doc>-<top|middle>-<old|pretext>.json
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const arg = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? args[i + 1] : fallback; };
const maxMean = Number(arg('--max-mean', 2.5));
const maxSlower = Number(arg('--max-slower', 1.2));
const maxWorst = Number(arg('--max-worst', 10));
const maxThumb = Number(arg('--max-thumb', 1));
const maxEnd = Number(arg('--max-end', 1));
const only = arg('--only', null);
const fixture = name => zlib.gunzipSync(fs.readFileSync(path.join(__dirname, 'fixtures/mixed', name))).toString('utf8');
const docs = [['100k', 'mixed-100k.md.gz'], ['300k', 'mixed-300k.md.gz'], ['1m', 'mixed-1m.md.gz']].filter(([label]) => !only || only === label);

const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

async function open(browser, port, off) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 800 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  // INIT: more script for the page before it loads (an experiment's switches; not for the predictions-off run).
  const init = off ? 'window.__typedownPretextOff=true;' : (arg('--init', null) || process.env.INIT || '');
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
  // The blocks only guessed while drawn are predicted once it is drawn, in idle time (ruleRest): the page as it ends up.
  const restAt = await page.waitForFunction(() => window.__typedownPretextOff || (window.__typedownPretextLog && window.__typedownPretextLog.restDoneAt !== undefined) ? (window.__typedownPretextLog?.restDoneAt ?? 0) - window.__t0 : false, { timeout: 60000, polling: 100 }).then(h => h.jsonValue()).catch(() => null);
  await new Promise(r => setTimeout(r, 1000));
  const frames = await page.evaluate(() => { window.__sampling = false; return window.__frames; });
  const end = await page.evaluate(() => ({ h: document.documentElement.scrollHeight, y: window.scrollY, top: document.getElementById(window.__watchKey)?.getBoundingClientRect().top, vh: window.innerHeight, long: !!document.querySelector('#editor.ag-long-document'), blocks: window.__typedownMuya.contentState.blocks.length, log: window.__typedownPretextLog || null, slices: (window.__typedownProgressiveLog || []).slice(-1)[0]?.slices }));
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
  return { frames, end, real, doneAt, restAt, t0, watchTop: anchor ? anchor.top : null };
}

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  const results = [];
  try {
    const modes = process.env.PRETEXT_OFF ? ['old'] : ['old', 'pretext'];
    for (const [label, file] of docs) {
      const text = fixture(file);
      for (const at of [0, 0.5]) {
        const byMode = {};
        for (const mode of modes) {
          const { page, errors } = await open(browser, port, mode === 'old');
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
          let worst = 0, weighted = 0, span = 0, thumbErr = 0, drift = 0, stall = 0, prev = null;
          for (const f of shown) {
            const err = Math.abs(f.h - real.h) / real.h * 100;
            if (f.pending) {
              worst = Math.max(worst, err);
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
          const name = `${label} ${at ? 'middle' : 'top'} ${mode}`;
          if (process.env.FRAMES_OUT) fs.writeFileSync(`${process.env.FRAMES_OUT}-${label}-${at ? 'middle' : 'top'}-${mode}.json`, JSON.stringify({ frames: frames.map(f => ({ ...f, t: Math.round(f.t - frames[0].t) })), end, real, readTop }));
          console.log(`${name}: ${text.length} chars, ${end.blocks} blocks, drawn in ${Math.round(r.doneAt)} ms over ${shown.length} frames, ${end.slices} slices${r.restAt ? `, the rest predicted ${Math.round(r.restAt)} ms after the load` : ''}, long-document class at the end ${end.long ? 'on' : 'off'}`);
          console.log(`  height off by ${mean.toFixed(2)}% on average, ${worst.toFixed(2)}% at worst; real ${real.h} px, at the end ${end.h} px (${endErr.toFixed(2)}%); thumb ${thumbErr.toFixed(2)}% of the track at worst, drift ${drift.toFixed(1)} px, longest frame gap ${Math.round(stall)} ms`);
          if (end.log && mode !== 'old') console.log(`  pretext: ${JSON.stringify(end.log)}`);
          const res = { name, mode, mean, worst, thumbErr, endErr, drift, stall, doneAt: r.doneAt, restAt: r.restAt, log: end.log, errors };
          byMode[mode] = res;
          results.push(res);
        }
        const name = `${label} ${at ? 'middle' : 'top'}`;
        const misses = r => [r.mean > maxMean, r.worst > maxWorst, at > 0 && r.thumbErr > maxThumb, r.endErr > maxEnd].some(Boolean);
        const p = byMode.pretext || byMode.old;
        check(p.mean <= maxMean, `${name} ${p.mode}: the page's height is within ${maxMean}% of its real one on average while drawn (${p.mean.toFixed(2)}%)`);
        check(p.worst <= maxWorst, `${name} ${p.mode}: ... and within ${maxWorst}% at worst (${p.worst.toFixed(2)}%)`);
        if (at > 0) check(p.thumbErr <= maxThumb, `${name} ${p.mode}: the thumb stays within ${maxThumb}% of where it belongs while drawn (${p.thumbErr.toFixed(2)}%)`);
        check(p.endErr <= maxEnd, `${name} ${p.mode}: once drawn (and the rest predicted), the page's height is within ${maxEnd}% of its real one (${p.endErr.toFixed(2)}%)`);
        check(p.drift <= 2, `${name} ${p.mode}: the block read stays where it is (${p.drift.toFixed(1)} px)`);
        check(!p.errors.length, `${name} ${p.mode}: no page errors${p.errors.length ? ': ' + p.errors.join(' | ') : ''}`);
        if (byMode.pretext) {
          const o = byMode.old;
          check(misses(o), `${name}: the old estimate misses a threshold (mean ${o.mean.toFixed(2)}%, worst ${o.worst.toFixed(2)}%, thumb ${o.thumbErr.toFixed(2)}%, end ${o.endErr.toFixed(2)}%) - the check tells them apart`);
          check(p.doneAt <= maxSlower * o.doneAt, `${name}: drawn in at most ${maxSlower} times the old estimate's time (${Math.round(p.doneAt)} vs ${Math.round(o.doneAt)} ms, ${(p.doneAt / o.doneAt).toFixed(2)}x)`);
          check(p.stall <= o.stall + 50, `${name}: the longest frame gap no more than 50 ms over the old estimate's (${Math.round(p.stall)} vs ${Math.round(o.stall)} ms)`);
        }
      }
    }
  } finally {
    await browser.close(); server.close();
  }
  if (process.env.JSON_OUT) fs.writeFileSync(process.env.JSON_OUT, JSON.stringify(results, null, 1));
  console.log(ok ? '\nall passed' : '\nFAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
