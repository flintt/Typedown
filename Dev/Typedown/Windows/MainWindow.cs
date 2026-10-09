using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core;
using Typedown.Core.Controls;
using Typedown.Core.Enums;
using Typedown.Core.Interfaces;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Typedown.Services;
using Typedown.Utilities;
using Windows.Globalization;
using Windows.Graphics;

namespace Typedown.Windows
{
    /// <summary>
    /// A document window: a WinUI 3 window (it was an XAML Islands XamlWindow) whose content reaches into the title bar.
    /// The system draws the caption buttons; the areas the controls mark with WindowChrome.Drag become the caption,
    /// dragged and double-clicked by the system.
    /// </summary>
    public class MainWindow : Window
    {
        private static readonly List<MainWindow> allWindows = new();

        /// <summary>The windows open, oldest first; a window leaves the list as it closes.</summary>
        public static IReadOnlyList<MainWindow> AllWindows => allWindows.ToList();

        /// <summary>The window an element is shown in.</summary>
        public static MainWindow GetWindow(UIElement element) =>
            element?.XamlRoot == null ? null : allWindows.FirstOrDefault(w => w.RootControl?.XamlRoot == element.XamlRoot);

        public IServiceScope ServiceScope { get; private set; } = Injection.ServiceProvider.CreateScope();

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public RootControl RootControl { get; private set; } = new();

        /// <summary>
        /// The window's content: a grid around the RootControl, as XamlUI's window had it. AppContentDialog shows itself
        /// by joining it, over the content; controls find their window's view model through its DataContext
        /// (InjectionExtensions), and it carries the theme, so a dialog shown in it is themed with the window.
        /// </summary>
        private readonly Microsoft.UI.Xaml.Controls.Grid rootGrid = new();

        public IServiceProvider ServiceProvider => ServiceScope?.ServiceProvider;

        public KeyboardAccelerator KeyboardAccelerator => ServiceProvider?.GetService<IKeyboardAccelerator>() as KeyboardAccelerator;

        public WindowService WindowService => ServiceProvider?.GetService<IWindowService>() as WindowService;

        public CompositeDisposable disposables = new();

        public Timer checkActiveTimer;

        /// <summary>The native window.</summary>
        public nint Handle { get; }

        /// <summary>Physical pixels per view pixel at the window's monitor.</summary>
        public double ScalingFactor => Handle == 0 ? 1 : Math.Max(96, PInvoke.GetDpiForWindow(Handle)) / 96.0;

        public bool IsActive { get; private set; }

        static MainWindow()
        {
            WindowChrome.WindowHandleOf = element => GetWindow(element)?.Handle ?? 0;
        }

