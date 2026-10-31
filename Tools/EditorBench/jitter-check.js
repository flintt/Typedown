// The text in the window holding still while a long document is drawn a part at a time and after (E2E JT01 in the real
// app: it shook by 0.7-0.9 px for a couple of seconds, and once jumped by 24 px). Every frame, where the blocks in the
// window are on screen; a block moving by half a pixel or more from one frame to the next is a move.
//   middle - a 1M document put back at a block in its middle (a tab read half way, switched back to): drawn above and
//            below that block, the room above it (the root's ::before) and the blocks going in above moving it, with the
//            page scrolled by whole pixels to make up for it
//   kept   - a 300k document switched back to at its top, kept by the page (its elements put back): the long-document
//            rule on while it settles, off after - the first block in the window stood at the rule's stand-in size
//            (50 px) until then, and grew or shrank to its own with nothing making up for it
//
// Headless Chromium lays out the blocks in the window even with the rule on; in the real app the first one stayed at the
// stand-in: the kept case also checks the rule does not go on and off at all.
//
//   node jitter-check.js [--only middle|kept]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path'); const zlib = require('zlib');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const only = (i => i >= 0 ? args[i + 1] : null)(args.indexOf('--only'));
const fixture = name => zlib.gunzipSync(fs.readFileSync(path.join(__dirname, 'fixtures/mixed', name))).toString('utf8');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

// As JT01 (Tools/AutomationE2E/Driver/Jitter.cs): every frame, the first few blocks with text in the window.
const START = `(()=>{window.__jt={frames:[],on:true};const tick=()=>{const s=window.__jt;if(!s||!s.on)return;const root=document.getElementById('ag-editor-id');const seen=[];if(root){for(const e of root.children){const r=e.getBoundingClientRect();if(r.bottom<=0)continue;if(r.top>=innerHeight)break;if(e.id&&e.textContent.trim())seen.push([e.id,r.top]);if(seen.length>=4)break}}const ed=document.getElementById('editor');s.frames.push([Math.round(performance.now()),window.scrollY,!!(window.__typedownMuya&&window.__typedownMuya.renderPending),seen,root?getComputedStyle(root,'::before').height:'',ed?ed.classList.contains('ag-long-document'):null,root&&root.firstElementChild?root.firstElementChild.id+':'+Math.round(root.firstElementChild.getBoundingClientRect().height*10)/10:'']);requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()`;
const STOP = `(()=>{const s=window.__jt;s.on=false;const f=s.frames;let moves=0,movesDrawing=0,maxDy=0;const list=[];for(let i=1;i<f.length;i++){const a=new Map(f[i-1][3]);for(const [id,top] of f[i][3]){if(!a.has(id))continue;const dy=top-a.get(id);if(Math.abs(dy)>=0.5){moves++;if(f[i][2])movesDrawing++;maxDy=Math.max(maxDy,Math.abs(dy));if(list.length<8)list.push([f[i][0]-f[0][0],id,Math.round(dy*100)/100,'scroll '+(f[i][1]-f[i-1][1]),'room '+f[i-1][4]+'>'+f[i][4],'long '+f[i-1][5]+'>'+f[i][5],'first '+f[i-1][6]+'>'+f[i][6],f[i][2]?'drawing':''])}break}}let flips=0;for(let i=1;i<f.length;i++)if(f[i][5]!==f[i-1][5])flips++;return {flips,frames:f.length,drawingFrames:f.filter(x=>x[2]).length,moves,movesDrawing,maxDy:Math.round(maxDy*100)/100,first:list}})()`;

