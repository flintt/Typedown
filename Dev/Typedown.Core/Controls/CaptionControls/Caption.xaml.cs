using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class Caption : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        private readonly CompositeDisposable disposables = new();

        public Caption()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            disposables.Add(ViewModel.GoBackCommand
                .WhenPropertyChanged(nameof(ViewModel.GoBackCommand.IsExecutable))
                .Cast<bool>()
                .Subscribe(x => UpdateBackButtonState(x)));
            UpdateBackButtonState(ViewModel.GoBackCommand.IsExecutable, false);
            // In compact mode the main page has no caption: its menu bar is the top row, 40 high. The settings' caption is
            // as high, so leaving or entering the settings moves nothing below it (32 high, the page jumped by 8 px).
            // It looks like that row too: the title in the middle, no icon - not a caption the main page does not have.
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.AppCompactMode)).Subscribe(_ => ApplyCompact()));
            ApplyCompact();
        }

        private void UpdateBackButtonState(bool canGoBack, bool useTransitions = true)
        {
            if (IsLoaded)
                VisualStateManager.GoToState(this, canGoBack ? "BackVisible" : "BackCollapsed", useTransitions && Settings.AnimationEnable);
        }

        private void ApplyCompact()
        {
            var compact = Settings.AppCompactMode;
            CaptionWrapper.Height = compact ? 40 : 32;
            Logo.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TitleText.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }

        /// <summary>
        /// The row in the custom theme's colours, those of the page under it; without a theme (null) it is clear again,
        /// over the window's own colour. The caption buttons are drawn by the system on a clear background, so they
        /// sit in that colour too.
        /// </summary>
        public void ApplyThemeBrushes(Microsoft.UI.Xaml.Media.Brush background, Microsoft.UI.Xaml.Media.Brush foreground)
        {
            if (background != null) CaptionWrapper.Background = background;
            else CaptionWrapper.ClearValue(Panel.BackgroundProperty);
            if (foreground != null) Foreground = foreground;
            else ClearValue(ForegroundProperty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
