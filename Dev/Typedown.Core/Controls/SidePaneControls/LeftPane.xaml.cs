using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    public sealed partial class LeftPane : UserControl
    {
        public static readonly DependencyProperty IsSearchPaneOpenProperty = DependencyProperty.Register(nameof(IsSearchPaneOpen), typeof(bool), typeof(LeftPane), new(false));
        public bool IsSearchPaneOpen { get => (bool)GetValue(IsSearchPaneOpenProperty); set => SetValue(IsSearchPaneOpenProperty, value); }

        public static readonly DependencyProperty IsFindResultsOpenProperty = DependencyProperty.Register(nameof(IsFindResultsOpen), typeof(bool), typeof(LeftPane), new(false));
        /// <summary>The find bar's results list is shown over the pane's page (UIViewModel.FindResultsOpen).</summary>
        public bool IsFindResultsOpen { get => (bool)GetValue(IsFindResultsOpenProperty); set => SetValue(IsFindResultsOpenProperty, value); }

        public static readonly DependencyProperty OutlineOpenProperty = DependencyProperty.Register(nameof(OutlineOpen), typeof(bool), typeof(LeftPane), new(true));
        /// <summary>The outline block is open (not folded to its title row): its page is made only then.</summary>
        public bool OutlineOpen { get => (bool)GetValue(OutlineOpenProperty); set => SetValue(OutlineOpenProperty, value); }

        /// <summary>Files/Outline are shown unless the folder search or the find bar's results list is over them.</summary>
        public static Visibility PageVisibility(bool searchPaneOpen, bool findResultsOpen) =>
            searchPaneOpen || findResultsOpen ? Visibility.Collapsed : Visibility.Visible;

        public AppViewModel ViewModel => DataContext as AppViewModel;

        public SettingsViewModel Settings => ViewModel?.SettingsViewModel;

        private readonly CompositeDisposable disposables = new();

        public LeftPane()
        {
            InitializeComponent();
            // One brush for the marks, there before any is drawn: a new accent is a new colour of it and reaches every
            // mark at once. The rows of the outline and the folder tree find it in the application's resources - a row
            // WinUI builds (or recycles) away from the pane looks up only those, and one outline row kept the system
            // accent under a custom theme. Only these two trees use that key.
            Application.Current.Resources[TreeAccentKey] = AccentBrush;
            // A row under the pointer or pressed draws its mark from keys of its own (the selected heading turned the
            // system blue whenever the mouse was over it): the same colour, a little lighter, as WinUI's own are.
            Application.Current.Resources[TreeAccentKey + "PointerOver"] = AccentPointerOverBrush;
            Application.Current.Resources[TreeAccentKey + "Pressed"] = AccentPressedBrush;
            Root.ActualThemeChanged += (_, _) => UpdateAccent();
            BlockSplitter.DragStarted += OnSplitterDragStarted;
            BlockSplitter.Dragged += OnSplitterDragged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var uiViewModel = ViewModel.UIViewModel;
            // What folds a block: the reader (remembered), or nothing to show in it - no folder, a plain text without
            // headings - until the reader opens it, or something asks for it (Open Folder, the status bar's heading).
            foreach (var name in new[] { nameof(SettingsViewModel.SidePaneFilesCollapsed), nameof(SettingsViewModel.SidePaneOutlineCollapsed),
                nameof(SettingsViewModel.SidePaneSplit), nameof(SettingsViewModel.PlainTextShown) })
                disposables.Add(Settings.WhenPropertyChanged(name).Subscribe(_ => UpdateBlocks()));
            FilesPage.FolderShownChanged += OnFolderShownChanged;
            disposables.Add(Disposable.Create(() => FilesPage.FolderShownChanged -= OnFolderShownChanged));
            var editor = ViewModel.EditorViewModel;
            editor.OutlineUpdated += OnOutlineUpdated;
            disposables.Add(Disposable.Create(() => editor.OutlineUpdated -= OnOutlineUpdated));
            // Another document: an outline the reader opened for one plain text folds away again for the next.
            disposables.Add(ViewModel.FileViewModel.WhenPropertyChanged(nameof(FileViewModel.FilePath)).Subscribe(_ =>
            {
                outlineForced = false;
                UpdateBlocks();
            }));
            disposables.Add(uiViewModel.WhenPropertyChanged(nameof(UIViewModel.SidePaneBlockRequest))
                .StartWith(uiViewModel.SidePaneBlockRequest)
                .Subscribe(_ =>
                {
                    if (uiViewModel.TakeSidePaneBlockRequest() is not int page) return;
                    if (page == 1) outlineForced = true;
                    else filesForced = true;
                    UpdateBlocks();
                }));
            disposables.Add(uiViewModel.WhenPropertyChanged(nameof(UIViewModel.FolderSearchOpen))
                .Cast<bool>()
                .StartWith(uiViewModel.FolderSearchOpen)
                .Subscribe(open => IsSearchPaneOpen = open));
            disposables.Add(uiViewModel.WhenPropertyChanged(nameof(UIViewModel.FindResultsOpen))
                .Cast<bool>()
                .StartWith(uiViewModel.FindResultsOpen)
                .Subscribe(open => IsFindResultsOpen = open));
            // The titles read their text when the pane loads. Changing the language on the settings page builds a new
            // main page; a change from another window or the automation API leaves this one, so the titles follow it
            // here, once the language has been applied.
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.Language))
                .Subscribe(language => _ = DispatcherQueue.RunIdleAsync(args =>
                {
                    FilesTitle.Text = Locale.GetString("Files");
                    OutlineTitle.Text = Locale.GetString("Outline");
                })));
            disposables.Add(Settings.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme))
                .StartWith(Settings.CustomTheme)
                .Subscribe(_ => UpdateAccent()));
            // The system's accent changed (Windows settings): raised off the UI thread.
            disposables.Add(uiSettings.GetColorValuesObservable()
                .Subscribe(_ => DispatcherQueue.TryEnqueueSafe(UpdateAccent)));
            UpdateBlocks();
        }

        // The marks of what is chosen here - the pill beside the current heading in the outline and beside the open file
        // in the folder tree - are drawn in the accent colour, which WinUI takes from Windows, not from the custom theme:
        // with any theme they stayed the system blue. A theme with an accent of its own has it put here, where the
        // outline and the folder tree look it up; a theme without one leaves the system accent.
        private const string TreeAccentKey = "TreeViewItemSelectionIndicatorForeground";

        // Shared by the windows' panes: the custom theme is one setting for all of them, and they share the UI thread.
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush AccentBrush = new();

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush AccentPointerOverBrush = new() { Opacity = 0.9 };

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush AccentPressedBrush = new() { Opacity = 0.8 };

        private readonly global::Windows.UI.ViewManagement.UISettings uiSettings = new();

        private void UpdateAccent()
        {
            var custom = ThemeFiles.Brush(ThemeFiles.Find(Settings?.CustomTheme)?.Accent) as Microsoft.UI.Xaml.Media.SolidColorBrush;
            // Without one, the system accent in the shade WinUI gives these marks (AccentFillColorDefault): darker on a
            // light theme, lighter on a dark one.
            AccentPointerOverBrush.Color = AccentPressedBrush.Color = AccentBrush.Color = custom?.Color ?? uiSettings.GetColorValue(Root.ActualTheme == ElementTheme.Dark
                ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2
                : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
        }

        // ---- the two blocks: files above, the outline below ----

        // Opened by the reader, or asked for, while there was nothing to show in it; forgotten when that changes.
        private bool filesForced, outlineForced;

        private bool filesHadFolder;

        // A block sharing the pane with the other is not dragged smaller than this.
        private const double BlockMinHeight = 48;

        private bool OutlineEmptyPlainText =>
            Settings.PlainTextShown && (ViewModel?.EditorViewModel?.Toc?.Children?.Count ?? 0) == 0;

        private bool FilesIsOpen => !Settings.SidePaneFilesCollapsed && (FilesPage.HasFolder || filesForced);

        private bool OutlineIsOpen => !Settings.SidePaneOutlineCollapsed && (!OutlineEmptyPlainText || outlineForced);

        private void OnFolderShownChanged()
        {
            if (FilesPage.HasFolder != filesHadFolder) filesForced = false;
            UpdateBlocks();
        }

        private void OnOutlineUpdated() => DispatcherQueue.TryEnqueueSafe(UpdateBlocks);

        private void UpdateBlocks()
        {
            if (Settings == null) return;
            filesHadFolder = FilesPage.HasFolder;
            var files = FilesIsOpen;
            var outline = OutlineIsOpen;
            FilesPage.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
            OutlineOpen = outline;
            BlockSplitter.Visibility = files && outline ? Visibility.Visible : Visibility.Collapsed;
            var split = Math.Clamp(Settings.SidePaneSplit, 0.05, 0.95);
            FilesRow.Height = files ? new GridLength(outline ? split : 1, GridUnitType.Star) : new GridLength(0);
            // Both folded: the outline's row takes the rest, empty, so the two title rows stay at the top.
            OutlineRow.Height = outline ? new GridLength(files ? 1 - split : 1, GridUnitType.Star)
                : files ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            FilesRow.MinHeight = OutlineRow.MinHeight = files && outline ? BlockMinHeight : 0;
            FilesChevron.Glyph = files ? "" : "";
            OutlineChevron.Glyph = outline ? "" : "";
            // The title row says whether its block is open, for a screen reader (and the tests).
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(Folder, files ? "expanded" : "collapsed");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(Toc, outline ? "expanded" : "collapsed");
        }

        private void OnFilesHeaderClick(object sender, RoutedEventArgs e)
        {
            if (FilesIsOpen) Settings.SidePaneFilesCollapsed = true;
            else
            {
                filesForced = true;
                Settings.SidePaneFilesCollapsed = false;
            }
            UpdateBlocks();
        }

        private void OnOutlineHeaderClick(object sender, RoutedEventArgs e)
        {
            if (OutlineIsOpen) Settings.SidePaneOutlineCollapsed = true;
            else
            {
                outlineForced = true;
                Settings.SidePaneOutlineCollapsed = false;
            }
            UpdateBlocks();
        }

        private double dragFilesHeight, dragBothHeight;

        private void OnSplitterDragStarted()
        {
            dragFilesHeight = FilesRow.ActualHeight;
            dragBothHeight = FilesRow.ActualHeight + OutlineRow.ActualHeight;
        }

        private void OnSplitterDragged(double moved)
        {
            if (Settings == null || dragBothHeight <= 2 * BlockMinHeight) return;
            var files = Math.Clamp(dragFilesHeight + moved, BlockMinHeight, dragBothHeight - BlockMinHeight);
            Settings.SidePaneSplit = files / dragBothHeight;
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.UIViewModel != null) ViewModel.UIViewModel.FolderSearchOpen = true;
            IsSearchPaneOpen = true;
        }

        private void OnSearchPaneClose(object sender, EventArgs e)
        {
            if (ViewModel?.UIViewModel != null) ViewModel.UIViewModel.FolderSearchOpen = false;
            IsSearchPaneOpen = false;
        }

        // Its close button or Esc: the pane back to the page it was on, or closed again.
        private void OnFindResultsPaneClose(object sender, EventArgs e) => ViewModel?.UIViewModel?.CloseFindResults();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            disposables.Clear();
            Bindings?.StopTracking();
        }
    }
}
