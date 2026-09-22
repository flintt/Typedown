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

- `roundtrip-check.js '"md..."' ...` — import each markdown string into Muya and print blocks + exported markdown.
- `handshake-check.js` — LoadFile → FileLoaded handshake: FileLoaded carries the normalized text and the load id, a superseded load leaks nothing, an edit afterwards reports MarkdownChange (exit code 1 on failure).
- `export-check.js '"md..."'` — run the HTML export pipeline and print the body (upstream #48 emphasis case by default).
- `scrollup-check.js` — Up arrow must scroll the caret back into view (upstream #51).
