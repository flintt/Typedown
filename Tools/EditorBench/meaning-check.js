// Does a save keep the meaning? Users open the same .md files in GitHub, VS Code, Obsidian and pandoc, so what the
// editor WRITES must mean, to a standard Markdown reader, what the file meant before - even where the editor itself
// shows it differently. Muya need not be a CommonMark renderer; its output must not change what other tools read.
//
// For every input the markdown is imported (full render, the same path a file load takes) and exported again - what
// a save writes - and that export is imported and exported a second time (a second save). The input and both
// exports are rendered in Node by a reference parser, markdown-it in CommonMark mode plus the GFM extensions
// (tables, ~~strikethrough~~, task list markers) and footnotes; YAML front matter is split off and compared as text.
// Equal HTML (after normalising whitespace between block tags and attribute order) = the save kept the meaning.
//
// A soft line break and a space render the same, so an export that only reflows a paragraph counts as kept, but is
// reported apart ("reflowed"). Line breaks inside <pre>, and hard breaks, are never folded.
//
// Inputs: the 652 CommonMark 0.31.2 examples (spec-data.json; ids spec-N, section = the spec's), the everyday
// documents below (ids doc-*, section "curated"), and the round-trip fixtures other checks use (ids fixture-*,
// section "fixture": harness.js's hand-written source fixtures, style-fixture.md, showcase.md, doc300k.md).
//
//   node meaning-check.js                  # compare with meaning-known.json; exit 1 on a regression
//   node meaning-check.js --update         # record the current set of failures (merged for a selection)
//   node meaning-check.js --section Lists  # only inputs whose section contains this (curated, fixture, spec, ...)
//   node meaning-check.js 42 190 doc-table # spec example numbers and/or id substrings
//   node meaning-check.js --verbose        # print every failure (input, export, HTML diff), not only regressions
//   node meaning-check.js --json out.json  # also write all results to a file
//
// A regression is an input recorded as keeping its meaning that now changes it (or a new input that fails); newly
// fixed inputs are printed as notes. Run with STATICS=<built Statics dir> and CHROME=<chrome binary> as the others.
const fs = require('fs');
const path = require('path');
const MarkdownIt = require('markdown-it');
const footnote = require('markdown-it-footnote');
const { openEditor, sourceFixtures } = require('./harness');

const knownPath = path.join(__dirname, 'meaning-known.json');
const args = process.argv.slice(2);
const flag = f => args.includes(f);
const value = f => { const i = args.indexOf(f); return i >= 0 ? args[i + 1] : null; };
const update = flag('--update'), verbose = flag('--verbose');
const section = value('--section'), jsonOut = value('--json');
const selectors = args.filter((a, i) => !a.startsWith('--') && !['--section', '--json'].includes(args[i - 1]));

