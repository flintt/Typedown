// How long a tab switch takes. The host keeps one editor and re-loads the document on every switch, so
// switching between a huge file and a small one pays a full load each way. This measures both directions.
//
//   node tab-switch-check.js [--big 300000] [--small 20000] [--rounds 3]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const args = process.argv.slice(2);
const at = (flag, fallback) => { const i = args.indexOf(flag); return i >= 0 ? Number(args[i + 1]) : fallback; };
const bigChars = at('--big', 300000), smallChars = at('--small', 20000), rounds = at('--rounds', 3);
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

const section = (i) => [`## Section ${i}`, `Paragraph ${i} with text long enough to wrap onto a second line in the editor.`, '- one\n- two', '```js\nconst a = ' + i + ';\n```', '| a | b |\n|---|---|\n| 1 | 2 |'].join('\n\n');
const doc = (chars) => { let m = '', n = 0; while (m.length < chars) m += section(++n) + '\n\n'; return m; };
const big = doc(bigChars), small = doc(smallChars);

const median = (a) => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: big, basePath: '/tmp' };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__loaded=0;window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;if(m.name==='FileLoaded')window.__loaded++;if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 180000 });
  await new Promise(r => setTimeout(r, 1500));

  // One switch: deliver LoadFile and wait until the editor has the new text laid out (one animation frame
  // after the DOM shows the expected number of top-level blocks).
  const switchTo = (text, blocks) => page.evaluate(async ([text, blocks]) => {
    const start = performance.now();
    window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: Date.now() });
    const root = () => document.querySelector('#ag-editor-id');
    for (let i = 0; i < 2000; i++) {
      await new Promise(r => requestAnimationFrame(r));
      if (root() && root().children.length === blocks) break;
    }
    await new Promise(r => requestAnimationFrame(r));
    return Math.round(performance.now() - start);
  }, [text, blocks]);

  const count = async () => page.evaluate(() => document.querySelector('#ag-editor-id').children.length);
  const bigBlocks = await count();
  await switchTo(small, 0).catch(() => 0); // prime; block count unknown yet
  const smallBlocks = await count();

  const toSmall = [], toBig = [];
  for (let r = 0; r < rounds; r++) {
    toBig.push(await switchTo(big, bigBlocks));
    toSmall.push(await switchTo(small, smallBlocks));
  }
  const elements = await page.evaluate(() => document.querySelectorAll('#ag-editor-id *').length);
  console.log(`big ${big.length} chars / ${bigBlocks} blocks, small ${small.length} chars / ${smallBlocks} blocks`);
  console.log(`  switch to the big one     ${median(toBig)} ms   ${toBig.join(', ')}`);
  console.log(`  switch back to the small  ${median(toSmall)} ms   ${toSmall.join(', ')}`);
  console.log(`  elements now              ${elements}`);
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
