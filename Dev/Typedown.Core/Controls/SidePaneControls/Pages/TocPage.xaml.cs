using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
            // The drag over the rows and the drop, handled or not by the tree's own list (it takes them, even not reordering:
            // set in XAML, these never ran - run 682).
            TreeView.AddHandler(DragOverEvent, new DragEventHandler(OnTreeDragOver), true);
            TreeView.AddHandler(DragLeaveEvent, new DragEventHandler(OnTreeDragLeave), true);
            TreeView.AddHandler(DropEvent, new DragEventHandler(OnTreeDrop), true);
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
                watched.SectionMoved += OnSectionMoved;
            }
            OnOutlineUpdated();
            // The heading marked while the pane was away (closed, or the window's page rebuilt): its rows are made anew here
            // and nothing marked them until the editor named a heading again - in reading mode, not before the reader
            // scrolled to another one.
            if (Editor?.AppliedCurSlug is string slug) OnOutlineHighlighted(slug);
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
                    // Another document: its outline in one step, each heading's node made whole (children and
                    // expansion) before it goes in, and without the rows' transitions. With them every row of a
                    // three-thousand-heading outline faded and slid in on its own as it was laid out, which is
                    // work for rows that were all there at once; they are put back once the new rows are laid
                    // out, for the headings an edit adds and moves.
                    var transitions = TreeView.ItemContainerTransitions;
                    if (transitions != null)
                    {
                        TreeView.ItemContainerTransitions = null;
                        RestoreTransitionsAfterLayout(transitions);
                    }
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

        private Microsoft.UI.Xaml.Media.Animation.TransitionCollection heldTransitions;

        // Once, after the next layout pass: that pass is the one that makes the rows of the outline just put in.
        // A second replacement before then keeps the transitions it found held, not the empty slot left by the first.
        private void RestoreTransitionsAfterLayout(Microsoft.UI.Xaml.Media.Animation.TransitionCollection transitions)
        {
            if (heldTransitions != null) return;
            heldTransitions = transitions;
            void restore(object sender, object e)
            {
                TreeView.LayoutUpdated -= restore;
                if (heldTransitions != null && TreeView.ItemContainerTransitions == null)
                    TreeView.ItemContainerTransitions = heldTransitions;
                heldTransitions = null;
            }
            TreeView.LayoutUpdated += restore;
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
                        // By its place and the file, not its words (the heading is the document's text).
                        var item = (node.Content as Models.TocTreeItem)?.TocItem;
                        var toc = Editor?.ContentState?.Toc;
                        Utilities.Log.Debug($"outline: marked {System.IO.Path.GetFileName(ViewModel?.FileViewModel?.FilePath) ?? "untitled"} "
                            + $"{LogText.Heading(toc?.IndexOf(item) ?? -1, toc?.Count ?? 0, item?.Lvl ?? 0, item?.Content)} (load {Editor?.LoadId})");
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

        // The heading right-clicked: the menu is one for all the rows, and knows its row by the element it opened on.
        private Models.TocTreeItem menuItem;

        private void OnTocItemFlyoutOpening(object sender, object e) =>
            menuItem = ((sender as Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase)?.Target as FrameworkElement)?.DataContext is Microsoft.UI.Xaml.Controls.TreeViewNode node
                ? node.Content as Models.TocTreeItem
                : null;

        private async void OnCopyHeadingLinkClick(object sender, RoutedEventArgs e)
        {
            var item = menuItem;
            try
            {
                if (item?.TocItem == null || Editor == null || !await Editor.CopyHeadingLinkAsync(item.TocItem))
                    Log.Debug("outline: no link copied for the heading right-clicked");
            }
            catch (System.Exception ex)
            {
                Log.Debug($"outline: copy heading link: {ex.Message}");
            }
        }

        private void OnExpandAllClick(object sender, RoutedEventArgs e) => SetExpanded(TreeView.RootNodes, true);

        private void OnCollapseAllClick(object sender, RoutedEventArgs e) => SetExpanded(TreeView.RootNodes, false);

        // ---- a heading dragged: its section moved in the document (the user's 28, 2026-11-20, as Obsidian does) ----
        // The tree does not reorder its rows (left to it, any drop near a row put the heading into that row - runs 679-681 -
        // so nothing could go after a heading at its level). Where on the row under the pointer the drop is says it: its upper
        // third before it, its middle into it (its last sub-section), its lower third after it - after its whole section, or,
        // an open row with rows under it, before the first of those, where the line shows; below the last row, after the
        // last section. A line or a box shows it while dragging. The page moves the text (contentState/sectionMoveCtrl.js)
        // and its outline comes back the usual way; refused, the reason shows.

        private TreeViewNode dragged;

        private static string SlugOf(TreeViewNode node) => (node?.Content as Models.TocTreeItem)?.TocItem?.Slug;

        private void OnDragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
        {
            // The text is moved: not in reading mode, and not in source mode (its outline is the text's lines, no blocks).
            var settings = ViewModel?.SettingsViewModel;
            var node = args.Items.Count == 1 ? FindNode(TreeView.RootNodes, (ContentOf(args.Items[0]) as Models.TocTreeItem)?.TocItem?.Slug) : null;
            if (node == null || Editor == null || settings == null || settings.ReadOnly || settings.SourceCode) { args.Cancel = true; return; }
            dragged = node;
            shownDrop = null;
            Utilities.Log.Debug($"outline: drag of {SlugOf(node)} begun");
        }

        // What the last drag over showed, so the log says each change once.
        private string shownDrop;

        private void OnDragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
        {
            dragged = null;
            ShowDrop(null);
        }

        private static bool Within(TreeViewNode node, TreeViewNode ancestor)
        {
            for (; node != null; node = node.Parent) if (node == ancestor) return true;
            return false;
        }

        /// <summary>Where a drop at <paramref name="at"/> (in the tree's coordinates) would put the dragged heading.</summary>
        private (TreeViewNode to, string where, Windows.Foundation.Rect row, double line)? DropAt(Windows.Foundation.Point at)
        {
            var inWindow = TreeView.TransformToVisual(null).TransformPoint(at);
            foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(inWindow, TreeView))
            {
                if (element is not TreeViewItem item || TreeView.NodeFromContainer(item) is not TreeViewNode node) continue;
                var row = item.TransformToVisual(TreeView).TransformBounds(new Windows.Foundation.Rect(0, 0, item.ActualWidth, item.ActualHeight));
                var part = (at.Y - row.Top) / System.Math.Max(1, row.Height);
                if (part < 1 / 3.0) return (node, "before", row, row.Top);
                if (part <= 2 / 3.0) return (node, "inside", row, 0);
                if (node.IsExpanded && node.Children.Count > 0) return (node.Children[0], "before", row, row.Bottom);
                return (node, "after", row, row.Bottom);
            }
            // Below the last row: after the last section.
            if (TreeView.RootNodes.Count > 0)
            {
                var lastNode = TreeView.RootNodes[^1];
                var shown = lastNode;
                while (shown.IsExpanded && shown.Children.Count > 0) shown = shown.Children[^1];
                if (TreeView.ContainerFromNode(shown) is TreeViewItem bottom)
                {
                    var row = bottom.TransformToVisual(TreeView).TransformBounds(new Windows.Foundation.Rect(0, 0, bottom.ActualWidth, bottom.ActualHeight));
                    if (at.Y > row.Bottom) return (lastNode, "after", row, row.Bottom);
                }
            }
            return null;
        }

        private void ShowDrop((TreeViewNode to, string where, Windows.Foundation.Rect row, double line)? drop)
        {
            DropLine.Visibility = DropInto.Visibility = Visibility.Collapsed;
            if (drop is not { } d) return;
            if (d.where == "inside")
            {
                Canvas.SetLeft(DropInto, d.row.Left + 2);
                Canvas.SetTop(DropInto, d.row.Top + 1);
                DropInto.Width = System.Math.Max(0, d.row.Width - 4);
                DropInto.Height = System.Math.Max(0, d.row.Height - 2);
                DropInto.Visibility = Visibility.Visible;
                return;
            }
            Canvas.SetLeft(DropLine, d.row.Left + 8);
            Canvas.SetTop(DropLine, d.line - 1);
            DropLine.Width = System.Math.Max(0, d.row.Width - 16);
            DropLine.Visibility = Visibility.Visible;
        }

        private (TreeViewNode to, string where, Windows.Foundation.Rect row, double line)? Allowed(DragEventArgs e)
        {
            if (dragged == null) return null;
            var drop = DropAt(e.GetPosition(TreeView));
            // Not into the dragged heading's own section (the page would refuse it).
            return drop is { } d && !Within(d.to, dragged) ? drop : null;
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            var drop = Allowed(e);
            e.AcceptedOperation = drop != null ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            ShowDrop(drop);
            var said = drop is { } d ? $"{d.where} {SlugOf(d.to)}" : "nowhere";
            if (dragged != null && said != shownDrop) Utilities.Log.Debug($"outline: drag over: {said}");
            shownDrop = said;
        }

        private void OnTreeDragLeave(object sender, DragEventArgs e) => ShowDrop(null);

        private void OnTreeDrop(object sender, DragEventArgs e)
        {
            var drop = Allowed(e);
            var from = SlugOf(dragged);
            ShowDrop(null);
            if (drop is not { } d || from == null || SlugOf(d.to) is not string to) { Utilities.Log.Debug($"outline: section {from} dropped nowhere"); return; }
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            Utilities.Log.Debug($"outline: section {from} dropped {d.where} {to}");
            Editor?.MoveSection(from, to, d.where);
        }

        // The rows as the outline has them (the tree's own move undone).
        private void PutBack()
        {
            try { if (Editor?.Toc is { } toc) Reconcile(TreeView.RootNodes, toc.Children); }
            catch (System.Exception ex) { Utilities.Log.Debug($"outline: could not put the rows back: {ex.Message}"); }
        }

        private Microsoft.UI.Dispatching.DispatcherQueueTimer noticeTimer;

        private void OnSectionMoved(bool moved, string reason)
        {
            if (moved) return;
            PutBack();
            var text = reason switch
            {
                "tooDeep" => Utilities.Locale.GetString("TocPane.MoveTooDeep"),
                "ownSection" => Utilities.Locale.GetString("TocPane.MoveIntoItself"),
                _ => null,
            };
            Utilities.Log.Debug($"outline: section not moved ({reason})");
            if (string.IsNullOrEmpty(text)) return;
            MoveNotice.Text = text;
            MoveNotice.Visibility = Visibility.Visible;
            noticeTimer ??= DispatcherQueue.CreateTimer();
            noticeTimer.Interval = System.TimeSpan.FromSeconds(5);
            noticeTimer.IsRepeating = false;
            noticeTimer.Tick -= OnNoticeTimer;
            noticeTimer.Tick += OnNoticeTimer;
            noticeTimer.Start();
        }

        private void OnNoticeTimer(Microsoft.UI.Dispatching.DispatcherQueueTimer timer, object e) => MoveNotice.Visibility = Visibility.Collapsed;

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (watched != null)
            {
                watched.OutlineHighlighted -= OnOutlineHighlighted;
                watched.OutlineUpdated -= OnOutlineUpdated;
                watched.SectionMoved -= OnSectionMoved;
            }
            watched = null;
            shownToc = null;
            list = null;
            Bindings?.StopTracking();
        }
    }
}
