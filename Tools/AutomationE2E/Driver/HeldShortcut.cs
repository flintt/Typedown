using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// SH01: Ctrl+, held down opens the settings once. The system repeats a held key, and each repeat went in or out of the
/// settings again - the main page made anew dozens of times a second, the editor handed the keyboard as often - until
/// the window hung and a menu bar on its way out took a key and brought the app down. Let go and pressed again, it
/// leaves them; no comma reaches the document either way.
/// </summary>
internal static partial class Program
{
    private static async Task SH01(List<string> notes)
    {
        using var c = await Session("e2e SH01");
        var id = await Open(c, Fixture("sh01.md", "# SH01\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        async Task<string> Page() => (string)(await c.Call("test.layout.contentTop", new { windowId }))["page"]!;
        try
        {
            await TypeInto(c, id, "");
            await Task.Delay(500);
            // Held for two seconds: the comma's key down sent again and again, as the system repeats it, no key up between.
            Send(Key(0x11, false));
            await Task.Delay(80);
            for (var i = 0; i < 60; i++)
            {
                Send(Comma(false));
                await Task.Delay(33);
            }
            Send(Comma(true));
            await Task.Delay(80);
            Send(Key(0x11, true));
            await Task.Delay(2000);
            var held = await Page();
            notes.Add($"after Ctrl+, held for 2 s: page {held}");
            Check(held == "settings", $"Ctrl+, held opens the settings once and stays there ({held})");
            await CtrlComma();
            await Task.Delay(2000);
            var again = await Page();
            var text = (string?)(await Get(c, id))["text"] ?? "";
            notes.Add($"pressed again: page {again}; the document {text.Replace("\n", "\\n")}");
            Check(again == "main", $"pressed again, Ctrl+, leaves the settings ({again})");
            Check(!text.Contains(','), "no comma reached the document");
        }
        finally
        {
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
