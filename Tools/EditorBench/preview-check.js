// A long document's first screen is painted before the editor builds it (services/loadPreview.ts), at start-up (the
// document in the settings) and on a load (LoadFile): a frame is drawn with the preview's text on it while the editor
// still has the old document or none, the host hears LoadPreviewShown for that load, and the preview goes once the
// load is reported (FileLoaded), the editor then holding the text. A short document gets no preview, nor does a long
// one switched back to that the page kept (Muya's keptDocuments): it comes back at once.
//
//   node preview-check.js
//   BREAK=1 node preview-check.js   - the preview taken away the moment it appears: the check must fail
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const para = (tag, i) => `Paragraph ${i} of ${tag}, with a few words to make it long enough to take a while to draw.\n\n`;
const long = tag => `# ${tag}\n\n` + Array.from({ length: 3000 }, (_, i) => (i % 1000 === 0 ? `## Part ${i / 1000}\n\n` : '') + para(tag, i)).join('');

// Each frame the page draws: whether the preview is up with the given marker, and whether the editor holds it.
const breakPreview = process.env.BREAK ? `new MutationObserver(()=>document.getElementById('td-load-preview')?.remove()).observe(document,{childList:true,subtree:true});` : '';
const watchFrames = `(()=>{window.__frames=[];const tick=()=>{const p=document.getElementById('td-load-preview');const e=document.getElementById('ag-editor-id');const a=p&&p.querySelector('[data-anchor]');window.__frames.push({t:performance.now(),preview:p?p.textContent.slice(0,200):null,editor:e?e.textContent.slice(0,200):null,anchorTop:a?a.getBoundingClientRect().top:null});requestAnimationFrame(tick)};requestAnimationFrame(tick)})()`;
// Where the editor's block starting with this text is in the window now (its top), or null.
const blockTop = text => `(()=>{const k=${JSON.stringify(text.slice(0, 30))};for(const b of document.querySelectorAll('#ag-editor-id .ag-paragraph')){if((b.textContent||'').replace(/\\s+/g,' ').trim().startsWith(k))return b.getBoundingClientRect().top}return null})()`;

