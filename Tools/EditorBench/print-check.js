// Printing a code block with a long line. The printed page has no scrollbar to scroll, so the line has to
// wrap; what came out instead was a horizontal scrollbar drawn on paper and the rest of the line cut off
// (also a Store review from 2022, still true in 2026). This asks the editor for the print HTML the way the
// host does, renders it as print media, and checks that no code block is wider than its box.
//
//   node print-check.js
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const longLine = 'const veryLongIdentifier = ' + Array.from({ length: 30 }, (_, i) => `argumentNumber${i}`).join(' + ') + ';';
const markdown = `# Print\n\nSome text.\n\n\`\`\`js\n${longLine}\nshort();\n\`\`\`\n\n\`\`\`\n${longLine}\n\`\`\`\n`;

const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});

(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const page = await browser.newPage(); await page.setViewport({ width: 1000, height: 700 });
  const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, markdown, basePath: '/tmp', loadId: 1 };
  await page.evaluateOnNewDocument(`(()=>{const ls=[];window.__marks={};window.__print=null;
    window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
    const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'Light',accentColor:{r:0,g:120,b:212,a:1},background:{R:249,G:249,B:249,A:1}},ContentLoaded:'',GetStringResources:{}};
    window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);window.__marks[m.name]=1;
      if(m.type==='invoke'&&m.name==='PrintHTML'){window.__print=m.args;}
      if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
  await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 60000 });
  await page.evaluate(() => window.__deliver('Export', { type: 'print', context: 1, basePath: '/tmp', title: 'Print', options: { printOptimization: true } }));
  await page.waitForFunction(() => window.__print, { timeout: 60000 });
  const html = await page.evaluate(() => typeof window.__print === 'string' ? JSON.parse(window.__print).html : window.__print.html);

  const paper = await browser.newPage(); await paper.setViewport({ width: 794, height: 1123 }); // A4 at 96dpi
  await paper.emulateMediaType('print');
  await paper.setContent(html, { waitUntil: 'load' });
  const blocks = await paper.evaluate(() => Array.from(document.querySelectorAll('pre')).map(pre => {
    const code = pre.querySelector('code') || pre;
    return { overflow: pre.scrollWidth - pre.clientWidth, wrap: getComputedStyle(code).whiteSpace, lines: Math.round(code.getBoundingClientRect().height / parseFloat(getComputedStyle(code).lineHeight || '20')) };
  }));
  for (const b of blocks) console.log(`  code block: ${b.overflow > 0 ? `${b.overflow}px wider than its box` : 'fits'}, white-space ${b.wrap}, about ${b.lines} lines`);
  const ok = blocks.length >= 2 && blocks.every(b => b.overflow <= 0);
  console.log(ok ? 'OK: every code block fits the printed page' : 'FAIL: a code block is cut off on paper');
  await browser.close(); server.close(); process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
