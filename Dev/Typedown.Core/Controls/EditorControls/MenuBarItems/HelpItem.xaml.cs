using System;
using Typedown.Core.Utilities;

namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    /// <summary>Help: the user guide (Chinese or English, as the interface) and the About page of the settings.</summary>
    public sealed partial class HelpItem : MenuBarItemBase
    {
        public HelpItem()
        {
            InitializeComponent();
        }

        protected override void OnRegisterShortcut()
        {
        }

        private async void OnUserGuideClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var path = UserGuide.Prepare(UserGuide.InterfaceLanguage());
                await ViewModel.FileViewModel.OpenFile(path);
            }
            catch (Exception ex)
            {
                Log.Debug($"help: the user guide could not be opened: {ex.Message}");
            }
        }

        private void OnAboutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.NavigateCommand.Execute("Settings/About");
        }
    }
}
