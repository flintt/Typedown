using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // A cold start bringing the last session back: how long the window shows an empty tab before the active document is
    // on screen. Reported: with heavy documents among the tabs (showcase, demo1) the window stood blank for a long time.
    // demo1 is the reader's own document, not in the repository: used when the test machine has a copy
    // (C:\e2e\private\demo1.md, E:\src\private\demo1.md); without it the 1M mixed one is the heavy active document.
    private static async Task SR01(List<string> notes)
    {
        var first = Fixture("sr01-a.md", "# SR01 first\n\nA short document.\n");
        var last = Fixture("sr01-c.md", "# SR01 last\n\nA short document.\n");
        string Embedded(string name, bool gz)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var input = gz ? (Stream)new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress) : stream;
            using var reader = new StreamReader(input, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var showcase = Fixture("sr01-showcase.md", Embedded("showcase.md", false));
        var mixed300k = Fixture("sr01-mixed-300k.md", Embedded("hj/mixed-300k.md.gz", true));
        var mixed1m = Fixture("sr01-mixed-1m.md", Embedded("hj/mixed-1m.md.gz", true));
        var demoCopy = new[] { @"C:\e2e\private\demo1.md", @"E:\src\private\demo1.md" }.FirstOrDefault(File.Exists);
        string? demo1 = null;
        if (demoCopy != null) { demo1 = Path.Combine(fixtures, "sr01-demo1.md"); File.Copy(demoCopy, demo1, true); }
        notes.Add(demo1 != null ? $"demo1 from {demoCopy} ({new FileInfo(demo1).Length / 1024} KB)" : "no demo1 copy on this machine: the 1M mixed document instead");
        var heavy = new[] { first, showcase, demo1, mixed300k, mixed1m, last }.Where(f => f != null).Select(f => f!).ToArray();
        var heavyActive = demo1 ?? mixed1m;
        var session = Path.Combine(testRoot, "data", "session.json");
        // Where the window was: put back at the end. Restarted, it came back elsewhere and the cases after found their
        // status bar under the taskbar (SB01, SB02).
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        RECT placed;
        using (var c = await Session("e2e SR01"))
        {
            var w = await FirstWindowId(c);
            GetWindowRect(await WindowOf(w, c), out placed);
            notes.Add($"the window before: {placed.Left},{placed.Top} {placed.Right - placed.Left}x{placed.Bottom - placed.Top}");
            await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 2 });
        }
        try
        {
            var results = new List<(string Label, long Window, long Tabs, long Content)>();
            // The same start with the active document alone: what a start costs anyway, the others' share left out.
            foreach (var (files, active, label) in new[] { (new[] { last }, last, "alone"), (heavy, last, "small-active"), (heavy, heavyActive, "heavy-active") })
            {
                File.WriteAllText(session, JsonConvert.SerializeObject(new { Files = files, ActiveIndex = Array.IndexOf(files, active), Time = DateTime.UtcNow }));
                var (window, tabs, content) = await ColdStart(notes, label, active, files.Length);
                results.Add((label, window, tabs, content));
                if (label != "small-active") continue;
                // The others there and read: one switched to shows its own text, a tab not shown has its file's text.
                using var c = await Session("e2e SR01 tabs");
                var w = await FirstWindowId(c);
                var docs = (JArray)(await c.Call("document.list", new { windowId = w }))["documents"]!;
                notes.Add("tabs: " + string.Join(", ", docs.Select(d => Path.GetFileName((string?)d["path"] ?? "") + ((bool?)d["active"] == true ? " (active)" : ""))));
                Check(docs.Select(d => Path.GetFileName((string?)d["path"] ?? "")).SequenceEqual(heavy.Select(Path.GetFileName)), $"the {heavy.Length} tabs in their order");
                var firstDoc = docs.First(d => ((string?)d["path"] ?? "").EndsWith("sr01-a.md"));
                var firstText = (string?)(await Get(c, (string)firstDoc["documentId"]!))["text"] ?? "";
                Check(firstText.Replace("\r\n", "\n") == File.ReadAllText(first).Replace("\r\n", "\n"), $"a tab not shown has its file's text ({firstText.Length} chars)");
                var showDoc = docs.First(d => ((string?)d["path"] ?? "").EndsWith("sr01-showcase.md"));
                await c.Call("document.focus", new { documentId = (string)showDoc["documentId"]! });
                var shown = false;
                for (var i = 0; i < 60 && !shown; i++)
                {
                    await Task.Delay(250);
                    try { shown = (string?)(await c.Call("window.list"))["windows"]![0]!["activeDocumentId"] == (string)showDoc["documentId"]! && (int?)(await c.Call("test.editor.eval", new { windowId = w, script = "document.querySelectorAll('#ag-editor-id > *').length" }))["result"] > 20; } catch { }
                }
                Check(shown, "switched to, showcase shows its own text");
            }
            notes.Add("restore lines: " + string.Join(" | ", LogLines("RestoreSession").Concat(LogLines("startup:")).Concat(LogLines("asked for the document")).OrderBy(l => l).TakeLast(12)));
            var alone = results[0];
            var small = results[1];
            Check(small.Content >= 0 && alone.Content >= 0, "the active document came on screen at all");
            // The others not waited for: on screen about as soon as when it is the only one.
            Check(small.Content - small.Window <= alone.Content - alone.Window + 700, $"with {heavy.Length - 1} heavy tabs more the active document is on screen about as soon as alone ({small.Content - small.Window} ms after the window, alone {alone.Content - alone.Window} ms)");
            // The tabs at once, before the page is up: 2.6 s after the window on w10 while they came with the active file.
            Check(small.Tabs >= 0 && small.Tabs - small.Window <= 1000, $"the {heavy.Length} tabs are in the strip within 1 s of the window ({small.Tabs - small.Window} ms)");
            var heavyOne = results[2];
            Check(heavyOne.Content >= 0 && heavyOne.Content - heavyOne.Window <= 6000, $"the active heavy document's top is on screen within 6 s of the window ({heavyOne.Content - heavyOne.Window} ms)");
        }
        finally
        {
            using var c = await Session("e2e SR01 restore");
            var w = await FirstWindowId(c);
            try { await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 0 }); } catch { }
            try
            {
                var window = await WindowOf(w, c);
                GetWindowRect(window, out var now);
                notes.Add($"the window after the restarts: {now.Left},{now.Top} {now.Right - now.Left}x{now.Bottom - now.Top}");
                if (now.Left != placed.Left || now.Top != placed.Top || now.Right != placed.Right || now.Bottom != placed.Bottom)
                    MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, placed.Right - placed.Left, placed.Bottom - placed.Top, 0x0004 | 0x0010);
            }
            catch (Exception e) { notes.Add("the window not put back: " + e.Message.Split('\n')[0]); }
        }
    }

    /// <summary>
    /// The host killed and started again, timed from its start: the window listed, the tabs all there, and the active
    /// document the expected one with its text laid out in the window and the editor shown (not hidden, not covered).
    /// Screens at 1, 2, 4 and 8 s.
    /// </summary>
    private static async Task<(long Window, long Tabs, long Content)> ColdStart(List<string> notes, string label, string activePath, int tabs)
    {
        try
        {
            using var pre = await Session("e2e flush");
            foreach (var w in (JArray)(await pre.Call("window.list"))["windows"]!)
                await pre.Call("test.settings.flush", new { windowId = (string)w["windowId"]! });
        }
        catch (Exception e) { notes.Add("flush before the restart: " + e.Message.Split('\n')[0]); }
        using (var dying = Process.GetProcessById(hostPid))
        {
            dying.Kill();
            dying.WaitForExit(10000);
        }
        File.Delete(Path.Combine(testRoot, "automation-endpoint.txt"));
        var clock = Stopwatch.StartNew();
        var restarted = Process.Start(new ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\"") { UseShellExecute = true })!;
        hostPid = restarted.Id;
        // The tabs as the strip shows them (UI Automation), not as the automation API lists them: that waits for the
        // page's startup document, and gave the page's time for the strip's.
        long strip = -1;
        var stripWatch = Task.Run(async () =>
        {
            while (clock.ElapsedMilliseconds < 30000 && strip < 0)
            {
                try
                {
                    restarted.Refresh();
                    var handle = restarted.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        var items = Deep.All(System.Windows.Automation.AutomationElement.FromHandle(handle), new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.TabItem));
                        if (items.Count >= tabs) strip = clock.ElapsedMilliseconds;
                    }
                }
                catch (Exception) { }
                await Task.Delay(100);
            }
        });
        var shots = Task.Run(async () =>
        {
            foreach (var at in new[] { 1000, 2000, 4000, 8000 })
            {
                var wait = at - (int)clock.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay(wait);
                notes.Add($"{label} at {at / 1000} s: " + Screenshot($"sr01-{label}-{at / 1000}s"));
            }
        });
        endpoint = await ReadEndpointFile(Path.Combine(testRoot, "automation-endpoint.txt")) ?? throw new CaseFailed("the restarted host never published its endpoint");
        var published = clock.ElapsedMilliseconds;
        using var c = await Connect();
        await Initialize(c, "e2e SR01 cold");
        long window = -1, all = -1, content = -1;
        JToken last = new JObject();
        while (clock.ElapsedMilliseconds < 60000 && content < 0)
        {
            try
            {
                var windows = (JArray)(await c.Call("window.list"))["windows"]!;
                if (windows.Count > 0)
                {
                    var windowId = (string)windows[0]["windowId"]!;
                    if (window < 0) window = clock.ElapsedMilliseconds;
                    if (all < 0 && (await DocumentsOf(c, windowId)).Count >= tabs) all = clock.ElapsedMilliseconds;
                    // The active document the expected one, and text of it laid out in the window.
                    var activeId = (string?)windows[0]["activeDocumentId"];
                    var docs = (JArray)(await c.Call("document.list", new { windowId }))["documents"]!;
                    var isActive = docs.Any(d => (string?)d["documentId"] == activeId && string.Equals(Path.GetFileName((string?)d["path"] ?? ""), Path.GetFileName(activePath), StringComparison.OrdinalIgnoreCase));
                    var text = (bool?)(await c.Call("test.editor.eval", new { windowId, script = "[...document.querySelectorAll('#ag-editor-id > *')].slice(0, 40).some(e => { const r = e.getBoundingClientRect(); return r.height > 0 && r.bottom > 0 && r.top < innerHeight && e.textContent.trim().length > 0 })" }))["result"] == true;
                    last = await c.Call("test.webview.state", new { windowId });
                    var app = last["app"] ?? last;
                    var visible = (string?)app["editorVisibility"] != "Collapsed" && (string?)app["viewVisibility"] != "Collapsed" && (bool?)app["leavingCover"] != true && ((double?)app["editorOpacity"] ?? 1) > 0.99;
                    if (isActive && text && visible) content = clock.ElapsedMilliseconds;
                }
            }
            catch (Exception) { }
            await Task.Delay(150);
        }
        await shots;
        await stripWatch;
        notes.Add($"{label}: endpoint {published} ms, window {window} ms, {tabs} tabs in the strip {strip} ms (listed {all} ms), active document on screen {content} ms; last state {last.ToString(Formatting.None)}");
        try { await c.Call("test.dialogs.dismiss", new { }); } catch { }
        return (window, strip, content);
    }
}
