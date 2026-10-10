using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// ES01: an editor shortcut the page does not handle itself (Ctrl+1, Heading 1) is carried out by the app while one
/// types in the editor. The app takes it only when the editor has the keyboard focus; on WinUI 3 the focus is on the
/// web view inside the editor, which the check did not count as the editor - the shortcut did nothing.
/// </summary>
internal static partial class Program
{
    private static async Task ES01(List<string> notes)
    {
        using var c = await Session("e2e ES01");
        var id = await Open(c, Fixture("es01.md", "plain words\n"));
        await TypeInto(c, id, "x");
        Check(await Eventually(async () => ((string?)(await Get(c, id))["text"] ?? "").Contains("wordsx"), 3000), "the typed x reached the paragraph");
        await Task.Delay(300);
        // Ctrl+1 as a keyboard sends it: the scan code with the key (the page reads it).
        INPUT One(bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x31, wScan = 0x02, dwFlags = up ? 0x0002u : 0u } } };
        Send(Key(0x11, false));
        await Task.Delay(60);
        Send(One(false), One(true));
        await Task.Delay(60);
        Send(Key(0x11, true));
        string text = "";
        var ok = await Eventually(async () => (text = (string?)(await Get(c, id))["text"] ?? "").StartsWith("# "), 4000);
        notes.Add("after Ctrl+1: " + text.Replace("\n", "\\n"));
        Check(ok, "Ctrl+1 made the paragraph a level 1 heading");
    }
}
