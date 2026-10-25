// A key typed the moment a new document is in the page, before its load is reported (its caret is placed and FileLoaded
// sent a frame later, or later still on a long document): FileLoaded must still report the document as loaded (`same`,
// or the text as given), and the key must reach the host as the reader's own edit afterwards. When the edit was the
// first report after the load it was taken for the load's text: the host saw the editor "rewrite" the document and
// loaded it again over what had just been typed or pasted (E2E WP01 and FO01, in a full Typeleaf run).
//
//   node edit-during-load-check.js [--rounds 6]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const args = process.argv.slice(2);
const rounds = Number(args[args.indexOf('--rounds') + 1]) || 6;
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const para = i => `Paragraph ${i} of the document, with a few words to make it long enough to take a moment.\n\n`;
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 800));
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  let loadId = 10;
  for (let round = 0; round < rounds; round++) {
    // Short and long documents: a long one's caret and report come later, the gap the key falls into is wider.
    const long = round % 2 === 1;
    const marker = `Doc${round}`;
    const text = `# ${marker}\n\n` + (long ? Array.from({ length: 1500 }, (_, i) => para(i)).join('') : 'Body text.\n');
    loadId++;
    await page.evaluate(() => { window.__msgs = [] });
    await page.evaluate((text, loadId) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId, hold: 0 }), text, loadId);
    // The moment the new document is in the page, a key - before its caret and its report.
    await page.waitForFunction(m => ((document.querySelector('#ag-editor-id') || {}).textContent || '').includes(m), { polling: 'mutation', timeout: 30000 }, marker);
    await page.keyboard.type('Z');
    await page.waitForFunction(id => window.__msgs.some(m => m.name === 'MarkdownChange' && m.args.loadId === id && (m.args.text || '').includes('Z')), { timeout: 10000 }, loadId).catch(() => {});
    await new Promise(r => setTimeout(r, 700));
    const list = await page.evaluate(() => window.__msgs.filter(m => ['FileLoaded', 'MarkdownChange'].includes(m.name)).map(m => ({ name: m.name, loadId: m.args.loadId, same: m.args.same, text: m.args.text, origin: m.args.origin })));
    const fl = list.find(m => m.name === 'FileLoaded' && m.loadId === loadId);
    const flText = fl ? (fl.same ? text : fl.text) : null;
    const typed = list.find(m => m.name === 'MarkdownChange' && m.loadId === loadId && (m.text || '').includes('Z'));
    const label = `${long ? 'long' : 'short'} document ${round}`;
    check(!!fl, `${label}: FileLoaded for the load`);
    check(fl && !(flText || '').includes('Z'), `${label}: FileLoaded reports the document as loaded, not with the typed key (${fl ? (fl.same ? 'same' : JSON.stringify((fl.text || '').slice(0, 40))) : 'none'})`);
    check(!!typed && (!fl || list.indexOf(typed) > list.indexOf(fl)), `${label}: the key reaches the host as an edit after FileLoaded`);
    check(!!typed && typed.origin === 'user', `${label}: the key is the reader's edit (origin ${typed && typed.origin})`);
  }
  console.log(ok ? 'OK: a key typed during a load is an edit after it, never part of it' : 'FAILED');
  await browser.close(); server.close(); process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2) });
