using System;
using Microsoft.UI;
using Windows.UI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Typedown.Core.Controls
{
    public class GridSplitter : UserControl
    {
        public static DependencyProperty ColumnWidthProperty = DependencyProperty.Register(nameof(ColumnWidth), typeof(double), typeof(GridSplitter), null);
        public double ColumnWidth { get => (double)GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }

        public static DependencyProperty ColumnExpectWidthProperty = DependencyProperty.Register(nameof(ColumnExpectWidth), typeof(double), typeof(GridSplitter), new(0d, OnPropertyChanged));
        public double ColumnExpectWidth { get => (double)GetValue(ColumnExpectWidthProperty); set => SetValue(ColumnExpectWidthProperty, value); }

        public static DependencyProperty ColumnMinWidthProperty = DependencyProperty.Register(nameof(ColumnMinWidth), typeof(double), typeof(GridSplitter), new(0d, OnPropertyChanged));
        public double ColumnMinWidth { get => (double)GetValue(ColumnMinWidthProperty); set => SetValue(ColumnMinWidthProperty, value); }

        public static DependencyProperty ColumnMaxWidthProperty = DependencyProperty.Register(nameof(ColumnMaxWidth), typeof(double), typeof(GridSplitter), new(double.PositiveInfinity, OnPropertyChanged));
        public double ColumnMaxWidth { get => (double)GetValue(ColumnMaxWidthProperty); set => SetValue(ColumnMaxWidthProperty, value); }

        public static DependencyProperty DeltaScaleProperty = DependencyProperty.Register(nameof(DeltaScale), typeof(double), typeof(GridSplitter), new(1d));
        public double DeltaScale { get => (double)GetValue(DeltaScaleProperty); set => SetValue(DeltaScaleProperty, value); }

        private readonly Border border = new();

        private double columnWidth;

        private bool manipulating;

        private bool entered;

        public GridSplitter()
        {
            border.Background = new SolidColorBrush(Colors.Transparent);
            border.Width = 9;
            Margin = new Thickness(-4, 0, -4, 0);
            Content = border;
            ManipulationMode = ManipulationModes.TranslateX;
            // Its own cursor while the pointer is over it (WinUI 3: an element's cursor, no window-wide one to set and reset).
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        }

        protected override void OnPointerEntered(PointerRoutedEventArgs e)
        {
            base.OnPointerEntered(e);
            entered = true;

        }

        protected override void OnPointerExited(PointerRoutedEventArgs e)
        {
            base.OnPointerExited(e);
            entered = false;
        }

        protected override void OnManipulationStarted(ManipulationStartedRoutedEventArgs e)
        {
            base.OnManipulationStarted(e);
            columnWidth = ColumnWidth;
            manipulating = true;
        }

        protected override void OnManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
            base.OnManipulationDelta(e);
            var scale = XamlRoot?.RasterizationScale ?? 1;
            columnWidth += DeltaScale * e.Delta.Translation.X * scale;
            ColumnExpectWidth = Math.Min(Math.Max(ColumnMinWidth, columnWidth), ColumnMaxWidth);
        }

        protected override void OnManipulationCompleted(ManipulationCompletedRoutedEventArgs e)
        {
            base.OnManipulationCompleted(e);
            manipulating = false;
        }

        public static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var target = d as GridSplitter;
            var limitedWidth = Math.Min(Math.Max(target.ColumnMinWidth, target.ColumnExpectWidth), target.ColumnMaxWidth);
            if (limitedWidth != target.ColumnWidth)
                target.ColumnWidth = limitedWidth;
        }
    }
}
