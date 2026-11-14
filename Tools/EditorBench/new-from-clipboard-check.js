// File > New from Clipboard pastes into the new document it made, in reading mode too (`Paste` with `intoNew`), which
// takes no other paste: the new tab stayed empty there (the user, 2026-11-14). Text goes in as it is, a web page's
// HTML as Markdown, as Ctrl+V pastes them; a paste without `intoNew` in reading mode still changes nothing.
//
//   node new-from-clipboard-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, flush, load, close } = await openEditor();
  const failures = [];
  const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures.push(what); };
  const paste = async arg => {
    await page.evaluate(a => window.__deliver('Paste', a), arg);
    await new Promise(r => setTimeout(r, 500));
  };
  try {
    for (const readOnly of [true, false]) {
      const state = readOnly ? 'reading mode' : 'editing';
      await mode(false, readOnly);
      await load('');
      await paste({ type: 'normal', text: '# From the clipboard\n\n- one\n- two', html: '', intoNew: true });
      let text = await flush();
      check(text.trim() === '# From the clipboard\n\n- one\n- two', `${state}: the clipboard's text in the new document (${JSON.stringify(text)})`);

      await load('');
      await paste({ type: 'normal', text: 'Web bold', html: '<h2>Web</h2><p>a <b>bold</b> word</p>', intoNew: true });
      text = await flush();
      check(/^## Web\n\na \*\*bold\*\* word\s*$/.test(text), `${state}: a web page's HTML as Markdown (${JSON.stringify(text)})`);
    }
    // Reading mode takes no other paste.
    await mode(false, true);
    await load('# Kept\n');
    await paste({ type: 'normal', text: 'should not go in', html: '' });
    const kept = await flush();
    check(kept === '# Kept\n', `reading mode: a paste without intoNew changes nothing (${JSON.stringify(kept)})`);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  if (failures.length) {
    console.log(`FAIL: ${failures.length}\n  ` + failures.join('\n  '));
    process.exit(1);
  }
  console.log('PASS: New from Clipboard fills its new document in reading mode and editing; reading mode takes no other paste');
})().catch(e => { console.error(e); process.exit(1); });