// ---------------------------------------------------------------------------------------------------------------
// Everyday documents: what users write, aimed at the places an editor that re-serialises tends to slip.
const curated = [
  // lists
  ['ordered-nested-bullet', '1. a\n   - b\n   - c\n2. d\n'],
  ['ordered-nested-ordered', '1. one\n   1. one.one\n   2. one.two\n2. two\n'],
  ['ordered-ten-marker', '9. nine\n10. ten\n    - nested under ten\n11. eleven\n'],
  ['ordered-ten-start', '10. x\n11. y\n    continuation of y\n'],
  ['ordered-start-number', '3. three\n4. four\n'],
  ['ordered-paren', '1) a\n2) b\n   - c\n'],
  ['bullet-nested-three', '- a\n  - b\n    - c\n      - d\n- e\n'],
  ['bullet-markers-mixed', '* star\n* star2\n\n+ plus\n+ plus2\n\n- dash\n'],
  ['loose-two-paragraphs', '- one\n\n  two\n- three\n'],
  ['loose-list', '- a\n\n- b\n\n- c\n'],
  ['tight-list-then-para', '- a\n- b\n\nparagraph after\n'],
  ['task-list-nested', '- [ ] todo\n- [x] done\n  - [ ] nested todo\n  - [X] nested done upper\n- plain item\n'],
  ['task-ordered', '1. [ ] first\n2. [x] second\n'],
  ['list-with-code', '1. step one:\n\n   ```bash\n   npm install\n   ```\n\n2. step two\n'],
  ['list-with-quote', '- item\n\n  > quoted inside\n'],
  ['list-item-hard-break', '- line one  \n  line two\n'],
  ['list-then-code-indented', '- item\n\n<!-- end list -->\n\n    code after list\n'],
  ['list-star-in-text', '- 2 * 3 = 6\n- a * b\n'],
  ['list-all-ones', '1. a\n1. b\n1. c\n'],
  ['list-start-zero', '0. zero\n1. one\n'],
  ['list-tab-nested', '- a\n\t- b\n\t\t- c\n- d\n'],
  ['list-four-space-nested', '- a\n    - b\n        - c\n'],
  ['ordered-four-space-nested', '1. a\n    - b\n2. c\n'],
  ['ordered-two-space-not-nested', '1. a\n  - b\n2. c\n'],
  ['list-lazy-continuation', '- item one\ncontinued lazily\n- item two\n'],
  ['list-then-para-no-blank', '- a\n- b\nnot a new paragraph\n'],
  ['list-item-empty-fence', '- a\n\n  ```\n  ```\n- b\n'],
  ['para-then-fence-no-blank', 'Run this:\n```sh\nmake\n```\nthen that.\n'],
  ['para-then-list-no-blank', 'Shopping:\n- milk\n- eggs\n'],
  ['para-then-ordered-not-one', 'The year\n2024. was good\n'],
  ['heading-then-list-no-blank', '## Todo\n- a\n- b\n'],
  ['para-then-table-no-blank', 'Results:\n| a | b |\n|---|---|\n| 1 | 2 |\n'],
  ['table-then-para-no-blank', '| a | b |\n|---|---|\n| 1 | 2 |\nafter table\n'],
  ['table-html-br', '| a | b |\n|---|---|\n| line<br>two | x |\n'],
  ['table-escaped-pipe-text', '| a | b |\n|---|---|\n| x \\| y | z |\n'],
  ['empty-fence', '```\n```\n\ntext\n'],
  ['bare-url', 'Visit https://example.com/path?q=1 today.\n'],
  ['html-then-markdown-no-blank', '<div>\n*not emphasis inside html block*\n</div>\n'],
  ['multiline-setext', 'Line one\nline two\n===\n'],
  // tables
  ['table-align', '| Left | Center | Right | None |\n|:-----|:------:|------:|------|\n| a | b | c | d |\n| 1 | 2 | 3 | 4 |\n'],
  ['table-code-pipe', '| Op | Meaning |\n|----|---------|\n| `a \\| b` | or |\n| `x` | \\| literal |\n'],
  ['table-inline-marks', '| a | b |\n|---|---|\n| **bold** | *em* |\n| [l](http://x.y) | ~~del~~ |\n'],
  ['table-empty-cells', '| a | b | c |\n|---|---|---|\n|   | x |   |\n| y |   |   |\n'],
  ['table-no-outer-pipes', 'a | b\n--|--\n1 | 2\n'],
  ['table-cjk', '| 名称 | 说明 |\n|:--|--:|\n| 编辑器 | 支持**粗体** |\n'],
  // code
  ['fence-contains-backticks', '````markdown\n```js\ncode\n```\n````\n'],
  ['fence-contains-tildes', '~~~~\n~~~\ninner\n~~~\n~~~~\n'],
  ['fence-tilde-contains-backticks', '~~~\n```\nnot a fence\n```\n~~~\n'],
  ['fence-info-string', '```python title="a.py"\nprint(1)\n```\n'],
  ['fence-empty-lines', '```\nline1\n\n\nline4\n```\n'],
  ['fence-unclosed-at-end', 'text\n\n```\nunclosed\n'],
  ['indented-code', 'para\n\n    indented code\n    more\n\nafter\n'],
  ['inline-code-marks', 'Use `**not bold**`, `~~not struck~~` and `==not marked==` and `a_b_c`.\n'],
  ['inline-code-backticks', 'Write `` ` `` or ``` `` ``` or `` `code` ``.\n'],
  ['inline-code-spaces', 'Spaces: ` a ` and `  b  ` and `` ` ``.\n'],
  // headings
  ['heading-trailing-hashes', '# Title #\n\n## Sub ##########\n\n### Ends with C# #\n\n#### Not closed#\n'],
  ['heading-setext', 'Title\n=====\n\nSub\n---\n'],
  ['heading-marks', '## A **bold** `code` [link](u) heading\n'],
  ['not-heading', '#hashtag and #5\n\n\\# escaped\n'],
  // block quotes
  ['quote-list-code', '> Note:\n>\n> - one\n> - two\n>\n> ```js\n> x = 1\n> ```\n'],
  ['quote-nested', '> outer\n>\n> > inner\n>\n> back\n'],
  ['quote-lazy', '> line one\nlazy continuation\n'],
  ['quote-heading', '> ## Heading in quote\n> text\n'],
  // links and images
  ['link-title', '[site](https://example.com "The Title") and [b](https://e.com \'single\') and [c](https://e.com (paren))\n'],
  ['link-angle-spaces', '[doc](<my docs/a file.md>) and ![img](<images/icon with spaces.png> "t")\n'],
  ['link-reference', 'See [the docs][docs] and [Docs] and [docs][].\n\n[docs]: https://example.com/docs "Docs Title"\n'],
  ['link-parens-in-url', '[wiki](https://en.wikipedia.org/wiki/Foo_(bar)) and [q](http://x.y/?a=1&b=2)\n'],
  ['autolink', '<https://example.com/a_b> and <mail@example.com>\n'],
  ['image-basic', '![alt text](./img/a.png) ![](b.png "only title")\n'],
  ['link-with-marks', '[**bold** and `code`](http://x.y)\n'],
  // html
  ['html-block-div', '<div align="center">\n  <img src="a.png" width="100">\n</div>\n\nafter\n'],
  ['html-details', '<details>\n<summary>Click</summary>\n\nHidden **markdown**.\n\n</details>\n'],
  ['html-comment', 'before\n\n<!-- a comment\nover lines -->\n\nafter\n'],
  ['html-inline', 'Press <kbd>Ctrl</kbd>+<kbd>S</kbd> and <br> break and <span style="color:red">red</span>.\n'],
  // footnotes, front matter
  ['footnotes', 'Text[^1] and more[^note].\n\n[^1]: First note.\n[^note]: Second, with `code` and **bold**.\n'],
  ['front-matter', '---\ntitle: 标题\ntags: [a, b]\ndate: 2024-01-01\n---\n\n# Body\n\ntext\n'],
  // breaks
  ['hard-break-spaces', 'line one  \nline two\n'],
  ['hard-break-backslash', 'line one\\\nline two\n'],
  ['soft-break', 'line one\nline two\nline three\n'],
  ['thematic-breaks', 'a\n\n***\n\nb\n\n___\n\nc\n\n- - -\n'],
  // emphasis
  ['emph-triple', '***a*** and ***b** c* and *a **b***\n'],
  ['emph-code-inside-strong', '**a `**` b** and *x `*` y*\n'],
  ['emph-underscore', '_em_ __strong__ snake_case_word __a__b\n'],
  ['emph-intraword', 'un*frigging*believable and foo**bar**baz\n'],
  ['strike', '~~struck~~ and ~~**bold struck**~~ and ~one tilde~\n'],
  ['emph-punctuation', '**"quoted"** and *(paren)* and **a**.\n'],
  // escapes
  ['escapes', '\\*not em\\* \\_x\\_ \\# \\[x\\] \\`y\\` 1\\. not a list \\| \\\\ back\n'],
  ['escape-list-start', '1\\. not a list\n\n\\- not a bullet\n\n\\+ nor this\n'],
  ['literal-specials', 'Price: 5 * 3 = 15, a_b, 2 < 3 > 1, & and #1, [brackets] and (parens).\n'],
  ['entities', '&copy; &amp; &lt;tag&gt; &#35; &nbsp;x\n'],
  ['dollar-text', 'It costs $5 and $10.\n'],
  // CJK
  ['cjk-marks', '这是**粗体**文字，这是*斜体*，还有`代码`和[链接](http://x.y)。\n'],
  ['cjk-emph-punct', '**中文：**后面的文字和**“引号”**测试。\n'],
  ['cjk-list', '1. 第一项\n   - 子项**加粗**\n2. 第二项\n'],
  ['cjk-heading-para', '# 标题\n\n第一段\n第二行\n\n> 引用中文\n'],
  // assorted documents
  ['readme-like', '# Project\n\n[![badge](https://img.shields.io/x.svg)](https://ci.example.com)\n\n## Install\n\n```sh\nnpm i project\n```\n\n## Usage\n\n1. Import:\n   ```js\n   import p from "project"\n   ```\n2. Call `p()`.\n\n| Option | Default |\n|---|---|\n| `a` | `1` |\n\nMIT © Someone\n'],
  ['notes-like', '## 2024-05-01\n\n- [x] Call Bob\n- [ ] Write report\n  - outline\n  - draft\n\n> Remember: **deadline** Friday.\n\n---\n\nIdeas:\n* one\n* two\n'],
  ['math', 'Inline $a_b * c_d$ and\n\n$$\nx_1 * y_2\n$$\n'],
];

