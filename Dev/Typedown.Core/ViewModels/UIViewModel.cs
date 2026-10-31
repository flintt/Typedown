using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml;

namespace Typedown.Core.ViewModels
{
    public sealed partial class UIViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public EditorViewModel EditorViewModel => ServiceProvider.GetService<EditorViewModel>();

        public FileViewModel FileViewModel => ServiceProvider.GetService<FileViewModel>();

        public SettingsViewModel SettingsViewModel => ServiceProvider.GetService<SettingsViewModel>();

        public RemoteInvoke RemoteInvoke => ServiceProvider.GetService<RemoteInvoke>();

        public string MainWindowTitle { get; private set; }

        /// <summary>A local automation client is connected (Typedown.Services.Automation): the title says so.</summary>
        public bool AutomationConnected { get; set; }

        /// <summary>Briefly, which client just changed a document in this window; shown in the title.</summary>
        public string AutomationNotice { get; set; }

        public ElementTheme ActualTheme { get; private set; }

        public double CaptionHeight { get; set; } = 32;

        public bool IsPrintPreviewOpen { get; private set; }

        /// <summary>The file an export just wrote, while its notice shows (null: no notice).</summary>
        public string ExportedPath { get; private set; }

        public bool ExportedNoticeOpen => ExportedPath != null;

        public string ExportedNotice => ExportedPath == null ? null : string.Format(Locale.GetString("ExportedTo"), ExportedPath);

        private DispatcherTimer exportedTimer;

        /// <summary>A notice that the export is written, with its place and buttons to open it; it goes by itself.</summary>
        public void ShowExported(string path)
        {
            ExportedPath = path;
            exportedTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            exportedTimer.Tick -= OnExportedTimerTick;
            exportedTimer.Tick += OnExportedTimerTick;
            exportedTimer.Stop();
            exportedTimer.Start();
        }

        public void HideExported()
        {
            exportedTimer?.Stop();
            ExportedPath = null;
        }

        private void OnExportedTimerTick(object sender, object e) => HideExported();

        /// <summary>
        /// The notice that the first visual edit may rewrite part of the document shown (the page works out whether, and
        /// says so with FirstEditNotice): shown as a card over the window like the export's, for that load only - taken
        /// away when another document is shown or this one closed. Null: no notice. The page takes it away after 10 s.
        /// </summary>
        public string FirstEditNotice { get; private set; }

        public bool FirstEditNoticeOpen => FirstEditNotice != null;

        /// <summary>The load the notice is for (EditorViewModel.LoadId).</summary>
        public int FirstEditNoticeLoad { get; private set; }

        public void ShowFirstEditNotice(int loadId)
        {
            FirstEditNoticeLoad = loadId;
            FirstEditNotice = Locale.GetString("FirstEditWarning");
        }

        public void HideFirstEditNotice() => FirstEditNotice = null;


        /// <summary>Folder-wide search pane in the side bar (Edit → Find → Search in folder, Ctrl+Shift+F).</summary>
        public bool FolderSearchOpen { get; set; }

        public Command<Unit> SearchInFolderCommand { get; } = new();

        /// <summary>
        /// A folder to show in the folder pane's tree (a folder the search pane found), until the folder page takes it:
        /// the page may not be there yet when it is asked for (the outline was shown).
        /// </summary>
        public string FolderRevealPath { get; private set; }

        /// <summary>
        /// The find bar's results list ("Search in document") is shown in the side pane, over the page it was on (Files,
        /// Outline, the folder search). Opened by the bar's "Show all results"; closed by its own close button, Esc, or the
        /// bar closing (the list is the bar's search: without the bar there is nothing to step through or follow).
        /// </summary>
        public bool FindResultsOpen { get; private set; }

        /// <summary>Raised when the list opens (or is asked for again while open): the list takes the keyboard.</summary>
        public event Action FindResultsFocusRequested;

        private readonly SidePaneReturn findResultsReturn = new();

        // The pane closed by the list itself (back to closed): not the reader closing it under the list.
        private bool restoringSidePane;

