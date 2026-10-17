// The outline in the state report (StateChange) goes only when it changed. A document with three thousand headings
// made every report some 176 KB with the whole outline in it, and the host read it all and rebuilt its outline each
// time; now a report whose outline is the one this load already sent leaves it out and says `tocUnchanged`. What
// has to hold: the first report of every load carries the outline (the host keeps one per load, and a restored
// document with the very same headings is a new load), a keystroke in a paragraph does not resend it, a keystroke in
// a heading does, and the outline put together the host's way (keep the held one when told it is unchanged) is the
// document's at every step.
//
//   node toc-report-check.js
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

const doc = ['# Alpha', 'the first paragraph', '## Alpha one', 'the second paragraph', '## Alpha two', 'the third paragraph'].join('\n\n') + '\n';

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: 'x\n', basePath: '/tmp', loadId: 1 };
  // Each report as the page sent it (outline in it or not) and the outline the host holds after it.
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__prev={};window.__reports=[];window.__held=null;
    window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
    const apply=(m)=>{if(typeof m.args!=='string')return null;const o=window.__prev[m.name];const v=m.diff?o.slice(0,m.start)+m.args+o.slice(m.end):m.args;window.__prev[m.name]=v;try{return JSON.parse(v)}catch(e){return null}};
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;
      if(m.name==='StateChange'){const a=apply(m);if(a&&a.state){const carried=Array.isArray(a.state.toc);
        if(carried)window.__held={loadId:a.loadId,toc:a.state.toc.map(t=>t.content)};
        else if(!a.tocUnchanged||!window.__held||window.__held.loadId!==a.loadId)window.__held={loadId:a.loadId,toc:null,broken:true};
        window.__reports.push({loadId:a.loadId,carried,unchanged:!!a.tocUnchanged,bytes:raw.length});}}
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 60000 });
  await new Promise(r => setTimeout(r, 800));

  const frames = (n) => page.evaluate(async (n) => { for (let i = 0; i < n; i++) await new Promise(r => requestAnimationFrame(r)); }, n);
  const take = () => page.evaluate(() => ({ reports: window.__reports.splice(0), held: window.__held }));
  const load = async (loadId) => {
    await page.evaluate((text, loadId) => { window.__reports = []; window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId }); }, doc, loadId);
    await frames(120); await new Promise(r => setTimeout(r, 300));
    return take();
  };
  const typeAt = async (selector, keys) => {
    const box = await page.evaluate((sel) => { const e = document.querySelector(sel); const r = e.getBoundingClientRect(); return { x: r.right - 2, y: r.top + r.height / 2 }; }, selector);
    await page.mouse.click(box.x, box.y); await new Promise(r => setTimeout(r, 300)); await take();
    for (const k of keys) { await page.keyboard.type(k); await new Promise(r => setTimeout(r, 250)); }
    await new Promise(r => setTimeout(r, 600));
    return take();
  };

  const fails = [];
  const expect = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) fails.push(what); };
  const outline = (held) => held && held.toc ? held.toc.join('|') : '(none)';

  const first = await load(10);
  expect(first.reports.length > 0 && first.reports[0].carried && first.reports[0].loadId === 10, `first report of load 10 carries the outline (${first.reports.length} report(s))`);
  expect(outline(first.held) === 'Alpha|Alpha one|Alpha two', `outline held after the load: ${outline(first.held)}`);

  const body = await typeAt('#ag-editor-id p.ag-paragraph', 'xyz');
  expect(body.reports.length > 0, `keys in a paragraph are reported (${body.reports.length} report(s))`);
  expect(body.reports.every(r => !r.carried && r.unchanged), `none of them resends the outline (${body.reports.map(r => r.carried ? 'toc' : 'kept').join(' ')}, ${body.reports.map(r => r.bytes).join('/')} bytes)`);
  expect(outline(body.held) === 'Alpha|Alpha one|Alpha two', `outline held: ${outline(body.held)}`);

  const head = await typeAt('#ag-editor-id h2', 'Q');
  expect(head.reports.some(r => r.carried && !r.unchanged), `a key in a heading sends the changed outline (${head.reports.map(r => r.carried ? 'toc' : 'kept').join(' ')})`);
  expect(outline(head.held) === 'Alpha|Alpha oneQ|Alpha two', `outline held: ${outline(head.held)}`);

  const again = await typeAt('#ag-editor-id p.ag-paragraph', 'w');
  expect(again.reports.length > 0 && again.reports.every(r => !r.carried && r.unchanged), `and the next paragraph key does not (${again.reports.map(r => r.carried ? 'toc' : 'kept').join(' ')})`);
  expect(outline(again.held) === 'Alpha|Alpha oneQ|Alpha two', `outline held: ${outline(again.held)}`);

  // The same text again under a new load (a tab switched back to): the very same headings, sent all the same.
  const reload = await load(11);
  expect(reload.reports.length > 0 && reload.reports[0].carried && reload.reports[0].loadId === 11, `first report of load 11 (same headings) carries the outline (${reload.reports.map(r => r.carried ? 'toc' : 'kept').join(' ')})`);
  expect(outline(reload.held) === 'Alpha|Alpha one|Alpha two', `outline held: ${outline(reload.held)}`);

  console.log(fails.length ? `FAIL: ${fails.length} step(s) wrong` : 'OK: the outline goes with the first report of a load and when it changes, not otherwise');
  await browser.close(); server.close();
  process.exit(fails.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
