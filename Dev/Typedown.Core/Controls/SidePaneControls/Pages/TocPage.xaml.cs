using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typedown.Core.Utilities;

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
            if (watched != null)
            {
                watched.OutlineHighlighted += OnOutlineHighlighted;
                watched.PropertyChanged += OnEditorPropertyChanged;
            }
            ScheduleExpansionSync();
        }

        // Another document's outline handed over in one step: its rows come up expanded as its model says.
        private void OnEditorPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditorViewModel.Toc)) ScheduleExpansionSync();
        }

        private bool syncScheduled;

        private void ScheduleExpansionSync(int pass = 0)
        {
            if (syncScheduled) return;
            syncScheduled = true;
            _ = DispatcherQueue.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                syncScheduled = false;
                // A node expanded in this pass makes its children's nodes once the tree has laid it out: the next pass
                // comes after that, from the queue. (Forcing the layout here instead crashed inside the tree while
                // tabs were switched quickly.)
                if (SyncExpansion() && pass < 20) { ScheduleExpansionSync(pass + 1); return; }
                // A heading marked before its row's node existed is marked now that the rows are all there.
                var slug = unmarkedSlug;
                unmarkedSlug = null;
                if (slug != null) OnOutlineHighlighted(slug);
            });
        }

        private string unmarkedSlug;

        // Whether a heading is expanded is kept twice: on the item model (bound both ways to the row) and on the tree's
        // own node, and the node is what the tree draws. When the outline was handed over for another tab, a row the
        // list made late - one below the fold, built after the binding had already given its value - came up with a
        // fresh node's "collapsed" while its model said expanded, and nothing told the node otherwise: now and then a
        // heading's children were hidden with nobody having collapsed it (OL01). The nodes are set from the models
        // once the rows are laid out, a level at a time.
        private bool SyncExpansion()
        {
            try
            {
                var toc = Editor?.Toc;
                return toc != null && SyncExpansion(TreeView.RootNodes, toc.Children) != Sync.Done;
            }
            catch (System.Exception ex)
            {
                Utilities.Log.Debug($"outline: could not set the rows' expansion: {ex.Message}");
                return false;
            }
        }

        private enum Sync { Done, Changed, NotReady }

        // Only nodes that are this outline's, level by level: right after the outline is handed over the tree still holds
        // the previous one's nodes for a moment, and expanding one of those (being torn down) crashed inside the tree.
        // Until the nodes match the models the pass changes nothing and asks to be run again.
        private static Sync SyncExpansion(System.Collections.Generic.IList<Microsoft.UI.Xaml.Controls.TreeViewNode> nodes, System.Collections.Generic.IList<Models.TocTreeItem> items)
        {
            if (nodes.Count != items.Count) return Sync.NotReady;
            for (var i = 0; i < nodes.Count; i++)
                if (!ReferenceEquals(nodes[i].Content, items[i])) return Sync.NotReady;
            var result = Sync.Done;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var item = items[i];
                if (item.Children.Count == 0) continue;
                if (node.IsExpanded != item.IsExpanded)
                {
                    node.IsExpanded = item.IsExpanded;
                    result = Sync.Changed;
                    continue; // its children's nodes come with the next layout
                }
                if (!node.IsExpanded) continue;
                var below = SyncExpansion(node.Children, item.Children);
                if (below == Sync.NotReady) return Sync.NotReady;
                if (below == Sync.Changed) result = Sync.Changed;
            }
            return result;
        }

        // The mark is set on the item model and reaches the row through the IsSelected binding of its
        // container — and the rows are virtualized: only those in view when the pane was built have a
        // container at all. A row further down gets one when it is scrolled to, and in preparing it the
        // list resets IsSelected from its own selection state, which never heard of the model's mark. So
        // the highlight worked for the first thirty-odd headings and vanished for the rest, from 5.4 in a
        // tall window and 5.3 in a shorter one. Selecting through the tree's own SelectedNode puts the mark
        // where the list keeps it, and it survives the row being rebuilt. The row is then brought into
        // view, moving the list as little as possible.
        // The selection is the tree's alone now. The row used to bind IsSelected both ways to the item model,
        // and the tree, the binding and the model then kept each other informed of every change while the
        // whole outline was being torn down and rebuilt twice per tab switch — twenty quick switches ended
        // in a stack overflow inside Microsoft.UI.Xaml. Nothing writes the model's mark back from the tree.
        private bool marking;

        private void OnOutlineHighlighted(string slug)
        {
            _ = DispatcherQueue.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                if (SyncExpansion()) ScheduleExpansionSync();
                marking = true;
                try
                {
                    var node = FindNode(TreeView.RootNodes, slug);
                    if (node == null)
                    {
                        if (syncScheduled) { unmarkedSlug = slug; return; }
                        Utilities.Log.Debug($"outline: no row for {slug} in the pane");
                        return;
                    }
                    if (TreeView.SelectedNode != node) TreeView.SelectedNode = node;
                    FindList(TreeView)?.ScrollIntoView(node);
                }
                catch (System.Exception ex) { Utilities.Log.Debug($"outline: could not mark {slug}: {ex.Message}"); }
                finally { marking = false; }
            });
        }

        // The reader moving the selection with the keyboard goes to that heading; the page marking a heading
        // (above) does not come back through here.
        // The tree raises this after the fact, not inside the SelectedNode setter, so the flag above is down
        // again by the time it arrives; and while the outline is replaced for another document it raises it
        // for rows nobody chose. What tells the reader's keyboard from all of that is focus: a reader moving
        // the selection has the tree focused, the page marking a heading does not.
        private bool TreeHasFocus()
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while (focused != null)
            {
                if (focused == TreeView) return true;
                focused = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused);
            }
            return false;
        }

        private void OnSelectionChanged(Microsoft.UI.Xaml.Controls.TreeView sender, Microsoft.UI.Xaml.Controls.TreeViewSelectionChangedEventArgs args)
        {
            if (marking || !TreeHasFocus()) return;
            foreach (var added in args.AddedItems)
                if (added is Models.TocTreeItem item && item.TocItem?.Slug != null && item.TocItem.Slug != Editor?.AppliedCurSlug)
                { Editor?.JumpBySlug(item.TocItem.Slug); return; }
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
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
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
            {
                Editor?.JumpBySlug(item.TocItem.Slug);
                // The reader picked the heading to write there: the keys go to the text, not to the outline (where
                // they started a search in it). Moving through the outline with the arrows does not come here.
                _ = DispatcherQueue.RunIdleAsync(_ => ViewModel?.MarkdownEditor?.FocusEditor());
            }
        }

        private void OnExpandAllClick(object sender, RoutedEventArgs e)
        {
            Editor?.Toc?.SetExpandedRecursive(true);
            ScheduleExpansionSync();
        }

        private void OnCollapseAllClick(object sender, RoutedEventArgs e)
        {
            Editor?.Toc?.SetExpandedRecursive(false);
            ScheduleExpansionSync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (watched != null)
            {
                watched.OutlineHighlighted -= OnOutlineHighlighted;
                watched.PropertyChanged -= OnEditorPropertyChanged;
            }
            watched = null;
            list = null;
            Bindings?.StopTracking();
        }
    }
}
