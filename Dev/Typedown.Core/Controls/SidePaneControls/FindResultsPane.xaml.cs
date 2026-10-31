using System;
using System.ComponentModel;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// The find bar's results list in the side pane ("Search in document"): the count of the bar's matches and a line of
    /// text around each of the first 500, as the page sends them (<see cref="FindResultsList"/>). An entry chosen puts the
    /// editor on that match, selected; the bar's "n of m" follows. Shown over the pane's page, which comes back when it
    /// closes (<see cref="UIViewModel.CloseFindResults"/>).
    /// </summary>
    public sealed partial class FindResultsPane : UserControl
    {
        public event EventHandler Close;

        public AppViewModel ViewModel => DataContext as AppViewModel;

        public FindResultsList Results => ViewModel?.EditorViewModel?.FindResults;

        private FindResultsList watched;

        public FindResultsPane()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // The list is the window's view model's: bound once the pane has its data context.
            Bindings.Update();
            watched = Results;
            if (watched != null) watched.PropertyChanged += OnResultsChanged;
            if (ViewModel?.UIViewModel is { } ui) ui.FindResultsFocusRequested += OnFocusRequested;
            OnFocusRequested();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (watched != null) watched.PropertyChanged -= OnResultsChanged;
            watched = null;
            if (ViewModel?.UIViewModel is { } ui) ui.FindResultsFocusRequested -= OnFocusRequested;
            Bindings?.StopTracking();
        }

        // The entry of the match chosen kept in view (a step with the bar's arrows, an entry chosen).
        private void OnResultsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FindResultsList.CurrentEntry) || Results?.CurrentEntry is not { } entry) return;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                try { ResultList.ScrollIntoView(entry); } catch (ArgumentException) { }
            });
        }

        // The keyboard to the list: on the entry of the match chosen, or the first.
        private void OnFocusRequested()
        {
            _ = DispatcherQueue.RunIdleAsync(_ =>
            {
                var results = Results;
                if (results == null || ResultList == null) return;
                var index = results.CurrentEntry?.Index ?? 0;
                if (results.Entries.Count > 0)
                {
                    ResultList.ScrollIntoView(results.Entries[Math.Min(index, results.Entries.Count - 1)]);
                    if (ResultList.ContainerFromIndex(Math.Min(index, results.Entries.Count - 1)) is ListViewItem row && row.Focus(FocusState.Keyboard)) return;
                }
                ResultList.Focus(FocusState.Programmatic);
            });
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e) => Close?.Invoke(this, EventArgs.Empty);

        // Esc closes the list (the bar stays open).
        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            e.Handled = true;
            Close?.Invoke(this, EventArgs.Empty);
        }

        private void OnResultClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FindResultEntry entry) ViewModel?.EditorViewModel?.ActivateFindResult(entry);
        }

        private void OnResultContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue || args.Item is not FindResultEntry entry) return;
            AutomationProperties.SetAutomationId(args.ItemContainer, AutomationIdOf(entry));
            AutomationProperties.SetName(args.ItemContainer, FindResultsList.SpokenName(Locale.GetString("FindResults.Position"), entry, Results?.Total ?? 0));
        }

        /// <summary>An entry's automation id: its place among the matches, from 1 ("FindResult:3").</summary>
        public static string AutomationIdOf(FindResultEntry entry) => "FindResult:" + (entry.Index + 1);

        /// <summary>"Results: 85" (the count is exact, capped or not).</summary>
        public static string TotalText(int total, bool capped) => string.Format(Locale.GetString("FindResults.Total"), total);
    }
}
