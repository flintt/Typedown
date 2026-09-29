// ApplyDocumentEdit, the page's half of an automation write (docs/automation-api-spec.md, section 2.2): the page
// refuses when its live text is not the base the host checked, otherwise applies the text exactly and replies with
// the source hash and what the first visual edit would do to it. Applying is not an edit: no MarkdownChange.
const crypto = require('crypto');
const { openEditor } = require('./harness');

const sha = text => crypto.createHash('sha256').update(text, 'utf8').digest('hex');

(async () => {
  const { page, errors, mode, flush, load, pause, close } = await openEditor();
  let ok = true;
  const check = (c, label) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}`); ok = ok && c; };
  let op = 0;
  const apply = async (baseText, text) => {
    const operationId = `op${++op}`;
    await page.evaluate(({ operationId, baseContentHash, text }) => {
      delete window.__last.DocumentEditApplied;
      delete window.__last.MarkdownChange;
      window.__deliver('ApplyDocumentEdit', { operationId, baseContentHash, text });
    }, { operationId, baseContentHash: sha(baseText), text });
    await page.waitForFunction(id => window.__last.DocumentEditApplied?.operationId === id, { timeout: 30000 }, operationId);
    await pause(500);
    return page.evaluate(() => ({ reply: window.__last.DocumentEditApplied, change: window.__last.MarkdownChange }));
  };
  try {
    const base = '# Title\n\nplain\n';
    await mode(false, false);
    await load(base);

    let { reply, change } = await apply(base, '# Title\n\nwritten by a program\n');
    check(reply.outcome === 'applied' && reply.sourceHash === sha('# Title\n\nwritten by a program\n'), 'visual mode: applied, the reply hashes the exact text');
    check(reply.normalization.pendingNormalization === 'none' && reply.normalization.normalizedHash === reply.sourceHash, `plain text normalizes to itself (${reply.normalization.pendingNormalization})`);
    check(!change, 'applying reported no MarkdownChange');
    check(await flush() === '# Title\n\nwritten by a program\n', 'flush returns the written text');
    check(await page.evaluate(() => document.querySelector('#ag-editor-id').innerText.includes('written by a program')), 'the page shows it');

    ({ reply } = await apply('# stale base\n', 'never\n'));
    check(reply.outcome === 'conflict', `a stale base is refused (${reply.outcome})`);
    check(await flush() === '# Title\n\nwritten by a program\n', 'nothing changed on conflict');

    // The reader types, and the host has not heard of it yet: the page takes the typing in and refuses.
    const point = await page.evaluate(() => { const w = document.createTreeWalker(document.querySelector('#ag-editor-id'), NodeFilter.SHOW_TEXT); for (let n; (n = w.nextNode());) if (n.data.includes('program')) { const r = document.createRange(); r.selectNodeContents(n); const b = r.getBoundingClientRect(); return { x: b.right - 1, y: (b.top + b.bottom) / 2 }; } });
    await page.mouse.click(point.x, point.y);
    await page.keyboard.press('End');
    await page.keyboard.type('!');
    ({ reply } = await apply('# Title\n\nwritten by a program\n', 'overwrite\n'));
    check(reply.outcome === 'conflict', `typing not yet reported makes the base stale (${reply.outcome})`);
    const typed = await flush();
    check(typed === '# Title\n\nwritten by a program!\n', 'the typing survives');

    ({ reply } = await apply(typed, '|a|b|\n|-|-|\n|1|2|\n'));
    check(reply.outcome === 'applied' && reply.normalization.pendingNormalization === 'unknown', `a formatting difference is unknown (${reply.normalization.pendingNormalization})`);
    check(await flush() === '|a|b|\n|-|-|\n|1|2|\n', 'the source is kept exactly, not the normalized form');

    ({ reply } = await apply('|a|b|\n|-|-|\n|1|2|\n', '```ts {title="x.ts"}\nconst a = 1;\n```\n'));
    check(reply.normalization.pendingNormalization === 'unsafe' && reply.normalization.reasons.includes('fence-lost'), `fence attributes are unsafe (${reply.normalization.reasons})`);

    await mode(true, false);
    const current = await flush();
    ({ reply, change } = await apply(current, '# source mode\n'));
    check(reply.outcome === 'applied' && reply.normalization.pendingNormalization === 'unknown' && reply.normalization.reasons[0] === 'notEvaluated' && reply.normalization.normalizedHash === null,
      `source mode: applied, normalization not evaluated (${JSON.stringify(reply.normalization.reasons)})`);
    check(await page.evaluate(() => document.querySelector('.CodeMirror').CodeMirror.getValue()) === '# source mode\n', 'CodeMirror holds the exact text');
    check(!change, 'source mode: no MarkdownChange');

    await mode(false, true);
    ({ reply } = await apply('# source mode\n', '# reading\n\ntext\n'));
    check(reply.outcome === 'applied' && reply.normalization.pendingNormalization === 'none', `reading mode accepts writes (${reply.outcome}/${reply.normalization.pendingNormalization} ${JSON.stringify(reply.normalization.reasons)})`);
    // A load arriving while an edit is being applied (a tab switch, the host restoring) supersedes it.
    const shown = await flush();
    const superseded = await page.evaluate(({ baseContentHash }) => {
      delete window.__last.DocumentEditApplied;
      window.__deliver('ApplyDocumentEdit', { operationId: 'late', baseContentHash, text: 'never shown\n' });
      window.__deliver('LoadFile', { text: '# other tab\n', loadId: 900, basePath: '/tmp' });
      return window.__last.DocumentEditApplied;
    }, { baseContentHash: sha(shown) });
    check(superseded?.operationId === 'late' && superseded.outcome === 'failed' && superseded.reason === 'superseded', `a load supersedes an edit in flight (${JSON.stringify(superseded)})`);
    await page.waitForFunction(() => window.__last.FileLoaded?.loadId === 900, { timeout: 30000 });
    check(await flush() === '# other tab\n', 'the load wins');
    check(!errors.length, `no page errors${errors.length ? `: ${errors.join(' | ')}` : ''}`);
  } finally {
    await close();
  }
  process.exit(ok ? 0 : 1);
})().catch(e => { console.error(e); process.exit(1); });
