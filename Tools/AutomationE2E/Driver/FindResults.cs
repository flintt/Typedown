using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// FR01: the find bar's results list ("Search in document" in the side pane), as a reader uses it. A document with six
/// matches of "kiwi" (paragraphs, a list, a table) and three headings; the side pane on the Outline; Ctrl+F with real
/// keys and the word typed; the bar's "1 of 6"; "Show all results" clicked: the list read through UI Automation - its
/// count and its six entries, each a line around its match; entry 3 clicked with the real mouse: the editor's selection
/// is exactly the third match (test.editor.eval) and the bar says 3 of 6; the list closed: the Outline is back. Then with
/// the side pane closed at the start: the list opens it, and closing the list closes it again; and closing the find bar
/// (Esc) closes the list with it.
/// </summary>
internal static partial class Program
{
    private static async Task FR01(List<string> notes)
    {
        using var c = await Session("e2e FR01");
        var text = string.Join("\n", new[]
        {
            "## First part", "", "The kiwi one is here.", "", "The kiwi two is here.", "",
            "## Second part", "", "The kiwi three is here.", "", "- kiwi four in a list", "",
            "## Third part", "", "| a | b |", "| --- | --- |", "| kiwi five | x |", "", "The kiwi six is the last.", "",
        });
        var doc = await Open(c, Fixture("fr01.md", text));
        var windowId = await WindowIdOf(c, doc);
        var window = await WindowOf(windowId, c);
        // The numbers of a text, in order ("3 of 6", "第 3 个，共 6 个" and "3/6" alike): the language is the machine's.
        static int[] Numbers(string? s) => Regex.Matches(s ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        string NameOf(string id) => FindIn(window, id)?.Current.Name ?? "";
        async Task<AutomationElement> OnScreen(string id, string what)
        {
            var element = await WaitFor(window, id);
            if (!await Eventually(async () => { await Task.CompletedTask; var r = element.Current.BoundingRectangle; return !r.IsEmpty && r.Width > 0 && r.Height > 0; }, 5000))
                throw new CaseFailed($"{what} ({id}) is there but has no place on the screen after 5 s ({FocusInfo(window)})");
            return element;
        }
        // The editor's match chosen and selection, as the page holds them.
        async Task<JObject> Chosen()
        {
            const string script = "(() => { const cs = window.__typedownMuya.contentState; const { matches, index } = cs.searchMatches; const m = matches[index];" +
                " const c = cs.cursor; const b = m && cs.getBlock(m.key); return JSON.stringify({ index, total: matches.length, selection: getSelection().toString()," +
                " exact: !!m && c.start.key === m.key && c.end.key === m.key && c.start.offset === m.start && c.end.offset === m.end, block: b ? b.text : null }) })()";
            return JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "{}");
        }
        async Task<JToken> View() => await c.Call("window.getView", new { windowId });
        // The outline page shown in the pane: one of its rows on the screen. Its headings are all top level (no row hidden
        // under a collapsed one), and the editor's own "Second part" is in the web view, which the search leaves out. The
        // outline's tree has no automation id of its own (its x:Name does not reach UI Automation): a row is what shows.
        bool OutlineShown()
        {
            try
            {
                return Deep.All(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.NameProperty, "Second part"))
                    .Any(e => { var r = e.Current.BoundingRectangle; return !e.Current.IsOffscreen && !r.IsEmpty && r.Width > 0 && r.Height > 0; });
            }
            catch (ElementNotAvailableException) { return false; }
        }
        // What the pane is showing, for a failure: the host's view, the outline as the host holds it, and what is over it.
        async Task<string> PaneState()
        {
            var view = await View();
            var outline = await c.Call("test.outline.state", new { windowId });
            var rows = ((outline["rows"] as JArray) ?? new JArray()).Select(r => (string?)r["name"]);
            return $"side pane {view["sidePane"]?.ToString(Newtonsoft.Json.Formatting.None)}, outline page there: {outline["tree"]}, outline rows [{string.Join(", ", rows)}], " +
                $"folder search {(FindIn(window, "SearchTextBox") != null ? "open" : "closed")}, results list {(FindIn(window, "FindResultsList") != null ? "open" : "closed")}, " +
                $"outline row on screen: {OutlineShown()}; screen: {Screenshot("fr01-pane-" + DateTime.Now.ToString("HHmmss"))}";
        }
        async Task FindWithKeys()
        {
            await ClickEditorTextOnce(c, windowId, window, "kiwi one");
            await Task.Delay(300);
            // The caret at the start of the document: the bar's search starts at the first match at or after the caret (as it
            // always has), and the click above leaves it inside "The kiwi one", past that kiwi - the bar then said 2 of 6.
            Chord("Ctrl+Home");
            await Task.Delay(300);
            Chord("Ctrl+F");
            Check(await Eventually(async () => { await Task.CompletedTask; return FocusedAutomationId() == "TextBoxSearch"; }, 3000), $"Ctrl+F puts the keyboard in the find bar's box ({FocusInfo(window)})");
            TypeText("kiwi");
            Check(await Eventually(async () => { await Task.CompletedTask; return Numbers(NameOf("FindCount")).SequenceEqual(new[] { 1, 6 }); }, 5000),
                $"the bar says 1 of 6 ('{NameOf("FindCount")}')");
        }
        async Task ShowAll()
        {
            await Click(window, await OnScreen("FindShowAllResults", "the find bar's \"Show all results\" button"));
            await OnScreen("FindResultsList", "the results list");
            Check(await Eventually(async () => { await Task.CompletedTask; return Numbers(NameOf("FindResultsTotal")).SequenceEqual(new[] { 6 }); }, 5000),
                $"the list counts 6 ('{NameOf("FindResultsTotal")}')");
        }

