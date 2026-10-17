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
            disposables.Add(Settings.WhenPropertyChanged(nameof(Settings.AppCompactMode))
                .Subscribe(_ => CaptionWrapper.Height = Settings.AppCompactMode ? 40 : 32));
            CaptionWrapper.Height = Settings.AppCompactMode ? 40 : 32;
        }

        private void UpdateBackButtonState(bool canGoBack, bool useTransitions = true)
        {
            if (IsLoaded)
                VisualStateManager.GoToState(this, canGoBack ? "BackVisible" : "BackCollapsed", useTransitions && Settings.AnimationEnable);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
