using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// Puts the interface's language tag (<see cref="Locale.ElementLanguage"/>) on what shows the interface's text, so
    /// WinUI draws Chinese, Japanese and Korean in that language's fallback font. Nothing set it before: an element took
    /// a default fixed when it - or the popup window it is shown in, which WinUI keeps and shows again - was made. After
    /// Chinese -> English -> Chinese, a menu opened while the interface was English came back in the English fallback
    /// font (a thinner Chinese, other glyph shapes) until the app was restarted.
    /// <list type="bullet">
    /// <item>A window's root carries the tag; everything in its tree inherits it (the pages, the side pane, the status
    /// bar, AppContentDialog, which joins the root).</item>
    /// <item>The menu bar's items carry it themselves (they are shown in a popup of their own, outside the tree), set when
    /// they are made and on every application.</item>
    /// <item>Every popup open in a window (menus, context menus, flyouts, tooltips, a combo box's list) gets it on its
    /// content as it is laid out (<see cref="StampOpenPopups"/>, from the window's LayoutUpdated): before it is drawn,
    /// each time it opens.</item>
    /// </list>
    /// </summary>
    public static class UiLanguage
    {
        public static string Tag => Locale.ElementLanguage;

        /// <summary>The tag on one element, when it has another (setting it again lays the element's text out again).</summary>
        public static void Set(FrameworkElement element)
        {
            var tag = Tag;
            if (element == null || string.IsNullOrEmpty(tag)) return;
            if (!string.Equals(element.Language, tag, StringComparison.OrdinalIgnoreCase)) element.Language = tag;
        }

        /// <summary>The tag on menu items and their submenus' items (they are shown in a popup, not under the menu bar).</summary>
        public static void SetItems(IEnumerable<MenuFlyoutItemBase> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                Set(item);
                if (item is MenuFlyoutSubItem sub) SetItems(sub.Items);
            }
        }

        /// <summary>The menu bar's menus and every item in them.</summary>
        public static void SetMenuBar(muxc.MenuBar menuBar)
        {
            if (menuBar == null) return;
            Set(menuBar);
            foreach (var menu in menuBar.Items)
            {
                Set(menu);
                SetItems(menu.Items);
            }
        }

        /// <summary>A flyout's content before it is shown: a menu's items, a flyout's content.</summary>
        public static void SetFlyout(FlyoutBase flyout)
        {
            if (flyout is MenuFlyout menu) SetItems(menu.Items);
            else if (flyout is Flyout plain && plain.Content is FrameworkElement content) Set(content);
        }

        /// <summary>
        /// Every popup open on the window's XamlRoot: the popup and what it shows. Cheap when none is open (an empty list),
        /// which is nearly always, so it is asked after every layout of the window.
        /// </summary>
        public static void StampOpenPopups(XamlRoot root)
        {
            if (root == null || string.IsNullOrEmpty(Tag)) return;
            try
            {
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
                {
                    Set(popup);
                    if (popup.Child is FrameworkElement child) Set(child);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"language: the open popups: {ex.Message}");
            }
        }

        private static readonly ConditionalWeakTable<FlyoutBase, object> watched = new();
        private static readonly object Seen = new();

        /// <summary>The flyouts attached to elements of a tree (context menus, a button's flyout) get the tag each time they open.</summary>
        public static void WatchFlyouts(DependencyObject root)
        {
            if (root == null) return;
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is UIElement element) Watch(element.ContextFlyout);
                if (node is Button button) Watch(button.Flyout);
                if (node is FrameworkElement framework) Watch(FlyoutBase.GetAttachedFlyout(framework));
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        private static void Watch(FlyoutBase flyout)
        {
            if (flyout == null || watched.TryGetValue(flyout, out _)) return;
            watched.Add(flyout, Seen);
            flyout.Opening += (s, _) => SetFlyout(s as FlyoutBase);
        }
    }
}
