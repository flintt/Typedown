// HTML entities in code are code: a fenced or indented block showing how to write &amp; or &ouml; keeps them as
// written, in the visual editor and in what a save writes, as CommonMark has it. The editor used to decode them when it
// made a code block (from MarkText), so `&amp;` became `&` and the next save wrote that, and `&AMP;` became "undefined".
//
//   node code-entities-check.js
const { openEditor } = require('./harness');

const code = 'a &amp; b &ouml; &AMP; &lt;div&gt; &nbsp;';
const doc = `# Start\n\n\`\`\`html\n${code}\n\`\`\`\n\n    ${code}\n\nafter\n`;

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}`); if (!ok) { failures.push(what); if (seen !== undefined) console.log('    ' + JSON.stringify(seen)); } };
  try {
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(300);
    const shown = await page.evaluate(() => [...document.querySelectorAll('#ag-editor-id pre code')].map(c => c.textContent));
    check(shown.length === 2 && shown.every(t => t === 'a &amp; b &ouml; &AMP; &lt;div&gt; &nbsp;'), 'both code blocks show the entities as written', shown);
    check((await flush()) === doc, 'the document is unchanged');
    // An edit in the fenced block: it is written out again from what the editor holds.
    await page.evaluate(() => {
      const state = window.__typedownMuya.contentState;
      const find = b => b.functionType === 'codeContent' ? b : b.children.map(find).find(Boolean);
      const block = find(state.blocks[1]);
      block.text += '!';
      const position = { key: block.key, offset: block.text.length };
      state.cursor = { start: position, end: position, anchor: position, focus: position };
      state.render();
      window.__typedownMuya.dispatchChange();
    });
    await pause(300);
    check((await flush()) === doc.replace(`${code}\n\`\`\``, `${code}!\n\`\`\``), 'after an edit in the block the code is still as written');
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
