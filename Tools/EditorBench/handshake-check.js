// Verifies the LoadFile -> FileLoaded handshake: FileLoaded must report the original source and the load's id,
// nothing for a superseded load may leak through, and no MarkdownChange precedes FileLoaded. The text goes back to the
// host only when the editor holds other text than it was given; otherwise FileLoaded says `same` and leaves it out
// (sending a long document back cost the host 20-40 ms per load).
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage();
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '1200px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '* one\n* two', basePath: 'C:\\tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  const waitFor = (pred, t = 10000) => page.waitForFunction(pred, { timeout: t });
  const msgs = () => page.evaluate(() => window.__msgs.filter(m => ['FileLoaded', 'MarkdownChange', 'CursorChange'].includes(m.name)).map(m => ({ name: m.name, loadId: m.args.loadId, text: m.args.text, same: m.args.same })));
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };

  await waitFor(() => window.__msgs.some(m => m.name === 'FileLoaded'));
  let list = await msgs();
  const fl = list.find(m => m.name === 'FileLoaded');
  check(fl.loadId === 1, 'initial FileLoaded echoes loadId=1');
  check(fl.same === true && fl.text === undefined, `initial FileLoaded says the source is kept, without the text (${JSON.stringify([fl.same, fl.text])})`);
  check(!list.some(m => m.name === 'MarkdownChange' && list.indexOf(m) < list.indexOf(fl)), 'no MarkdownChange before initial FileLoaded');

  // Load a document that Muya rewrites on import (setext heading, '*' bullets, trailing spaces).
  await page.evaluate(() => { window.__msgs.length = 0; window.__deliver('LoadFile', { text: 'Title\n=====\n\n* a   \n* b\n', basePath: 'C:\\tmp', loadId: 2 }); });
  await waitFor(() => window.__msgs.some(m => m.name === 'FileLoaded'));
  list = await msgs();
  const fl2 = list.find(m => m.name === 'FileLoaded');
  check(fl2.loadId === 2, 'LoadFile FileLoaded echoes loadId=2');
  check(fl2.same === true && fl2.text === undefined, `FileLoaded: source kept despite internal normalization, no text sent (${JSON.stringify([fl2.same, fl2.text])})`);
  check(!list.some(m => m.loadId !== 2), 'nothing tagged with a stale loadId after LoadFile');
  check(!list.some(m => m.name === 'MarkdownChange'), 'no MarkdownChange emitted for a load (host takes the text from FileLoaded)');

  // Two loads back to back: only the last may complete the handshake.
  await page.evaluate(() => { window.__msgs.length = 0; window.__deliver('LoadFile', { text: '# A\n\ntext a\n', basePath: 'C:\\tmp', loadId: 3 }); window.__deliver('LoadFile', { text: '# B\n\ntext b\n', basePath: 'C:\\tmp', loadId: 4 }); });
  await waitFor(() => window.__msgs.some(m => m.name === 'FileLoaded'));
  await new Promise(r => setTimeout(r, 1200));
  list = await msgs();
  const fls = list.filter(m => m.name === 'FileLoaded');
  check(fls.length === 1 && fls[0].loadId === 4 && fls[0].same === true, `rapid LoadFile x2 -> one FileLoaded for loadId=4, its text kept (${JSON.stringify(fls.map(m => [m.loadId, m.same]))})`);
  check(await page.evaluate(() => document.querySelector('#ag-editor-id').textContent.includes('text b')), 'the page shows the second load');
  check(!list.some(m => m.loadId === 3), 'no report leaked for the superseded loadId=3');

  // A real edit after the load reports a MarkdownChange with the current loadId.
  await page.evaluate(() => { window.__msgs.length = 0; const cs = window.__typedownMuya.contentState; const find = (bs) => { for (const b of bs) { if (typeof b.text === 'string' && b.text.includes('text b')) return b; const r = find(b.children || []); if (r) return r; } }; find(cs.blocks).text = 'text B edited'; cs.render(); window.__typedownMuya.dispatchChange(); });
  await waitFor(() => window.__msgs.some(m => m.name === 'MarkdownChange'));
  list = await msgs();
  const mc = list.find(m => m.name === 'MarkdownChange');
  check(mc.loadId === 4 && mc.text.includes('text B edited'), 'edit after load -> MarkdownChange tagged loadId=4');

  // Undo/redo from the host's history (SetMarkdown) with a new loadId: what the page reports afterwards carries it, so
  // the host can tell a report made before the undo from one made after.
  await page.evaluate(() => { window.__msgs.length = 0; window.__deliver('SetMarkdown', { text: '# B\n\nundone text\n', basePath: 'C:\\tmp', loadId: 5 }); });
  await new Promise(r => setTimeout(r, 800));
  await page.evaluate(() => { window.__msgs.length = 0; const cs = window.__typedownMuya.contentState; const find = (bs) => { for (const b of bs) { if (typeof b.text === 'string' && b.text.includes('undone')) return b; const r = find(b.children || []); if (r) return r; } }; find(cs.blocks).text = 'undone text, typed'; cs.render(); window.__typedownMuya.dispatchChange(); });
  await waitFor(() => window.__msgs.some(m => m.name === 'MarkdownChange'));
  list = await msgs();
  const afterUndo = list.find(m => m.name === 'MarkdownChange');
  check(afterUndo.loadId === 5 && afterUndo.text.includes('undone text, typed'), `a report after SetMarkdown with loadId=5 carries it (${afterUndo.loadId})`);

  // The race seen in the Uno edition: redo arrives while the editor still holds the undone text and reports it
  // (Muya's late change event). That report must not reach the host as a change under the redo's loadId.
  await page.evaluate(() => {
    window.__msgs.length = 0;
    window.__deliver('SetMarkdown', { text: '# B\n\nredone text\n', basePath: 'C:\\tmp', loadId: 6 });
    window.__typedownMuya.dispatchChange(); // same tick: the editor still holds the previous text
  });
  await new Promise(r => setTimeout(r, 800));
  list = await msgs();
  const stale = list.filter(m => m.name === 'MarkdownChange' && !m.text.includes('redone'));
  check(stale.length === 0, `no report of the previous text after SetMarkdown (${JSON.stringify(stale.map(m => [m.loadId, m.text.slice(0, 20)]))})`);
  check(!list.some(m => m.name === 'MarkdownChange' && m.loadId === 6 && !m.text.includes('redone')), 'nothing under loadId=6 describes the previous text');

  // A load the editor gives back changed: FileLoaded carries the text, and no `same`, so the host can see the rewrite
  // (it loads once more, then takes it). The rewrite is made where Muya would make one: in its change reports about
  // the new document, which therefore say other text than was given.
  await page.evaluate(() => {
    window.__msgs.length = 0;
    const center = window.__typedownMuya.eventCenter;
    const dispatch = center.dispatch;
    window.__undoRewrite = () => { center.dispatch = dispatch; };
    center.dispatch = function (name, arg, ...rest) {
      if (name === 'contentChange' && arg?.markdown?.includes('text c')) arg = { ...arg, markdown: arg.markdown.replace('text c', 'text c, rewritten') };
      return dispatch.call(this, name, arg, ...rest);
    };
    window.__deliver('LoadFile', { text: '# C\n\ntext c\n', basePath: 'C:\\tmp', loadId: 7 });
  });
  await waitFor(() => window.__msgs.some(m => m.name === 'FileLoaded'));
  await page.evaluate(() => window.__undoRewrite());
  list = await msgs();
  const fl7 = list.find(m => m.name === 'FileLoaded');
  check(fl7.loadId === 7 && fl7.same === undefined && typeof fl7.text === 'string' && fl7.text.includes('text c, rewritten'),
    `a rewritten load sends the text back (${JSON.stringify([fl7.loadId, fl7.same, fl7.text])})`);

  await browser.close(); server.close();
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
