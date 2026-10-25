using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Where the window comes back after the process ends, the close button's way and killed. Reported: started again
    // after it was ended without the close, the window came back 8 px (24 px at 300%) lower right each time, and the
    // status bar ended under the taskbar (SB01, SB02 after SR01's restarts).
    private static async Task WD01(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        string Show(RECT r) => $"{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}";
        async Task<(Client C, string W, IntPtr Handle, RECT Rect)> Where(Client c)
        {
            var w = await FirstWindowId(c);
            var handle = await WindowOf(w, c);
            await Task.Delay(1500); // loaded and its place saved, as a window shown a while is
            GetWindowRect(handle, out var r);
            return (c, w, handle, r);
        }
        var (c, windowId, window, start) = await Where(await Session("e2e WD01"));
        notes.Add("the window at the start: " + Show(start));
        try
        {
            foreach (var killed in new[] { true, true, true, false, false, false })
            {
                await c.Call("test.settings.flush", new { windowId });
                using (var host = Process.GetProcessById(hostPid))
                {
                    if (killed) host.Kill();
                    else PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    if (!host.WaitForExit(20000)) { host.Kill(); throw new CaseFailed("the host did not exit within 20 s of its window's close (a save question?)"); }
                }
                c.Dispose();
                (c, windowId, window, var now) = await Where(await StartHostAgain(notes, "e2e WD01 again"));
                notes.Add($"{(killed ? "killed" : "closed")}, started again: " + Show(now));
                Check(now.Left == start.Left && now.Top == start.Top && now.Right == start.Right && now.Bottom == start.Bottom, $"{(killed ? "killed" : "closed with the close button")} and started again, the window is where it was ({Show(now)}, was {Show(start)})");
            }
            // Another window opened while this one is up: set off lower right, not on top of it.
            var other = (string)(await c.Call("test.window.open"))["windowId"]!;
            var otherHandle = await WindowOf(other, c);
            await Task.Delay(1500);
            GetWindowRect(otherHandle, out var second);
            notes.Add("another window: " + Show(second));
            PostMessage(otherHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            for (var i = 0; i < 40 && IsWindow(otherHandle); i++) await Task.Delay(250);
            var dx = second.Left - start.Left;
            Check(dx > 0 && second.Top - start.Top == dx && second.Right - second.Left == start.Right - start.Left, $"another window opens set off lower right of the first ({Show(second)}, the first {Show(start)})");
        }
        finally
        {
            try
            {
                GetWindowRect(window, out var now);
                if (now.Left != start.Left || now.Top != start.Top || now.Right != start.Right || now.Bottom != start.Bottom)
                    MbSetWindowPos(window, IntPtr.Zero, start.Left, start.Top, start.Right - start.Left, start.Bottom - start.Top, 0x0004 | 0x0010);
            }
            catch (Exception e) { notes.Add("the window not put back: " + e.Message.Split('\n')[0]); }
            c.Dispose();
        }
    }
}
