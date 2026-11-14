using Typedown.Core.Utilities;
using Windows.ApplicationModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class AboutApp : UserControl
    {
        public AboutApp()
        {
            InitializeComponent();
        }

        // From Typedown.Automation.Brand: the edition's own link and feedback place.
        public static string LinkText => Typedown.Automation.Brand.AboutLinkText;
        public static System.Uri LinkUrl => new(Typedown.Automation.Brand.AboutLinkUrl);

        public static string GetAppVersion()
        {
            string version;
            if (Config.IsPackaged)
            {
                var packageVersion = Package.Current.Id.Version;
                version = string.Format("{0}.{1}.{2}.{3}", packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
                return string.IsNullOrWhiteSpace(Config.TestBuild) ? version : version + " (" + Config.TestBuild + ")";
            }

            // The entry assembly is Typedown.exe, whose version comes from the release number in its csproj;
            // use it here so an unpackaged build reports the application version rather than the library version.
            var assemblyVersion = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Version;
            version = string.Format("{0}.{1}.{2}.{3}", assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build, assemblyVersion.Revision);
            var buildKind = string.IsNullOrWhiteSpace(Config.TestBuild) ? "Unpackaged" : Config.TestBuild + ", Unpackaged";
            return version + " (" + buildKind + ")";
        }

        /// <summary>
        /// The ways to give feedback an edition offers instead of the link (set from Edition.Initialize): Send feedback
        /// opens them as a menu. Typedown leaves it null and the button opens its GitHub issues.
        /// </summary>
        public static System.Func<ViewModels.AppViewModel, System.Collections.Generic.IEnumerable<MenuFlyoutItemBase>> FeedbackItems { get; set; }

        private void FeedBackButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var items = FeedbackItems?.Invoke(this.GetService<ViewModels.AppViewModel>());
                if (items != null)
                {
                    var menu = new MenuFlyout();
                    foreach (var item in items) menu.Items.Add(item);
                    if (menu.Items.Count > 0)
                    {
                        menu.ShowAt(FeedBackButton);
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Utilities.Log.WriteLocal("AboutFeedback", ex.ToString());
            }
            // Feedback goes to the edition's GitHub issues, where it reaches the people who build it, rather
            // than the original author's server that the in-app dialog posted to.
            Utilities.Common.OpenUrl(Typedown.Automation.Brand.FeedbackUrl);
        }

        private string updatePage;

        private void ShowUpdatePanel()
        {
            UpdatePanel.Visibility = Services.UpdateCheck.Available ? Visibility.Visible : Visibility.Collapsed;
            var settings = this.GetService<ViewModels.SettingsViewModel>();
            if (settings != null) AutoUpdateCheck.IsChecked = settings.CheckForUpdatesAutomatically;
        }

        private void AutoUpdateCheck_Changed(object sender, RoutedEventArgs e)
        {
            var settings = this.GetService<ViewModels.SettingsViewModel>();
            if (settings != null && settings.CheckForUpdatesAutomatically != (AutoUpdateCheck.IsChecked == true))
                settings.CheckForUpdatesAutomatically = AutoUpdateCheck.IsChecked == true;
        }

        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdatesButton.IsEnabled = false;
            UpdatePageLink.Visibility = Visibility.Collapsed;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.IsActive = true;
            UpdateStatus.Text = Locale.GetString("About.CheckingForUpdates");
            try
            {
                var info = await Services.UpdateCheck.CheckAsync();
                updatePage = info.PageUrl;
                if (info.IsNewer)
                {
                    UpdateStatus.Text = string.Format(Locale.GetString("About.UpdateFound"), info.Latest.ToString(3));
                    UpdatePageLink.Visibility = Visibility.Visible;
                }
                else UpdateStatus.Text = string.Format(Locale.GetString("About.UpToDate"), info.Current.ToString(3));
            }
            catch (System.Exception ex)
            {
                Utilities.Log.Debug($"updates: the check asked for failed: {ex.Message}");
                UpdateStatus.Text = string.Format(Locale.GetString("About.UpdateCheckFailed"), ex.Message.Split('\n')[0]);
            }
            finally
            {
                UpdateProgress.IsActive = false;
                UpdateProgress.Visibility = Visibility.Collapsed;
                CheckForUpdatesButton.IsEnabled = true;
            }
        }

        private void UpdatePageLink_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(updatePage)) Utilities.Common.OpenUrl(updatePage);
        }

        private void OpenLogFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Utilities.Log.LogFolder);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{Utilities.Log.LogFolder}\"");
            }
            catch (System.Exception ex)
            {
                Utilities.Log.Debug($"about: could not open the log folder: {ex.Message}");
            }
        }

        private async void OpenSourceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "ThirdPartyNotices.txt");
                await ShowTextAsync(XamlRoot, Locale.GetString("OpenSourceSoftware"), System.IO.File.ReadAllText(path));
            }
            catch (System.Exception ex)
            {
                Utilities.Log.WriteLocal("AboutOpenSource", ex.ToString());
            }
        }

        /// <summary>
        /// A long text (the licences) in a dialog, to read and copy from: a read-only box that scrolls, as wide as the
        /// window allows; in a fixed-width font unless <paramref name="monospace"/> is false (prose, not a laid-out file).
        /// </summary>
        public static async System.Threading.Tasks.Task ShowTextAsync(XamlRoot root, string title, string text, bool monospace = true)
        {
            var box = new TextBox
            {
                // AcceptsReturn before the text: a box that does not take returns cuts what it is given at the first line
                // break (the terms showed their title alone, FB01 on w10).
                AcceptsReturn = true,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = monospace ? 12 : 14,
                Width = System.Math.Min(640, root.Size.Width - 120),
                Height = System.Math.Max(200, System.Math.Min(520, root.Size.Height - 220)),
            };
            box.Text = text;
            if (monospace) box.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
            ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            await AppContentDialog.Create(title, box, Locale.GetDialogString("Ok")).ShowAsync(root);
        }

        /// <summary>
        /// A section of its own that an edition adds below the rest (set from Edition.Initialize); called each time the
        /// page is shown. Typedown leaves it null and the page is as it always was.
        /// </summary>
        public static System.Func<UIElement> EditionSection { get; set; }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ShowUpdatePanel();
            try
            {
                EditionSectionPresenter.Content = EditionSection?.Invoke();
            }
            catch (System.Exception ex)
            {
                Utilities.Log.WriteLocal("AboutEditionSection", ex.ToString());
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            EditionSectionPresenter.Content = null;
            Bindings?.StopTracking();
        }
    }
}
