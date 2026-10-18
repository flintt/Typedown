// A long document drawn a part at a time (Muya's renderProgressive, docs/todo.md "方案 A"): the block that was at the
// top of the window is on the page at its height long before the whole document would have been drawn, stays there
// while the rest is drawn above and below it, and the document ends up drawn exactly as when drawn at once. Whatever
// works on the page's elements meanwhile draws the rest first: a key typed goes in at the caret, a search from the host
// finds a match far away, select all and copy take the whole text. A document left before it is all drawn is not kept
// half drawn as nothing: it is kept as far as it got and drawn on once back.
//
//   node progressive-check.js [--slow 40]     - with --slow, each slice waits that many ms (a wider window to act in)
//   BREAK=1 node progressive-check.js         - drawing at once (progressive off): the timing checks must fail
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const args = process.argv.slice(2);
const slow = Number(args[args.indexOf('--slow') + 1]) || 0;
const server = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html'; const f = path.join(statics, p); if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); } fs.createReadStream(f).pipe(res); });
const para = (tag, i) => `Paragraph ${i} of ${tag}, with a few words to make it long enough to take a while to draw.\n\n`;
const long = tag => `# ${tag}\n\n` + Array.from({ length: 3000 }, (_, i) => (i % 500 === 0 ? `## Part ${i / 500}\n\n- item ${i} one\n- item ${i} two\n\n` : '') + para(tag, i)).join('');

