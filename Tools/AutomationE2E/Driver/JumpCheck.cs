using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// JU01: a long document comes back where it was left without the page moving under the reader - switched back to in
/// its tab, and opened in a window of its own as the start-up restores its tabs. A reader saw the page jump as the
/// editor took over from a first-screen preview drawn another way. The window's text column is watched frame by frame:
/// from the moment the document's text is there, every frame must be the final picture, not the final picture moved up
/// or down. The text at the top of the window must also be what was there when it was left. As a control, the old way
/// (the preview, then the whole document drawn at once) is run too: the watch has to see its movement, or it could
/// not see this one either.
/// </summary>
internal static partial class Program
{
    // One frame of the text column: the mean brightness of each second row.
    private static double[] RowProfile(IntPtr window)
    {
        if (!GetWindowRect(window, out var r)) return Array.Empty<double>();
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int left = r.Left + w * 3 / 10, width = w * 4 / 10, top = r.Top + 150, height = h - 220;
        if (width <= 0 || height <= 0) return Array.Empty<double>();
        using var shot = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
        var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        shot.UnlockBits(data);
        var rows = new double[height / 2];
        for (var y = 0; y < rows.Length; y++)
        {
            double sum = 0; var n = 0;
            for (var x = 0; x < width; x += 3)
            {
                var i = y * 2 * data.Stride + x * 4;
                sum += 0.299 * bytes[i + 2] + 0.587 * bytes[i + 1] + 0.114 * bytes[i];
                n++;
            }
            rows[y] = sum / n;
        }
        return rows;
    }

    private static double Spread(double[] rows)
    {
        if (rows.Length == 0) return 0;
        var mean = rows.Average();
        return Math.Sqrt(rows.Sum(v => (v - mean) * (v - mean)) / rows.Length);
    }

    // How far (in rows of two pixels) the frame is the final picture moved, and how much better that fits than none.
    private static (int Shift, double AtZero, double Best) ShiftOf(double[] frame, double[] final)
    {
        double Diff(int s)
        {
            double sum = 0; var n = 0;
            for (var y = 0; y < frame.Length; y++)
            {
                var j = y + s;
                if (j < 0 || j >= final.Length) continue;
                sum += Math.Abs(frame[y] - final[j]); n++;
            }
            return n < frame.Length / 2 ? double.MaxValue : sum / n;
        }
        var atZero = Diff(0);
        var best = (Shift: 0, Diff: atZero);
        for (var s = -200; s <= 200; s++) { var d = Diff(s); if (d < best.Diff) best = (s, d); }
        return (best.Shift, atZero, best.Diff);
    }

