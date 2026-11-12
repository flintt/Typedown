// mermaid.min.js (3.5 MB) loads only when a document draws a diagram (public/lazy-mermaid.js): a document without one
// never requests it, a document with one requests it exactly once and the diagram is drawn. Loading it from
// index.html made every start parse it first.
const assert = require('assert/strict');
// The page served and the browser launched as every check does (harness); the host stubbed here, before the page starts.
const { serveStatics, launchBrowser } = require('./harness');

(async () => {
  const server = await serveStatics();
  const browser = await launchBrowser();
  async function open(markdown) {
    const page = await browser.newPage();
    const requests = [];
    page.on('request', request => { if (request.url().endsWith('/mermaid.min.js')) requests.push(request.url()); });
    const settings = {
      fontSize: 16, lineHeight: 1.6, editorAreaWidth: '900px', tabSize: 4,
      textDirection: 'auto', preferLooseListItem: true, listIndentation: '1',
      tableAlignColumns: false, readOnly: false, markdown, basePath: '/tmp', loadId: 1
    };
    await page.evaluateOnNewDocument(settings => {
      const listeners = [];
      const deliver = (name, args) => listeners.forEach(listener => listener({ data: JSON.stringify({ name, args }) }));
      const replies = {
        GetSettings: settings,
        GetCurrentTheme: { theme: 'Light', accentColor: { r: 0, g: 120, b: 212, a: 1 }, background: { R: 249, G: 249, B: 249, A: 1 } },
        ContentLoaded: '', GetStringResources: {}
      };
      window.__marks = {};
      window.chrome = { webview: {
        addEventListener: (_type, listener) => listeners.push(listener),
        postMessage: raw => {
          const message = JSON.parse(raw);
          window.__marks[message.name] = true;
          if (message.type === 'invoke') setTimeout(() => deliver(message.id, { code: 0, data: replies[message.name] ?? null }), 0);
        }
      } };
    }, settings);
    await page.goto(server.url, { waitUntil: 'load' });
    await page.waitForFunction(() => window.__marks.FileLoaded, { timeout: 30000 });
    return { page, requests };
  }

  try {
    const plain = await open('# Plain\n\nNo diagram here.\n\n```js\nconst a = 1\n```\n');
    await new Promise(resolve => setTimeout(resolve, 1500));
    assert.equal(plain.requests.length, 0, `a document without a diagram requested mermaid.min.js (${plain.requests.length})`);
    // Still the loader's accessor: nothing has looked mermaid up (reading window.mermaid here would load it).
    assert.equal(await plain.page.evaluate(() => typeof Object.getOwnPropertyDescriptor(window, 'mermaid').get), 'function');
    console.log('PASS: no diagram, mermaid.min.js not loaded');

    const diagram = await open('# Diagram\n\n```mermaid\ngraph LR\n  A[Alpha] --> B[Beta]\n```\n');
    await diagram.page.waitForFunction(() => document.querySelector('.ag-container-preview svg'), { timeout: 30000 });
    const labels = await diagram.page.evaluate(() => document.querySelector('.ag-container-preview svg').textContent);
    assert.ok(labels.includes('Alpha') && labels.includes('Beta'), `the diagram is drawn (${labels})`);
    assert.equal(diagram.requests.length, 1, `mermaid.min.js requested once (${diagram.requests.length})`);
    assert.equal(await diagram.page.evaluate(() => typeof window.mermaid.render), 'function');
    console.log('PASS: a diagram loads mermaid.min.js once and is drawn');
  } finally {
    await browser.close();
    server.close();
  }
})().catch(error => { console.error(error); process.exit(1); });
