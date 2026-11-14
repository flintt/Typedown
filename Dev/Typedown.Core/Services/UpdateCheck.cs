using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Windows.ApplicationModel;

namespace Typedown.Core.Services
{
    /// <summary>A release the feed names: its version, the page to download it from, its title.</summary>
    public sealed record UpdateInfo(Version Latest, Version Current, string PageUrl, string Title)
    {
        public bool IsNewer => Latest > Current;
    }

    /// <summary>
    /// Settings > About > Check for updates, and the daily check when Settings has it on: the edition's release feed
    /// (Typedown.Automation.Brand.UpdateFeedUrl - GitHub's latest release, {"tag_name": "v1.4.2", "html_url": ...}, or a
    /// file of the same shape) against this build's version. Nothing is downloaded or installed: a newer release is said,
    /// with its page. Not where there is no feed (an edition without one) or the Microsoft Store updates the app.
    /// </summary>
    public static class UpdateCheck
    {
        /// <summary>
        /// The test host's feed instead of the edition's (test.updates.feed): the feed's JSON itself, so a test needs no
        /// network and an edition without a feed can be tested too.
        /// </summary>
        public static string FeedJsonOverride { get; set; }

        /// <summary>The check is offered: a feed, and not the Store's package (the Store updates it).</summary>
        public static bool Available => FeedJsonOverride != null || (!string.IsNullOrEmpty(Typedown.Automation.Brand.UpdateFeedUrl) && !FromStore);

        private static bool FromStore
        {
            get
            {
                try { return Config.IsPackaged && Package.Current.SignatureKind == PackageSignatureKind.Store; }
                catch { return false; }
            }
        }

        /// <summary>This build's version, its four numbers (the About page's text has the build kind after them).</summary>
        public static Version CurrentVersion
        {
            get
            {
                var m = Regex.Match(Controls.AboutApp.GetAppVersion() ?? "", @"^\d+(\.\d+){1,3}");
                return m.Success && Version.TryParse(m.Value, out var v) ? v : new Version(0, 0);
            }
        }

        private static readonly HttpClient client = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // GitHub's API refuses a request without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Typedown.Automation.Brand.Name + "-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>The feed's latest release against this build; throws when the feed cannot be had or read.</summary>
        public static async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
        {
            var json = FeedJsonOverride ?? await client.GetStringAsync(Typedown.Automation.Brand.UpdateFeedUrl, cancellationToken);
            var feed = JObject.Parse(json);
            var tag = (string)feed["tag_name"] ?? (string)feed["version"] ?? "";
            var m = Regex.Match(tag, @"\d+(\.\d+){1,3}");
            if (!m.Success || !Version.TryParse(m.Value, out var latest))
                throw new FormatException($"no version in the feed's \"{tag}\"");
            var page = (string)feed["html_url"] ?? (string)feed["url"] ?? Typedown.Automation.Brand.AboutLinkUrl;
            var info = new UpdateInfo(Pad(latest), Pad(CurrentVersion), page, (string)feed["name"] ?? tag);
            Log.Debug($"updates: the feed has {info.Latest}, this build is {info.Current}: {(info.IsNewer ? "newer there" : "up to date")}");
            return info;
        }

        // 1.4.2 and 1.4.2.0 are one version.
        private static Version Pad(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

        private static int automaticStarted;

        /// <summary>
        /// The daily check (Settings > About, off unless chosen): once a process, a while after the start, not again within
        /// a day of the last; a newer release says so in a card at the window's bottom right (the app's notices' place).
        /// </summary>
        public static async Task CheckAutomaticallyAsync(AppViewModel app, bool now = false)
        {
            if (app == null || !Available) return;
            var settings = app.SettingsViewModel;
            if (!settings.CheckForUpdatesAutomatically) return;
            if (!now)
            {
                if (Interlocked.Exchange(ref automaticStarted, 1) == 1) return;
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
            if (DateTimeOffset.TryParse(settings.LastUpdateCheck, out var last) && DateTimeOffset.Now - last < TimeSpan.FromHours(20) && !now)
            {
                Log.Debug($"updates: checked {last:u}; not again today");
                return;
            }
            UpdateInfo info;
            try { info = await CheckAsync(); }
            catch (Exception ex) { Log.Debug($"updates: the daily check failed: {ex.Message}"); return; }
            settings.LastUpdateCheck = DateTimeOffset.Now.ToString("o");
            if (info.IsNewer) ShowCard(app, info);
        }

        public const string CardId = "update";

        private static void ShowCard(AppViewModel app, UpdateInfo info)
        {
            var text = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = string.Format(Locale.GetString("UpdateCard.Available"), info.Latest.ToString(3), info.Current.ToString(3)),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MaxWidth = 280,
            };
            var open = new Microsoft.UI.Xaml.Controls.Button { Content = Locale.GetString("UpdateCard.Download") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open, "UpdateCardDownload");
            open.Click += (_, _) => { Common.OpenUrl(info.PageUrl); EditionHooks.CloseCard(app, CardId); };
            var later = new Microsoft.UI.Xaml.Controls.Button { Content = Locale.GetString("UpdateCard.Later") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(later, "UpdateCardLater");
            later.Click += (_, _) => EditionHooks.CloseCard(app, CardId);
            var buttons = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(open);
            buttons.Children.Add(later);
            var content = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 10 };
            content.Children.Add(text);
            content.Children.Add(buttons);
            EditionHooks.ShowCard(app, new EditorBar { Id = CardId, Content = content });
        }
    }
}
