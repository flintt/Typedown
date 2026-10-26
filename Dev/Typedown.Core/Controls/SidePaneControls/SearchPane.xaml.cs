﻿using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// The side pane's search through the open folder (upstream #33): the names of folders and files first, from the
    /// listings alone, then the files' contents in the background (<see cref="FolderSearch"/>), shown in two groups
    /// (<see cref="FolderSearchResults"/>).
    /// </summary>
    public sealed partial class SearchPane : UserControl, INotifyPropertyChanged
    {
        public event EventHandler Close;

        public event PropertyChangedEventHandler PropertyChanged;

        public AppViewModel ViewModel => DataContext as AppViewModel;

        public FolderSearchResults Results { get; } = new();

        public string Status { get; set; } = "";

        /// <summary>A query is typed: the groups are shown (with their counts, 0 too).</summary>
        public bool HasQuery { get; set; }

        private CancellationTokenSource searchCts;

        /// <summary>The query the results shown are for (what a file opened from them is searched for).</summary>
        private string shownQuery;

        public SearchPane()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ = DispatcherQueue.RunIdleAsync(() => SearchTextBox.Focus(FocusState.Programmatic));
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            searchCts?.Cancel();
        }

        private void OnSearchTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SearchTextBox.Text) && Results.NameCount == 0 && Results.ContentCount == 0)
                Close?.Invoke(this, EventArgs.Empty);
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close?.Invoke(this, EventArgs.Empty);
        }

        private string SearchRoot
        {
            get
            {
                var file = ViewModel?.FileViewModel;
                if (file == null) return null;
                if (!string.IsNullOrEmpty(file.WorkFolder) && Directory.Exists(file.WorkFolder)) return file.WorkFolder;
                if (!string.IsNullOrEmpty(file.FilePath)) return Path.GetDirectoryName(file.FilePath);
                return null;
            }
        }

        private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            searchCts?.Cancel();
            var query = sender.Text?.Trim();
            Results.Clear();
            shownQuery = query;
            HasQuery = !string.IsNullOrEmpty(query);
            if (!HasQuery)
            {
                Status = "";
                return;
            }
            var root = SearchRoot;
            if (root == null)
            {
                HasQuery = false;
                Status = Locale.GetString("FolderPane.NoFolderOpen");
                return;
            }
            var cts = searchCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(300, cts.Token); // debounce typing
                Status = Locale.GetString("FolderSearch.Searching");
                // Both report on this thread, in the order the search made them: a hit before the pass that found it ends.
                var hits = new Progress<FolderSearchHit>(hit => { if (!cts.IsCancellationRequested) Show(hit); });
                var namesDone = new Progress<FolderSearchSummary>(names =>
                {
                    if (!cts.IsCancellationRequested)
                        Status = string.Format(Locale.GetString("FolderSearch.SearchingContents"), Count(names.Names, names.NamesAtCap));
                });
                var search = new FolderSearch();
                var summary = await Task.Run(() => search.Run(root, query, ((IProgress<FolderSearchHit>)hits).Report, cts.Token, ((IProgress<FolderSearchSummary>)namesDone).Report), cts.Token);
                if (cts.IsCancellationRequested) return;
                Status = string.Format(Locale.GetString("FolderSearch.Summary"),
                    Count(summary.Names, summary.NamesAtCap), Count(summary.Contents, summary.ContentsAtCap), summary.FilesRead);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Status = ex.Message;
            }
        }

        private void Show(FolderSearchHit hit)
        {
            var row = Results.Apply(hit);
            // A row marked later: its spoken name says so too.
            if (row != null && hit.Kind == FolderSearchHitKind.AlsoContent && NameResultList.ContainerFromItem(row) is ListViewItem container)
                AutomationProperties.SetName(container, SpokenName(row));
        }

        private static string Count(int count, bool atCap) => atCap ? $"{count} (max)" : count.ToString();

        private void OnShowAllNamesClick(object sender, RoutedEventArgs e)
        {
            Results.ShowAllNames();
            // The button goes; the keyboard stays in the list, on the first row it had not shown.
            var next = FolderSearchResults.NamesPreview;
            _ = DispatcherQueue.RunIdleAsync(() =>
            {
                if (NameResultList.ContainerFromIndex(next) is ListViewItem row) row.Focus(FocusState.Keyboard);
            });
        }

        private void OnResultContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue || args.Item is not FolderSearchHit hit) return;
            AutomationProperties.SetAutomationId(args.ItemContainer, AutomationIdOf(hit));
            AutomationProperties.SetName(args.ItemContainer, SpokenName(hit));
        }

        /// <summary>A row's automation id: its group and its path under the root, with forward slashes ("SearchName:notes/plan.md").</summary>
        public static string AutomationIdOf(FolderSearchHit hit) =>
            (hit.Kind == FolderSearchHitKind.Content ? "SearchContent:" : "SearchName:") + hit.RelativePath.Replace('\\', '/');

        /// <summary>What a screen reader says for a row: its name, folder or not, where it is, and that its content matches too.</summary>
        private static string SpokenName(FolderSearchHit hit)
        {
            var parts = new System.Collections.Generic.List<string> { hit.Name };
            if (hit.IsFolder) parts.Add(Locale.GetString("FolderSearch.Folder"));
            if (hit.RelativePath != hit.Name) parts.Add(hit.RelativePath);
            if (hit.AlsoContent) parts.Add(Locale.GetString("FolderSearch.AlsoInContent"));
            if (!string.IsNullOrEmpty(hit.Snippet)) parts.Add(hit.Snippet);
            return string.Join(", ", parts);
        }

        public static string GlyphOf(bool isFolder) => isFolder ? "" : "";

        public static Visibility ShownIfText(string text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        public static string NamesHeader(int count) => string.Format(Locale.GetString("FolderSearch.NamesHeader"), count);

        public static string ContentsHeader(int count) => string.Format(Locale.GetString("FolderSearch.ContentsHeader"), count);

        public static string ShowAllText(int count) => string.Format(Locale.GetString("FolderSearch.ShowAll"), count);

        private async void OnResultClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not FolderSearchHit hit || ViewModel == null) return;
            // A folder is shown in the tree (the pane closes for it); the root stays.
            if (hit.IsFolder)
            {
                ViewModel.UIViewModel.RevealInFolderTree(hit.FullPath);
                return;
            }
            var query = shownQuery;
            ViewModel.FileViewModel.OpenFileCommand.Execute(hit.FullPath);
            if (string.IsNullOrEmpty(query) || !hit.ContentMatched) return;
            // Let the document load, then open the in-document find with the same query.
            var editor = ViewModel.EditorViewModel;
            await Task.Delay(400);
            editor.SearchValue = query;
            editor.FindCommand.Execute("search");
        }
    }
}
