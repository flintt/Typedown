// A custom theme's text colour on the page: paragraphs, lists, tables in --editorColor, the theme's own, as the theme
// designer shows it. The themes set --editorColor and, until 2026-11-02, no colour of their own: the text took the
// base sheet's (Indigo's #dde3ef text pure white in the app, a reader's report). Each theme that ships, as it is and
// with its "color:" line taken out (an older or a hand-made theme); and with no theme, the base's text as before.
//
//   node theme-text-colour-check.js
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

(async () => {
  const { page, errors, mode, load, pause, close } = await openEditor();
  const failures = [];
  const report = (ok, what, detail) => { console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${what}${ok ? '' : ': ' + detail}`); if (!ok) failures.push(what); };
  const folders = ['../../Dev/Typedown/Resources/Themes', '../../Dev/Edition/Resources/Themes'].map(f => path.resolve(__dirname, f));
  const themes = folders.flatMap(f => fs.readdirSync(f).filter(n => n.endsWith('.css')).map(n => path.join(f, n)));
  const doc = '# Heading\n\nA paragraph of **bold** text.\n\n- an item\n\n| a | b |\n|---|---|\n| 1 | 2 |\n';
  const colours = () => page.evaluate(() => {
    const of = sel => { const e = document.querySelector(sel); return e ? getComputedStyle(e).color : null; };
    const probe = document.createElement('span');
    probe.style.color = getComputedStyle(document.documentElement).getPropertyValue('--editorColor').trim();
    document.body.appendChild(probe);
    const expected = getComputedStyle(probe).color;
    probe.remove();
    return { expected, p: of('#ag-editor-id p'), strong: of('#ag-editor-id strong'), li: of('#ag-editor-id li'), td: of('#ag-editor-id td') };
  });
  const show = async (base, themeCss) => {
    await page.evaluate(base => window.__deliver('ThemeChanged', { theme: base, accentColor: { r: 0, g: 120, b: 212, a: 255 }, background: { r: 249, g: 249, b: 249, a: 255 } }), base);
    await page.evaluate(css => window.__deliver('SettingsChanged', { themeCss: css }), themeCss);
    await pause(400);
    await mode(true, false);
    await load(doc);
    await mode(false, false);
    await pause(400);
    return colours();
  };
  try {
    for (const file of themes) {
      const css = fs.readFileSync(file, 'utf8');
      const base = /\*\s*base:\s*(\w+)/.exec(css)?.[1] ?? 'light';
      const Base = base[0].toUpperCase() + base.slice(1);
      for (const [label, themeCss] of [['', css], [' without its "color:" line', css.replace(/^\s*color:[^;]*;\n/m, '')]]) {
        const c = await show(Base, themeCss);
        const wrong = ['p', 'strong', 'li', 'td'].filter(k => c[k] !== c.expected);
        report(wrong.length === 0, `${path.basename(file)}${label}: the text in the theme's colour`, `${wrong.map(k => `${k} ${c[k]}`).join(', ')} (the theme's ${c.expected})`);
      }
    }
    // No theme: the base sheet's text colour as before (the light base's black, not its --editorColor).
    const plain = await show('Light', '');
    report(plain.p === 'rgb(0, 0, 0)', 'no theme, light: the text the base sheet\'s black as before', plain.p);
    if (errors.length) failures.push(`page errors: ${errors.join(' | ')}`);
  } finally {
    await close();
  }
  console.log(failures.length ? `${failures.length} FAILED` : 'all passed');
  process.exit(failures.length ? 1 : 0);
})();
