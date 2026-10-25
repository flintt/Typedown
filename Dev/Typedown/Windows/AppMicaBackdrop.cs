using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

namespace Typedown.Windows
{
    /// <summary>
    /// Mica in the app's theme. WinUI 3's MicaBackdrop takes light or dark from the system, not from the theme the window
    /// asks for: under Typedown's dark theme on a light Windows the Mica stayed light, and the editor - transparent over
    /// it - showed light grey behind light text. XamlUI's backdrop was tinted from the app's theme; this sets the
    /// controller's theme from the window content's actual theme, and its active state from the window's.
    /// </summary>
    public sealed class AppMicaBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop
    {
        private MicaController controller;
        private SystemBackdropConfiguration configuration;
        private bool dark;
        private bool active = true;

        public AppMicaBackdrop(bool dark) => this.dark = dark;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            configuration = new SystemBackdropConfiguration();
            Apply();
            controller = new MicaController();
            var added = controller.AddSystemBackdropTarget(connectedTarget);
            controller.SetSystemBackdropConfiguration(configuration);
            Typedown.Core.Utilities.Log.Debug($"mica: connected, target added={added}, supported={MicaController.IsSupported()}, theme={configuration.Theme}, active={configuration.IsInputActive}");
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            base.OnTargetDisconnected(disconnectedTarget);
            controller?.RemoveSystemBackdropTarget(disconnectedTarget);
            controller?.Dispose();
            controller = null;
            configuration = null;
        }

        /// <summary>
        /// The window's default configuration (system theme, activation) changed. The base implementation throws
        /// E_INVALIDARG for a backdrop that drives its own controller (seen on Windows 11 26100 at every theme or
        /// activation change), and this backdrop takes both from the window itself, so there is nothing to do here.
        /// </summary>
        protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
        }

        /// <summary>The content's theme changed (the setting, or Windows when the app follows it).</summary>
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
