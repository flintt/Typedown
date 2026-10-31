using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// WC01: windows opened and closed again and again, as the close button closes them, each after its editor had a
/// while to load and a few keys and scrolls to answer: the process lives on. Closing a window let its web view go only
/// after the window had gone (the window's services were released later, on its queue), and a call the web view still
/// had on its way then failed in the window's message loop and ended the process (0xc000027b in CoreMessagingXP,
/// at the end of AC01 in 3 of 4 full runs).
/// </summary>
internal static partial class Program
{
    private static async Task WC01(List<string> notes)
    {
        using var host = Process.GetProcessById(hostPid);
        using var c = await Session("e2e WC01");
        var waits = new[] { 400, 1200, 2500, 4000 };
        var rounds = 24;
        for (var i = 0; i < rounds; i++)
        {
            var opened = (string)(await c.Call("test.window.open"))["windowId"]!;
            var window = await WindowOf(opened, c);
            await Task.Delay(waits[i % waits.Length]);
            // The page busy as the window goes: it scrolls and reports, a script call is on its way.
            try
            {
                _ = c.Call("test.editor.eval", new { windowId = opened, script = "for (let k = 0; k < 40; k++) window.scrollTo(0, k * 10); window.scrollY" });
            }
            catch { }
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            for (var k = 0; k < 6 && !host.HasExited; k++) await Task.Delay(250);
            if (host.HasExited)
            {
                notes.Add($"the process ended at round {i + 1} (exit code 0x{host.ExitCode:x8})");
                break;
            }
        }
        Check(!host.HasExited, $"the process lives on after {rounds} windows were opened and closed");
        using var again = await Session("e2e WC01 after");
        var left = ((JArray)(await again.Call("window.list"))["windows"]!).Count;
        notes.Add($"{left} window(s) left");
        Check(left >= 1, $"the first window is still there ({left})");
    }
}
