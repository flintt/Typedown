using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// LK01: a link to another Markdown file, clicked in reading mode, opens it the way the settings say: with "open files
/// in a new tab" on (the default), as a tab of the same window, not in a window of its own.
/// </summary>
internal static partial class Program
{
    private static async Task LK01(List<string> notes)
    {
        using var c = await Session("e2e LK01");
        var target = Fixture("lk01-b.md", "# LK01 B\n\nthe linked file\n");
        var id = await Open(c, Fixture("lk01-a.md", "# LK01 A\n\n[go to b](lk01-b.md)\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var windowsBefore = ((JArray)(await c.Call("window.list"))["windows"]!).Count;
        await c.Call("window.setView", new { windowId, mode = "reading" });
        await Task.Delay(1500);
        await Activate(window);
        var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const a = [...document.querySelectorAll('#ag-editor-id a')].find(x => (x.getAttribute('href') || '').endsWith('lk01-b.md')); if (!a) return null; const r = a.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } })()" }))["result"];
        Check(at != null && at.Type == JTokenType.Object, "the link is on the page");
        var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at!["x"]!, y = (int)at["y"]! });
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        SetCursorPos((int)screen["x"]!, (int)screen["y"]!);
        await Task.Delay(150);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
        await Task.Delay(60);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
        string? opened = null; var windowsAfter = windowsBefore;
        for (var i = 0; i < 40 && opened == null; i++)
        {
            await Task.Delay(150);
            var all = (JArray)(await c.Call("window.list"))["windows"]!;
            windowsAfter = all.Count;
            foreach (var w in all)
                if (((JArray)(await c.Call("document.list", new { windowId = (string)w["windowId"]! }))["documents"]!).Any(d => string.Equals((string?)d["path"], target, StringComparison.OrdinalIgnoreCase)))
                    opened = (string)w["windowId"]!;
        }
        notes.Add($"windows {windowsBefore} -> {windowsAfter}; the linked file is in {(opened == null ? "no window" : opened == windowId ? "the same window" : "another window")}");
        try
        {
            Check(opened != null, "the linked file opened");
            Check(opened == windowId && windowsAfter == windowsBefore, "it opened as a tab of the same window, not in a new window");
        }
        finally
        {
            await c.Call("window.setView", new { windowId, mode = "visual" });
            if (opened != null && opened != windowId) PostMessage(await WindowOf(opened, c), 0x0010, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
