using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Automation;

/// <summary>
/// UP01: Settings > About > Check for updates, against a release feed the test host is given (test.updates.feed: no
/// network, and an edition without a feed tested too). Clicked: a newer release says its version with a link to its
/// page; the same version, up to date; a feed with no version, a failure that says so. The daily check's box clicked
/// turns the setting on; made then (test.updates.autoCheck), a newer release leaves the card at the window's bottom right,
/// and Later takes it away; with the setting off, no card.
/// </summary>
internal static partial class Program
{
    private static async Task UP01(List<string> notes)
    {
        using var c = await Session("e2e UP01");
        var windowId = await FirstWindowId(c);
        var window = await WindowOf(windowId, c);
        string Status() => FindIn(window, "UpdateStatus")?.Current.Name ?? "";
        async Task About()
        {
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(300);
            await c.Call("test.window.navigate", new { windowId, route = "Settings/About" });
            await WaitFor(window, "CheckForUpdatesButton", 8000);
        }
        async Task<string> AskFeed(string json, string expect)
        {
            await c.Call("test.updates.feed", new { json });
            await About();
            await Activate(window);
            // The page may be the one shown before, its last answer still there: a new one waited for.
            var before = Status();
            await Click(window, FindIn(window, "CheckForUpdatesButton")!);
            var status = "";
            await Eventually(async () => { await Task.Delay(100); status = Status(); return status != before && !status.Contains("…") && status.Contains(expect); }, 8000);
            return status;
        }
        try
        {
            var edition = (bool?)(await c.Call("test.updates.feed", new { json = (string?)null }))["available"] == true;
            notes.Add($"the edition's own feed: {(edition ? "there" : "none (no check offered)")}");
            if (!edition)
            {
                await c.Call("test.window.navigate", new { windowId, route = "Settings/About" });
                await Task.Delay(1500);
                Check(FindIn(window, "CheckForUpdatesButton") == null, "an edition without a feed shows no Check for updates");
            }

            var newer = await AskFeed("{\"tag_name\":\"v99.1.0\",\"name\":\"v99.1.0\",\"html_url\":\"https://example.com/releases/v99.1.0\"}", "99.1.0");
            notes.Add("newer: " + newer);
            Check(newer.Contains("99.1.0"), $"a newer release is said with its version ({newer})");
            Check(FindIn(window, "UpdatePageLink") != null, "with a link to its page");

            var same = await AskFeed("{\"tag_name\":\"v0.0.1\",\"html_url\":\"https://example.com/releases/v0.0.1\"}", ".");
            notes.Add("older: " + same);
            Check(!same.Contains("0.0.1") && FindIn(window, "UpdatePageLink") == null, $"an older release: up to date, no link ({same})");

            var broken = await AskFeed("{\"name\":\"no version here\"}", "version");
            notes.Add("no version: " + broken);
            Check(broken.Length > 0 && FindIn(window, "UpdatePageLink") == null, $"a feed with no version: a failure said ({broken})");

            // The daily check: its box turns the setting on.
            await c.Call("test.settings.set", new { windowId, name = "CheckForUpdatesAutomatically", value = false });
            await About();
            await Click(window, FindIn(window, "AutoUpdateCheck")!);
            var on = false;
            await Eventually(async () => on = (bool?)(await c.Call("test.settings.get", new { windowId, name = "CheckForUpdatesAutomatically" }))["value"] == true, 4000);
            Check(on, "the daily check's box turns the setting on");
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            await Task.Delay(500);
            await c.Call("test.updates.feed", new { json = "{\"tag_name\":\"v99.1.0\",\"html_url\":\"https://example.com/releases/v99.1.0\"}" });
            var card = (string?)(await c.Call("test.updates.autoCheck", new { windowId }))["card"];
            notes.Add($"daily check, on: card {card ?? "none"}");
            Check(card == "update", "on, a newer release leaves its card");
            var later = await WaitFor(window, "UpdateCardLater", 5000);
            await Click(window, later);
            var gone = await Eventually(async () => { await Task.Delay(100); return FindIn(window, "UpdateCardLater") == null; }, 4000);
            Check(gone, "Later takes the card away");

            await c.Call("test.settings.set", new { windowId, name = "CheckForUpdatesAutomatically", value = false });
            card = (string?)(await c.Call("test.updates.autoCheck", new { windowId }))["card"];
            notes.Add($"daily check, off: card {card ?? "none"}");
            Check(card == null, "off, no card");
        }
        finally
        {
            try { await c.Call("test.updates.feed", new { json = (string?)null }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "CheckForUpdatesAutomatically", value = false }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "LastUpdateCheck", value = "" }); } catch { }
            try { if (FindIn(window, "UpdateCardLater") is { } l) await Click(window, l); } catch { }
            try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
        }
    }
}