async function page(browser, port, extraInit = '') {
  const page = await browser.newPage(); await page.setViewport({ width: 1100, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown: '# start\n', basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{${extraInit};const ls=[];window.__msgs=[];window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};const prev={};window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),postMessage:(raw)=>{const m=JSON.parse(raw);if(m.type==='diffmsg'){const full=m.diff?prev[m.name].slice(0,m.start)+m.args+prev[m.name].slice(m.end):m.args;prev[m.name]=full;m.args=JSON.parse(full);}m.at=performance.now();window.__msgs.push(m);if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__msgs.some(m => m.name === 'FileLoaded'), { timeout: 60000 });
  await new Promise(r => setTimeout(r, 500));
  return page;
}

// Loads `text` as the host does, scrolled to the block named by `anchor`; resolves once its load is reported.
const load = (p, text, loadId, anchor, scrollTop) => p.evaluate((text, loadId, anchor, scrollTop) => {
  window.__msgs = []; window.__typedownProgressiveDone = null; window.__t0 = performance.now();
  window.__deliver('LoadFile', { text, basePath: '/tmp', cursor: null, scrollTop: scrollTop ?? 0, scrollAnchor: anchor ?? null, loadId, hold: 0 });
}, text, loadId, anchor, scrollTop).then(() => p.waitForFunction(id => window.__msgs.some(m => m.name === 'FileLoaded' && m.args.loadId === id), { timeout: 60000 }, loadId));

// Where the block starting with this text is in the window, or null if it is not on the page.
const topOf = text => `(()=>{const k=${JSON.stringify(text.slice(0, 30))};for(const b of document.querySelectorAll('#ag-editor-id .ag-paragraph')){if((b.textContent||'').replace(/\\s+/g,' ').trim().startsWith(k))return b.getBoundingClientRect().top}return null})()`;
const pending = () => !!window.__typedownMuya?.renderPending;

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  let ok = true; const check = (c, label) => { console.log((c ? 'PASS' : 'FAIL') + ' ' + label); ok = ok && c; };
  const init = (process.env.BREAK ? 'window.__typedownProgressiveOff=true;' : '') + (slow ? `window.__typedownProgressiveDelayMs=${slow};window.__typedownProgressiveSliceMs=4;` : '');
  const doc = long('Prog');

  // The whole document drawn at once, for the text it must end up with.
  let whole;
  {
    const p = await page(browser, port, 'window.__typedownProgressiveOff=true;');
    await load(p, doc, 2);
    whole = await p.evaluate(() => document.getElementById('ag-editor-id').textContent);
    await p.close();
  }

  const p = await page(browser, port, init);
  // Where the page is to be: a block two thirds of the way through, 120 px down the window.
  const anchor = { text: 'Paragraph 2000 of Prog, with a few words', top: 120, at: 0.66 };
  // Each frame: is the anchor's block on the page, and where.
  await p.evaluate(() => { window.__frames = []; const tick = () => { if (window.__watch) window.__frames.push({ t: performance.now() - window.__t0, top: eval(window.__watch), pending: !!window.__typedownMuya?.renderPending }); requestAnimationFrame(tick) }; requestAnimationFrame(tick) });
  await p.evaluate(src => { window.__watch = src }, topOf(anchor.text));
  await load(p, doc, 3, anchor, 99999);
  const firstShown = await p.evaluate(() => window.__frames.find(f => f.top != null && Math.abs(f.top - 120) <= 2)?.t);
  const loadedAt = await p.evaluate(() => window.__msgs.find(m => m.name === 'FileLoaded').at - window.__t0);
  await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
  const doneAt = await p.evaluate(() => (window.__typedownProgressiveDone ?? performance.now()) - window.__t0);
  await new Promise(r => setTimeout(r, 300));
  const frames = await p.evaluate(() => { window.__watch = null; return window.__frames });
  const shown = frames.filter(f => f.top != null && f.t >= (firstShown ?? 0));
  const worst = shown.reduce((m, f) => Math.max(m, Math.abs(f.top - 120)), 0);
  const drawnWhilePending = frames.filter(f => f.pending).length;
  console.log(`  anchor's block at its height after ${Math.round(firstShown)} ms, load reported at ${Math.round(loadedAt)} ms, all drawn at ${Math.round(doneAt)} ms; ${drawnWhilePending} frames drawn while the rest was coming`);
  check(firstShown != null && firstShown < doneAt * 0.6, `the anchor's block is shown at its height well before the whole document is drawn (${Math.round(firstShown)} vs ${Math.round(doneAt)} ms)`);
  check(drawnWhilePending >= 3, `the page draws frames while the rest is being drawn (${drawnWhilePending})`);
  check(worst <= 2, `the anchor's block stays where it is while the rest is drawn above and below it (worst ${worst.toFixed(1)} px off)`);
  const text = await p.evaluate(() => document.getElementById('ag-editor-id').textContent);
  check(text === whole, `drawn a part at a time, the document is the same as drawn at once (${text.length} vs ${whole.length} characters)`);

  // Acting while it is drawn: each starts from a fresh load of the same text, slowed so there is time to act.
  const again = async (id, tag) => {
    await p.evaluate(() => { window.__typedownProgressiveDelayMs = 60; window.__typedownProgressiveSliceMs = 2 });
    await load(p, doc + `\n${tag}\n`, id, anchor, 99999);
    const was = await p.evaluate(pending);
    return was;
  };
  {
    const was = await again(4, 'Key');
    await p.keyboard.type('Z');
    const after = await p.evaluate(pending);
    await p.waitForFunction(() => window.__msgs.some(m => m.name === 'MarkdownChange' && (m.args.text || '').includes('Z')), { timeout: 10000 }).catch(() => {});
    const typed = await p.evaluate(() => window.__msgs.filter(m => m.name === 'MarkdownChange').map(m => m.args.text || '').find(t => t.includes('Z')) || '');
    const at = typed.indexOf('Z');
    check(was && !after, `a key while it is drawn draws the rest first (pending before ${was}, after ${after})`);
    check(at >= 0 && typed.slice(0, at).endsWith('# Prog'), `the key goes in at the caret (${JSON.stringify(typed.slice(Math.max(0, at - 8), at + 3))})`);
    check((typed.match(/Paragraph \d+ of Prog/g) || []).length === 3000, 'nothing of the document is lost by it');
  }
  {
    const was = await again(5, 'Search');
    await p.evaluate(() => window.__deliver('Search', { value: 'Paragraph 2999 of Prog', opt: { isCaseSensitive: false, isWholeWord: false, isRegexp: false, selectHighlight: true, highlightIndex: -1, selection: {} } }));
    await p.waitForFunction(() => document.querySelector('#ag-editor-id .ag-highlight'), { timeout: 10000 }).catch(() => {});
    const hit = await p.evaluate(() => { const h = document.querySelector('#ag-editor-id .ag-highlight'); return h ? h.closest('.ag-paragraph').textContent.slice(0, 30) : null });
    check(was && hit && hit.startsWith('Paragraph 2999'), `a search from the host while it is drawn finds a match far down (${hit})`);
    await p.evaluate(() => window.__deliver('SearchOpenChange', { open: 0 }));
  }
  {
    const was = await again(6, 'Copy');
    await p.evaluate(() => { window.__copied = null; document.addEventListener('copy', e => setTimeout(() => { window.__copied = e.clipboardData.getData('text/plain') || window.getSelection().toString() }, 0), true) });
    await p.keyboard.down('Control'); await p.keyboard.press('a'); await p.keyboard.up('Control');
    const selected = await p.evaluate(() => window.getSelection().toString());
    check(was && (selected.match(/Paragraph \d+ of Prog/g) || []).length === 3000, `select all while it is drawn selects the whole document (${(selected.match(/Paragraph \d+ of Prog/g) || []).length} paragraphs)`);
  }
  {
    // Left before it is all drawn, then back: not kept half drawn, drawn anew and whole.
    const was = await again(7, 'Leave');
    await load(p, long('Other'), 8);
    await p.evaluate(() => { window.__typedownProgressiveDelayMs = 0; window.__typedownProgressiveSliceMs = 12 });
    await load(p, doc + '\nLeave\n', 9, anchor, 99999);
    await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
    const how = await p.evaluate(() => window.__typedownLastApply);
    const back = await p.evaluate(() => document.getElementById('ag-editor-id').textContent);
    check(was && how === 'kept' && (back.match(/Paragraph \d+ of Prog/g) || []).length === 3000, `left half drawn, it is kept as far as it got: back at once, and drawn on to the whole (${how})`);
    check(back === whole + '\nLeave\n'.replace(/\n/g, '') || (back.match(/Paragraph \d+ of Prog/g) || []).length === 3000, 'the document drawn on after coming back is whole');
  }
  {
    // The top of the window inside a long code block (commands and their output, as a reader's document is): the place
    // is taken by the top-level block, so it is found again - the start of a code block's text says nothing of the line
    // at the top of the window, and an anchor by text was never found there.
    await p.evaluate(() => { window.__typedownProgressiveDelayMs = 0; window.__typedownProgressiveSliceMs = 12 });
    const code = '# Code\n\n' + Array.from({ length: 300 }, (_, i) => `## Step ${i}\n\nRun these on router ${i}:\n\n\`\`\`bash\n` + Array.from({ length: 40 }, (_, k) => `Router${i}(config)# interface gi0/${k}\n ip address 10.${i % 250}.${k}.1 255.255.255.0`).join('\n') + '\n\`\`\`\n\n').join('');
    await load(p, code, 10);
    await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
    await p.evaluate(() => { const blocks = document.querySelectorAll('#ag-editor-id > pre'); const b = blocks[Math.floor(blocks.length * 0.6)]; window.scrollTo(0, window.scrollY + b.getBoundingClientRect().top + 300) });
    await new Promise(r => setTimeout(r, 500));
    const report = await p.evaluate(() => [...window.__msgs].reverse().find(m => m.name === 'OnScroll' && m.args.anchor)?.args);
    const codeAnchor = report && report.anchor;
    check(codeAnchor && typeof codeAnchor.block === 'number' && codeAnchor.top < -100 && /Router\d+\(config\)/.test(codeAnchor.text), `in a long code block, the place is its top-level block (${JSON.stringify(codeAnchor)})`);
    await load(p, long('Other2'), 11);
    await p.evaluate(() => window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear());
    await load(p, code, 12, codeAnchor, report.scrollY);
    const drawn = await p.evaluate(() => window.__typedownLastProgressive);
    await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
    await new Promise(r => setTimeout(r, 500));
    const top = await p.evaluate(b => document.getElementById('ag-editor-id').children[b].getBoundingClientRect().top, codeAnchor.block);
    check(drawn === true, `back in it, it is drawn a part at a time from that block (${drawn})`);
    check(Math.abs(top - codeAnchor.top) <= 2, `the code block is back at its height (${top} vs ${codeAnchor.top})`);
  }
  {
    // The place as the page reports it, over paragraphs: each has an icon drawn before it ("P"), not in the document -
    // the place was never found while its text was read with the icon's.
    await load(p, doc, 13);
    await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
    await p.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight * 0.55));
    await new Promise(r => setTimeout(r, 500));
    const report = await p.evaluate(() => [...window.__msgs].reverse().find(m => m.name === 'OnScroll' && m.args.anchor)?.args);
    const placed = report && report.anchor;
    check(placed && /^Paragraph \d+ of Prog/.test(placed.text), `over paragraphs, the place's text is the document's, not the icon's (${JSON.stringify(placed && placed.text.slice(0, 30))})`);
    await load(p, long('Other3'), 14);
    await p.evaluate(() => window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear());
    await load(p, doc, 15, placed, report.scrollY);
    const drawn = await p.evaluate(() => window.__typedownLastProgressive);
    await p.waitForFunction(() => !window.__typedownMuya?.renderPending, { timeout: 120000 });
    await new Promise(r => setTimeout(r, 500));
    const top = await p.evaluate(b => document.getElementById('ag-editor-id').children[b].getBoundingClientRect().top, placed.block);
    check(drawn === true && Math.abs(top - placed.top) <= 2, `back in it, drawn a part at a time and the paragraph at its height (${drawn}, ${top} vs ${placed.top})`);
  }
  const errors = await p.evaluate(() => (window.__pageErrors || []).length);
  console.log(ok ? 'OK: a long document is drawn from where the reader was, and whole before anything works on it' : 'FAILED');
  await browser.close(); server.close(); process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(2) });
