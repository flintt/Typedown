using System;
using System.Reactive.Disposables;
using Typedown.Core.Controls.EditorControls.MenuBarItems;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class MenuBar : UserControl
    {
        /// <summary>
        /// Paints this bar from a custom theme. Null puts the colours of the built-in theme back; the inner
        /// grid carries the visible background, so setting the control's own is not enough.
        /// </summary>
        /// <summary>
        /// Turns the compact menu bar's window-drag areas on or off. Full screen has no window to drag, and the tabs move
        /// up into the bar's place there: an area left on would take their clicks as title-bar drags.
        /// </summary>
        public void SetDragEnabled(bool enabled)
        {
            foreach (var bar in new FrameworkElement[] { LeftDragBar, RightDragBar })
                if (bar != null && WindowChrome.GetDrag(bar) != enabled)
                    WindowChrome.SetDrag(bar, enabled);
        }

        public void ApplyThemeBrushes(Microsoft.UI.Xaml.Media.Brush background, Microsoft.UI.Xaml.Media.Brush foreground)
        {
            if (RootGrid != null)
            {
                if (background != null) RootGrid.Background = background;
                else RootGrid.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
            }
            if (foreground != null) Foreground = foreground;
            else ClearValue(ForegroundProperty);
        }

        /// <summary>
        /// The line under the menu row. It separates the menu from the tabs when both have the same background (the
        /// built-in themes); when a custom theme paints the menu row with its own panel colour, the change of colour
        /// already does that and the line only makes the header heavier.
        /// </summary>
        public void SetDivider(bool visible)
        {
            if (RootGrid != null) RootGrid.BorderThickness = visible ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        }

        public AppViewModel ViewModel => DataContext as AppViewModel;
        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        private readonly CompositeDisposable disposables = new();

        public MenuBar()
        {
            InitializeComponent();
        }

        private void OnSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (TitleGrid != null)
            {
                if (ActualWidth / 2 > MenuBarControl.ActualWidth + TitleTextBlock.ActualWidth / 2 + 16)
                {
                    TitleGrid.Margin = new(0);
                    Grid.SetColumn(TitleGrid, 0);
                    Grid.SetColumnSpan(TitleGrid, 3);
                }
                else
                {
                    TitleGrid.Margin = new(0, 0, 46 * 3, 0);
                    Grid.SetColumn(TitleGrid, 1);
                    Grid.SetColumnSpan(TitleGrid, 2);
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Settings.AppCompactMode)
            {
                var uiViewModel = ViewModel.UIViewModel;
                var oldCaptionHeight = uiViewModel.CaptionHeight;
                uiViewModel.CaptionHeight = 40;
                disposables.Add(Disposable.Create(() => uiViewModel.CaptionHeight = oldCaptionHeight));
            }
            // A language changed while this window shows its document (through the automation API, or another window's
            // settings) passes no page that would build the menus again, and they kept the old language while the side
            // pane and the status bar followed. Their titles and items are read from {u:LocaleString} when they are
            // created, so the menus - and only they - are created again. The main page used to be built again instead,
            // which reloaded the editor page: a read or a write right then could fail, and two pages talking to the host
            // at once could end the process.
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.Language)).Subscribe(_ =>
                _ = DispatcherQueue.TryRunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, RebuildMenus)));
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.SourceCode)).Subscribe(_ => ApplyModeToMenus()));
        }

        private void RebuildMenus()
        {
            try
            {
                var items = MenuBarControl.Items;
                items.Clear();
                items.Add(new FileItem());
                items.Add(new EditItem());
                items.Add(new ParagraphItem());
                items.Add(new FormatItem());
                items.Add(new ViewItem());
                ApplyModeToMenus();
            }
            catch (Exception ex)
            {
                Log.Debug($"menus after a language change: {ex.Message}");
            }
        }

        // Paragraph and Format have nothing to do in source mode (the markup binds this for the menus it creates).
        private void ApplyModeToMenus()
        {
            if (Settings == null) return;
            var visibility = IsCollapsed(Settings.SourceCode);
            foreach (var item in MenuBarControl.Items)
                if (item is ParagraphItem || item is FormatItem) item.Visibility = visibility;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Dispose();
            Bindings?.StopTracking();
        }

        private Visibility IsCollapsed(bool boolean) => boolean ? Visibility.Collapsed : Visibility.Visible;

        private DateTime prevLeftButtonPressedTime = DateTime.Now;

        private void OnMenuBarPointerEvent(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var window = WindowChrome.WindowHandleOf(this);
            if (Settings.AppCompactMode && e.OriginalSource is Grid && window != IntPtr.Zero)
            {
                _ = DispatcherQueue.RunIdleAsync(() =>
                {
                    PInvoke.GetCursorPos(out var point);
                    var packedPoint = (point.Y << 16) + point.X;
                    var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
                    if (kind == PointerUpdateKind.LeftButtonPressed)
                    {
                        if ((DateTime.Now - prevLeftButtonPressedTime).TotalMilliseconds < PInvoke.GetDoubleClickTime())
                            PInvoke.PostMessage(window, (uint)PInvoke.WindowMessage.WM_NCLBUTTONDBLCLK, (nint)(uint)PInvoke.HitTestFlags.CAPTION, packedPoint);
                        else
                            PInvoke.PostMessage(window, (uint)PInvoke.WindowMessage.WM_NCLBUTTONDOWN, (nint)(uint)PInvoke.HitTestFlags.CAPTION, packedPoint);
                        prevLeftButtonPressedTime = DateTime.Now;
                    }
                    if (kind == PointerUpdateKind.RightButtonReleased)
                    {
                        PInvoke.PostMessage(window, (uint)PInvoke.WindowMessage.WM_NCRBUTTONUP, (nint)(uint)PInvoke.HitTestFlags.CAPTION, packedPoint);
                    }
                });
            }
        }
    }
}
