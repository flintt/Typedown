// The document rendered as XHTML for another format (RenderXhtml) with mathAsTex: each formula is its TeX, inline in a
// span.math-tex and on its own line in a span.math-tex-display, for a host writing formulas its own way (Word's
// equations). KaTeX's drawing was all there was - its MathML, which holds the TeX, does not pass the sanitizer - and a
// Word export of a^2 read "a2". Without mathAsTex the formulas are KaTeX's, as the other exports want them.
const { openEditor } = require('./harness');

const doc = '# T\n\nInline $a^2 + b < c$ here.\n\n$$\n\\frac{x}{y}\n$$\n\nend\n';

(async () => {
  const { page, mode, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  const render = async (token, mathAsTex) => {
    await page.evaluate(({ token, mathAsTex }) => { delete window.__last.RenderedXhtml; window.__deliver('RenderXhtml', { token, mathAsTex }); }, { token, mathAsTex });
    await page.waitForFunction(() => window.__last.RenderedXhtml, { timeout: 60000 });
    return page.evaluate(() => window.__last.RenderedXhtml);
  };
  try {
    await mode(true, false);
    await load(doc);
    await pause(300);
    const tex = await render(1, true);
    check(!tex.error, `rendered with mathAsTex${tex.error ? ` (error ${tex.error})` : ''}`);
    check(/<span class="math-tex">a\^2 \+ b &lt; c<\/span>/.test(tex.xhtml || ''), 'the inline formula is its TeX, escaped');
    check(/<span class="math-tex-display">\s*\\frac\{x\}\{y\}\s*<\/span>/.test(tex.xhtml || ''), 'the display formula is its TeX');
    check(!/class="katex/.test(tex.xhtml || ''), 'no KaTeX drawing');
    const drawn = await render(2, false);
    check(/class="katex"/.test(drawn.xhtml || '') && !/math-tex/.test(drawn.xhtml || ''), 'without mathAsTex the formulas are KaTeX\'s');
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
