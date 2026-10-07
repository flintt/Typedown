using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// A menu closed - a command chosen, Esc, a click elsewhere - left the focus on the menu bar, or on nothing: the
    /// letters typed next went nowhere until a click in the text. Set on every menu's presenter by the implicit style
    /// (Resources/Styles/MenuFlyout.xaml), so the menu bar, its submenus and the context menus alike: once the menu is
    /// gone and its command has run, the keyboard goes to the editor - unless the command put it somewhere on purpose
    /// (the find box, a dialog, the settings page), or another menu is still open.
    /// </summary>
    public static class MenuFocus
    {
        public static readonly DependencyProperty ReturnToEditorProperty = DependencyProperty.RegisterAttached(
            "ReturnToEditor", typeof(bool), typeof(MenuFocus), new PropertyMetadata(false, OnReturnToEditorChanged));

        public static bool GetReturnToEditor(DependencyObject target) => (bool)target.GetValue(ReturnToEditorProperty);

        public static void SetReturnToEditor(DependencyObject target, bool value) => target.SetValue(ReturnToEditorProperty, value);

        private static readonly DependencyProperty RootProperty = DependencyProperty.RegisterAttached(
            "Root", typeof(XamlRoot), typeof(MenuFocus), new PropertyMetadata(null));

        private static void OnReturnToEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement presenter) || !(bool)e.NewValue) return;
            presenter.Loaded += (_, _) => presenter.SetValue(RootProperty, presenter.XamlRoot);
            presenter.Unloaded += (_, _) =>
            {
                // Unloaded, the presenter no longer knows its window: the one it was shown in, kept from Loaded.
                if (!(presenter.GetValue(RootProperty) is XamlRoot root)) return;
                _ = presenter.Dispatcher.RunIdleAsync(_ =>
                {
                    if (VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0) return;
                    var focused = FocusManager.GetFocusedElement(root);
                    if (focused != null && !(focused is Microsoft.UI.Xaml.Controls.MenuBarItem) && !(focused is Interfaces.IMarkdownEditor)) return;
                    ((root.Content as FrameworkElement)?.DataContext as AppViewModel)?.MarkdownEditor?.FocusEditor();
                });
            };
        }
    }
}
