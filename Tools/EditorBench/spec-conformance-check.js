// Measures how far the editor's HTML follows CommonMark 0.31.2: every spec example's Markdown is rendered the way
// HTML export, PDF export and RenderXhtml render it (Muya.exportHtml -> services/exportHtml.js renderHtml: the
// marked fork in Muya/lib/parser/marked, DOMPurify, mermaid/diagram passes) and compared with the spec's HTML.
// spec-check.js locks round trips; this one is about the spec's expected output.
//
// Comparison. Both strings are parsed by the browser (a <template>; the export's own output has already been through
// a DOM parse in DOMPurify, so the expected side gets the same treatment and serialisation artefacts cancel) and
// written out the way CommonMark's test/normalize.py does: lowercase tags, attributes sorted, entities decoded and
// re-escaped canonically, whitespace collapsed outside <pre> and dropped around block tags, void tags without a
// closing form. An example passes when the two are equal.
//
// Two scores are reported:
//   * strict  - normalize.py only.
//   * passes  - also ignoring what the editor adds on purpose and a browser does not show: heading id and its
//               atx/setext class, the code block's indented-/fenced-code-block class, Prism's highlighting spans,
//               the one newline that ends a code block's text, an ordered list's data-delimiter (kept for "1)"
//               lists), and the file:// the export puts before a root-relative image path (the page's basePath
//               makes "/url" a local file). This is the score the known file locks.
//
//   node spec-conformance-check.js                 # compare with spec-conformance-known.json
//   node spec-conformance-check.js --update        # record the current pass set
//   node spec-conformance-check.js --section Tabs  # only examples whose section name contains this
//   node spec-conformance-check.js 42 190          # only these example numbers (shows expected/got for each)
//   node spec-conformance-check.js --verbose       # show expected/got for every failure
//
// Exits 1 only when an example the known file records as passing now fails (a regression); newly passing ones are
// a note (run --update to lock them in).
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

const dataPath = path.join(__dirname, 'spec-conformance-data.json');
const knownPath = path.join(__dirname, 'spec-conformance-known.json');

const args = process.argv.slice(2);
const update = args.includes('--update');
const verbose = args.includes('--verbose');
const sectionAt = args.indexOf('--section');
const section = sectionAt >= 0 ? args[sectionAt + 1] : null;
const only = args.filter((a, i) => /^\d+$/.test(a) && (sectionAt < 0 || i !== sectionAt + 1)).map(Number);

const all = JSON.parse(fs.readFileSync(dataPath, 'utf8'));
let examples = all;
if (section) examples = examples.filter(e => e.section.toLowerCase().includes(section.toLowerCase()));
if (only.length) examples = examples.filter(e => only.includes(e.example));
if (update && examples.length !== all.length) { console.error('--update records the whole spec; run it without --section or example numbers'); process.exit(1); }

// Runs in the page. Returns [strict, lenient] normalised forms of an HTML string; `editor` applies the decoration
// stripping (only to the export's side - the spec's HTML has none of it, except the code block's final newline).
function pageNormalize(html, editor) {
  const BLOCK = new Set(['article', 'header', 'aside', 'hgroup', 'blockquote', 'hr', 'iframe', 'body', 'li', 'map', 'button', 'object', 'canvas', 'ol', 'output', 'p', 'pre', 'progress', 'video', 'audio', 'address', 'dd', 'div', 'dl', 'dt', 'fieldset', 'figcaption', 'figure', 'footer', 'form', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'noscript', 'section', 'table', 'tbody', 'td', 'tfoot', 'th', 'thead', 'tr', 'ul', 'caption', 'colgroup', 'col', 'main', 'nav', 'details', 'summary', 'menu', 'legend', 'optgroup', 'option', 'search', 'dialog', 'frame', 'frameset', 'base', 'basefont', 'link', 'param', 'source', 'track', 'title', 'head', 'html'])
  const VOID = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr'])
  const escText = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/ /g, '&nbsp;')
  const escAttr = s => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/ /g, '&nbsp;')
  const parse = () => { const t = document.createElement('template'); t.innerHTML = html; return t.content }

  const strip = root => {
    for (const h of root.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
      h.removeAttribute('id')
      h.classList.remove('atx', 'setext')
      if (h.getAttribute('class') === '') h.removeAttribute('class')
    }
    for (const code of root.querySelectorAll('pre > code')) {
      code.classList.remove('indented-code-block', 'fenced-code-block')
      if (code.getAttribute('class') === '') code.removeAttribute('class')
      if (code.children.length) code.textContent = code.textContent // Prism's token spans
    }
    for (const ol of root.querySelectorAll('ol[data-delimiter]')) ol.removeAttribute('data-delimiter')
    for (const img of root.querySelectorAll('img[src^="file:///"]')) img.setAttribute('src', img.getAttribute('src').slice(7))
  }
  const dropFinalNewline = root => {
    for (const code of root.querySelectorAll('pre > code')) {
      const last = code.lastChild
      if (last && last.nodeType === 3 && last.data.endsWith('\n')) last.data = last.data.slice(0, -1)
    }
  }

  const serialize = root => {
    let out = ''; let last = 'starttag'; let lastTag = ''; let inPre = false
    const walk = node => {
      for (const n of node.childNodes) {
        if (n.nodeType === 3) {
          let data = n.data
          const afterTag = last === 'endtag' || last === 'starttag'
          const afterBlock = afterTag && BLOCK.has(lastTag)
          if (afterTag && lastTag === 'br') data = data.replace(/^\n+/, '')
          if (!inPre) data = data.replace(/\s+/g, ' ')
          if (afterBlock && !inPre) data = last === 'starttag' ? data.replace(/^\s+/, '') : data.trim()
          out += escText(data); last = 'data'
        } else if (n.nodeType === 8) {
          out += `<!--${n.data}-->`; last = 'comment'
        } else if (n.nodeType === 1) {
          const tag = n.localName
          if (tag === 'pre') inPre = true
          if (BLOCK.has(tag)) out = out.replace(/\s+$/, '')
          out += '<' + tag
          for (const a of [...n.attributes].map(a => [a.name, a.value]).sort((x, y) => x[0] < y[0] ? -1 : x[0] > y[0] ? 1 : 0)) out += ` ${a[0]}="${escAttr(a[1])}"`
          out += '>'; lastTag = tag; last = 'starttag'
          if (VOID.has(tag)) { last = 'endtag'; continue }
          walk(tag === 'template' ? n.content : n)
          if (tag === 'pre') inPre = false
          else if (BLOCK.has(tag)) out = out.replace(/\s+$/, '')
          out += `</${tag}>`; lastTag = tag; last = 'endtag'
        }
      }
    }
    walk(root)
    return out.trim()
  }

  const strict = serialize(parse())
  const lenientRoot = parse()
  if (editor) strip(lenientRoot)
  dropFinalNewline(lenientRoot)
  return [strict, serialize(lenientRoot)]
}

