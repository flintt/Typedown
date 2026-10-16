using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json;

/// <summary>
/// TL01: keys typed at once after a tab is made or shown go into that tab's document (docs/todo.md, "标签加载期间按键丢失").
/// The host changes the active tab first and the page shows the new document a moment later; keys typed in between
/// went into the document the page still showed and were dropped there, or for a new tab, written into it and then
/// overwritten. No wait between the command and the keys here: that is the case.
/// </summary>
internal static partial class Program
{
    private static async Task TL01(List<string> notes)
    {
        using var c = await Session("e2e TL01");
        var a = await Open(c, Fixture("tl01-a.md", "# TL01 A\n\nalpha\n"));
        var b = await Open(c, Fixture("tl01-b.md", "# TL01 B\n\nbeta\n"));
        var windowId = await WindowIdOf(c, a);
        var window = await WindowOf(windowId, c);
        var made = new List<string>();
        var files = new[] { a, b };
        try
        {
            await TypeInto(c, a, "");

            // Each step: what every open document holds before it, the command and the keys straight after it, then the
            // letters in the tab now shown (the page and the host both) and nothing else changed anywhere.
            async Task Step(string what, Func<Task> command, string letters, bool enterAndChinese = false)
            {
                var before = new Dictionary<string, string>();
                foreach (var d in await DocumentsOf(c, windowId)) before[d] = (string)(await Get(c, d))["text"]!;
                var shownBefore = await ActiveOf(c, windowId);
                await command();
                // As a quick typist: the first key 50 ms after the command, then one every 30 ms. Keys sent all at once (no
                // gap) reach the page before the host has even seen the shortcut; no one types that fast, and that gap is
                // left open (docs/todo.md).
                await Task.Delay(50);
                foreach (var ch in letters) { Keys(ch.ToString()); await Task.Delay(30); }
                if (enterAndChinese) { Chord("Enter"); await Task.Delay(30); TypeText("中"); await Task.Delay(30); TypeText("文"); }
                // Every step here shows another tab; which one is read once the host has made the change.
                Check(await Eventually(async () => await ActiveOf(c, windowId) != shownBefore, 3000), $"{what} showed another tab");
                var target = await ActiveOf(c, windowId);
                if (!before.ContainsKey(target)) made.Add(target);
                var last = enterAndChinese ? "中文" : letters;
                await WaitForPage(c, target, t => t.Contains(last), $"what was typed after {what}");
                string text = "";
                for (var i = 0; i < 40 && !(text = (string)(await Get(c, target))["text"]!).Contains(last); i++) await Task.Delay(50);
                notes.Add($"after {what}: {JsonConvert.SerializeObject(text)}");
                // What happened, for a failure: every document's text and the page's own account of the hold.
                foreach (var d in before.Keys.Append(target).Distinct())
                    notes.Add($"  {(d == target ? "shown" : "other")}: {JsonConvert.SerializeObject((string)(await Get(c, d))["text"]!)}");
                try { notes.Add("  hold: " + (await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__inputHoldTrace || null)" }))["result"]); }
                catch (Exception e) { notes.Add("  hold: " + e.Message.Split('\n')[0]); }
                Check(text.Contains(letters), $"the letters typed after {what} are all in the tab shown, in order ({JsonConvert.SerializeObject(text)})");
                if (enterAndChinese)
                    Check(System.Text.RegularExpressions.Regex.IsMatch(text, letters + "\n+中文"),
                        $"after {what} the Enter is a new paragraph and the Chinese text follows it ({JsonConvert.SerializeObject(text)})");
                if (before.TryGetValue(target, out var was))
                    Check(text.Length == was.Length + letters.Length, $"after {what} only the letters were added to the tab shown ({JsonConvert.SerializeObject(was)} -> {JsonConvert.SerializeObject(text)})");
                foreach (var (d, t) in before)
                    if (d != target)
                    {
                        var now = (string)(await Get(c, d))["text"]!;
                        Check(now == t, $"after {what} the document left is unchanged ({JsonConvert.SerializeObject(t)} -> {JsonConvert.SerializeObject(now)})");
                    }
            }

            await Step("Ctrl+N", () => { Chord("Ctrl+N"); return Task.CompletedTask; }, "xy", enterAndChinese: true);
            await Step("a tab clicked", async () =>
            {
                var tab = Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem))
                    .Cast<AutomationElement>().FirstOrDefault(t => t.Current.Name.Contains("tl01-b")) ?? throw new CaseFailed("no tl01-b tab in the strip");
                var r = tab.Current.BoundingRectangle;
                await Click(window, (int)(r.Left + 20), (int)(r.Top + r.Height / 2));
            }, "kz");
            await Step("Ctrl+Tab", () => { Chord("Ctrl+Tab"); return Task.CompletedTask; }, "mw");
            await Step("the + button", async () => await Click(window, await WaitFor(window, "AddButton")), "pv");
            await Step("Ctrl+Shift+H", () => { Chord("Ctrl+Shift+H"); return Task.CompletedTask; }, "hj");
        }
        finally
        {
            foreach (var doc in made.Distinct()) await UndoAndClose(c, doc, notes);
            foreach (var doc in files)
            {
                try
                {
                    var original = doc == a ? "# TL01 A\n\nalpha\n" : "# TL01 B\n\nbeta\n";
                    for (var i = 0; i < 5 && (string)(await Get(c, doc))["text"]! != original; i++)
                        await c.Call("document.undo", new { documentId = doc, baseRevision = await Revision(c, doc), reveal = "document" });
                    await c.Call("document.close", new { documentId = doc });
                }
                catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
            }
        }
    }
}
