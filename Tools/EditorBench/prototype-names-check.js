// No method of the editor's ContentState is defined twice. Its methods come from the class (contentState/index.js) and
// from the *Ctrl.js files, each putting functions on ContentState.prototype; one of the same name put later replaces the
// other without a word. Paragraph > Duplicate failed outright in 1.4.1 that way: the copy buttons' clipboard copy was
// added as ContentState.prototype.copyBlock, over the class's own copyBlock (the deep copy of a block Duplicate uses).
// Static: reads the sources, needs no browser.
//
//   node prototype-names-check.js
const fs = require('fs');
const path = require('path');

const dir = process.env.CONTENT_STATE_DIR || path.join(__dirname, '..', '..', 'Dev', 'Typedown.Editor', 'src', 'components', 'Muya', 'lib', 'contentState');
const defined = new Map(); // name -> [where]
const add = (name, where) => defined.set(name, [...(defined.get(name) || []), where]);

// The class's own methods: lines indented two spaces, `name (args) {` or `name(args) {`, inside `class ContentState`.
const index = fs.readFileSync(path.join(dir, 'index.js'), 'utf8');
const body = index.slice(index.indexOf('class ContentState'));
const reserved = new Set(['constructor', 'if', 'for', 'while', 'switch', 'catch', 'function', 'return']);
// A getter and a setter of one name are one property, not two methods.
for (const m of body.matchAll(/^ {2}(?:async |static )?(get |set )?([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*\{/gm)) {
  if (!reserved.has(m[2])) add(m[1] === 'set ' ? `${m[2]} (setter)` : m[2], 'index.js (class)');
}
// What every file puts on the prototype.
for (const file of fs.readdirSync(dir).filter(f => f.endsWith('.js'))) {
  const text = fs.readFileSync(path.join(dir, file), 'utf8');
  for (const m of text.matchAll(/ContentState\.prototype\.([A-Za-z_$][\w$]*)\s*=/g)) add(m[1], file);
}

const twice = [...defined].filter(([, where]) => where.length > 1);
for (const [name, where] of twice) console.log(`  FAIL ${name}: defined in ${where.join(', ')}`);
console.log(`${defined.size} methods; ${twice.length ? `${twice.length} defined twice` : 'each defined once'}`);
process.exit(twice.length ? 1 : 0);
