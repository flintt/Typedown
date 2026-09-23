const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const markdown = '# Title\n\nSome **bold** text to select and copy here.\n';
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage();
  page.on('pageerror', e => console.log('PAGEERROR:', e.message.split('\n')[0]));
  page.on('console', m => { const t = m.text(); if (/error|Error|warn/.test(t)) console.log('CONSOLE:', t.slice(0, 200)); });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', readOnly: true, markdown, basePath: '/tmp' };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};const deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;if(m.type==='invoke'){setTimeout(()=>deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 30000 });
  await new Promise(r => setTimeout(r, 500));
  await page.evaluate(() => {
    window.__log = [];
    for (const t of ['mousedown', 'mouseup', 'click']) document.addEventListener(t, e => window.__log.push(`${t} prevented=${e.defaultPrevented} target=${e.target.nodeName}.${e.target.className}`), true);
    for (const t of ['mousedown', 'mouseup', 'click']) document.addEventListener(t, e => window.__log.push(`bubble-end ${t} prevented=${e.defaultPrevented}`), false);
    document.addEventListener('selectionchange', () => window.__log.push(`selectionchange "${window.getSelection().toString().slice(0, 20)}"`));
    const cs = window.__typedownMuya.contentState;
    for (const name of ['render', 'partialRender', 'singleRender', 'setCursor', 'selectionChange']) {
      const fn = cs[name].bind(cs);
      cs[name] = (...args) => { window.__log.push(`${name}(${JSON.stringify(args)}) cursor=${JSON.stringify(cs.cursor)}`); return fn(...args); };
    }
    const blur = window.__typedownMuya.blur.bind(window.__typedownMuya);
    window.__typedownMuya.blur = (...a) => { window.__log.push('muya.blur()'); return blur(...a); };
    const mo = new MutationObserver(ms => window.__log.push(`mutations=${ms.length} first=${ms[0].type}`));
    mo.observe(document.querySelector('#ag-editor-id'), { childList: true, subtree: true, characterData: true, attributes: true });
  });
  const box = await (await page.$('p.ag-paragraph')).boundingBox();
  await page.mouse.move(box.x + 4, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width - 10, box.y + box.height / 2, { steps: 10 });
  await page.mouse.up();
  await new Promise(r => setTimeout(r, 400));
  const out = await page.evaluate(() => ({ log: window.__log, sel: window.getSelection().toString() }));
  console.log(out.log.join('\n'));
  console.log('FINAL selection:', JSON.stringify(out.sel));
  await browser.close(); server.close();
})().catch(e => { console.error(e); process.exit(1); });
