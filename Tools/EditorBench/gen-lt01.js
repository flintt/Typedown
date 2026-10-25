// A long document shaped like the one LT01 was reported with (2026-10-25: 1.5 MB, 1.49 M characters, 2938 top-level
// blocks, dense in raw HTML blocks), made up here: no real text. Raw HTML tables, styled divs, <details>, runs of
// span-styled lines, headings, lists, fenced code, paragraphs and near-identical repeated lines, from a fixed seed so
// every run gets the same document. The last line and a word found once near the end are what LT01 looks for.
//
//   node gen-lt01.js [out.md]          - writes the document (default: stdout) and its shape to stderr
//   require('./gen-lt01').generate()   - { text, word, lastLine }
'use strict';

const UNIQUE = 'Quillowmarsh';

function generate({ blocks: target = 2938, seed = 20261025 } = {}) {
  let s = seed >>> 0;
  const rnd = () => ((s = (Math.imul(s, 1664525) + 1013904223) >>> 0) / 4294967296);
  const int = (a, b) => a + Math.floor(rnd() * (b - a + 1));
  const pick = list => list[Math.floor(rnd() * list.length)];
  const syl = ['ka', 'lo', 'mi', 'ten', 'ra', 'vo', 'su', 'pel', 'dra', 'ni', 'qua', 'zor', 'be', 'tis', 'ham', 'gu', 'fen', 'or', 'wy', 'lex'];
  const word = () => { let w = ''; for (let i = int(1, 3); i > 0; i--) w += pick(syl); return w; };
  const words = n => Array.from({ length: n }, word).join(' ');
  const sentence = () => { const t = words(int(5, 11)); return t[0].toUpperCase() + t.slice(1) + '.'; };
  const sentences = n => Array.from({ length: n }, sentence).join(' ');
  const color = () => pick(['#c00000', '#1f4e79', '#385723', 'rgb(89, 89, 89)', 'rgb(192, 0, 0)', '#7030a0', '#333']);
  const styled = t => `<span style="color: ${color()}; font-size: ${int(12, 16)}px;">${t}</span>`;
  const inline = () => {
    const parts = [sentences(int(1, 2))];
    if (rnd() < 0.3) parts.push(`**${words(int(2, 4))}**`);
    if (rnd() < 0.2) parts.push('`' + word() + '()`');
    if (rnd() < 0.2) parts.push(`[${words(2)}](https://example.com/${word()}/${int(1, 999)})`);
    if (rnd() < 0.35) parts.push(styled(words(int(3, 8))));
    parts.push(sentences(int(1, 2)));
    return parts.join(' ');
  };

  const table = () => {
    const cols = int(3, 5);
    const rows = int(2, 5);
    const td = () => `<td style="border: 1px solid #bfbfbf; padding: 4px;">${rnd() < 0.4 ? styled(words(int(1, 6))) : words(int(1, 8))}</td>`;
    const lines = ['<table style="border-collapse: collapse; width: 100%;">', '<tbody>',
      '<tr>' + Array.from({ length: cols }, () => `<th style="background: #d9e2f3; border: 1px solid #bfbfbf;">${words(int(1, 2))}</th>`).join('') + '</tr>'];
    for (let r = 0; r < rows; r++) lines.push('<tr>' + Array.from({ length: cols }, td).join('') + '</tr>');
    lines.push('</tbody>', '</table>');
    return lines.join('\n');
  };
  const div = () => {
    const lines = [`<div style="margin: 8px 0; padding: 8px 12px; border-left: 4px solid ${color()}; background: #f7f7f7;" class="note-${word()}">`];
    for (let i = int(1, 3); i > 0; i--) lines.push(`<p style="margin: 0;">${rnd() < 0.5 ? styled(sentences(int(1, 3))) : sentences(int(1, 3))}</p>`);
    if (rnd() < 0.3) lines.push(`<img src="https://example.com/img/${word()}.png" width="${int(80, 400)}" alt="${word()}">`);
    lines.push('</div>');
    return lines.join('\n');
  };
  const details = () => [`<details>`, `<summary>${styled(words(int(2, 6)))}</summary>`, `<div>${sentences(int(2, 5))}</div>`, `<ul><li>${words(5)}</li><li>${words(6)}</li></ul>`, `</details>`].join('\n');
  // A run of lines styled one by one, as pasted from a word processor: many nearly the same.
  const spanLines = () => {
    const base = words(int(4, 9));
    return Array.from({ length: int(2, 5) }, (_, i) => `<p style="margin: 0; line-height: 1.5;">${styled(`${base} ${i + 1}`)}${rnd() < 0.5 ? '&nbsp;' : ''}</p>`).join('\n');
  };
  const list = () => {
    const ordered = rnd() < 0.4;
    return Array.from({ length: int(3, 7) }, (_, i) => `${ordered ? `${i + 1}.` : '-'} ${rnd() < 0.2 ? styled(words(int(3, 8))) : sentence()}`).join('\n');
  };
  const code = () => {
    const lang = pick(['js', 'python', 'bash', 'json', 'html']);
    return '```' + lang + '\n' + Array.from({ length: int(3, 10) }, () => `${'  '.repeat(int(0, 3))}${word()}(${word()}, ${int(0, 99)}) // ${words(int(1, 5))}`).join('\n') + '\n```';
  };
  const repeated = (() => { const base = words(8); return () => `${base[0].toUpperCase() + base.slice(1)}: ${int(1, 3) === 1 ? 'see above' : 'as before'}, ${int(1, 40)}.`; })();

  const kinds = [
    [0.06, () => `${'#'.repeat(int(1, 3))} ${words(int(2, 6))}`],
    [0.22, inline],
    [0.15, table],
    [0.14, div],
    [0.07, details],
    [0.10, spanLines],
    [0.10, list],
    [0.05, code],
    [0.11, repeated],
  ];
  const out = [];
  for (let i = 0; i < target - 2; i++) {
    let r = rnd();
    let make = kinds[kinds.length - 1][1];
    for (const [p, f] of kinds) { if (r < p) { make = f; break; } r -= p; }
    out.push(make());
    // The word found once, in the document's last twentieth.
    if (i === Math.floor(target * 0.97)) out.push(`The ${UNIQUE} entry closes the list of ${words(3)}.`);
  }
  const lastLine = `Closing line ${words(4)}, the last of the document.`;
  out.push(lastLine);
  return { text: out.join('\n\n') + '\n', word: UNIQUE, lastLine };
}

module.exports = { generate };

if (require.main === module) {
  const { text, word, lastLine } = generate();
  const file = process.argv[2];
  if (file) require('fs').writeFileSync(file, text); else process.stdout.write(text);
  process.stderr.write(`${Buffer.byteLength(text)} bytes, ${text.length} characters, ${text.split(/\n\n+/).length} chunks; word ${word}; last line "${lastLine}"\n`);
}
