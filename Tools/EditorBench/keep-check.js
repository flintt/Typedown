// Switching back to a long document the page kept (components/Muya/index.tsx, keepShown/takeKept). The host switches
// tabs as it does in the app: FlushContent, then LoadFile with the tab's text, the caret it last heard of and its
// scroll offset. A switch back to a long document left untouched must show the very elements it left (they carry a
// mark set before leaving), drawn exactly as a new build of that text and caret draws them, with the same reports
// (FileLoaded, StateChange with the outline, CursorChange, each with the load's id) and the same scroll position. After
// a settings change, a different text, an edit or a reading-mode switch it must be built again. At most two are kept.
// Prints the memory one kept document holds.
//
//   node keep-check.js
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

// Long documents with headings, emphasis, links, lists, quotes, code, tables and maths, past the length kept (50,000).
const section = (tag, i) => [`## ${tag} section ${i}`, `Paragraph ${i} of ${tag} with **bold**, *emphasis*, \`code\`, a [link](https://example.com/${i}) and $x^${i % 9}$ in it, long enough to wrap.`,
  `- item one of ${i}\n- item two with *more*\n  - nested ${i}`, `> quoted ${i}`, '```js\nconst a = ' + i + ';\n```', `| a | b |\n|---|---|\n| ${i} | **${i + 1}** |`].join('\n\n');
const doc = (tag, chars) => { let m = `# ${tag}\n\n`, n = 0; while (m.length < chars) m += section(tag, ++n) + '\n\n'; return m; };
const short = '# Short\n\nA short document.\n\n- one\n- two\n';

