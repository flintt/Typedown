using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// SF02: a long document under a dark custom theme (Dracula) shows the theme's colour, never white and never the app's
/// own dark colour, when the settings are left and when a window opens with it (as the start-up restores its tabs). A
/// reader saw the app's dark colour flash before Dracula's, and a light one under a light custom theme: the page was
/// loaded again on leaving the settings, painted the app theme's colour until the custom theme's CSS came, and the
/// control around it was transparent over the window's colour while the long document was drawn.
/// </summary>
internal static partial class Program
{
    // Dracula's page colour and text colour (Tools/Themes/generate.py); the app's dark page colour.
    private static readonly (int R, int G, int B) DraculaBg = (0x28, 0x2a, 0x36), DraculaFg = (0xf8, 0xf8, 0xf2), AppDark = (0x28, 0x28, 0x28);

    // One read of the window's area: the share of it that is light, the app's dark colour, Dracula's page colour, and its text.
    // `textArea` (screen pixels): the text counted there only, of that area - the editor's. Over the whole window, any
    // near-white pixel counted (Dracula's text is near white): the settings page's own text sliding out passed for the
    // document's.
    private static (double Light, double AppDark, double Theme, double Text) ThemeShares(IntPtr window, RECT? textArea = null)
    {
        if (!GetWindowRect(window, out var r)) return (0, 0, 0, 0);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return (0, 0, 0, 0);
        int light = 0, appDark = 0, theme = 0, text = 0, all = 0, inArea = 0;
        bool InArea(int x, int y) => textArea is not { } a || (r.Left + x >= a.Left && r.Left + x < a.Right && r.Top + y >= a.Top && r.Top + y < a.Bottom);
        bool Near(int cr, int cg, int cb, (int R, int G, int B) c, int tolerance) =>
            Math.Abs(cr - c.R) <= tolerance && Math.Abs(cg - c.G) <= tolerance && Math.Abs(cb - c.B) <= tolerance;
        using (var shot = new System.Drawing.Bitmap(w, h))
        {
            using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
            var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            shot.UnlockBits(data);
            for (var y = 0; y < h; y += 6)
                for (var x = 0; x < w; x += 6)
                {
                    var i = y * data.Stride + x * 4;
                    int b = bytes[i], gg = bytes[i + 1], rr = bytes[i + 2];
                    all++;
                    if (0.299 * rr + 0.587 * gg + 0.114 * b > 150) light++;
                    if (Near(rr, gg, b, AppDark, 2)) appDark++;
                    if (Near(rr, gg, b, DraculaBg, 3)) theme++;
                    if (!InArea(x, y)) continue;
                    inArea++;
                    if (Near(rr, gg, b, DraculaFg, 12)) text++;
                }
        }
        return (light * 100.0 / all, appDark * 100.0 / all, theme * 100.0 / all, inArea == 0 ? 0 : text * 100.0 / inArea);
    }

    // Where the editor is on the screen, a little inside its edges. The web view is drawn into the window's composition
    // (its Chrome_WidgetWin_0 child is 0x0), so it is placed by its size (test.webview.state): at the window's right
    // edge, right of the side pane, above the 32 px status bar.
    private static async Task<RECT?> EditorArea(Client c, string windowId, IntPtr window)
    {
        var state = await c.Call("test.webview.state", new { windowId });
        static (double W, double H)? Size(string? s) =>
            s?.Split('x') is [var w, var h] && double.TryParse(w, out var dw) && double.TryParse(h, out var dh) ? (dw, dh) : null;
        if (Size((string?)state["viewSize"]) is not { } view || Size((string?)state["rootSize"]) is not { } root || root.W <= 0) return null;
        if (!GetWindowRect(window, out var r)) return null;
        var scale = (r.Right - r.Left) / root.W;
        int Px(double dip) => (int)Math.Round(dip * scale);
        var bottom = r.Bottom - Px(32) - Px(12);
        return new RECT { Left = r.Right - Px(view.W) + Px(24), Right = r.Right - Px(24), Bottom = bottom, Top = bottom - Px(view.H) + Px(64) };
    }

