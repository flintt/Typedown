// Chinese in code (a code block, inline code, a math source) is drawn in the text's font - the one chosen in the
// settings, or the system's sans-serif - not the browser's monospace fallback (SimSun, a serif one, on Chinese Windows);
// the Latin letters stay in the monospaced font. Inline code has a little room on either side of its background.
// The fonts actually used for each glyph are the browser's own report (CSS.getPlatformFontsForNode).
//
//   node code-font-fallback-check.js
// Needs a CJK sans-serif and monospace font where it runs that differ (Noto Sans CJK and Noto Sans Mono CJK on Linux).
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  try {
    const cdp = await page.target().createCDPSession();
    await cdp.send('DOM.enable');
    await cdp.send('CSS.enable');
    // The fonts the glyphs of the first element matching `selector` are drawn in: { family: glyph count }.
    const fontsOf = async selector => {
      const { root } = await cdp.send('DOM.getDocument', { depth: -1 });
      const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector });
      if (!nodeId) return null;
      const { fonts } = await cdp.send('CSS.getPlatformFontsForNode', { nodeId });
      return Object.fromEntries(fonts.map(f => [f.familyName, f.glyphCount]));
    };
    const doc = 'Text `行内代码 code` here.\n\n```\nconst 名字 = "中文";\n```\n\n$\\text{公式}x$\n';
    // With no font chosen the browser's default font for the script and the generic family decides: on Windows the
    // sans-serif one is Microsoft YaHei, the monospace one SimSun. Here both may be the same, so what is checked is
    // that sans-serif comes before monospace in code's font list.
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(500);
    for (const [where, selector] of [['code block', 'pre.ag-paragraph .ag-code-content'], ['inline code', 'span code'], ['math source', 'span.ag-math']]) {
      const list = await page.evaluate(s => getComputedStyle(document.querySelector(s)).fontFamily, selector);
      const sans = list.indexOf('sans-serif'), mono = list.lastIndexOf('monospace');
      check(sans >= 0 && mono > sans, `no font chosen: ${where}'s font list has sans-serif before monospace (${list})`);
    }
    const chosen = 'Noto Sans CJK TC';
    for (const [what, fontFamily, cjkFont] of [
      [`"${chosen}" chosen for the text`, `"${chosen}"`, chosen],
    ]) {
      await page.evaluate(f => window.__deliver('SettingsChanged', { fontFamily: f }), fontFamily);
      await mode(true, false);
      await load(doc);
      await mode(false, false);
      await pause(500);
      for (const [where, selector] of [['code block', 'pre.ag-paragraph .ag-code-content'], ['inline code', 'span code'], ['math source', 'span.ag-math .ag-math-text']]) {
        const fonts = await fontsOf(selector);
        if (!fonts) { check(false, `${what}: ${where} found`); continue; }
        const monoCjk = Object.keys(fonts).filter(f => /Mono CJK/.test(f));
        check(fonts[cjkFont] > 0 && !monoCjk.length, `${what}: ${where}'s Chinese in ${cjkFont}, none in a monospace CJK font (${JSON.stringify(fonts)})`);
        if (where !== 'math source') check(Object.keys(fonts).some(f => /Mono/.test(f) && !/CJK/.test(f)), `${what}: ${where}'s Latin letters in a monospaced font`);
      }
    }
    const margin = await page.evaluate(() => { const c = document.querySelector('#ag-editor-id span code'); const s = getComputedStyle(c); return [parseFloat(s.marginLeft), parseFloat(s.marginRight)]; });
    check(margin[0] > 0 && margin[1] > 0, `inline code has room on either side (margin ${margin.join(', ')} px)`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
