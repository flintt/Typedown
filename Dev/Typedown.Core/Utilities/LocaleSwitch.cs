using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The words a switch shows beside it, in the interface's language. Without its own, a ToggleSwitch shows WinUI's
    /// "On"/"Off", which follow the system's language: "开" in an English window on a Chinese Windows. The switch style
    /// (Resources/Styles/ToggleSwitch.xaml) sets this on every switch; a switch with words of its own keeps them.
    /// </summary>
    public static class LocaleSwitch
    {
        public static readonly DependencyProperty WordsProperty = DependencyProperty.RegisterAttached(
            "Words", typeof(bool), typeof(LocaleSwitch), new PropertyMetadata(false, OnWordsChanged));

        public static bool GetWords(DependencyObject element) => (bool)element.GetValue(WordsProperty);

        public static void SetWords(DependencyObject element, bool value) => element.SetValue(WordsProperty, value);

        private static void OnWordsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ToggleSwitch toggle || e.NewValue is not true) return;
            Apply(toggle);
            // Again each time it is shown: the language may have changed since it was made.
            toggle.Loaded += (_, _) => Apply(toggle);
        }

        private static void Apply(ToggleSwitch toggle)
        {
            if (toggle.OnContent is null or string) toggle.OnContent = Locale.GetString("On");
            if (toggle.OffContent is null or string) toggle.OffContent = Locale.GetString("Off");
        }
    }
}
