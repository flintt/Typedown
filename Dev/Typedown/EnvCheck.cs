using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Typedown.Core.Utilities;
using Typedown.Services.Automation;
using Typedown.Windows;

namespace Typedown
{
    public static class EnvCheck
    {
        public static bool IsWebView2Installed()
        {
            if (TestHostHooks.PretendWebView2Missing) return false;
            try
            {
                return WebView2Runtime.IsInstalledVersion(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> EnsureWebView2Installed()
        {
            if (IsWebView2Installed())
            {
                return true;
            }
            Log.Debug("startup: the WebView2 runtime is not installed, offering to install it");
            return await WebViewInstallWindow.TryInstallWebView2();
        }
    }
}