        /// <summary>The results list shown, the side pane opened for it if it was closed; the page asked for the list.</summary>
        public void ShowFindResults()
        {
            findResultsReturn.Open(SettingsViewModel.SidePaneOpen, SettingsViewModel.SidePaneIndex);
            SettingsViewModel.SidePaneOpen = true;
            if (!FindResultsOpen)
            {
                FindResultsOpen = true;
                ServiceProvider.GetService<Interfaces.IMarkdownEditor>()?.PostMessage("FindResultsOpen", new { open = true });
            }
            FindResultsFocusRequested?.Invoke();
        }

        /// <summary>The results list closed: the side pane back to the page it was on, or closed again if it was closed.</summary>
        public void CloseFindResults()
        {
            if (!FindResultsOpen) return;
            EndFindResults();
            if (findResultsReturn.Close() is not { } was) return;
            restoringSidePane = true;
            try
            {
                SettingsViewModel.SidePaneIndex = was.Page;
                SettingsViewModel.SidePaneOpen = was.Open;
            }
            finally
            {
                restoringSidePane = false;
            }
        }

        /// <summary>The list goes and the pane stays as the reader has made it (they closed it, or opened the folder search).</summary>
        private void DropFindResults()
        {
            if (!FindResultsOpen) return;
            findResultsReturn.Forget();
            EndFindResults();
        }

        private void EndFindResults()
        {
            FindResultsOpen = false;
            ServiceProvider.GetService<Interfaces.IMarkdownEditor>()?.PostMessage("FindResultsOpen", new { open = false });
        }

        /// <summary>The search pane closed, the Files page shown, and <paramref name="path"/> opened and selected in its tree. The root stays.</summary>
        public void RevealInFolderTree(string path)
        {
            DropFindResults();
            FolderSearchOpen = false;
            SettingsViewModel.SidePaneOpen = true;
            SettingsViewModel.SidePaneIndex = 0;
            FolderRevealPath = path;
        }

        /// <summary>The folder asked for, once: the folder page shows it and it is forgotten.</summary>
        public string TakeFolderRevealPath()
        {
            var path = FolderRevealPath;
            if (path != null) FolderRevealPath = null;
            return path;
        }

        /// <summary>Borderless full-screen window without the caption/menu bar (F11).</summary>

        public bool IsFullScreen { get; set; }

        public object PrintPreviewContent { get; private set; }

        public string PrintPreviewTitle { get; private set; }

        private readonly CompositeDisposable disposables = new();

        private readonly UISettings uiSettings = new();

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;

        public UIViewModel(IServiceProvider serviceProvider)
        {
            // The window thread it is made on (each window has its own).
            dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ServiceProvider = serviceProvider;
            SearchInFolderCommand.OnExecute.Subscribe(_ =>
            {
                DropFindResults();
                SettingsViewModel.SidePaneOpen = true;
                FolderSearchOpen = true;
            });
            disposables.Add(RemoteInvoke.Handle<JToken, object>("GetStringResources", GetStringResources));
            _ = dispatcher.RunIdleAsync(() => InitializeBinding());
        }

