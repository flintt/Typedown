namespace Typedown.Core.Utilities
{
    /// <summary>What the start-up needs to know of the WebView2 runtime the editor page runs in, apart from the runtime itself.</summary>
    public static class WebView2Runtime
    {
        /// <summary>
        /// Microsoft's download page for the runtime: the online setup and the offline (standalone) installers for each
        /// processor. Offered when the setup the app carries could not install it - most often a machine without the
        /// internet, which only the standalone installer serves.
        /// </summary>
        public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download";

        /// <summary>The version the loader reports means a runtime there: older loaders answer null instead of throwing.</summary>
        public static bool IsInstalledVersion(string version) => !string.IsNullOrWhiteSpace(version);
    }
}
