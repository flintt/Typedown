// A picture dropped or pasted goes in as a placeholder ("loading-…") while the host makes it ready, then takes what the
// host made of it (ContentState.settleImagePlaceholder, shared by the drop and the paste - the modularity audit's M08):
// - made ready: the host's address, the file's name as its text;
// - failed: its own address kept, the name as its text - "loading-…" was left in the document for good;
// - the placeholder deleted meanwhile: nothing put back.
//
//   node image-placeholder-check.js
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, flush, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, seen) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + seen}`); if (!ok) failures.push(what); };
  // A placeholder put in at the end of the first paragraph, settled with the host's answer given.
  const settle = (id, answer, removeFirst) => page.evaluate(async ({ id, answer, removeFirst }) => {
    const muya = window.__typedownMuya;
    const cs = muya.contentState;
    const block = cs.blocks[0].children ? cs.blocks[0].children[0] : cs.blocks[0];
    const offset = block.text.length;
    cs.cursor = { start: { key: block.key, offset }, end: { key: block.key, offset } };
    cs.insertImage({ alt: id, src: '/tmp/pictures/cat.png' });
    await new Promise(r => setTimeout(r, 200));
    if (removeFirst) {
      const b = cs.getBlock(block.key);
      b.text = b.text.replace(/!\[[^\]]*\]\([^)]*\)/, '');
      cs.cursor = { start: { key: b.key, offset: 0 }, end: { key: b.key, offset: 0 } };
      cs.render();
      await new Promise(r => setTimeout(r, 100));
    }
    muya.options.imageAction = async () => { if (answer === 'fail') throw new Error('the copy failed'); return answer; };
    await cs.settleImagePlaceholder(id, '/tmp/pictures/cat.png', 'cat');
    muya.dispatchChange();
  }, { id, answer, removeFirst });
  try {
    await mode(false, false);

    await load('A paragraph.\n');
    await pause(800);
    await settle('loading-ok1', 'assets/cat.png', false);
    await pause(400);
    let text = await flush();
    check(text.includes('![cat](assets/cat.png)') && !text.includes('loading-'), 'made ready: the host\'s address, the name as its text', JSON.stringify(text));

    await load('A paragraph.\n');
    await pause(800);
    await settle('loading-fail1', 'fail', false);
    await pause(400);
    text = await flush();
    check(text.includes('![cat](/tmp/pictures/cat.png)') && !text.includes('loading-'), 'failed: its own address kept, no "loading-" left', JSON.stringify(text));

    await load('A paragraph.\n');
    await pause(800);
    await settle('loading-gone1', 'assets/cat.png', true);
    await pause(400);
    text = await flush();
    check(!text.includes('cat'), 'deleted meanwhile: nothing put back', JSON.stringify(text));
    if (errors.length) failures.push(`page errors: ${errors.slice(0, 3).join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
