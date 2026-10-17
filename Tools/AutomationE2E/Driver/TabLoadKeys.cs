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
        // B is long (some 300,000 characters): shown again it takes the page a while, and keys typed meanwhile are what the
        // hold keeps. A short document is in the page before a quick typist's first key, hold or not. Few headings: with
        // thousands the outline kept the window's thread busy for half a second, and the keyboard reached the page only
        // after it - another matter (docs/todo.md).
        var bText = "# TL01 B\n\nbeta\n\n" + string.Concat(Enumerable.Range(0, 3000).Select(i => (i % 1000 == 0 ? $"## Part {i / 1000}\n\n" : "") + $"Paragraph {i} of the long document, with a few words to make it long enough to take a while.\n\n"));
        var b = await Open(c, Fixture("tl01-b.md", bText));
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
                // As a quick typist: the first key 150 ms after the command, then one every 60 ms. The letters go in as
                // characters, past any input method: with a Chinese one on (the W10 PC has one), Enter commits letters
                // being composed instead of making a paragraph, and the next letters wait in a composition - lost to the
                // case, not to the app. Keys sent with no gap reach the page before the host has seen the shortcut (a gap
                // the hold leaves open, docs/todo.md).
                await Task.Delay(150);
                foreach (var ch in letters) { TypeText(ch.ToString()); await Task.Delay(60); }
                if (enterAndChinese) { Chord("Enter"); await Task.Delay(60); TypeText("中"); await Task.Delay(60); TypeText("文"); }
                // Every step here shows another tab; which one is read once the host has made the change.
                Check(await Eventually(async () => await ActiveOf(c, windowId) != shownBefore, 3000), $"{what} showed another tab");
                var target = await ActiveOf(c, windowId);
                if (!before.ContainsKey(target)) made.Add(target);
                var last = enterAndChinese ? "中文" : letters;
                try { await WaitForPage(c, target, t => t.Contains(last), $"what was typed after {what}"); }
                catch (CaseFailed)
                {
                    try { notes.Add($"  hold, {what}: " + (await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__inputHoldTrace || null)" }))["result"]); } catch { }
                    throw;
                }
                string text = "";
                for (var i = 0; i < 40 && !(text = (string)(await Get(c, target))["text"]!).Contains(last); i++) await Task.Delay(50);
                string Short(string t) => JsonConvert.SerializeObject(t.Length > 60 ? t[..40] + "…" + t[^15..] : t) + (t.Length > 60 ? $" ({t.Length} chars)" : "");
                notes.Add($"after {what}: {Short(text)}");
                // What happened, for a failure: every document's text and the page's own account of the hold.
                foreach (var d in before.Keys.Append(target).Distinct())
                    notes.Add($"  {(d == target ? "shown" : "other")}: {Short((string)(await Get(c, d))["text"]!)}");
                try { notes.Add("  hold: " + (await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__inputHoldTrace || null)" }))["result"]); }
                catch (Exception e) { notes.Add("  hold: " + e.Message.Split('\n')[0]); }
                Check(text.Contains(letters), $"the letters typed after {what} are all in the tab shown, in order ({Short(text)})");
                if (enterAndChinese)
                    Check(System.Text.RegularExpressions.Regex.IsMatch(text, letters + "\n+中文"),
                        $"after {what} the Enter is a new paragraph and the Chinese text follows it ({Short(text)})");
                if (before.TryGetValue(target, out var was))
                    Check(text.Length == was.Length + letters.Length, $"after {what} only the letters were added to the tab shown ({Short(was)} -> {Short(text)})");
                foreach (var (d, t) in before)
                    if (d != target)
                    {
                        var now = (string)(await Get(c, d))["text"]!;
                        Check(now == t, $"after {what} the document left is unchanged ({Short(t)} -> {Short(now)})");
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
                    var original = doc == a ? "# TL01 A\n\nalpha\n" : bText;
                    for (var i = 0; i < 5 && (string)(await Get(c, doc))["text"]! != original; i++)
                        await c.Call("document.undo", new { documentId = doc, baseRevision = await Revision(c, doc), reveal = "document" });
                    await c.Call("document.close", new { documentId = doc });
                }
                catch (Exception e) { notes.Add("cleanup: " + e.Message.Split('\n')[0]); }
            }
        }
    }
}
