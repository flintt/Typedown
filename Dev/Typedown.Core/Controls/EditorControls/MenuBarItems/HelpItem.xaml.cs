using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Typedown.Core.Utilities;

namespace Typedown.Core.Controls.EditorControls.MenuBarItems
{
    /// <summary>
    /// Help: the user guide (Chinese or English, as the interface), the shortcut keys, the ways to give feedback and the
    /// About page of the settings. An edition with ways of its own (AboutApp.FeedbackItems: rate in the Store, report by
    /// mail) has them here in place of Send feedback, each an item; without, Send feedback opens the edition's issues.
    /// </summary>
    public sealed partial class HelpItem : MenuBarItemBase
    {
        private bool feedbackFilled;

        public HelpItem()
        {
            InitializeComponent();
        }

        protected override void OnRegisterShortcut()
        {
            if (feedbackFilled) return;
            feedbackFilled = true;
            var items = AboutApp.FeedbackItems?.Invoke(ViewModel)?.ToList();
            if (items == null || items.Count == 0) return;
            var at = Items.IndexOf(FeedbackItem);
            Items.RemoveAt(at);
            foreach (var item in items)
            {
                // The rest of the menu has no icons; the About page's menu of the same items does.
                if (item is MenuFlyoutItem plain) plain.Icon = null;
                Items.Insert(at++, item);
            }
            // Made after the menu took the interface's language (MenuBarItemBase): given it as the rest were.
            UiLanguage.SetItems(items);
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

        private void OnShortcutsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.NavigateCommand.Execute("Settings/Shortcut");
        }

        private void OnFeedbackClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Common.OpenUrl(Typedown.Automation.Brand.FeedbackUrl);
        }

        private void OnAboutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.NavigateCommand.Execute("Settings/About");
        }
    }
}
