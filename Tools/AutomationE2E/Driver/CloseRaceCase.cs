using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal static partial class Program
{
    /// <summary>
    /// CW01: windows opened and closed again at once, twenty times - the close crash's moment (0xc000027b: work queued for
    /// a window ran after it closed and threw, which WinUI ends the process for; two dumps, hp, 2026-10 and 11-03). A
    /// window's work is queued at 300 ms (its size put right) and 2 s (the drop target), the closes come before and
    /// between. The test host answers after each, and is the same process at the end; the callbacks that threw (now
    /// logged, DispatcherQueueCallback) are named in the notes - what is left to fix at its root.
    /// </summary>
    private static async Task CW01(List<string> notes)
    {
        using var c = await Session("e2e CW01");
        var path = Fixture("cw01.md", "# CW01\n\nA window opened and closed at once.\n");
        var pidBefore = hostPid;
        var delays = new[] { 50, 150, 300, 400, 900, 1600, 2100, 2600 };
        for (var i = 0; i < 20; i++)
        {
            var opened = await c.Call("test.window.open", new { path });
            var windowId = (string)opened["windowId"]!;
            var window = await WindowOf(windowId, c);
            await Task.Delay(delays[i % delays.Length]);
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            await Task.Delay(400);
            // Still there: the automation answers.
            try { await c.Call("window.list"); }
            catch (Exception e) { throw new CaseFailed($"after close {i + 1} (open {delays[i % delays.Length]} ms) the test host did not answer: {e.Message}"); }
        }
        await Task.Delay(3000);
        await c.Call("window.list");
        bool alive;
        try { alive = !System.Diagnostics.Process.GetProcessById(pidBefore).HasExited; } catch (ArgumentException) { alive = false; }
        Check(alive, "twenty windows opened and closed at once: the app is still running, the same process");

        // The work that threw after a close, as the log has it.
        var logs = Directory.Exists(testRoot)
            ? Directory.GetFiles(testRoot, "*-DispatcherQueueCallback.log", SearchOption.AllDirectories)
            : Array.Empty<string>();
        notes.Add($"queued work that threw: {logs.Length}");
        foreach (var log in logs.Take(5))
        {
            var lines = File.ReadAllLines(log).SkipWhile(l => l.Length > 0).Skip(1).Take(3).Select(l => l.Length > 220 ? l[..220] : l);
            notes.Add($"{Path.GetFileName(log)}: {string.Join(" | ", lines)}");
        }
    }
}
