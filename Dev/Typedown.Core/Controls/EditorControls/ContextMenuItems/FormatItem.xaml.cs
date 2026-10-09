using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Typedown.Core.Controls.EditorControls.ContextMenuItems
{
    public sealed partial class FormatItem : MenuFlyoutItem
    {
        public event EventHandler ItemClick;

        public FormatItem()
        {
            InitializeComponent();
            AddHandler(PointerReleasedEvent, new PointerEventHandler(OnMenuFlyoutItemPointerReleased), true);
        }

        private void OnMenuFlyoutItemPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ItemClick?.Invoke(this, EventArgs.Empty);
        }
    }
}
