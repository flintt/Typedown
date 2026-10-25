// LT01 in the page alone (Tools/AutomationE2E/Driver/LongTimes.cs): a long document dense in raw HTML (gen-lt01.js,
// shaped like the 1.5 MB one it was reported with) opened, and how long a reader waits - its text on screen, drawn
// whole, the longest the page draws no frame meanwhile, Ctrl+End right after opening, and a search for a word near the
// end right after opening (Ctrl+F in the page, then the host's find bar: SearchOpenChange, a Search for each letter
// typed, Enter = Find next). Each opening is fresh: another document first, then the long one. The limits are LT01's.
//
//   node lt01-check.js [--only open,end,search] [--width 1280 --height 900] [--reading] [--prof name]
//   --slow N: the page's CPU N times slower (DevTools throttling), for a machine slower than this one (the W10 PC is
//             about 2-3 times slower)
//   --typing ms: between the letters of the search (90, as LT01's keys come)
//   --reading: in reading mode (JT02/JT03 drew the same document in reading mode at 1402 x 1070)
//   --prof name: a CPU profile of the run to name.cpuprofile (prof-summary.js reads it)
//   --trace name: a DevTools performance trace of the run to name.json (layout and style passes, with their sizes)
'use strict';
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const { generate } = require('./gen-lt01');

const statics = path.resolve(process.env.STATICS || path.join(__dirname, '../../Dev/Typedown/Resources/Statics'));
const args = process.argv.slice(2);
const arg = (name, def) => { const i = args.indexOf('--' + name); return i >= 0 ? args[i + 1] : def; };
const only = (arg('only', 'open,end,search')).split(',');
const width = Number(arg('width', 1280)), height = Number(arg('height', 900));
const prof = arg('prof', null);
const reading = args.includes('--reading');
const typingMs = Number(arg('typing', 90));
const slowRate = Number(arg('slow', 1));
const trace = arg('trace', null);
const LIMIT = { first: 1500, whole: 10000, gap: 200, end: 1000, search: 1000 };
const HANG_MS = 60000;

const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });

async function open(browser, port) {
  const page = await browser.newPage(); await page.setViewport({ width, height });
  // LT01 turns height prediction off (test.settings.set PredictHeights false).
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1, predictHeights: false, readOnly: reading };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=m.name==='MarkdownChange'?null:JSON.parse(full);}m.at=performance.now();if(window.__msgs.length>200)window.__msgs.splice(0,100);window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  return page;
}

// Evaluates in the page; a page that does not answer within HANG_MS counts as hung (null), as the host's eval timing out.
const evalOr = (p, fn, ...a) => Promise.race([p.evaluate(fn, ...a), new Promise(r => setTimeout(() => r(null), HANG_MS))]);
const onScreen = what => {
  for (const b of document.querySelectorAll('#ag-editor-id > *')) {
    const r = b.getBoundingClientRect();
    if (r.bottom > 0 && r.top < innerHeight && (what == null ? b.textContent.trim().length > 0 : b.textContent.includes(what))) return true;
  }
  return false;
};
// Polls (every 30 ms, as LT01 does) until `test` holds in the page; ms from `from` (Date.now()), Infinity past `limit`.
async function until(p, test, a, from, limit) {
  while (Date.now() - from < limit) {
    const r = await evalOr(p, test, a);
    if (r === null) return Infinity;
    if (r) return Date.now() - from;
    await new Promise(r => setTimeout(r, 30));
  }
  return Infinity;
}

let loadId = 10;
async function fresh(p) {
  await p.evaluate((id) => window.__deliver('LoadFile', { text: '# other\n', basePath: '/tmp', cursor: null, scrollTop: 0, scrollAnchor: null, loadId: id, hold: 0 }), ++loadId);
  await p.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === id), { timeout: 60000 }, loadId);
  await new Promise(r => setTimeout(r, 300));
  // Frames from the moment it is sent: the longest gap, once text is on screen.
  await p.evaluate(() => { const f = window.__ltFrames = { max: 0, over: 0, last: 0, on: false, from: 0 }; const tick = t => { if (f.on) { const g = t - f.last; if (g > f.max) f.max = g; if (g > 200) f.over++ } f.last = t; requestAnimationFrame(tick) }; requestAnimationFrame(tick) });
  // Sent from inside the page, as the host's message arrives there (the text put in the page beforehand: passing 1.5 MB
  // through the DevTools protocol each time counted its own transfer). Its text on screen: the first frame that has it,
  // timed in the page from the moment the message is handed to it - the page's part of LT01's first screen, not the host's
  // reading and sending of the file.
  const from = Date.now();
  await p.evaluate(id => {
    const t0 = performance.now();
    window.__ltFirst = null;
    const look = () => {
      const blocks = window.__typedownMuya?.contentState?.blocks?.length || 0;
      const on = blocks > 1000 && Array.from(document.querySelectorAll('#ag-editor-id > *')).some(b => { const r = b.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight && b.textContent.trim().length > 0 });
      if (on) window.__ltFirst = performance.now() - t0; else if (performance.now() - t0 < 10000) requestAnimationFrame(look);
    };
    requestAnimationFrame(look);
    window.__deliver('LoadFile', { text: window.__lt01Text, basePath: '/tmp', cursor: null, scrollTop: 0, scrollAnchor: null, loadId: id, hold: 0 });
  }, ++loadId);
  await until(p, () => window.__ltFirst != null, null, from, 10000);
  const first = (await evalOr(p, () => window.__ltFirst)) ?? Infinity;
  return { from, first };
}
const focusEditor = p => p.evaluate(() => { const e = document.getElementById('ag-editor-id'); e && e.focus({ preventScroll: true }) });

