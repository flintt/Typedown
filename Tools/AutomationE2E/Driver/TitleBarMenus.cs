using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// MB01: a menu open while the title bar is clicked or dragged closes, as one does when anything else outside it is
/// clicked: the File menu and the editor's context menu, clicked on the empty title bar, and the File menu with the
/// window dragged by its title bar. The window's old host closed open menus on a title-bar click; the WinUI 3 window
/// hands those clicks to the system, and a menu stayed open there, or floated where the window had been.
/// </summary>
internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool MbSetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private static async Task MB01(List<string> notes)
    {
        using var c = await Session("e2e MB01");
        var id = await Open(c, Fixture("mb01.md", "# MB01\n\nSome text in the editor.\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Activate(window);
        await Task.Delay(1000);
        var failures = new List<string>();
        async Task<int> Popups() => ((JArray)(await c.Call("test.window.popups", new { windowId }))["popups"]!).Count;
        // The empty title bar: the widest caption region the window gives the system, its middle, on the screen.
        async Task<(int X, int Y)> TitleBarPoint()
        {
            var regions = (string?)(await c.Call("test.window.layout", new { windowId }))["nonClientRegions"] ?? "";
            var caption = regions.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("Caption:")) ?? "";
            var best = caption.Substring("Caption:".Length).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Chunk(2).Where(pair => pair.Length == 2)
                .Select(pair => (xy: pair[0].Split(','), wh: pair[1].Split('x')))
                .Select(r => (x: int.Parse(r.xy[0]), y: int.Parse(r.xy[1]), w: int.Parse(r.wh[0]), h: int.Parse(r.wh[1])))
                .OrderByDescending(r => r.w).FirstOrDefault();
            if (best.w == 0) throw new CaseFailed($"no caption region to click ({regions})");
            var origin = new POINT { X = best.x + best.w / 2, Y = best.y + best.h / 2 };
            DmClientToScreen(window, ref origin);
            return (origin.X, origin.Y);
        }
        // As a hand does it, without bringing the window forward first (that alone would close a menu).
        void MoveTo(int x, int y)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = x * 65535 / (w - 1), dy = y * 65535 / (h - 1), dwFlags = 0x0001 | 0x8000 } } });
        }
        async Task OpenFileMenu()
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(window);
            var bar = Deep.First(root, new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.MenuBar),
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.FrameworkIdProperty, "XAML"))) ?? throw new CaseFailed("no menu bar in the window");
            var file = bar.FindFirst(System.Windows.Automation.TreeScope.Children, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.MenuItem))
                ?? throw new CaseFailed("no File menu");
            ((System.Windows.Automation.ExpandCollapsePattern)file.GetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern)).Expand();
            await Task.Delay(600);
        }
        async Task Step(string step, Func<Task> open, Func<(int X, int Y), Task> act)
        {
            await Activate(window);
            await Task.Delay(300);
            await open();
            var before = await Popups();
            var at = await TitleBarPoint();
            await act(at);
            await Task.Delay(800);
            var after = await Popups();
            notes.Add($"{step}: {before} open before, {after} after (title bar at {at.X},{at.Y})");
            if (before == 0) failures.Add($"{step}: the menu did not open");
            else if (after != 0) failures.Add($"{step}: the menu is still open");
            // Whatever is left, closed for the next step.
            for (var i = 0; i < 2 && after != 0; i++) { Send(Key(0x1B, false), Key(0x1B, true)); await Task.Delay(200); }
        }
        async Task ClickAt((int X, int Y) at)
        {
            MoveTo(at.X - 4, at.Y); await Task.Delay(60); MoveTo(at.X, at.Y); await Task.Delay(120);
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
            await Task.Delay(60);
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
        }
        GetWindowRect(window, out var placed);
        try
        {
            await Step("File menu, title bar clicked", OpenFileMenu, ClickAt);
            await Step("context menu, title bar clicked", async () => { await ContextMenuAt(c, windowId, window, "#ag-editor-id p"); }, ClickAt);
            await Step("File menu, window dragged by its title bar", OpenFileMenu, async at =>
            {
                MoveTo(at.X, at.Y); await Task.Delay(120);
                Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
                for (var i = 1; i <= 8; i++) { await Task.Delay(40); MoveTo(at.X + i * 8, at.Y + i * 3); }
                await Task.Delay(100);
                Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
            });
            // A menu closed that way opens again at the first click, as before.
            await Activate(window);
            await OpenFileMenu();
            var again = await Popups();
            notes.Add($"File menu opened again after the drag: {again} open");
            if (again == 0) failures.Add("the File menu did not open again after the drag closed it");
            for (var i = 0; i < 2; i++) { Send(Key(0x1B, false), Key(0x1B, true)); await Task.Delay(200); }
        }
        finally
        {
            // The window back where it was.
            MbSetWindowPos(window, IntPtr.Zero, placed.Left, placed.Top, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }
        Check(failures.Count == 0, string.Join("; ", failures));
    }
}
