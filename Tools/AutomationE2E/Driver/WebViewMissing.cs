using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// WV02, WV03: a start-up on a machine without the WebView2 runtime (todo #15). A host of its own, in a folder of its
/// own, told by TYPEDOWN_TEST_WEBVIEW2_MISSING to find no runtime; its setup is a stand-in that ends at once, so
/// nothing is installed. The window is found by the process, its parts by their automation ids (any language).
/// </summary>
internal static partial class Program
{
    private static (Process Host, string Root) StartHostWithoutWebView2(string mode, string name)
    {
        var root = testRoot + "-" + name;
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo(hostExe, $"--automation-test-root \"{root}\"") { UseShellExecute = false };
        start.Environment["TYPEDOWN_TEST_WEBVIEW2_MISSING"] = mode;
        return (Process.Start(start)!, root);
    }

    private static List<IntPtr> VisibleWindowsOf(int pid)
    {
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == pid && IsWindowVisible(window) && GetWindowRect(window, out var r) && r.Right - r.Left > 200 && r.Bottom - r.Top > 150)
                found.Add(window);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static async Task<IntPtr> WaitForVisibleWindowOf(Process host, int timeoutMs, string what)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (host.HasExited) throw new CaseFailed($"the host exited (code {host.ExitCode}) before {what}");
            var window = VisibleWindowsOf(host.Id).FirstOrDefault();
            if (window != IntPtr.Zero) return window;
            await Task.Delay(100);
        }
        throw new CaseFailed($"no {what} within {timeoutMs} ms");
    }

    /// <summary>What the host wrote of its start: the install window's and the start's lines, and the error files.</summary>
    private static void HostLogNotes(string root, List<string> notes)
    {
        var logs = Path.Combine(root, "logs");
        try
        {
            var debug = Path.Combine(logs, "debug.log");
            if (File.Exists(debug))
            {
                using var reader = new StreamReader(new FileStream(debug, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                foreach (var line in reader.ReadToEnd().Split('\n').Where(l => l.Contains("webview2 setup") || l.Contains("startup") || l.Contains("exception:")).Take(40))
                    notes.Add("host log: " + line.TrimEnd('\r'));
            }
            else notes.Add("host log: no debug.log");
            if (Directory.Exists(logs))
                foreach (var file in Directory.GetFiles(logs).Where(f => !f.EndsWith("debug.log")).Take(5))
                    notes.Add($"host error file {Path.GetFileName(file)}: " + string.Join(" | ", File.ReadAllLines(file).Skip(5).Take(8)));
        }
        catch (Exception e) { notes.Add("host log: " + e.Message); }
    }

    private static void EndHost(Process host, string root, List<string> notes, bool failed)
    {
        // A failure's host log goes into the notes first, and its folder is kept.
        if (failed) { HostLogNotes(root, notes); notes.Add("host folder kept: " + root); }
        try
        {
            if (!host.HasExited)
            {
                foreach (var window in VisibleWindowsOf(host.Id)) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                if (!host.WaitForExit(20000)) { notes.Add("the host did not exit within 20 s of its windows closed; killed"); host.Kill(true); }
            }
        }
        catch (Exception e) { notes.Add("ending the host: " + e.Message); }
        host.Dispose();
        if (failed) return;
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// WV02: the runtime missing, the install window comes up with the setup and the download; the setup that installs
    /// it lets the start go on to the main window (the install window's close used to end the app with it, as the last
    /// window), and the install window is gone then.
    /// </summary>
    private static async Task WV02(List<string> notes)
    {
        var (host, root) = StartHostWithoutWebView2("install", "wv02");
        var failed = true;
        try
        {
            var window = await WaitForVisibleWindowOf(host, 30000, "install window");
            await Task.Delay(1000);
            notes.Add("the install window as it came up: " + Screenshot("WV02-install-window"));
            Check(FindIn(window, "WebView2DownloadLink") != null, "the install window has no download link");
            Check(FindIn(window, "WebView2InstallFailedText") == null, "the install window says the setup failed before it ran");
            Check(!File.Exists(Path.Combine(root, "automation-endpoint.txt")), "the main window started although the runtime is missing");
            await Click(window, await WaitFor(window, "WebView2InstallButton"));
            var endpointName = await ReadEndpointFile(Path.Combine(root, "automation-endpoint.txt"));
            Check(endpointName != null, $"no main window after the setup installed the runtime (host {(host.HasExited ? $"exited, code {host.ExitCode}" : "running")})");
            Client? c = null;
            for (var i = 0; c == null; i++)
            {
                try { c = new Client(endpointName!); }
                catch (Exception) when (i < 100) { await Task.Delay(100); }
            }
            using (c)
            {
                await Initialize(c, "e2e WV02");
                await WaitForWindow(c);
            }
            Check(await Eventually(() => Task.FromResult(!IsWindow(window) || !IsWindowVisible(window)), 5000), "the install window is still on screen next to the main window");
            Check(await Eventually(() => Task.FromResult(!IsWindow(window)), 5000), "the install window was hidden but never closed");
            Check(!host.HasExited, "the host exited after the main window came up");
            failed = false;
        }
        finally { EndHost(host, root, notes, failed); }
    }

    /// <summary>
    /// WV03: a setup that did not install the runtime leaves the install window on screen saying so, with the download
    /// page; Cancel then ends the start, no main window ever.
    /// </summary>
    private static async Task WV03(List<string> notes)
    {
        var (host, root) = StartHostWithoutWebView2("fail", "wv03");
        var failed = true;
        try
        {
            var window = await WaitForVisibleWindowOf(host, 30000, "install window");
            await Task.Delay(1000);
            notes.Add("the install window as it came up: " + Screenshot("WV03-install-window"));
            await Click(window, await WaitFor(window, "WebView2InstallButton"));
            await WaitFor(window, "WebView2InstallFailedText", 15000);
            Check(IsWindowVisible(window), "the install window left the screen after the setup failed");
            Check(FindIn(window, "WebView2DownloadLink") != null, "the download link is gone after the setup failed");
            Check(!host.HasExited, "the host ended after the setup failed, without a word");
            notes.Add("the install window after the setup failed: " + Screenshot("WV03-install-failed"));
            await Click(window, await WaitFor(window, "WebView2CancelButton"));
            Check(host.WaitForExit(15000), "the host did not end after Cancel");
            Check(!File.Exists(Path.Combine(root, "automation-endpoint.txt")), "a main window started without the runtime");
            failed = false;
        }
        finally { EndHost(host, root, notes, failed); }
    }
}
