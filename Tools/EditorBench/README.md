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
- `outline-follow-check.js` — reading mode has no caret, so the outline follows the page. Two things have
  to hold and it checks both: the heading reported moves with the page and comes back, and reporting it
  does not move the page. Its fake host is hostile on purpose — it scrolls to whatever `cur` a state report
  carries, which is what the first attempt fed it, so a heading routed back through that report starts the
  loop and the check sees the page moving on its own.
- `inline-mark-check.js` — `==text==` is highlight: it renders as `<mark>`, survives a round trip through the
  editor unchanged, exports as `<mark>`, is stripped from the outline's heading text, and HTML with `<mark>`
  imports as `==text==`. Three Store reviews asked for it; the markers used to be plain text.
- `caret-check.js` — edit mode has to have a caret after every load: the editor focused, a selection inside
  it, and a typed key landing in the text. Checked for the first load and for a load arriving as a tab
  switch. The page side passes; a caret that is missing on Windows is the host holding XAML focus elsewhere.
- `print-check.js` — asks the editor for the print HTML the way the host does, renders it as print media on
  an A4-wide page and checks that no code block is wider than its box. A long code line used to print a
  horizontal scrollbar and lose the rest of the line (a Store review from 2022, still true in 2026): the
  GitHub sheet and Prism keep `white-space: pre` on the code inside the block, more specifically than the
  export's own wrap rule. Failed with two blocks about 4000px too wide before the print rule went in.
- `outline-jump-check.js [--file doc.md]` — clicks every heading in the outline. Each has to land at the top
  of the window (16px below the edge, or as far as the page goes at the end of the document) and end up
  marked in the outline. The heading used to land 320px down, the caret's offset, so the section before it
  showed at the top and the follower marked that one: 3 of 40 at the top, 1 of 40 marked, before the fix.
- `open-position-check.js --file doc.md [--scroll N] [--line L] [--readonly 0|1] [--click "5.4.1"]` — opens a
  real document the way the host does, with a remembered caret and scroll offset, and records every move
  of the page and every outline report for the first seconds, then reads it with the wheel. The page has
  to settle where the host asked, the outline may not be left pointing at a place the page merely passed
  through, and no state report may arrive without a current heading. Written for a report that lost its
  outline highlight; what it found instead was the page leaving the remembered offset for the remembered
  caret a quarter of a second after landing — the load's own selection change, arriving through two timers,
  treated as a caret movement. It failed on that bundle (settled at 97, not 6000) before the fix went in.
- `idle-quiet-check.js` — an editor left alone must send nothing and re-render nothing. A stream of reports
  with nobody typing shows up as an outline that flickers and collapses, a selection that cannot be dragged
  because it dies on every render, and eventually a crash.
- `tab-stress-check.js` — twelve switches in a row, the way a tab bar is actually used: one editor element
  and one editable root throughout, and nothing sent once the switching stops.
- `tab-state-check.js` — what the host is told after a switch. The outline, the word count and the caret
  all reach the host through StateChange, and restoring a document already built produces no change of its
  own, so the report has to be asked for — at the wrong moment it goes to an editor that is no longer
  listening and the outline goes on describing the previous document, which is what shipped.
- `scroll-yield-check.js` — the hold that keeps a long document at its offset has to let go the moment the
  reader moves the page, by wheel, key, pointer or touch. Each gesture gets a page of its own: sharing one
  meant only the first met a live hold, and the check passed against a build with the guard removed.
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
