// Enter at the very start of a list item's text makes a new, empty item above it - a task item or a bullet, with a
// nested list under it or not. With something under it, the item got an empty line split off inside it instead.
const { openEditor } = require('./harness');

const cases = [
  { name: 'task item with a nested list', doc: '- [ ] first\n  - [ ] child\n- [ ] second\n', want: '- [ ] \n- [ ] first\n  - [ ] child\n- [ ] second\n' },
  { name: 'task item alone', doc: '- [ ] first\n- [ ] second\n', want: '- [ ] \n- [ ] first\n- [ ] second\n' },
  { name: 'a later task item with a nested list', doc: '- [ ] zero\n- [ ] first\n  - [ ] child\n- [ ] second\n', want: '- [ ] zero\n- [ ] \n- [ ] first\n  - [ ] child\n- [ ] second\n' },
  { name: 'bullet item with a nested list', doc: '- first\n  - child\n- second\n', want: '- \n- first\n  - child\n- second\n' },
];

(async () => {
  const { page, mode, load, flush, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  try {
    await mode(false, false);
    for (const { name, doc, want } of cases) {
      await load(doc);
      await pause(500);
      const handle = await page.evaluateHandle(() => [...document.querySelectorAll('#ag-editor-id li')]
        .find(li => li.textContent.trim().startsWith('first')).querySelector('.ag-paragraph'));
      await handle.asElement().click();
      await page.keyboard.press('Home');
      await pause(200);
      await page.keyboard.press('Enter');
      await pause(400);
      const md = await flush();
      check(md === want, `${name}: ${JSON.stringify(md)}`);
    }
  } finally { await close(); }
  console.log(ok ? '\nPASS' : '\nFAIL');
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error('check failed:', e.message); process.exit(1); });
