// The outline marks the heading of what is on screen as soon as a document is shown - not only once the reader has
// scrolled. Edit mode follows the caret (StateChange's `cur`), reading mode the page (OutlineCurrent); both have to
// name a heading for every load, without the page being touched:
//   modes:     edit, reading
//   documents: short (drawn at once), long (> 10k characters, drawn a part at a time), 300k (also kept on a switch)
//   loads:     opened fresh at the top; opened at a remembered place (scrollTop + scrollAnchor, the caret with it,
//              as the host remembers them); switched back to (the same text and place again - a kept document for 300k);
//              the page loaded again with it (GetSettings: start-up, leaving the settings)
// The stub host marks what the real one marks: in edit mode the `cur` of each state report of the load shown, in
// both modes the heading an OutlineCurrent names. A load starts with nothing marked (the host's outline is another
// document's). Expected: the heading whose top has reached the top of the window (OUTLINE_TOP), the first one if none
// has - which is also the caret's heading, the caret being given in that section.
// The page loaded again names the very key it named on the page before (its blocks are numbered from the start): the
// host has to mark it in the new load's outline all the same - that half is the host's, tested in
// Dev/Typedown.Automation.Tests/OutlineMarkTests.cs (the stub here starts each page with nothing marked).
//
//   node outline-load-check.js [--only edit|reading] [--doc short|long|300k]
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const args = process.argv.slice(2);
const opt = (f) => { const i = args.indexOf(f); return i >= 0 ? args[i + 1] : null; };
const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

const section = (i) => [`## Section ${i}`, `Paragraph ${i} with text long enough to wrap onto a second line in the editor, and then some more.`, '- one\n- two'].join('\n\n');
const make = (chars) => { let m = '', n = 0; while (m.length < chars) m += section(++n) + '\n\n'; return m; };
const docs = [['short', make(5000)], ['long', make(40000)], ['300k', fs.readFileSync(path.join(__dirname, 'doc300k.md'), 'utf8')]]
  .filter(([name]) => !opt('--doc') || opt('--doc') === name);
const other = '# Another document\n\nSomething else to look at.\n\n## Its second heading\n\nMore.\n';
const modes = ['edit', 'reading'].filter(m => !opt('--only') || opt('--only') === m);

const pause = (ms) => new Promise(r => setTimeout(r, ms));

// An editor page behind a stub host, its first document given with GetSettings as the host does at start-up and when
// the page is loaded again (leaving the settings navigates the web view to it anew).
async function openPage(browser, port, mode, first) {
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, readOnly: mode === 'reading', basePath: '/tmp', loadId: 1,
    markdown: first ? first.text : '', cursor: first ? first.place.cursor : null, scrollTop: first ? first.place.y : null, scrollAnchor: first ? first.place.anchor : null };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];const prev={};
    window.__host={loadId:1,marked:null,via:null,readOnly:${mode === 'reading'}};
    window.__deliver=(n,a)=>{if(n==='LoadFile'){window.__host.loadId=a.loadId;window.__host.marked=null;window.__host.via=null;}ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}))};
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);
      if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;try{m.args=JSON.parse(full)}catch(e){m.args=null}}
      m.at=performance.now();window.__msgs.push(m);const h=window.__host,a=m.args||{};
      if(m.name==='StateChange'&&a.loadId===h.loadId&&!h.readOnly&&a.state&&a.state.cur){h.marked=a.state.cur.slug;h.via='caret';}
      if(m.name==='OutlineCurrent'&&a.loadId===h.loadId&&a.slug){h.marked=a.slug;h.via='page';}
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 120000 });
  const settled = await afterLoad(page, 1, 0, first ? first.place : null);
  return { page, errors, settled };
}

// After a load's report: the page put back where it was asked to be, the document drawn whole; then a second for the
// outline. Nothing scrolls the page meanwhile.
async function afterLoad(page, id, since, place) {
  const drawn = await page.waitForFunction(id => window.__msgs.find(m => m.name === 'LoadDrawn' && m.args.loadId === id) || null, { timeout: 5000 }, id).then(h => h.jsonValue()).catch(() => null);
  if (drawn && drawn.args.progressive)
    await page.waitForFunction(id => window.__msgs.some(m => m.name === 'LoadDrawnWhole' && m.args.loadId === id), { timeout: 120000 }, id).catch(() => { });
  if (place && place.y > 0)
    await page.waitForFunction(since => window.__msgs.some(m => m.name === 'ScrollSettled' && m.at > since), { timeout: 30000 }, since).catch(() => { });
  await pause(1000);
  return { progressive: !!(drawn && drawn.args.progressive) };
}

