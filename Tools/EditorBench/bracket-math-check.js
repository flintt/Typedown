// The "\( \) and \[ \] as formula delimiters" setting (Settings > Editor, `bracketMath`): with it, LaTeX's \( ... \) is
// an inline formula and \[ ... \] (its own lines, or a whole line) a formula block, in the editor and in the HTML the
// exports render; the text handed back is the file's, delimiters as written. Off, the default: \( and \[ are escaped
// brackets, as CommonMark has them, and $ / $$ are formulas as before. Turning it on or off takes the shown document
// in again, the text unchanged.
//
//   node bracket-math-check.js
const { openEditor } = require('./harness');

const doc = [
  '# Bracket math',
  '',
  'Inline \\(E = mc^2\\) and dollars $a^2$ here.',
  '',
  '\\[',
  '\\int_0^1 x \\, \\mathrm{d}x',
  '\\]',
  '',
  '\\[ \\frac{a_{\\nu + 2}}{a_\\nu} \\xrightarrow[\\nu \\rightarrow \\infty]{} \\frac{2}{\\nu} \\]',
  '',
  '$$',
  'D = D_0',
  '$$',
  '',
  'Escaped \\\\(not math\\\\) stays text.',
  ''
].join('\n');

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const setting = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { bracketMath: on }), on);
    await new Promise(r => setTimeout(r, 400));
  };
  const look = () => page.evaluate(() => {
    const root = document.querySelector('#ag-editor-id');
    const paragraphs = [...root.querySelectorAll('.ag-paragraph-content')].map(p => p.textContent);
    return {
      inlineFormulas: root.querySelectorAll('.ag-inline-math .katex, span.ag-math .katex').length,
      blocks: [...root.querySelectorAll('figure[data-role="MULTIPLEMATH"]')].map(f => f.getAttribute('data-mathstyle') || ''),
      blockErrors: root.querySelectorAll('.ag-math-error, .ag-multiple-math-block .ag-math-error').length,
      paragraphs,
    };
  });
  const exported = () => page.evaluate(async () => {
    const muya = window.__typedownMuya;
    muya.markdown = muya.getMarkdown();
    const html = await muya.exportHtml();
    const holder = document.createElement('div');
    holder.innerHTML = html;
    return {
      katex: holder.querySelectorAll('.katex').length,
      display: holder.querySelectorAll('.katex-display').length,
      literal: holder.textContent.includes('\\(E = mc^2\\)') || holder.textContent.includes('(E = mc^2)'),
    };
  });
  try {
    await mode(false, false);
    for (const on of [false, true, false]) {
      await setting(on);
      await load(doc);
      const state = on ? 'on' : 'off';
      const seen = await look();
      if (on) {
        // \(E = mc^2\) and $a^2$; the two \[ \] blocks and the $$ one.
        check(seen.inlineFormulas === 2, `${state}: two inline formulas, \\( \\) and $ (${seen.inlineFormulas})`);
        check(JSON.stringify(seen.blocks) === JSON.stringify(['bracket', 'bracketline', '']), `${state}: three formula blocks, \\[ \\] on lines of their own, a whole line \\[ \\], and $$ (${JSON.stringify(seen.blocks)})`);
        check(seen.paragraphs.some(p => p.includes('\\\\(not math\\\\)')), `${state}: an escaped backslash before ( stays text`);
      } else {
        check(seen.inlineFormulas === 1, `${state}: only the $ inline formula (${seen.inlineFormulas})`);
        check(JSON.stringify(seen.blocks) === JSON.stringify(['']), `${state}: only the $$ block (${JSON.stringify(seen.blocks)})`);
      }
      check(seen.blockErrors === 0, `${state}: no formula drawn as invalid (${seen.blockErrors})`);
      const back = await flush();
      check(back === doc, `${state}: the text handed back is the file as it was${back === doc ? '' : `:\n${back}`}`);
      const html = await exported();
      check(on ? html.katex >= 5 && html.display === 3 : html.katex === 2 && html.display === 1,
        `${state}: the export draws ${on ? 'five formulas, three of them blocks' : 'the $ and $$ formulas only'} (${html.katex} formulas, ${html.display} blocks)`);
      check(on ? !html.literal : html.literal, `${state}: \\(E = mc^2\\) in the export ${on ? 'is a formula' : 'is text'}`);
    }
    // Turned on with the document shown: taken in again, the blocks there at once, the text unchanged.
    await load(doc);
    await setting(true);
    const switched = await look();
    check(switched.blocks.length === 3 && switched.inlineFormulas === 2, `turned on with the document shown: its formulas drawn (${switched.blocks.length} blocks, ${switched.inlineFormulas} inline)`);
    check(await flush() === doc, 'turned on with the document shown: the text unchanged');
    await setting(false);
    const back = await look();
    check(back.blocks.length === 1 && back.inlineFormulas === 1, `turned off again: only the $ formulas (${back.blocks.length} blocks, ${back.inlineFormulas} inline)`);
    check(await flush() === doc, 'turned off again: the text unchanged');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) {
    console.log(`FAIL: ${failures.length}\n  ` + failures.join('\n  '));
    process.exit(1);
  }
  console.log('PASS: \\( \\) and \\[ \\] are formulas with the setting, written back as they came, and text without it');
})().catch(e => { console.error(e); process.exit(1); });
