using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// At a failure, each window's web view as the app sees it (test.webview.state) and as its page sees itself (visible or
/// hidden, whether a frame still comes): a page that waits for a frame that never comes holds every document load.
/// </summary>
internal static partial class Program
{
    private static async Task DiagnoseWebViews(List<string> notes)
    {
        using var c = await Session("e2e diagnosis").WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var w in (JArray)(await c.Call("window.list").WaitAsync(TimeSpan.FromSeconds(5)))["windows"]!)
        {
            var windowId = (string)w["windowId"]!;
            string app, page;
            try { app = (await c.Call("test.webview.state", new { windowId }).WaitAsync(TimeSpan.FromSeconds(5))).ToString(Newtonsoft.Json.Formatting.None); }
            catch (Exception e) { app = "error " + e.Message.Split('\n')[0]; }
            try
            {
                await c.Call("test.editor.eval", new { windowId, script = "(window.__dgFrame = 0, requestAnimationFrame(() => window.__dgFrame = 1), 0)" }).WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(1000);
                page = (await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify({ visibility: document.visibilityState, focus: document.hasFocus(), frame: window.__dgFrame })" }).WaitAsync(TimeSpan.FromSeconds(5)))["result"]?.ToString() ?? "null";
            }
            catch (Exception e) { page = "error " + e.Message.Split('\n')[0]; }
            notes.Add($"web view of {windowId}: app {app}; page {page}");
        }
    }
}
