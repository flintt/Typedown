using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// A bar dragged up or down between two blocks one above the other (the side pane's files and outline). It only says
    /// how far it has been dragged since the drag began; the owner sizes the blocks.
    /// </summary>
    public class RowSplitter : UserControl
    {
        public event Action DragStarted;

        /// <summary>The distance down from where the drag began, in view pixels (up is negative).</summary>
        public event Action<double> Dragged;

        public event Action DragCompleted;

        private double moved;

        public RowSplitter()
        {
            Content = new Border { Background = new SolidColorBrush(Colors.Transparent), Height = 7 };
            Margin = new Thickness(0, -3, 0, -3);
            ManipulationMode = ManipulationModes.TranslateY;
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);
        }

        protected override void OnManipulationStarted(ManipulationStartedRoutedEventArgs e)
        {
            base.OnManipulationStarted(e);
            moved = 0;
            DragStarted?.Invoke();
        }

        protected override void OnManipulationDelta(ManipulationDeltaRoutedEventArgs e)
        {
            base.OnManipulationDelta(e);
            moved += e.Delta.Translation.Y;
            Dragged?.Invoke(moved);
        }

        protected override void OnManipulationCompleted(ManipulationCompletedRoutedEventArgs e)
        {
            base.OnManipulationCompleted(e);
            DragCompleted?.Invoke();
        }
    }

    /// <summary>A grid that is in the automation tree (a plain grid is not), so it can be found and measured by its id.</summary>
    public class AutomationGrid : Grid
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
}