(async () => {
  const started = Date.now();
  const ed = await openEditor();
  await ed.mode(false, false); // visual mode: Muya, and with it window.__typedownMuya
  await ed.page.evaluate(`window.__normalize = ${pageNormalize.toString()}`);

  const results = {};
  for (const e of examples) {
    const before = ed.errors.length;
    let r;
    try {
      r = await ed.page.evaluate(async (md, expected) => {
        const muya = window.__typedownMuya;
        muya.markdown = md; // exportHtml renders muya.markdown: the document text, as the exports take it
        const got = await muya.exportHtml();
        const [expStrict, expLenient] = window.__normalize(expected, false);
        const [gotStrict, gotLenient] = window.__normalize(got, true);
        return { got, strict: expStrict === gotStrict, pass: expLenient === gotLenient, expN: expLenient, gotN: gotLenient };
      }, e.markdown, e.html);
    } catch (err) {
      r = { got: null, strict: false, pass: false, threw: String(err.message).split('\n')[0] };
    }
    if (ed.errors.length > before && !r.threw) r.threw = ed.errors.slice(before)[0].split('\n')[0];
    results[e.example] = { ...r, e };
  }
  await ed.close();

  // Per-section table, in spec order.
  const sections = [];
  for (const e of examples) {
    let s = sections.find(s => s.name === e.section);
    if (!s) sections.push(s = { name: e.section, total: 0, pass: 0, strict: 0, failing: [] });
    const r = results[e.example];
    s.total++; if (r.pass) s.pass++; else s.failing.push(e.example); if (r.strict) s.strict++;
  }
  const pct = (a, b) => (b ? (100 * a / b).toFixed(1) : '0.0').padStart(5) + '%';
  const width = Math.max(...sections.map(s => s.name.length), 7);
  console.log(`${'section'.padEnd(width)}  pass/total      %   strict  failing`);
  for (const s of sections) console.log(`${s.name.padEnd(width)}  ${String(s.pass).padStart(4)}/${String(s.total).padEnd(4)} ${pct(s.pass, s.total)}  ${String(s.strict).padStart(6)}  ${s.failing.join(' ')}`);
  const total = examples.length, pass = sections.reduce((a, s) => a + s.pass, 0), strict = sections.reduce((a, s) => a + s.strict, 0);
  console.log(`\noverall: ${pass}/${total} pass (${pct(pass, total).trim()}); strict normalize.py only: ${strict}/${total} (${pct(strict, total).trim()})`);
  const threw = Object.values(results).filter(r => r.threw);
  if (threw.length) console.log(`threw: ${threw.map(r => `${r.e.example} (${r.threw})`).join('; ')}`);

  const show = r => {
    console.log(`\nexample ${r.e.example} (${r.e.section})`);
    console.log('  markdown:', JSON.stringify(r.e.markdown));
    console.log('  expected:', JSON.stringify(r.expN ?? r.e.html));
    console.log('  got:     ', JSON.stringify(r.gotN ?? r.got));
  };
  if (only.length || verbose) for (const r of Object.values(results)) if (!r.pass) show(r);

  if (update) {
    const known = { spec: '0.31.2', passing: Object.values(results).filter(r => r.pass).map(r => r.e.example).sort((a, b) => a - b) };
    fs.writeFileSync(knownPath, JSON.stringify(known) + '\n');
    console.log(`\nrecorded ${known.passing.length} passing examples in ${path.basename(knownPath)}`);
  } else if (fs.existsSync(knownPath)) {
    const known = new Set(JSON.parse(fs.readFileSync(knownPath, 'utf8')).passing);
    const regressed = Object.values(results).filter(r => known.has(r.e.example) && !r.pass);
    const improved = Object.values(results).filter(r => !known.has(r.e.example) && r.pass).map(r => r.e.example);
    if (improved.length) console.log(`\nnote: now passing, not yet in the known file (run --update): ${improved.join(' ')}`);
    if (regressed.length) {
      console.log(`\nREGRESSION: ${regressed.length} example(s) passed before and fail now: ${regressed.map(r => r.e.example).join(' ')}`);
      if (!only.length && !verbose) regressed.forEach(show);
      console.log(`\n(${((Date.now() - started) / 1000).toFixed(1)}s)`);
      process.exit(1);
    }
    console.log('\nno regressions against the known file');
  } else console.log(`\nno ${path.basename(knownPath)} yet - run with --update to record the pass set`);
  console.log(`(${((Date.now() - started) / 1000).toFixed(1)}s)`);
})().catch(e => { console.error(e); process.exit(1); });