let failures = 0;
const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures++; };

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'], protocolTimeout: 180000 });
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 700 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: 'x\n', basePath: '/tmp', loadId: 1 };
  // Every page message is kept with its load id; StateChange as the host reads it (a diff, tocUnchanged keeps the outline).
  await page.evaluateOnNewDocument(`(()=>{const ls=[];const prev={};window.__last={};window.__msgs=[];let toc=null;
    window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);return;}
      let a=m.args; if(m.type==='diffmsg'){const o=prev[m.name]||'';prev[m.name]=m.diff?o.slice(0,m.start)+m.args+o.slice(m.end):m.args;a=JSON.parse(prev[m.name]);}
      if(m.name==='StateChange'&&a&&a.state){if(a.tocUnchanged)a.state.toc=toc;else toc=a.state.toc;}
      window.__last[m.name]=a; window.__msgs.push({name:m.name,args:a});}}}})();`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__last.FileLoaded, { timeout: 60000 });
  await new Promise(r => setTimeout(r, 800));
  const cdp = await page.target().createCDPSession();

  let loadId = 10, current = null, currentLoad = 0;
  const tabs = {};
  const flush = async () => {
    await page.evaluate(() => { delete window.__last.ContentFlushed; window.__deliver('FlushContent', { token: 7 }) });
    await page.waitForFunction(() => window.__last.ContentFlushed, { timeout: 60000 });
  };
  // The host's side of a switch: the tab left keeps the text the page flushed, the caret and scroll offset it reported.
  const switchTo = async (name) => {
    if (current) {
      await flush();
      const s = await page.evaluate(id => {
        const c = window.__msgs.filter(m => m.name === 'CursorChange' && m.args.loadId === id).pop();
        return { text: window.__last.ContentFlushed.text, cursor: c ? c.args.cursor : null, y: Math.round(window.scrollY) };
      }, currentLoad);
      Object.assign(tabs[current], { text: s.text, cursor: s.cursor, scrollTop: s.y });
    }
    const id = ++loadId; currentLoad = id; current = name; const t = tabs[name];
    return page.evaluate(async ({ t, id }) => {
      for (const e of document.querySelectorAll('#ag-editor-id > *')) e.__mark = 1;
      window.__msgs = [];
      window.__deliver('LoadFile', { text: t.text, basePath: '/tmp', cursor: t.cursor, scrollTop: t.scrollTop, loadId: id, hold: 0 });
      const until = async f => { for (let i = 0; i < 4000 && !f(); i++) await new Promise(r => setTimeout(r, 5)); };
      const got = n => window.__msgs.filter(m => m.name === n && m.args && m.args.loadId === id);
      await until(() => got('FileLoaded').length && got('StateChange').length && got('CursorChange').length);
      // A long document drawn a part at a time (Muya's renderProgressive) is compared once it is all drawn.
      await until(() => !(window.__typedownMuya && window.__typedownMuya.renderPending));
      await new Promise(r => setTimeout(r, 1200));
      const root = document.querySelector('#ag-editor-id');
      const ids = new Map();
      // Block keys differ from build to build: they are numbered in the order they appear.
      const keys = t => t.replace(/\bag-\d+\b/g, k => { if (!ids.has(k)) ids.set(k, `K${ids.size}`); return ids.get(k); });
      // The block the caret moves through is re-rendered in place (snabbdom's patch), which writes a style as "a: b;"
      // and an empty title as title="" where the HTML of a new build has "a: b" and no title: the same drawing.
      const html = keys(root.innerHTML).replace(/ style="([^"]*?);"/g, ' style="$1"').replace(/ title=""/g, '');
      const sel = window.getSelection();
      const block = sel.rangeCount ? sel.anchorNode && (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement).closest('[id^="ag-"]') : null;
      const state = got('StateChange')[0];
      return {
        reused: [...root.children].some(e => e.__mark), apply: window.__typedownLastApply, html,
        fileLoaded: got('FileLoaded')[0].args, firstState: { toc: keys(JSON.stringify(state.args.state.toc)), unchanged: !!state.args.tocUnchanged, words: JSON.stringify(state.args.state.wordCount), cur: keys(JSON.stringify(state.args.state.cur)) },
        cursor: JSON.stringify((got('CursorChange').pop() || { args: {} }).args.cursor), caret: block ? `${ids.get(block.id) || block.id}:${sel.anchorOffset}:${sel.focusOffset}` : null,
        y: Math.round(window.scrollY), kept: window.__typedownKeptDocuments ? window.__typedownKeptDocuments.size() : -1,
        loadIds: [...new Set(window.__msgs.filter(m => m.args && 'loadId' in m.args).map(m => m.args.loadId))],
        markdownChanges: window.__msgs.filter(m => m.name === 'MarkdownChange').length,
      };
    }, { t, id });
  };
  // A switch back built anew from the same tab state, for comparison: nothing kept, so it cannot be reused.
  const fresh = async (name) => {
    await switchTo('B');
    await page.evaluate(() => window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear());
    return switchTo(name);
  };
  const firstDiff = (a, b) => { let i = 0; while (i < a.length && a[i] === b[i]) i++; return `at ${i}: kept ${JSON.stringify(a.slice(Math.max(0, i - 120), i + 80))} new ${JSON.stringify(b.slice(Math.max(0, i - 120), i + 80))}`; };
  const same = (r, f, what) => {
    if (r.html !== f.html) console.log('    ' + firstDiff(r.html, f.html));
    check(r.html === f.html, `${what}: the elements are those a new build draws${r.html === f.html ? '' : ` (${r.html.length} / ${f.html.length} chars)`}`);
    check(r.caret === f.caret && r.cursor === f.cursor, `${what}: the caret is where a new build puts it (${r.caret} ${r.cursor} / ${f.caret} ${f.cursor})`);
    check(r.y === f.y && Math.abs(r.y - tabs[current].scrollTop) <= 2, `${what}: the page is back at its offset (${r.y} / ${f.y}, asked ${tabs[current].scrollTop})`);
    check(r.fileLoaded.same === true && f.fileLoaded.same === true, `${what}: FileLoaded says the text is as given`);
    // Not the word count: on a long document the first report of a load can carry the count of the document before
    // (Muya's word count is reused for 300 ms), for a new build as much as for a kept one.
    check(r.firstState.toc === f.firstState.toc && r.firstState.cur === f.firstState.cur && !r.firstState.unchanged && !f.firstState.unchanged, `${what}: the first StateChange carries the same outline and caret line`);
    check(r.loadIds.length === 1 && r.markdownChanges === 0, `${what}: every report carries the load's id (${r.loadIds}), no MarkdownChange`);
  };

  // The long documents as Muya exports them, so that each is its own export (a caret it reported in lines of its
  // export is then a caret in its text).
  const settle = async (name, text) => { tabs[name] = { text, cursor: null, scrollTop: 0 }; await switchTo(name); return page.evaluate(() => window.__typedownMuya.getMarkdown()); };
  tabs.A = { text: await settle('A0', doc('Alpha', 120000)), cursor: null, scrollTop: 0 };
  tabs.C = { text: await settle('C0', doc('Gamma', 90000)), cursor: null, scrollTop: 0 };
  tabs.D = { text: await settle('D0', doc('Delta', 70000)), cursor: null, scrollTop: 0 };
  tabs.B = { text: short, cursor: null, scrollTop: 0 };
  console.log(`A ${tabs.A.text.length} chars, C ${tabs.C.text.length}, D ${tabs.D.text.length}, B ${tabs.B.text.length}`);

  console.log('A switch back to a document left as it was loaded:');
  await switchTo('A');
  await page.evaluate(() => window.scrollTo(0, 9000)); await new Promise(r => setTimeout(r, 400));
  await switchTo('B');
  let r = await switchTo('A');
  check(r.reused && r.apply === 'kept', `the kept elements are shown again (reused ${r.reused}, ${r.apply})`);
  let f = await fresh('A');
  check(!f.reused, 'with nothing kept it is built anew');
  same(r, f, 'kept');

  console.log('After the caret was moved by a click:');
  await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[60]; p.scrollIntoView({ block: 'center' }); });
  await new Promise(r => setTimeout(r, 300));
  const box = await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[60].getBoundingClientRect(); return { x: p.left + 40, y: p.top + p.height / 2 }; });
  await page.mouse.click(box.x, box.y); await new Promise(r => setTimeout(r, 600));
  await switchTo('B');
  r = await switchTo('A');
  check(r.reused, `a document whose caret only moved is shown again (${r.apply})`);
  f = await fresh('A');
  same(r, f, 'caret moved');
  console.log('  inside a heading, a list item, a quote, code, a table cell:');
  for (const sel of ['#ag-editor-id > h2', '#ag-editor-id > ul li', '#ag-editor-id > blockquote p', '#ag-editor-id > pre .ag-code-content', '#ag-editor-id > figure td']) {
    const at = await page.evaluate(sel => { const e = document.querySelectorAll(sel)[30]; e.scrollIntoView({ block: 'center' }); const b = e.getBoundingClientRect(); return { x: b.left + Math.min(60, b.width / 2), y: b.top + b.height / 2 }; }, sel);
    await page.mouse.click(at.x, at.y); await new Promise(r => setTimeout(r, 600));
    await switchTo('B');
    r = await switchTo('A');
    f = await fresh('A');
    // With the caret in a table cell a new build draws a tool bar on every table: such a document is not kept.
    if (sel.endsWith('td')) { check(!r.reused, `${sel}: not kept (${r.apply})`); continue; }
    check(r.reused, `${sel}: shown again (${r.apply})`);
    same(r, f, sel);
  }

  // Built with the caret in a cell, every table has a tool bar; the caret moved out leaves the others theirs.
  await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[40]; p.scrollIntoView({ block: 'center' }); });
  const pb = await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[40].getBoundingClientRect(); return { x: p.left + 40, y: p.top + p.height / 2 }; });
  await page.mouse.click(pb.x, pb.y); await new Promise(r => setTimeout(r, 600));
  await switchTo('B'); r = await switchTo('A');
  check(!r.reused, `caret moved out of a table into a document built with it in one: not kept (${r.apply})`);
  await switchTo('B'); r = await switchTo('A'); f = await fresh('A');
  check(r.reused, `built again with the caret in a paragraph: kept (${r.apply})`);
  same(r, f, 'paragraph again');

  console.log('Not reused after a change:');
  await switchTo('B');
  await page.evaluate(() => window.__deliver('SettingsChanged', { fontSize: 17 }));
  await new Promise(r => setTimeout(r, 300));
  r = await switchTo('A');
  check(!r.reused && r.apply === 'whole', `a settings change while away: built anew (${r.apply})`);
  await page.evaluate(() => window.__deliver('SettingsChanged', { fontSize: 16 })); await new Promise(r => setTimeout(r, 500));
  // Built under the earlier setting, so not kept for this one either; built again, it is.
  await switchTo('B'); r = await switchTo('A');
  check(!r.reused, `a settings change while shown: built anew on the way back (${r.apply})`);
  await switchTo('B'); r = await switchTo('A');
  check(r.reused, `kept again once built with the settings in force (${r.apply})`);
  await switchTo('B');
  await page.evaluate(() => window.__deliver('SettingsChanged', { readOnly: true })); await new Promise(r => setTimeout(r, 300));
  r = await switchTo('A');
  check(!r.reused, `reading mode switched on while away: built anew (${r.apply})`);
  await page.evaluate(() => window.__deliver('SettingsChanged', { readOnly: false })); await new Promise(r => setTimeout(r, 500));
  await switchTo('B'); await switchTo('A');
  await switchTo('B');
  tabs.A.text += '\nOne more paragraph.\n';
  r = await switchTo('A');
  check(!r.reused && r.apply === 'whole', `another text: built anew (${r.apply})`);
  // Typed into: not kept (an edited document may not be what a new build of its text draws).
  await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[5]; p.scrollIntoView({ block: 'center' }); });
  const tb = await page.evaluate(() => { const p = document.querySelectorAll('#ag-editor-id > p')[5].getBoundingClientRect(); return { x: p.left + 40, y: p.top + p.height / 2 }; });
  await page.mouse.click(tb.x, tb.y); await new Promise(r => setTimeout(r, 300));
  await page.keyboard.type('Z'); await new Promise(r => setTimeout(r, 600));
  await switchTo('B');
  check(tabs.A.text.includes('Z'), 'the typed letter reached the host');
  r = await switchTo('A');
  check(!r.reused, `typed into before leaving: built anew (${r.apply})`);

  console.log('Bounded:');
  await switchTo('C'); await switchTo('D'); await switchTo('B');
  r = await page.evaluate(() => window.__typedownKeptDocuments ? window.__typedownKeptDocuments.size() : -1);
  check(r === 2, `three long documents left, two kept (${r})`);
  r = await switchTo('A');
  check(!r.reused, `the oldest went (${r.apply})`);
  r = await switchTo('D');
  check(r.reused, `the newest is there (${r.apply})`);

  // What one kept document holds: on B with A kept, then with nothing kept.
  await switchTo('A'); await switchTo('B');
  const measure = async () => {
    for (let i = 0; i < 3; i++) { await cdp.send('HeapProfiler.collectGarbage'); await new Promise(r => setTimeout(r, 300)); }
    return { js: (await cdp.send('Runtime.getHeapUsage')).usedSize, nodes: (await cdp.send('Memory.getDOMCounters')).nodes, kept: await page.evaluate(() => window.__typedownKeptDocuments ? window.__typedownKeptDocuments.size() : -1) };
  };
  const withKept = await measure();
  await page.evaluate(() => window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear());
  const without = await measure();
  console.log(`  memory with ${withKept.kept} kept (D and A, ${tabs.D.text.length + tabs.A.text.length} chars): JS heap +${((withKept.js - without.js) / 1048576).toFixed(1)} MB, DOM nodes +${withKept.nodes - without.nodes}`);

  check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.slice(0, 3).join(' | ') : ''}`);
  console.log(failures ? `${failures} FAILED` : 'all passed');
  await browser.close(); server.close();
  process.exit(failures ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
