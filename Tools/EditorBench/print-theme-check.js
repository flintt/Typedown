// Printed (and in the print preview), a document is in the light theme's colours whatever the theme on screen: the
// page is white on paper, and a dark theme's light text, its headings', links' and code's colours went onto it as
// they were - pale on white. Under Dark with Dracula (a custom theme) the print media's colours are the light theme's
// (as the light theme shows them on screen, within a little: the print styles darken the text a shade).
//
//   node print-theme-check.js
const puppeteer = require('puppeteer-core'); const http = require('http'); const fs = require('fs'); const path = require('path');
const statics = path.resolve(process.env.STATICS || '../../Dev/Typedown/Resources/Statics');
const markdown = '# Heading\n\nSome **text** with a [link](https://example.com) and `code`.\n\n> quoted\n\n```js\nconst answer = 42; // a comment\n```\n';
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p === '/') p = '/index.html';
  const f = path.join(statics, p);
  if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.statusCode = 404; return res.end(); }
  fs.createReadStream(f).pipe(res);
});
(async () => {
  await new Promise(r => server.listen(0, r)); const port = server.address().port;
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/opt/google/chrome/chrome', headless: 'new', args: ['--no-sandbox'] });
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  // The page under a theme (and a custom theme's CSS), its colours as lightness 0 (black) to 255 (white).
  const open = async (theme, themeCss) => {
    const page = await browser.newPage(); await page.setViewport({ width: 794, height: 1123 });
    const settings = { fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4, textDirection: 'auto', preferLooseListItem: true, listIndentation: '1', tableAlignColumns: false, readOnly: true, markdown, basePath: '/tmp', loadId: 1, themeCss };
    await page.evaluateOnNewDocument(`(()=>{const ls=[];
      window.__deliver=(n,a)=>ls.forEach(l=>l({data:JSON.stringify({name:n,args:a})}));
      const resp={GetSettings:${JSON.stringify(settings)},GetCurrentTheme:{theme:'${theme}',accentColor:{r:0,g:120,b:212,a:1},background:{R:40,G:42,B:54,A:1}},ContentLoaded:'',GetStringResources:{}};
      window.chrome={webview:{addEventListener:(t,l)=>ls.push(l),dispatchEvent:(e)=>ls.forEach(l=>l(e)),postMessage:(raw)=>{const m=JSON.parse(raw);
        if(m.type==='invoke'){setTimeout(()=>window.__deliver(m.id,{code:0,data:m.name in resp?resp[m.name]:null}),0);}}}}})()`);
    await page.goto(`http://127.0.0.1:${port}/index.html`, { waitUntil: 'load' });
    await page.waitForFunction(() => document.querySelector('#ag-editor-id h1') && document.querySelector('#ag-editor-id .token'), { timeout: 30000 });
    await new Promise(r => setTimeout(r, 1000));
    return page;
  };
  const colours = page => page.evaluate(() => {
    const light = c => { const m = c.match(/[\d.]+/g).map(Number); return Math.round(0.299 * m[0] + 0.587 * m[1] + 0.114 * m[2]); };
    const of = (selector, property = 'color') => { const e = document.querySelector(selector); return e ? light(getComputedStyle(e)[property]) : -1; };
    const tokens = [...document.querySelectorAll('#ag-editor-id pre .token')].map(t => light(getComputedStyle(t).color));
    const tokenColours = [...document.querySelectorAll('#ag-editor-id pre .token')].map(t => getComputedStyle(t).color).join(' ');
    return { text: of('#ag-editor-id p'), heading: of('#ag-editor-id h1'), link: of('#ag-editor-id a'), quote: of('#ag-editor-id blockquote'), token: Math.max(...tokens), tokenColours, page: of('body', 'backgroundColor') };
  });
  try {
    const dracula = fs.readFileSync(path.join(__dirname, '../../Dev/Typedown/Resources/Themes/dracula.css'), 'utf8');
    const reference = await colours(await open('Light', ''));
    const page = await open('Dark', dracula);
    const screen = await colours(page);
    await page.emulateMediaType('print');
    await new Promise(r => setTimeout(r, 500));
    const printed = await colours(page);
    if (process.env.SHOT) await page.screenshot({ path: process.env.SHOT, clip: { x: 0, y: 0, width: 794, height: 420 } });
    console.log('light theme on screen ' + JSON.stringify(reference));
    console.log('Dracula on screen     ' + JSON.stringify(screen));
    console.log('Dracula printed       ' + JSON.stringify(printed));
    check(screen.text > 150, 'on screen the theme is dark (light text): the case this checks');
    check(printed.page > 240, 'printed, the page is white');
    // The text and the headings as the light theme has them (a shade darker: the print styles); links and quotes are
    // the print styles' own (dark, a grey bar); the code's colours exactly the light theme's.
    for (const part of ['text', 'heading'])
      check(Math.abs(printed[part] - reference[part]) <= 30, `printed, the ${part} is as the light theme has it (${printed[part]}, light ${reference[part]})`);
    for (const part of ['link', 'quote'])
      check(printed[part] >= 0 && printed[part] < 140, `printed, the ${part} is dark on the white page (${printed[part]})`);
    check(printed.tokenColours === reference.tokenColours, `printed, the code is in the light theme's colours (${printed.tokenColours === reference.tokenColours ? 'the same' : printed.tokenColours + ' / light ' + reference.tokenColours})`);
  } finally {
    await browser.close(); server.close();
  }
  if (failures.length) { console.log(`\n${failures.length} failed`); process.exit(1); }
  console.log('\nall passed');
})().catch(e => { console.error(e); process.exit(1); });