// ---------------------------------------------------------------------------------------------------------------
// Inputs
const inputs = [];
for (const e of require('./spec-data.json')) inputs.push({ id: `spec-${e.n}`, n: e.n, section: e.section, kind: 'spec', markdown: e.markdown });
for (const [name, md] of curated) inputs.push({ id: `doc-${name}`, section: 'curated', kind: 'doc', markdown: md });
for (const [name, md] of sourceFixtures(false)) if (!name.startsWith('commonmark-') && name !== 'large-doc') inputs.push({ id: `fixture-${name}`, section: 'fixture', kind: 'fixture', markdown: md });
for (const [name, file] of [['style-fixture', 'style-fixture.md'], ['showcase', 'fixtures/showcase/showcase.md'], ['doc300k', 'doc300k.md']])
  inputs.push({ id: `fixture-${name}`, section: 'fixture', kind: 'fixture', markdown: fs.readFileSync(path.join(__dirname, file), 'utf8') });

let selected = inputs;
if (section) selected = selected.filter(i => i.section.toLowerCase().includes(section.toLowerCase()) || i.kind === section.toLowerCase());
if (selectors.length) selected = selected.filter(i => selectors.some(s => /^\d+$/.test(s) ? i.n === Number(s) : i.id.includes(s)));

// ---------------------------------------------------------------------------------------------------------------
// Reference reader
// linkify: GFM links bare http(s):// URLs; fuzzy (scheme-less) links and e-mails stay off, closer to GFM than not.
const md = MarkdownIt('commonmark', { html: true, linkify: true }).enable(['table', 'strikethrough', 'linkify']).use(footnote);
md.linkify.set({ fuzzyLink: false, fuzzyEmail: false });

