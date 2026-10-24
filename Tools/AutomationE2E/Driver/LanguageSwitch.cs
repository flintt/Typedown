using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Typedown.Automation;

internal static partial class Program
{
    // The interface in English after the language was changed through the API (as the Store pictures do it, or another
    // window): a window made in Chinese has no Chinese word left on its main page, home page or settings page. Reported
    // (2026-10-24): "已保存" in the status bar and Chinese on the home page of English windows.
    private static async Task LG01(List<string> notes)
    {
        using var c = await Session("e2e LG01");
        var id = await Open(c, Fixture("lg01.md", "# LG01\n\nSome text.\n"));
        var first = await WindowIdOf(c, id);
        var original = (await c.Call("settings.get", new { keys = new[] { "ui.language" } }))["values"]!["ui.language"]!;
        var cjk = new Regex(@"[\u3040-\u30ff\u3400-\u9fff\uf900-\ufaff\uff01-\uff60]");
        string? windowId = null;
        IntPtr window = IntPtr.Zero;
        async Task<List<string>> Texts(string page, bool cjkOnly)
        {
            var left = new List<string>();
            foreach (var t in (JArray)(await c.Call("test.window.texts", new { windowId }))["texts"]!)
            {
                var text = (string)t["text"]!;
                // A file's own name or path is the reader's, in whatever language; the test host's title marker is the test's.
                if (text.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || text.Contains(":\\") || text.Contains("AUTOMATION TEST HOST")) continue;
                if (!cjkOnly || cjk.IsMatch(text)) left.Add($"{page}: \"{text}\" ({t["where"]})");
            }
            return left;
        }
        async Task<string> Menu() => string.Join(" ", ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Select(t => (string)t!));
        var found = new List<string>();
        try
        {
            // A window made in Chinese - as a reader's is, or the Store pictures' - then the language changed to English
            // through the API, which builds nothing again.
            await SetSetting(c, "ui.language", "zh-Hans");
            windowId = (string)(await c.Call("test.window.open", new { path = Fixture("lg01b.md", "# LG01 B\n\nMore text.\n") }))["windowId"]!;
            window = await WindowOf(windowId, c);
            await Task.Delay(3000);
            var made = await Texts("made in Chinese", true);
            notes.Add($"the new window in Chinese: {made.Count} texts, e.g. " + string.Join("; ", made.Take(3)));
            Check(made.Count > 5, "the new window was made in Chinese (else nothing could be left behind)");

            await SetSetting(c, "ui.language", "en");
            var menu = "";
            for (var i = 0; i < 40 && !menu.StartsWith("File"); i++) { await Task.Delay(250); try { menu = await Menu(); } catch (JsonRpcRemoteException) { } }
            notes.Add("menu: " + menu);
            await Task.Delay(1500);
            found.AddRange(await Texts("main window", true));

            await ClickInPopup(window, await MenuEntry(window, "MenuBarFileItem", "HomeMenuItem"));
            Check(await HomeShown(c, windowId, true), "File > Home shows the home page");
            await Task.Delay(1500);
            found.AddRange(await Texts("home page", true));

            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(2500);
            found.AddRange(await Texts("settings", true));
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(800);

            foreach (var f in found.Take(40)) notes.Add(f);
            Check(found.Count == 0, $"no Chinese left in the English interface ({found.Count}): " + string.Join("; ", found.Take(6)));
        }
        finally
        {
            if (windowId != null) try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            if (window != IntPtr.Zero) try { PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); } catch { }
            try { await SetSetting(c, "ui.language", original); } catch { }
        }
    }
}