async function open(browser, port) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: 'x\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];const prev={};window.__last={};window.__msgs=[];
    window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);return;}
      let a=m.args; if(m.type==='diffmsg'){const o=prev[m.name]||'';prev[m.name]=m.diff?o.slice(0,m.start)+m.args+o.slice(m.end):m.args;a=JSON.parse(prev[m.name]);}
      window.__last[m.name]=a; window.__msgs.push({name:m.name,args:a});}}}})();`);
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__last.FileLoaded, { timeout: 60000 });
  await new Promise(r => setTimeout(r, 800));
  return { page, errors };
}

let loadId = 10;
// The host's LoadFile; resolves once loaded and drawn, and `settle` ms more.
async function load(page, args, settle) {
  const id = ++loadId;
  await page.evaluate(a => { window.__msgs = []; window.__deliver('LoadFile', a) }, { basePath: '/tmp', hold: 0, cursor: null, scrollTop: 0, scrollAnchor: null, ...args, loadId: id });
  await page.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args && m.args.loadId === id), { timeout: 120000 }, id);
  await page.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000, polling: 100 });
  await new Promise(r => setTimeout(r, settle));
}
// What the host keeps of a tab being left: the text flushed, the caret and the scroll offset reported.
async function leave(page) {
  await page.evaluate(() => { delete window.__last.ContentFlushed; window.__deliver('FlushContent', { token: 7 }) });
  await page.waitForFunction(() => window.__last.ContentFlushed, { timeout: 60000 });
  return page.evaluate(() => ({ text: window.__last.ContentFlushed.text, cursor: (window.__msgs.filter(m => m.name === 'CursorChange').pop() || { args: {} }).args.cursor || null, scrollTop: Math.round(window.scrollY) }));
}

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'], protocolTimeout: 180000 });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  const big = fixture('mixed-1m.md.gz'), mid = fixture('mixed-300k.md.gz');
  try {
    if (!only || only === 'middle') {
      const { page, errors } = await open(browser, port);
      // Put back at a block half way down, 120 px from the top of the window, as a tab read half way is.
      const blocks = big.split(/\n{2,}/).length;
      await page.evaluate(START);
      await load(page, { text: big, scrollTop: 99999, scrollAnchor: { block: Math.round(blocks / 2), text: '', top: 120 } }, 1500);
      const r = await page.evaluate(STOP);
      console.log(`middle: ${JSON.stringify(r)}`);
      check(r.drawingFrames > 3, `middle: it was drawn a part at a time while watched (${r.drawingFrames} frames)`);
      check(r.moves === 0, `middle: the text in the window does not move (${r.moves} moves of half a pixel or more, ${r.movesDrawing} while drawn, largest ${r.maxDy} px)`);
      check(!errors.length, `middle: no page errors ${errors.join(' | ')}`);
      await page.close();
    }
    if (!only || only === 'kept') {
      const { page, errors } = await open(browser, port);
      await load(page, { text: mid }, 1500);
      const left = await leave(page);
      await load(page, { text: '# Other\n\n' + 'A short document to switch to.\n\n'.repeat(20) }, 800);
      await leave(page);
      if (process.env.JT_CLEAR) await page.evaluate(() => window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear());
      const cursor = process.env.JT_CARET ? { anchor: { line: Number(process.env.JT_CARET), ch: 0 }, focus: { line: Number(process.env.JT_CARET), ch: 0 } } : left.cursor;
      await page.evaluate(START);
      await load(page, { text: left.text, cursor, scrollTop: left.scrollTop }, 2500);
      const r = await page.evaluate(STOP);
      const how = await page.evaluate(() => window.__typedownLastApply);
      console.log(`kept: ${how}, ${JSON.stringify(r)}`);
      check(r.moves === 0, `kept (${how}): the text in the window does not move (${r.moves} moves, largest ${r.maxDy} px)`);
      // Drawn whole when it was left (and below the size that keeps the long-document rule once drawn), it comes back
      // without the rule: put on for a moment, the blocks it skipped stood at its 50 px stand-in (in the real app the
      // first one in the window did) until it went off again.
      check(r.flips === 0, `kept (${how}): the long-document rule is not put on and taken off again (${r.flips} changes)`);
      check(!errors.length, `kept: no page errors ${errors.join(' | ')}`);
      await page.close();
    }
  } finally { await browser.close(); server.close(); }
  console.log(ok ? '\nall passed' : '\nFAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
