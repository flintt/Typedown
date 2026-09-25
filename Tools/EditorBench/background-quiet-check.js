// A document kept in memory must not answer events meant for the one on screen. Each editor binds handlers
// to `document` — keyboard, tooltip, the floats — and a kept one still holds whatever was selected in it
// when it was left, so a Backspace could delete an image in a document nobody is looking at.
//
//   node background-quiet-check.js
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

const docA = '# A\n\nfirst document\n\n![](https://example.invalid/a.png)\n';
const docB = '# B\n\nsecond document\n';

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: docA, basePath: '/tmp' };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__tooltips=0;window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;if(m.name==='OpenToolTip')window.__tooltips++;if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 60000 });
  await new Promise(r => setTimeout(r, 800));

  // Watch the document A editor, then leave it for another document.
  await page.evaluate(() => {
    const a = window.__typedownMuya
    window.__a = a
    window.__calls = { backspace: 0, enter: 0 }
    const cs = a.contentState
    const back = cs.docBackspaceHandler.bind(cs), enter = cs.docEnterHandler.bind(cs)
    cs.docBackspaceHandler = (e) => { window.__calls.backspace++; return back(e) }
    cs.docEnterHandler = (e) => { window.__calls.enter++; return enter(e) }
  });
  await page.evaluate(async (text) => {
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 2 });
    for (let i = 0; i < 120; i++) await new Promise(r => requestAnimationFrame(r));
  }, docB);

  const state = await page.evaluate(async () => {
    const swapped = window.__typedownMuya !== window.__a
    const detached = !window.__a.container.isConnected
    const before = window.__tooltips
    for (const code of ['Backspace', 'Enter']) document.dispatchEvent(new KeyboardEvent('keydown', { code, bubbles: true }))
    document.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }))
    await new Promise(r => setTimeout(r, 200))
    return { swapped, detached, calls: window.__calls, tooltipsAdded: window.__tooltips - before }
  });

  // The same events, once that document is the one on screen again: without this the check could pass by
  // dispatching events that reach nothing at all, and would say "quiet" however the guard was broken.
  await page.evaluate(async (text) => {
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 3 });
    for (let i = 0; i < 120; i++) await new Promise(r => requestAnimationFrame(r));
  }, docA);
  const back = await page.evaluate(async () => {
    const showing = window.__typedownMuya === window.__a && window.__a.container.isConnected
    for (const code of ['Backspace', 'Enter']) document.dispatchEvent(new KeyboardEvent('keydown', { code, bubbles: true }))
    await new Promise(r => setTimeout(r, 200))
    return { showing, calls: { ...window.__calls } }
  });

  console.log(`  another editor is showing   ${state.swapped}`);
  console.log(`  the first one is detached   ${state.detached}`);
  console.log(`  its document key handlers   backspace ${state.calls.backspace}, enter ${state.calls.enter}`);
  console.log(`  tooltips it reported        ${state.tooltipsAdded}`);
  console.log(`  shown again, same editor    ${back.showing}`);
  console.log(`  now its handlers run        backspace ${back.calls.backspace}, enter ${back.calls.enter}`);
  const quiet = state.swapped && state.detached && state.calls.backspace === 0 && state.calls.enter === 0 && state.tooltipsAdded === 0;
  const live = back.showing && back.calls.backspace > 0 && back.calls.enter > 0;
  if (!live) console.log('the events reached nothing even with the document on screen — the check proves nothing');
  const ok = quiet && live;
  console.log(ok ? 'OK: the document kept in memory stayed out of it' : 'FAIL: a document nobody is looking at answered the event');
  await browser.close(); server.close();
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
