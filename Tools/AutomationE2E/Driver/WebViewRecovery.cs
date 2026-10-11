using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// WV01: the editor comes back after the web view's browser process ends under it (a WebView2 update, a crash, a tool
/// ending it): its document is on the page again and takes typing. The editor only reloaded its page, which a browser
/// process gone cannot do: the reload threw, every later load of the page too (back from the settings), and the editor
/// said it could not start until the app was restarted.
/// </summary>
internal static partial class Program
{
    // The web view browser processes the test host started: msedgewebview2 children of the host itself (its renderers
    // and helpers are children of those).
    private static List<int> WebViewBrowsersOf(int pid)
    {
        var found = new List<int>();
        var snapshot = CreateToolhelp32Snapshot(0x2, 0);
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                if (entry.th32ParentProcessID == pid && string.Equals(entry.szExeFile, "msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                    found.Add((int)entry.th32ProcessID);
        }
        finally { CloseHandle(snapshot); }
        return found;
    }

    private static async Task WV01(List<string> notes)
    {
        using var c = await Session("e2e WV01");
        var id = await Open(c, Fixture("wv01.md", "# WV01\n\nBefore the browser went.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Task.Delay(1500);
        var browsers = WebViewBrowsersOf(hostPid);
        notes.Add($"web view browser processes of the host: {string.Join(", ", browsers)}");
        if (browsers.Count == 0) throw new CaseFailed("no web view browser process under the test host");
        foreach (var pid in browsers)
            try { Process.GetProcessById(pid).Kill(); } catch (Exception ex) { notes.Add($"ending {pid}: {ex.Message}"); }
        // The page back with the document on it: asked of the page itself, as a reader sees it (with the caret in the
        // heading, its level is shown in it too).
        string? heading = null;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 20000 && !(heading ?? "").Contains("WV01"))
        {
            await Task.Delay(500);
            try { heading = (string?)(await c.Call("test.editor.eval", new { windowId, script = "document.querySelector('#ag-editor-id h1')?.textContent || ''" }))["result"]; }
            catch (Exception ex) { heading = null; if (clock.ElapsedMilliseconds > 19000) notes.Add("page: " + ex.Message); }
        }
        notes.Add($"after {clock.ElapsedMilliseconds} ms the page shows heading {heading ?? "(no page)"}; new browser processes: {string.Join(", ", WebViewBrowsersOf(hostPid))}");
        Check((heading ?? "").Contains("WV01"), "the editor is back with the document after its browser process ended");
        // And it is a working editor: what is typed reaches the document.
        await c.Call("document.replaceText", new { documentId = id, baseRevision = await Revision(c, id), find = "Before the browser went.", replacement = "Before the browser went. After it came back.", expectedCount = 1 });
        await Task.Delay(800);
        var text = (string?)(await Get(c, id))["text"] ?? "";
        notes.Add("text: " + Newtonsoft.Json.JsonConvert.SerializeObject(text));
        Check(text.Contains("Before the browser went.") && text.Contains("After it came back."), "the document is whole and takes an edit");
    }
}
