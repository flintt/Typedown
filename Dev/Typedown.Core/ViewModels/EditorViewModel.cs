using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Linq;
using System.Threading.Tasks;
using Typedown.Core.Controls;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Models.RuntimeModels;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.Core.ViewModels
{
    public sealed partial class EditorViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();
        public FileViewModel FileViewModel => ServiceProvider.GetService<FileViewModel>();
        public FloatViewModel FloatViewModel => ServiceProvider.GetService<FloatViewModel>();
        public FormatViewModel FormatViewModel => ServiceProvider.GetService<FormatViewModel>();
        public SettingsViewModel Settings => ServiceProvider.GetService<SettingsViewModel>();
        public EventCenter EventCenter => ServiceProvider.GetService<EventCenter>();
        public RemoteInvoke RemoteInvoke => ServiceProvider.GetService<RemoteInvoke>();

        public JToken Selection { get; set; }
        public JToken CodeMirrorSelection { get; set; }
        public ContentState ContentState { get; set; }
        public MenuState MenuState { get; set; }
        public ParagraphState ParagraphState { get; set; }
        /// <summary>
        /// The outline tree. Replaced whole when a different document arrives, updated in place when the
        /// same document changes: the tree control is bound to it, and taking a document's outline apart
        /// row by row under that control while a second document's arrived is what crashed the process —
        /// a fail-fast inside Windows.UI.Xaml from the collection-changed handler, in the dump, and a stack
        /// overflow there before that. One document's rows are never removed one at a time any more.
        /// </summary>
        public TocTreeItem Toc { get; private set; } = new();

        /// <summary>The load whose outline <see cref="Toc"/> holds.</summary>
        private int tocLoadId = -1;
        public ContentHistory History { get; set; } = new();

        // A fresh editor counts as a pristine untitled document (see TabsViewModel.IsActiveTabBlank), otherwise the
        // first file opened at startup would land in a second tab next to an empty one.
        public string Markdown { get; set; } = Common.DefaultMarkdwn;
        public bool Selected { get; set; }
        public string SelectionText { get; set; }
        public bool TextSelected { get; set; }
        public bool Saved { get; set; } = true;
        public bool AutoSavedSucc { get; set; } = true;
        public bool DisplaySaved { get; set; } = true;
        public ulong FileHash { get; set; } = Common.SimpleHash(Common.DefaultMarkdwn);
        public ulong CurrentHash { get; set; } = Common.SimpleHash(Common.DefaultMarkdwn);

        /// <summary>
        /// Bumped on every content load pushed to the editor and echoed back on its reports; a report carrying an
        /// older id belongs to the previous document (in flight during a tab switch) and is dropped.
        /// </summary>
        public int LoadId { get; private set; }
        public string SearchValue { get; set; } = null;
        public bool FirstStart { get; set; } = true;
        public bool FileLoaded { get; set; }

        public Command<Unit> UndoCommand { get; } = new();
        public Command<Unit> RedoCommand { get; } = new();
        public Command<string> CutCommand { get; } = new();
        public Command<string> PasteCommand { get; } = new();
        public Command<string> CopyCommand { get; } = new();
        public Command<Unit> DeleteSelectionCommand { get; } = new();
        public Command<Unit> SelectAllCommand { get; } = new();
        public Command<string> FindCommand { get; } = new();

        public IMarkdownEditor MarkdownEditor => ServiceProvider.GetService<IMarkdownEditor>();
        public IClipboard Clipboard => ServiceProvider.GetService<IClipboard>();
        public AutoBackup AutoBackup => ServiceProvider.GetService<AutoBackup>();

        private readonly CompositeDisposable disposables = new();

        private bool contentUpdating = false;

        public EditorViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            EventCenter.GetObservable<EditorEventArgs>("MarkdownChange").Subscribe(x => OnMarkdownChange(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("ContentFlushed").Subscribe(x => OnContentFlushed(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("FileLoaded").Subscribe(x => OnFileLoaded(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("DocumentEditApplied").Subscribe(x => OnDocumentEditApplied(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("NormalizationReport").Subscribe(x => OnNormalizationReport(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("PresentationFrames").Subscribe(x => { var t = x.Args?["token"]?.Value<int?>() ?? 0; if (framesWaiters.TryGetValue(t, out var w)) w.TrySetResult(true); });
            EventCenter.GetObservable<EditorEventArgs>("EditorStyle").Subscribe(x => { var t = x.Args?["token"]?.Value<int?>() ?? 0; if (styleWaiters.TryGetValue(t, out var w)) w.TrySetResult(x.Args); });
            EventCenter.GetObservable<EditorEventArgs>("CursorChange").Subscribe(x => OnCursorChange(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("OnScroll").Subscribe(x => OnScroll(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("SelectionChange").Subscribe(x => OnSelectionChange(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("CodeMirrorSelectionChange").Subscribe(x => OnCodeMirrorSelectionChange(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("StateChange").Subscribe(x => OnStateChange(x.Args));
            EventCenter.GetObservable<EditorEventArgs>("OutlineCurrent").Subscribe(x => OnOutlineCurrent(x.Args));
            // What the page's scroll hold saw after a load: asked for, ended at, why it stopped, and the viewport
            // it started with — a viewport of no height is a page loaded before its window was there.
            EventCenter.GetObservable<EditorEventArgs>("ScrollSettled").Subscribe(x => Log.Debug($"settle: {x.Args}"));
            RemoteInvoke.Handle("GetSettings", GetSettings);
            RemoteInvoke.Handle<JToken>("SetClipboard", OnSetClipboard);
            Settings.WhenPropertyChanged(nameof(Settings.AutoSave)).Subscribe(_ => Settings_AutoSaveChanged(Settings.AutoSave));
            // The editor reads the theme as "themeCss"; the setting only holds the file name.
            Settings.WhenPropertyChanged(nameof(Settings.CustomTheme)).Subscribe(_ =>
                MarkdownEditor?.PostMessage("SettingsChanged", new Dictionary<string, object>() { { "themeCss", ThemeFiles.Read(Settings.CustomTheme) } }));
            this.WhenPropertyChanged(nameof(SearchValue)).Subscribe(_ => SearchValueChanged());
            this.WhenPropertyChanged(nameof(Saved)).Subscribe(_ => SavedOrAutoSavedSuccChanged());
            this.WhenPropertyChanged(nameof(AutoSavedSucc)).Subscribe(_ => SavedOrAutoSavedSuccChanged());
            UndoCommand.OnExecute.Subscribe(_ => Undo());
            RedoCommand.OnExecute.Subscribe(_ => Redo());
            FindCommand.OnExecute.Subscribe(x => Find(x));
            PasteCommand.OnExecute.Subscribe(x => Paste(x));
            CutCommand.OnExecute.Subscribe(x => Cut(x));
            CopyCommand.OnExecute.Subscribe(x => Copy(x));
            DeleteSelectionCommand.OnExecute.Subscribe(_ => DeleteSelection());
            SelectAllCommand.OnExecute.Subscribe(_ => SelectAll());
        }

        private readonly TaskCompletionSource<bool> startupDocumentReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once the page has asked for its settings and the window's startup document is in place. Until then
        /// the startup flow may still replace the active document (a new window's blank one), so automation waits.
        /// </summary>
        public Task StartupDocumentReady => startupDocumentReady.Task;

        public async Task<object> GetSettings()
        {
            awaitingNewPage = false;
            if (FirstStart)
            {
                FirstStart = false;
                await FileViewModel.LoadStartUpMarkdown();
            }
            startupDocumentReady.TrySetResult(true);
            return new
            {
                Settings.FocusMode,
                Settings.Typewriter,
                Settings.SourceCode,
                Settings.FontSize,
                Settings.LineHeight,
                Settings.AutoPairBracket,
                Settings.AutoPairQuote,
                Settings.TrimUnnecessaryCodeBlockEmptyLines,
                Settings.PreferLooseListItem,
                Settings.ListIndentation,
                Settings.TableAlignColumns,
                Settings.ShowParagraphMarker,
                Settings.ReadOnly,
                Settings.CustomCss,
                ThemeCss = ThemeFiles.Read(Settings.CustomTheme),
                Settings.SpellcheckEnabled,
                Settings.AutoPairMarkdownSyntax,
                Settings.EditorAreaWidth,
                Settings.FontFamily,
                Settings.TextDirection,
                Settings.TabSize,
                Settings.HighlightAutomationChanges,
                Markdown,
                BasePath = FileViewModel.ImageBasePath,
                Cursor = Settings.RememberCursorPosition ? CursorMemory.Get(FileViewModel.FilePath) : null,
                ScrollTop = Settings.RememberCursorPosition ? CursorMemory.GetScroll(FileViewModel.FilePath) : null,
                LoadId = ++LoadId,
            };
        }

        /// <summary>Pushes a whole document into the editor (see <see cref="LoadId"/>).</summary>
        public void PostLoadFile(string text, object cursor = null)
        {
            var scrollTop = Settings.RememberCursorPosition ? CursorMemory.GetScroll(FileViewModel.FilePath) : null;
            loadClock = text != null && text.Length > 200000 ? System.Diagnostics.Stopwatch.StartNew() : null;
            MarkdownEditor?.PostMessage("LoadFile", new { text, basePath = FileViewModel.ImageBasePath, cursor, scrollTop, loadId = ++LoadId });
        }

        // Scroll offset per file: reading mode has no caret, so this is what brings it back to the same place.
        private double lastLoggedScroll = double.NaN;

        /// <summary>
        /// True while the web view is out of the window: leaving the settings page rebuilds the main page,
        /// and the editor container lets go of the view and takes it back. The browser reports a scroll to
        /// the top in between, which is not the reader moving and must not be remembered.
        /// </summary>
        public bool EditorDetached { get; set; }

        /// <summary>The last offset the page reported while it was in the window.</summary>
        public double LastScrollY { get; private set; }

        /// <summary>Puts the page back where it was before the web view was taken out of the window.</summary>
        public void RestoreScroll()
        {
            if (LastScrollY > 0) MarkdownEditor?.PostMessage("RestoreScroll", new { y = LastScrollY });
        }

        private void OnScroll(JToken arg)
        {
            if (!FileLoaded || string.IsNullOrEmpty(FileViewModel.FilePath)) return;
            var scrollY = arg["scrollY"]?.Value<double?>();
            if (scrollY == null) return;
            if (EditorDetached) return;
            // Only the page showing the current load knows where the reader is. A report without a load id is
            // a page that has just been navigated and has no document yet; one with another id is the page
            // that was, still talking. Either recorded a 0 that the next load then came back to.
            var id = arg["loadId"];
            if (id == null || id.Type != JTokenType.Integer || id.Value<int>() != LoadId) return;
            LastScrollY = scrollY.Value;
            CursorMemory.SetScroll(FileViewModel.FilePath, scrollY.Value);
            // Every 500px, not every event: enough to see in the log that the page moved, and how far.
            if (double.IsNaN(lastLoggedScroll) || Math.Abs(scrollY.Value - lastLoggedScroll) >= 500)
            {
                lastLoggedScroll = scrollY.Value;
                Log.Debug($"scroll: {scrollY.Value:0}");
            }
        }

        private bool IsStaleReport(JToken arg)
        {
            var id = arg?["loadId"];
            if (id == null || id.Type != JTokenType.Integer || id.Value<int>() == LoadId) return false;
            Log.Debug($"Editor report dropped: loadId={id} current={LoadId}");
            return true;
        }

        public void OnSelectionChange(JToken arg)
        {
            Selection = arg["selection"];
            MenuState = arg["menuState"].ToObject<MenuState>();
            SelectionText = arg["selectionText"].ToString();
            ParagraphState = new ParagraphState(MenuState);
            UpdateMuyaSelected();
        }

        public void OnCodeMirrorSelectionChange(JToken arg)
        {
            contentUpdating = false;
            CodeMirrorSelection = arg["cursor"];
            var anchorLine = CodeMirrorSelection["anchor"]["line"].ToObject<int>();
            var headLine = CodeMirrorSelection["head"]["line"].ToObject<int>();
            var anchorCh = CodeMirrorSelection["anchor"]["ch"].ToObject<int>();
            var headCh = CodeMirrorSelection["head"]["ch"].ToObject<int>();
            TextSelected = Selected = anchorLine != headLine || anchorCh != headCh;
            SelectionText = arg["selectionText"].ToString();
        }

        public void UpdateMuyaSelected()
        {
            var formatViewModel = ServiceProvider.GetService<FormatViewModel>();
            TextSelected = Selection["start"]["offset"].ToString() != Selection["end"]["offset"].ToString();
            Selected = TextSelected || formatViewModel.FormatState.Image;
        }

        public async void OnMarkdownChange(string markdown)
        {
            if (!string.Equals(Markdown, markdown, StringComparison.Ordinal))
                ServiceProvider.GetService<TabsViewModel>()?.ActiveTab?.NoteTextChanged();
            Markdown = markdown;
            if (!contentUpdating) History.ContentChange(Markdown);
            CurrentHash = Common.SimpleHash(Markdown);
            if (!FileLoaded) await Task.Delay(100);
            var saved = FileHash == CurrentHash;
            // Mode switches preserve the source in the editor. A changed hash is an actual
            // edit, including the first report after a switch, and must remain unsaved.
            if (Saved && !saved && !History.Undoable && !History.IsPending)
                Log.Debug($"MarkdownChange: became unsaved without an undoable edit (len={markdown.Length} fileHash={FileHash} currentHash={CurrentHash} loaded={FileLoaded} loadId={LoadId})");
            Saved = saved;
        }

        private System.Diagnostics.Stopwatch loadClock;

        public void OnFileLoaded(JToken arg)
        {
            if (IsStaleReport(arg)) return;
            // The page being replaced still answers a load sent while it goes (the file opened meanwhile); only the
            // new page, once it has asked for its settings, can confirm what it shows.
            if (awaitingNewPage) Log.Debug($"editor: load {LoadId} confirmed by the page being replaced, not counted");
            else ConfirmedLoadId = LoadId;
            CompleteReloadWaiter(arg);
            if (loadClock != null)
            {
                Log.Debug($"FileLoaded after {loadClock.ElapsedMilliseconds} ms in the editor");
                loadClock = null;
            }
            if (!FileLoaded)
            {
                FileLoaded = true;
                var newText = arg["text"].ToString();
                var hash = Common.SimpleHash(newText);
                if (hash != FileHash)
                    Log.Debug($"FileLoaded: editor normalized the text (len {Markdown.Length} -> {newText.Length}, loadId={LoadId})");
                FileHash = hash;
                History.InitHistory(newText);
                OnMarkdownChange(newText);
                if (FloatViewModel.FindReplaceDialogOpen > 0)
                    OnSearch();
            }
        }

        public void OnMarkdownChange(JToken arg)
        {
            if (IsStaleReport(arg)) return;
            var text = arg["text"].ToString();
            if (holdingReports)
            {
                // An automation edit is applied in the page and not yet committed here: the reader's typing on top
                // of it waits until the commit, so it lands after the edit instead of being overwritten by it.
                heldReport = text;
                return;
            }
            OnMarkdownChange(text);
        }

        private bool holdingReports;
        private string heldReport;

        /// <summary>From the moment the page is handed an automation edit until the host commits or restores.</summary>
        public void HoldEditorReports()
        {
            holdingReports = true;
            heldReport = null;
        }

        /// <summary>
        /// Ends <see cref="HoldEditorReports"/>. After a commit the held typing is applied as the next change; after a
        /// restore it is dropped - it was typed on the edit that was rolled back.
        /// </summary>
        public void ReleaseEditorReports(bool apply)
        {
            holdingReports = false;
            var text = heldReport;
            heldReport = null;
            if (apply && text != null) OnMarkdownChange(text);
        }

        private readonly Dictionary<int, TaskCompletionSource<string>> pageTextWaiters = new();

        /// <summary>The text the page holds right now (for the automation test host's test.editor.pageText).</summary>
        public async Task<string> ReadPageTextAsync(int timeoutMs)
        {
            if (MarkdownEditor == null) return null;
            var token = ++flushToken;
            var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pageTextWaiters) pageTextWaiters[token] = waiter;
            try
            {
                MarkdownEditor.PostMessage("FlushContent", new { token });
                var finished = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                return finished == waiter.Task ? waiter.Task.Result : null;
            }
            finally
            {
                lock (pageTextWaiters) pageTextWaiters.Remove(token);
            }
        }

        public CursorState CurrentCursor { get; internal set; }

        public void OnCursorChange(JToken arg)
        {
            if (IsStaleReport(arg)) return;
            var cursor = arg["cursor"]?.ToObject<CursorState>();
            History.CursorChange(cursor);
            if (cursor != null)
            {
                CurrentCursor = cursor;
                if (FileLoaded && !string.IsNullOrEmpty(FileViewModel.FilePath))
                    CursorMemory.Set(FileViewModel.FilePath, cursor);
            }
        }

        /// <summary>
        /// The heading the editor last said it is at. Selecting it is this class echoing the editor back, not
        /// somebody choosing it, and jumping there would send the editor a scroll it did not ask for — which
        /// in reading mode, where the outline follows the page, is a loop: the page scrolls, reports a
        /// heading, gets scrolled to it, reports again. The page then twitches with nobody touching it.
        /// </summary>
        private string appliedCurSlug;

        /// <summary>The heading the outline currently marks, so a selection landing on it again is not a jump.</summary>
        public string AppliedCurSlug => appliedCurSlug;

        /// <summary>
        /// True while the outline is being rebuilt from a report. The tree is bound to the collection being
        /// rewritten, and its own selection writes back into these items as it goes — selections this class
        /// caused, not ones anybody chose. Jumping on them scrolled the document away and re-entered the
        /// rebuild, which is what crashed when two tabs were switched between quickly.
        /// </summary>
        private bool rebuildingToc;

        /// <summary>A heading the page reported before there was an outline to mark it in.</summary>
        private string pendingOutlineSlug;
        private JToken pendingOutlineWhere;

        /// <summary>
        /// Raised with the slug of the heading just marked in the outline. The outline pane brings that row
        /// into view: with the page following the reader, the mark ran off the bottom of the pane about
        /// two-thirds of the way through a fifty-heading report and looked as if it had disappeared.
        /// </summary>
        public event Action<string> OutlineHighlighted;

        public void OnStateChange(JToken arg)
        {
            // Cleared whatever the report describes: it means the editor has answered, and leaving it set
            // would keep the next real edit out of the undo history.
            contentUpdating = false;
            // A report describes the document that was loaded when it was made. Switch tabs quickly and the
            // previous document's report arrives after the new one is open: its outline is of a document no
            // longer on screen, and every heading in it is a place the editor cannot go.
            if (IsStaleReport(arg)) return;
            ContentState = arg["state"].ToObject<ContentState>();
            rebuildingToc = true;
            try
            {
                if (ContentState.Cur == null)
                {
                    // Nothing is highlighted when the editor reports no current heading — a selection that
                    // spans two sections does that on purpose, but seeing it any other time is the bug.
                    Log.Debug($"outline: no current heading in a report of {ContentState.Toc?.Count ?? 0} entries");
                }
                if (ContentState.Cur != null)
                {
                    appliedCurSlug = ContentState.Cur.Slug;
                    if (ContentState.Toc.All(x => x.Slug != ContentState.Cur.Slug))
                        Log.Debug($"outline: current heading {ContentState.Cur.Slug} is not among the {ContentState.Toc.Count} entries reported with it");
                    // The mark on the model is for ExpandToSelected; the row itself is marked by the pane, through
                    // the tree's own selection, when OutlineHighlighted is raised below.
                    ContentState.Toc.ForEach(x => x.IsSelected = x.Slug == ContentState.Cur.Slug);
                }
                if (tocLoadId == LoadId)
                {
                    Toc.UpdateChildren(ContentState.Toc);
                }
                else
                {
                    // Another document: build its tree away from the control, then hand it over in one step.
                    var fresh = new TocTreeItem();
                    fresh.UpdateChildren(ContentState.Toc);
                    tocLoadId = LoadId;
                    Toc = fresh;
                }
                if (Settings.TocAutoExpand)
                    Toc.ExpandToSelected();
            }
            finally
            {
                rebuildingToc = false;
            }
            if (ContentState.Cur != null && pendingOutlineSlug == null)
                OutlineHighlighted?.Invoke(ContentState.Cur.Slug);
            if (pendingOutlineSlug != null)
            {
                var slug = pendingOutlineSlug;
                var where = pendingOutlineWhere;
                pendingOutlineSlug = null;
                pendingOutlineWhere = null;
                OnOutlineCurrent(new JObject { ["slug"] = slug, ["where"] = where, ["loadId"] = LoadId });
            }
        }

        /// <summary>
        /// Which heading the reader has scrolled to. Reading mode has no caret for the outline to follow, so
        /// the editor says where the page is instead — on a message of its own, and this is the whole reason
        /// for that: `cur` in a state report is what <see cref="JumpBySlug"/> decides from, so a heading
        /// arriving that way would be scrolled to, which moves the page, which reports another heading. The
        /// first attempt did exactly that and the page twitched with nobody touching it. Nothing here scrolls.
        /// </summary>
        public void OnOutlineCurrent(JToken arg)
        {
            if (IsStaleReport(arg)) return;
            var slug = arg["slug"]?.ToString();
            if (string.IsNullOrEmpty(slug)) return;
            if (ContentState?.Toc == null)
            {
                // The page reports where it is as soon as it has laid the document out, which is before the
                // first state report has reached here — there is no outline yet to mark. Dropping it left the
                // highlight blank until the reader happened to scroll to a different heading, because the
                // page only reports a heading when it changes. Keep it for the outline to arrive.
                pendingOutlineSlug = slug;
                pendingOutlineWhere = arg["where"];
                Log.Debug($"outline: heading {slug} arrived before the outline; kept for when it does");
                return;
            }
            if (appliedCurSlug == slug) return;
            var match = ContentState.Toc.FirstOrDefault(x => x.Slug == slug);
            var where = arg["where"];
            var at = where == null ? "" : where["jump"] != null ? $" [jumped to it, scrollY={where["y"]}]" : $" [scrollY={where["y"]}, heading {where["index"]}/{where["of"]} at {where["top"]}]";
            if (match == null)
            {
                // The document was rebuilt (a reload, a switch back to this tab) and the page named a heading
                // by its new key before the outline with the new keys got here. Marking "none of these" left
                // the outline blank; the report is kept for the outline that is on its way, like one that
                // arrives before any outline at all.
                pendingOutlineSlug = slug;
                pendingOutlineWhere = where;
                Log.Debug($"outline: heading {slug} is not among the {ContentState.Toc.Count} entries{at}; kept for the next outline");
                return;
            }
            appliedCurSlug = slug;
            Log.Debug($"outline: now on {match.Content}{at}");
            rebuildingToc = true;
            try
            {
                foreach (var item in ContentState.Toc)
                    item.IsSelected = item.Slug == slug;
                if (Settings.TocAutoExpand)
                    Toc.ExpandToSelected();
            }
            finally
            {
                rebuildingToc = false;
            }
            OutlineHighlighted?.Invoke(slug);
        }

        public void OnSearch()
        {
            if (string.IsNullOrEmpty(SearchValue))
                SearchValue = null;
            MarkdownEditor?.PostMessage("Search", new
            {
                value = SearchValue,
                opt = new
                {
                    searchIsCaseSensitive = Settings.SearchIsCaseSensitive,
                    searchIsWholeWord = Settings.SearchIsWholeWord,
                    searchIsRegexp = Settings.SearchIsRegexp,
                    selection = Settings.SourceCode ? CodeMirrorSelection : Selection
                }
            });
        }

        // Reporting the text is throttled in the editor while typing, so anything that reads Markdown as the
        // document — saving, exporting, sharing — asks for it to be brought up to date first. The wait is
        // bounded: if the page does not answer, what we already hold is still written rather than nothing.
        private int flushToken;
        private readonly Dictionary<int, TaskCompletionSource<bool>> flushWaiters = new();

        /// <summary>
        /// Brings the editor's latest text in. Returns true when the editor answered (or there was nothing to
        /// flush), false when it did not answer within <paramref name="timeoutMs"/> — a caller deciding whether it
        /// is safe to close or replace a document without asking must not treat a timeout as "content is current".
        /// </summary>
        public async Task<bool> FlushContentAsync(int timeoutMs = 500)
        {
            if (MarkdownEditor == null || !FileLoaded) return true;
            var token = ++flushToken;
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (flushWaiters) flushWaiters[token] = waiter;
            try
            {
                MarkdownEditor.PostMessage("FlushContent", new { token });
                var finished = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                return finished == waiter.Task && waiter.Task.Result;
            }
            finally
            {
                lock (flushWaiters) flushWaiters.Remove(token);
            }
        }

        private void OnContentFlushed(JToken args)
        {
            var token = args?["token"]?.ToObject<int>() ?? 0;
            TaskCompletionSource<string> pageText = null;
            lock (pageTextWaiters) pageTextWaiters.TryGetValue(token, out pageText);
            pageText?.TrySetResult(args?["text"]?.ToString());
            TaskCompletionSource<bool> waiter = null;
            lock (flushWaiters) flushWaiters.TryGetValue(token, out waiter);
            waiter?.TrySetResult(true);
        }

        /// <summary>The newest load the page has confirmed with FileLoaded.</summary>
        public int ConfirmedLoadId { get; private set; } = -1;

        /// <summary>
        /// The editor page is being loaded again (leaving the settings page loads it again, as do a page error and a
        /// crashed web process). The load the old page confirmed is not the new page's: until the new page has asked
        /// for its settings and confirmed that load, <see cref="WaitForLoadAsync"/> waits instead of letting a read or
        /// write through to a page that is not listening yet (it then timed out after the 2 s flush).
        /// </summary>
        public void OnEditorPageNavigating()
        {
            if (ConfirmedLoadId != -1) Log.Debug($"editor: page loading again, load {ConfirmedLoadId} no longer confirmed");
            ConfirmedLoadId = -1;
            awaitingNewPage = true;
            PageGeneration++;
            // A flush sent to the page going away is never answered: say so now instead of after its timeout.
            List<TaskCompletionSource<bool>> waiting;
            lock (flushWaiters) waiting = flushWaiters.Values.ToList();
            foreach (var waiter in waiting) waiter.TrySetResult(false);
        }

        /// <summary>From the editor page starting to load again until the new page asks for its settings.</summary>
        private bool awaitingNewPage;

        /// <summary>Advances every time the editor page starts loading again.</summary>
        public int PageGeneration { get; private set; }

        /// <summary>
        /// The page has taken in the current load and handed over its latest text. When the page loads again while
        /// this waits (leaving the settings page), it starts over once with the new page instead of failing.
        /// </summary>
        public async Task<bool> SyncWithPageAsync(int loadTimeoutMs, int flushTimeoutMs)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var generation = PageGeneration;
                if (await WaitForLoadAsync(loadTimeoutMs) && await FlushContentAsync(flushTimeoutMs)) return true;
                if (PageGeneration == generation) return false;
            }
            return false;
        }

        /// <summary>
        /// Waits until the page has taken in the current load (its FileLoaded arrived). An automation edit sent before
        /// that races the load: the page may still show the previous text, or apply the edit and then the load.
        /// </summary>
        public async Task<bool> WaitForLoadAsync(int timeoutMs)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (ConfirmedLoadId != LoadId)
            {
                if (MarkdownEditor == null || clock.ElapsedMilliseconds > timeoutMs) return false;
                await Task.Delay(25);
            }
            return true;
        }

        // Automation writes (docs/automation-api-spec.md, section 2.2; Services/AutomationDocuments.cs). The page applies
        // an edit and answers DocumentEditApplied; a failed edit is undone by reloading the text held before it.
        private readonly Dictionary<string, (int loadId, TaskCompletionSource<JToken> waiter)> editWaiters = new();
        private readonly Dictionary<int, TaskCompletionSource<string>> reloadWaiters = new();

        /// <summary>Hands an automation edit to the page and waits for its reply; null when it did not answer in time.</summary>
        public async Task<JToken> ApplyDocumentEditAsync(string operationId, long targetRevision, string baseContentHash, string text, int timeoutMs)
        {
            if (MarkdownEditor == null) return null;
            var waiter = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            editWaiters[operationId] = (LoadId, waiter);
            try
            {
                MarkdownEditor.PostMessage("ApplyDocumentEdit", new { operationId, targetRevision, baseContentHash, text, loadId = LoadId });
                var finished = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                return finished == waiter.Task ? waiter.Task.Result : null;
            }
            finally
            {
                editWaiters.Remove(operationId);
            }
        }

        private void OnDocumentEditApplied(JToken arg)
        {
            var operationId = arg?["operationId"]?.ToString();
            Log.Debug($"automation: page reply {operationId} {arg?["outcome"]} {arg?["reason"]} loadId={arg?["loadId"]} current={LoadId}");
            if (operationId == null || !editWaiters.TryGetValue(operationId, out var entry)) return;
            // A reply from another load is about another document's text: the edit's outcome is unknown.
            var id = arg["loadId"];
            var sameLoad = id != null && id.Type == JTokenType.Integer && id.Value<int>() == entry.loadId && entry.loadId == LoadId;
            entry.waiter.TrySetResult(sameLoad ? arg : new JObject { ["operationId"] = operationId, ["outcome"] = "failed", ["reason"] = "superseded" });
        }

        private int normalizationToken;
        private readonly Dictionary<int, TaskCompletionSource<JToken>> normalizationWaiters = new();

        /// <summary>
        /// Asks the page what the first visual edit would do to the text it shows (automation document.get); null when
        /// it did not answer in time or answered for another load.
        /// </summary>
        public async Task<JToken> QueryNormalizationAsync(int timeoutMs)
        {
            if (MarkdownEditor == null || !FileLoaded) return null;
            var token = ++normalizationToken;
            var loadId = LoadId;
            var waiter = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            normalizationWaiters[token] = waiter;
            try
            {
                MarkdownEditor.PostMessage("QueryNormalization", new { token });
                var finished = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                if (finished != waiter.Task) return null;
                var reply = waiter.Task.Result;
                var id = reply?["loadId"];
                return id != null && id.Type == JTokenType.Integer && id.Value<int>() == loadId && loadId == LoadId ? reply : null;
            }
            finally
            {
                normalizationWaiters.Remove(token);
            }
        }

        private readonly Dictionary<int, TaskCompletionSource<JToken>> styleWaiters = new();

        /// <summary>The page's computed font size, line height and direction (automation test host checks).</summary>
        public async Task<JToken> QueryEditorStyleAsync(int timeoutMs)
        {
            if (MarkdownEditor == null) return null;
            var token = ++normalizationToken;
            var waiter = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            styleWaiters[token] = waiter;
            try
            {
                MarkdownEditor.PostMessage("QueryEditorStyle", new { token });
                return await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs)) == waiter.Task ? waiter.Task.Result : null;
            }
            finally
            {
                styleWaiters.Remove(token);
            }
        }

        private readonly Dictionary<int, TaskCompletionSource<bool>> framesWaiters = new();

        /// <summary>
        /// True once the page has drawn two animation frames after this call (automation awaitPresentation); false when
        /// it does not answer in time.
        /// </summary>
        public async Task<bool> AwaitPageFramesAsync(int timeoutMs)
        {
            if (MarkdownEditor == null) return false;
            var token = ++normalizationToken;
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            framesWaiters[token] = waiter;
            try
            {
                MarkdownEditor.PostMessage("AwaitPresentation", new { token });
                return await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs)) == waiter.Task;
            }
            finally
            {
                framesWaiters.Remove(token);
            }
        }

        private void OnNormalizationReport(JToken arg)
        {
            var token = arg?["token"]?.Value<int?>() ?? 0;
            if (normalizationWaiters.TryGetValue(token, out var waiter)) waiter.TrySetResult(arg);
        }

        /// <summary>
        /// Reloads the editor with the given text through the normal LoadFile flow, keeping the host's history, hashes
        /// and revision, and returns the text the page confirmed (null when it did not confirm in time).
        /// </summary>
        public async Task<string> ReloadAsync(string text, object cursor, double? scrollTop, int timeoutMs)
        {
            if (MarkdownEditor == null) return null;
            var loadId = ++LoadId;
            var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            reloadWaiters[loadId] = waiter;
            try
            {
                MarkdownEditor.PostMessage("LoadFile", new { text, basePath = FileViewModel.ImageBasePath, cursor, scrollTop, loadId });
                var finished = await Task.WhenAny(waiter.Task, Task.Delay(timeoutMs));
                return finished == waiter.Task ? waiter.Task.Result : null;
            }
            finally
            {
                reloadWaiters.Remove(loadId);
            }
        }

        private void CompleteReloadWaiter(JToken arg)
        {
            var id = arg?["loadId"];
            if (id == null || id.Type != JTokenType.Integer) return;
            if (reloadWaiters.TryGetValue(id.Value<int>(), out var waiter)) waiter.TrySetResult(arg["text"]?.ToString());
        }

        /// <summary>
        /// Commits text the page has confirmed for an automation edit: one undo step of its own (the reader's pending
        /// typing is closed first so the two never merge), the unsaved state and the revision.
        /// </summary>
        public void CommitAutomationText(string text)
        {
            if (!string.Equals(Markdown, text, StringComparison.Ordinal))
                ServiceProvider.GetService<TabsViewModel>()?.ActiveTab?.NoteTextChanged();
            History.CommitPending();
            Markdown = text;
            History.ContentChange(text);
            History.CursorChange(CurrentCursor ?? new(Focus: new(Line: 0, Ch: 0), Anchor: new(Line: 0, Ch: 0)));
            History.CommitPending();
            CurrentHash = Common.SimpleHash(text);
            Saved = FileHash == CurrentHash;
        }

        public void Undo()
        {
            var state = History.Undo();
            if (state == null)
            {
                return;
            }
            OnMarkdownChange(state.Text);
            contentUpdating = true;
            MarkdownEditor?.PostMessage("SetMarkdown", new
            {
                text = state.Text,
                cursor = state.Cursor,
                basePath = FileViewModel.ImageBasePath,
                loadId = NextHistoryLoadId(),
            });
        }

        public void Redo()
        {
            var state = History.Redo();
            if (state == null)
            {
                return;
            }
            OnMarkdownChange(state.Text);
            contentUpdating = true;
            MarkdownEditor?.PostMessage("SetMarkdown", new
            {
                text = state.Text,
                cursor = state.Cursor,
                loadId = NextHistoryLoadId(),
            });
        }

        /// <summary>
        /// Undo and redo put text into the page under a new load id: a report the page made before (still on its way)
        /// then carries the old id and is dropped, instead of writing the text just undone back over the history.
        /// SetMarkdown has no FileLoaded handshake, so the new id counts as confirmed at once.
        /// </summary>
        private int NextHistoryLoadId()
        {
            ConfirmedLoadId = ++LoadId;
            return LoadId;
        }

        public void Cut(string type)
        {
            MarkdownEditor?.PostMessage("Cut", new { type });
        }

        public async void Paste(string type)
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.ContainsText(TextDataFormat.Html))
                {
                    var text = await Clipboard.GetTextAsync(TextDataFormat.UnicodeText);
                    var html = await Clipboard.GetTextAsync(TextDataFormat.Html);
                    if (Common.MatchHtmlImg(html) is HtmlImgTag img)
                    {
                        if (UriHelper.IsWebUrl(img.Src))
                        {
                            img.Src = await ServiceProvider.GetService<ImageAction>().DoWebFileAction(img.Src);
                        }
                        else if (UriHelper.TryGetLocalPath(img.Src, out _))
                        {
                            img.Src = await ServiceProvider.GetService<ImageAction>().DoLocalFileAction(img.Src);
                            img.Src = img.Src.Replace('\\', '/');
                        }
                        MarkdownEditor?.PostMessage("InsertImage", img);
                        return;
                    }
                    MarkdownEditor?.PostMessage("Paste", new { type, text, html });
                }
                else if (await Clipboard.GetFileDropListAsync() is StringCollection files && files.Count == 1)
                {
                    if (FileTypeHelper.IsImageFile(files[0]))
                    {
                        MarkdownEditor?.PostMessage("InsertImage", new HtmlImgTag(src: files[0], alt: Path.GetFileNameWithoutExtension(files[0])));
                    }
                }
                else if (await Clipboard.GetImageAsync() is IClipboardImage image)
                {

                    var src = await ServiceProvider.GetService<ImageAction>().DoClipboardAction(image);
                    src = src.Replace('\\', '/');
                    MarkdownEditor?.PostMessage("InsertImage", new HtmlImgTag(src));

                }
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
            }
        }

        public void Copy(string type)
        {
            MarkdownEditor?.PostMessage("Copy", new { type });
        }

        public void OnSetClipboard(JToken arg)
        {
            var type = arg["type"].ToString();
            var data = arg["data"].ToString();
            if (type == "text/plain")
            {
                Clipboard.SetText(data, TextDataFormat.UnicodeText);
            }
            else if (type == "text/html")
            {
                Clipboard.SetText(data, TextDataFormat.Html);
            }
        }

        public void DeleteSelection()
        {
            MarkdownEditor?.PostMessage("DeleteSelection", null);
        }

        public void SelectAll()
        {
            MarkdownEditor?.PostMessage("SelectAll", null);
        }

        public void Find(string action)
        {
            var appViewModel = ServiceProvider.GetService<AppViewModel>();
            if (appViewModel.FloatViewModel.FindReplaceDialogOpen == 0)
            {
                appViewModel.FloatViewModel.FindReplaceDialogOpen = FloatViewModel.FindReplaceDialogState.Search;
                appViewModel.EditorViewModel.OnSearch();
            }
            else
            {
                MarkdownEditor?.PostMessage("Find", new { action });
            }
        }

        public void SearchValueChanged()
        {
            var floatViewModel = ServiceProvider.GetService<FloatViewModel>();
            if (floatViewModel.FindReplaceDialogOpen > 0)
                OnSearch();
        }

        public void SavedOrAutoSavedSuccChanged()
        {
            try
            {
                var fileViewModel = ServiceProvider.GetService<FileViewModel>();
                DisplaySaved = Saved || (Settings.AutoSave && fileViewModel.FilePath != null && AutoSavedSucc);
                if (Saved)
                    AutoBackup.DeleteBackup(fileViewModel.FilePath, ServiceProvider.GetService<TabsViewModel>()?.ActiveTab?.DocumentId);
            }
            catch
            {
                // Ignore
            }
        }

        public void Settings_AutoSaveChanged(bool autoSave)
        {
            DisplaySaved = Saved || autoSave;
        }

        public void JumpBySlug(string slug)
        {
            MarkdownEditor?.PostMessage("ScrollTo", new { slug });
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
