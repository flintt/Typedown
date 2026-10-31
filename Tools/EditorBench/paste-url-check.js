// A pasted address goes in at once: a bare URL, and a page whose links show their own address. The paste fetched each
// such address's page for its title and waited for it, up to 1.5 s a link, one after another - here a site that takes
// 3 s to answer, and lets the page read it, so the old paste waited and put the title in. The address is the text now.
const http = require('http');
const { openEditor } = require('./harness');

(async () => {
  const slow = http.createServer((req, res) => setTimeout(() => {
    res.writeHead(200, { 'Content-Type': 'text/html', 'Access-Control-Allow-Origin': '*' });
    res.end('<html><head><title>Slow Page Title</title></head><body>x</body></html>');
  }, 3000));
  await new Promise(r => slow.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${slow.address().port}`;
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  const samples = [
    { name: 'a bare URL', text: `${base}/one`, html: '', want: [`${base}/one`] },
    { name: 'a page with three links showing their own address', text: 'see', html: `<p>See <a href="${base}/a">${base}/a</a>, <a href="${base}/b">${base}/b</a> and <a href="${base}/c">${base}/c</a>.</p>`, want: [`${base}/a`, `${base}/b`, `${base}/c`] },
  ];
  try {
    await mode(false, false);
    for (const s of samples) {
      await load('');
      await pause(300);
      const ms = await page.evaluate(async s => {
        const t0 = performance.now();
        window.__deliver('Paste', { type: 'normal', text: s.text, html: s.html });
        while (performance.now() - t0 < 6000) {
          const md = window.__typedownMuya.getMarkdown();
          if (s.want.every(w => md.includes(w))) return performance.now() - t0;
          await new Promise(r => setTimeout(r, 20));
        }
        return -1;
      }, s);
      const md = await page.evaluate(() => window.__typedownMuya.getMarkdown());
      check(ms >= 0 && ms < 600, `${s.name}: in the document after ${Math.round(ms)} ms (limit 600)`);
      check(!md.includes('Slow Page Title'), `${s.name}: the address is the text, no fetched title (${JSON.stringify(md.trim().slice(0, 120))})`);
    }
  } finally { await close(); slow.close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
