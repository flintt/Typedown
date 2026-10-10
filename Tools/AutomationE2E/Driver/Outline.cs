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
        notes.Add($"{checkedRounds} rounds checked, {bad.Count} collapsed rows");
        foreach (var b in bad.Take(12)) notes.Add(b);
        Check(bad.Count == 0, "no heading with headings under it is collapsed after a tab switch");
    }
}