        try
        {
            await WaitUntilDrawn(c, windowId, doc, "kiwi six");

            // A folder search left open by an earlier case would cover the pages: closed first (its close button).
            if (FindIn(window, "SearchTextBox") != null && FindIn(window, "CloseButton") is { } closeSearch)
            {
                notes.Add("the folder search was open: closed");
                await Click(window, closeSearch);
                await WaitGone(window, "SearchTextBox");
            }
            // The side pane on the Outline, with this document's headings in it.
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
            var outlineRows = await Eventually(async () => ((await c.Call("test.outline.state", new { windowId }))["rows"] as JArray)?.Any(r => (string?)r["name"] == "Second part") == true, 8000);
            Check(outlineRows, "the outline holds this document's headings" + (outlineRows ? "" : $" ({await PaneState()})"));
            var shown = await Eventually(async () => { await Task.CompletedTask; return OutlineShown(); }, 8000);
            Check(shown, "the side pane shows the Outline" + (shown ? "" : $" ({await PaneState()})"));

            await FindWithKeys();
            await ShowAll();
            var names = Enumerable.Range(1, 6).Select(i => NameOf($"FindResult:{i}")).ToList();
            for (var i = 0; i < names.Count; i++) notes.Add($"entry {i + 1}: '{names[i]}'");
            var words = new[] { "one", "two", "three", "four", "five", "six" };
            Check(names.Select((n, i) => n.Contains("kiwi") && n.Contains(words[i]) && Numbers(n).Take(2).SequenceEqual(new[] { i + 1, 6 })).All(x => x),
                "six entries, each read as its place (n of 6) and the line around its match, in the document's order");
            Check(FindIn(window, "FindResult:7") == null, "no seventh entry");
            Check(FindIn(window, "FindResultsTooMany") == null, "no \"too many results\" line for 6");
            var covered = !OutlineShown();
            Check(covered, "the list is shown in the pane's place, over the Outline" + (covered ? "" : $" ({await PaneState()})"));

            // Entry 3 with the real mouse: the third match selected in the editor, the bar on it.
            await Click(window, await OnScreen("FindResult:3", "the third entry"));
            JObject chosen = new();
            Check(await Eventually(async () => { chosen = await Chosen(); return (int?)chosen["index"] == 2; }, 5000), $"entry 3 makes the third match the editor's current one ({chosen})");
            notes.Add($"after entry 3: {chosen.ToString(Newtonsoft.Json.Formatting.None)}");
            Check((bool?)chosen["exact"] == true && (string?)chosen["selection"] == "kiwi" && ((string?)chosen["block"] ?? "").Contains("three"),
                $"the editor's selection is exactly that match, the kiwi of \"three\" ({chosen})");
            Check(await Eventually(async () => { await Task.CompletedTask; return Numbers(NameOf("FindCount")).SequenceEqual(new[] { 3, 6 }); }, 3000),
                $"the bar says 3 of 6 ('{NameOf("FindCount")}')");

            // Closed: the Outline again.
            await Click(window, await OnScreen("FindResultsClose", "the list's close button"));
            await WaitGone(window, "FindResultsList");
            var back = await Eventually(async () => { await Task.CompletedTask; return OutlineShown(); }, 5000);
            Check(back, "closing the list shows the Outline again" + (back ? "" : $" ({await PaneState()})"));
            var view = await View();
            Check((bool?)view["sidePane"]?["open"] == true && (string?)view["sidePane"]?["page"] == "outline", $"the side pane is open on the Outline ({view["sidePane"]})");
            Check(FindIn(window, "TextBoxSearch") != null, "the find bar stays open");

            // With the side pane closed: the list opens it, closing the list closes it again. The bar closed first (Esc in its box).
            await Click(window, await OnScreen("TextBoxSearch", "the find bar's box"));
            await Task.Delay(200);
            Chord("Esc");
            await WaitGone(window, "TextBoxSearch");
            await c.Call("window.setView", new { windowId, sidePane = new { open = false } });
            Check(await Eventually(async () => (bool?)(await View())["sidePane"]?["open"] == false, 3000), "the side pane is closed");
            await FindWithKeys();
            await ShowAll();
            Check((bool?)(await View())["sidePane"]?["open"] == true, "\"Show all results\" opens the closed side pane");
            await Click(window, await OnScreen("FindResult:5", "the fifth entry"));
            Check(await Eventually(async () => { chosen = await Chosen(); return (int?)chosen["index"] == 4 && (bool?)chosen["exact"] == true; }, 5000),
                $"entry 5 selects the fifth match ({chosen})");
            await Click(window, await OnScreen("FindResultsClose", "the list's close button"));
            await WaitGone(window, "FindResultsList");
            Check(await Eventually(async () => (bool?)(await View())["sidePane"]?["open"] == false, 3000), "closing the list closes the side pane again");

            // The find bar closed (Esc in its box) closes the list too, the pane back as it was (closed).
            await ShowAll();
            var box = await OnScreen("TextBoxSearch", "the find bar's box");
            await Click(window, box);
            await Task.Delay(200);
            Chord("Esc");
            await WaitGone(window, "FindResultsList");
            Check(await Eventually(async () => (bool?)(await View())["sidePane"]?["open"] == false, 3000), "closing the find bar closes the list and the side pane it had opened");
        }
        finally
        {
            try { await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } }); } catch { }
            try { await c.Call("document.close", new { documentId = doc }); } catch { }
        }
    }
}
