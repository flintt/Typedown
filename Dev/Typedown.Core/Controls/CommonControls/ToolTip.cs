using Typedown.Core.Utilities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public class ToolTip
    {
        public static DependencyProperty TextResourceProperty { get; } = DependencyProperty.Register("TextResource", typeof(string), typeof(ToolTip), new(null, OnResourcePropertyChanged));
        public static string GetTextResource(DependencyObject target) => (string)target.GetValue(TextResourceProperty);
        public static void SetTextResource(DependencyObject target, string value) => target.SetValue(TextResourceProperty, value);

        /// <summary>
        /// The settings' shortcut for what the element does (a SettingsViewModel property, "ShortcutStrong"): the tip says
        /// it after the text, "Bold (Ctrl+B)", as the menu bar's item does - read as the tip opens, so a shortcut changed
        /// in the settings is the one said (the context menu's format buttons named none: the user, 2026-11-13).
        /// </summary>
        public static DependencyProperty ShortcutProperty { get; } = DependencyProperty.Register("Shortcut", typeof(string), typeof(ToolTip), new(null, OnResourcePropertyChanged));
        public static string GetShortcut(DependencyObject target) => (string)target.GetValue(ShortcutProperty);
        public static void SetShortcut(DependencyObject target, string value) => target.SetValue(ShortcutProperty, value);

        private static void OnResourcePropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var resource = GetTextResource(target);
            if (string.IsNullOrEmpty(resource))
            {
                ToolTipService.SetToolTip(target, null);
                return;
            }
            if (string.IsNullOrEmpty(GetShortcut(target)))
            {
                ToolTipService.SetToolTip(target, Locale.GetString(resource));
                return;
            }
            var tip = new Microsoft.UI.Xaml.Controls.ToolTip { Content = Locale.GetString(resource) };
            tip.Opened += (_, _) => tip.Content = TextWithShortcut(target, resource);
            ToolTipService.SetToolTip(target, tip);
        }

        private static string TextWithShortcut(DependencyObject target, string resource)
        {
            var text = Locale.GetString(resource);
            if (target is not FrameworkElement element) return text;
            var settings = element.GetService<ViewModels.SettingsViewModel>();
            var key = settings?.GetType().GetProperty(GetShortcut(target))?.GetValue(settings) as Models.ShortcutKey;
            var shown = key == null ? null : element.GetService<Interfaces.IKeyboardAccelerator>()?.GetShortcutKeyText(key);
            return string.IsNullOrEmpty(shown) ? text : $"{text} ({shown})";
        }
    }
}
