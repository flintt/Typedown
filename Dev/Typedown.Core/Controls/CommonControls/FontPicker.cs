using System;
using System.Collections.Generic;
using System.Linq;
using Typedown.Core.Controls.SettingControls.SettingItems;
using Typedown.Core.Utilities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// The installed fonts to choose one from, each name shown in its own font with a sample in it (a Chinese font shows
    /// Chinese), a box above to narrow them down. What is chosen goes first in the field's value; the fonts after the first
    /// stay (FontFamilyList.WithFirst). Windows has no font dialog for this kind of app; the fields took a name typed in.
    /// </summary>
    public static class FontPicker
    {
        // What each font shows: Latin, Chinese, Japanese and digits, so a font that has only some of them looks it.
        private const string Sample = "Aa 永 あ 123";

        private static DataTemplate template;

        private static DataTemplate Template => template ??= (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<StackPanel Padding=\"0,4,0,4\"><TextBlock FontSize=\"12\" Opacity=\"0.75\"/><TextBlock FontSize=\"20\" TextTrimming=\"CharacterEllipsis\"/></StackPanel>" +
            "</DataTemplate>");

        /// <summary>A "Choose…" button that opens the list beside <paramref name="current"/>'s field.</summary>
        public static Button Button(Func<string> current, Action<string> chosen, string automationId)
        {
            var button = new Button { Content = Locale.GetString("FontPicker.Choose"), VerticalAlignment = VerticalAlignment.Bottom };
            AutomationProperties.SetAutomationId(button, automationId);
            button.Click += (_, _) => Show(button, current(), chosen);
            return button;
        }

        public static void Show(FrameworkElement anchor, string current, Action<string> chosen)
        {
            var fonts = EditorSetting.InstalledFontFamilies;
            var search = new TextBox { PlaceholderText = Locale.GetString("FontPicker.Search"), Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetAutomationId(search, "FontPicker.Search");
            var list = new ListView
            {
                Width = 340,
                Height = 380,
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.Single,
                ItemTemplate = Template,
                ItemsSource = fonts,
            };
            AutomationProperties.SetAutomationId(list, "FontPicker.List");
            // Filled as each row is shown (the list makes rows only for what is in view): a few hundred fonts each loaded
            // to draw its row at once was slow.
            list.ContainerContentChanging += (_, e) =>
            {
                if (e.InRecycleQueue || e.Item is not string name || e.ItemContainer.ContentTemplateRoot is not StackPanel row) return;
                ((TextBlock)row.Children[0]).Text = name;
                var sample = (TextBlock)row.Children[1];
                sample.Text = Sample;
                sample.FontFamily = new FontFamily(name);
                AutomationProperties.SetName(e.ItemContainer, name);
                AutomationProperties.SetAutomationId(e.ItemContainer, "FontPicker.Font:" + name);
            };
            var flyout = new Flyout { Content = new StackPanel { Children = { search, list } }, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
            search.TextChanged += (_, _) =>
            {
                var filter = search.Text.Trim();
                list.ItemsSource = filter.Length == 0 ? fonts : fonts.Where(n => n.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            };
            list.ItemClick += (_, e) =>
            {
                if (e.ClickedItem is not string name) return;
                flyout.Hide();
                chosen(FontFamilyList.WithFirst(current, name));
            };
            // The font the field has first is the one shown chosen, scrolled to.
            var first = FontFamilyList.First(current);
            var at = first == null ? -1 : fonts.ToList().FindIndex(n => string.Equals(n, first, StringComparison.OrdinalIgnoreCase));
            flyout.Opened += (_, _) =>
            {
                if (at >= 0)
                {
                    list.SelectedIndex = at;
                    list.ScrollIntoView(fonts[at], ScrollIntoViewAlignment.Leading);
                }
                search.Focus(FocusState.Programmatic);
            };
            flyout.ShowAt(anchor);
        }
    }
}
