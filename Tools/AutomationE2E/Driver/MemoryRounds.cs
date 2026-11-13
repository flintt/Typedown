using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// MM01: memory over many mode and document switches on Windows (the store's two Memory failures on 1.3.9.0; todo
/// "2026-11-10 Partner Center Memory failures"). A long document with formulas and footnotes switched between visual
/// and source mode 70 times, then two documents swapped 40 times; the private memory of the host and everything it
/// started (WebView2) and the page's JS heap read every 10 rounds. After the first 10 (the warm-up: caches filling,
/// code compiled) the private memory left once the web view has given back its caches (garbage collected, a critical memory
/// pressure, the memory target low) may not grow by more than 80 MB: each switch left a footnote tool and about
/// 4.7 MB of heap behind before 34a8341e, 280 MB over 60 rounds. The numbers go into the notes either way.
/// </summary>
internal static partial class Program
{
    private static async Task MM01(List<string> notes)
    {
        var folder = FixtureFolder("mm01");
        string Document(string name, int sections)
        {
            var sb = new StringBuilder($"# {name}\n\n");
            for (var i = 0; i < sections; i++)
                sb.Append($"## Section {i}\n\nA paragraph with **bold**, *emphasis*, a [link](https://example.com/{i}) and a formula $x_{i}^2 + \\frac{{{i}}}{{2}}$, and a note[^n{i}].\n\n")
                  .Append($"$$\n\\sum_{{k=0}}^{{{i}}} k^2\n$$\n\n- one item {i}\n- another item {i}\n\n[^n{i}]: Footnote {i}.\n\n");
            return sb.ToString();
        }
        var a = Path.Combine(folder, "mm01-a.md");
        var b = Path.Combine(folder, "mm01-b.md");
        File.WriteAllText(a, Document("mm01 A", 500));
        File.WriteAllText(b, Document("mm01 B", 200));
        using var c = await Session("e2e MM01");
        // What the cases before left (the host is one process for the run): memory, windows and documents open, before
        // this case opens anything - round 0 came after its own document and had the run's history in it (1070-1190 MB in
        // a full run against 656 MB after two cases; the user asked, 2026-11-14).
        {
            var entry = TreeMemory(hostPid);
            var windows = (Newtonsoft.Json.Linq.JArray)(await c.Call("window.list"))["windows"]!;
            var documents = 0;
            foreach (var w in windows)
                documents += ((Newtonsoft.Json.Linq.JArray)(await c.Call("document.list", new { windowId = (string)w["windowId"]! }))["documents"]!).Count;
            notes.Add($"before the case: private {entry.privateMb:F0} MB in {entry.processes} processes; {windows.Count} window(s), {documents} document(s) open");
            notes.Add("before the case by process: " + string.Join(", ", TreeMemoryEach(hostPid).Select(p => $"{p.Name}#{p.Pid} {p.PrivateMb:F0}")));
        }
        var idA = await Open(c, a);
        var windowId = await WindowIdOf(c, idA);
        string? idB = null;
        async Task<string> PageMode()
        {
            var r = (string?)(await c.Call("test.editor.eval", new { windowId, script = "document.querySelector('.CodeMirror') ? 'source' : (document.querySelector('#ag-editor-id') ? 'visual' : 'none')" }))["result"];
            return r ?? "none";
        }
        async Task SetMode(bool source)
        {
            await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = source });
            var want = source ? "source" : "visual";
            await Eventually(async () => await PageMode() == want, 20000, 50);
            await Task.Delay(300);
        }
        async Task<double> JsHeapMb()
        {
            try { return Convert.ToDouble((await c.Call("test.editor.eval", new { windowId, script = "performance.memory ? performance.memory.usedJSHeapSize : 0" }))["result"]) / 1048576; }
            catch { return -1; }
        }
        var samples = new List<(int round, double priv, double js)>();
        async Task Sample(int round)
        {
            await Task.Delay(1500);
            var tree = TreeMemory(hostPid);
            var js = await JsHeapMb();
            samples.Add((round, tree.privateMb, js));
            notes.Add($"round {round}: private {tree.privateMb:F0} MB in {tree.processes} processes, JS heap {js:F1} MB");
            // Which process it is (the host or one of WebView2's), by process id across the rounds.
            notes.Add($"round {round} by process: " + string.Join(", ", TreeMemoryEach(hostPid).Select(p => $"{p.Name}#{p.Pid} {p.PrivateMb:F0}")));
            notes.Add($"round {round} page: {await PageCounters()}");
        }
        // What the page holds (DevTools): nodes, documents and listeners - a switch leaving its page behind shows here - and
        // the JS heap exactly (performance.memory gives 9.5 MB rounded, every round).
        async Task<string> PageCounters()
        {
            try
            {
                var dom = (await c.Call("test.webview.cdp", new { windowId, method = "Memory.getDOMCounters" }))["result"]!;
                var heap = (await c.Call("test.webview.cdp", new { windowId, method = "Runtime.getHeapUsage" }))["result"]!;
                return $"nodes {dom["nodes"]}, documents {dom["documents"]}, listeners {dom["jsEventListeners"]}, JS heap {(double)heap["usedSize"]! / 1048576:F1} of {(double)heap["totalSize"]! / 1048576:F1} MB";
            }
            catch (Exception e) { return "no counters: " + e.Message.Split('\n')[0]; }
        }
        // The private memory once the page and the web view have given back what they can: garbage collected, a critical
        // memory pressure, the memory target low (then normal again). What is left is held, not cache.
        var purged = new Dictionary<int, double>();
        async Task Purged(int round)
        {
            try
            {
                // The web view gives memory back in its own time: once was not enough (w10 job 495: 914 MB after 70 mode
                // switches, 677 MB ten swaps later with the larger document open, the page's nodes and listeners the same).
                // Asked again until two readings are within 15 MB, four times at most; the lowest kept.
                await c.Call("test.webview.memoryTarget", new { windowId, level = "low" });
                var readings = new List<double>();
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    await c.Call("test.webview.cdp", new { windowId, method = "HeapProfiler.collectGarbage" });
                    await c.Call("test.webview.cdp", new { windowId, method = "Memory.simulatePressureNotification", @params = "{\"level\":\"critical\"}" });
                    await Task.Delay(3000);
                    readings.Add(TreeMemory(hostPid).privateMb);
                    if (readings.Count >= 2 && Math.Abs(readings[^1] - readings[^2]) < 15) break;
                }
                purged[round] = readings.Min();
                notes.Add($"round {round} purged: private {purged[round]:F0} MB (readings {string.Join(", ", readings.Select(r => r.ToString("F0")))}); " + string.Join(", ", TreeMemoryEach(hostPid).Select(p => $"{p.Name}#{p.Pid} {p.PrivateMb:F0}")) + $"; page {await PageCounters()}");
            }
            catch (Exception e) { notes.Add($"round {round} not purged: " + e.Message.Split('\n')[0]); }
            finally { try { await c.Call("test.webview.memoryTarget", new { windowId, level = "normal" }); } catch { } }
        }
        try
        {
            await Eventually(async () => await PageShows(c, windowId, "Section 499"), 60000, 200);
            await Sample(0);
            for (var round = 1; round <= 70; round++)
            {
                await SetMode(true);
                await SetMode(false);
                if (round % 10 == 0) await Sample(round);
                if (round == 10 || round == 70) await Purged(round);
            }
            // The second document opened (its own page state, measured on its own), then the two swapped 40 times: after the
            // first 10 swaps no more growth either. 20 swaps measured once came to +295 MB against round 70 with the
            // opening in it (w10 job 400): not told apart from a leak.
            idB = await Open(c, b);
            await Eventually(async () => await PageShows(c, windowId, "Section 199"), 60000, 200);
            await Sample(71);
            for (var swap = 1; swap <= 40; swap++)
            {
                await c.Call("document.focus", new { documentId = swap % 2 == 1 ? idA : idB });
                await Task.Delay(700);
                if (swap % 10 == 0) await Sample(70 + swap);
                if (swap == 10 || swap == 40) await Purged(70 + swap);
            }
            // Judged on the memory left once purged: the raw numbers have up to a gigabyte of cache the web view gives back
            // under pressure in them, and swung by hundreds of megabytes from run to run (swaps 10 -> 40 raw: hp +214, -47;
            // w10 +278 - purged -12 and +53, the page's nodes and listeners the same before and after; jobs 489, 492, 493).
            var warm = samples.First(s => s.round == 10);
            var last = samples.First(s => s.round == 70);
            notes.Add($"mode switches 10 -> 70 raw: private {last.priv - warm.priv:+0;-0} MB");
            Check(purged.ContainsKey(10) && purged.ContainsKey(70), "the memory measured once purged after 10 and 70 mode switches");
            var growth = purged[70] - purged[10];
            notes.Add($"mode switches 10 -> 70 once purged: {growth:+0;-0} MB ({purged[10]:F0} -> {purged[70]:F0})");
            Check(growth < 80, $"60 mode switches after the warm-up leave less than 80 MB more private memory once purged ({growth:+0;-0} MB)");
            var opened = samples.First(s => s.round == 71);
            var swapWarm = samples.First(s => s.round == 80);
            var swapLast = samples.First(s => s.round == 110);
            notes.Add($"second document opened: private {opened.priv - last.priv:+0;-0} MB; swaps 10 -> 40 raw: {swapLast.priv - swapWarm.priv:+0;-0} MB");
            Check(purged.ContainsKey(80) && purged.ContainsKey(110), "the memory measured once purged after 10 and 40 swaps");
            var swapGrowth = purged[110] - purged[80];
            notes.Add($"swaps 10 -> 40 once purged: {swapGrowth:+0;-0} MB ({purged[80]:F0} -> {purged[110]:F0})");
            Check(swapGrowth < 80, $"30 document swaps after the first 10 leave less than 80 MB more private memory once purged ({swapGrowth:+0;-0} MB)");
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = false }); } catch { }
            foreach (var d in new[] { idA, idB }) if (d != null) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
    }
}
