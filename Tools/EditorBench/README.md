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
  the run. Three examples still do not survive a second import; they are marked in the baseline and are left
  alone on purpose, because none of them loses text and each settles after one more round:

  - **7 (Tabs)** `-\t\tfoo` — tabs indenting content into a code block inside a list item. Everyday tab use is
    fine (tab-indented nested lists, `-` followed by a tab, a tab-indented second paragraph all round-trip with
    the tabs turned into spaces, once). Fixing this one means expanding tabs by column width in the parser,
    which touches every indentation decision there is.
  - **96 (Setext headings)** `---` used as a rule with a setext underline on the next line. The writing itself is
    ambiguous — the spec uses it to probe that boundary — so changing it means re-deciding what those three
    dashes mean.
  - **117 (Indented code blocks)** an indented code block padded with blank lines; one leading blank line is
    dropped. The code itself is untouched.
- `style-check.js` — lays out `style-fixture.md` (headings, lists, tables, code, quote, maths, footnote, raw
  html) and measures every element: position, size, font, weight, colour, background. A block passes if it is
  within 2px of where it was and within 1px of its size; fonts and colours must match exactly. `--update`
  records, `--theme dark` keeps its own baseline, and `--against <dir>` compares two editor builds directly
  instead of against a baseline — that is how the Windows and Uno bundles are checked against each other:

      node style-check.js --against ../../../Typedown-Uno/Typedown.Uno/Assets/Editor

  The maths in the fixture is measured twice over: KaTeX writes a visible `.katex-html` tree and a hidden
  `.katex-mathml` one for screen readers. Since a whole-document load builds the page from an HTML string
  (see `docs/editor-performance.md`), the hidden tree is parsed as real MathML instead of as elements that
  merely happen to be called `math` — one element fewer, different metrics, and nothing visible changes. That
  is what the baseline was re-recorded for.
- `outline-follow-check.js` — reading mode has no caret, so the outline's current heading is anchored to the
  scroll position instead; this scrolls a read-only document and checks the reported heading moves with it
  and comes back. It also decodes the diff protocol `transport.postMessage` uses — `StateChange` arrives as a
  fragment plus a range, not as an object, and a harness that reads `args.state` straight off it sees nothing.
- `tab-switch-check.js` — switching tabs between a long document and an ordinary one, both directions.
  `--big`, `--small`, `--rounds`. It reports **blocked time**, not only when the content appears: what a
  reader feels is how long the window stops answering, and work that lands after the content does still
  counts — measuring up to first paint once had me reporting a win that could not be felt. It switches with
  the text the editor reports back, not the file as written, because that is what the host stores and hands
  over, and anything else is a load of a different document.
- `legacy-issues.js` — the issues reported against the original Typedown, in one run: raw html renders with its
  inline CSS, a code block is in the saved markdown the moment it is typed, a table cell whose text is selected
  and deleted still takes input, PageUp/PageDown keep moving.
- `roundtrip-check.js '"md..."' ...` — import each markdown string into Muya and print blocks + exported markdown.
- `handshake-check.js` — LoadFile → FileLoaded handshake: FileLoaded carries the normalized text and the load id, a superseded load leaks nothing, an edit afterwards reports MarkdownChange (exit code 1 on failure).
- `scroll-flash-check.js` — a tab switch hands the document over with the offset it was left at; this samples
  where the page actually is over the frames that follow. Laying out a long document goes on well past the
  first paint and the browser can drop the position with nobody scrolling, which showed as the top of the
  document appearing for a moment. `--trace` records every scroll call with its stack and watches the editor
  root for replacement — useful when hunting, misleading when timing, so it is off by default.
- `scroll-restore-check.js` — a load that carries `scrollTop` (remembered offset, read-only mode) lands at that offset instead of at the caret; a load without it starts at the top.
- `export-check.js '"md..."'` — run the HTML export pipeline and print the body (upstream #48 emphasis case by default).
- `scrollup-check.js` — Up arrow must scroll the caret back into view (upstream #51).
