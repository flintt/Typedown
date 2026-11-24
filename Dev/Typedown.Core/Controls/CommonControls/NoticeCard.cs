using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// A notice drawn as a card (UIViewModel.Notices, MainPage's stack): its text (or an edition's content) with the links
    /// under it, its buttons beside, and its close button - the automation ids the cards each had: the card's, its text
    /// (+ "Text"), its close button (+ "Close"), each button's own.
    /// </summary>
    public static class NoticeCard
    {
        /// <param name="hide">Takes the notice away (a button pressed, the close button).</param>
        public static Border Make(Notice notice, Style style, Action<Notice> hide)
        {
            var card = new Border { Style = style };
            if (!string.IsNullOrEmpty(notice.AutomationId)) AutomationProperties.SetAutomationId(card, notice.AutomationId);
            if (notice.Content != null)
            {
                // An edition's card: its content and its own buttons, in the frame (a page made again takes it from the
                // one before - it can have but one parent).
                if (notice.Content is FrameworkElement element && element.Parent is ContentPresenter before) before.Content = null;
                card.Padding = new Thickness(16, 10, 8, 12);
                card.Child = new ContentPresenter { Content = notice.Content };
                return card;
            }
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var main = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
            var text = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(notice.AutomationId)) AutomationProperties.SetAutomationId(text, notice.AutomationId + "Text");
            main.Children.Add(text);
            grid.Children.Add(main);
            var column = 1;
            foreach (var action in notice.Actions)
            {
                if (action.Link)
                {
                    var link = new HyperlinkButton { Padding = new Thickness(0), Content = action.Text };
                    if (action.AutomationId != null) AutomationProperties.SetAutomationId(link, action.AutomationId);
                    link.Click += (_, _) => Run(action);
                    main.Children.Add(link);
                    continue;
                }
                var button = new Button { Content = action.Text, VerticalAlignment = VerticalAlignment.Center };
                if (action.AutomationId != null) AutomationProperties.SetAutomationId(button, action.AutomationId);
                // A button does its thing and the card goes (as the export's Open and Open file location did).
                button.Click += (_, _) => { hide(notice); Run(action); };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(button, column++);
                grid.Children.Add(button);
            }
            if (notice.Closable)
            {
                var close = new Button { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Content = new FontIcon { Glyph = "", FontSize = 12 } };
                if (!string.IsNullOrEmpty(notice.AutomationId)) AutomationProperties.SetAutomationId(close, notice.AutomationId + "Close");
                AutomationProperties.SetName(close, Locale.GetString("Close"));
                close.Click += (_, _) => hide(notice);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(close, column);
                grid.Children.Add(close);
            }
            card.Child = grid;
            return card;
        }

        /// <summary>
        /// A custom theme's colours on a card, as the dialogs take them (its panels', its text, its separators); null each:
        /// the card's own (light or dark). The cards went by light and dark only, under Sepia or Dracula too (the user,
        /// 2026-11-24). Its words take the text colour; buttons keep theirs, as in the dialogs.
        /// </summary>
        public static void ApplyColours(Border card, Microsoft.UI.Xaml.Media.Brush surface, Microsoft.UI.Xaml.Media.Brush foreground, Microsoft.UI.Xaml.Media.Brush border)
        {
            if (surface != null) card.Background = surface; else card.ClearValue(Border.BackgroundProperty);
            if (border != null) card.BorderBrush = border; else card.ClearValue(Border.BorderBrushProperty);
            foreach (var text in Words(card))
            {
                if (foreground != null) text.SetValue(text is TextBlock ? TextBlock.ForegroundProperty : IconElement.ForegroundProperty, foreground);
                else text.ClearValue(text is TextBlock ? TextBlock.ForegroundProperty : IconElement.ForegroundProperty);
            }
        }

        // The card's words and icons, not inside its buttons (they keep their own colours) - the close button's cross too.
        private static System.Collections.Generic.IEnumerable<FrameworkElement> Words(DependencyObject root)
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            if (count == 0 && root is Border b && b.Child != null) { foreach (var x in Words(b.Child)) yield return x; yield break; }
            if (root is Panel panel && count == 0) { foreach (var child in panel.Children) foreach (var x in Words(child)) yield return x; yield break; }
            if (root is ContentPresenter presenter && count == 0 && presenter.Content is DependencyObject content) { foreach (var x in Words(content)) yield return x; yield break; }
            for (var i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock or IconElement) yield return (FrameworkElement)child;
                else if (child is ButtonBase button && button.Content is IconElement icon && button.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }) yield return icon;
                else if (child is not ButtonBase) foreach (var x in Words(child)) yield return x;
            }
        }

        public static void Run(NoticeAction action)
        {
            try { action.Run?.Invoke(); }
            catch (Exception ex) { Log.WriteLocal("Notice", $"{action.AutomationId}: {ex}"); }
        }
    }
}