        public MainWindow()
        {
            Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            allWindows.Add(this);
            TrySetPrimaryLanguage();
            // Applied again whenever the setting changes: the windows and dialogs opened after it then come up
            // in the new language, and the settings say which parts still need the program restarted.
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Language)).Subscribe(_ => TrySetPrimaryLanguage()));
            Title = Config.AppName;
            ExtendsContentIntoTitleBar = true;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)(480 * ScalingFactor);
                presenter.PreferredMinimumHeight = (int)(300 * ScalingFactor);
            }
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            rootGrid.DataContext = AppViewModel;
            rootGrid.Children.Add(RootControl);
            Content = rootGrid;
            RootControl.Loaded += OnLoaded;
            RootControl.LayoutUpdated += (_, _) => UpdateCaptionRegions();
            Activated += OnActivated;
            AppWindow.Changed += OnAppWindowChanged;
            AppWindow.Closing += OnClosing;
            Closed += OnClosed;
            WindowChrome.DragAreasChanged += OnDragAreasChanged;
            InitializeBinding();
            checkActiveTimer = new(CheckActiveTimerCallback, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1));
            LastActive ??= this;
        }

        /// <summary>Shown where the window was last (or centred at its first size), and activated.</summary>
        public void Start()
        {
            if (!this.ShowWindowWithSavedPlacement())
            {
                var size = new SizeInt32((int)(1130 * ScalingFactor), (int)(700 * ScalingFactor));
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
            }
            Activate();
            if (Config.WindowsBuild < 22000) KeepTopEdgeInClientArea();
        }

        public void InitializeBinding()
        {
            // The theme in force, as the editor and the panels have it: a custom theme's base, else the built-in setting.
            // Following the built-in setting alone left the window (its menus) light under a dark custom theme whenever
            // the two settings disagreed - a theme file whose "base" was edited, or settings written elsewhere.
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme))
                .Merge(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)))
                .Select(_ => AppViewModel.SettingsViewModel.EffectiveTheme)
                .StartWith(AppViewModel.SettingsViewModel.EffectiveTheme)
                .Subscribe(SetTheme));
            disposables.Add(AppViewModel.UIViewModel.WhenPropertyChanged(nameof(UIViewModel.MainWindowTitle)).Cast<string>().StartWith(AppViewModel.UIViewModel.MainWindowTitle).Subscribe(SetTitle));
            disposables.Add(AppViewModel.FileViewModel.NewWindowCommand.OnExecute.Subscribe(path => Utilities.Common.OpenNewWindow(new string[] { path }, forceNewWindow: true)));
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.UseMicaEffect)).Cast<bool>().StartWith(AppViewModel.SettingsViewModel.UseMicaEffect).Subscribe(EnableMicaEffect));
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Topmost)).Cast<bool>().StartWith(AppViewModel.SettingsViewModel.Topmost).Subscribe(SetTopmost));
            disposables.Add(AppViewModel.UIViewModel.WhenPropertyChanged(nameof(UIViewModel.IsFullScreen)).Cast<bool>().Subscribe(SetFullScreen));
        }

        private void SetTheme(AppTheme theme)
        {
            var requested = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.Black => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            rootGrid.RequestedTheme = requested;
            // The caption buttons are the system's: given the content's colours, light or dark.
            var dark = requested == ElementTheme.Dark || (requested == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            // The frame the system draws (the top edge on Windows 10, the border on Windows 11) in the same theme, as
            // XamlUI's window did; it follows the system's theme otherwise.
            uint darkFrame = dark ? 1u : 0u;
            PInvoke.DwmSetWindowAttribute(Handle, Config.WindowsBuild >= 18985 ? PInvoke.DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE : (PInvoke.DwmWindowAttribute)19, ref darkFrame, sizeof(uint));
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            bar.ButtonHoverBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : global::Windows.UI.Color.FromArgb(0x20, 0, 0, 0);
            bar.ButtonPressedBackgroundColor = dark ? global::Windows.UI.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : global::Windows.UI.Color.FromArgb(0x30, 0, 0, 0);
            bar.ButtonInactiveForegroundColor = dark ? global::Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : global::Windows.UI.Color.FromArgb(0x80, 0, 0, 0);
        }

        private void SetTitle(string title)
        {
            Title = title;
        }

        // Borderless full screen (upstream #11): the system's full-screen presenter covers the monitor and hides the caption
        // buttons; the in-app caption and menu bar hide through RootControl/MainPage. Leaving it puts the window back as it was.
        private void SetFullScreen(bool enable)
        {
            try
            {
                AppWindow.SetPresenter(enable ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
                if (!enable) SetTopmost(AppViewModel.SettingsViewModel.Topmost);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("FullScreen", ex.ToString());
            }
        }

        private void SetTopmost(bool topmost)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.IsAlwaysOnTop = topmost;
        }

        private void EnableMicaEffect(bool enable)
        {
            try
            {
                SystemBackdrop = enable && Config.IsMicaSupported ? new MicaBackdrop() : null;
                RootControl.Background = SystemBackdrop != null ? new SolidColorBrush(Colors.Transparent) : null;
                RootControl.ShowWindowBackground(SystemBackdrop == null);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("MicaEffect", $"enable={enable} IsMicaSupported={Config.IsMicaSupported} build={Config.WindowsBuild} OS={Environment.OSVersion.VersionString}\n{ex}");
                SystemBackdrop = null;
                RootControl.Background = null;
                RootControl.ShowWindowBackground(true);
            }
        }

        // ---- the top edge on Windows 10 ----

        private delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);

        [System.Runtime.InteropServices.DllImport("comctl32.dll")]
        private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern nint FindWindowEx(nint parent, nint after, string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(nint hwnd, out PInvokeRect rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hwnd, out PInvokeRect rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ScreenToClient(nint hwnd, ref PInvokePoint point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

        private struct PInvokeRect { public int Left, Top, Right, Bottom; }

        private struct PInvokePoint { public int X, Y; }

        private SubclassProc topEdgeProc;

        /// <summary>
        /// On Windows 10 the window lays its content (the DesktopChildSiteBridge child window) one pixel below its top
        /// and paints that row itself, white whatever the theme: a dialog's smoke stopped short of it and it stayed light
        /// under a dark theme. XamlUI's window let the content draw the top row too. Here every position the window gives
        /// its content is taken one pixel up and made one taller (WM_WINDOWPOSCHANGING of the content window), and the
        /// content is put there once now. Windows 11 draws a border around every window instead, in the frame's theme
        /// (SetTheme).
        /// </summary>
        private void KeepTopEdgeInClientArea()
        {
            var bridge = FindWindowEx(Handle, 0, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
            if (bridge == 0 || topEdgeProc != null) return;
            topEdgeProc = (hwnd, msg, wParam, lParam, id, data) =>
            {
                if (msg == 0x0046 /* WM_WINDOWPOSCHANGING */)
                {
                    // WINDOWPOS: hwnd, hwndInsertAfter, x, y, cx, cy, flags.
                    var flags = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 32);
                    var noMove = (flags & 0x0002) != 0;
                    var noSize = (flags & 0x0001) != 0;
                    var y = System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 20);
                    var cy = System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 28);
                    if (!noMove && y >= 1 && y <= 2)
                    {
                        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 20, 0);
                        if (!noSize) System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 28, cy + y);
                    }
                    else if (noMove && !noSize && GetClientRect(Handle, out var client) && TopOf(hwnd) == 0 && cy >= client.Bottom - 2 && cy < client.Bottom)
                        System.Runtime.InteropServices.Marshal.WriteInt32(lParam, 28, client.Bottom);
                }
                return DefSubclassProc(hwnd, msg, wParam, lParam);
            };
            SetWindowSubclass(bridge, topEdgeProc, 1, 0);
            if (TopOf(bridge) is var top && top >= 1 && top <= 2 && GetWindowRect(bridge, out var at))
                // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER: the hook above makes it the top row.
                SetWindowPos(bridge, 0, 0, top, at.Right - at.Left, at.Bottom - at.Top, 0x0004 | 0x0010 | 0x0200);
        }

        private int TopOf(nint child)
        {
            if (!GetWindowRect(child, out var at)) return -1;
            var origin = new PInvokePoint { X = at.Left, Y = at.Top };
            ScreenToClient(Handle, ref origin);
            return origin.Y;
        }

        // ---- the caption areas ----

        private readonly List<WeakReference<FrameworkElement>> dragAreas = new();
        private string lastRegions;

        private void OnDragAreasChanged(FrameworkElement element)
        {
            lock (dragAreas)
            {
                dragAreas.RemoveAll(x => !x.TryGetTarget(out var e) || e == element);
                if (WindowChrome.GetDrag(element)) dragAreas.Add(new WeakReference<FrameworkElement>(element));
            }
            UpdateCaptionRegions();
        }

        // The marked areas of this window that are on screen, in the window's physical pixels, as its caption regions; set
        // again only when they moved.
        private void UpdateCaptionRegions()
        {
            if (RootControl?.XamlRoot == null) return;
            var scale = RootControl.XamlRoot.RasterizationScale;
            var rects = new List<RectInt32>();
            lock (dragAreas)
                foreach (var weak in dragAreas)
                {
                    if (!weak.TryGetTarget(out var e) || e.XamlRoot != RootControl.XamlRoot || !WindowChrome.GetDrag(e) || e.ActualWidth <= 0 || e.ActualHeight <= 0 || !IsShown(e)) continue;
                    try
                    {
                        var at = e.TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point(0, 0));
                        rects.Add(new RectInt32((int)Math.Round(at.X * scale), (int)Math.Round(at.Y * scale), (int)Math.Round(e.ActualWidth * scale), (int)Math.Round(e.ActualHeight * scale)));
                    }
                    catch (ArgumentException)
                    {
                        // not in the tree any more
                    }
                }
            var key = string.Join(";", rects.Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
            if (key == lastRegions) return;
            lastRegions = key;
            try
            {
                InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption, rects.ToArray());
            }
            catch (Exception ex)
            {
                Log.Debug($"caption regions: {ex.Message}");
            }
        }

        private static bool IsShown(FrameworkElement element)
        {
            for (DependencyObject node = element; node != null; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement u && u.Visibility != Visibility.Visible) return false;
            return true;
        }

        // ---- the window's life ----

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SaveWindowPlacementWithOffset(false);
            AppViewModel.MainWindow = Handle;
            Core.Services.AutomationWindows.Register(AppViewModel, DispatcherQueue);
            UpdateCaptionRegions();
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPresenterChange || args.DidSizeChange)
                WindowService?.RaiseWindowStateChanged(Handle);
            if ((args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) && (RootControl?.IsLoaded ?? false))
                SaveWindowPlacementWithOffset();
        }

        /// <summary>The window that was most recently active; files opened from the shell land here as tabs.</summary>
        public static MainWindow LastActive { get; private set; }

        private void OnActivated(object sender, WindowActivatedEventArgs e)
        {
            IsActive = e.WindowActivationState != WindowActivationState.Deactivated;
            WindowService?.RaiseWindowIsActivedChanged(Handle);
            if (KeyboardAccelerator != null) KeyboardAccelerator.IsEnable = IsActive;
            if (IsActive) LastActive = this;
        }

        private bool isCloseable = false;

        private bool isClosing = false;

        private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
        {
            try
            {
                if (!isCloseable)
                {
                    e.Cancel = true;
                    if (!isClosing)
                    {
                        isClosing = true;
                        await AppViewModel.FileViewModel.AutoSaveFile();
                        if (await AppViewModel.TabsViewModel.AskToSaveAll())
                        {
                            AppViewModel.TabsViewModel.SaveSession();
                            ForceClose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // The window stays open (nothing unsaved is lost) and can be closed again; out of the handler it ended
                // the app with whatever was not saved.
                Log.WriteLocal("WindowClosing", ex.ToString());
            }
            finally
            {
                isClosing = false;
            }
        }

        public async void ForceClose()
        {
            this.TrySaveWindowPlacement();
            // Window placement is itself a setting. Do not let process shutdown cut off the queued atomic write.
            await AppViewModel.SettingsViewModel.FlushSettingsAsync();
            isCloseable = true;
            await Task.Yield();
            if (!closed)
                Close();
        }

        /// <summary>Set as the window's close handler starts: from then on it is not one of the open windows.</summary>
        private bool closed;

        /// <summary>The window is closing for good (past the save question) or has closed.</summary>
        internal bool IsGoingAway => isCloseable || closed;

        private void OnClosed(object sender, WindowEventArgs e)
        {
            closed = true;
            allWindows.Remove(this);
            WindowChrome.DragAreasChanged -= OnDragAreasChanged;
            // A window that closes without the user asking for it is how "the app just vanished" happens, and
            // nothing else records it: note who asked.
            Log.Debug($"window closed\n{Environment.StackTrace}");
            if (LastActive == this) LastActive = null;
            var keepRun = AppViewModel.SettingsViewModel.KeepRun;
            checkActiveTimer?.Dispose();
            checkActiveTimer = null;
            if (ServiceScope != null) Core.Services.AutomationWindows.Unregister(AppViewModel);
            // The content is taken away first and the scope disposed after it has unloaded: WinUI raises the controls'
            // Unloaded after Closed, and their handlers still reach the window's services (EditorContainer's editor) -
            // disposed first, the close ended in an ObjectDisposedException.
            var scope = ServiceScope;
            RootControl = null;
            Content = null;
            void Release() { ServiceScope = null; scope?.Dispose(); }
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Release)) Release();
            disposables.Dispose();
            // Whether this was the last window is read off the open windows, not off the view models (kept by weak
            // references; one not yet collected counted as a live window, and the process stayed headless with the
            // single-instance mutex).
            var others = allWindows.Count(x => x != this && !x.closed);
            var instances = AppViewModel.GetInstances().Count;
            if (others == 0)
            {
                if (keepRun)
                {
                    Log.Debug($"last window closed, staying (KeepRun on; {instances} view model(s) still referenced)");
                    Process.GetCurrentProcess().MaxWorkingSet = Process.GetCurrentProcess().MinWorkingSet;
                }
                else
                {
                    Log.Debug($"last window closed, exiting ({instances} view model(s) still referenced)");
                    Application.Current.Exit();
                }
            }
            else
            {
                Log.Debug($"window closed, {others} other window(s) remain");
            }
        }

        private bool isPlacementSaving = false;

        private async void SaveWindowPlacementWithOffset(bool throttle = true)
        {
            if (!isPlacementSaving && !isCloseable && !isClosing && AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
            {
                isPlacementSaving = true;
                try
                {
                    if (throttle) await Task.Delay(100);
                    await DispatcherQueue.RunAsync(() => this.TrySaveWindowPlacement(new(8, 8)));
                }
                finally
                {
                    isPlacementSaving = false;
                }
            }
        }

        private void CheckActiveTimerCallback(object state)
        {
            _ = DispatcherQueue.RunIdleAsync(() =>
            {
                if (Handle != 0 && KeyboardAccelerator != null)
                {
                    var isActive = PInvoke.GetForegroundWindow() == Handle;
                    if (isActive != KeyboardAccelerator.IsEnable)
                        KeyboardAccelerator.IsEnable = isActive;
                }
            });
        }

        private void TrySetPrimaryLanguage()
        {
            var settingLanguage = AppViewModel.SettingsViewModel.Language;
            var supported = Locale.SupportedLangs.ContainsKey(settingLanguage);
            try
            {
                // Only works with package identity (the MSIX build); it throws in the installer and portable
                // builds, which is why the language is also set on the resource contexts below.
                ApplicationLanguages.PrimaryLanguageOverride = supported ? settingLanguage : string.Empty;
            }
            catch (Exception ex)
            {
                Log.Debug($"language: PrimaryLanguageOverride refused: {ex.Message}");
            }
            Locale.ApplyLanguage(supported ? settingLanguage : null);
        }
    }
}
