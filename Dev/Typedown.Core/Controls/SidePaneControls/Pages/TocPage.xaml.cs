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
                watched.OutlineUpdated += OnOutlineUpdated;
            }
            OnOutlineUpdated();
        }

        // ---- the tree's nodes ----
        // The tree is given nodes this page makes, not the item models as its ItemsSource. Given the models, the tree made
        // its own nodes and kept a heading's expansion twice, on its node and (bound both ways) on the model; a row it
        // made late, below the fold of an outline just handed over for another tab, came up with a fresh node's
        // "collapsed" while the model said expanded, so a heading's children were hidden now and then with nobody
        // having collapsed it (OL01). Setting those nodes from outside crashed inside the tree while it was rebuilding.
        // Here each node is made with its expansion, the reader's expanding and collapsing is copied to the model, and
        // nothing else writes either.

        private Models.TocTreeItem shownToc;

        private void OnOutlineUpdated()
        {
            var toc = Editor?.Toc;
            try
            {
                if (toc == null) { TreeView.RootNodes.Clear(); shownToc = null; return; }
                if (!ReferenceEquals(toc, shownToc))
                {
                    // Another document: its outline in one step.
                    TreeView.RootNodes.Clear();
                    foreach (var item in toc.Children) TreeView.RootNodes.Add(MakeNode(item));
                    shownToc = toc;
                    return;
                }
                // The same document's outline again (an edit): the nodes of headings still there are kept, with their
                // expansion; those gone are removed and new ones made.
                Reconcile(TreeView.RootNodes, toc.Children);
            }
            catch (System.Exception ex) { Utilities.Log.Debug($"outline: could not show the outline: {ex.Message}"); }
        }

        private static TreeViewNode MakeNode(Models.TocTreeItem item)
        {
            var node = new TreeViewNode { Content = item };
            foreach (var child in item.Children) node.Children.Add(MakeNode(child));
            node.IsExpanded = item.IsExpanded;
            return node;
        }

        private static void Reconcile(System.Collections.Generic.IList<TreeViewNode> nodes, System.Collections.Generic.IList<Models.TocTreeItem> items)
        {
            var wanted = new System.Collections.Generic.HashSet<Models.TocTreeItem>(items, System.Collections.Generic.ReferenceEqualityComparer.Instance);
            for (var i = nodes.Count - 1; i >= 0; i--)
                if (!(nodes[i].Content is Models.TocTreeItem had && wanted.Contains(had))) nodes.RemoveAt(i);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (i < nodes.Count && ReferenceEquals(nodes[i].Content, item))
                {
                    Reconcile(nodes[i].Children, item.Children);
                    continue;
                }
                var at = -1;
                for (var j = i + 1; j < nodes.Count; j++)
                    if (ReferenceEquals(nodes[j].Content, item)) { at = j; break; }
                if (at < 0)
                {
                    nodes.Insert(i, MakeNode(item));
                    continue;
                }
                var moved = nodes[at];
                nodes.RemoveAt(at);
                nodes.Insert(i, moved);
                Reconcile(moved.Children, item.Children);
            }
        }

        // The reader's expanding and collapsing, kept on the model: a later edit's outline keeps it, and the model is
        // what expanding to the current heading starts from.
        private void OnNodeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (args.Node?.Content is Models.TocTreeItem item) item.IsExpanded = true;
        }

        private void OnNodeCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            if (args.Node?.Content is Models.TocTreeItem item) item.IsExpanded = false;
        }

        private static void SetExpanded(System.Collections.Generic.IList<TreeViewNode> nodes, bool expanded)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count > 0) node.IsExpanded = expanded;
                if (node.Content is Models.TocTreeItem item) item.IsExpanded = expanded;
                SetExpanded(node.Children, expanded);
            }
        }

        // Every heading above the current one opened (and the current one), as the model's ExpandToSelected did.
        private static bool ExpandTo(System.Collections.Generic.IList<TreeViewNode> nodes, string slug)
        {
            foreach (var node in nodes)
            {
                if ((node.Content as Models.TocTreeItem)?.TocItem?.Slug == slug || ExpandTo(node.Children, slug))
                {
                    if (node.Children.Count > 0) node.IsExpanded = true;
                    if (node.Content is Models.TocTreeItem item) item.IsExpanded = true;
                    return true;
                }
            }
            return false;
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
                marking = true;
                try
                {
                    if (ViewModel?.SettingsViewModel?.TocAutoExpand ?? true) ExpandTo(TreeView.RootNodes, slug);
                    var node = FindNode(TreeView.RootNodes, slug);
                    if (node == null) { Utilities.Log.Debug($"outline: no row for {slug} in the pane"); return; }
                    if (TreeView.SelectedNode != node)
                    {
                        TreeView.SelectedNode = node;
                        Utilities.Log.Debug($"outline: marked {(node.Content as Models.TocTreeItem)?.TocItem?.Content} (load {Editor?.LoadId})");
                    }
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
                if (ContentOf(added) is Models.TocTreeItem item && item.TocItem?.Slug != null && item.TocItem.Slug != Editor?.AppliedCurSlug)
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
            if (ContentOf(args.InvokedItem) is Models.TocTreeItem item && item.TocItem?.Slug != null)
            {
                Editor?.JumpBySlug(item.TocItem.Slug);
                // The reader picked the heading to write there: the keys go to the text, not to the outline (where
                // they started a search in it). Moving through the outline with the arrows does not come here.
                _ = DispatcherQueue.RunIdleAsync(_ => ViewModel?.MarkdownEditor?.FocusEditor());
            }
        }

        // With nodes given to it, the tree hands its nodes to these, not the models.
        private static object ContentOf(object item) => item is TreeViewNode node ? node.Content : item;

        private void OnExpandAllClick(object sender, RoutedEventArgs e) => SetExpanded(TreeView.RootNodes, true);

        private void OnCollapseAllClick(object sender, RoutedEventArgs e) => SetExpanded(TreeView.RootNodes, false);

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (watched != null)
            {
                watched.OutlineHighlighted -= OnOutlineHighlighted;
                watched.OutlineUpdated -= OnOutlineUpdated;
            }
            watched = null;
            shownToc = null;
            list = null;
            Bindings?.StopTracking();
        }
    }
}