async function page(browser, port, markdown, extra = {}) {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown, basePath: '/tmp', loadId: 1, cursor: null, ...extra };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Dark',accentColor:{r:0,g:120,b:212,a:1},background:{R:40,G:40,B:40,A:255}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}};${watchFrames};${breakPreview}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  return page;
}

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };

  // What the frames and messages say about one load whose text starts with this marker.
  const judge = async (p, marker, loadId, label, expectPreview) => {
    await p.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === id), { timeout: 60000 }, loadId);
    await new Promise(r => setTimeout(r, 300));
    const { frames, msgs, previewNow } = await p.evaluate(() => ({ frames: window.__frames, msgs: window.__msgs.map(m => ({ name: m.name, args: m.args, at: m.at })), previewNow: !!document.getElementById('td-load-preview') }));
    const loaded = msgs.find(m => m.name === 'FileLoaded' && m.args.loadId === loadId);
    const shownMsg = msgs.find(m => m.name === 'LoadPreviewShown' && m.args.loadId === loadId);
    // A frame painted with the preview of this document while the editor did not have it yet.
    const early = frames.find(f => f.preview && f.preview.includes(marker) && !(f.editor || '').includes(marker));
    const editorAt = frames.find(f => (f.editor || '').includes(marker));
    if (expectPreview) {
      check(!!early, `${label}: a frame shows the preview before the editor has the document (preview at ${early && Math.round(early.t)} ms, editor at ${editorAt && Math.round(editorAt.t)} ms)`);
      check(!!shownMsg, `${label}: the host hears LoadPreviewShown for load ${loadId}`);
    } else {
      check(!early && !shownMsg, `${label}: no preview`);
    }
    check(!previewNow, `${label}: the preview is gone once FileLoaded is sent (${loaded && Math.round(loaded.at)} ms)`);
    check(!!editorAt, `${label}: the editor shows the document`);
  };

  // Start-up with a long document.
  {
    const p = await page(browser, port, long('StartDoc'));
    await judge(p, 'StartDoc', 1, 'start-up, long', true);
    // A load of another long document, then a short one, then back to the first long one (kept: no preview).
    let id = 1;
    for (const [tag, text, expect] of [['LoadA', long('LoadA'), true], ['Short', '# Short\n\nA short document.\n', false], ['StartDoc', null, false]]) {
      id++;
      await p.evaluate(() => { window.__frames = []; window.__msgs = [] });
      const body = text ?? long('StartDoc');
      await p.evaluate((text, loadId) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId, hold: 0 }), body, id);
      await judge(p, tag, id, `load ${tag}${text ? '' : ' (switched back: kept)'}`, expect);
      if (tag === 'StartDoc') {
        const kept = await p.evaluate(() => window.__typedownLastApply);
        console.log(`  (the switch back was applied as: ${kept})`);
      }
    }
    // The caret's block: a load with the caret far down previews from there.
    id++;
    await p.evaluate(() => { window.__frames = []; window.__msgs = [] });
    await p.evaluate((text, loadId) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: { anchor: { line: 3000, ch: 0 }, focus: { line: 3000, ch: 0 } }, scrollTop: 0, loadId, hold: 0 }), long('Deep'), id);
    await p.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === id), { timeout: 60000 }, id);
    const deep = await p.evaluate(() => window.__frames.find(f => f.preview)?.preview || '');
    check(/Paragraph 1[45]\d\d of Deep/.test(deep) && !deep.includes('Paragraph 0 of Deep'), `a caret far down previews from its block (${JSON.stringify(deep.slice(0, 60))})`);
    await p.close();
  }
  // Where the page was, put back without a jump: a long document scrolled half way, left, and loaded again with what
  // the page reported (the host keeps the anchor with the offset) - the preview shows the block at the top of the
  // window at the height it was, and the editor then puts the same block at the same height.
  let anchored = null;
  {
    const p = await page(browser, port, '# start\n');
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
    const doc = long('Anch');
    await p.evaluate((text) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 2, hold: 0 }), doc);
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 2), { timeout: 60000 });
    await new Promise(r => setTimeout(r, 500));
    await p.evaluate(() => window.scrollTo(0, 61234));
    await new Promise(r => setTimeout(r, 500));
    const report = await p.evaluate(() => [...window.__msgs].reverse().find(m => m.name === 'OnScroll' && m.args.anchor)?.args);
    check(!!report?.anchor, `the scroll report carries the block at the top (${JSON.stringify(report?.anchor)})`);
    const before = report?.anchor ? await p.evaluate(blockTop(report.anchor.text)) : null;
    await p.evaluate((text) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: 0, loadId: 3, hold: 0 }), long('Other'));
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 3), { timeout: 60000 });
    // The same text with a line more: not the document kept, built anew.
    const again = doc + '\nThe end.\n';
    await p.evaluate(() => { window.__frames = []; window.__msgs = [] });
    await p.evaluate((text, scrollTop, scrollAnchor) => window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop, scrollAnchor, loadId: 4, hold: 0 }), again, report.scrollY, report.anchor);
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === 4), { timeout: 60000 });
    await new Promise(r => setTimeout(r, 1500));
    const previewTop = await p.evaluate(() => window.__frames.find(f => f.anchorTop != null)?.anchorTop);
    const after = await p.evaluate(blockTop(report.anchor.text));
    console.log(`  anchor ${report.anchor.top}, the block before leaving ${before}, in the preview ${previewTop}, in the editor after ${after}`);
    check(previewTop != null && Math.abs(previewTop - report.anchor.top) <= 2, `load: the preview shows the block where it was (${previewTop} vs ${report.anchor.top})`);
    check(after != null && Math.abs(after - report.anchor.top) <= 2, `load: the editor puts the same block there - no jump from the preview (${after} vs ${report.anchor.top})`);
    anchored = { text: again, scrollTop: report.scrollY, anchor: report.anchor };
    await p.close();
  }
  // The same at start-up: the document and where it was come with the settings.
  if (anchored) {
    const p = await page(browser, port, anchored.text, { scrollTop: anchored.scrollTop, scrollAnchor: anchored.anchor });
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
    await new Promise(r => setTimeout(r, 1500));
    const previewTop = await p.evaluate(() => window.__frames.find(f => f.anchorTop != null)?.anchorTop);
    const after = await p.evaluate(blockTop(anchored.anchor.text));
    console.log(`  start-up: anchor ${anchored.anchor.top}, in the preview ${previewTop}, in the editor after ${after}`);
    check(previewTop != null && Math.abs(previewTop - anchored.anchor.top) <= 2, `start-up: the preview shows the block where it was (${previewTop} vs ${anchored.anchor.top})`);
    check(after != null && Math.abs(after - anchored.anchor.top) <= 2, `start-up: the editor puts the same block there (${after} vs ${anchored.anchor.top})`);
    await p.close();
  }
  // Start-up with a short document: no preview.
  {
    const p = await page(browser, port, '# Small\n\nA short document.\n');
    await judge(p, 'Small', 1, 'start-up, short', false);
    await p.close();
  }
  console.log(ok ? 'OK: a long document is previewed before it is built, and the preview goes once it is loaded' : 'FAILED');
  await browser.close(); server.close(); process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2) });
