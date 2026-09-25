using Typedown.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Typedown.Core.Controls.SidePanelControls.Pages
{
    public sealed partial class TocPage : Page
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public EditorViewModel Editor => ViewModel?.EditorViewModel;

        public TocPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private EditorViewModel watched;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            watched = Editor;
            if (watched != null) watched.OutlineHighlighted += OnOutlineHighlighted;
        }

        // The highlighted row follows the reader down the document, and the pane does not follow the row
        // by itself: two-thirds of the way through a fifty-heading report the mark was below the bottom of
        // the pane, which reads as "the highlight disappeared after 5.4". Bring the row into view, moving
        // the list as little as possible so the rows around it stay put.
        private void OnOutlineHighlighted(string slug)
        {
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                try
                {
                    var node = FindNode(TreeView.RootNodes, slug);
                    var list = FindList(TreeView);
                    if (node != null && list != null) list.ScrollIntoView(node);
                }
                catch (System.Exception ex) { Utilities.Log.Debug($"outline: could not bring {slug} into view: {ex.Message}"); }
            });
        }

        private static Microsoft.UI.Xaml.Controls.TreeViewNode FindNode(System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.TreeViewNode> nodes, string slug)
        {
            foreach (var node in nodes)
            {
                if ((node.Content as Models.TocTreeItem)?.TocItem?.Slug == slug) return node;
                var found = FindNode(node.Children, slug);
                if (found != null) return found;
            }
            return null;
        }

        private ListView list;

        // The TreeView shows its flattened nodes in a ListView of its own; that is what can scroll a row into view.
        private ListView FindList(DependencyObject root)
        {
            if (list != null) return list;
            var count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is ListView lv) return list = lv;
                var found = FindList(child);
                if (found != null) return found;
            }
            return null;
        }

        // Selection-change already jumps, but clicking the heading the cursor is currently in (after scrolling
        // away) changed nothing, which reads as "single click does not work" (upstream #59, #2). Always jump on click.
        private void OnItemInvoked(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is Models.TocTreeItem item && item.TocItem?.Slug != null)
                Editor?.JumpBySlug(item.TocItem.Slug);
        }

        private void OnExpandAllClick(object sender, RoutedEventArgs e) => Editor?.Toc?.SetExpandedRecursive(true);

        private void OnCollapseAllClick(object sender, RoutedEventArgs e) => Editor?.Toc?.SetExpandedRecursive(false);

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (watched != null) watched.OutlineHighlighted -= OnOutlineHighlighted;
            watched = null;
            list = null;
            Bindings?.StopTracking();
        }
    }
}
