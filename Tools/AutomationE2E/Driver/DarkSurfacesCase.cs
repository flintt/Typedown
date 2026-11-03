using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

internal static partial class Program
{
    /// <summary>
    /// DK01: the app's surfaces in its dark theme are dark - readers elsewhere found scroll bars, the settings and lists
    /// left light (docs/todo.md, 在线讨论 #13). The editor with a code block that scrolls sideways, a list and a table, the
    /// side pane with the outline and the folder, the editor's menu, the settings page: each a picture (kept with the
    /// run's results) and its share of light pixels - text only in a dark surface, a few percent; a light panel is most
    /// of it.
    /// </summary>
    private static async Task DK01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e DK01");
        var folder = FixtureFolder("dk01");
        var path = Path.Combine(folder, "dk01.md");
        File.WriteAllText(path, "# DK01\n\n## A list\n\n- one\n- two\n  - nested\n\n## A table\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n## Code\n\n```js\nconst line = '" + new string('x', 400) + "';\n```\n\nText after.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        var customBefore = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomTheme" }))["value"];
        var failures = new List<string>();
        (double Light, double Mean) Look(string name, double limit = 15)
        {
            if (!GetWindowRect(window, out var r)) return (0, 0);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            using var shot = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
            try { shot.Save(Path.Combine(outputDir, $"DK01-{name}.png"), System.Drawing.Imaging.ImageFormat.Png); } catch { }
            var data = shot.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            shot.UnlockBits(data);
            long light = 0, all = 0; double sum = 0;
            for (var y = 0; y < h; y += 4)
                for (var x = 0; x < w; x += 4)
                {
                    var i = y * data.Stride + x * 4;
                    var l = 0.299 * bytes[i + 2] + 0.587 * bytes[i + 1] + 0.114 * bytes[i];
                    all++; sum += l;
                    if (l > 150) light++;
                }
            var share = light * 100.0 / all;
            notes.Add($"{name}: {share:0.0} % light pixels, mean {sum / all:0}");
            if (share > limit) failures.Add($"{name} {share:0.0} %");
            return (share, sum / all);
        }
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = "" });
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            await c.Call("window.setView", new { windowId, bounds = new { x = 40, y = 40, width = 1300, height = 820 }, mode = "visual", statusBar = true, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(2500);
            Look("editor-outline");

            await c.Call("test.folder.open", new { windowId, path = folder });
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "files" } });
            await Task.Delay(1500);
            Look("editor-folder");

            // The code block scrolled into view: its own sideways scroll bar.
            await c.Call("test.editor.eval", new { windowId, script = "document.querySelector('#ag-editor-id pre').scrollIntoView({ block: 'center' }), 1" });
            await Task.Delay(800);
            Look("code-block");

            // The editor's own menu.
            await ClickEditorText(c, windowId, window, "Text after");
            var at = (await c.Call("test.editor.eval", new { windowId, script = "(() => { const r = [...document.querySelectorAll('#ag-editor-id p')].find(p => p.textContent.includes('Text after')).getBoundingClientRect(); return { x: Math.round(r.left + 20), y: Math.round(r.top + r.height / 2) } })()" }))["result"]!;
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            var rc = new RECT();
            GetWindowRect(window, out rc);
            await Click(window, (int)screen["x"]! - rc.Left, (int)screen["y"]! - rc.Top, Mouse.Right);
            await Task.Delay(900);
            Look("context-menu");
            await CloseMenus();

            // The settings page.
            await CtrlComma();
            await Task.Delay(2000);
            Look("settings", 20);
            await CtrlComma();
            await Task.Delay(1500);

            Check(failures.Count == 0, $"every surface is dark in the dark theme (light: {string.Join(", ", failures)})");
        }
        finally
        {
            await CloseMenus();
            try { await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore ?? 0 }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "CustomTheme", value = customBefore ?? "" }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
