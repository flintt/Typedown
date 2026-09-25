// The outline highlight follows the caret, and reading mode has no caret. This scrolls a read-only document
// and checks that the editor reports a current heading that moves with the page.
//
//   node outline-follow-check.js [--chars 120000]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const args = process.argv.slice(2);
const at = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? Number(args[i + 1]) : fallback; };
const chars = at('--chars', 120000);
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

const section = (i) => [`## Section ${i}`, `Paragraph ${i} with text long enough to wrap onto a second line in the editor.`, '- one\n- two'].join('\n\n');
let markdown = '', n = 0;
while (markdown.length < chars) markdown += section(++n) + '\n\n';

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, readOnly: true, markdown, basePath: '/tmp' };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__cur=[];window.__prev={};window.__apply=(m)=>{if(typeof m.args!=='string')return null;const o=window.__prev[m.name];const v=m.diff?o.slice(0,m.start)+m.args+o.slice(m.end):m.args;window.__prev[m.name]=v;try{return JSON.parse(v)}catch(e){return null}};window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;if(m.name==='StateChange'){const a=window.__apply(m);window.__cur.push(a&&a.state&&a.state.cur?a.state.cur.content:null);}if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 120000 });
  await new Promise(r => setTimeout(r, 1200));

  const seen = [];
  for (const y of [0, 4000, 12000, 30000, 0]) {
    const cur = await page.evaluate(async (y) => {
      window.scrollTo(0, y);
      for (let i = 0; i < 20; i++) await new Promise(r => requestAnimationFrame(r));
      return window.__cur[window.__cur.length - 1];
    }, y);
    seen.push([y, cur]);
    console.log(`  scrollY ${String(y).padStart(6)}  ->  ${cur ?? 'nothing'}`);
  }
  const values = seen.map(s => s[1]);
  const moved = new Set(values.filter(Boolean)).size > 1;
  const backAtTop = values[values.length - 1] === values[0];
  console.log(moved && backAtTop ? 'OK: the current heading follows the page and comes back' : 'FAIL: the current heading does not follow the page');
  await browser.close(); server.close();
  process.exit(moved && backAtTop ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
