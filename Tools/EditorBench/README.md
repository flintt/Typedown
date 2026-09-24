# Editor benchmark

Drives the built editor bundle (`Dev/Typedown/Resources/Statics`) in headless Chrome with the WebView2 host
stubbed out, loads a large document, types into it and reports per-keystroke cost plus a CPU profile.

```bash
cd Tools/EditorBench
npm init -y && npm i puppeteer-core source-map          # once
# build the editor first (GENERATE_SOURCEMAP=true gives readable profiles)
node bench.js ../../Dev/Typedown/Resources/Statics big.md label
node prof-summary.js profile-label.cpuprofile ../../Dev/Typedown/Resources/Statics/static/js/main.*.js.map
```

`bench.js` expects Chrome at `/opt/google/chrome/chrome`; edit `executablePath` otherwise. The harness itself
costs ~20 ms per keystroke (CDP round trips), so compare runs against each other, not against zero.

Reference (80k-char document, 1287 blocks, 4-core Linux box): before the 2026-09-18 fixes 83–107 ms per
keystroke, after 45–53 ms.

`modeswitch-check.js` reproduces upstream #20/#61 (blank lines appended on every source-mode switch): the
markdown printed after "back to muya" must be identical to the value shown before the switch.

Other checks (all take `STATICS=<dir>` or default to the built editor):

- `spec-check.js` — every CommonMark 0.31.2 example (652) imported, exported, imported and exported again. The
  second export must equal the first (importing what we just wrote must not keep changing the document), and the
  first must equal `spec-known.json`, the recorded behaviour of this editor. Muya is not a CommonMark renderer,
  so the baseline records what we do, not what the spec says; its point is that no editor change can quietly
  alter the document model of 652 documents. `--update` re-records, `--section <name>` or example numbers narrow
  the run. Three examples still do not survive a second import — a tab-indented list item, a rule immediately
  followed by a setext heading, and an indented code block wrapped in blank lines; each settles after one more
  round and none of them loses text. They are marked in the baseline.
- `style-check.js` — lays out `style-fixture.md` (headings, lists, tables, code, quote, maths, footnote, raw
  html) and measures every element: position, size, font, weight, colour, background. A block passes if it is
  within 2px of where it was and within 1px of its size; fonts and colours must match exactly. `--update`
  records, `--theme dark` keeps its own baseline, and `--against <dir>` compares two editor builds directly
  instead of against a baseline — that is how the Windows and Uno bundles are checked against each other:

      node style-check.js --against ../../../Typedown-Uno/Typedown.Uno/Assets/Editor

- `legacy-issues.js` — the issues reported against the original Typedown, in one run: raw html renders with its
  inline CSS, a code block is in the saved markdown the moment it is typed, a table cell whose text is selected
  and deleted still takes input, PageUp/PageDown keep moving.
- `roundtrip-check.js '"md..."' ...` — import each markdown string into Muya and print blocks + exported markdown.
- `handshake-check.js` — LoadFile → FileLoaded handshake: FileLoaded carries the normalized text and the load id, a superseded load leaks nothing, an edit afterwards reports MarkdownChange (exit code 1 on failure).
- `scroll-restore-check.js` — a load that carries `scrollTop` (remembered offset, read-only mode) lands at that offset instead of at the caret; a load without it starts at the top.
- `export-check.js '"md..."'` — run the HTML export pipeline and print the body (upstream #48 emphasis case by default).
- `scrollup-check.js` — Up arrow must scroll the caret back into view (upstream #51).
