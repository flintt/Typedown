// The "join the lines of a paragraph in reading mode" setting (Settings > Editor, `joinSoftBreaks`; markdown-linewrap's
// samples): with it, a single line ending inside a paragraph shows as a space in reading mode, as Markdown has it, and as
// nothing between two CJK characters; a hard break (two spaces, a backslash) and a list stay lines. Off, the default,
// and while editing either way, the lines show as they are in the file. The text the editor hands back is the file's.
//
//   node softbreaks-join-check.js
const { openEditor } = require('./harness');

// [what, document, the first block's text in reading mode without the setting, and with it]
const samples = [
  ['a paragraph of three lines', 'Line one\nLine two\nLine three\n', 'Line one\nLine two\nLine three', 'Line one Line two Line three'],
  ['quoted lines', '> Line one\n> Line two\n> Line three\n', 'Line one\nLine two\nLine three', 'Line one Line two Line three'],
  // Hard breaks stay lines. Their marks (the two spaces, the backslash) are in the text shown either way, as before.
  ['lines ended with two spaces', 'Line one  \nLine two  \nLine three\n', 'Line one  \nLine two  \nLine three', 'Line one  \nLine two  \nLine three'],
  ['lines ended with a backslash', 'Line one\\\nLine two\n', 'Line one\\\nLine two', 'Line one\\\nLine two'],
  ['an escaped backslash at a line end', 'Line one\\\\\nLine two\n', 'Line one\\\\\nLine two', 'Line one\\\\ Line two'],
  ['a list', '- Line one\n- Line two\n', 'Line one', 'Line one'],
  ['Chinese lines', '第一行文字\n第二行文字\n', '第一行文字\n第二行文字', '第一行文字第二行文字'],
  ['Chinese then English', '中文一行\nEnglish line\n', '中文一行\nEnglish line', '中文一行 English line'],
  ['Korean lines', '첫째 줄\n둘째 줄\n', '첫째 줄\n둘째 줄', '첫째 줄 둘째 줄'],
];

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const setting = async on => {
    await page.evaluate(on => window.__deliver('SettingsChanged', { joinSoftBreaks: on }), on);
    await new Promise(r => setTimeout(r, 300));
  };
  // The first block's text as the page shows it (innerText follows the CSS: a line ending shown as a space reads as one).
  const shown = async (md, readOnly) => {
    await mode(true, false);
    await load(md);
    await mode(false, readOnly);
    return page.evaluate(() => {
      const first = document.querySelector('#ag-editor-id .ag-paragraph-content');
      return first ? first.innerText.replace(/\n$/, '') : null;
    });
  };
  const lines = s => (s || '').split('\n').length;
  try {
    for (const on of [false, true]) {
      await setting(on);
      const state = on ? 'on' : 'off';
      for (const [what, md, off, joined] of samples) {
        const reading = await shown(md, true);
        const expected = on ? joined : off;
        check(reading === expected, `${state}, reading, ${what}: ${JSON.stringify(reading)}${reading === expected ? '' : ` (expected ${JSON.stringify(expected)})`}`);
        // While editing, the lines as in the file whatever the setting.
        const editing = await shown(md, false);
        check(lines(editing) === lines(off), `${state}, editing, ${what}: ${lines(editing)} line(s) as in the file (${JSON.stringify(editing)})`);
      }
      await mode(false, false);
      await load(samples[0][1]);
      check(await flush() === samples[0][1], `${state}: the text handed back is the file as it was`);
    }
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) {
    console.log(`FAIL: ${failures.length}\n  ` + failures.join('\n  '));
    process.exit(1);
  }
  console.log('PASS: single line endings join in reading mode with the setting, and only there');
})().catch(e => { console.error(e); process.exit(1); });