// What is on screen and what the host has marked, read in one go.
const look = (page) => page.evaluate((OUTLINE_TOP) => {
  const root = document.getElementById('ag-editor-id');
  const heads = Array.from(root.querySelectorAll(':scope > h1, :scope > h2, :scope > h3, :scope > h4, :scope > h5, :scope > h6'));
  let found = -1;
  for (let i = 0; i < heads.length; i++) { if (heads[i].getBoundingClientRect().top <= OUTLINE_TOP + 1) found = i; else break; }
  const expected = heads[Math.max(0, found)];
  // The heading's text without what the editor draws besides it (its "H2" mark, contenteditable=false) and its #s.
  const name = (slug) => {
    const e = slug && document.getElementById(slug);
    if (!e) return slug ? `(${slug}, not on the page)` : null;
    const copy = e.cloneNode(true); copy.querySelectorAll('[contenteditable="false"]').forEach(x => x.remove());
    return copy.textContent.replace(/^\s*#+\s*/, '').trim();
  };
  return { y: Math.round(window.scrollY), expected: expected ? expected.id : null, expectedName: expected ? name(expected.id) : null, marked: window.__host.marked, markedName: name(window.__host.marked), via: window.__host.via };
}, 16);

async function runMode(browser, port, mode) {
  let { page, errors } = await openPage(browser, port, mode, null);
  let loadId = 1;
  const failures = [];
  const report = (label, progressive, seen, note = '') => {
    const ok = !!seen.expected && seen.marked === seen.expected;
    console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${mode.padEnd(7)} ${label.padEnd(30)} ${progressive ? 'progressive' : 'at once    '} page at ${String(seen.y).padStart(6)}: on screen "${seen.expectedName}", marked ${seen.marked ? `"${seen.markedName}" (${seen.via})` : 'nothing'}${note}`);
    if (!ok) failures.push(`${mode} ${label}`);
    return seen;
  };
  const load = async (text, place) => {
    const id = ++loadId;
    const since = await page.evaluate(({ text, id, place }) => {
      const at = performance.now();
      window.__deliver('LoadFile', { text, basePath: '/tmp', loadId: id, cursor: place ? place.cursor : null, scrollTop: place ? place.y : null, scrollAnchor: place ? place.anchor : null });
      return at;
    }, { text, id, place });
    await page.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args && m.args.loadId === id), { timeout: 120000 }, id);
    return afterLoad(page, id, since, place);
  };
  const check = async (label, text, place) => { const { progressive } = await load(text, place); return report(label, progressive, await look(page)); };

  // Where a reader left the document: a place well down it, read back from the page's own scroll report as the host
  // keeps it (scrollY and the block at the top of the window), with the caret in the section on screen.
  const leave = async (text, fraction) => {
    const y = await page.evaluate(async (fraction) => {
      window.scrollTo(0, Math.round((document.documentElement.scrollHeight - window.innerHeight) * fraction));
      for (let i = 0; i < 10; i++) await new Promise(r => requestAnimationFrame(r));
      return Math.round(window.scrollY);
    }, fraction);
    await pause(700);
    const scrolled = await page.evaluate(() => { const s = window.__msgs.filter(m => m.name === 'OnScroll' && m.args); return s.length ? s[s.length - 1].args : null; });
    const seen = await look(page);
    const line = text.split('\n').findIndex(l => /^#{1,6}\s/.test(l) && l.replace(/^#+\s*/, '').trim() === seen.expectedName);
    if (line < 0) throw new Error(`heading "${seen.expectedName}" not found in the text`);
    const cursor = { anchor: { line: line + 2, ch: 0 }, focus: { line: line + 2, ch: 0 } };
    return { y: scrolled ? Math.round(scrolled.scrollY) : y, anchor: scrolled ? scrolled.anchor : null, cursor };
  };

  for (const [name, text] of docs) {
    await check(`${name}: fresh at the top`, text, null);
    const place = await leave(text, 0.6);
    await load(other, null);
    // A different text, so this is a new build, not the copy kept from the load above: opened at a remembered place.
    const again = text + '\nThe end.\n';
    await check(`${name}: at a remembered place`, again, place);
    await load(other, null);
    // The same text and place again: a tab switched back to (a 300k document comes back kept, as it was left).
    const back = await check(`${name}: switched back to`, again, place);
    // The page loaded anew with the same document and place (leaving the settings navigates the web view to it again):
    // its first document comes with GetSettings. Twice: a page's blocks are numbered from the start, so the second
    // page's heading has the very key the first page's had - a heading the host has marked already, for the load
    // before (the host must still mark it in the outline it builds for this load: OutlineMark).
    let before = null;
    for (const time of ['', ', twice']) {
      if (errors.length) failures.push(`${mode} page errors: ${errors.slice(0, 3).join(' | ')}`);
      await page.close();
      ({ page, errors } = await openPage(browser, port, mode, { text: again, place }));
      loadId = 1;
      const seen = await look(page);
      const progressive = await page.evaluate(() => window.__msgs.some(m => m.name === 'LoadDrawn' && m.args.progressive));
      report(`${name}: page loaded again${time}`, progressive, seen, before && seen.marked === before ? ` - the same key (${seen.marked}) as on the page before` : '');
      before = seen.marked;
    }
  }
  if (errors.length) failures.push(`${mode} page errors: ${errors.slice(0, 3).join(' | ')}`);
  await page.close();
  return failures;
}

(async () => {
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'], protocolTimeout: 300000 });
  const failures = [];
  for (const mode of modes) failures.push(...await runMode(browser, port, mode));
  await browser.close(); server.close();
  console.log(failures.length ? `FAIL: no heading or the wrong one marked after ${failures.length} load(s): ${failures.join('; ')}`
                              : 'OK: every load marks the heading on screen without the page being scrolled');
  process.exit(failures.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
