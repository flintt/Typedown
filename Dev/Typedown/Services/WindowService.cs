using System.Reactive.Subjects;
using Typedown.Core.Interfaces;
using Typedown.Core.Utilities;
using Typedown.Windows;
using Windows.Foundation;
using Microsoft.UI.Xaml;

namespace Typedown.Services
{
    public class WindowService : IWindowService
    {
        public Subject<nint> WindowStateChanged { get; } = new();

        public Subject<nint> WindowIsActivedChanged { get; } = new();

        public void RaiseWindowStateChanged(nint hWnd) => WindowStateChanged.OnNext(hWnd);

        public void RaiseWindowIsActivedChanged(nint hWnd) => WindowIsActivedChanged.OnNext(hWnd);

        public nint GetWindow(UIElement element) => MainWindow.GetWindow(element)?.Handle ?? default;

        // The window the XAML content is drawn in: under WinUI 3 the window itself (XAML Islands had a child window for it).
        public nint GetXamlSourceHandle(UIElement element) => MainWindow.GetWindow(element)?.Handle ?? default;

        public Point GetCursorPos(UIElement relativeTo)
        {
            var window = MainWindow.GetWindow(relativeTo);
            PInvoke.GetCursorPos(out var screenPos);
            var xamlRootRect = PInvoke.GetClientRectOnScreen(window.Handle);
            var pos = new Point((screenPos.X - xamlRootRect.left) / window.ScalingFactor, (screenPos.Y - xamlRootRect.top) / window.ScalingFactor);
            return relativeTo.XamlRoot.Content.TransformToVisual(relativeTo).TransformPoint(pos);
        }
    }
}
