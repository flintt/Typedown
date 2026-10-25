// Where a heading far down lands when the outline jumps to it (the host's ScrollTo, EditorViewModel.JumpBySlug) while a
// long document is still drawn a part at a time - the jump draws the rest at once, and the blocks it passes stand at
// the size given them until laid out (assets/styles/index.css: the long-document rule) - and once more after it is
// drawn. Mixed documents of 100k, 300k and 1M characters (fixtures/mixed), each with the old per-character estimate
// (window.__typedownPretextOff) and with the predicted heights (predictHeights.js). After each jump, every frame for
// a while (until the blocks guessed while drawn are predicted too, ruleRest): where the heading is against where the
// jump puts it (16 px from the top of the window), how often it moved, where it ends up.
//
//   node heading-jump-check.js [--only 100k|300k|1m] [--max-off 50]
//
// With predictions: the heading ends within --max-off px of its place and moves no more often than with the old
// estimate. Where the long-document rule stays once drawn (1M), the old estimate must end farther off or move more
// often, or the check tells nothing apart; below it the page is laid out whole once drawn and both land exactly.
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const arg = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? args[i + 1] : fallback; };
const maxOff = Number(arg('--max-off', 50));
const only = arg('--only', null);
const fixture = name => zlib.gunzipSync(fs.readFileSync(path.join(__dirname, 'fixtures/mixed', name))).toString('utf8');
const docs = [['100k', 'mixed-100k.md.gz'], ['300k', 'mixed-300k.md.gz'], ['1m', 'mixed-1m.md.gz']].filter(([label]) => !only || only === label);
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

async function open(browser, port, off) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 800 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{${off ? 'window.__typedownPretextOff=true;' : ''}const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  return { page, errors };
}

// The page's half (the same script as the E2E case HJ02): jump to the heading `at` (a share of the document) not drawn
// yet, then watch it until `untilRest` (the rest predicted) and a moment more.
const JUMP = fs.readFileSync(path.join(__dirname, 'heading-jump-page.js'), 'utf8');

async function jump(page, at) {
  await page.evaluate(JUMP);
  const started = await page.evaluate(at => window.__hjJump(at), at);
  if (!started.ok) return started;
  await page.waitForFunction(() => window.__hjj && window.__hjj.done, { timeout: 120000, polling: 200 });
  return page.evaluate(() => window.__hjSummary());
}

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  try {
    for (const [label, file] of docs) {
      const text = fixture(file);
      const r = {};
      for (const mode of ['old', 'pretext']) {
        const { page, errors } = await open(browser, port, mode === 'old');
        await page.evaluate(text => { window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, scrollAnchor: null, loadId: 2, hold: 0 }) }, text);
        await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args?.loadId === 2) || window.__typedownMuya?.contentState.blocks.length > 50, { timeout: 120000, polling: 20 });
        // While drawn: as soon as the first part is there.
        const drawing = await jump(page, 0.75);
        // Once drawn (and the rest predicted): another heading, back up.
        await page.waitForFunction(() => !window.__typedownMuya.renderPending, { timeout: 120000, polling: 100 });
        const drawn = await jump(page, 0.45);
        r[mode] = { drawing, drawn };
        for (const [when, j] of [['while drawn', drawing], ['once drawn', drawn]]) {
          console.log(`${label} ${mode} ${when}: ${JSON.stringify(j)}`);
        }
        check(!errors.length, `${label} ${mode}: no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
        await page.close();
      }
      const p = r.pretext, o = r.old;
      for (const when of ['drawing', 'drawn']) {
        const pj = p[when], oj = o[when];
        if (!pj.ok) { check(false, `${label} ${when}: the jump happened (${pj.why})`); continue; }
        check(pj.endOff <= maxOff, `${label} ${when}: with predictions the heading ends within ${maxOff} px of its place (${pj.endOff} px; old ${oj.endOff ?? '-'})`);
        check(!oj.ok || pj.moves <= oj.moves, `${label} ${when}: with predictions it moves no more often than with the old estimate (${pj.moves} vs ${oj.moves ?? '-'})`);
      }
      // Below the long-document size the page is laid out whole once drawn - the jump lands exactly either way; past it
      // (the 1M document) the blocks passed stand at their stand-in sizes, and the old ones must show.
      if (o.drawn.long) {
        const worse = j => j.ok && (j.endOff > maxOff || j.moves > p.drawing.moves);
        check(worse(o.drawing) || worse(o.drawn), `${label}: the old estimate lands farther off or moves more (old ${o.drawing.endOff} px/${o.drawing.moves} moves while drawn, ${o.drawn.endOff} px/${o.drawn.moves} once drawn) - the check tells them apart`);
      }
    }
  } finally { await browser.close(); server.close(); }
  console.log(ok ? '\nall passed' : '\nFAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
