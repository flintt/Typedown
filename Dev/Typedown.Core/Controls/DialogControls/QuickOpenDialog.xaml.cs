using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Typedown.Core.Controls
{
    /// <summary>
    /// File > Quick Open (Ctrl+P): a box to type a few letters of a file's name in, and the files that have them - the
    /// folder in the side pane's and the ones opened lately - best first. Up and Down choose, Enter opens, Esc closes.
    /// </summary>
    public sealed partial class QuickOpenDialog : UserControl
    {
        private readonly List<QuickOpenCandidate> candidates = new();

        private readonly CancellationTokenSource listing = new();

        private AppContentDialog dialog;

        private string picked;

        public QuickOpenDialog()
        {
            InitializeComponent();
        }

        /// <summary>The file chosen, or null when the dialog was closed without one.</summary>
        public static async Task<string> PickAsync(AppViewModel viewModel)
        {
            var content = new QuickOpenDialog();
            var dialog = AppContentDialog.Create(Locale.GetString("QuickOpen"), content, Locale.GetDialogString("Cancel"));
            content.dialog = dialog;
            // The keyboard in the box, once the dialog has put it on its own button: typed letters go there at once.
            content.Loaded += (_, _) =>
            {
                content.QueryBox.Focus(FocusState.Keyboard);
                content.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => content.QueryBox.Focus(FocusState.Keyboard));
            };
            _ = content.FillAsync(viewModel);
            await dialog.ShowAsync(viewModel.XamlRoot);
            content.listing.Cancel();
            return content.picked;
        }

        // The folder whose files are offered: the side pane's tree, else the folder opened, else the document's own.
        private static string FolderOf(FileViewModel file) =>
            new[] { file?.FolderTreeRoot, file?.WorkFolder, string.IsNullOrEmpty(file?.FilePath) ? null : Path.GetDirectoryName(file.FilePath) }
                .FirstOrDefault(f => !string.IsNullOrEmpty(f) && Directory.Exists(f));

        private async Task FillAsync(AppViewModel viewModel)
        {
            var file = viewModel.FileViewModel;
            var root = FolderOf(file);
            var recent = file?.AccessHistory?.FileRecentlyOpened?.ToList() ?? new List<string>();
            // The files opened lately first, at once; the folder's when it has been walked (off the UI thread).
            Add(recent.Where(File.Exists).Select(p => Candidate(p, root, true)));
            Refresh();
            List<string> folder;
            try { folder = await Task.Run(() => QuickOpen.Files(root, listing.Token)); }
            catch (OperationCanceledException) { return; }
            var known = new HashSet<string>(candidates.Select(c => c.Path), StringComparer.OrdinalIgnoreCase);
            Add(folder.Where(known.Add).Select(p => Candidate(p, root, false)));
            if (folder.Count >= QuickOpen.MaxFiles) Log.Debug($"quick open: {root} has more than {QuickOpen.MaxFiles} files; the first are offered");
            Refresh();
        }

        private void Add(IEnumerable<QuickOpenCandidate> more) => candidates.AddRange(more);

        // Shown under the name: where it is in the folder, or its whole folder when it is elsewhere.
        private static QuickOpenCandidate Candidate(string path, string root, bool recent)
        {
            var inRoot = !string.IsNullOrEmpty(root) && path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var where = inRoot ? Path.GetRelativePath(root, path) : path;
            return new QuickOpenCandidate(path, Path.GetFileName(path), where, recent);
        }

        private void Refresh()
        {
            var found = QuickOpen.Rank(QueryBox.Text, candidates);
            Results.ItemsSource = found;
            if (found.Count > 0) Results.SelectedIndex = 0;
            StatusText.Text = found.Count == 0 ? Locale.GetString("QuickOpenBox.None") : "";
        }

        private void OnQueryChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var count = (Results.ItemsSource as IList<QuickOpenCandidate>)?.Count ?? 0;
            switch (e.Key)
            {
                case VirtualKey.Down when count > 0:
                    Results.SelectedIndex = Math.Min(count - 1, Results.SelectedIndex + 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
                case VirtualKey.Up when count > 0:
                    Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    Pick(Results.SelectedItem as QuickOpenCandidate);
                    e.Handled = true;
                    break;
            }
        }

        private void OnItemClick(object sender, ItemClickEventArgs e) => Pick(e.ClickedItem as QuickOpenCandidate);

        private void Pick(QuickOpenCandidate candidate)
        {
            if (candidate == null) return;
            picked = candidate.Path;
            // Close, not Hide: Hide only takes it off the screen, and ShowAsync (and every dialog after it) went on waiting.
            dialog?.Close();
        }
    }
}
