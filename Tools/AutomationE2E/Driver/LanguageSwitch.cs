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
    // window): no Chinese or Japanese word left on the main window, the home page or the settings page. Reported
    // (2026-10-24): "已保存" in the status bar and Chinese on the home page of English windows.
    private static async Task LG01(List<string> notes)
    {
        using var c = await Session("e2e LG01");
        var id = await Open(c, Fixture("lg01.md", "# LG01\n\nSome text.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var original = (await c.Call("settings.get", new { keys = new[] { "ui.language" } }))["values"]!["ui.language"]!;
        var cjk = new Regex(@"[぀-ヿ㐀-鿿豈-﫿！-｠]");
        async Task<List<string>> Left(string page)
        {
            var left = new List<string>();
            foreach (var t in (JArray)(await c.Call("test.window.texts", new { windowId }))["texts"]!)
            {
                var text = (string)t["text"]!;
                // A file's own name or path is the reader's, in whatever language.
                if (text.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || text.Contains(":\\")) continue;
                if (cjk.IsMatch(text)) left.Add($"{page}: \"{text}\" ({t["where"]})");
            }
            return left;
        }
        var found = new List<string>();
        try
        {
            await SetSetting(c, "ui.language", "en");
            string menu = "";
            for (var i = 0; i < 40 && !menu.StartsWith("File"); i++)
            {
                await Task.Delay(250);
                try { menu = string.Join(" ", ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Select(t => (string)t!)); } catch (JsonRpcRemoteException) { }
            }
            notes.Add("menu: " + menu);
            await Task.Delay(1500);
            found.AddRange(await Left("main window"));

            await ClickInPopup(window, await MenuEntry(window, "MenuBarFileItem", "HomeMenuItem"));
            Check(await HomeShown(c, windowId, true), "File > Home shows the home page");
            await Task.Delay(1500);
            found.AddRange(await Left("home page"));

            await c.Call("test.window.navigate", new { windowId, route = "Settings" });
            await Task.Delay(2500);
            found.AddRange(await Left("settings"));
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(800);

            foreach (var f in found.Take(40)) notes.Add(f);
            Check(found.Count == 0, $"no Chinese or Japanese left in the English interface ({found.Count}): " + string.Join("; ", found.Take(6)));
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
            try { await SetSetting(c, "ui.language", original); } catch { }
        }
    }
}
