using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// OL01: tabs switched back and forth, quickly and slowly: every heading with headings under it stays expanded in the
/// outline (nobody collapsed one), in the tree's own nodes as in the model. A reader saw the outline of the tab just
/// shown come up collapsed now and then.
/// </summary>
internal static partial class Program
{
    private static string OutlineFixture(string prefix)
    {
        var text = new System.Text.StringBuilder();
        for (var a = 1; a <= 3; a++)
        {
            text.Append($"# {prefix}{a}\n\nText under {prefix}{a}.\n\n");
            for (var b = 1; b <= 3; b++)
            {
                text.Append($"## {prefix}{a}.{b}\n\nText under {prefix}{a}.{b}.\n\n");
                for (var d = 1; d <= 2; d++) text.Append($"### {prefix}{a}.{b}.{d}\n\nText under {prefix}{a}.{b}.{d}.\n\n");
            }
        }
        return Fixture($"ol01-{prefix.ToLowerInvariant()}.md", text.ToString());
    }

    private static async Task OL01(List<string> notes)
    {
        using var c = await Session("e2e OL01");
        var prefixes = new[] { "Q", "R", "S" };
        var paths = prefixes.Select(OutlineFixture).ToArray();
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await Open(c, path));
        var windowId = await WindowIdOf(c, ids[0]);
        await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
        await Task.Delay(1500);
        var random = new Random(1);
        var current = ids.Count - 1;
        var bad = new List<string>();
        var checkedRounds = 0;
        for (var round = 0; round < 30; round++)
        {
            // Now and then a burst: tabs switched faster than the outline is rebuilt.
            var switches = round % 5 == 4 ? 3 : 1;
            for (var s = 0; s < switches; s++)
            {
                var next = random.Next(ids.Count - 1);
                if (next >= current) next++;
                current = next;
                await Open(c, paths[current]);
                await Task.Delay(random.Next(0, 200));
            }
            // The outline of the tab now shown: wait for it, then for it to settle.
            JToken state = null;
            for (var i = 0; i < 40; i++)
            {
                state = await c.Call("test.outline.state", new { windowId });
                var rows = (JArray)state["rows"]!;
                if (rows.Count > 0 && rows.All(r => ((string?)r["name"] ?? "").StartsWith(prefixes[current]))) break;
                await Task.Delay(100);
            }
            await Task.Delay(500);
            state = await c.Call("test.outline.state", new { windowId });
            if (round == 0) notes.Add($"pane {state["pane"]}, tree {state["tree"]}, autoExpand {state["autoExpand"]}, {((JArray)state["rows"]!).Count} rows");
            Check((bool?)state["tree"] == true, "the outline pane is shown");
            var all = (JArray)state["rows"]!;
            Check(all.Count == 30 && all.All(r => ((string?)r["name"] ?? "").StartsWith(prefixes[current])), $"round {round}: the outline is the shown tab's ({prefixes[current]}), {all.Count} rows");
            checkedRounds++;
            foreach (var r in all.Where(r => (int)r["children"]! > 0))
            {
                // A row whose parents are collapsed has no node; one with a node must be expanded, and so must the model.
                if ((bool)r["model"]! == false || (bool?)r["node"] == false || (r["node"]!.Type == JTokenType.Null && (int)r["depth"]! == 1))
                    bad.Add($"round {round} {r["name"]}: model {r["model"]}, node {(r["node"]!.Type == JTokenType.Null ? "none" : r["node"])}, node children {r["nodeChildren"]}/{r["children"]}");
            }
        }
        // An edit of the shown document: the outline is that document's again, a heading renamed in place.
        var shownText = System.IO.File.ReadAllText(paths[current]).Replace($"## {prefixes[current]}1.2\n", $"## {prefixes[current]}1.2 renamed\n");
        await c.Call("document.replace", new { documentId = ids[current], baseRevision = await Revision(c, ids[current]), text = shownText, normalizationPolicy = "allowUnknown" });
        JArray edited = new();
        for (var i = 0; i < 40; i++)
        {
            edited = (JArray)(await c.Call("test.outline.state", new { windowId }))["rows"]!;
            if (edited.Any(r => (string?)r["name"] == $"{prefixes[current]}1.2 renamed")) break;
            await Task.Delay(100);
        }
        await Task.Delay(500);
        edited = (JArray)(await c.Call("test.outline.state", new { windowId }))["rows"]!;
        notes.Add($"after an edit: {edited.Count} rows, renamed row {(edited.Any(r => (string?)r["name"] == $"{prefixes[current]}1.2 renamed") ? "shown" : "missing")}");
        Check(edited.Count == 30 && edited.Any(r => (string?)r["name"] == $"{prefixes[current]}1.2 renamed"), "an edited heading is renamed in the outline");
        foreach (var r in edited.Where(r => (int)r["children"]! > 0 && ((bool)r["model"]! == false || (bool?)r["node"] != true)))
            bad.Add($"after the edit {r["name"]}: model {r["model"]}, node {r["node"]}");
        notes.Add($"{checkedRounds} rounds checked, {bad.Count} collapsed rows");
        foreach (var b in bad.Take(12)) notes.Add(b);
        Check(bad.Count == 0, "no heading with headings under it is collapsed after a tab switch");
    }
}

