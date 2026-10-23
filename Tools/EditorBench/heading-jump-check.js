// Where a heading far down lands when the outline jumps to it (the host's ScrollTo, EditorViewModel.JumpBySlug) while a
// long document is still drawn a part at a time (the jump draws the rest at once, StateRender.completeRender; the part
// not drawn yet stood at its estimated room, estimateRest), and once more after it is drawn. Mixed documents of 100k,
// 300k and 1M characters (fixtures/mixed). After each jump, every frame until the drawing is done and a second more (at
// least a second): where the heading is against where the jump puts it (16 px from the top of the window).
// A 300k document jumped to while drawn landed right, then half a second later one move threw the heading 88,648 px away
// (100k: 27,297 px after 68 ms): the load's delayed second scroll (Muya/index.tsx settleScroll), held back by the drawing
// the jump finished, put the page back where the load had left it.
//
//   node heading-jump-check.js [--only 100k|300k|1m] [--max-off 50]
//
// Passes when, for each jump, the heading is on the page in every frame watched and never more than --max-off px from
// its place. The 1M document only with --only 1m: past the long-document size the blocks passed stand at the rule's
// stand-in size until laid out (assets/styles/index.css), and the heading drifts by hundreds of pixels once drawn -
// the estimate's own error, not this (vm103 2026-10-23: 876 px while drawn, 153 px once drawn).
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const arg = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? args[i + 1] : fallback; };
const maxOff = Number(arg('--max-off', 50));
const only = arg('--only', null);
const fixture = name => zlib.gunzipSync(fs.readFileSync(path.join(__dirname, 'fixtures/mixed', name))).toString('utf8');
const docs = [['100k', 'mixed-100k.md.gz'], ['300k', 'mixed-300k.md.gz'], ['1m', 'mixed-1m.md.gz']].filter(([label]) => only ? only === label : label !== '1m');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

async function open(browser, port) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 800 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  return { page, errors };
}

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
      const { page, errors } = await open(browser, port);
      await page.evaluate(text => { window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, scrollAnchor: null, loadId: 2, hold: 0 }) }, text);
      await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args?.loadId === 2) || window.__typedownMuya?.contentState.blocks.length > 50, { timeout: 120000, polling: 20 });
      // While drawn: as soon as the first part is there.
      const drawing = await jump(page, 0.75);
      // Once drawn: another heading, back up.
      await page.waitForFunction(() => !window.__typedownMuya.renderPending, { timeout: 120000, polling: 100 });
      const drawn = await jump(page, 0.45);
      for (const [when, j] of [['while drawn', drawing], ['once drawn', drawn]]) {
        console.log(`${label} ${when}: ${JSON.stringify(j)}`);
        if (!j.ok) { check(false, `${label} ${when}: the jump happened (${j.why})`); continue; }
        check(when !== 'while drawn' || j.wasDrawing, `${label} ${when}: the jump came while the document was still drawn`);
        check(j.watchedMs >= 1000, `${label} ${when}: watched for at least 1 s (${j.watchedMs} ms)`);
        check(!j.missingFrames && j.worstOff <= maxOff, `${label} ${when}: the heading stays within ${maxOff} px of its place in every frame (worst ${j.worstOff} px, end ${j.endOff} px, ${j.missingFrames} frames without it)`);
      }
      check(!errors.length, `${label}: no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
      await page.close();
    }
  } finally { await browser.close(); server.close(); }
  console.log(ok ? '\nall passed' : '\nFAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
