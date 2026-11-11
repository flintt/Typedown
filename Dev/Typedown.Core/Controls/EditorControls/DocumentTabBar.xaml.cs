using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using muxc = Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    /// <summary>Tab strip above the editor; one tab per open document (see <see cref="TabsViewModel"/>).</summary>
    public sealed partial class DocumentTabBar : UserControl
    {
        public AppViewModel ViewModel => DataContext as AppViewModel;

        public TabsViewModel Tabs => ViewModel?.TabsViewModel;

        private readonly CompositeDisposable disposables = new();

        public DocumentTabBar()
        {
            InitializeComponent();
            // Typing that reaches the strip just after a tab or the + button was clicked is for the document: the strip
            // has the keyboard until the editor gets it back, and with a long document coming in that took some 600 ms
            // (the window's thread busy). Passed on in order to the page, which keeps it for the document shown next.
            TabView.CharacterReceived += OnStripCharacterReceived;
            // handledEventsToo: once there are more tabs than fit, the strip's own scroll viewer takes the
            // wheel and marks it handled, so a plain handler never sees it and the wheel only slid the strip
            // sideways instead of moving between tabs.
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Bindings.Update();
            // The + button too leaves the keyboard in the document (see the tabs' AllowFocusOnInteraction). It is made with
            // the strip's template, which may come after this: looked for again as the strip lays itself out, until found.
            if (!KeepAddButtonOffFocus()) TabView.LayoutUpdated += OnStripLayoutUpdated;
            var settings = ViewModel?.SettingsViewModel;
            if (settings != null)
                disposables.Add(settings.WhenPropertyChanged(nameof(settings.CustomTheme)).StartWith(settings.CustomTheme).Subscribe(_ => UpdateThemeColours()));
            if (Tabs is { } tabs)
            {
                System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, e) =>
                {
                    Log.Debug($"tabs: collection {e.Action} old {e.OldStartingIndex} new {e.NewStartingIndex}; shown at {tabs.Tabs.IndexOf(tabs.ActiveTab)}");
                    NoteShownIndexSoon();
                };
                tabs.Tabs.CollectionChanged += changed;
                disposables.Add(System.Reactive.Disposables.Disposable.Create(() => tabs.Tabs.CollectionChanged -= changed));
                disposables.Add(tabs.WhenPropertyChanged(nameof(tabs.ActiveTab)).StartWith(tabs.ActiveTab).Subscribe(_ => shownIndex = tabs.Tabs.IndexOf(tabs.ActiveTab)));
            }
        }

        /// <summary>
        /// The editor CSS runs in WebView and cannot style this native WinUI control. Mirror the custom theme's
        /// colours here. The strip itself stays transparent, so it shows the same background as the side pane
        /// beside it and the header band (menu row) ends at one height across the window; inactive tabs have no
        /// fill of their own, and the selected tab is a card a little lighter than the editor background, edged
        /// with the theme's border colour. Painting the strip and the inactive tabs with the panel surface made
        /// the header twice as tall on the right as on the left and heavy. The remaining states are derived so
        /// hover, press, close and add buttons stay legible.
        /// </summary>
        private void UpdateThemeColours()
        {
            var theme = ThemeFiles.Find(ViewModel?.SettingsViewModel?.CustomTheme);
            var editor = ThemeFiles.Brush(theme?.Background);
            var surface = ThemeFiles.Brush(theme?.Surface) ?? editor;
            var foreground = ThemeFiles.Brush(theme?.Foreground) ?? ThemeFiles.Readable(theme?.Surface ?? theme?.Background);
            var border = ThemeFiles.Brush(theme?.Border);

            TabView.Background = new SolidColorBrush(Colors.Transparent);

            var transparent = theme == null ? null : new SolidColorBrush(Colors.Transparent);
            var strip = editor ?? surface;
            var card = Card(strip);
            var inactiveForeground = WithOpacity(foreground, .68);
            var disabledForeground = WithOpacity(foreground, .40);
            var hover = Blend(strip, foreground, .06);
            var pressed = Blend(strip, foreground, .12);
            var buttonHover = WithOpacity(foreground, .10);
            var buttonPressed = WithOpacity(foreground, .16);

            SetBrush("TabViewItemHeaderBackground", transparent);
            SetBrush("TabViewItemHeaderBackgroundSelected", card);
            SetBrush("TabViewItemHeaderBackgroundPointerOver", hover);
            SetBrush("TabViewItemHeaderBackgroundPressed", pressed);
            SetBrush("TabViewItemHeaderBackgroundDisabled", transparent);
            SetBrush("TabViewItemHeaderForeground", inactiveForeground);
            SetBrush("TabViewItemHeaderForegroundSelected", foreground);
            SetBrush("TabViewItemHeaderForegroundPointerOver", foreground);
            SetBrush("TabViewItemHeaderForegroundPressed", foreground);
            SetBrush("TabViewItemHeaderForegroundDisabled", disabledForeground);

            SetBrush("TabViewItemIconForeground", inactiveForeground);
            SetBrush("TabViewItemIconForegroundSelected", foreground);
            SetBrush("TabViewItemIconForegroundPointerOver", foreground);
            SetBrush("TabViewItemIconForegroundPressed", foreground);
            SetBrush("TabViewItemSeparator", border);
            SetBrush("TabViewBorderBrush", border);
            // The tabs not shown have no edge: a tab dragged and let go stayed in a state that draws its edge, the shown
            // tab's card's without its fill (the user's list, 2026-11-11, 7; TD01 on w10). Only the shown tab's card is edged.
            SetBrush("TabViewItemBorderBrush", new SolidColorBrush(Colors.Transparent));
            SetBrush("TabViewSelectedItemBorderBrush", border);
            SetResource("TabViewSelectedItemBorderThickness", null);

            SetButtonBrushes("TabViewButton", transparent, hover, pressed, foreground, disabledForeground, border);
            SetButtonBrushes("TabViewScrollButton", transparent, hover, pressed, foreground, disabledForeground, border);

            SetBrush("TabViewItemHeaderCloseButtonForeground", inactiveForeground);
            SetBrush("TabViewItemHeaderCloseButtonForegroundPointerOver", foreground);
            SetBrush("TabViewItemHeaderCloseButtonForegroundPressed", foreground);
            SetBrush("TabViewItemHeaderPointerOverCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderPressedCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderSelectedCloseButtonForeground", foreground);
            SetBrush("TabViewItemHeaderDisabledCloseButtonForeground", disabledForeground);
            SetBrush("TabViewItemHeaderCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
            SetBrush("TabViewItemHeaderPointerOverCloseButtonBackground", buttonHover);
            SetBrush("TabViewItemHeaderPressedCloseButtonBackground", buttonPressed);
            SetBrush("TabViewItemHeaderSelectedCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
            SetBrush("TabViewItemHeaderDisabledCloseButtonBackground", new SolidColorBrush(Colors.Transparent), surface != null);
            RefreshThemeResources();
        }

        /// <summary>
        /// Changing these resources does not reach tabs that already exist: each tab looked its brushes up when it
        /// was created, while its selected/pointer-over states look theirs up again on every change of state. Without
        /// this the first tabs kept the default colours, new tabs got the theme's, and switching tabs mixed the two -
        /// two different "current tab" looks, and a tab left behind with the other one's colours - until something
        /// else (toggling a setting) happened to make the tab strip look everything up again. Flipping the strip's
        /// theme and back makes every tab and every state resolve its theme resources afresh, from these values.
        /// </summary>
        private void RefreshThemeResources()
        {
            var requested = TabView.RequestedTheme;
            TabView.RequestedTheme = TabView.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            TabView.RequestedTheme = requested;
        }

        private void SetButtonBrushes(string prefix, Brush background, Brush hover, Brush pressed, Brush foreground, Brush disabledForeground, Brush border)
        {
            SetBrush(prefix + "Background", background);
            SetBrush(prefix + "BackgroundPointerOver", hover);
            SetBrush(prefix + "BackgroundPressed", pressed);
            SetBrush(prefix + "BackgroundDisabled", background);
            SetBrush(prefix + "Foreground", foreground);
            SetBrush(prefix + "ForegroundPointerOver", foreground);
            SetBrush(prefix + "ForegroundPressed", foreground);
            SetBrush(prefix + "ForegroundDisabled", disabledForeground);
            SetBrush(prefix + "BorderBrush", border);
            SetBrush(prefix + "BorderBrushPointerOver", border);
            SetBrush(prefix + "BorderBrushPressed", border);
            SetBrush(prefix + "BorderBrushDisabled", border);
        }

        private void SetBrush(string key, Brush brush, bool apply = true) => SetResource(key, apply ? brush : null);

        private void SetResource(string key, object value)
        {
            if (value == null)
                TabView.Resources.Remove(key);
            else
                TabView.Resources[key] = value;
        }

        private static Brush WithOpacity(Brush brush, double opacity)
        {
            if (!(brush is SolidColorBrush solid)) return brush;
            var colour = solid.Color;
            return new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), colour.R, colour.G, colour.B));
        }

        /// <summary>The selected tab: the background moved toward white (a light theme) or lifted slightly (a dark one).</summary>
        private static Brush Card(Brush background)
        {
            if (!(background is SolidColorBrush bg)) return background;
            var c = bg.Color;
            var light = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;
            return Blend(background, new SolidColorBrush(Colors.White), light ? .55 : .07);
        }

        private static Brush Blend(Brush background, Brush foreground, double amount)
        {
            if (!(background is SolidColorBrush bg) || !(foreground is SolidColorBrush fg)) return background;
            byte Mix(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
            return new SolidColorBrush(Color.FromArgb(255, Mix(bg.Color.R, fg.Color.R), Mix(bg.Color.G, fg.Color.G), Mix(bg.Color.B, fg.Color.B)));
        }

        /// <summary>
        /// The wheel over the tab strip moves between tabs: up goes left, down goes right. The strip scrolls
        /// itself when there are more tabs than fit, which is never what the wheel is wanted for here.
        /// </summary>
        private void OnPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (Tabs == null) return;
            var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta == 0) return;
            e.Handled = true;
            // Off, the wheel stops at the first and the last tab instead of going round (General > the wheel over the tabs).
            var index = Tabs.Tabs.IndexOf(Tabs.ActiveTab);
            if (ViewModel?.SettingsViewModel?.TabWheelWraps == false && (delta > 0 ? index <= 0 : index >= Tabs.Tabs.Count - 1)) return;
            (delta > 0 ? Tabs.PreviousTabCommand : Tabs.NextTabCommand).Execute(default);
            // The strip may have scrolled itself before this ran; following the selection puts it right.
            TabView.SelectedItem = Tabs.ActiveTab;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Tabs == null) return;
            Log.Debug($"tabs: selection {Names(e.RemovedItems)} -> {Names(e.AddedItems)}; shown {Tabs.ActiveTab?.Title} at {Tabs.Tabs.IndexOf(Tabs.ActiveTab)} (was {shownIndex}), dragging {dragging}, order {string.Join("|", Tabs.Tabs.Select(t => t.Title))}");
            // The selection moved because the selected tab went (closed): which tab is shown then is the close's to decide
            // (TabsViewModel.CloseTab shows the neighbour itself). Asked for here as well, the switch could come after a
            // file opened at once had been shown, and put the neighbour back over it (TC03; HJ02/HJ03 on w10).
            if (e.RemovedItems.Any(x => x is DocumentTab gone && !Tabs.Tabs.Contains(gone)))
            {
                if (TabView.SelectedItem != Tabs.ActiveTab) TabView.SelectedItem = Tabs.ActiveTab;
                return;
            }
            // The tab shown taken out of the strip for a moment - a drag takes it out and puts it in again - and the selection gone
            // to a neighbour meanwhile: not a choice of the reader's, nothing is shown for it (the log of TD01 on w10: the
            // switch to the neighbour went out while the tab shown was out, and it was shown). Put back once it is in.
            if (!Tabs.Tabs.Contains(Tabs.ActiveTab))
            {
                Log.Debug("tabs: the tab shown is out of the strip for a moment; the selection is not a switch");
                return;
            }
            // The tab shown dragged to another place: taken out of the strip and put back, its selection went to a neighbour,
            // and that neighbour's document was shown - the dropped tab no longer looked the shown one (the user's list,
            // 2026-11-11, 7; TD01 on w10: td01-3 dragged to the front, td01-1 shown). Not a choice of the reader's: the
            // tab shown stays.
            if (TabView.SelectedItem is DocumentTab moved && moved != Tabs.ActiveTab && Tabs.Tabs.Contains(Tabs.ActiveTab)
                && (dragging || Environment.TickCount64 - shownTabMovedAt < 500 || MovedAway(moved)))
            {
                Log.Debug("tabs: the tab shown was moved in the strip; it stays shown");
                TabView.SelectedItem = Tabs.ActiveTab;
                return;
            }
            if (TabView.SelectedItem is DocumentTab tab && tab != Tabs.ActiveTab)
            {
                if (!closing) handedOverAt = DateTime.UtcNow;
                Tabs.SwitchTabCommand.Execute(tab);
                // A tab clicked in the strip keeps the keyboard on the tab; the reader came to write in the document.
                // Not when the selection moved because a tab was closed: handed over while the next document was
                // being loaded, the keys disturbed its loading, and the outline stayed on the closed document's
                // headings (E2E TC02). The keyboard is where it was after a close anyway.
                // The tab does not take the keyboard when clicked (AllowFocusOnInteraction); should it have it anyway
                // (reached with Tab), the editor gets it back once the click is done with - only then: moving it while
                // the reader types loses a key.
                if (!closing)
                {
                    DispatcherQueue.TryEnqueueSafe(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (XamlRoot != null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not Microsoft.UI.Xaml.Controls.WebView2)
                            ViewModel?.MarkdownEditor?.FocusEditor();
                    });
                }
            }
        }

        private bool KeepAddButtonOffFocus()
        {
            var buttons = Descendants(TabView).OfType<Button>().Where(b => b.Name == "AddButton" || Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "AddButton").ToList();
            foreach (var button in buttons) button.AllowFocusOnInteraction = false;
            if (buttons.Count > 0) Log.Debug($"tabs: the + button leaves the keyboard where it is ({buttons.Count})");
            return buttons.Count > 0;
        }

        private void OnStripLayoutUpdated(object sender, object e)
        {
            if (KeepAddButtonOffFocus()) TabView.LayoutUpdated -= OnStripLayoutUpdated;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }

        private DateTime handedOverAt = DateTime.MinValue;

        private void OnStripCharacterReceived(UIElement sender, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs e)
        {
            if (DateTime.UtcNow - handedOverAt > TimeSpan.FromSeconds(3)) return;
            var ch = e.Character;
            if (ch != '\r' && char.IsControl(ch)) return;
            e.Handled = true;
            ViewModel?.MarkdownEditor?.PostMessage("TypedElsewhere", new { text = ch == '\r' ? "\n" : ch.ToString() });
        }

        private void OnAddTabButtonClick(muxc.TabView sender, object args)
        {
            handedOverAt = DateTime.UtcNow;
            ViewModel?.FileViewModel.NewFileCommand.Execute(default);
        }

        // A tab dragged to another place and let go kept the look it had while dragged - an edge like the shown tab's card
        // without its fill (the user's list, 2026-11-11, 7; seen on w10 in TD01's pictures): the strip moves the item
        // and the container it lands in is not put back into its state. Every tab is put back into the state it is in
        // once the move is done.
        // Where the tab shown was in the strip, as the strip last showed it: a drag moves it, and the selection stays at that
        // place and so goes to the tab there now (TD01 on w10, with neither the strip's drag events nor its item events
        // told). Known again after any change of the tabs, once the strip has done with it (Low).
        private int shownIndex = -1;

        private bool MovedAway(DocumentTab selected)
        {
            var now = Tabs.Tabs.IndexOf(Tabs.ActiveTab);
            return shownIndex >= 0 && now >= 0 && now != shownIndex && Tabs.Tabs.IndexOf(selected) == shownIndex;
        }

        private void NoteShownIndexSoon() => DispatcherQueue.TryEnqueueSafe(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Tabs != null) shownIndex = Tabs.Tabs.IndexOf(Tabs.ActiveTab);
        });

        // When the tab shown was last put back into the strip (a drag moves a tab by taking it out and putting it in again).
        private long shownTabMovedAt;

        private void OnTabItemsChanged(muxc.TabView sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
        {
            Log.Debug($"tabs: strip items {args.CollectionChange} at {args.Index}");
            if (args.CollectionChange == Windows.Foundation.Collections.CollectionChange.ItemInserted && Tabs != null
                && (int)args.Index < Tabs.Tabs.Count && Tabs.Tabs[(int)args.Index] == Tabs.ActiveTab)
            {
                shownTabMovedAt = Environment.TickCount64;
                // Selected again: taken out, it gave its selection to a neighbour.
                DispatcherQueue.TryEnqueueSafe(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => { if (Tabs != null && TabView.SelectedItem != Tabs.ActiveTab) TabView.SelectedItem = Tabs.ActiveTab; });
            }
            ResetTabStatesSoon();
        }

        // A tab being dragged (the strip raises these for a move within it too): no selection meanwhile is the reader's. Taken
        // for one for 10 s at most, should the end never be told: the tabs would no longer take a click.
        private long dragStartedAt = long.MinValue / 2, dragEndedAt;
        private bool dragging => dragStartedAt > dragEndedAt && Environment.TickCount64 - dragStartedAt < 10000;

        private void OnTabDragStarting(muxc.TabView sender, muxc.TabViewTabDragStartingEventArgs args)
        {
            dragStartedAt = Environment.TickCount64;
            Log.Debug($"tabs: drag starting ({(args.Item as DocumentTab)?.Title})");
        }

        // For the log: the tabs a selection change names.
        private static string Names(System.Collections.Generic.IList<object> items) => "[" + string.Join(",", items.Select(x => (x as DocumentTab)?.Title ?? "?")) + "]";

        private void OnTabDragCompleted(muxc.TabView sender, muxc.TabViewTabDragCompletedEventArgs args)
        {
            dragEndedAt = Environment.TickCount64;
            Log.Debug($"tabs: drag completed ({(args.Item as DocumentTab)?.Title})");
            if (Tabs != null && TabView.SelectedItem != Tabs.ActiveTab) TabView.SelectedItem = Tabs.ActiveTab;
            ResetTabStatesSoon();
        }

        // Once the strip is done with the move, and again once the drop's animation has played.
        private async void ResetTabStatesSoon()
        {
            DispatcherQueue.TryEnqueueSafe(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ResetTabStates);
            await System.Threading.Tasks.Task.Delay(700);
            ResetTabStates();
        }

        private void ResetTabStates()
        {
            if (Tabs == null) return;
            foreach (var tab in Tabs.Tabs)
            {
                if (TabView.ContainerFromItem(tab) is not muxc.TabViewItem item) continue;
                var common = VisualStateManager.GoToState(item, item.IsSelected ? "Selected" : "Normal", false);
                VisualStateManager.GoToState(item, "NotDragging", false);
                VisualStateManager.GoToState(item, "NoReorderHint", false);
                // Which states it is in, by group, for the log: what keeps a dropped tab's edge.
                if (VisualTreeHelper.GetChildrenCount(item) > 0 && VisualTreeHelper.GetChild(item, 0) is FrameworkElement root)
                    Log.Debug($"tabs: {tab.Title} states {string.Join(", ", VisualStateManager.GetVisualStateGroups(root).Select(g => g.Name + "=" + (g.CurrentState?.Name ?? "-")))} (to {(item.IsSelected ? "Selected" : "Normal")}: {common})");
                // A tab dragged is lifted (a translation off the strip, which casts a shadow) and was left lifted: the edge
                // it kept was that shadow's, darker than the strip (TD01's pictures on w10). Put back down.
                if (item.IsSelected) continue;
                if (item.Translation != System.Numerics.Vector3.Zero)
                {
                    Log.Debug($"tabs: {tab.Title} was left lifted ({item.Translation}); put down");
                    item.Translation = System.Numerics.Vector3.Zero;
                }
                // The lift may be the composition visual's own (the list's drag animates it, not the element's Translation),
                // or a shadow the element was given: each put back, and said in the log when it was there.
                if (item.Shadow != null)
                {
                    Log.Debug($"tabs: {tab.Title} had a shadow; taken off");
                    item.Shadow = null;
                }
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(item);
                visual.Properties.TryGetVector3("Translation", out var lifted);
                if (lifted != System.Numerics.Vector3.Zero || visual.Scale != System.Numerics.Vector3.One || visual.Opacity != 1f)
                {
                    Log.Debug($"tabs: {tab.Title} visual left at translation {lifted}, scale {visual.Scale}, opacity {visual.Opacity}; put back");
                    visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
                    visual.Scale = System.Numerics.Vector3.One;
                    visual.Opacity = 1f;
                }
            }
        }

        private bool closing;

        private void OnTabCloseRequested(muxc.TabView sender, muxc.TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is DocumentTab tab)
            {
                closing = true;
                Tabs?.CloseTabCommand.Execute(tab);
                _ = DispatcherQueue.RunIdleAsync(_ => closing = false);
            }
        }

        /// <summary>The ⌂ button's tooltip: Home and its shortcut.</summary>
        public string HomeToolTip
        {
            get
            {
                var key = ViewModel?.SettingsViewModel?.ShortcutHome;
                var text = key == null ? null : this.GetService<Interfaces.IKeyboardAccelerator>()?.GetShortcutKeyText(key);
                return string.IsNullOrEmpty(text) ? Locale.GetString("Home") : $"{Locale.GetString("Home")} ({text})";
            }
        }

        private void OnHomeButtonClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.HomeViewModel.ShowHomeCommand.Execute(default);
        }

        // The tab the menu was opened on, and its file.
        private DocumentTab menuTab;

        private void OnTabContextFlyoutOpening(object sender, object e)
        {
            var flyout = (MenuFlyout)sender;
            menuTab = (flyout.Target as FrameworkElement)?.DataContext as DocumentTab;
            var path = menuTab?.FilePath;
            var tabs = Tabs?.Tabs;
            var index = tabs?.IndexOf(menuTab) ?? -1;
            var store = Services.Favorites.Shared;
            var isFavorite = !string.IsNullOrEmpty(path) && store.Contains(path);
            foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
            {
                switch (item.Tag as string)
                {
                    case "Close": item.IsEnabled = index >= 0; break;
                    case "CloseOthers": item.IsEnabled = index >= 0 && tabs.Count > 1; break;
                    case "CloseToTheRight": item.IsEnabled = index >= 0 && index < tabs.Count - 1; break;
                    case "File": item.IsEnabled = !string.IsNullOrEmpty(path); break;
                    case "Favorite":
                        item.Text = Locale.GetString(isFavorite ? "Favorites.Remove" : "Favorites.Add");
                        item.IsEnabled = !string.IsNullOrEmpty(path) && (isFavorite || store.Items.Count < Services.FavoritesStore.MaxCount);
                        break;
                }
            }
        }

        // The closes go the way the ×'s does (TabsViewModel.CloseTab: shown, asked about saving); a cancelled question stops
        // the rest.
        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            if (menuTab == null || Tabs == null) return;
            closing = true;
            Tabs.CloseTabCommand.Execute(menuTab);
            _ = DispatcherQueue.RunIdleAsync(_ => closing = false);
        }

        private async void OnTabCloseOthersClick(object sender, RoutedEventArgs e)
        {
            if (menuTab != null && Tabs != null) await Tabs.CloseOthers(menuTab);
        }

        private async void OnTabCloseToTheRightClick(object sender, RoutedEventArgs e)
        {
            if (menuTab != null && Tabs != null) await Tabs.CloseToTheRight(menuTab);
        }

        private void OnTabCopyPathClick(object sender, RoutedEventArgs e)
        {
            var path = menuTab?.FilePath;
            if (string.IsNullOrEmpty(path)) return;
            try { this.GetService<Interfaces.IClipboard>()?.SetText(path); }
            catch (Exception ex) { Log.Debug($"tab menu: copy path: {ex.Message}"); }
        }

        private void OnTabOpenLocationClick(object sender, RoutedEventArgs e)
        {
            var path = menuTab?.FilePath;
            if (!string.IsNullOrEmpty(path)) Common.OpenFileLocation(path);
        }

        // The tab shown first, then found in the tree as the Files page's "show the current file's folder" does.
        private async void OnTabShowInSideBarClick(object sender, RoutedEventArgs e)
        {
            var tab = menuTab;
            if (string.IsNullOrEmpty(tab?.FilePath) || Tabs == null) return;
            if (tab != Tabs.ActiveTab) await Tabs.SwitchTo(tab);
            if (tab == Tabs.ActiveTab) ViewModel?.UIViewModel.RevealActiveFileInFolderTree();
        }

        private void OnTabFavoriteClick(object sender, RoutedEventArgs e)
        {
            var path = menuTab?.FilePath;
            if (string.IsNullOrEmpty(path)) return;
            var store = Services.Favorites.Shared;
            if (!store.Remove(path)) store.Add(path, folder: false);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
