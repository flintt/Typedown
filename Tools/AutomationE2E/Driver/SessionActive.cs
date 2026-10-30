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
