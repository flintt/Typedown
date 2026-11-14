using Microsoft.UI.Xaml.Controls.Primitives;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// A toggle button showing a state it is given (IsChecked bound one way) and never changing it itself: a click runs its
    /// command, and the state comes back from what the command did. The context menu's format buttons show whether the
    /// selection is bold, italic... (the user, 2026-11-13; the menu report's M11: they were plain buttons, the state seen
    /// nowhere) - a plain ToggleButton flips itself on a click, and that local value took the place of the binding.
    /// </summary>
    public sealed class StateToggleButton : ToggleButton
    {
        protected override void OnToggle() { }
    }
}
