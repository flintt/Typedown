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
    // on screen. Reported: with a long document among the tabs the window stood blank for a long time.
    private static async Task SR01(List<string> notes)
    {
        var first = Fixture("sr01-a.md", "# SR01 first\n\nA short document.\n");
        var big = Fixture("sr01-big.md", "# SR01 big\n\n" + string.Concat(Enumerable.Range(0, 3200).Select(i => $"Paragraph {i}: " + string.Join(" ", Enumerable.Repeat("words", 15)) + ".\n\n")));
        var last = Fixture("sr01-c.md", "# SR01 last\n\nA short document.\n");
        var session = Path.Combine(testRoot, "data", "session.json");
        using (var c = await Session("e2e SR01"))
        {
            var w = await FirstWindowId(c);
            await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 2 });
        }
        try
        {
            var results = new List<(string Label, long Window, long Tabs, long Content)>();
            foreach (var (active, heading, label) in new[] { (2, "SR01 last", "small-active"), (1, "SR01 big", "big-active") })
            {
                File.WriteAllText(session, JsonConvert.SerializeObject(new { Files = new[] { first, big, last }, ActiveIndex = active, Time = DateTime.UtcNow }));
                var (window, tabs, content) = await ColdStart(notes, label, heading, 3);
                results.Add((label, window, tabs, content));
            }
            notes.Add("restore lines: " + string.Join(" | ", LogLines("RestoreSession").TakeLast(4)));
            var small = results[0];
            Check(small.Content >= 0, "the active short document came on screen at all");
            // From the window shown to its active document readable: the other documents' loading is not to be waited for.
            Check(small.Content - small.Window <= 3000, $"the active short document is on screen within 3 s of the window ({small.Content - small.Window} ms; window {small.Window} ms, all tabs {small.Tabs} ms)");
            var bigOne = results[1];
            Check(bigOne.Content >= 0 && bigOne.Content - bigOne.Window <= 5000, $"the active long document's top is on screen within 5 s of the window ({bigOne.Content - bigOne.Window} ms)");
        }
        finally
        {
            using var c = await Session("e2e SR01 restore");
            var w = await FirstWindowId(c);
            try { await c.Call("test.settings.set", new { windowId = w, name = "FileStartupAction", value = 0 }); } catch { }
        }
    }

    /// <summary>
    /// The host killed and started again, timed from its start: the window listed, the tabs all there, and the active
    /// document's heading laid out and the editor shown (not hidden, not covered). Screens at 1, 2, 4 and 8 s.
    /// </summary>
    private static async Task<(long Window, long Tabs, long Content)> ColdStart(List<string> notes, string label, string heading, int tabs)
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
                    var shown = (string?)(await c.Call("test.editor.eval", new { windowId, script = "(() => { const h = document.querySelector('#ag-editor-id h1'); const r = h && h.getBoundingClientRect(); return h && r.height > 0 && r.bottom > 0 && r.top < innerHeight ? h.textContent : '' })()" }))["result"] ?? "";
                    last = await c.Call("test.webview.state", new { windowId });
                    var app = last["app"] ?? last;
                    var visible = (string?)app["editorVisibility"] != "Collapsed" && (string?)app["viewVisibility"] != "Collapsed" && (bool?)app["leavingCover"] != true && ((double?)app["editorOpacity"] ?? 1) > 0.99;
                    if (shown.Contains(heading) && visible) content = clock.ElapsedMilliseconds;
                }
            }
            catch (Exception) { }
            await Task.Delay(150);
        }
        await shots;
        notes.Add($"{label}: endpoint {published} ms, window {window} ms, all {tabs} tabs {all} ms, active document on screen {content} ms; last state {last.ToString(Formatting.None)}");
        try { await c.Call("test.dialogs.dismiss", new { }); } catch { }
        return (window, all, content);
    }
}
