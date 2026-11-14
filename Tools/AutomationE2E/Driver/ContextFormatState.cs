using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// XM05 (the user, 2026-11-13; the menu report's M11): the context menu's format buttons show the format where it was
/// right-clicked - pressed in bold text for bold, in italic for italic, none in plain text; Bold pressed in bold text
/// takes the bold off, and the next menu there shows it off.
/// </summary>
internal static partial class Program
{
    private static async Task XM05(List<string> notes)
    {
        var folder = FixtureFolder("xm05");
        var path = Path.Combine(folder, "xm05.md");
        File.WriteAllText(path, "# Formats\n\nPlainwordsatthestart then **boldwordshere** then *italicwordshere* end.\n");
        using var c = await Session("e2e XM05");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        string[] buttons = { "ContextFormatStrong", "ContextFormatEmphasis", "ContextFormatUnderline", "ContextFormatStrikethrough" };
        // Each format button's state as a screen reader has it (its toggle pattern).
        Dictionary<string, bool?> States()
        {
            var states = new Dictionary<string, bool?>();
            foreach (var b in buttons)
            {
                var e = FindAnywhereInHost(window, b);
                states[b] = e != null && e.TryGetCurrentPattern(TogglePattern.Pattern, out var p) ? ((TogglePattern)p).Current.ToggleState == ToggleState.On : null;
            }
            return states;
        }
        string Show(Dictionary<string, bool?> s) => string.Join(", ", s.Select(x => $"{x.Key.Replace("ContextFormat", "")}={(x.Value == null ? "none" : x.Value.Value ? "on" : "off")}"));
        void Close() => Send(Key(0x1B, false), Key(0x1B, true));
        async Task<Dictionary<string, bool?>> MenuOn(string where, string selector)
        {
            await Eventually(() => Task.FromResult(FindAnywhereInHost(window, "SelectAllItem") == null), 3000, 100);
            await ContextMenuAt(c, windowId, window, selector);
            await Task.Delay(400);
            var s = States();
            notes.Add($"{where}: {Show(s)}");
            return s;
        }
        try
        {
            var bold = await MenuOn("bold text", "#ag-editor-id strong");
            Close(); await Task.Delay(300);
            Check(bold["ContextFormatStrong"] == true && bold["ContextFormatEmphasis"] == false, $"in bold text Bold is pressed, Italic not ({Show(bold)})");

            var italic = await MenuOn("italic text", "#ag-editor-id em");
            Close(); await Task.Delay(300);
            Check(italic["ContextFormatEmphasis"] == true && italic["ContextFormatStrong"] == false, $"in italic text Italic is pressed, Bold not ({Show(italic)})");

            var plain = await MenuOn("plain text", "#ag-editor-id p span.ag-plain-text");
            Close(); await Task.Delay(300);
            Check(plain.Values.All(v => v == false), $"in plain text none is pressed ({Show(plain)})");

            // Bold pressed in bold text: the bold taken off, and the menu there shows it off.
            await MenuOn("bold again", "#ag-editor-id strong");
            var strong = FindAnywhereInHost(window, "ContextFormatStrong") ?? throw new CaseFailed("no Bold button");
            await ClickInPopup(window, strong);
            var text = "";
            var unbolded = await Eventually(async () => !(text = (string)(await Get(c, id))["text"]!).Contains("**boldwordshere**"), 5000, 200);
            notes.Add($"after Bold: {text.Replace("\n", " | ")}");
            Check(unbolded && text.Contains("boldwordshere"), "Bold in bold text takes the bold off");
            await Task.Delay(500);
            var after = await MenuOn("where the bold was", "#ag-editor-id p span.ag-plain-text");
            Close();
            Check(after["ContextFormatStrong"] == false, $"and the menu there shows Bold off ({Show(after)})");
        }
        finally
        {
            try { Close(); } catch { }
            try { await c.Call("document.save", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
