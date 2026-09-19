using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Typedown.Core.Controls.SidePanelControls.Pages
{
    public sealed partial class TocPage : Page
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public EditorViewModel Editor => ViewModel.EditorViewModel;

        public TocPage()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        // Selection-change already jumps, but clicking the heading the cursor is currently in (after scrolling
        // away) changed nothing, which reads as "single click does not work" (upstream #59, #2). Always jump on click.
        private void OnItemInvoked(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is Models.TocTreeItem item && item.TocItem?.Slug != null)
                Editor?.JumpBySlug(item.TocItem.Slug);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings?.StopTracking();
        }
    }
}