    // Watches the window for a while and says which frames, once the text was there, showed the final picture moved.
    private static async Task<(int Moved, int Frames, long? TextAt, string Detail)> WatchForJumps(IntPtr window, int ms)
    {
        var frames = new List<(long At, double[] Rows)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            frames.Add((clock.ElapsedMilliseconds, RowProfile(window)));
            await Task.Delay(1);
        }
        var final = frames[^1].Rows;
        var first = frames.FindIndex(f => Spread(f.Rows) > 4);
        if (first < 0) return (0, frames.Count, null, "no text seen");
        var moved = new List<string>();
        foreach (var (at, rows) in frames.Skip(first))
        {
            if (rows.Length != final.Length) continue;
            var (shift, atZero, best) = ShiftOf(rows, final);
            // The final picture moved: nearly the same rows, shifted - not another picture (the document switched from).
            // By more than one row of two pixels: a frame a pixel or two off (the page's whole-pixel scrolling against
            // blocks of fractional heights) is not a jump anyone sees, and the watch reads rows two pixels apart anyway.
            if (Math.Abs(shift) > 1 && best < 3 && atZero > best * 1.5 + 0.5) moved.Add($"{at}ms:{shift * 2}px");
        }
        return (moved.Count, frames.Count - first, frames[first].At, moved.Count == 0 ? "" : string.Join(" ", moved.Take(12)));
    }

    // The editor's block at the top of the window: the start of its text and its height.
    private const string TopBlockScript = "(()=>{const e=document.getElementById('ag-editor-id');if(!e)return 'none';const r=e.getBoundingClientRect();const x=r.left+Math.min(r.width/2,300);for(const y of [2,16,40,80]){const b=document.elementFromPoint(x,y)?.closest('.ag-paragraph');if(b&&e.contains(b))return JSON.stringify({text:(b.textContent||'').replace(/\\s+/g,' ').trim().slice(0,40),top:Math.round(b.getBoundingClientRect().top)})}return 'none'})()";

    private static async Task JU01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e JU01");
        // Text only (no diagram or picture drawn later), paragraphs of different lengths: a moved picture shows.
        // As a real document is: headings, lists, quotes and code among the paragraphs (all drawn at once, no diagram).
        string Doc(string tag) => $"# JU01 {tag}\n\n" + string.Concat(Enumerable.Range(0, 2500).Select(i =>
            (i % 40 == 0 ? $"## {tag} part {i / 40}\n\n" : "")
            + (i % 15 == 7 ? $"- item {i} one\n- item {i} two with **bold** text\n- item {i} three\n\n" : "")
            + (i % 23 == 11 ? $"> A quote at {i}, with `code` in it.\n\n" : "")
            + (i % 31 == 19 ? $"```js\nconst x{i} = {i};\nconsole.log(x{i});\n```\n\n" : "")
            + $"Paragraph {i} of {tag}: " + string.Join(" ", Enumerable.Repeat("words", 4 + i * 7 % 23)) + ".\n\n"));
        var aPath = Fixture("ju01-a.md", Doc("A"));
        var a = await Open(c, aPath);
        // B looks nothing like A (headings and code), so its picture is not taken for A's moved.
        var b = await Open(c, Fixture("ju01-b.md", "# JU01 B\n\n" + string.Concat(Enumerable.Range(0, 1200).Select(i => $"## Heading {i}\n\n```\ncode line {i}\n```\n\n"))));
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        string? other = null;
        try
        {
            async Task<string> Eval(string script, string? win = null) => (string?)(await c.Call("test.editor.eval", new { windowId = win ?? windowId, script }))["result"] ?? "";
            async Task WhollyDrawn(string? win = null) => await Eventually(async () => await Eval("String(!window.__typedownMuya?.renderPending)", win) == "true", 20000);

            // A scrolled half way, its place remembered (the page reports it as it scrolls), then B.
            async Task<string> LeaveAHalfWay()
            {
                await c.Call("document.focus", new { documentId = a });
                await Activate(window);
                await Task.Delay(1500);
                await WhollyDrawn();
                await Eval("window.scrollTo(0, document.documentElement.scrollHeight * 0.55); 'ok'");
                await Task.Delay(1200);
                var top = await Eval(TopBlockScript);
                await c.Call("document.focus", new { documentId = b });
                await Task.Delay(1500);
                await WhollyDrawn();
                // Nothing kept: A is drawn anew when it comes back.
                await Eval("window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear(); 'ok'");
                return top;
            }

            // In its tab.
            var before = await LeaveAHalfWay();
            var watch = WatchForJumps(window, 4000);
            await Task.Delay(150);
            await c.Call("document.focus", new { documentId = a });
            var tab = await watch;
            await WhollyDrawn();
            var tabTop = await Eval(TopBlockScript);
            notes.Add($"switched back: {tab.Frames} frames from the text at {tab.TextAt} ms, moved {tab.Moved} {tab.Detail}; top before {before}, after {tabTop}");
            Check(before != "none" && tab.TextAt != null, "the document was seen coming back");
            Check(tab.Moved == 0, $"switched back to, the page does not move once its text is there ({tab.Moved} frames moved: {tab.Detail})");
            Check(SameTop(before, tabTop), $"switched back to, the top of the window is what it was ({before} / {tabTop})");

            // Back and forth between two documents like a reader's (one of 30,000 characters with diagrams and code, one of
            // 11,000 plain), nothing changed in them: no "Opening…" over the page, which shows the document left until the
            // next takes its place. It flashed up on every switch - neither is kept (too short, diagrams), and drawing the
            // first part with its diagrams takes longer than the 150 ms it waited.
            async Task<(int Shown, int Loads)> Opening()
            {
                var o = await c.Call("test.editor.opening", new { windowId });
                return ((int)o["shown"]!, (int)o["loads"]!);
            }
            var rich = await Open(c, Fixture("ju01-rich.md", "# JU01 rich\n\n" + string.Concat(Enumerable.Range(0, 60).Select(i =>
                $"## Section {i}\n\nSome text for section {i}, a few lines of it to read before the picture.\n\n"
                + (i % 4 == 0 ? $"```mermaid\ngraph TD\n  A{i}[Start {i}] --> B{i}{{Choice}}\n  B{i} -->|yes| C{i}[One]\n  B{i} -->|no| D{i}[Two]\n  C{i} --> E{i}[End]\n  D{i} --> E{i}\n```\n\n" : "")
                + (i % 3 == 1 ? $"```python\ndef f{i}(x):\n    return x * {i}\n```\n\n" : "")
                + (i % 5 == 2 ? $"```js\nconst v{i} = [{i}, {i + 1}].map(n => n * 2);\n```\n\n" : "")
                + string.Concat(Enumerable.Range(0, 3).Select(k => $"Paragraph {k} of section {i}: " + string.Join(" ", Enumerable.Repeat("words", 12 + (i + k) % 9)) + ".\n\n"))))));
            var plain = await Open(c, Fixture("ju01-plain.md", "# JU01 plain\n\n" + string.Concat(Enumerable.Range(0, 180).Select(i => $"Line {i} of the plain document, with some words in it to read.\n\n"))));
            await c.Call("document.focus", new { documentId = rich });
            await Task.Delay(2500);
            var openingBefore = await Opening();
            for (var i = 0; i < 6; i++)
            {
                await c.Call("document.focus", new { documentId = i % 2 == 0 ? plain : rich });
                await Task.Delay(1500);
            }
            var openingAfter = await Opening();
            notes.Add($"six switches between a document with diagrams and a plain one: \"Opening…\" shown {openingAfter.Shown - openingBefore.Shown} times over {openingAfter.Loads - openingBefore.Loads} long loads");
            Check(openingAfter.Loads - openingBefore.Loads >= 6 && openingAfter.Shown == openingBefore.Shown, $"switching between long documents does not say it is opening ({openingAfter.Shown - openingBefore.Shown} of {openingAfter.Loads - openingBefore.Loads})");
            try { await c.Call("document.close", new { documentId = rich }); } catch { }
            try { await c.Call("document.close", new { documentId = plain }); } catch { }

            // The watch sees a move when there is one: the page moved 120 px and back while it watches.
            await Activate(window);
            await Task.Delay(300);
            watch = WatchForJumps(window, 2500);
            await Task.Delay(600);
            var down = await Eval("(() => { const y = window.scrollY; window.scrollBy(0, 120); return y + ' > ' + window.scrollY })()");
            await Task.Delay(500);
            var up = await Eval("(() => { const y = window.scrollY; window.scrollBy(0, -120); return y + ' > ' + window.scrollY })()");
            var control = await watch;
            notes.Add($"control (the page moved 120 px and back: {down}, {up}): moved {control.Moved} of {control.Frames} frames {control.Detail}");
            Check(control.Moved >= 3, $"the watch sees the page move when it does ({control.Moved} frames)");

            // The old way, for the record: the static preview, then the whole document at once.
            before = await LeaveAHalfWay();
            await Eval("window.__typedownProgressiveOff = true; 'ok'");
            watch = WatchForJumps(window, 4000);
            await Task.Delay(150);
            await c.Call("document.focus", new { documentId = a });
            var old = await watch;
            await Eval("window.__typedownProgressiveOff = false; 'ok'");
            notes.Add($"the old way (preview, then all at once): moved {old.Moved} of {old.Frames} frames {old.Detail}");

            // In a window of its own, as the start-up restores a tab: A closed where it was left, opened anew.
            before = await LeaveAHalfWay();
            await c.Call("document.focus", new { documentId = a });
            await Task.Delay(1500);
            await WhollyDrawn();
            before = await Eval(TopBlockScript);
            await c.Call("document.close", new { documentId = a });
            await Task.Delay(800);
            var opened = await c.Call("test.window.open", new { path = aPath });
            other = (string)opened["windowId"]!;
            var otherWindow = await WindowOf(other, c);
            var start = await WatchForJumps(otherWindow, 5000);
            await WhollyDrawn(other);
            var startTop = await Eval(TopBlockScript, other);
            notes.Add($"a window opening with it: {start.Frames} frames from the text at {start.TextAt} ms, moved {start.Moved} {start.Detail}; top before {before}, after {startTop}");
            Check(start.TextAt != null, "the document was seen in the new window");
            Check(start.Moved == 0, $"opened in a window of its own, the page does not move once its text is there ({start.Moved} frames moved: {start.Detail})");
            Check(SameTop(before, startTop), $"opened in a window of its own, the top of the window is what it was ({before} / {startTop})");
        }
        finally
        {
            if (other != null) try { PostMessage(await WindowOf(other, c), 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1500); } catch { }
            try { await c.Call("document.close", new { documentId = b }); } catch { }
            try { await c.Call("document.close", new { documentId = a }); } catch { }
        }
    }

    // The same block at the top, within a few pixels.
    private static bool SameTop(string before, string after)
    {
        try
        {
            var x = Newtonsoft.Json.Linq.JObject.Parse(before); var y = Newtonsoft.Json.Linq.JObject.Parse(after);
            return (string?)x["text"] == (string?)y["text"] && Math.Abs((double)x["top"]! - (double)y["top"]!) <= 3;
        }
        catch { return false; }
    }
}
