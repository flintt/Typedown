using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class WebView2InstallControl : UserControl
    {
        public EventHandler CloseButtonClick;

        public EventHandler InstallButtonClick;

        public WebView2InstallControl()
        {
            this.InitializeComponent();
        }

        /// <summary>Whether the window says the setup ran and did not install the runtime.</summary>
        public bool IsInstallFailedShown
        {
            get => InstallFailedText.Visibility == Visibility.Visible;
            set => InstallFailedText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            CloseButtonClick?.Invoke(this, EventArgs.Empty);
        }

        private void OnInstallButtonClick(object sender, RoutedEventArgs e)
        {
            InstallButtonClick?.Invoke(this, EventArgs.Empty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
