// Markdown structure the visual editor keeps as CommonMark reads it (the audit's C01, rechecked 2026-11-24):
// - a "===" or "---" line after a quote's lazy continuation is part of the quote's paragraph or a break after the quote,
//   never a setext heading in it (CommonMark 0.31 example 93: "> foo\nbar\n===" is one paragraph in a quote);
// - an empty fenced code block stays empty when written out (it gained an empty line).
// Each loaded: its blocks; then written out in full (normalizeMarkdown: read and exported, no minimal change), and that
// read again: the same blocks.
//
//   node structure-c01-check.js
const { openEditor } = require('./harness');

const cases = [
  { id: 'spec93', md: '> foo\nbar\n===\n', shape: ['blockquote[p]'] },
  { id: 'spec93-dash', md: '> foo\nbar\n---\n', shape: ['blockquote[p]', 'hr'] },
  { id: 'quote-then-hr', md: '> Foo\n---\n', shape: ['blockquote[p]', 'hr'] },
  { id: 'heading-in-quote', md: '> foo\n> ===\n', shape: ['blockquote[h1]'] },
  { id: 'setext-outside', md: 'foo\nbar\n===\n', shape: ['h1'] },
  { id: 'empty-fence', md: '```\n```\n\nAfter\n', shape: ['pre', 'p'], out: '```\n```\n\nAfter\n' },
  { id: 'empty-fence-lang', md: '```js\n```\n', shape: ['pre'], out: '```js\n```\n' },
];

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const check = (ok, what, detail) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what} - ${detail}`); if (!ok) failures.push(what); };
  const shape = () => page.evaluate(() => {
    const kind = b => b.type === 'figure' ? (b.functionType || 'figure') : b.type
    return window.__typedownMuya.contentState.blocks.map(b => b.type === 'blockquote' ? `blockquote[${b.children.map(kind).join(',')}]` : kind(b))
  });
  const written = md => page.evaluate(m => window.__typedownMuya.normalizeMarkdown(m), md);
  const text = s => (s.match(/\S.*$/gm) || []).join(' ')
  try {
    await mode(false, false);
    for (const c of cases) {
      await load(c.md);
      await pause(300);
      const got = await shape();
      check(JSON.stringify(got) === JSON.stringify(c.shape), `${c.id}: read as CommonMark does`, JSON.stringify(got));
      const out = (await written(c.md)).replace(/\r\n/g, '\n');
      await load(out);
      await pause(300);
      const again = await shape();
      check(JSON.stringify(again) === JSON.stringify(c.shape), `${c.id}: written out, read the same`, JSON.stringify({ out, again }));
      if (c.out) check(out === c.out, `${c.id}: written out as it was`, JSON.stringify(out));
      else check(text(out).replace(/[>=\-]/g, '').trim().split(/\s+/).join(' ') === text(c.md).replace(/[>=\-]/g, '').trim().split(/\s+/).join(' '), `${c.id}: its words kept`, JSON.stringify(out));
    }
    check(errors.length === 0, 'no page errors', JSON.stringify(errors));
  } finally {
    await close();
  }
  process.exitCode = failures.length ? 1 : 0;
})().catch(e => { console.error(e); process.exitCode = 1 });
