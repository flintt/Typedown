const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const markdown = '# Title\n\nSome **bold** text to select and copy here.\n\n| a | b |\n|---|---|\n| 1 | 2 |\n';
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage();
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', readOnly: process.env.RO !== '0', markdown, basePath: '/tmp' };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__msgs=[];const deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));window.__deliver=deliver;const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 30000 });
  await new Promise(r => setTimeout(r, 500));
  // drag-select across the paragraph
  const box = await (await page.$('p.ag-paragraph')).boundingBox();
  await page.mouse.move(box.x + 4, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width - 10, box.y + box.height / 2, { steps: 12 });
  await page.mouse.up();
  await new Promise(r => setTimeout(r, 300));
  const sel = await page.evaluate(() => window.getSelection().toString());
  const css = await page.evaluate(() => {
    const e = document.querySelector('p.ag-paragraph');
    const m = window.__typedownMuya;
    return {
      pSelect: getComputedStyle(e).userSelect, bodySelect: getComputedStyle(document.body).userSelect,
      containerSelect: getComputedStyle(m.container).userSelect,
      containerEditable: m.container.getAttribute('contenteditable'),
      rangeSelect: (() => { const r = document.createRange(); r.selectNodeContents(e); const s = window.getSelection(); s.removeAllRanges(); s.addRange(r); return s.toString().slice(0, 30); })(),
    };
  });
  // the host's Copy command path (Ctrl+C is handled by the shell, which sends Copy back)
  const copied = await page.evaluate(async () => {
    let text = null;
    const orig = document.execCommand.bind(document);
    document.execCommand = (cmd, ...rest) => { if (cmd === 'copy') { text = window.getSelection().toString(); return true; } return orig(cmd, ...rest); };
    window.__deliver('Copy', 'copyAsMarkdown');
    await new Promise(r => setTimeout(r, 300));
    return text;
  });
  console.log('selection:', JSON.stringify(sel));
  console.log('css/editable:', JSON.stringify(css));
  console.log('Copy command produced:', JSON.stringify(copied));
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
