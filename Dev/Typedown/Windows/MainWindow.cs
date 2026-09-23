using Microsoft.Extensions.DependencyInjection;
using System;
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
using Typedown.XamlUI;
using Windows.Globalization;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Typedown.Windows
{
    public class MainWindow : XamlWindow
    {
        public IServiceScope ServiceScope { get; private set; } = Injection.ServiceProvider.CreateScope();

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public RootControl RootControl { get; private set; } = new();

        public IServiceProvider ServiceProvider => ServiceScope?.ServiceProvider;

        public KeyboardAccelerator KeyboardAccelerator => ServiceProvider?.GetService<IKeyboardAccelerator>() as KeyboardAccelerator;

        public WindowService WindowService => ServiceProvider?.GetService<IWindowService>() as WindowService;

        public CompositeDisposable disposables = new();

        public Timer checkActiveTimer;

        public MainWindow()
        {
            TrySetPrimaryLanguage();
            Title = Config.AppName;
            MinWidth = 480;
            MinHeight = 300;
            Width = 1130;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            DataContext = AppViewModel;
            Content = RootControl;
            Frame = false;
            Loaded += OnLoaded;
            StateChanged += OnStateChanged;
            IsActiveChanged += OnIsActiveChanged;
            LocationChanged += OnLocationChanged;
            SizeChanged += OnSizeChanged;
            Closing += OnClosing;
            Closed += OnClosed;
            InitializeBinding();
            checkActiveTimer = new(CheckActiveTimerCallback, null, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1));
            LastActive ??= this;
        }

        public void InitializeBinding()
        {
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme)).Cast<AppTheme>().StartWith(AppViewModel.SettingsViewModel.AppTheme).Subscribe(SetTheme));
            disposables.Add(AppViewModel.UIViewModel.WhenPropertyChanged(nameof(UIViewModel.MainWindowTitle)).Cast<string>().StartWith(AppViewModel.UIViewModel.MainWindowTitle).Subscribe(SetTitle));
            disposables.Add(AppViewModel.FileViewModel.NewWindowCommand.OnExecute.Subscribe(path => Utilities.Common.OpenNewWindow(new string[] { path }, forceNewWindow: true)));
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.UseMicaEffect)).Cast<bool>().StartWith(AppViewModel.SettingsViewModel.UseMicaEffect).Subscribe(EnableMicaEffect));
            disposables.Add(AppViewModel.SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Topmost)).Cast<bool>().StartWith(AppViewModel.SettingsViewModel.Topmost).Subscribe(SetTopmost));
            disposables.Add(AppViewModel.UIViewModel.WhenPropertyChanged(nameof(UIViewModel.IsFullScreen)).Cast<bool>().Subscribe(SetFullScreen));
        }

        private void SetTheme(AppTheme theme)
        {
            RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                AppTheme.Black => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        private void SetTitle(string title)
        {
            Title = title;
        }

        private PInvoke.WINDOWPLACEMENT? placementBeforeFullScreen;
        private int styleBeforeFullScreen;
        private FrameworkElement captionControlGroup;

        // Borderless full screen (upstream #11): drop the caption/thick frame styles and cover the monitor;
        // restore the saved style and placement on exit. The in-app caption/menu bar hides via RootControl/MainPage,
        // the XamlUI min/max/close buttons are collapsed here since they live outside our content.
        private void SetFullScreen(bool enable)
        {
            try
            {
                var hwnd = Handle;
                if (enable)
                {
                    if (placementBeforeFullScreen != null) return;
                    PInvoke.GetWindowPlacement(hwnd, out var placement);
                    placementBeforeFullScreen = placement;
                    styleBeforeFullScreen = PInvoke.GetWindowLong(hwnd, PInvoke.WindowLongFlags.GWL_STYLE);
                    // A maximized window keeps its work-area clamp and frame insets; leave that state first.
                    if (PInvoke.IsZoomed(hwnd))
                        PInvoke.ShowWindow(hwnd, PInvoke.ShowWindowCommand.Restore);
                    var rect = PInvoke.GetWindowMonitorRect(hwnd);
                    var style = styleBeforeFullScreen & ~(int)(PInvoke.WindowStyles.WS_CAPTION | PInvoke.WindowStyles.WS_THICKFRAME);
                    PInvoke.SetWindowLong(hwnd, PInvoke.WindowLongFlags.GWL_STYLE, style);
                    SetFullScreenDwmAttributes(hwnd, true);
                    var flags = PInvoke.SetWindowPosFlags.SWP_NOZORDER | PInvoke.SetWindowPosFlags.SWP_NOOWNERZORDER | PInvoke.SetWindowPosFlags.SWP_FRAMECHANGED;
                    PInvoke.SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, flags);
                    // XamlUI's WM_NCCALCSIZE keeps the resize-border insets on the left/right/bottom regardless of the
                    // style, so the client area ends up short of the monitor edges. Measure the shortfall and grow the
                    // window past the monitor by exactly that much (the same trick Windows uses for maximized windows).
                    var client = PInvoke.GetClientRectOnScreen(hwnd);
                    int dl = client.left - rect.left, dt = client.top - rect.top, dr = rect.right - client.right, db = rect.bottom - client.bottom;
                    if (dl != 0 || dt != 0 || dr != 0 || db != 0)
                    {
                        Log.Debug($"FullScreen: client inset l={dl} t={dt} r={dr} b={db}, compensating");
                        PInvoke.SetWindowPos(hwnd, IntPtr.Zero, rect.left - dl, rect.top - dt, rect.right - rect.left + dl + dr, rect.bottom - rect.top + dt + db, flags);
                    }
                    SetCaptionControlGroupVisible(false);
                }
                else
                {
                    if (placementBeforeFullScreen == null) return;
                    var placement = placementBeforeFullScreen.Value;
                    placementBeforeFullScreen = null;
                    SetCaptionControlGroupVisible(true);
                    PInvoke.SetWindowLong(hwnd, PInvoke.WindowLongFlags.GWL_STYLE, styleBeforeFullScreen);
                    SetFullScreenDwmAttributes(hwnd, false);
                    PInvoke.SetWindowPlacement(hwnd, ref placement);
                    PInvoke.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        PInvoke.SetWindowPosFlags.SWP_NOMOVE | PInvoke.SetWindowPosFlags.SWP_NOSIZE | PInvoke.SetWindowPosFlags.SWP_NOZORDER | PInvoke.SetWindowPosFlags.SWP_NOOWNERZORDER | PInvoke.SetWindowPosFlags.SWP_FRAMECHANGED);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLocal("FullScreen", ex.ToString());
            }
        }

        // Win11 draws rounded corners and a 1px border on every top-level window; both look wrong on a full-screen surface.
        // The attributes are unknown on Win10 and simply fail there.
        private static void SetFullScreenDwmAttributes(nint hwnd, bool enable)
        {
            var corner = enable ? PInvoke.DWMWCP_DONOTROUND : PInvoke.DWMWCP_DEFAULT;
            PInvoke.DwmSetWindowAttribute(hwnd, PInvoke.DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
            var border = enable ? PInvoke.DWMWA_COLOR_NONE : PInvoke.DWMWA_COLOR_DEFAULT;
            PInvoke.DwmSetWindowAttribute(hwnd, PInvoke.DwmWindowAttribute.DWMWA_BORDER_COLOR, ref border, sizeof(uint));
        }

        // XamlUI hosts its own min/max/close buttons above our Content (RootLayout template); find them once by type name.
        private void SetCaptionControlGroupVisible(bool visible)
        {
            captionControlGroup ??= FindCaptionControlGroup();
            if (captionControlGroup != null)
                captionControlGroup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private FrameworkElement FindCaptionControlGroup()
        {
            DependencyObject root = RootControl;
            if (root == null) return null;
            while (VisualTreeHelper.GetParent(root) is DependencyObject parent)
                root = parent;
            return FindDescendant(root, e => e.GetType().Name == "CaptionControlGroup");
        }

        private static FrameworkElement FindDescendant(DependencyObject node, Func<FrameworkElement, bool> predicate)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement fe && predicate(fe))
                    return fe;
                if (FindDescendant(child, predicate) is FrameworkElement found)
                    return found;
            }
            return null;
        }

        private void EnableMicaEffect(bool enable)
        {
            try
            {
                RootControl.Background = enable ? new SystemBackdropBrush(this) : new SolidColorBrush(Colors.Transparent);
            }
            catch (Exception ex)
            {
                // SystemBackdropBrush needs Win2D (Microsoft.Graphics.Canvas.dll); fall back rather than take the window down.
                Log.WriteLocal("MicaEffect", $"enable={enable} IsMicaSupported={Config.IsMicaSupported} build={Config.WindowsBuild} OS={Environment.OSVersion.VersionString}\n{ex}");
                RootControl.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            this.ShowWindowWithSavedPlacement();
            SaveWindowPlacementWithOffset(false);
            AppViewModel.MainWindow = Handle;
            // XAML registers its OLE drop target once the island is up; wrap it a moment later (retry once).
            _ = Dispatcher.RunIdleAsync(() => Utilities.FileDropTarget.Install(this));
            _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.RunAsync(() => Utilities.FileDropTarget.Install(this)));
        }

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            WindowService?.RaiseWindowStateChanged(Handle);
            if (RootControl?.IsLoaded ?? false)
                SaveWindowPlacementWithOffset();
        }

        /// <summary>The window that was most recently active; files opened from the shell land here as tabs.</summary>
        public static MainWindow LastActive { get; private set; }

        private void OnIsActiveChanged(object sender, IsActiveChangedEventArgs e)
        {
            WindowService?.RaiseWindowIsActivedChanged(Handle);
            KeyboardAccelerator.IsEnable = e.NewIsActive;
            if (e.NewIsActive) LastActive = this;
        }

        private void OnLocationChanged(object sender, LocationChangedEventArgs e)
        {
            if (RootControl?.IsLoaded ?? false)
                SaveWindowPlacementWithOffset();
        }

        private void OnSizeChanged(object sender, XamlUI.SizeChangedEventArgs e)
        {
            if (RootControl?.IsLoaded ?? false)
                SaveWindowPlacementWithOffset();
        }

        private bool isCloseable = false;

        private bool isClosing = false;

        private async void OnClosing(object sender, ClosingEventArgs e)
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
            finally
            {
                isClosing = false;
            }
        }

        public async void ForceClose()
        {
            this.TrySaveWindowPlacement();
            isCloseable = true;
            await Task.Yield();
            if (Handle != default)
                Close();
        }

        private void OnClosed(object sender, ClosedEventArgs e)
        {
            // A window that closes without the user asking for it is how "the app just vanished" happens, and
            // nothing else records it: note who asked.
            Log.Debug($"window closed\n{Environment.StackTrace}");
            if (LastActive == this) LastActive = null;
            var keepRun = AppViewModel.SettingsViewModel.KeepRun;
            checkActiveTimer?.Dispose();
            checkActiveTimer = null;
            ServiceScope?.Dispose();
            ServiceScope = null;
            RootControl = null;
            Content = null;
            DataContext = null;
            disposables.Dispose();
            if (!AppViewModel.GetInstances().Any())
            {
                if (keepRun)
                    Process.GetCurrentProcess().MaxWorkingSet = Process.GetCurrentProcess().MinWorkingSet;
                else
                {
                    Log.Debug("last window closed, exiting");
                    XamlApplication.Current.Exit();
                }
            }
        }

        private bool isPlacementSaving = false;

        private async void SaveWindowPlacementWithOffset(bool throttle = true)
        {
            if (!isPlacementSaving && !isCloseable && !isClosing && placementBeforeFullScreen == null)
            {
                isPlacementSaving = true;
                try
                {
                    if (throttle) await Task.Delay(100);
                    await Dispatcher.RunAsync(() => this.TrySaveWindowPlacement(new(8, 8)));
                }
                finally
                {
                    isPlacementSaving = false;
                }
            }
        }

        private void CheckActiveTimerCallback(object state)
        {
            _ = Dispatcher.RunIdleAsync(() =>
            {
                if (Handle != 0)
                {
                    var isActive = PInvoke.GetForegroundWindow() == Handle;
                    if (isActive != KeyboardAccelerator.IsEnable)
                        KeyboardAccelerator.IsEnable = isActive;
                }
            });
        }

        private void TrySetPrimaryLanguage()
        {
            try
            {
                var settingLanguage = AppViewModel.SettingsViewModel.Language;
                if (Locale.SupportedLangs.ContainsKey(settingLanguage))
                    ApplicationLanguages.PrimaryLanguageOverride = settingLanguage;
                else
                    ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
            }
            catch
            {

            }
        }
    }
}
