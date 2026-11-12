using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;
using Newtonsoft.Json.Linq;

/// <summary>
/// QO01: File > Quick Open by its keys, Ctrl+P, in the editor: a few letters of a name typed, in order but not together,
/// find the file in a subfolder of the side pane's folder, best first; Down chooses the next, Enter opens it in a tab.
/// A file in a hidden folder is not offered; Esc closes the box and opens nothing.
/// </summary>
internal static partial class Program
{
    private static async Task QO01(List<string> notes)
    {
        using var c = await Session("e2e QO01");
        var folder = Path.Combine(fixtures, "qo01");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        foreach (var name in new[] { "alpha-notes.md", "beta.md", "sub/gamma-plan.md", "sub/gamma-place.md", ".hidden/gamma-plot.md" })
        {
            var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"# {Path.GetFileNameWithoutExtension(path)}\n");
        }
        var id = await Open(c, Path.Combine(folder, "alpha-notes.md"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<List<string>> Opened() => ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!).Select(d => (string?)d["path"] ?? "").ToList();
        void TypeText(string text) => Send(text.SelectMany(ch => new[]
        {
            new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 } } },
            new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 | 0x0002 } } },
        }).ToArray());
        async Task CtrlP()
        {
            await Activate(window);
            await c.Call("test.editor.focus", new { windowId });
            await Task.Delay(200);
            Send(Key(0x11, false));
            await Task.Delay(80);
            Send(Key(0x50, false), Key(0x50, true));
            await Task.Delay(80);
            Send(Key(0x11, true));
        }
        List<string> Listed() => FindAnywhereInHost(window, "QuickOpenList") is { } list
            ? list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().Select(e => e.Current.Name).ToList()
            : new List<string>();
        try
        {
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Task.Delay(800);

            // Ctrl+P in the editor: the box, with the keyboard in it.
            await CtrlP();
            Check(await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "QuickOpenBox") != null), 4000, 150), "Ctrl+P in the editor opens Quick Open");
            await Task.Delay(400);
            TypeText("gmpl");
            List<string> shown = new();
            await Eventually(() => Task.FromResult((shown = Listed()).Count >= 2), 4000, 150);
            notes.Add("\"gmpl\": " + string.Join(", ", shown));
            Check(shown.Take(2).OrderBy(x => x).SequenceEqual(new[] { "gamma-place.md", "gamma-plan.md" }), "letters in order, not together, find the files in the subfolder");
            Check(!shown.Contains("gamma-plot.md"), "a file in a hidden folder is not offered");

            // Down to the second, Enter: that one opens.
            var second = shown[1];
            Send(Key(0x28, false), Key(0x28, true));
            await Task.Delay(150);
            Send(Key(0x0D, false), Key(0x0D, true));
            var opened = await Eventually(async () => (await Opened()).Any(p => p.EndsWith(second, StringComparison.OrdinalIgnoreCase)), 5000, 200);
            Check(opened, $"Down then Enter opens the second ({second})");
            Check(await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "QuickOpenBox") == null), 3000, 150), "and the box is gone");

            // Esc: closed, nothing opened.
            var before = (await Opened()).Count;
            await CtrlP();
            Check(await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "QuickOpenBox") != null), 4000, 150), "Ctrl+P again");
            await Task.Delay(300);
            TypeText("beta");
            await Task.Delay(300);
            Send(Key(0x1B, false), Key(0x1B, true));
            await Task.Delay(600);
            Check(FindAnywhereInHost(window, "QuickOpenBox") == null && (await Opened()).Count == before, "Esc closes it and opens nothing");
        }
        finally
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            foreach (var d in await Opened())
                if (d.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    var doc = ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!).FirstOrDefault(x => (string?)x["path"] == d);
                    try { await c.Call("document.close", new { documentId = (string)doc!["documentId"]! }); } catch { }
                }
        }
    }
}
