using Windows.ApplicationModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class AboutApp : UserControl
    {
        public AboutApp()
        {
            InitializeComponent();
        }

        public static string GetAppVersion()
        {
            if (Config.IsPackaged)
            {
                var version = Package.Current.Id.Version;
                return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision) + " (" + Config.TestBuild + ")";
            }
            else
            {
                // The entry assembly is Typedown.exe, whose version comes from the release number in its csproj;
                // this code lives in Typedown.Core, whose own version is a separate number that nobody bumps.
                var version = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly()).GetName().Version;
                return string.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision) + " (" + Config.TestBuild + ", Unpackaged)";
            }
        }

        private void FeedBackButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // Feedback goes to the fork's GitHub issues, where it reaches the people who build this, rather
            // than the original author's server that the in-app dialog posted to.
            Utilities.Common.OpenUrl("https://github.com/flintt/Typedown/issues");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
