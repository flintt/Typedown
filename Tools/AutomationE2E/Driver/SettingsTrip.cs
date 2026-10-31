using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// NT01: how long Ctrl+, takes into the settings and out again, measured (notes; no limit yet). With a document of a
/// few hundred paragraphs open, five trips with the page animation on (the default) and five with it off, by the real
/// keys. For each: when the frame shows the other page as seen from here (test.layout.contentTop, polled), and the
/// host's own steps from the moment it was asked (test.window.navigationTimes): the new page made and in the frame,
/// laid out, and drawn. The first trip into the settings is the one that builds them for the first time.
/// </summary>
internal static partial class Program
{
    private static async Task NT01(List<string> notes)
    {
        using var c = await Session("e2e NT01");
        var text = new StringBuilder("# NT01\n\n");
        for (var i = 1; i <= 300; i++)
        {
            if (i % 30 == 1) text.Append($"## Part {i / 30 + 1}\n\n");
            text.Append($"Paragraph {i} with some words in it to fill a line, and a little **bold** and `code` too.\n\n");
        }
        var id = await Open(c, Fixture("nt01.md", text.ToString()));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var animationBefore = (bool?)(await c.Call("test.settings.get", new { windowId, name = "AnimationEnable" }))["value"];
        async Task<string> Page() => (string)(await c.Call("test.layout.contentTop", new { windowId }))["page"]!;
        try
        {
            await Task.Delay(2000);
            await Activate(window);
            foreach (var animation in new[] { true, false })
            {
                await c.Call("test.settings.set", new { windowId, name = "AnimationEnable", value = animation });
                await Task.Delay(500);
                var seen = new Dictionary<string, List<double>>();
                for (var trip = 1; trip <= 10; trip++)
                {
                    var to = trip % 2 == 1 ? "settings" : "main";
                    // As a reader presses it (CtrlComma): Ctrl down, the comma, Ctrl up 80 ms later; timed from the comma.
                    Send(Key(0x11, false));
                    await Task.Delay(80);
                    var watch = Stopwatch.StartNew();
                    Send(Comma(false), Comma(true));
                    await Task.Delay(80);
                    Send(Key(0x11, true));
                    string page = "";
                    while (watch.ElapsedMilliseconds < 10000 && (page = await Page()) != to) await Task.Delay(10);
                    var shown = watch.Elapsed.TotalMilliseconds;
                    Check(page == to, $"Ctrl+, goes to the {to} page within 10 s (animation {(animation ? "on" : "off")}, trip {trip})");
                    // The host's steps: drawn comes a frame or two after the page is seen.
                    JObject times = new();
                    for (var i = 0; i < 100 && times["drawn"] == null; i++) { await Task.Delay(30); times = (JObject)await c.Call("test.window.navigationTimes", new { windowId }); }
                    var steps = string.Join(", ", times.Properties().Select(p => $"{p.Name} {p.Value} ms"));
                    notes.Add($"animation {(animation ? "on" : "off")}, trip {trip} to {to}: seen from the driver {shown:0} ms; host: {steps}");
                    var key = $"{(animation ? "on" : "off")} to {to}";
                    if (!seen.ContainsKey(key)) seen[key] = new();
                    seen[key].Add(times["drawn"] != null ? (double)times["drawn"]! : double.NaN);
                    // Time to look at the page, as a reader would, before going back.
                    await Task.Delay(1200);
                }
                foreach (var (key, values) in seen)
                    notes.Add($"summary {key}: drawn at {string.Join(" / ", values.Select(v => v.ToString("0")))} ms (median {values.OrderBy(v => v).ElementAt(values.Count / 2):0})");
            }
            Check(await Page() == "main", "the trips end on the main page");
        }
        finally
        {
            if (animationBefore != null) await c.Call("test.settings.set", new { windowId, name = "AnimationEnable", value = animationBefore });
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