/** Leading YAML front matter (GitHub, Obsidian, pandoc and VS Code all treat it as metadata, not text). The spec
 *  examples are read as plain CommonMark, so they never have any. */
function splitFrontMatter(src, allow) {
  if (!allow) return { front: null, body: src };
  const m = /^---[ \t]*\n([\s\S]*?\n)?(?:---|\.\.\.)[ \t]*(?:\n|$)/.exec(src);
  return m ? { front: (m[1] || '').replace(/[ \t]+$/gm, '').replace(/\n+$/, ''), body: src.slice(m[0].length) } : { front: null, body: src };
}

const BLOCK = 'address|article|aside|blockquote|body|caption|col|colgroup|dd|details|dialog|div|dl|dt|fieldset|figcaption|figure|footer|form|h[1-6]|head|header|hr|html|iframe|legend|li|link|main|menu|nav|ol|optgroup|option|p|pre|section|summary|table|tbody|td|tfoot|th|thead|title|tr|ul|section';
const blockTag = new RegExp(`^</?(?:${BLOCK})\\b`, 'i');

/** HTML a reader sees, in a canonical form: whitespace between block tags dropped, attributes sorted, GFM task
 *  markers turned into a marker element. Returns { strict, folded } - folded also treats a soft line break as a
 *  space (outside <pre>), which is how every renderer shows it. */