    // The window's frames for a while: each one's shares, and the time the document's text first covers some of it - more
    // than half a percent of the window, or with `textArea`, `textNeeded` percent of that area.
    private static async Task<(List<(long At, double Light, double AppDark, double Theme, double Text)> Frames, long? TextAt)> WatchTheme(IntPtr window, int ms, RECT? textArea = null, double textNeeded = 0.5)
    {
        var frames = new List<(long, double, double, double, double)>();
        long? textAt = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            var (light, appDark, theme, text) = ThemeShares(window, textArea);
            frames.Add((clock.ElapsedMilliseconds, light, appDark, theme, text));
            if (textAt == null && (textArea == null ? text > textNeeded : text >= textNeeded)) textAt = clock.ElapsedMilliseconds;
            await Task.Delay(1);
        }
        return (frames, textAt);
    }

    // Frames after the theme's colour first covered most of the window in which it no longer does: the window going black
    // or white (the web view shown before its page painted) between the theme's colour and the document.
    private static int LeftTheTheme(List<(long At, double Light, double AppDark, double Theme, double Text)> frames)
    {
        var first = frames.FindIndex(f => f.Theme >= 50);
        return first < 0 ? 0 : frames.Skip(first).Count(f => f.Theme < 40);
    }

    private static string Describe(List<(long At, double Light, double AppDark, double Theme, double Text)> frames) =>
        string.Join(" ", frames.Where((_, i) => i % Math.Max(1, frames.Count / 8) == 0)
            .Select(f => $"{f.At}:L{f.Light:0}/D{f.AppDark:0}/T{f.Theme:0}/x{f.Text:0.0}"));

    private static async Task SF02(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e SF02");
        var text = "# SF02\n\n" + string.Concat(Enumerable.Range(0, 3000).Select(i => (i % 1000 == 0 ? $"## Part {i / 1000}\n\n" : "") + $"Paragraph {i} of the long document, with a few words to make it long enough to take a while.\n\n"));
        var file = Fixture("sf02.md", text);
        var first = await Open(c, file);
        var windowId = await WindowIdOf(c, first);
        var window = await WindowOf(windowId, c);
        var themeBefore = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomTheme" }))["value"];
        string? otherWindow = null;
        IntPtr otherHandle = IntPtr.Zero;
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "dracula" });
            await TypeInto(c, first, "");
            await Task.Delay(2500);

            // Out of the settings and back. The document's text is told by the editor's area against what it showed there
            // before: back when 70 % of that is there again.
            var editorArea = await EditorArea(c, windowId, window);
            var textBefore = editorArea == null ? 0 : ThemeShares(window, editorArea).Text;
            notes.Add(editorArea is { } ea ? $"the editor's area {ea.Right - ea.Left}x{ea.Bottom - ea.Top} at {ea.Left},{ea.Top}, its text before {textBefore:0.00} %" : "the editor's size not known: the whole window's text");
            Check(editorArea == null || textBefore > 0.2, $"the document's text is on screen before the settings ({textBefore:0.00} % of the editor's area)");
            await CtrlComma();
            await Task.Delay(2000);
            if (editorArea != null)
            {
                // The settings page over it: the document's text is gone from there (else nothing below is a way back). The
                // settings' own near-white text is about a third of the document's (w10, 0.19 % of 0.56 %).
                var inSettings = ThemeShares(window, editorArea).Text;
                notes.Add($"in the settings: {inSettings:0.00} % of the editor's area is the document's text colour");
                Check(inSettings < textBefore * 0.5,$"the settings page covers the document ({inSettings:0.00} % text left of {textBefore:0.00} %)");
            }
            await CtrlComma();
            var (back, backText) = editorArea == null ? await WatchTheme(window, 2500) : await WatchTheme(window, 2500, editorArea, textBefore * 0.7);
            var flashBack = back.Count(f => f.Light > 25 || f.AppDark > 25) + LeftTheTheme(back);
            notes.Add($"leaving the settings: {back.Count} frames, text at {backText} ms; {Describe(back)}");
            Check(flashBack == 0, $"leaving the settings shows no light, app-dark or other frame once in the theme's colour ({flashBack} of {back.Count})");
            Check(backText != null && backText < 400, $"leaving the settings, the document is there at once ({backText} ms)");

            // A window opening with the long document, as the start-up restores its tabs (or Explorer opens it).
            // Another file of the same text: a file open already is shown in its window, not opened in a new one.
            var second = Fixture("sf02-second.md", text);
            var opened = await c.Call("test.window.open", new { path = second });
            otherWindow = (string)opened["windowId"]!;
            otherHandle = await WindowOf(otherWindow, c);
            var (open, openText) = await WatchTheme(otherHandle, 4000);
            var flashOpen = open.Count(f => f.Light > 25 || f.AppDark > 25) + LeftTheTheme(open);
            notes.Add($"a window opening with it: {open.Count} frames, text at {openText} ms; {Describe(open)}");
            Check(flashOpen == 0, $"the window opening with the long document shows no light, app-dark or other frame once in the theme's colour ({flashOpen} of {open.Count})");
        }
        finally
        {
            if (otherHandle != IntPtr.Zero) { PostMessage(otherHandle, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1500); } // WM_CLOSE
            try { await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = themeBefore ?? "" }); } catch { }
            try { await c.Call("document.close", new { documentId = first }); } catch { }
        }
    }
}
