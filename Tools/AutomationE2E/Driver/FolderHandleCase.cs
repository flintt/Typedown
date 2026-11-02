using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

internal static partial class Program
{
    /// <summary>
    /// KR01: a window closed lets go of its document's folder - it can be renamed and deleted then (a reader on the old
    /// repository, #81: with the app left running in the background, the folder stayed held). A window of its own with a
    /// document in a folder and that folder in its side pane, closed; the folder renamed from outside, then deleted.
    /// The same close as the last window's (keep running in the background only decides whether the process stays).
    /// </summary>
    private static async Task KR01(List<string> notes)
    {
        using var c = await Session("e2e KR01");
        var root = FixtureFolder("kr01");
        var folder = Path.Combine(root, "held");
        var moved = Path.Combine(root, "held-renamed");
        if (Directory.Exists(moved)) Directory.Delete(moved, true);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "kr01.md");
        File.WriteAllText(path, "# KR01\n\nA document in a folder of its own.\n");
        var opened = await c.Call("test.window.open", new { path });
        var windowId = (string)opened["windowId"]!;
        var window = await WindowOf(windowId, c);
        try
        {
            await c.Call("test.folder.open", new { windowId, path = folder });
            await Task.Delay(1500);
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            window = IntPtr.Zero;
            await Task.Delay(3000);
            string? error = null;
            var renamed = await Eventually(() =>
            {
                try { Directory.Move(folder, moved); return Task.FromResult(true); }
                catch (Exception e) { error = e.Message; return Task.FromResult(false); }
            }, 5000, 500);
            notes.Add(renamed ? "renamed after the close" : $"not renamed: {error}");
            Check(renamed, $"the window closed, its document's folder can be renamed ({error})");
            Directory.Delete(moved, true);
            Check(!Directory.Exists(moved), "and deleted");
        }
        finally
        {
            if (window != IntPtr.Zero) { PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); await Task.Delay(1500); }
        }
    }
}
