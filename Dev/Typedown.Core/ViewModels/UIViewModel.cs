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

        /// <summary>
        /// The cards at the window's bottom right, oldest first (MainPage draws them): an export written, the first edit's
        /// rewrite, a copy the clipboard never took, an edition's card (an update, the Store rating). One frame, one stack
        /// and one way of showing them (Services/Notices.cs) - each had its own copy of all of it (the user, 2026-11-23).
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<Notice> Notices { get; } = new();

        private readonly Dictionary<Notice, DispatcherTimer> noticeTimers = new();

        /// <summary>
        /// A card shown (one with the same key goes), and its line kept in the notices list when it is worth having there.
        /// <paramref name="popUp"/> false: only the line - a notice the reader turned off goes on being kept there.
        /// </summary>
        public void ShowNotice(Notice notice, bool popUp = true)
        {
            if (notice == null) return;
            if (notice.InHistory)
            {
                NoticeHistory.Add(new NoticeEntry
                {
                    Time = DateTime.Now,
                    Key = notice.Key,
                    Text = notice.HistoryText ?? notice.Text,
                    Where = FileViewModel?.FileName,
                    Actions = notice.HistoryActions ?? notice.Actions.Where(a => !a.CardOnly && !a.Link).ToList(),
                    Quiet = !popUp,
                });
            }
            if (!popUp) return;
            HideNotice(notice.Key);
            Notices.Add(notice);
            if (notice.Timeout is { } after)
            {
                var timer = new DispatcherTimer { Interval = after };
                timer.Tick += (_, _) => HideNotice(notice);
                noticeTimers[notice] = timer;
                timer.Start();
            }
        }

        /// <summary>The card with this key taken away, if one shows.</summary>
        public void HideNotice(string key)
        {
            var shown = Notices.FirstOrDefault(n => n.Key == key);
            if (shown != null) HideNotice(shown);
        }

        public void HideNotice(Notice notice)
        {
            if (notice == null || !Notices.Remove(notice)) return;
            if (noticeTimers.Remove(notice, out var timer)) timer.Stop();
            try { notice.Closed?.Invoke(); }
            catch (Exception ex) { Log.Debug($"notice {notice.Key}: closed: {ex.Message}"); }
        }

        public bool IsNoticeShown(string key) => Notices.Any(n => n.Key == key);

        /// <summary>The file an export just wrote, while its card shows (null: none).</summary>
        public string ExportedPath { get; private set; }

        /// <summary>A card that the export is written, with its place and buttons to open it; it goes by itself.</summary>
        public void ShowExported(string path)
        {
            ShowNotice(new Notice
            {
                Key = "export",
                AutomationId = "ExportNotice",
                Text = string.Format(Locale.GetString("ExportedTo"), path),
                Actions = new[]
                {
                    new NoticeAction { Text = Locale.GetString("Open"), AutomationId = "ExportNoticeOpen", Run = () => OpenExported(path, Common.OpenFile) },
                    new NoticeAction { Text = Locale.GetString("OpenFileLocation"), AutomationId = "ExportNoticeFolder", Run = () => OpenExported(path, Common.OpenFileLocation) },
                },
                Timeout = TimeSpan.FromSeconds(10),
                InHistory = true,
                Closed = () => { if (ExportedPath == path) ExportedPath = null; },
            });
            ExportedPath = path;
        }

        public void HideExported() => HideNotice("export");

        private async void OpenExported(string path, Action<string> open)
        {
            try { open(path); }
            catch (Exception ex)
            {
                Log.Debug($"export notice: could not open {path}: {ex.Message}");
                try { await Controls.AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel?.XamlRoot); } catch { }
            }
        }

        /// <summary>
        /// A copy (or cut) that never reached the clipboard - another program held it for the whole retry
        /// (Clipboard.WriteFailed): a card, not a dialog over the document. It goes by itself after ten seconds, as an
        /// export's (five were too few to be seen by one looking elsewhere).
        /// </summary>
        public void ShowClipboardNotice() => ShowNotice(new Notice
        {
            Key = "clipboard",
            AutomationId = "ClipboardNotice",
            Text = Locale.GetString("Clipboard.WriteFailed"),
            Timeout = TimeSpan.FromSeconds(10),
            InHistory = true,
        });

        public void HideClipboardNotice() => HideNotice("clipboard");

        /// <summary>
        /// The card that the first visual edit may rewrite part of the document shown (the page works out whether, and
        /// says so with FirstEditNotice): for that load only - taken away when another document is shown or this one
        /// closed. Its text while it shows, else null.
        /// </summary>
        public string FirstEditNotice { get; private set; }

        /// <summary>The load the notice is for (EditorViewModel.LoadId).</summary>
        public int FirstEditNoticeLoad { get; private set; }

        public void ShowFirstEditNotice(int loadId, IReadOnlyList<string> reasons = null)
        {
            var text = FirstEditNoticeText(reasons);
            // Turned off for every document (Settings > Editor) or for this one (the card's "Don't show again for this
            // document", Services/FirstEditMutes.cs): no card - only its line in the notices list (the user, 2026-11-23).
            var quiet = (SettingsViewModel != null && !SettingsViewModel.WarnFirstEditRewrite)
                || FirstEditMutes.Shared.IsMuted(AppViewModel?.FileViewModel?.FilePath, AppViewModel?.TabsViewModel?.ActiveTab?.DocumentId);
            FirstEditNoticeLoad = loadId;
            ShowNotice(new Notice
            {
                Key = "firstEdit",
                AutomationId = "FirstEditNotice",
                Text = text,
                Actions = new[] { new NoticeAction { Text = Locale.GetString("FirstEditNeverShow"), AutomationId = "FirstEditNoticeNever", Link = true, Run = MuteFirstEditNotice } },
                InHistory = true,
                Closed = () => { if (FirstEditNotice == text) FirstEditNotice = null; },
            }, popUp: !quiet);
            if (!quiet) FirstEditNotice = text;
        }

        // What the first visual edit may rewrite, by the page's reasons (services/normalization.ts), in the interface's
        // words for those parts. The notice said "raw HTML" whatever it was - a list numbered on had it say so for a
        // document with no HTML in it (the user, 2026-11-07).
        private static readonly (string Reason, string Key)[] notedParts =
        {
            ("html-lost", null), ("link-lost", "Hyperlink"), ("footnote-lost", "Footnote"), ("math-lost", "Math"),
            ("fence-lost", "CodeFences"), ("word-lost", "Paragraph"), ("task-state-changed", "TaskList"), ("front-matter-lost", "YAMLFrontMatter"),
        };

        public static string FirstEditNoticeText(IReadOnlyList<string> reasons)
        {
            var parts = notedParts.Where(p => reasons?.Contains(p.Reason) == true).Select(p => p.Key == null ? "HTML" : Locale.GetString(p.Key)).ToList();
            if (parts.Count == 0 || (parts.Count == 1 && reasons.Contains("html-lost"))) return Locale.GetString("FirstEditWarning");
            var language = Locale.Culture?.TwoLetterISOLanguageName ?? "en";
            var separator = language is "zh" or "ja" ? "、" : language == "ar" || language == "fa" || language == "ur" ? "، " : ", ";
            return string.Format(Locale.GetString("FirstEditWarningFor"), string.Join(separator, parts));
        }

        /// <summary>
        /// The card's "Don't show again for this document": that document only, by its path (an untitled one for as long
        /// as the app runs). The reader asked whether it was for this document (2026-11-07); Settings > Editor turns the
        /// notice off for every document. Its line still goes into the notices list.
        /// </summary>
        public void MuteFirstEditNotice()
        {
            FirstEditMutes.Shared.Mute(AppViewModel?.FileViewModel?.FilePath, AppViewModel?.TabsViewModel?.ActiveTab?.DocumentId);
            HideFirstEditNotice();
        }

        public void HideFirstEditNotice() => HideNotice("firstEdit");

        // An edition's cards (EditionHooks.ShowCard: an update, the Store rating) as this window's notices: one each,
        // stacked with the others - the edition's one card at a time took the place of the one there.
        private void SyncEditionCards()
        {
            var cards = EditionHooks.CardsOf(AppViewModel);
            foreach (var shown in Notices.Where(n => n.Key.StartsWith("card:") && !cards.Any(c => "card:" + c.Id == n.Key && ReferenceEquals(c.Content, n.Content))).ToList())
                HideNotice(shown);
            foreach (var card in cards)
            {
                if (Notices.Any(n => n.Key == "card:" + card.Id && ReferenceEquals(n.Content, card.Content))) continue;
                var id = card.Id;
                ShowNotice(new Notice
                {
                    Key = "card:" + id,
                    AutomationId = "EditionCard",
                    Content = card.Content,
                    Closable = false,
                    InHistory = card.HistoryText != null,
                    HistoryText = card.HistoryText,
                    HistoryActions = card.HistoryActions ?? Array.Empty<NoticeAction>(),
                    // Taken off the window some other way: the edition's card closed too (it is told).
                    Closed = () => EditionHooks.CloseCard(AppViewModel, id),
                });
            }
        }


        /// <summary>Folder-wide search pane in the side bar (Edit → Find → Search in folder, Ctrl+Shift+F).</summary>
        public bool FolderSearchOpen { get; set; }

        public Command<Unit> SearchInFolderCommand { get; } = new();

        /// <summary>
        /// A folder to show in the folder pane's tree (a folder the search pane found), until the folder page takes it:
        /// the page may not be there yet when it is asked for (the outline was shown).
        /// </summary>
        public string FolderRevealPath { get; private set; }

        /// <summary>
        /// The document shown to be shown in the folder pane's tree (a tab's Show in Side Bar), as the pane's "show the current
        /// file's folder" button does, until the folder page takes it.
        /// </summary>
        public bool FolderRevealActiveFile { get; private set; }

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
            ShowSidePane(0);
            FolderRevealPath = path;
        }

        /// <summary>The side bar opened on the Files page, and the document shown found in its tree (see <see cref="FolderRevealActiveFile"/>).</summary>
        public void RevealActiveFileInFolderTree()
        {
            DropFindResults();
            FolderSearchOpen = false;
            ShowSidePane(0);
            FolderRevealActiveFile = true;
        }

        /// <summary>
        /// A block of the side pane asked to be shown (0 the files, 1 the outline), until the pane takes it: the pane may
        /// not be there yet (it was closed). The block is opened out even if it had folded itself away (no folder open, a
        /// plain text without headings).
        /// </summary>
        public int? SidePaneBlockRequest { get; private set; }

        /// <summary>The side pane opened with its files (0) or outline (1) opened out.</summary>
        public void ShowSidePane(int page)
        {
            SettingsViewModel.SidePaneIndex = page;
            if (page == 1) SettingsViewModel.SidePaneOutlineCollapsed = false;
            else SettingsViewModel.SidePaneFilesCollapsed = false;
            SidePaneBlockRequest = page;
            SettingsViewModel.SidePaneOpen = true;
        }

        /// <summary>The block asked for, once: the pane opens it out and it is forgotten.</summary>
        public int? TakeSidePaneBlockRequest()
        {
            var page = SidePaneBlockRequest;
            if (page != null) SidePaneBlockRequest = null;
            return page;
        }

        /// <summary>Whether the document shown was asked for, once (the folder page shows it).</summary>
        public bool TakeFolderRevealActiveFile()
        {
            var asked = FolderRevealActiveFile;
            if (asked) FolderRevealActiveFile = false;
            return asked;
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
            void OnCardChanged(AppViewModel app)
            {
                if (app != AppViewModel) return;
                if (dispatcher.HasThreadAccess) SyncEditionCards();
                else dispatcher.TryEnqueueSafe(SyncEditionCards);
            }
            EditionHooks.CardChanged += OnCardChanged;
            disposables.Add(Disposable.Create(() => EditionHooks.CardChanged -= OnCardChanged));
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
