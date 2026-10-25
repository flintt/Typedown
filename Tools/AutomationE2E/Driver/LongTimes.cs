using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/// <summary>
/// LT01: how long a reader waits on a long document - the limits are what a reader puts up with, not what a build
/// happens to need (2026-10-25: a 1.5 MB document took over 60 s to be drawn whole on the W10 PC, and the cases that
/// waited for it had been given 90 s each and passed). Each opening is fresh: the document is closed and opened again.
/// </summary>
internal static partial class Program
{
    private const double LtFirstScreenMs = 1500, LtWholeMs = 10_000, LtKeyMs = 1000, LtFreezeMs = 200;

    private static async Task LT01(List<string> notes)
    {
        using var c = await Session("e2e LT01");
        string Embedded(string name)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var source = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(source, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        // The long document it was reported with, where a test machine keeps a private copy (never in the repository);
        // otherwise a generated one.
        var privateCopy = new[] { @"C:\e2e\private", @"E:\src\private" }.Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.md").OrderBy(f => f, StringComparer.Ordinal)).FirstOrDefault();
        var heavyPath = privateCopy != null ? Path.Combine(fixtures, "lt01-private.md") : Fixture("lt01-mixed-1m.md", Embedded("hj/mixed-1m.md.gz"));
        if (privateCopy != null) File.Copy(privateCopy, heavyPath, true);
        var text = File.ReadAllText(heavyPath);
        notes.Add($"long: {(privateCopy != null ? "the private copy" : "mixed 1M")} ({new FileInfo(heavyPath).Length / 1024} KB)");
        // A word found once in the document, in its last twentieth, ignoring case and as part of a longer word as the find
        // bar does: what the search below is for ("Multiple" was once in exact case, six times as the bar counts, and Enter
        // went to the first of the six, at the top).
        var word = Regex.Matches(text.Substring(text.Length * 19 / 20), @"[A-Za-z][A-Za-z0-9]{7,}").Select(m => m.Value).FirstOrDefault(w => Regex.Matches(text, Regex.Escape(w), RegexOptions.IgnoreCase).Count == 1)
            ?? throw new CaseFailed("no word found once in the document's last twentieth");

        var other = await Open(c, Fixture("lt01-other.md", "# other\n"));
        var windowId = await WindowIdOf(c, other);
        await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = false });
        async Task<string> Eval(string script) => (string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "";
        async Task<double> Until(Func<Task<bool>> done, double limitMs)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < limitMs) { if (await done()) return w.Elapsed.TotalMilliseconds; await Task.Delay(30); }
            return double.PositiveInfinity;
        }
        const string Pending = "String(!!(window.__typedownMuya&&window.__typedownMuya.renderPending))";
        // Text of the document on screen: a block of the editor with text, inside the window.
        const string OnScreen = "String(Array.from(document.querySelectorAll('#ag-editor-id > *')).some(b => { const r = b.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight && b.textContent.trim().length > 0 }))";
        string Shown(string what) => "String(Array.from(document.querySelectorAll('#ag-editor-id > *')).some(b => { const r = b.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight && b.textContent.includes(" + Newtonsoft.Json.JsonConvert.SerializeObject(what) + ") }))";
        // The page's frames from now on: the longest gap and how many went over the limit.
        var frames = $"(() => {{ const f = window.__ltFrames = {{ max: 0, over: 0, last: performance.now(), on: true }}; const tick = t => {{ const g = t - f.last; f.last = t; if (g > f.max) f.max = g; if (g > {LtFreezeMs}) f.over++; if (f.on) requestAnimationFrame(tick) }}; requestAnimationFrame(tick); return 'ok' }})()";
        const string FramesDone = "(() => { const f = window.__ltFrames || {}; f.on = false; return JSON.stringify({ max: Math.round(f.max || 0), over: f.over || 0 }) })()";
        var lastLine = text.TrimEnd().Split('\n').Last(l => l.Trim().Length > 0).Trim().TrimStart('#', '-', '*', '>', ' ', '|');
        var endText = lastLine.Length > 20 ? lastLine.Substring(0, 20) : lastLine;

        // The clock for "opened" starts as the document is asked for, not before the case's own pause: started before it, the
        // first screen counted 500 ms of the case's own waiting and the round trip of document.focus (2026-10-25).
        Stopwatch opened = new();
        async Task<string> Fresh()
        {
            await c.Call("document.focus", new { documentId = other });
            await Task.Delay(500);
            opened.Restart();
            return await Open(c, heavyPath);
        }
        async Task Close(string id) { try { await c.Call("document.close", new { documentId = id }); } catch { } await Task.Delay(1000); }

        // 1. Opened and left alone: on screen, drawn whole, and the longest the page stopped drawing frames meanwhile.
        var heavy = await Fresh();
        var first = double.IsInfinity(await Until(async () => await Eval(OnScreen) == "true", 10_000)) ? double.PositiveInfinity : opened.Elapsed.TotalMilliseconds;
        await Eval(frames);
        var whole = await Until(async () => await Eval(Pending) == "false", 120_000);
        var wholeMs = double.IsInfinity(whole) ? whole : opened.Elapsed.TotalMilliseconds;
        var gaps = await Eval(FramesDone);
        notes.Add($"opened: text on screen {first:0} ms, drawn whole {(double.IsInfinity(whole) ? "not within 120 s" : $"{wholeMs:0} ms")}; frames meanwhile {gaps}");
        await Close(heavy);

        // 2. Opened and Ctrl+End at once: the end of the document on screen.
        heavy = await Fresh();
        await Until(async () => await Eval(OnScreen) == "true", 10_000);
        await c.Call("test.editor.focus", new { windowId });
        var drawing = await Eval(Pending) == "true";
        Chord("Ctrl+End");
        var end = await Until(async () => await Eval(Shown(endText)) == "true", 30_000);
        notes.Add($"Ctrl+End {(drawing ? "while drawing" : "(already drawn)")}: the last line on screen in {(double.IsInfinity(end) ? "over 30 s" : $"{end:0} ms")}");
        await Close(heavy);

        // 3. Opened and searched at once for a word near the end.
        heavy = await Fresh();
        await Until(async () => await Eval(OnScreen) == "true", 10_000);
        await c.Call("test.editor.focus", new { windowId });
        var searching = await Eval(Pending) == "true";
        Chord("Ctrl+F");
        await Task.Delay(300);
        TypeText(word);
        Keys((ushort)0x0D); // Enter: to the first match
        var found = await Until(async () => await Eval(Shown(word)) == "true", 30_000);
        // The page's own account of the search (components/Muya window.__typedownSearchLog): when each message came (page
        // clock, ms), how long the page took over it and how many matches - whether the wait was the page's or the messages'.
        var searchLog = await Eval("JSON.stringify({ now: Math.round(performance.now()), log: window.__typedownSearchLog || [] })");
        notes.Add($"search, the page's account (now = when the word was seen + one eval): {searchLog}");
        // Not seen: where the page is, where the match and its block are, and who moved the page last (services/scrollWriters).
        if (double.IsInfinity(found))
            notes.Add("search not seen, the page: " + await Eval("JSON.stringify((() => { const h = document.querySelector('#ag-editor-id .ag-highlight'); const b = h && h.closest('#ag-editor-id > *'); const r = x => x && (({ top, bottom }) => [Math.round(top), Math.round(bottom)])(x.getBoundingClientRect()); const st = window.__typedownMuya && window.__typedownMuya.contentState.stateRender.progressive; return { y: Math.round(scrollY), h: document.documentElement.scrollHeight, inner: innerHeight, mark: r(h), block: b && b.id, blockAt: r(b), blockText: b ? b.textContent.length : null, drawing: st ? [st.lo, st.hi, st.blocks.length] : null, matches: window.__typedownMuya && window.__typedownMuya.contentState.searchMatches.matches.length, scrolls: window.__typedownScrolls ? window.__typedownScrolls(30000) : null } })())"));
        Keys((ushort)0x1B); // Esc
        notes.Add($"search for \"{word}\" {(searching ? "while drawing" : "(already drawn)")}: on screen in {(double.IsInfinity(found) ? "over 30 s" : $"{found:0} ms")}");
        await Close(heavy);

        var g = Newtonsoft.Json.Linq.JObject.Parse(gaps);
        Check(first <= LtFirstScreenMs, $"opened, its text on screen within {LtFirstScreenMs:0} ms ({first:0} ms)");
        Check(wholeMs <= LtWholeMs, $"drawn whole within {LtWholeMs / 1000:0} s ({(double.IsInfinity(whole) ? "over 120 s" : $"{wholeMs / 1000:0.0} s")})");
        Check((int)g["max"]! <= LtFreezeMs, $"while it is drawn the page never stops drawing frames for more than {LtFreezeMs:0} ms (longest {g["max"]} ms, {g["over"]} over)");
        Check(end <= LtKeyMs, $"Ctrl+End right after opening shows the last line within {LtKeyMs:0} ms ({(double.IsInfinity(end) ? "over 30 s" : $"{end:0} ms")})");
        Check(found <= LtKeyMs, $"a search right after opening shows a word near the end within {LtKeyMs:0} ms ({(double.IsInfinity(found) ? "over 30 s" : $"{found:0} ms")})");
    }
}
