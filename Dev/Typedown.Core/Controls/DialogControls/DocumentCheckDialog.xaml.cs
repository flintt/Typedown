using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Controls
{
    /// <summary>A problem as the check's list shows it: what it is, and what it points at and where.</summary>
    public sealed class DocumentProblemRow
    {
        public DocumentProblem Problem { get; init; }

        public string Title { get; init; }

        public string Detail { get; init; }

        // What a list row is called (a screen reader reads the row by it; the tests find it by it).
        public override string ToString() => $"{Title}: {Detail}";
    }

    /// <summary>
    /// File > Check Document, and before an export: the problems found, one row each. A row clicked closes the list and
    /// finds the place in the document (the find bar, with the text that is wrong). Before an export: Export anyway, or
    /// Cancel.
    /// </summary>
    public sealed partial class DocumentCheckDialog : UserControl
    {
        private AppContentDialog dialog;

        private DocumentProblem chosen;

        public DocumentCheckDialog()
        {
            InitializeComponent();
        }

        /// <summary>The list shown. True when the reader chose to export anyway (before an export); false otherwise.</summary>
        public static async Task<bool> ShowAsync(AppViewModel viewModel, IReadOnlyList<DocumentProblem> problems, bool beforeExport)
        {
            var content = new DocumentCheckDialog();
            content.Summary.Text = problems.Count == 0
                ? Locale.GetString("DocumentCheck.None")
                : string.Format(Locale.GetString(beforeExport ? "DocumentCheck.BeforeExport" : "DocumentCheck.Found"), problems.Count);
            content.Problems.ItemsSource = problems.Select(Row).ToList();
            content.Problems.Visibility = problems.Count == 0 ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
            var dialog = beforeExport
                ? AppContentDialog.Create(Locale.GetString("CheckDocument"), content, Locale.GetDialogString("Cancel"), Locale.GetString("DocumentCheck.ExportAnyway"))
                : AppContentDialog.Create(Locale.GetString("CheckDocument"), content, Locale.GetDialogString("Ok"));
            content.dialog = dialog;
            var result = await dialog.ShowAsync(viewModel.XamlRoot);
            if (content.chosen != null)
            {
                // The find bar on the wrong text: the place shown and marked, as a search finds it.
                var editor = viewModel.EditorViewModel;
                editor.SearchValue = viewModel.SettingsViewModel.SearchIsRegexp
                    ? System.Text.RegularExpressions.Regex.Escape(content.chosen.Find)
                    : content.chosen.Find;
                editor.FindCommand.Execute("search");
                return false;
            }
            return beforeExport && result == ContentDialogResult.Primary;
        }

        private static DocumentProblemRow Row(DocumentProblem problem)
        {
            var where = problem.Line > 0 ? string.Format(Locale.GetString("DocumentCheck.Line"), problem.Line) + " · " : "";
            return problem.Kind switch
            {
                DocumentProblemKind.MissingImage => new() { Problem = problem, Title = Locale.GetString("DocumentCheck.MissingImage"), Detail = where + problem.Target },
                DocumentProblemKind.MissingFile => new() { Problem = problem, Title = Locale.GetString("DocumentCheck.MissingFile"), Detail = where + problem.Target },
                DocumentProblemKind.MissingHeading => new() { Problem = problem, Title = Locale.GetString("DocumentCheck.MissingHeading"), Detail = where + problem.Target },
                _ => new()
                {
                    Problem = problem,
                    Title = Locale.GetString(problem.Target == "plantuml-off" ? "DocumentCheck.PlantUmlOff" : "DocumentCheck.NotDrawn"),
                    Detail = problem.Find,
                },
            };
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not DocumentProblemRow row || string.IsNullOrEmpty(row.Problem.Find)) return;
            chosen = row.Problem;
            // Close, not Hide: Hide only takes it off the screen, and ShowAsync (and every dialog after it) went on waiting.
            dialog?.Close();
        }
    }
}
