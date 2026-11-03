using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// SF01: a window opening under the dark theme shows no light frame on its way: from the moment it is on screen, its
/// client area is sampled as often as the screen can be read for two seconds, and no sample may be light. The old host kept a new window hidden
/// until its content had loaded; the WinUI 3 window was shown at once, before its content.
/// </summary>
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeInt(IntPtr window, int attribute, out int value, int size);

    /// <summary>The window kept off the screen by DWM (DWMWA_CLOAKED): the host cloaks a new one until its first frame.</summary>
    private static bool IsCloaked(IntPtr window) => DwmGetWindowAttributeInt(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static async Task SF01(List<string> notes)
    {
        using var c = await Session("e2e SF01");
        var theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
        await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id = "dark" }));
        await Task.Delay(1500);
        List<IntPtr> HostWindows()
        {
            var found = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid == hostPid && IsWindowVisible(window) && GetWindowRect(window, out var r) && r.Right - r.Left > 300 && r.Bottom - r.Top > 200)
                    found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }
        var before = HostWindows();
        string? windowId = null;
        IntPtr shown = IntPtr.Zero;
        try
        {
            var opening = c.Call("test.window.open");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 8000 && shown == IntPtr.Zero)
            {
                shown = HostWindows().FirstOrDefault(w => !before.Contains(w));
                if (shown == IntPtr.Zero) await Task.Delay(5);
            }
            if (shown == IntPtr.Zero) throw new CaseFailed("no new window came on screen");
            var appeared = clock.ElapsedMilliseconds;
            {
                var all = new List<string>();
                EnumWindows((h, _) =>
                {
                    GetWindowThreadProcessId(h, out var pid);
                    if (pid == hostPid && IsWindowVisible(h)) { var t = new System.Text.StringBuilder(200); GetWindowText(h, t, t.Capacity); GetWindowRect(h, out var rr); all.Add($"{h.ToInt64():x}{(h == shown ? "*" : "")}{(before.Contains(h) ? " before" : "")} {rr.Right - rr.Left}x{rr.Bottom - rr.Top} '{t.ToString().Split(" - ")[0]}'"); }
                    return true;
                }, IntPtr.Zero);
                notes.Add("the host's windows when one came up (* taken as the new one): " + string.Join("; ", all));
            }
            // Five points of the client area (the middle and around it), physical pixels on the screen.
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            var samples = new List<(long At, double Light)>();
            // What lay under the middle of the window and what was in front, per sample: the screen is read where the
            // window is, so another window over it would be measured instead.
            var covered = new List<(long At, IntPtr Over, IntPtr Front)>();
            string Title(IntPtr h) { var t = new System.Text.StringBuilder(200); GetWindowText(h, t, t.Capacity); return h == shown ? "the new window" : $"{h.ToInt64():x} '{t}'"; }
            var sampling = System.Diagnostics.Stopwatch.StartNew();
            while (sampling.ElapsedMilliseconds < 2000)
            {
                if (GetWindowRect(shown, out var r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    // The window's area in one read of the screen, and how much of it is light: a light frame is a light
                    // window, not a light pixel - five points once landed on the white text of the home page's recent
                    // files (a dark window), which the cases before had filled.
                    var lightPixels = 0;
                    var all = 0;
                    using (var shot = new System.Drawing.Bitmap(w, h))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
                        var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            var row = new byte[data.Stride];
                            for (var y = 0; y < h; y += 8)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                                for (var x = 0; x < w; x += 8)
                                {
                                    var i = x * 4;
                                    all++;
                                    if (0.0722 * row[i] + 0.7152 * row[i + 1] + 0.2126 * row[i + 2] > 150) lightPixels++;
                                }
                            }
                        }
                        finally { shot.UnlockBits(data); }
                    }
                    samples.Add((sampling.ElapsedMilliseconds, all == 0 ? 0 : 100.0 * lightPixels / all));
                    var over = GetAncestor(WindowFromPoint(new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 }), 2);
                    if (over != shown) covered.Add((sampling.ElapsedMilliseconds, over, GetForegroundWindow()));
                }
                await Task.Delay(10);
            }
            windowId = (string?)(await opening)["windowId"];
            // What each window's page was told and paints: the page's colour comes with the host's theme message, its
            // text from the theme's style sheet - a light page with light text means the two disagreed.
            const string themeState = "JSON.stringify({ body: getComputedStyle(document.body).backgroundColor, bodyStyle: document.body.style.backgroundColor, theme: document.documentElement.style.getPropertyValue('--actualTheme'), sheet: (document.getElementById('link_style_editor') || {}).href, text: getComputedStyle(document.body).color, webgl: !!document.createElement('canvas').getContext('webgl') })";
            foreach (var w in (JArray)(await c.Call("window.list"))["windows"]!)
            {
                var id = (string)w["windowId"]!;
                try { notes.Add($"page of {(id == windowId ? "the new window" : id)}: {(await c.Call("test.editor.eval", new { windowId = id, script = themeState }))["result"]}"); }
                catch (Exception e) { notes.Add($"page of {id}: {e.Message}"); }
                try
                {
                    var state = await c.Call("test.webview.state", new { windowId = id });
                    notes.Add($"paint of {(id == windowId ? "the new window" : id)}: editor op {state["editorOpacity"]}, web view {state["viewColour"]}; {state["paint"]}");
                }
                catch (Exception e) { notes.Add($"paint of {id}: {e.Message}"); }
            }
            // Light over a quarter of the window: the editor's or the home page's text stays far below that.
            var light = samples.Where(s => s.Light > 25).ToList();
            notes.Add($"on screen {appeared} ms after the call; {samples.Count} samples, most light {samples.Max(s => s.Light):0}% of the window; " +
                string.Join(" ", samples.Where((_, i) => i % 6 == 0).Take(20).Select(s => $"{s.At}:{s.Light:0}%")));
            // The window drawn on its own (PrintWindow, as the window paints itself, whatever lies behind or over it),
            // against the screen at the same points: light on the screen and dark here, the screen shows something else.
            if (GetWindowRect(shown, out var pr))
            {
                int pw = pr.Right - pr.Left, ph = pr.Bottom - pr.Top;
                using var own = new System.Drawing.Bitmap(pw, ph);
                using (var g = System.Drawing.Graphics.FromImage(own))
                {
                    var hdc = g.GetHdc();
                    try { PrintWindow(shown, hdc, 2); } finally { g.ReleaseHdc(hdc); }
                }
                using var screen = new System.Drawing.Bitmap(pw, ph);
                using (var g = System.Drawing.Graphics.FromImage(screen)) g.CopyFromScreen(pr.Left, pr.Top, 0, 0, new System.Drawing.Size(pw, ph));
                double L(System.Drawing.Color k) => 0.2126 * k.R + 0.7152 * k.G + 0.0722 * k.B;
                notes.Add("at the end, the window drawn on its own / the screen: " + string.Join(" ", new[] { (0.5, 0.5), (0.3, 0.4), (0.7, 0.7) }.Select(p =>
                    $"{L(own.GetPixel((int)(pw * p.Item1), (int)(ph * p.Item2))):0}/{L(screen.GetPixel((int)(pw * p.Item1), (int)(ph * p.Item2))):0}")));
                own.Save(System.IO.Path.Combine(outputDir, "SF01-own.png"));
                screen.Save(System.IO.Path.Combine(outputDir, "SF01-screen.png"));
            }
            if (covered.Count > 0)
                notes.Add($"the new window was not on top in {covered.Count} samples, from {covered[0].At} ms: over it {Title(covered[0].Over)}, in front {Title(covered[0].Front)}");
            Check(light.Count == 0, $"the dark window showed a light frame {light.Count} times in its first two seconds (first at {light.FirstOrDefault().At} ms, {light.FirstOrDefault().Light:0}% of it light)");
        }
        finally
        {
            if (shown != IntPtr.Zero) { PostMessage(shown, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1000); }
            await SetSetting(c, "appearance.theme", theme);
        }
    }

    /// <summary>
    /// SF03: SF01 the other way round - a window opening under the light theme shows no dark frame on its way (a reader's
    /// report, 2026-11-01: a brief black screen at start, a light theme with Windows' apps dark). Sampled as SF01 does:
    /// the window's area read from the screen every 10 ms for two seconds, dark over a quarter of it is a dark frame. The
    /// light page's text is far below that. Each sample's dark share goes into the notes, with when the window came up.
    /// </summary>
    private static async Task SF03(List<string> notes)
    {
        using var c = await Session("e2e SF03");
        var theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
        await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id = "light" }));
        await Task.Delay(1500);
        List<IntPtr> HostWindows()
        {
            var found = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var pid);
                if (pid == hostPid && IsWindowVisible(window) && GetWindowRect(window, out var r) && r.Right - r.Left > 300 && r.Bottom - r.Top > 200)
                    found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }
        var before = HostWindows();
        IntPtr shown = IntPtr.Zero;
        try
        {
            var opening = c.Call("test.window.open");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 8000 && shown == IntPtr.Zero)
            {
                shown = HostWindows().FirstOrDefault(w => !before.Contains(w));
                if (shown == IntPtr.Zero) await Task.Delay(5);
            }
            if (shown == IntPtr.Zero) throw new CaseFailed("no new window came on screen");
            var appeared = clock.ElapsedMilliseconds;
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            var samples = new List<(long At, double Dark)>();
            var sampling = System.Diagnostics.Stopwatch.StartNew();
            var savedFirstDark = false;
            while (sampling.ElapsedMilliseconds < 2000)
            {
                if (GetWindowRect(shown, out var r))
                {
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    var darkPixels = 0;
                    var all = 0;
                    using var shot = new System.Drawing.Bitmap(w, h);
                    using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
                    var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new byte[data.Stride];
                        for (var y = 0; y < h; y += 8)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                            for (var x = 0; x < w; x += 8)
                            {
                                var i = x * 4;
                                all++;
                                if (0.0722 * row[i] + 0.7152 * row[i + 1] + 0.2126 * row[i + 2] < 60) darkPixels++;
                            }
                        }
                    }
                    finally { shot.UnlockBits(data); }
                    var dark = all == 0 ? 0 : 100.0 * darkPixels / all;
                    samples.Add((sampling.ElapsedMilliseconds, dark));
                    if (dark > 25 && !savedFirstDark)
                    {
                        shot.Save(System.IO.Path.Combine(outputDir, "SF03-first-dark.png"));
                        savedFirstDark = true;
                    }
                }
                await Task.Delay(10);
            }
            await opening;
            var darkFrames = samples.Where(s => s.Dark > 25).ToList();
            notes.Add($"on screen {appeared} ms after the call; {samples.Count} samples, most dark {samples.Max(s => s.Dark):0}% of the window; " +
                string.Join(" ", samples.Where((_, i) => i % 6 == 0).Take(25).Select(s => $"{s.At}:{s.Dark:0}%")));
            if (darkFrames.Count > 0)
                notes.Add($"dark from {darkFrames.First().At} ms to {darkFrames.Last().At} ms after it came up ({darkFrames.Count} samples); the first saved as SF03-first-dark.png");
            Check(darkFrames.Count == 0, $"the light window showed a dark frame {darkFrames.Count} times in its first two seconds (first at {darkFrames.FirstOrDefault().At} ms, {darkFrames.FirstOrDefault().Dark:0}% of it dark)");
        }
        finally
        {
            if (shown != IntPtr.Zero) { PostMessage(shown, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1000); }
            await SetSetting(c, "appearance.theme", theme);
        }
    }

    /// <summary>
    /// SF04: SF03 as the reader starts the app (2026-11-01, still a black flash at every start): the host started cold,
    /// bringing back a session of three tabs (a long document shown, two others not), the light theme with the Apricot
    /// page and Windows' apps dark - and once more with Windows' apps light. From the process's start the window is
    /// read from the screen as often as it can be for five seconds - a patch of the editor's area, the reader saw a
    /// single dark frame; over half of it dark is a dark frame. The first dark patches are saved, with the startup lines of the debug log for when they came.
    /// </summary>
    private static Task SF04(List<string> notes) =>
        // Under 80: the empty window before XAML drew in it was a dark grey of 63, the Apricot page over 200.
        ColdStartFlash(notes, "SF04", "typeleaf-apricot", "dark", (r, g, b) => 0.0722 * b + 0.7152 * g + 0.2126 * r < 80);

    /// <summary>
    /// SF05: SF04 under a light custom theme the other way: no WHITE frame. A reader (hp88, the Store package 1.4.1.13,
    /// Sepia) saw the editor's area flash white as the app started and brought its tabs back; SF04 looks for dark frames
    /// only. Sepia's page is #f4ecd8 - its blue 216; white, the editor page's own light (#f9f9f9) and the app's light grey
    /// are all over 240 in every channel. A read whose patch is over half such a colour is a white frame.
    /// </summary>
    private static Task SF05(List<string> notes) =>
        ColdStartFlash(notes, "SF05", "sepia", "white", (r, g, b) => b >= 236 && 0.0722 * b + 0.7152 * g + 0.2126 * r >= 230);

    /// <summary>
    /// A cold start restoring three tabs under a custom theme, Windows' apps dark and then light: the editor's patch read
    /// from the screen from the process's start, and any stretch of reads over half <paramref name="wrongName"/>
    /// (<paramref name="wrong"/> says which pixels are) a failure, saved and timed against the startup log.
    /// </summary>
    private static async Task ColdStartFlash(List<string> notes, string id, string customThemeId, string wrongName, Func<byte, byte, byte, bool> wrong)
    {
        string Embedded(string name)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var reader = new System.IO.StreamReader(stream, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var n = id.ToLowerInvariant();
        var files = new[] { Fixture(n + "-a.md", $"# {id} first\n\nA short document.\n"), Fixture(n + "-showcase.md", Embedded("showcase.md")), Fixture(n + "-c.md", $"# {id} last\n\nA short document.\n") };
        var session = System.IO.Path.Combine(testRoot, "data", "session.json");
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var personalize = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var systemLight = personalize?.GetValue("AppsUseLightTheme") as int? ?? 1;
        JToken theme;
        string? customTheme;
        RECT placed;
        using (var c = await Session("e2e " + id))
        {
            var w = await FirstWindowId(c);
            GetWindowRect(await WindowOf(w, c), out placed);
            theme = (await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!;
            customTheme = (string?)(await c.Call("test.settings.get", new { windowId = w, name = "CustomTheme" }))["value"];
            await SetSetting(c, "appearance.theme", JObject.FromObject(new { kind = "builtIn", id = "light" }));
            await c.Call("test.settings.set", new { windowId = w, name = "CustomTheme", value = customThemeId });
            await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 2 });
        }
        var failures = new List<string>();
        try
        {
            foreach (var dark in new[] { true, false })
            {
                var label = dark ? "apps dark" : "apps light";
                SetSystemTheme(!dark);
                System.IO.File.WriteAllText(session, Newtonsoft.Json.JsonConvert.SerializeObject(new { Files = files, ActiveIndex = 1, Time = DateTime.UtcNow }));
                try
                {
                    using var pre = await Session("e2e flush");
                    foreach (var w in (JArray)(await pre.Call("window.list"))["windows"]!)
                        await pre.Call("test.settings.flush", new { windowId = (string)w["windowId"]! });
                }
                catch (Exception e) { notes.Add("flush before the restart: " + e.Message.Split('\n')[0]); }
                using (var dying = System.Diagnostics.Process.GetProcessById(hostPid))
                {
                    dying.Kill();
                    dying.WaitForExit(10000);
                }
                await Task.Delay(1000);
                System.IO.File.Delete(System.IO.Path.Combine(testRoot, "automation-endpoint.txt"));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var restarted = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\"") { UseShellExecute = true })!;
                hostPid = restarted.Id;
                var samples = new List<(long At, double Wrong)>();
                long appeared = -1;
                var saved = 0;
                long cloakedAt = -1;
                IntPtr window = IntPtr.Zero;
                while (clock.ElapsedMilliseconds < 8000 && (appeared < 0 || clock.ElapsedMilliseconds - appeared < 5000))
                {
                    if (window == IntPtr.Zero)
                    {
                        EnumWindows((h, _) =>
                        {
                            GetWindowThreadProcessId(h, out var pid);
                            if (pid == hostPid && IsWindowVisible(h) && GetWindowRect(h, out var rr) && rr.Right - rr.Left > 300 && rr.Bottom - rr.Top > 200)
                            {
                                // Cloaked, it is not on the screen yet: what is read there is what lies behind it.
                                if (!IsCloaked(h)) { window = h; return false; }
                                if (cloakedAt < 0) cloakedAt = clock.ElapsedMilliseconds;
                            }
                            return true;
                        }, IntPtr.Zero);
                        if (window == IntPtr.Zero) { await Task.Delay(2); continue; }
                        appeared = clock.ElapsedMilliseconds;
                    }
                    if (!GetWindowRect(window, out var r)) break;
                    // A patch of the editor's area (the middle, below the tab strip and the toolbar), not the whole
                    // window: the reader saw one dark frame, and a read of the whole window takes longer than a frame.
                    // The patch is read every millisecond or two.
                    int width = 160, height = 120;
                    int left = (r.Left + r.Right) / 2 - width / 2, top = r.Top + (r.Bottom - r.Top) * 55 / 100 - height / 2;
                    var wrongPixels = 0;
                    var all = 0;
                    using var shot = new System.Drawing.Bitmap(width, height);
                    using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
                    var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new byte[data.Stride];
                        for (var y = 0; y < height; y += 4)
                        {
                            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                            for (var x = 0; x < width; x += 4)
                            {
                                var i = x * 4;
                                all++;
                                if (wrong(row[i + 2], row[i + 1], row[i])) wrongPixels++;
                            }
                        }
                    }
                    finally { shot.UnlockBits(data); }
                    var share = all == 0 ? 0 : 100.0 * wrongPixels / all;
                    var at = clock.ElapsedMilliseconds;
                    samples.Add((at, share));
                    // The first wrong patches, with the whole window read once right after.
                    if (share > 50 && saved < 4)
                    {
                        shot.Save(System.IO.Path.Combine(outputDir, $"{id}-{(dark ? "dark" : "light")}-{saved}-{at}ms.png"));
                        if (saved == 0) notes.Add($"{label}: the window just after the first {wrongName} patch: " + Screenshot($"{id}-{(dark ? "dark" : "light")}-window"));
                        saved++;
                    }
                }
                // Wrong stretches: from and to (ms from the start), with how many reads each had.
                var stretches = new List<(long From, long To, int Reads)>();
                foreach (var s in samples)
                {
                    if (s.Wrong <= 50) continue;
                    if (stretches.Count > 0 && s.At - stretches[^1].To <= 20) stretches[^1] = (stretches[^1].From, s.At, stretches[^1].Reads + 1);
                    else stretches.Add((s.At, s.At, 1));
                }
                var gaps = samples.Zip(samples.Skip(1), (x, y) => y.At - x.At).DefaultIfEmpty(0).ToList();
                notes.Add($"{label}: the window up {cloakedAt} ms after the start (cloaked; -1: never seen so), on screen {appeared} ms; {samples.Count} reads of the editor's patch (longest gap {gaps.Max()} ms), most {wrongName} {(samples.Count > 0 ? samples.Max(s => s.Wrong) : 0):0}%; {wrongName} stretches: " +
                    (stretches.Count == 0 ? "none" : string.Join(", ", stretches.Take(10).Select(t => $"{t.From}-{t.To} ms ({t.Reads} reads)"))));
                notes.Add($"{label}: every 100th read: " + string.Join(" ", samples.Where((_, i) => i % 100 == 0).Take(40).Select(s => $"{s.At}:{s.Wrong:0}%")));
                if (stretches.Count > 0)
                    failures.Add($"{label}: the editor's area {wrongName} {stretches.Count} time(s), first {stretches[0].From}-{stretches[0].To} ms from the start (the window up at {appeared} ms)");
                endpoint = await ReadEndpointFile(System.IO.Path.Combine(testRoot, "automation-endpoint.txt")) ?? throw new CaseFailed("the restarted host never published its endpoint");
                using (var probe = await Connect())
                {
                    await Initialize(probe, $"e2e {id} cold");
                    await WaitForWindow(probe);
                    await Task.Delay(1500);
                    try { await probe.Call("test.dialogs.dismiss", new { }); } catch { }
                }
                notes.Add($"{label}: startup lines: " + string.Join(" | ", LogLines("startup:").TakeLast(9).Concat(LogLines("window: on screen").TakeLast(1)).Concat(LogLines("window: cloak").TakeLast(2))));
            }
            Check(failures.Count == 0, string.Join("; ", failures));
        }
        finally
        {
            SetSystemTheme(systemLight != 0);
            using var c = await Session($"e2e {id} restore");
            var w = await FirstWindowId(c);
            try { await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 0 }); } catch { }
            try { await c.Call("test.settings.set", new { windowId = w, name = "CustomTheme", value = customTheme ?? "" }); } catch { }
            try { await SetSetting(c, "appearance.theme", theme); } catch { }
            try
            {
                var window = await WindowOf(w, c);
                GetWindowRect(window, out var now);
                if (now.Left != placed.Left || now.Top != placed.Top || now.Right != placed.Right || now.Bottom != placed.Bottom)
                    MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, placed.Right - placed.Left, placed.Bottom - placed.Top, 0x0004 | 0x0010);
            }
            catch (Exception e) { notes.Add("the window not put back: " + e.Message.Split('\n')[0]); }
        }
    }
}
