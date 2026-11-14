using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Typedown.Core;
using Typedown.Core.Controls.DialogControls;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Typedown.Windows;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Typedown.Services
{
    /// <summary>
    /// Settings > General: New from Clipboard's shortcut in other programs too. Registered with RegisterHotKey on a
    /// message-only window of the process (not on a window the reader may close), when the setting is on; pressed, the
    /// window last active - or a new one, where the process runs with none (Keep running) - comes to the front and makes
    /// a new tab of the clipboard (FileViewModel.NewFromClipboardAsync). Read from the settings store, which every window
    /// shares: turned on or off, or its key changed, in any window, it is registered again. A key another program has
    /// registered cannot be had: said so when the reader has just asked for it, written to the log at a start.
    /// </summary>
    internal static class GlobalShortcut
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0x544C; // "TL"
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        private static readonly IntPtr HWND_MESSAGE = new(-3);

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style;
            public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

        private static WndProc proc; // kept: the window calls it for as long as the process runs
        private static IntPtr window;
        private static DispatcherQueue dispatcher;
        private static JsonSettingsStore store;
        private static bool registered;
        private static ShortcutKey registeredKey;

        /// <summary>The key registered now, or null (off, no key, or another program has it): for tests and the log.</summary>
        public static ShortcutKey Registered => registered ? registeredKey : null;

        public static void Initialize(DispatcherQueue queue)
        {
            if (window != IntPtr.Zero) return;
            dispatcher = queue;
            store = JsonSettingsStore.Shared(Path.Combine(Config.GetLocalFolderPath(), "Settings.json"));
            proc = WindowProc;
            var cls = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = proc,
                hInstance = GetModuleHandle(null),
                lpszClassName = Config.InstanceName + ".GlobalShortcut",
            };
            RegisterClassEx(ref cls);
            window = CreateWindowEx(0, cls.lpszClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                Log.Debug($"global shortcut: no message window ({Marshal.GetLastWin32Error()}); not available");
                return;
            }
            store.Changed += (name, _) =>
            {
                if (name is null or nameof(SettingsViewModel.GlobalNewFromClipboard) or nameof(SettingsViewModel.ShortcutNewFromClipboard))
                    dispatcher.TryEnqueueSafe(() => Apply(asked: true));
            };
            Apply(asked: false);
        }

        private static void Apply(bool asked)
        {
            if (registered) UnregisterHotKey(window, HotkeyId);
            registered = false;
            registeredKey = null;
            if (!store.Get(nameof(SettingsViewModel.GlobalNewFromClipboard), false)) return;
            var key = store.Get(nameof(SettingsViewModel.ShortcutNewFromClipboard), SettingsViewModel.DefaultShortcutNewFromClipboard);
            if (key == null)
            {
                Log.Debug("global shortcut: on, but New from Clipboard has no key");
                return;
            }
            var modifiers = MOD_NOREPEAT
                | (key.Modifiers.HasFlag(VirtualKeyModifiers.Control) ? MOD_CONTROL : 0)
                | (key.Modifiers.HasFlag(VirtualKeyModifiers.Menu) ? MOD_ALT : 0)
                | (key.Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? MOD_SHIFT : 0)
                | (key.Modifiers.HasFlag(VirtualKeyModifiers.Windows) ? MOD_WIN : 0);
            if (RegisterHotKey(window, HotkeyId, modifiers, (uint)key.Key))
            {
                registered = true;
                registeredKey = key;
                Log.Debug($"global shortcut: New from Clipboard on {key.Modifiers}+{key.Key} in every program");
                return;
            }
            var error = Marshal.GetLastWin32Error();
            Log.Debug($"global shortcut: {key.Modifiers}+{key.Key} could not be registered ({error}: another program has it)");
            if (asked) _ = SayTaken(key);
        }

        private static async Task SayTaken(ShortcutKey key)
        {
            var target = MainWindow.LastActive is { IsGoingAway: false } last ? last : MainWindow.AllWindows.FirstOrDefault(w => !w.IsGoingAway);
            if (target?.AppViewModel?.XamlRoot == null) return;
            var text = new Core.Converters.ShortcutKeyToStringConverter().Convert(key, typeof(string), null, null) as string;
            try
            {
                await AppContentDialog.Create(Locale.GetString("NewFromClipboard"), string.Format(Locale.GetDialogString("GlobalShortcutTaken"), text), Locale.GetDialogString("Ok")).ShowAsync(target.AppViewModel.XamlRoot);
            }
            catch (Exception ex) { Log.Debug($"global shortcut: could not say the key is taken: {ex.Message}"); }
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Pressed();
                return IntPtr.Zero;
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private static async void Pressed()
        {
            try
            {
                var target = MainWindow.LastActive is { IsGoingAway: false } last ? last : MainWindow.AllWindows.FirstOrDefault(w => !w.IsGoingAway);
                if (target == null)
                {
                    // The process kept with no window (Keep running): a window for it, its startup document in first.
                    Log.Debug("global shortcut: pressed with no window open; opening one");
                    target = new MainWindow();
                    target.AppViewModel.CommandLineArgs = Array.Empty<string>();
                    target.Start();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var editor = target.AppViewModel.EditorViewModel;
                    while ((editor.LoadId == 0 || editor.ConfirmedLoadId < editor.LoadId) && clock.ElapsedMilliseconds < 20000)
                        await Task.Delay(50);
                }
                else Log.Debug("global shortcut: pressed; New from Clipboard in the window last active");
                // WM_HOTKEY lets this process take the foreground.
                if (PInvoke.IsIconic(target.Handle)) PInvoke.ShowWindow(target.Handle, PInvoke.ShowWindowCommand.Restore);
                PInvoke.SetForegroundWindow(target.Handle);
                await target.AppViewModel.FileViewModel.NewFromClipboardAsync();
            }
            catch (Exception ex) { Log.Debug($"global shortcut: New from Clipboard failed: {ex}"); }
        }
    }
}