function render(src, allowFront) {
  const { front, body } = splitFrontMatter(src, allowFront);
  let html = md.render(body);
  // GFM task list items: "[ ]" / "[x]" / "[X]" + whitespace at the start of a list item (or of its first paragraph).
  html = html.replace(/<li>(\s*<p>)?\[([ xX])\](?=\s)\s*/g, (_, p, c) => `<li><task ${c === ' ' ? 'open' : 'done'}/>${p || ''}`);
  // Character references in raw HTML pass through and the browser decodes them: write each as its character (the
  // ones markdown-it itself emits - &amp; &lt; &gt; &quot; - stay, so "&amp;ouml;" is still not "ö").
  html = html.replace(/&(?:#\d+|#[xX][0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);/g, ent => ['&amp;', '&lt;', '&gt;', '&quot;'].includes(ent) ? ent : md.utils.unescapeAll(ent).replace(/[&<>"]/g, c => md.utils.escapeHtml(c)));
  // Sort attributes of every opening tag.
  html = html.replace(/<([a-zA-Z][\w-]*)((?:\s+[^\s"'>\/=]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?)+)\s*(\/?)>/g, (_, tag, attrs, slash) => {
    const list = attrs.match(/[^\s"'>\/=]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?/g) || [];
    return `<${tag} ${list.map(a => a.replace(/\s*=\s*/, '=')).sort().join(' ')}${slash ? ' /' : ''}>`;
  });
  // Whitespace next to block tags carries no meaning; elsewhere keep it.
  const parts = html.split(/(<[^>]+>)/);
  let out = '', inPre = 0;
  for (let i = 0; i < parts.length; i++) {
    let s = parts[i];
    if (i % 2) { if (/^<pre\b/i.test(s)) inPre++; else if (/^<\/pre>/i.test(s)) inPre--; out += s; continue; }
    if (!inPre) {
      const prevBlock = i === 0 || blockTag.test(parts[i - 1] || '');
      const nextBlock = i === parts.length - 1 || blockTag.test(parts[i + 1] || '');
      if (prevBlock) s = s.replace(/^\s+/, '');
      if (nextBlock) s = s.replace(/\s+$/, '');
    }
    out += s;
  }
  const strict = (front !== null ? `<frontmatter>${front}</frontmatter>` : '') + out;
  // folded: soft breaks (a newline in text outside <pre>, not after a <br>) count as a space
  let folded = '', pre = 0;
  for (const [i, s] of strict.split(/(<[^>]+>)/).entries()) {
    if (i % 2) { if (/^<pre\b/i.test(s)) pre++; else if (/^<\/pre>/i.test(s)) pre--; folded += s; continue; }
    folded += pre ? s : s.replace(/[ \t]*\n[ \t]*/g, ' ');
  }
  return { strict, folded };
}

/** Kept / reflowed / changed between the reading of the input and the reading of an export. */
function compare(a, b) { return a.strict === b.strict ? 'kept' : a.folded === b.folded ? 'reflowed' : 'changed'; }

/** The differing hunks (at most `hunks`, each cut to `max` lines a side) between two canonical HTMLs, split at
 *  block tags. */
function htmlDiff(a, b, hunks = 3, max = 6) {
  const lines = s => s.replace(new RegExp(`(<(?:${BLOCK}|task|frontmatter)\\b[^>]*>)`, 'gi'), '\n$1').split('\n').filter(Boolean);
  const x = lines(a), y = lines(b);
  let s = 0; while (s < x.length && s < y.length && x[s] === y[s]) s++;
  let e = 0; while (e < x.length - s && e < y.length - s && x[x.length - 1 - e] === y[y.length - 1 - e]) e++;
  const X = x.slice(s, x.length - e), Y = y.slice(s, y.length - e), n = X.length, m = Y.length;
  // edit script over the middle part: LCS when it is small enough, else one replaced block
  let ops = [];
  if (n * m <= 4e6) {
    const L = new Uint16Array((n + 1) * (m + 1));
    for (let i = n - 1; i >= 0; i--) for (let j = m - 1; j >= 0; j--)
      L[i * (m + 1) + j] = X[i] === Y[j] ? L[(i + 1) * (m + 1) + j + 1] + 1 : Math.max(L[(i + 1) * (m + 1) + j], L[i * (m + 1) + j + 1]);
    let i = 0, j = 0;
    while (i < n || j < m) {
      if (i < n && j < m && X[i] === Y[j]) { ops.push([' ', X[i]]); i++; j++; }
      else if (j < m && (i === n || L[i * (m + 1) + j + 1] >= L[(i + 1) * (m + 1) + j])) ops.push(['+', Y[j++]]);
      else ops.push(['-', X[i++]]);
    }
  } else ops = [...X.map(l => ['-', l]), ...Y.map(l => ['+', l])];
  if (s > 0) ops.unshift([' ', x[s - 1]]);
  // group into hunks: runs of changes with one line of context before
  const out = []; let shown = 0, k = 0;
  while (k < ops.length && shown < hunks) {
    if (ops[k][0] === ' ') { k++; continue; }
    if (k > 0 && ops[k - 1][0] === ' ') out.push(`  ${ops[k - 1][1]}`);
    const minus = [], plus = [];
    while (k < ops.length && ops[k][0] !== ' ') (ops[k][0] === '-' ? minus : plus).push(ops[k++][1]);
    for (const [sign, ls] of [['-', minus], ['+', plus]]) {
      for (const l of ls.slice(0, max)) out.push(`${sign} ${l}`);
      if (ls.length > max) out.push(`${sign} ... (${ls.length - max} more)`);
    }
    shown++;
    if (shown < hunks) out.push('  ~');
  }
  const rest = ops.slice(k).filter(o => o[0] !== ' ').length;
  if (out[out.length - 1] === '  ~') out.pop();
  if (rest) out.push(`  (+ more differences: ${rest} lines)`);
  return out.map(l => l.length > 220 ? l.slice(0, 220) + '...' : l).join('\n');
}

// ---------------------------------------------------------------------------------------------------------------
(async () => {
  const t0 = Date.now();
  const ed = await openEditor();
  await ed.mode(false, false);                                     // the visual editor: Muya, which does the saving
  await ed.page.waitForFunction(() => window.__typedownMuya, { timeout: 30000 });
  const tOpen = Date.now();

  const results = {};
  const crashed = [];
  for (const input of selected) {
    const before = ed.errors.length;
    let r;
    try {
      // as spec-check.js: import (full render, as a file load does) and export, then once more with the export
      r = await ed.page.evaluate(src => {
        const muya = window.__typedownMuya;
        muya.setMarkdown(src, null, false);
        const first = muya.getMarkdown();
        muya.setMarkdown(first, null, false);
        return { first, second: muya.getMarkdown() };
      }, input.markdown);
    } catch (err) { crashed.push({ id: input.id, why: String(err.message).split('\n')[0] }); continue; }
    const allowFront = input.kind !== 'spec';
    const ref = render(input.markdown, allowFront), one = render(r.first, allowFront), two = render(r.second, allowFront);
    const save1 = compare(ref, one), save2 = compare(ref, two);
    const res = { id: input.id, section: input.section, save1, save2, kept: save1 !== 'changed' && save2 !== 'changed' };
    if (ed.errors.length > before) res.threw = ed.errors.slice(before)[0].split('\n')[0];
    if (!res.kept) {
      const bad = save1 === 'changed' ? one : two;
      res.input = input.markdown; res.export = r.first;
      if (save1 !== 'changed') res.secondExport = r.second;
      res.diff = htmlDiff(ref.folded, bad.folded);
      // the same apart from <p> wrappers inside list items: only the list's tight/loose spacing changed
      const untight = h => h.replace(/(<li>(?:<task \w+ \/>)?)<p>/g, '$1').replace(/<\/p>(?=<\/li>|<ul|<ol)/g, '').replace(/<\/p><p>/g, ' ');
      if (untight(ref.folded) === untight(bad.folded)) res.only = 'list tight/loose spacing';
    }
    results[input.id] = res;
  }
  await ed.close();
  const tEnd = Date.now();

  // ---- report
  const all = Object.values(results);
  const tally = kind => {
    const rs = all.filter(r => r.id.startsWith(kind));
    return `${rs.filter(r => r.kept).length}/${rs.length} kept (${rs.filter(r => r.kept && (r.save1 === 'reflowed' || r.save2 === 'reflowed')).length} only reflowed)` +
      `, ${rs.filter(r => !r.kept && r.save1 === 'changed').length} changed by the first save, ${rs.filter(r => !r.kept && r.save1 !== 'changed').length} only by the second`;
  };
  const show = (r, why) => {
    console.log(`\n${r.id} (${r.section})${why ? ' - ' + why : ''}${r.only ? ' [' + r.only + ' only]' : ''}${r.threw ? ' [page error: ' + r.threw + ']' : ''}`);
    const clip = s => { const j = JSON.stringify(s); return j.length > 400 ? j.slice(0, 400) + '..."' : j; };
    console.log('  input :', clip(r.input));
    console.log('  export:', clip(r.export));
    if (r.secondExport !== undefined) console.log('  2nd   :', clip(r.secondExport));
    console.log(r.diff.replace(/^/gm, '    '));
  };

  const known = fs.existsSync(knownPath) ? JSON.parse(fs.readFileSync(knownPath, 'utf8')) : null;
  if (jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(results, null, 1));

  console.log(`spec examples: ${tally('spec-')}`);
  console.log(`curated docs : ${tally('doc-')}`);
  console.log(`fixtures     : ${tally('fixture-')}`);
  console.log(`time: editor open ${((tOpen - t0) / 1000).toFixed(1)}s, ${all.length} inputs ${((tEnd - tOpen) / 1000).toFixed(1)}s`);
  if (crashed.length) console.log('crashed:', JSON.stringify(crashed));

  if (update) {
    const failing = Object.assign({}, known ? known.failing : {});
    const keeping = new Set(known ? known.kept : []);
    for (const r of all) {
      if (r.kept) { delete failing[r.id]; keeping.add(r.id); } else { failing[r.id] = r.save1 === 'changed' ? 'first save' : 'second save'; keeping.delete(r.id); }
    }
    const order = (a, b) => { const k = s => [s.startsWith('spec-') ? 0 : s.startsWith('doc-') ? 1 : 2, Number(s.slice(5)) || 0, s]; const [x, y] = [k(a), k(b)]; return x[0] - y[0] || x[1] - y[1] || (x[2] < y[2] ? -1 : 1); };
    const sorted = {}; for (const k of Object.keys(failing).sort(order)) sorted[k] = failing[k];
    fs.writeFileSync(knownPath, JSON.stringify({ note: 'Inputs whose meaning a save changes today (read by markdown-it, CommonMark + GFM tables/strikethrough/tasks + footnotes). Written by meaning-check.js --update.', failing: sorted, kept: [...keeping].sort(order) }, null, 1) + '\n');
    if (verbose) for (const r of all.filter(r => !r.kept)) show(r);
    console.log(`recorded: ${Object.keys(sorted).length} failing, ${keeping.size} keeping their meaning`);
    if (crashed.length) process.exit(1);
    return;
  }

  if (!known) { console.error('no meaning-known.json yet - run with --update'); process.exit(1); }
  const keptBefore = new Set(known.kept);
  const regressions = all.filter(r => !r.kept && !(r.id in known.failing));
  const fixed = all.filter(r => r.kept && r.id in known.failing);
  if (verbose) for (const r of all.filter(r => !r.kept && r.id in known.failing)) show(r, 'known');
  for (const r of regressions) show(r, keptBefore.has(r.id) ? 'REGRESSION: kept its meaning before' : 'REGRESSION: new input that changes meaning');
  for (const r of fixed) console.log(`note: ${r.id} now keeps its meaning (known failure fixed; run --update)`);
  if (regressions.length || crashed.length) { console.log(`\n${regressions.length} regression(s)${crashed.length ? `, ${crashed.length} crashed` : ''}`); process.exit(1); }
  console.log(`no regression (${all.filter(r => !r.kept).length} known failures${fixed.length ? `, ${fixed.length} fixed` : ''})`);
})().catch(e => { console.error(e); process.exit(1); });
