using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// SR02: the tab the reader was on is the one the next start shows. A window with three documents, the last one edited
/// and not saved, the first one shown again, then the window closed with automatic saving on: the session written
/// names the first as the active one. The close saved the unsaved tab by switching to it first and wrote the session
/// after, so the last tab saved was taken for the active one ("it always opens on the last tab").
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// SR03: the next start shows the tab the reader closed on, the tabs in their order, with untitled documents kept unsaved
    /// among them (Settings > Keep unsaved changes when closing) - closed and started again for real, not only the session
    /// file read (SR02 read it right, and every start still came back on an unsaved one: recovered from its backup, each
    /// untitled document was made the active tab). Tabs a, untitled 1, b, untitled 2. Round 1: a shown - the start shows a,
    /// the four in their order. Round 2: untitled 2 shown - the start shows it, its text with it.
    /// </summary>
    private static async Task SR03(List<string> notes)
    {
        var a = Fixture("sr03-a.md", "# SR03 a\n\nThe file shown.\n");
        var b = Fixture("sr03-b.md", "# SR03 b\n\nAnother file.\n");
        string? windowId = null;
        bool? keepBefore = null, runBefore = null;
        Client c = await Session("e2e SR03");
        // The window's tabs as names: a file's name, an untitled one's first line.
        async Task<List<string>> Tabs()
        {
            // The case's own window while there are others (before the first close), the only one after a start.
            var all = (JArray)(await c.Call("window.list"))["windows"]!;
            var w = all.Any(x => (string?)x["windowId"] == windowId) ? windowId! : (string)all.First()["windowId"]!;
            var names = new List<string>();
            foreach (var d in (JArray)(await c.Call("document.list", new { windowId = w }))["documents"]!)
                names.Add((string?)d["path"] is string p ? Path.GetFileName(p) : (((string?)(await Get(c, (string)d["documentId"]!))["text"]) ?? "").Split('\n')[0]);
            return names.Where(n => n.Contains("sr03") || n.Contains("SR03")).ToList();
        }
        try
        {
            windowId = (string)(await c.Call("test.window.open"))["windowId"]!;
            await Task.Delay(3000);
            keepBefore = (bool?)(await c.Call("test.settings.get", new { windowId, name = "KeepUnsavedOnClose" }))["value"];
            runBefore = (bool?)(await c.Call("test.settings.get", new { windowId, name = "KeepRun" }))["value"];
            await c.Call("test.settings.set", new { windowId, name = "KeepUnsavedOnClose", value = true });
            await c.Call("test.settings.set", new { windowId, name = "KeepRun", value = false });
            var idA = (string)(await c.Call("document.open", new { path = a, windowId, reveal = "document" }))["documentId"]!;
            var idU1 = (string)(await c.Call("document.create", new { windowId, text = "SR03 untitled 1, kept unsaved\n", reveal = "document" }))["documentId"]!;
            await WaitForPage(c, idU1, t => t.Contains("SR03 untitled 1"), "untitled 1");
            await c.Call("document.open", new { path = b, windowId, reveal = "document" });
            var idU2 = (string)(await c.Call("document.create", new { windowId, text = "SR03 untitled 2, kept unsaved\n", reveal = "document" }))["documentId"]!;
            await WaitForPage(c, idU2, t => t.Contains("SR03 untitled 2"), "untitled 2");
            var before = await Tabs();
            notes.Add("tabs before closing: " + string.Join(" | ", before));

            // Round 1: a file shown, two untitled ones unsaved among the tabs.
            await c.Call("document.focus", new { documentId = idA });
            await Task.Delay(800);
            c = await CloseAllAndStart(c, windowId, notes, "round 1");
            var (path1, text1) = await ActiveDocument(c);
            var after1 = await Tabs();
            notes.Add($"round 1, after the start: shown {(path1 != null ? Path.GetFileName(path1) : "untitled: " + text1?.Split('\n')[0])}; tabs {string.Join(" | ", after1)}");
            Check(path1 != null && SamePath(path1, a), $"closed on sr03-a.md with two unsaved untitled documents among the tabs, the start shows sr03-a.md ({path1 ?? text1?.Split('\n')[0]})");
            Check(after1.SequenceEqual(before), $"the tabs come back in their order ({string.Join(" | ", after1)})");

            // Round 2: the second untitled one shown.
            windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!).First()["windowId"]!;
            var docs = (JArray)(await c.Call("document.list", new { windowId }))["documents"]!;
            string? second = null;
            foreach (var d in docs)
                if ((string?)d["path"] == null && (((string?)(await Get(c, (string)d["documentId"]!))["text"]) ?? "").Contains("SR03 untitled 2"))
                    second = (string)d["documentId"]!;
            Check(second != null, "untitled 2 came back as a tab");
            await c.Call("document.focus", new { documentId = second! });
            await Task.Delay(800);
            c = await CloseAllAndStart(c, windowId, notes, "round 2");
            var (path2, text2) = await ActiveDocument(c);
            notes.Add($"round 2, after the start: shown {(path2 != null ? Path.GetFileName(path2) : "untitled: " + text2?.Split('\n')[0])}; tabs {string.Join(" | ", await Tabs())}");
            Check(path2 == null && (text2 ?? "").Contains("SR03 untitled 2"), $"closed on untitled 2, the start shows it with its text ({path2 ?? text2?.Split('\n')[0]})");
        }
        finally
        {
            // The untitled one saved as a file and the documents closed: left kept, every later start would bring it back.
            try
            {
                windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!).First()["windowId"]!;
                foreach (var d in (JArray)(await c.Call("document.list", new { windowId }))["documents"]!)
                {
                    if (d["path"] == null || d["path"]!.Type == JTokenType.Null)
                    {
                        var text = ((string?)(await Get(c, (string)d["documentId"]!))["text"]) ?? "";
                        if (!text.Contains("SR03 untitled")) continue;
                        await c.Call("document.focus", new { documentId = (string)d["documentId"]! });
                        await Task.Delay(500);
                        await c.Call("test.file.saveAs", new { windowId, path = Path.Combine(Path.GetDirectoryName(a)!, text.Contains("untitled 1") ? "sr03-untitled-1.md" : "sr03-untitled-2.md") });
                    }
                }
                foreach (var d in (JArray)(await c.Call("document.list", new { windowId }))["documents"]!)
                    if (((string?)d["path"] ?? "").Contains("sr03-")) try { await c.Call("document.close", new { documentId = (string)d["documentId"]! }); } catch { }
                await c.Call("test.settings.set", new { windowId, name = "KeepUnsavedOnClose", value = keepBefore ?? false });
                await c.Call("test.settings.set", new { windowId, name = "KeepRun", value = runBefore ?? false });
            }
            catch (Exception e) { notes.Add("clean-up: " + e.Message.Split('\n')[0]); }
            c.Dispose();
        }
    }

    // Every window closed as a person closes it (the one given last, so its session is the one written), the process
    // let end, and started again: a client of the new process.
    private static async Task<Client> CloseAllAndStart(Client c, string last, List<string> notes, string label)
    {
        await c.Call("test.settings.flush", new { windowId = last });
        foreach (var w in (JArray)(await c.Call("window.list"))["windows"]!)
        {
            var id = (string)w["windowId"]!;
            if (id == last) continue;
            var other = await WindowOf(id, c);
            PostMessage(other, 0x0010, IntPtr.Zero, IntPtr.Zero);
            await Eventually(async () => { await Task.CompletedTask; return !IsWindow(other); }, 10000);
        }
        var window = await WindowOf(last, c);
        c.Dispose();
        PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
        using (var ending = System.Diagnostics.Process.GetProcessById(hostPid))
        {
            if (!ending.WaitForExit(20000))
            {
                notes.Add($"{label}: the process did not end after its last window closed; ended");
                ending.Kill();
                ending.WaitForExit(10000);
            }
        }
        File.Delete(Path.Combine(testRoot, "automation-endpoint.txt"));
        var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\"") { UseShellExecute = true })!;
        hostPid = started.Id;
        endpoint = await ReadEndpointFile(Path.Combine(testRoot, "automation-endpoint.txt")) ?? throw new CaseFailed("the started host never published its endpoint");
        var fresh = await Connect();
        await Initialize(fresh, "e2e SR03 " + label);
        // Up: a window with its active document, the session's tabs and the untitled ones back.
        await Eventually(async () =>
        {
            var ws = (JArray)(await fresh.Call("window.list"))["windows"]!;
            return ws.Count > 0 && ws[0]["activeDocumentId"] != null && ws[0]["activeDocumentId"]!.Type != JTokenType.Null;
        }, 30000);
        await Task.Delay(1500);
        return fresh;
    }

    // The first window's active document: its path (null for an untitled one) and its text.
    private static async Task<(string? Path, string? Text)> ActiveDocument(Client c)
    {
        var w = ((JArray)(await c.Call("window.list"))["windows"]!).First();
        var id = (string)w["activeDocumentId"]!;
        var doc = ((JArray)(await c.Call("document.list", new { windowId = (string)w["windowId"]! }))["documents"]!).First(d => (string?)d["documentId"] == id);
        var text = (string?)(await Get(c, id))["text"];
        return ((string?)doc["path"], text);
    }

    private static async Task SR02(List<string> notes)
    {
        using var c = await Session("e2e SR02");
        var windowId = (string)(await c.Call("test.window.open"))["windowId"]!;
        var window = await WindowOf(windowId, c);
        await Task.Delay(3000);
        var autoSaveBefore = (bool?)(await c.Call("test.settings.get", new { windowId, name = "AutoSave" }))["value"];
        var session = Path.Combine(testRoot, "data", "session.json");
        var paths = new[] { "sr02-a.md", "sr02-b.md", "sr02-c.md" }.Select(n => Fixture(n, $"# SR02 {n}\n\nText.\n")).ToArray();
        var closed = false;
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = true });
            var ids = new List<string>();
            foreach (var path in paths)
                ids.Add((string)(await c.Call("document.open", new { path, windowId, reveal = "document" }))["documentId"]!);
            await TypeInto(c, ids[2], "Z");
            await WaitForPage(c, ids[2], t => t.Contains('Z'), "the keystroke");
            await c.Call("document.focus", new { documentId = ids[0] });
            await Task.Delay(800);
            var info = (JObject)(await c.Call("document.list", new { windowId }));
            notes.Add("before closing: " + string.Join(", ", ((JArray)info["documents"]!).Select(d => $"{Path.GetFileName((string?)d["path"] ?? "untitled")}{((bool?)d["saved"] == false ? " (unsaved)" : "")}")));
            var written = File.Exists(session) ? File.GetLastWriteTimeUtc(session) : DateTime.MinValue;
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            closed = true;
            Check(await Eventually(async () => { await Task.CompletedTask; return File.Exists(session) && File.GetLastWriteTimeUtc(session) > written; }, 10000), "the window's close writes the session");
            var saved = JObject.Parse(File.ReadAllText(session));
            var files = ((JArray)saved["Files"]!).Select(f => (string)f!).ToList();
            var active = (int)saved["ActiveIndex"]!;
            notes.Add($"session: {string.Join(", ", files.Select(Path.GetFileName))}; active {active} ({(active >= 0 && active < files.Count ? Path.GetFileName(files[active]) : "-")})");
            Check(paths.All(p => files.Any(f => SamePath(f, p))), "the session has the three documents");
            Check(active >= 0 && active < files.Count && SamePath(files[active], paths[0]), "the active one is the tab the reader was on, not the last one saved");
            Check(Disk(paths[2]).Contains('Z'), "the edited document was saved on the way out");
        }
        finally
        {
            if (!closed) try { PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { }
            try
            {
                var other = (string)((JArray)(await c.Call("window.list"))["windows"]!).First()["windowId"]!;
                await c.Call("test.settings.set", new { windowId = other, name = "AutoSave", value = autoSaveBefore ?? false });
            }
            catch { }
        }
    }
}
