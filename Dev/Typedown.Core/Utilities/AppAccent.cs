using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The accent the standard controls draw with - switches, accent buttons (a dialog's default button among them),
    /// check boxes, the chosen item of a list, progress - in a custom theme's accent when it names one, else in the
    /// system's. WinUI takes these from Windows alone, so under any theme the settings and the dialogs stayed the
    /// system blue while the editor and the side pane wore the theme's colour.
    ///
    /// The brushes WinUI's resources hold for each theme are given the colour in place: every control, and every key
    /// another style points at them with (AccentButtonBackground, ToggleSwitchFillOn...), reaches the same brush, so
    /// one change reaches them all, already drawn or not. Set in place they no longer follow Windows by themselves, so
    /// the system colours are put back here, in the shades WinUI gives each theme, and again when Windows' accent changes.
    /// </summary>
    public static class AppAccent
    {
        private enum Shade { Fill, TextPrimary, TextSecondary, TextTertiary, OnAccentPrimary, OnAccentSecondary }

        private static readonly (string Key, Shade Shade)[] Keys =
        {
            ("AccentFillColorDefaultBrush", Shade.Fill),
            ("AccentFillColorSecondaryBrush", Shade.Fill),   // the brush's own opacity (0.9) makes it lighter
            ("AccentFillColorTertiaryBrush", Shade.Fill),    // 0.8
            ("AccentFillColorSelectedTextBackgroundBrush", Shade.Fill),
            ("AccentTextFillColorPrimaryBrush", Shade.TextPrimary),
            ("AccentTextFillColorSecondaryBrush", Shade.TextSecondary),
            ("AccentTextFillColorTertiaryBrush", Shade.TextTertiary),
            ("TextOnAccentFillColorPrimaryBrush", Shade.OnAccentPrimary),
            ("TextOnAccentFillColorSecondaryBrush", Shade.OnAccentSecondary),
        };

        private static readonly UISettings uiSettings = new();

        private static List<(bool Dark, string Key, Shade Shade, SolidColorBrush Brush)> brushes;

        /// <summary>The accent now given to the controls, null for the system's.</summary>
        public static Color? Current { get; private set; }

        /// <summary>How many of WinUI's accent brushes were found to colour (the test host reports it).</summary>
        public static int BrushCount => brushes?.Count ?? 0;

        private static bool watching;

        /// <summary>Gives the controls a theme's accent ("#bd93f9"), or the system's for null or one that cannot be read.</summary>
        public static void Apply(string accent)
        {
            if (!watching)
            {
                watching = true;
                // Raised off the UI thread; the colours are put back on the thread of the window that asked last.
                var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                uiSettings.ColorValuesChanged += (_, _) => queue?.TryEnqueue(() => { if (Current == null) Update(null); });
            }
            Update((ThemeFiles.Brush(accent) as SolidColorBrush)?.Color);
        }

        private static void Update(Color? custom)
        {
            Current = custom;
            brushes ??= Find();
            foreach (var (dark, _, shade, brush) in brushes)
                brush.Color = ColourOf(shade, dark, custom);
        }

        private static Color ColourOf(Shade shade, bool dark, Color? custom)
        {
            if (custom is { } c)
            {
                // Text on the accent: black or white, whichever reads on it (a yellow accent takes dark text).
                var light = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.55;
                return shade switch
                {
                    Shade.OnAccentPrimary => light ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),
                    Shade.OnAccentSecondary => light ? Color.FromArgb(0x80, 0, 0, 0) : Color.FromArgb(0xB3, 255, 255, 255),
                    _ => c,
                };
            }
            // WinUI's own: the system accent darker on a light theme, lighter on a dark one.
            UIColorType Type(UIColorType onLight, UIColorType onDark) => dark ? onDark : onLight;
            return shade switch
            {
                Shade.Fill => uiSettings.GetColorValue(Type(UIColorType.AccentDark1, UIColorType.AccentLight2)),
                Shade.TextPrimary => uiSettings.GetColorValue(Type(UIColorType.AccentDark2, UIColorType.AccentLight3)),
                Shade.TextSecondary => uiSettings.GetColorValue(Type(UIColorType.AccentDark3, UIColorType.AccentLight3)),
                Shade.TextTertiary => uiSettings.GetColorValue(Type(UIColorType.AccentDark1, UIColorType.AccentLight2)),
                Shade.OnAccentPrimary => dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255),
                _ => dark ? Color.FromArgb(0x80, 0, 0, 0) : Color.FromArgb(0xB3, 255, 255, 255),
            };
        }

        /// <summary>The accent brushes of the light and the dark theme ("Default" is WinUI's dark), wherever they are held.</summary>
        private static List<(bool, string, Shade, SolidColorBrush)> Find()
        {
            var found = new List<(bool, string, Shade, SolidColorBrush)>();
            var seen = new HashSet<SolidColorBrush>();
            var visited = new HashSet<ResourceDictionary>();
            void Walk(ResourceDictionary dictionary)
            {
                if (dictionary == null || !visited.Add(dictionary)) return;
                foreach (var theme in new[] { "Light", "Dark", "Default" })
                {
                    if (!dictionary.ThemeDictionaries.TryGetValue(theme, out var value) || value is not ResourceDictionary themed) continue;
                    foreach (var (key, shade) in Keys)
                        if (themed.TryGetValue(key, out var brush) && brush is SolidColorBrush solid && seen.Add(solid))
                            found.Add((theme != "Light", key, shade, solid));
                }
                foreach (var merged in dictionary.MergedDictionaries) Walk(merged);
            }
            Walk(Application.Current.Resources);
            Log.Debug($"accent brushes found: {found.Count}");
            return found;
        }
    }
}
