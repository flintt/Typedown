using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// OQ01: files asked to be opened at the same moment all open, each in a tab of its own with its own text. Each open put
/// a tab of its own in front and then read its file; by the time a read ended another open's tab was shown, and A08's
/// guard (a file read while another tab was shown is not put into it) dropped every open but the last (hp job 586, eight
/// at once: seven "The file could not be opened") - as several files opened together from Explorer could be, each
/// handed to the running window by a launch of its own, on a disk slower to read than a process is to start.
/// </summary>
internal static partial class Program
{
    private static async Task OQ01(List<string> notes)
    {
        var folder = FixtureFolder("oq01");
        var paths = Enumerable.Range(1, 6).Select(i => Path.Combine(folder, $"oq01-{i}.md")).ToList();
        for (var i = 0; i < paths.Count; i++) File.WriteAllText(paths[i], $"# oq01 document {i + 1}\n\nits own text {i + 1}\n");
        using var c = await Session("e2e OQ01");
        var first = await Open(c, Fixture("oq01-start.md", "# oq01 start\n"));
        var windowId = await WindowIdOf(c, first);
        var window = await WindowOf(windowId, c);
        var ids = new List<string>();
        try
        {
            // All asked at once; each answer kept, a refusal as its message.
            var answers = await Task.WhenAll(paths.Select(async p =>
            {
                try { return (p, id: await Open(c, p), error: (string?)null); }
                catch (Exception ex) { return (p, id: (string?)null, error: ex.Message.Split('\n')[0]); }
            }));
            ids.AddRange(answers.Where(a => a.id != null).Select(a => a.id!));
            var refused = answers.Where(a => a.id == null).ToList();
            notes.Add($"opened {ids.Count} of {paths.Count}" + (refused.Count > 0 ? "; refused: " + string.Join(", ", refused.Select(r => $"{Path.GetFileName(r.p)} ({r.error})")) : ""));
            Check(refused.Count == 0, "every file asked at once is opened");
            foreach (var (p, id, _) in answers.Where(a => a.id != null))
            {
                var got = await Get(c, id!);
                var n = Path.GetFileNameWithoutExtension(p).Split('-')[1];
                Check(SamePath((string?)got["path"], p) && ((string?)got["text"] ?? "").Contains($"its own text {n}"), $"{Path.GetFileName(p)} has its own path and text");
            }
            Check(ids.Distinct().Count() == ids.Count, "each in a document of its own");
            await Task.Delay(1000);
            var tabs = Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>()
                .Select(t => t.Current.Name).Where(n => n.Contains("oq01-") && !n.Contains("start")).ToList();
            notes.Add("tabs: " + string.Join(" | ", tabs));
            Check(tabs.Count == paths.Count, "each in a tab of its own");
        }
        finally
        {
            foreach (var id in ids.Append(first)) { try { await c.Call("document.close", new { documentId = id }); } catch { } }
        }
    }
}
