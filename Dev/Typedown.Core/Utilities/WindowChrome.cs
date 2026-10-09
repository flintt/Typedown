using System;
using Microsoft.UI.Xaml;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The window's title-bar areas as the controls see them, without knowing the window: an element marked
    /// <see cref="DragProperty"/> is part of the caption (dragged, double-clicked to maximize, right-clicked for the system
    /// menu - the system does all of that), and the host window turns the marked elements into caption regions
    /// (InputNonClientPointerSource) whenever they change or move. It replaced XamlUI's drag windows, native windows laid
    /// over each area, which had to be made again whenever an area came back (out of full screen, a page built anew).
    /// </summary>
    public static class WindowChrome
    {
        public static readonly DependencyProperty DragProperty = DependencyProperty.RegisterAttached(
            "Drag", typeof(bool), typeof(WindowChrome), new PropertyMetadata(false, OnDragChanged));

        public static bool GetDrag(DependencyObject element) => (bool)element.GetValue(DragProperty);

        public static void SetDrag(DependencyObject element, bool value) => element.SetValue(DragProperty, value);

        /// <summary>An element was marked or unmarked: the host window of its XamlRoot collects its caption regions again.</summary>
        public static event Action<FrameworkElement> DragAreasChanged;

        /// <summary>The native window an element is shown in (set by the host; zero when it has none yet).</summary>
        public static Func<UIElement, IntPtr> WindowHandleOf { get; set; } = _ => IntPtr.Zero;

        private static void OnDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element) DragAreasChanged?.Invoke(element);
        }
    }
}
