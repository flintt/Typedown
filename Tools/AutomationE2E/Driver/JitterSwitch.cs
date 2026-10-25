using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Switching to a long document read far down from a short one, as the reader does: what the screen shows frame by
    // frame, taken from the screen itself (not only what the page says of its blocks), with predicting the layout off
    // and on. Reported (hp, 2026-10-24): from showcase back to demo1, demo1 still shook while drawn.
    // What the page tells the host while the long document comes, and when (from the switch), with the frames the page
    // went without for more than 100 ms: where the time between the cover and the document on the screen goes.
    private const string JtMessagesScript = @"(()=>{const w=window.chrome.webview;if(!w.__jtWrapped){const o=w.postMessage.bind(w);w.postMessage=m=>{try{const s=window.__jtSaid;if(s&&s.length<60){const n=JSON.parse(m).name;if(n&&!/^(OnScroll|SelectionChange|SelectionFormats|invoke)$/.test(n))s.push([Math.round(performance.now()-window.__jtAt),n])}}catch(e){}return o(m)};w.__jtWrapped=true}window.__jtSaid=[];window.__jtAt=performance.now();let last=performance.now();const tick=()=>{const now=performance.now();if(now-window.__jtAt>6000)return;if(now-last>100&&window.__jtSaid.length<60)window.__jtSaid.push([Math.round(last-window.__jtAt),'gap '+Math.round(now-last)]);last=now;requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    private static async Task JT02(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e JT02");
        string Embedded(string name, bool gz)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var source = gz ? (Stream)new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress) : stream;
            using var reader = new StreamReader(source, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var demoCopy = new[] { @"C:\e2e\private\demo1.md", @"E:\src\private\demo1.md" }.FirstOrDefault(File.Exists);
        var heavyPath = demoCopy != null ? Path.Combine(fixtures, "jt02-demo1.md") : Fixture("jt02-mixed-1m.md", Embedded("hj/mixed-1m.md.gz", true));
        if (demoCopy != null) File.Copy(demoCopy, heavyPath, true);
        var shortPath = Fixture("jt02-showcase.md", Embedded("showcase.md", false));
        notes.Add($"long: {(demoCopy != null ? "demo1" : "mixed 1M")} ({new FileInfo(heavyPath).Length / 1024} KB), short: showcase");
        var heavy = await Open(c, heavyPath);
        var windowId = await WindowIdOf(c, heavy);
        var window = await WindowOf(windowId, c);
        async Task<bool> Pending() => (bool?)(await c.Call("test.editor.eval", new { windowId, script = "!!(window.__typedownMuya&&window.__typedownMuya.renderPending)" }))["result"] == true;
        async Task Drawn(int seconds) { for (var i = 0; i < seconds * 4 && await Pending(); i++) await Task.Delay(250); }
        var other = await Open(c, shortPath);
        await Task.Delay(2000);
        var dir = Path.Combine(outputDir, "jt02");
        Directory.CreateDirectory(dir);
        var results = new List<(string Label, JObject Page, ScreenMoves Screen)>();
        GetWindowRect(window, out var normal);
        // One switch from the short document to the long one, watched for 6 s from just before it.
        async Task Watch(string label, string? keep)
        {
            await c.Call("test.editor.eval", new { windowId, script = JtStartScript });
            await c.Call("test.editor.eval", new { windowId, script = JtMessagesScript });
            using var stop = new CancellationTokenSource();
            var shots = Task.Run(() => WatchScreen(window, stop.Token, keep == null ? null : Path.Combine(dir, keep)));
            await Task.Delay(100);
            await c.Call("document.focus", new { documentId = heavy });
            await Task.Delay(6000);
            stop.Cancel();
            var screen = await shots;
            var page = JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script = JtStopScript }))["result"] ?? "{}");
            var said = (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__jtSaid||[])" }))["result"];
            // The web view's own size frame by frame (ms from the first frame: width x height @ its top on the screen): a move
            // on the screen that the page's blocks do not show (JT02, reading, hp window, 106 px and back) is the host's.
            var view = (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(((window.__jt&&window.__jt.frames)||[]).filter((f,i,a)=>i===0||f[9]!==a[i-1][9]).map(f=>[f[0]-a0(),f[9]]))".Replace("a0()", "(window.__jt.frames[0]||[0])[0]") }))["result"];
            notes.Add($"{label}: the web view frame by frame: {view}");
            notes.Add($"{label}: screen {screen}; page {page.ToString(Newtonsoft.Json.Formatting.None)}; the page told the host (ms from the switch; frame gaps over 100 ms as gap): {said}");
            results.Add((label, page, screen));
        }
        try
        {
            // In reading mode too: the reader's demo1 was open in it (hp, 1.4.0.13: back from showcase, the page jumped up
            // and down 3-4 times by 40-55 px while drawn).
            // And at the reader's window on hp (1402 x 1070) with the outline open: only there did the page jump while
            // drawn (w10, reading mode: by 26-161 px and back, frame after frame).
            foreach (var (mode, hpWindow) in new[] { ("visual", false), ("reading", false), ("reading", true) })
            foreach (var predict in new[] { false, true })
            {
                var on = (mode == "reading" ? "reading, " : "") + (hpWindow ? "hp window, " : "") + (predict ? "on" : "off");
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(500);
                if (hpWindow)
                {
                    MbSetWindowPos(window, IntPtr.Zero, 40, 20, 1402, 1070, 0x0004 | 0x0010);
                    await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
                    await Task.Delay(1000);
                }
                await c.Call("window.setView", new { windowId, mode });
                await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = predict });
                await Task.Delay(500);
                // Where the reader had it on hp: from a quarter of the way to the end (scrollY 90,000-386,000 of 400,000).
                foreach (var at in new[] { 0.25, 0.6, 0.95 })
                {
                    await c.Call("document.focus", new { documentId = heavy });
                    await Task.Delay(1000);
                    await Drawn(90);
                    await c.Call("test.editor.eval", new { windowId, script = $"window.scrollTo(0, Math.round(document.documentElement.scrollHeight * {at.ToString(System.Globalization.CultureInfo.InvariantCulture)})); 'ok'" });
                    await Task.Delay(1500);
                    await c.Call("document.focus", new { documentId = other });
                    await Task.Delay(3000);
                    for (var round = 1; round <= 2; round++)
                    {
                        await Watch($"predict {on}, at {at:P0}, round {round}", $"{on.Replace(", ", "-")}-{at * 100:0}-{round}");
                        await c.Call("document.focus", new { documentId = other });
                        await Task.Delay(3000);
                    }
                }
                // Left before it was all drawn and switched back to (load 3 on hp was left 5 s in, still drawing).
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(2500);
                var drawing = await Pending();
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(3000);
                await Watch($"predict {on}, left still drawing ({(drawing ? "it was" : "it was NOT")}) and back", $"{on.Replace(", ", "-")}-unfinished");
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(3000);
            }
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = false }); } catch { }
            try { await c.Call("document.focus", new { documentId = heavy }); await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = false } }); } catch { }
            try { MbSetWindowPos(window, IntPtr.Zero, normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, 0x0004 | 0x0010); } catch { }
            foreach (var d in new[] { heavy, other }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
        foreach (var (label, page, screen) in results)
        {
            Check(screen.OldAgain == 0, $"{label}: once covered, the screen goes on to the long document without the short one showing again ({screen.OldAgain} frames of it)");
            Check(screen.Moves == 0 && (int?)page["moves"] == 0, $"{label}: once the long document is on screen its text does not move ({screen.Moves} moves on screen, largest {screen.MaxShift} px; the page counts {page["moves"]})");
        }
    }

    private sealed record ScreenMoves(int Frames, int Moves, int MaxShift, int Replaced, int OldAgain, string Timeline)
    {
        public override string ToString() => $"{Frames} frames, {Moves} moves (largest {MaxShift} px), {Replaced} replaced, {OldAgain} with the document left shown again after the cover: {Timeline}";
    }

    // The editor's part of the window, taken as fast as the screen gives it for as long as the token lets it; between
    // two frames whose middle column of text differs, the vertical shift that lines them up best (a move), or none
    // (the content replaced). A frame after the page was covered (nearly blank: the cover, "Opening…") that shows again
    // what the first frame showed is the document left coming back for a moment - the cover taken away before the new
    // document was on the screen. The frames of the first round are kept as pictures.
    // Nearly every row as dark as the middle one: a page of one colour with at most a line of text ("Opening…").
    private static bool Covered(double[] rows)
    {
        var sorted = rows.OrderBy(v => v).ToArray();
        var middle = sorted[sorted.Length / 2];
        return rows.Count(v => Math.Abs(v - middle) > 4) < rows.Length / 20;
    }

    // Between two frames that do not line up as a whole: a quarter of the picture (top to bottom) that lines up with the
    // frame before at a shift of more than 2 px while another quarter is unchanged where it was - a part of the page
    // that jumped. The shift, or null. A quarter of plain background lines up with anything: it must hold text.
    private static int? PartShift(double[] rows, double[] last, int h)
    {
        var band = h / 4;
        int? moved = null; var stillBand = false;
        for (var b = 0; b < 4; b++)
        {
            int from = b * band, to = from + band;
            double Diff(int s)
            {
                double d = 0; int n = 0;
                for (var y = Math.Max(from, s); y < Math.Min(to, h + s); y++) { d += Math.Abs(rows[y] - last[y - s]); n++; }
                return n > band / 2 ? d / n : double.MaxValue;
            }
            double spread = 0; for (var y = from + 1; y < to; y++) spread += Math.Abs(rows[y] - rows[y - 1]);
            if (spread / band < 1) continue; // blank
            var still = Diff(0);
            if (still < 0.5) { stillBand = true; continue; }
            // A band that barely changed (a line a pixel off) is not lined up with another place: in a page of like lines
            // (the long document's runs of near-identical lines, 46 px apart at 200%) it matched one three lines away (w10: -140 px for a
            // line 1 px higher; hp: +46 px again and again, the page's own blocks never moving).
            if (still < 3) continue;
            var best = 0; var bestD = still;
            for (var s = -h / 3; s <= h / 3; s++) { var d = Diff(s); if (d < bestD) { bestD = d; best = s; } }
            if (Math.Abs(best) > 2 && bestD < still * 0.25 && bestD < 2) moved ??= best;
        }
        return stillBand ? moved : null;
    }

    private static ScreenMoves WatchScreen(IntPtr window, CancellationToken stop, string? keep)
    {
        GetWindowRect(window, out var r);
        // Below the title, tab and tool bars and above the status bar; the middle half across.
        var w = (r.Right - r.Left) / 2; var x = r.Left + (r.Right - r.Left) / 4;
        var top = r.Top + (r.Bottom - r.Top) / 5; var h = (r.Bottom - r.Top) * 3 / 5;
        if (keep != null) Directory.CreateDirectory(keep);
        var watch = Stopwatch.StartNew();
        double[]? last = null, first = null; int frames = 0, moves = 0, maxShift = 0, replaced = 0, oldAgain = 0;
        var covered = false;
        var events = new List<string>();
        using var bitmap = new System.Drawing.Bitmap(w, h);
        while (!stop.IsCancellationRequested)
        {
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen(x, top, 0, 0, new System.Drawing.Size(w, h));
            var at = watch.ElapsedMilliseconds;
            frames++;
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bitmap.UnlockBits(data);
            // Each row's darkness, and how many of its pixels differ from their right neighbour (text, not a fill).
            var rows = new double[h];
            for (var y = 0; y < h; y++)
            {
                double sum = 0;
                for (var i = 0; i < w; i += 2) { var p = y * data.Stride + i * 4; sum += 255 * 3 - bytes[p] - bytes[p + 1] - bytes[p + 2]; }
                rows[y] = sum / (w / 2);
            }
            double DiffTo(double[] other, int s)
            {
                double d = 0; int n = 0;
                for (var y = Math.Max(0, s); y < h + Math.Min(0, s); y++) { d += Math.Abs(rows[y] - other[y - s]); n++; }
                return n > h / 2 ? d / n : double.MaxValue;
            }
            if (first == null) first = rows;
            else if (!covered && Covered(rows)) { covered = true; events.Add($"{at}ms covered"); }
            else if (covered && DiffTo(first, 0) < 1)
            {
                oldAgain++;
                if (events.Count < 30) events.Add($"{at}ms OLD");
            }
            if (last != null)
            {
                double Diff(int s) => DiffTo(last, s);
                var still = Diff(0);
                // From or to the cover (a page of one colour and "Opening…"), nothing is lined up: its one line of text
                // lines up with any other line somewhere, and a page that came after it read as moved (w10: -116 px,
                // reading mode, the page then showing a picture's empty frame).
                if (still > 0.5 && (Covered(rows) || Covered(last)))
                {
                    replaced++;
                    if (events.Count < 30) events.Add($"{at}ms new({still:0.#})");
                    if (keep != null) bitmap.Save(Path.Combine(keep, $"{frames:000}-{at}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                else if (still > 0.5)
                {
                    var best = 0; var bestD = still;
                    for (var s = -h / 3; s <= h / 3; s++) { var d = Diff(s); if (d < bestD) { bestD = d; best = s; } }
                    if (best != 0 && bestD < still * 0.25 && bestD < 2)
                    {
                        moves++; maxShift = Math.Max(maxShift, Math.Abs(best));
                        if (events.Count < 30) events.Add($"{at}ms {(best > 0 ? "+" : "")}{best}px");
                    }
                    else if (PartShift(rows, last, h) is { } part)
                    {
                        // Not the whole picture: a part of it moved and the rest stayed (the blocks below a place that
                        // changed height, the text above it still) - as much a jump to the reader as the whole moving.
                        moves++; maxShift = Math.Max(maxShift, Math.Abs(part));
                        if (events.Count < 30) events.Add($"{at}ms part {(part > 0 ? "+" : "")}{part}px");
                    }
                    else
                    {
                        replaced++;
                        if (events.Count < 30) events.Add($"{at}ms new({still:0.#})");
                    }
                    if (keep != null) bitmap.Save(Path.Combine(keep, $"{frames:000}-{at}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            else if (keep != null) bitmap.Save(Path.Combine(keep, $"{frames:000}-{at}.png"), System.Drawing.Imaging.ImageFormat.Png);
            last = rows;
        }
        return new ScreenMoves(frames, moves, maxShift, replaced, oldAgain, string.Join(", ", events));
    }
}
