// A plain text file laid out as prose is in the body text's font and the preset's paragraph rules, as the visual editor's
// paragraphs are (the user's list, 2026-11-11, 6): it was drawn in the source theme's code font whatever was set, and
// a preset's first-line indent, justification and letter spacing did nothing there. The preset's rule for it is the
// one TypographyDesign.FieldsCss writes (".plain-prose .CodeMirror-line"). With the first line indented, the caret at a
// line's start is drawn where its text starts, and a click there puts it there.
//
//   node plain-typography-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${seen === undefined ? '' : ': ' + seen}`); if (!ok) failures.push(what); };
  const read = sel => page.evaluate(sel => {
    const e = document.querySelector(sel); if (!e) return null;
    const s = getComputedStyle(e);
    return { font: s.fontFamily, weight: s.fontWeight, size: s.fontSize, spacing: s.letterSpacing, indent: s.textIndent, align: s.textAlign };
  }, sel);
  try {
    const css = '#ag-editor-id > p.ag-paragraph, .markdown-body > p { text-indent: 2em; text-align: justify; letter-spacing: 0.05em; }\n'
      + '.plain-prose .CodeMirror-line { text-indent: 2em; text-align: justify; letter-spacing: 0.05em; }';
    await page.evaluate(css => window.__deliver('SettingsChanged', { fontFamily: 'Georgia', fontSize: 20, lineHeight: 2, editionCss: css }), css);
    await mode(false, false);
    const prose = 'The first paragraph of a plain text file, long enough to wrap onto a second line in the editor at its width so that the indent is seen on the first line only.';
    await load(`${prose}\nThe second paragraph.\n`);
    await pause(500);
    const visual = await read('#ag-editor-id > p.ag-paragraph');
    await page.evaluate(() => window.__deliver('SettingsChanged', { sourceCode: true, plainTextShown: true, plainTextProse: true }));
    await page.waitForSelector('.plain-prose .CodeMirror-line');
    await pause(800);
    const plain = await read('.plain-prose .CodeMirror-line');
    console.log('  visual paragraph: ' + JSON.stringify(visual));
    console.log('  plain text line:  ' + JSON.stringify(plain));
    // The family asked for; what follows it in the list (fallbacks) is each one's own.
    const family = f => (f || '').split(',')[0].trim().replace(/^["']|["']$/g, '');
    check(plain && family(plain.font) === family(visual.font) && family(plain.font) === 'Georgia', 'the plain text is in the body text font', plain?.font);
    check(plain && plain.weight === visual.weight, 'and its weight', plain?.weight);
    check(plain && plain.size === visual.size, 'and its size', plain?.size);
    check(plain && plain.indent === visual.indent && plain.align === visual.align && plain.spacing === visual.spacing, "the preset's indent, justification and letter spacing reach its lines");

    // The caret at the start of the first line: where the text starts, after the indent.
    const caret = await page.evaluate(() => {
      const cm = document.querySelector('.CodeMirror').CodeMirror;
      cm.focus();
      cm.setCursor({ line: 0, ch: 0 });
      const at = cm.cursorCoords({ line: 0, ch: 0 }, 'window');
      const range = document.createRange();
      const text = document.querySelector('.CodeMirror-line span').firstChild;
      range.setStart(text, 0); range.setEnd(text, 1);
      const letter = range.getBoundingClientRect();
      const second = cm.cursorCoords({ line: 0, ch: 1 }, 'window');
      return { caret: at.left, letter: letter.left, second: second.left, back: cm.coordsChar({ left: letter.left + 1, top: (letter.top + letter.bottom) / 2 }, 'window') };
    });
    console.log('  caret ' + JSON.stringify(caret));
    check(Math.abs(caret.caret - caret.letter) < 2, 'the caret at the start of an indented line is drawn where its text starts', `${caret.caret} vs ${caret.letter}`);
    check(caret.back.line === 0 && caret.back.ch <= 1, 'a click on the first letter puts the caret there', JSON.stringify(caret.back));

    // Another preset chosen while the plain text file is shown: its font, size and paragraph rules at once (the user,
    // 2026-11-12, 20: the font stayed the old one until a Markdown tab was shown - only the visual editor set it).
    await page.evaluate(() => window.__deliver('SettingsChanged', { fontFamily: 'Verdana', fontSize: 24, lineHeight: 2,
      editionCss: '.plain-prose .CodeMirror-line { text-indent: 3em; text-align: left; letter-spacing: normal; }' }));
    await pause(600);
    const changed = await read('.plain-prose .CodeMirror-line');
    console.log('  after another preset: ' + JSON.stringify(changed));
    check(changed && family(changed.font) === 'Verdana' && changed.size === '24px' && changed.indent === '72px',
      'another preset chosen on the plain text tab: its font, size and indent at once', JSON.stringify(changed));
    if (errors.length) failures.push(`page errors: ${errors.slice(0, 3).join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