/// <summary>
/// OL02: in reading mode, two long documents, each scrolled to its middle (the caret left at the top, as a reader scrolling
/// with the wheel leaves it), switched between: the outline marks the shown document's heading, and while a tab is being left it does
/// not mark another heading of the document being left first. A reader saw the outline jump to some other section of
/// the previous outline for a moment on every switch.
/// </summary>
internal static partial class Program
{
    private static async Task OL02(List<string> notes)
    {
        using var c = await Session("e2e OL02");
        string Long(string prefix)
        {
            var text = new System.Text.StringBuilder();
            for (var a = 1; a <= 40; a++)
            {
                text.Append($"## {prefix} {a}\n\n");
                for (var p = 0; p < 3; p++) text.Append($"Paragraph {p} of {prefix} {a}, long enough to take a line or two of the page, so that forty sections make a document several screens tall.\n\n");
            }
            return Fixture($"ol02-{prefix.ToLowerInvariant()}.md", text.ToString());
        }
        var prefixes = new[] { "Kilo", "Lima" };
        var paths = prefixes.Select(Long).ToArray();
        var ids = new List<string>();
        foreach (var path in paths) ids.Add(await Open(c, path));
        var windowId = await WindowIdOf(c, ids[0]);
        // Reading mode: the outline follows the page as it is scrolled (in the other modes it follows the caret).
        await c.Call("window.setView", new { windowId, mode = "reading", sidePane = new { open = true, page = "outline" } });
        // Each document scrolled to its middle, the caret where it was opened (the top).
        foreach (var i in new[] { 0, 1 })
        {
            await Open(c, paths[i]);
            await Task.Delay(1500);
            await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, document.documentElement.scrollHeight / 2); true" });
            await Task.Delay(1500);
        }
        List<string> Marks() => LogLines("outline: marked ");
        var bad = new List<string>();
        var current = 1;
        for (var round = 0; round < 8; round++)
        {
            await Task.Delay(1500);
            var before = Marks();
            var lastMark = before.LastOrDefault() ?? "";
            var next = 1 - current;
            await Open(c, paths[next]);
            await Task.Delay(2500);
            var during = Marks().Skip(before.Count).ToList();
            notes.Add($"round {round}, {prefixes[current]} -> {prefixes[next]}: last mark '{Tail(lastMark)}', then {string.Join(" / ", during.Select(Tail))}");
            // A mark of the document being left, other than the one it had, is the jump.
            foreach (var m in during.TakeWhile(m => !Tail(m).StartsWith(prefixes[next])))
                if (Tail(m).StartsWith(prefixes[current]) && Tail(m) != Tail(lastMark)) bad.Add($"round {round}: {Tail(m)}");
            Check(during.Count == 0 || Tail(during.Last()).StartsWith(prefixes[next]), $"round {round}: the outline ends on a heading of {prefixes[next]}");
            current = next;
        }
        Check(bad.Count == 0, $"no other heading of the document being left is marked on a switch ({string.Join("; ", bad)})");
        await c.Call("window.setView", new { windowId, mode = "visual" });
    }

    // "12:00:00.000 outline: marked Kilo 21 (load 9)" -> "Kilo 21"
    private static string Tail(string line)
    {
        var i = line.IndexOf("outline: marked ");
        if (i < 0) return line;
        var s = line.Substring(i + "outline: marked ".Length);
        var j = s.LastIndexOf(" (load ");
        return j < 0 ? s : s.Substring(0, j);
    }
}