        private void InitializeBinding()
        {
            if (disposables.IsDisposed)
                return;
            disposables.Add(EditorViewModel.WhenPropertyChanged(nameof(EditorViewModel.DisplaySaved)).Subscribe(_ => UpdateTitle()));
            disposables.Add(FileViewModel.WhenPropertyChanged(nameof(FileViewModel.FileName)).Subscribe(_ => UpdateTitle()));
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.ReadOnly)).Subscribe(_ => UpdateTitle()));
            disposables.Add(this.WhenPropertyChanged(nameof(AutomationConnected)).Subscribe(_ => UpdateTitle()));
            disposables.Add(this.WhenPropertyChanged(nameof(AutomationNotice)).Subscribe(_ => UpdateTitle()));
            // The reader closed the side pane under the results list: the list goes with it, and nothing is put back later.
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.SidePaneOpen)).Subscribe(_ =>
            {
                if (!SettingsViewModel.SidePaneOpen && !restoringSidePane) DropFindResults();
            }));
            // The title carries a translated word now, so it has to be rebuilt when the language changes —
            // otherwise it keeps the old one while the menus around it have already changed.
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.Language)).Subscribe(_ => UpdateTitle()));
            disposables.Add(uiSettings.GetColorValuesObservable()
                .Merge(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AppTheme)))
                .Merge(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.CustomTheme)))
                .Subscribe(_ => UpdateActualTheme()));
            UpdateTitle();
            UpdateActualTheme();
        }

        private object GetStringResources(JToken args)
        {
            try
            {
                return args["names"].ToObject<List<string>>().ToDictionary(x => x, x => Locale.GetString(x));
            }
            catch (Exception ex)
            {
                // The page then shows its placeholders and notices without text: say why.
                Log.WriteLocal("GetStringResources", ex.ToString());
                return new Dictionary<string, string>();
            }
        }

        private void UpdateActualTheme()
        {
            // Not at idle priority: with a large document rendering or a folder being walked, an idle callback
            // waits for the thread to go quiet, and the window sits in the old theme until it does.
            _ = dispatcher?.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    var theme = SettingsViewModel.EffectiveTheme;
                    if (theme == Enums.AppTheme.Default)
                        ActualTheme = Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                    else
                        ActualTheme = theme == Enums.AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                }
                catch
                {
                    // Ignore
                }
            });
        }

        /// <summary>
        /// The test host's title says it is one, and that a client is connected; screenshots taken with it (the
        /// website) turn both off (test.app.titleMarker). Only ever read by the test host.
        /// </summary>
        public static bool ShowTestHostMarker { get; set; } = true;

        /// <summary>
        /// The test host only: the E2E case running now and what it checks ("MN02 ..."), shown after the test host's
        /// marker in every window's title, so whoever watches the screen sees what should be happening.
        /// </summary>
        public static string TestCaseLabel { get; set; }

        /// <summary>The title worked out again (after <see cref="ShowTestHostMarker"/> changed).</summary>
        public void RefreshTitle() => UpdateTitle();

        private void UpdateTitle()
        {
            try
            {
                var title = new StringBuilder();
                if (!AppViewModel.EditorViewModel.DisplaySaved)
                    title.Append('*');
                if (AppViewModel.FileViewModel.FileName != null)
                    title.Append(AppViewModel.FileViewModel.FileName + " - ");
                title.Append(Config.AppName);
                if (Config.IsAutomationTestHost && ShowTestHostMarker)
                    title.Insert(0, "AUTOMATION TEST HOST \u00b7 " + (string.IsNullOrEmpty(TestCaseLabel) ? "" : TestCaseLabel + " \u00b7 "));
                // Reading mode swallows every keystroke, and without a word about it that looks like the editor
                // has stopped responding. The status bar says so too, but it can be switched off — the title
                // cannot, and it is what the taskbar shows.
                if (SettingsViewModel.ReadOnly)
                    title.Append(" \u00b7 " + Locale.GetString("ReadOnlyMode"));
                // Automation is visible in the title for the same reason: the status bar can be switched off, the
                // title cannot, and no client can clear it - except in the test host taking screenshots, whose own
                // driver is the client (test.app.titleMarker).
                if (Config.IsAutomationTestHost && !ShowTestHostMarker) { }
                else if (!string.IsNullOrEmpty(AutomationNotice))
                    title.Append(" \u00b7 " + AutomationNotice);
                else if (AutomationConnected)
                    title.Append(" \u00b7 " + Locale.GetString("AutomationConnected"));
                MainWindowTitle = title.ToString();
            }
            catch
            {
                // Ignore
            }
        }

        public void ShowPrintPreview(object content, string title = null)
        {
            ClosePrintPreview();
            PrintPreviewTitle = title;
            PrintPreviewContent = content;
            IsPrintPreviewOpen = true;
        }

        public void ClosePrintPreview()
        {
            IsPrintPreviewOpen = false;
            var oldContent = PrintPreviewContent;
            PrintPreviewContent = null;
            PrintPreviewTitle = null;
            (oldContent as IDisposable)?.Dispose();
        }

        public void Dispose()
        {
            ClosePrintPreview();
            disposables.Dispose();
        }
    }
}