(async () => {
  const { text, word, lastLine } = generate();
  const endText = lastLine.slice(0, 20);
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'], protocolTimeout: 600000 });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  const fmt = ms => Number.isFinite(ms) ? `${Math.round(ms)} ms` : 'never (hung or over the wait)';
  const p = await open(browser, port);
  await p.evaluate(t => { window.__lt01Text = t }, text);
  const cdp = await p.target().createCDPSession();
  if (slowRate > 1) await cdp.send('Emulation.setCPUThrottlingRate', { rate: slowRate });
  if (prof) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.setSamplingInterval', { interval: 200 }); await cdp.send('Profiler.start'); }
  if (trace) await p.tracing.start({ path: `${trace}.json`, categories: ['devtools.timeline', 'disabled-by-default-devtools.timeline', 'blink', 'v8.execute'] });
  console.log(`document: ${text.length} characters, word "${word}", last line "${endText}…", window ${width}x${height}`);

  if (only.includes('open')) {
    await p.evaluate(() => { for (const k of Object.keys(window.__typedownPretextStats || {})) delete window.__typedownPretextStats[k] });
    const { from, first } = await fresh(p);
    await p.evaluate(() => { window.__ltFrames.on = true; window.__ltFrames.last = performance.now() });
    const blocks = await p.evaluate(() => window.__typedownMuya?.contentState?.blocks?.length);
    const whole = await until(p, () => !window.__typedownMuya?.renderPending, null, from, 120000);
    const gaps = await p.evaluate(() => { const f = window.__ltFrames; f.on = false; return { max: Math.round(f.max), over: f.over } });
    // Where the slices' time went (predictHeights.js account()).
    const stats = await p.evaluate(() => Object.entries(window.__typedownPretextStats || {}).filter(([k]) => k.startsWith('slice')).map(([k, v]) => `${k} ${Math.round(v.ms)} ms/${v.n}`).join(', '));
    console.log(`  slices: ${stats}`);
    console.log(`open: ${blocks} top-level blocks; text on screen ${fmt(first)}, drawn whole ${fmt(whole)}, longest frame gap ${gaps.max} ms (${gaps.over} over 200)`);
    check(first <= LIMIT.first, `opened, its text on screen within ${LIMIT.first} ms (${fmt(first)})`);
    check(whole <= LIMIT.whole, `drawn whole within ${LIMIT.whole / 1000} s (${fmt(whole)})`);
    check(gaps.max <= LIMIT.gap, `while it is drawn no frame gap over ${LIMIT.gap} ms (longest ${gaps.max} ms, ${gaps.over} over)`);
  }
  if (only.includes('end')) {
    await fresh(p);
    await focusEditor(p);
    const drawing = await p.evaluate(() => !!window.__typedownMuya?.renderPending);
    const from = Date.now();
    await p.keyboard.down('Control'); await p.keyboard.press('End'); await p.keyboard.up('Control');
    const end = await until(p, onScreen, endText, from, 30000);
    console.log(`Ctrl+End ${drawing ? 'while drawing' : '(already drawn)'}: the last line on screen in ${fmt(end)}`);
    check(drawing && end <= LIMIT.end, `Ctrl+End right after opening shows the last line within ${LIMIT.end} ms (${fmt(end)})`);
    // The caret where Ctrl+End puts it: at the end of the last line.
    const caret = await evalOr(p, last => { const s = getSelection(); const b = s.anchorNode && (s.anchorNode.nodeType === 1 ? s.anchorNode : s.anchorNode.parentElement).closest('#ag-editor-id > *'); return { last: !!b && b === document.getElementById('ag-editor-id').lastElementChild && b.textContent.includes(last), atEnd: s.isCollapsed && s.anchorOffset === (s.anchorNode.textContent || '').length } }, endText);
    if (!reading) check(!!caret && caret.last && caret.atEnd, `and the caret is at the end of the last line (${JSON.stringify(caret)})`);
  }
  if (only.includes('search')) {
    await fresh(p);
    await focusEditor(p);
    const drawing = await p.evaluate(() => !!window.__typedownMuya?.renderPending);
    // What each search, step and new start of the drawing costs the page (printed with the result).
    await p.evaluate(() => {
      const m = window.__typedownMuya; const sr = m.contentState.stateRender; const log = window.__ltSearchLog = [];
      const wrap = (o, name) => { const f = o[name]; o[name] = function (...a) { const t = performance.now(); try { return f.apply(this, a) } finally { log.push(`${name} ${typeof a[0] === 'string' ? a[0] : ''} ${Math.round(performance.now() - t)}`) } } };
      wrap(m, 'search'); wrap(m, 'find'); wrap(sr, 'jumpProgressive'); wrap(sr, 'redrawIdle'); wrap(m.contentState, 'search'); wrap(sr, 'redrawNow');
    });
    const from = Date.now();
    // Ctrl+F: the host takes the F (its keyboard hook swallows a shortcut it knows), the page sees the Ctrl going down and
    // up; then the find bar posts a Search for each letter typed (EditorViewModel.SearchValueChanged) and Find next for
    // Enter (FindReplace.OnTextBoxSearchKeyDown).
    await p.keyboard.down('Control'); await p.keyboard.up('Control');
    await evalOr(p, () => window.__deliver('SearchOpenChange', { open: 1 }));
    await new Promise(r => setTimeout(r, 300));
    const opt = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: false, selection: { anchor: null, focus: null } };
    // Typed as LT01 types it (real keys, about 90 ms apart): the bar posts a Search per letter.
    for (let i = 1; i <= word.length; i++) {
      // Posted without waiting for the page, as the host does; a page that is busy gets them queued.
      p.evaluate((value, opt) => window.__deliver('Search', { value, opt }), word.slice(0, i), opt).catch(() => {});
      await new Promise(r => setTimeout(r, typingMs));
    }
    // Enter: Find next. When the page itself first shows the word in the window, timed in the page from that message.
    p.evaluate(word => {
      const t0 = performance.now(); window.__ltFound = null;
      const look = () => {
        const on = Array.from(document.querySelectorAll('#ag-editor-id > *')).some(b => { const r = b.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight && b.textContent.includes(word) });
        if (on) window.__ltFound = performance.now() - t0; else if (performance.now() - t0 < 30000) requestAnimationFrame(look);
      };
      window.__deliver('Find', { action: 'next' });
      requestAnimationFrame(look);
    }, word).catch(() => {});
    const typed = Date.now();
    const found = await until(p, onScreen, word, typed, 30000);
    console.log(`  page: ${(await evalOr(p, () => window.__ltSearchLog.filter(l => !/^redrawIdle .* [0-9]$/.test(l)).join(', ')))}`);
    console.log(`  in the page: the word in the window ${fmt(await evalOr(p, () => window.__ltFound ?? Infinity))} after Find reached it`);
    console.log(`search for "${word}" ${drawing ? 'while drawing' : '(already drawn)'}: on screen ${fmt(found)} after Enter (${fmt(found + typed - from)} after Ctrl+F)`);
    check(drawing && found <= LIMIT.search, `a search right after opening shows a word near the end within ${LIMIT.search} ms of Enter (${fmt(found)})`);
    // Marked as the match chosen, in the window.
    const mark = await evalOr(p, () => { const m = document.querySelector('#ag-editor-id .ag-highlight'); if (!m) return null; const r = m.getBoundingClientRect(); return { text: m.textContent, inWindow: r.bottom > 0 && r.top < innerHeight } });
    check(!!mark && mark.text.toLowerCase() === word.toLowerCase() && mark.inWindow, `it is marked as the match, in the window (${JSON.stringify(mark)})`);
    // The bar closed: the marks go, the caret is left on the match.
    const closed = Date.now();
    await evalOr(p, () => window.__deliver('SearchOpenChange', { open: 0 }));
    const gone = await until(p, () => !document.querySelector('#ag-editor-id .ag-highlight, #ag-editor-id .ag-selection'), null, closed, 10000);
    check(gone <= LIMIT.search, `closing the search takes its marks away within ${LIMIT.search} ms (${fmt(gone)})`);
  }
  if (trace) { await p.tracing.stop(); console.log(`trace: ${trace}.json`); }
  if (prof) {
    const { profile } = await cdp.send('Profiler.stop');
    fs.writeFileSync(`${prof}.cpuprofile`, JSON.stringify(profile));
    console.log(`profile: ${prof}.cpuprofile`);
  }
  await browser.close(); server.close();
  console.log(ok ? 'lt01-check: all passed' : 'lt01-check: FAILED');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2); });
