using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// HM01: Help > User guide opens the user guide in the interface's language (English or Chinese) as a tab, from a copy in
/// the app's data folder (the shipped one is in a read-only folder), and Help > About opens the About page of the
/// settings. The Store package had no user guide at all, and the app no way to open one.
/// </summary>
internal static partial class Program
{
    private static async Task HM01(List<string> notes)
    {
        using var c = await Session("e2e HM01");
        var windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;
        var window = await WindowOf(windowId, c);
        try
        {
            await Activate(window);
            await InvokeMenuBarItem(window, "UserGuideItem");
            JToken? guide = null;
            for (var i = 0; i < 40 && guide == null; i++)
            {
                await Task.Delay(150);
                guide = ((JArray)(await c.Call("document.list", new { windowId }))["documents"]!)
                    .FirstOrDefault(d => ((string?)d["path"] ?? "").Contains("user-guide", StringComparison.OrdinalIgnoreCase));
            }
            Check(guide != null, "Help > User guide opened the user guide as a tab");
            var path = (string)guide!["path"]!;
            notes.Add("opened: " + path);
            Check(path.Contains(@"\guide\", StringComparison.OrdinalIgnoreCase), "from the copy in the app's data folder, not the read-only one beside the app");
            var text = (string?)(await Get(c, (string)guide["documentId"]!))["text"] ?? "";
            var english = path.EndsWith("user-guide.en.md", StringComparison.OrdinalIgnoreCase);
            notes.Add($"{(english ? "English" : "Chinese")} guide, {text.Length} characters, starts {Newtonsoft.Json.JsonConvert.SerializeObject(text.Split('\n')[0])}");
            // Under the edition's own name in an edition (the copy has the brand replaced).
            Check(text.Length > 5000 && System.Text.RegularExpressions.Regex.IsMatch(text, english ? @"^# \w+ User Guide" : @"^# \w+ 使用说明"), "the whole guide, in the language of its file");

            await Activate(window);
            await InvokeMenuBarItem(window, "AboutItem");
            var about = await WaitFor(window, "FeedBackButton", 8000);
            Check(about != null, "Help > About opened the About page");
        }
        finally
        {
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
        }
    }
}
