using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A system backdrop - Mica or the menus' acrylic - in the app's theme. WinUI 3's MicaBackdrop and DesktopAcrylicBackdrop
    /// take light or dark from Windows, not from the theme the app asks for: under the app's dark theme on a light Windows
    /// the window's Mica stayed light, the editor (transparent over it) light grey behind light text; and a menu of the
    /// app's own in a popup (Open Recent's entry menu) was the light acrylic under dark text (on hp88, 2026-11-12: the same
    /// mistake made again for want of one place for it). Any backdrop of the app's is this one: the controller's theme set
    /// from the app's, and its active state from the window's.
    /// </summary>
    public sealed class AppBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop
    {
        public enum Kind { Mica, Acrylic }

        private readonly Kind kind;
        private ISystemBackdropControllerWithTargets controller;
        private SystemBackdropConfiguration configuration;
        private bool dark;
        private bool active = true;

        public AppBackdrop(Kind kind, bool dark)
        {
            this.kind = kind;
            this.dark = dark;
        }

        /// <summary>The menus' acrylic in the theme of the element it is for (its window's content).</summary>
        public static AppBackdrop MenuFor(XamlRoot root) =>
            new(Kind.Acrylic, (root?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark);

        public static bool IsSupported(Kind kind) => kind == Kind.Mica ? MicaController.IsSupported() : DesktopAcrylicController.IsSupported();

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            configuration = new SystemBackdropConfiguration();
            Apply();
            controller = kind == Kind.Mica ? new MicaController() : new DesktopAcrylicController();
            var added = controller.AddSystemBackdropTarget(connectedTarget);
            controller.SetSystemBackdropConfiguration(configuration);
            Log.Debug($"backdrop: {kind} connected, target added={added}, supported={IsSupported(kind)}, theme={configuration.Theme}, active={configuration.IsInputActive}");
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            base.OnTargetDisconnected(disconnectedTarget);
            controller?.RemoveSystemBackdropTarget(disconnectedTarget);
            (controller as IDisposable)?.Dispose();
            controller = null;
            configuration = null;
        }

        /// <summary>
        /// The window's default configuration (system theme, activation) changed. The base implementation throws
        /// E_INVALIDARG for a backdrop that drives its own controller (seen on Windows 11 26100 at every theme or
        /// activation change), and this backdrop takes both from the app itself, so there is nothing to do here.
        /// </summary>
        protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
        }

        /// <summary>The app's theme changed (the setting, or Windows when the app follows it).</summary>
        public void SetDark(bool value)
        {
            dark = value;
            Apply();
        }

        /// <summary>The window became active or inactive: Mica dims while it is not, as for any window.</summary>
        public void SetActive(bool value)
        {
            active = value;
            Apply();
        }

        private void Apply()
        {
            if (configuration == null) return;
            configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            configuration.IsInputActive = active;
        }
    }
}
