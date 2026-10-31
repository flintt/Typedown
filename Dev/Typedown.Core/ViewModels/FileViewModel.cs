using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typedown.Core.Controls;
using Typedown.Core.Controls.DialogControls;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Services;
using Typedown.Core.Utilities;
using Windows.ApplicationModel.Core;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.ViewModels
{
    public sealed partial class FileViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public AppViewModel AppViewModel => ServiceProvider.GetService<AppViewModel>();

        public SettingsViewModel SettingsViewModel => ServiceProvider.GetService<SettingsViewModel>();

        public EditorViewModel EditorViewModel => ServiceProvider.GetService<EditorViewModel>();

        public EventCenter EventCenter => ServiceProvider.GetService<EventCenter>();

        public RemoteInvoke RemoteInvoke => ServiceProvider.GetService<RemoteInvoke>();

        public AccessHistory AccessHistory => ServiceProvider.GetService<AccessHistory>();

        public HomeViewModel Home => ServiceProvider.GetService<HomeViewModel>();

        public string WorkFolder { get; private set; } = null;

        /// <summary>True when the user chose the workspace folder (Open Folder), false when it was restored at startup.</summary>
        public bool WorkFolderIsExplicit { get; private set; }

        /// <summary>
        /// The folder the side pane's tree shows, set by the tree; null before it was first shown. It can differ from
        /// <see cref="WorkFolder"/>: unless a folder was opened, the tree follows the document in front.
        /// </summary>
        public string FolderTreeRoot { get; set; }

        /// <summary>Raised after a document was loaded from disk (not on tab switches); <c>preview</c> = single-click open.</summary>
        public event Action<string, bool> FileOpened;

        [OnChangedMethod(nameof(OnFilePathChanged))]
        public string FilePath { get; private set; } = null;

        public string ImageBasePath => string.IsNullOrEmpty(FilePath) ? SettingsViewModel.DefaultImageBasePath : Path.GetDirectoryName(FilePath);

        public string FileName => Path.GetFileName(FilePath);

        public Command<Unit> NewFileCommand { get; } = new();
        public Command<string> NewWindowCommand { get; } = new();
        public Command<string> OpenFileCommand { get; } = new();
        public Command<string> OpenFolderCommand { get; } = new();
        public Command<Unit> NewFolderCommand { get; } = new();
        /// <summary>File > Open recent > Clear recent files: the files only (the folders have their own entry).</summary>
        public Command<Unit> ClearHistoryCommand { get; } = new();
        /// <summary>File > Open recent > Folders > Clear recent folders.</summary>
        public Command<Unit> ClearFolderHistoryCommand { get; } = new();
        /// <summary>Settings > General > Data > Clear recent: files and folders.</summary>
        public Command<Unit> ClearAllHistoryCommand { get; } = new();
        public Command<Unit> SaveCommand { get; } = new();
        public Command<Unit> SaveAsCommand { get; } = new();
        public Command<Unit> ImportCommand { get; } = new();
        public Command<ExportConfig> ExportCommand { get; } = new();
        public Command<Unit> PrintCommand { get; } = new();
        public Command<Unit> ShareToHedgeDocCommand { get; } = new();
        public Command<Unit> UploadLocalImagesCommand { get; } = new();
        public Command<Unit> ExitCommand { get; } = new();

        private readonly DispatcherTimer saveFileTimer = new();
        private readonly SemaphoreSlim saveLock = new(1, 1);
        private bool saveTimerRunning;

        private readonly DispatcherTimer fileReloadTimer = new();

        private FileSystemWatcher fileWatcher;

        private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        private DateTime ignoreExternalChangeUntil = DateTime.MinValue;

        private bool reloadDialogOpened;

        public AutoBackup AutoBackup => ServiceProvider.GetService<AutoBackup>();

        public IMarkdownEditor MarkdownEditor => ServiceProvider.GetService<IMarkdownEditor>();

        private readonly CompositeDisposable disposables = new();

        public FileViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            // New, as the reader asks for it (the + button, the menu, Ctrl+N): the keyboard goes to the new document. The
            // + button kept it, and the letters typed next went nowhere until a click in the text.
            // A new tab is for writing: no home page on it unless Settings > General > On startup asks for one there too.
            NewFileCommand.OnExecute.Subscribe(async _ =>
            {
                await NewFileFun();
                if (SettingsViewModel.ShowHomeOnNewTab) Home?.Show(); else Home?.Hide();
                MarkdownEditor?.FocusEditorInPlace();
            });
            OpenFileCommand.OnExecute.Subscribe(async x => await OpenFile(x));
            OpenFolderCommand.OnExecute.Subscribe(async x => await OpenFolder(x));
            SaveAsCommand.OnExecute.Subscribe(async _ => await SaveAs());
            SaveCommand.OnExecute.Subscribe(async _ => await Save());
            ExitCommand.OnExecute.Subscribe(_ => Exit());
            // "Clear recent files" cleared the folders too, which it does not say: each list has its own entry now.
            ClearHistoryCommand.OnExecute.Subscribe(x => { _ = AccessHistory.ClearFileHistory(); });
            ClearFolderHistoryCommand.OnExecute.Subscribe(x => { _ = AccessHistory.ClearFolderHistory(); });
            ClearAllHistoryCommand.OnExecute.Subscribe(x => { _ = AccessHistory.ClearHistory(); });
            ExportCommand.OnExecute.Subscribe(Export);
            PrintCommand.OnExecute.Subscribe(_ => Print());
            ShareToHedgeDocCommand.OnExecute.Subscribe(_ => ShareToHedgeDoc());
            UploadLocalImagesCommand.OnExecute.Subscribe(async _ => await UploadLocalImages());
            ImportCommand.OnExecute.Subscribe(_ => Import());
            RemoteInvoke.Handle<JToken, bool>("ExportCallback", ExportCallback);
            RemoteInvoke.Handle<JToken, bool>("PrintHTML", PrintHTML);
            // The page printing itself (window.print) and done: the print preview's margins white meanwhile.
            RemoteInvoke.Handle<JToken, bool>("Printing", args =>
            {
                MarkdownEditor?.SetPrinting(args?["printing"]?.Value<bool>() == true);
                return true;
            });
            saveFileTimer.Interval = TimeSpan.FromSeconds(5);
            saveFileTimer.Tick += SaveFileTimerTick;
            saveFileTimer.Start();
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            fileReloadTimer.Interval = TimeSpan.FromMilliseconds(400);
            fileReloadTimer.Tick += FileReloadTimerTick;
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AutoReload)).Subscribe(_ => StartWatchFile()));
            _ = dispatcherQueue.RunIdleAsync(() => OnStartup());
        }

        private async void SaveFileTimerTick(object sender, object e)
        {
            if (disposables.IsDisposed || saveTimerRunning)
            {
                return;
            }
            saveTimerRunning = true;
            try
            {
                CursorMemory.Flush();
                if (SettingsViewModel.AutoSave)
                {
                    EditorViewModel.AutoSavedSucc = await AutoSaveFile();
                    if (!EditorViewModel.AutoSavedSucc)
                        await AutoBackupFile();
                }
                else
                {
                    await AutoBackupFile();
                }
                // Background tabs are never auto-saved, so back up their dirty ones every tick regardless of the
                // active document's auto-save result.
                await BackupDirtyBackgroundTabsAsync();
            }
            catch (Exception ex)
            {
                // Every five seconds: a failure is logged and the next tick tries again (out of the timer's handler it
                // ended the app).
                Log.Debug($"save timer: {ex}");
            }
            finally
            {
                saveTimerRunning = false;
            }
        }

        public async Task<bool> AutoSaveFile()
        {
            try
            {
                if (SettingsViewModel.AutoSave && EditorViewModel.FileLoaded && (EditorViewModel.FileHash != EditorViewModel.CurrentHash) && FilePath != null)
                    return await Save(false);
                return FilePath != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>One backup pass now, as the save timer does (for the automation test host).</summary>
        public async Task<bool> BackupNowAsync()
        {
            var ok = await AutoBackupFile();
            await BackupDirtyBackgroundTabsAsync();
            return ok;
        }

        /// <summary>
        /// Every unsaved document backed up now, for a window closing that keeps them (Settings > Keep unsaved changes when
        /// closing): true when each one is on disk as it is - the next start brings them back - false when one is not.
        /// </summary>
        public async Task<bool> KeepUnsavedAsync()
        {
            var ok = await AutoBackupFile();
            await BackupDirtyBackgroundTabsAsync();
            var tabs = TabsViewModel;
            if (tabs != null)
                foreach (var tab in tabs.Tabs)
                    if (tab != tabs.ActiveTab && !tab.Saved && !string.IsNullOrWhiteSpace(tab.Markdown) && tab.BackupHash != tab.CurrentHash)
                        ok = false;
            Log.Debug($"keep unsaved on close: {(ok ? "every unsaved document backed up" : "one could not be backed up; asking")}");
            return ok;
        }

        /// <summary>Whether the save timer is in a tick (a tick that never finishes stops all later backups).</summary>
        public bool SaveTimerBusy => saveTimerRunning;

        private async Task<bool> AutoBackupFile()
        {
            var active = TabsViewModel?.ActiveTab;
            if (EditorViewModel.FileHash != EditorViewModel.CurrentHash && !string.IsNullOrWhiteSpace(EditorViewModel.Markdown))
            {
                var backedHash = EditorViewModel.CurrentHash;
                var ok = await AutoBackup.Backup(FilePath, EditorViewModel.Markdown, active?.DocumentId);
                // Keep the active tab's BackupHash in step with what is on disk, so the invariant still holds when
                // this tab later becomes a background one (see BackupDirtyBackgroundTabsAsync).
                if (ok && active != null) active.BackupHash = backedHash;
                return ok;
            }
            AutoBackup.DeleteBackup(FilePath, active?.DocumentId);
            if (active != null) active.BackupHash = 0;
            return true;
        }

        // Background tabs are snapshots the live editor does not hold, so a crash would lose their unsaved edits.
        // This runs every save tick regardless of whether the active document's auto-save ran or succeeded — the
        // active save never touches the other tabs. Each dirty background tab is backed up - a titled one keyed by
        // its path, which recovery matches on reopen, an untitled one by its document id, which the next start
        // recovers (RecoverUntitledBackupsAsync) - skipping ones unchanged since their last backup.
        private async Task BackupDirtyBackgroundTabsAsync()
        {
            var tabs = TabsViewModel;
            if (tabs == null) return;
            foreach (var tab in tabs.Tabs.ToList())
            {
                if (tab == tabs.ActiveTab)
                    continue;
                if (!tab.Saved && !string.IsNullOrWhiteSpace(tab.Markdown))
                {
                    // Capture text and its hash together before the await; switching into this tab, editing and
                    // switching away could otherwise update the snapshot to a hash that never reached disk.
                    var text = tab.Markdown;
                    var backedHash = tab.CurrentHash;
                    if (backedHash != tab.BackupHash && await AutoBackup.Backup(tab.FilePath, text, tab.DocumentId))
                        tab.BackupHash = backedHash;
                }
                else if (tab.Saved && tab.BackupHash != 0)
                {
                    AutoBackup.DeleteBackup(tab.FilePath, tab.DocumentId);
                    tab.BackupHash = 0;
                }
            }
        }

        public TabsViewModel TabsViewModel => ServiceProvider.GetService<TabsViewModel>();

        /// <summary>Used by <see cref="TabsViewModel"/> when restoring a background tab into the editor.</summary>
        internal void SetFilePathFromTab(string path) => FilePath = path;

        /// <summary>
        /// Hash of the raw text last read from or written to disk. The editor normalizes the document on import, so
        /// <c>EditorViewModel.FileHash</c> (hash of the normalized text) can differ from the disk content even when
        /// nothing changed; comparing against this avoids false "file changed" prompts.
        /// </summary>
        internal ulong DiskHash { get; set; }

        /// <summary>After a tab switch the file was not watched; check the disk copy once.</summary>
        internal async Task CheckExternalChangeAfterSwitch()
        {
            if (string.IsNullOrEmpty(FilePath) || !SettingsViewModel.AutoReload) return;
            await HandleExternalFileChange();
        }

        private async Task NewFileFun(bool postMessage = true, bool inNewTab = true)
        {
            // Keys typed from here on are for the new document (see HoldInput); the document left keeps its latest edits,
            // as on a tab switch.
            var hold = postMessage ? EditorViewModel.HoldInput() : 0;
            try
            {
                if (postMessage) await EditorViewModel.FlushBeforeTabChangeAsync();
                await NewFileCore(postMessage, inNewTab);
            }
            finally
            {
                if (hold != 0) EditorViewModel.ReleaseInput(hold);
            }
        }

        private async Task NewFileCore(bool postMessage, bool inNewTab)
        {
            // Tabs (upstream #73): a new document opens in its own tab unless the current one is a pristine untitled tab.
            if (inNewTab && TabsViewModel != null && !TabsViewModel.IsActiveTabBlank)
                TabsViewModel.BeginNewTab();
            else if (!await AskToSave())
                return;
            else
                TabsViewModel?.ActiveTab?.BecomeNewDocument();
            FilePath = null;
            FileFormat = TextFileFormat.Default;
            EditorViewModel.FileHash = Common.SimpleHash(Common.DefaultMarkdwn);
            string backup = null;
            if (AppViewModel.GetInstances().Where(x => x != AppViewModel).All(x => !string.IsNullOrEmpty(x.FileViewModel.FilePath)))
            {
                backup = await CheckBackup(FilePath, EditorViewModel.FileHash);
            }
            if (backup == null)
            {
                EditorViewModel.Markdown = Common.DefaultMarkdwn;
                EditorViewModel.CurrentHash = EditorViewModel.FileHash;
                EditorViewModel.Saved = true;
                EditorViewModel.AutoSavedSucc = true;
                EditorViewModel.AutoSaveHeldBack = null;
                EditorViewModel.FileLoaded = false;
            }
            else
            {
                EditorViewModel.Markdown = backup;
                EditorViewModel.CurrentHash = Common.SimpleHash(backup);
                EditorViewModel.Saved = false;
                EditorViewModel.AutoSavedSucc = false;
                EditorViewModel.FileLoaded = true;
            }
            EditorViewModel.History.InitHistory(EditorViewModel.Markdown);
            if (postMessage)
            {
                EditorViewModel.PostLoadFile(EditorViewModel.Markdown);
            }
            EditionHooks.ReportActivity(ActivityKind.DocumentShown, AppViewModel);
        }

        /// <param name="preview">Single-click open from the file tree: reuse the current preview tab instead of adding one.</param>
        public async Task<bool> OpenFile(string filePath = null, bool preview = false)
        {
            filePath ??= await AppViewModel.MainWindow.PickMarkdownFileAsync();
            if (filePath == null)
                return false;
            return await LoadFile(filePath, true, true, preview);
        }

        public async Task<bool> OpenFolder(string folderPath = null)
        {
            folderPath ??= await AppViewModel.MainWindow.PickMarkdownFolderAsync();
            if (folderPath == null)
                return false;
            if (!await LoadFolder(folderPath, explicitRoot: true))
                return false;
            SettingsViewModel.SidePaneOpen = true;
            SettingsViewModel.SidePaneIndex = 0;
            return true;
        }

        private async Task<bool> LoadFile(string path, bool skipSavedCheck = false, bool postMessage = true, bool preview = false)
        {
            DocumentTab startedTab = null;
            try
            {
                if (TryGetOpenedWindow(path, out var window) && window != AppViewModel.MainWindow)
                {
                    _ = AppViewModel.XamlRoot?.Content?.DispatcherQueue?.RunIdleAsync(() => { PInvoke.SetForegroundWindow(window); });
                    return false;
                }
                // A recent file that is not there stays in the list (greyed out): a network drive or a USB stick may only
                // be unplugged. The reader removes it if it is gone for good.
                if (!File.Exists(path))
                    throw new FileNotFoundException("File does not exist.");
                // Tabs: an already open file just becomes active; anything else opens beside the current document
                // (reusing a pristine untitled tab), so the old document never needs a save prompt here.
                if (TabsViewModel?.FindByPath(path) is DocumentTab existing)
                {
                    await TabsViewModel.SwitchTo(existing);
                    if (!preview) existing.IsPreview = false; // double-click / explicit open pins a preview tab
                    return true;
                }
                if (TabsViewModel != null && !(preview ? TabsViewModel.CanReuseActiveTabForPreview : TabsViewModel.IsActiveTabBlank))
                    startedTab = TabsViewModel.BeginNewTab();
                var openedAt = System.Diagnostics.Stopwatch.StartNew();
                var (text, format) = await TextFileFormat.ReadAsync(path);
                EditionHooks.ReportDocumentOnDisk(path, text, written: false);
                // Read: the reused tab now holds another document.
                if (startedTab == null) TabsViewModel?.ActiveTab?.BecomeNewDocument();
                FileFormat = format;
                var readMs = openedAt.ElapsedMilliseconds;
                EditorViewModel.FirstStart = false;
                EditorViewModel.FileHash = Common.SimpleHash(text);
                DiskHash = EditorViewModel.FileHash;
                FilePath = path;
                _ = AccessHistory.RecordFileHistory(FilePath);
                var backup = await CheckBackup(path, EditorViewModel.FileHash);
                if (backup == null)
                {
                    EditorViewModel.Markdown = text;
                    EditorViewModel.CurrentHash = EditorViewModel.FileHash;
                    EditorViewModel.Saved = true;
                    EditorViewModel.FileLoaded = false;
                }
                else
                {
                    EditorViewModel.Markdown = backup;
                    EditorViewModel.CurrentHash = Common.SimpleHash(backup);
                    EditorViewModel.Saved = false;
                    EditorViewModel.FileLoaded = true;
                }
                EditorViewModel.AutoSavedSucc = true;
                EditorViewModel.AutoSaveHeldBack = null;
                EditorViewModel.History.InitHistory(EditorViewModel.Markdown);
                if (postMessage)
                {
                    var cursor = SettingsViewModel.RememberCursorPosition ? CursorMemory.Get(path) : null;
                    EditorViewModel.PostLoadFile(EditorViewModel.Markdown, cursor);
                }
                if (TabsViewModel != null)
                    TabsViewModel.ActiveTab.IsPreview = preview;
                FileOpened?.Invoke(path, preview);
                EditionHooks.ReportActivity(ActivityKind.DocumentShown, AppViewModel);
                // Opening a large document is the one place where the host's own work is worth timing: the
                // editor reports back when it has the text, and the gap between the two is the page's share.
                if (text.Length > 200000)
                    Log.Debug($"open {System.IO.Path.GetFileName(path)}: {text.Length} chars, read {readMs} ms, host {openedAt.ElapsedMilliseconds} ms up to handing it over");
                return true;
            }
            catch (Exception ex)
            {
                if (startedTab != null) TabsViewModel?.AbortNewTab(startedTab);
                await AppContentDialog.Create(Locale.GetDialogString("ReadErrorTitle"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return false;
            }
        }

        private async Task<bool> LoadFolder(string folderPath, bool explicitRoot = false)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    throw new FileNotFoundException("Folder does not exist.");
                // Opened by the reader: said before the folder changes, which the file tree follows at once. Set after, the
                // tree first stayed with the document (not yet the reader's folder), then moved only when something else
                // asked again - the root changed twice in 2 ms, or not at all with the folder pane already open.
                if (explicitRoot) WorkFolderIsExplicit = true;
                WorkFolder = folderPath;
                _ = AccessHistory.RecordFolderHistory(folderPath);
                return true;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return false;
            }
        }

        private async Task<string> CheckBackup(string path, ulong fileHash)
        {
            string text = await AutoBackup.GetBackup(path);
            if (text == null || Common.SimpleHash(text) == fileHash) return null;
            // Kept on purpose when the window closed (Settings > Keep unsaved changes when closing): back without asking.
            if (SettingsViewModel.KeepUnsavedOnClose) return text;
            var dialog = AppContentDialog.Create();
            dialog.Title = Locale.GetDialogString("RecoverTitle");
            dialog.Content = Locale.GetDialogString("RecoverContent");
            dialog.PrimaryButtonText = Locale.GetDialogString("Recover");
            dialog.SecondaryButtonText = Locale.GetDialogString("Delete");
            dialog.DefaultButton = ContentDialogButton.Primary;
            var result = await dialog.ShowAsync(AppViewModel.XamlRoot);
            if (result == ContentDialogResult.Primary)
            {
                return text;
            }
            else
            {
                AutoBackup.DeleteBackup(path);
                return null;
            }
        }

        /// <summary>
        /// The byte shape of the open document: its encoding, byte order mark and line ending. A file opened with
        /// CRLF is written back with CRLF — saving a document without editing it must not rewrite every line.
        /// </summary>
        public TextFileFormat FileFormat { get; set; } = TextFileFormat.Default;

        private async Task<bool> WriteAllText(string path, string text, bool alert = true)
        {
            try
            {
                // A blank buffer over a file that has content is data loss unless the reader actually deleted the
                // text — an undoable edit. It happens two ways, and neither is a document the reader emptied:
                //   - a save fires between opening the file and the editor answering (a hang, a crash-reload, an
                //     exit on the way) — the editor has not loaded yet;
                //   - the editor is cleared programmatically after loading (a stale/empty load, a tab race, the
                //     document handed back blank) — there is no undoable edit behind the change, and Ctrl+Z
                //     cannot bring it back either.
                // In both cases refuse the write, so the file on disk keeps its content and the reader can reopen
                // it. A genuine "select all, delete" is undoable and still saves. A reader lost an afternoon's
                // work to a file "overwritten with 0K"; nothing here writes that.
                if (string.IsNullOrWhiteSpace(text) && File.Exists(path) && new FileInfo(path).Length > 0
                    && (!EditorViewModel.FileLoaded || !EditorViewModel.History.Undoable))
                {
                    Log.Debug($"save skipped: buffer is blank (len={text.Length}) with no undoable edit (loaded={EditorViewModel.FileLoaded} undoable={EditorViewModel.History.Undoable}) while the file has {new FileInfo(path).Length} bytes");
                    if (!alert) EditorViewModel.AutoSaveHeldBack = Locale.GetString("SaveState.BlankHeldBack");
                    return false;
                }
                // A file we could not decode losslessly (a non-UTF-8 file — GBK, Latin-1 — read as UTF-8, so its
                // undecodable bytes became U+FFFD) must not be overwritten by an automatic save: that rewrites it
                // as UTF-8 and the undecodable bytes are lost for good. The skipped auto-save falls through to a
                // crash backup, so nothing typed is lost. (A confirm-on-manual-save prompt is deferred until its
                // dialog strings are translated.)
                if (!alert && (FileFormat?.LossyDecode ?? false) && File.Exists(path))
                {
                    Log.Debug($"auto-save skipped: '{path}' was decoded lossily as {(FileFormat?.Encoding?.WebName ?? "utf-8")}; an automatic overwrite would replace its original bytes");
                    EditorViewModel.AutoSaveHeldBack = Locale.GetString("SaveState.EncodingHeldBack");
                    return false;
                }
                IgnoreOwnFileWrite();
                await SafeFile.WriteAllBytesAtomicAsync(path, (FileFormat ?? TextFileFormat.Default).GetBytes(text));
                IgnoreOwnFileWrite();
                EditionHooks.ReportDocumentOnDisk(path, text, written: true);
                EditorViewModel.AutoSaveHeldBack = null;
                return true;
            }
            catch (Exception ex)
            {
                if (!alert) EditorViewModel.AutoSaveHeldBack = string.Format(Locale.GetString("SaveState.WriteFailed"), ex.Message);
                if (alert)
                {
                    await AppContentDialog.Create(Locale.GetDialogString("SaveErrorTitle"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                }
                return false;
            }
        }

        /// <summary>
        /// A new untitled document in this window (a new tab, or the pristine blank one), as the New command; shown in the
        /// editor, not under the home page (an automation client's document.create, the home page's own command).
        /// </summary>
        public async Task NewDocumentAsync()
        {
            await NewFileFun();
            Home?.Hide();
        }

        /// <summary>
        /// The active tab made a new blank document at once (the last tab closed with the home page on): no question, no
        /// backup looked for - what was there has been saved or let go already.
        /// </summary>
        internal void ResetToBlankDocument()
        {
            if (TabsViewModel?.ActiveTab is { } tab)
            {
                tab.BecomeNewDocument();
                tab.IsPreview = false;
                tab.BackupHash = 0;
                tab.Cursor = null;
            }
            FilePath = null;
            FileFormat = TextFileFormat.Default;
            DiskHash = 0;
            EditorViewModel.FileHash = Common.SimpleHash(Common.DefaultMarkdwn);
            EditorViewModel.Markdown = Common.DefaultMarkdwn;
            EditorViewModel.CurrentHash = EditorViewModel.FileHash;
            EditorViewModel.Saved = true;
            EditorViewModel.AutoSavedSucc = true;
            EditorViewModel.AutoSaveHeldBack = null;
            EditorViewModel.FileLoaded = false;
            EditorViewModel.History.InitHistory(EditorViewModel.Markdown);
            EditorViewModel.PostLoadFile(EditorViewModel.Markdown);
            EditionHooks.ReportActivity(ActivityKind.DocumentShown, AppViewModel);
        }

        /// <summary>
        /// Saves the active document to its existing path without any dialog (the automation API's save): false when
        /// it has no path or the write was refused or failed. Follows the auto-save rules, so a file decoded lossily
        /// is not overwritten.
        /// </summary>
        public Task<bool> SaveToExistingPathAsync() => FilePath == null ? Task.FromResult(false) : Save(false);

        /// <summary>What File > Save does (Vim's :wq waits for it before closing the tab).</summary>
        public Task<bool> SaveAsync() => Save();

        private async Task<bool> Save(bool alert = true)
        {
            var path = FilePath;
            await saveLock.WaitAsync();
            try
            {
                if (disposables.IsDisposed || FilePath != path)
                    return false;
                return await SaveCore(alert);
            }
            finally
            {
                saveLock.Release();
            }
        }

        private async Task<bool> SaveCore(bool alert)
        {
            if (FilePath == null)
            {
                var result = await SaveAsCore();
                return result != null;
            }
            else
            {
                var path = FilePath;
                var tab = TabsViewModel?.ActiveTab;
                await EditorViewModel.FlushContentAsync(); // what we write must be what is on screen
                // The flush awaited, and a tab switch does not take this lock: if the active document changed
                // under us the live buffer now belongs to another document, and writing it to this path would
                // put one document's text into another. Check the tab reference too — two untitled tabs share a
                // null path, so the path check alone would not notice a switch between them.
                if (disposables.IsDisposed || FilePath != path || TabsViewModel?.ActiveTab != tab)
                    return false;
                var markdown = EditorViewModel.Markdown;
                var hash = Common.SimpleHash(markdown);
                var result = await WriteAllText(path, markdown, alert);
                if (result && !disposables.IsDisposed && FilePath == path)
                {
                    EditorViewModel.FileHash = hash;
                    DiskHash = hash;
                    EditorViewModel.Saved = EditorViewModel.CurrentHash == hash; // CurrentHash tracks the live buffer; avoids an O(n) string compare per save
                    if (EditorViewModel.Saved)
                    {
                        AutoBackup.DeleteBackup(path);
                        if (TabsViewModel?.ActiveTab is { } savedTab) savedTab.BackupHash = 0;
                    }
                    _ = AccessHistory.RecordFileHistory(path);
                }
                return result && !disposables.IsDisposed && FilePath == path && EditorViewModel.Saved;
            }
        }

        private async Task<string> SaveAs()
        {
            var path = FilePath;
            await saveLock.WaitAsync();
            try
            {
                if (disposables.IsDisposed || FilePath != path)
                    return null;
                return await SaveAsCore();
            }
            finally
            {
                saveLock.Release();
            }
        }

        private async Task<string> SaveAsCore()
        {
            try
            {
                var originalPath = FilePath;
                var originalTab = TabsViewModel?.ActiveTab;
                var filePicker = new FileSavePicker();
                filePicker.SetOwnerWindow(AppViewModel.MainWindow);
                filePicker.FileTypeChoices.Add("Markdown Files", FileTypeHelper.Markdown.ToList());
                filePicker.SuggestedFileName = FileName ?? "untitled";
                var file = await FilePickersExtensions.ShowAsync(() => filePicker.PickSaveFileAsync());
                if (file == null) return null;
                return await SaveAsToCore(file.Path, originalPath, originalTab);
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return null;
            }
        }

        /// <summary>Save as, with the file already chosen (the test host skips the dialog).</summary>
        public async Task<string> SaveAsTo(string path)
        {
            var originalPath = FilePath;
            await saveLock.WaitAsync();
            try
            {
                if (disposables.IsDisposed || FilePath != originalPath)
                    return null;
                return await SaveAsToCore(path, originalPath, TabsViewModel?.ActiveTab);
            }
            finally
            {
                saveLock.Release();
            }
        }

        private async Task<string> SaveAsToCore(string targetPath, string originalPath, Models.DocumentTab originalTab)
        {
            try
            {
                if (!disposables.IsDisposed && FilePath == originalPath && TabsViewModel?.ActiveTab == originalTab)
                {
                    await EditorViewModel.FlushContentAsync();
                    if (disposables.IsDisposed || FilePath != originalPath || TabsViewModel?.ActiveTab != originalTab)
                        return null;
                    var markdown = EditorViewModel.Markdown;
                    var hash = Common.SimpleHash(markdown);
                    var imagesBefore = ImageBasePath;
                    var result = await WriteAllText(targetPath, markdown);
                    if (result && !disposables.IsDisposed && FilePath == originalPath)
                    {
                        CopyPicturesAlong(markdown, imagesBefore, Path.GetDirectoryName(targetPath));
                        FilePath = targetPath;
                        EditorViewModel.FileHash = hash;
                        DiskHash = hash;
                        EditorViewModel.Saved = EditorViewModel.CurrentHash == hash; // CurrentHash tracks the live buffer; avoids an O(n) string compare per save
                        if (EditorViewModel.Saved)
                        {
                            AutoBackup.DeleteBackup(originalPath, originalTab?.DocumentId);
                            if (TabsViewModel?.ActiveTab is { } savedTab) savedTab.BackupHash = 0;
                        }
                        _ = AccessHistory.RecordFileHistory(FilePath);
                        if (!string.Equals(originalPath, targetPath, StringComparison.OrdinalIgnoreCase))
                            EditionHooks.ReportActivity(ActivityKind.DocumentRenamed, AppViewModel, originalPath);
                        return EditorViewModel.Saved ? targetPath : null;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return null;
            }
        }

        // Pictures by relative paths (./images/x.png) were beside the old place - for an untitled document, the default
        // picture folder - and pointed nowhere once it was saved elsewhere: copied along (ImageAction.CopyRelativeImages).
        private static void CopyPicturesAlong(string markdown, string fromBase, string toBase)
        {
            try
            {
                var copied = ImageAction.CopyRelativeImages(markdown, fromBase, toBase);
                if (copied > 0) Log.Debug($"save as: {copied} picture(s) copied from {fromBase} to {toBase}");
            }
            catch (Exception ex)
            {
                Log.Debug($"save as: the pictures could not be copied from {fromBase}: {ex.Message}");
            }
        }

        // Upload the current document to the configured HedgeDoc server and show the resulting links. HedgeDoc 1.x
        // cannot update a note over HTTP, so an unchanged document gets its previous links back and a changed one
        // asks before a new note (new link) is created.
        private async void ShareToHedgeDoc()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SettingsViewModel.HedgeDocServer))
                {
                    await AppContentDialog.Create(Locale.GetDialogString("HedgeDocShare.ResultTitle"), Locale.GetDialogString("HedgeDocShare.NotConfigured"), Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                    AppViewModel.NavigateCommand.Execute("Settings/Export");
                    return;
                }
                // Local pictures are files on this computer: the note's readers would not see them.
                var localImages = await new ImageBatchUpload(AppViewModel).LocalImagesOfActiveDocumentAsync();
                if (localImages.Count > 0)
                {
                    var upload = await AppContentDialog.Create(
                        Locale.GetDialogString("HedgeDocShare.ResultTitle"),
                        string.Format(Locale.GetDialogString("UploadImages.HedgeDocPrompt"), localImages.Count),
                        Locale.GetString("Cancel"),
                        Locale.GetDialogString("UploadImages.UploadFirst"),
                        Locale.GetDialogString("UploadImages.ShareAnyway")).ShowAsync(AppViewModel.XamlRoot);
                    if (upload == Microsoft.UI.Xaml.Controls.ContentDialogResult.None)
                        return;
                    if (upload == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary && !await UploadLocalImages())
                        return;
                }
                await EditorViewModel.FlushContentAsync();
                var markdown = EditorViewModel.Markdown;
                var hash = Common.SimpleHash(markdown);
                var previous = HedgeDocShareMemory.Get(FilePath);
                if (previous != null)
                {
                    var previousResult = new HedgeDocShareResult { NoteUrl = previous.NoteUrl, PublishedUrl = previous.PublishedUrl };
                    if (previous.ContentHash == hash)
                    {
                        await HedgeDocShareDialog.ShowAsync(AppViewModel.XamlRoot, previousResult, Locale.GetDialogString("HedgeDocShare.Unchanged"));
                        return;
                    }
                    var choice = await AppContentDialog.Create(
                        Locale.GetDialogString("HedgeDocShare.ResultTitle"),
                        Locale.GetDialogString("HedgeDocShare.ChangedPrompt"),
                        Locale.GetString("Cancel"),
                        Locale.GetDialogString("HedgeDocShare.ReUpload"),
                        Locale.GetDialogString("HedgeDocShare.UseOldLink")).ShowAsync(AppViewModel.XamlRoot);
                    if (choice == Microsoft.UI.Xaml.Controls.ContentDialogResult.Secondary)
                    {
                        await HedgeDocShareDialog.ShowAsync(AppViewModel.XamlRoot, previousResult, null);
                        return;
                    }
                    if (choice != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                        return;
                }
                var result = await HedgeDocService.ShareAsync(SettingsViewModel.HedgeDocServer, markdown, SettingsViewModel.HedgeDocEmail, SettingsViewModel.HedgeDocPassword, SettingsViewModel.HedgeDocPublishReadOnly);
                HedgeDocShareMemory.Set(FilePath, result, hash);
                await HedgeDocShareDialog.ShowAsync(AppViewModel.XamlRoot, result, null);
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
            }
        }

        /// <summary>
        /// File > Upload local images, with a progress dialog that can cancel and a dialog with the result. False when
        /// nothing was uploaded because there is no configuration or the user cancelled.
        /// </summary>
        private async Task<bool> UploadLocalImages()
        {
            try
            {
                var title = Locale.GetDialogString("UploadImages.Title");
                var batch = new ImageBatchUpload(AppViewModel);
                if ((await batch.LocalImagesOfActiveDocumentAsync()).Count == 0)
                {
                    await AppContentDialog.Create(title, Locale.GetDialogString("UploadImages.None"), Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                    return true;
                }
                if (await ServiceProvider.GetService<ImageUpload>().GetDefaultConfigAsync() == null)
                {
                    var open = await AppContentDialog.Create(title, Locale.GetDialogString("UploadImages.NoConfig"), Locale.GetString("Cancel"), Locale.GetDialogString("UploadImages.OpenSettings")).ShowAsync(AppViewModel.XamlRoot);
                    if (open == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                        AppViewModel.NavigateCommand.Execute("Settings/Image");
                    return false;
                }

                var status = new TextBlock { Text = string.Format(Locale.GetDialogString("UploadImages.Progress"), 1, "…"), TextWrapping = TextWrapping.Wrap };
                var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 0), MinWidth = 320 };
                var panel = new StackPanel();
                panel.Children.Add(status);
                panel.Children.Add(bar);
                var progressDialog = AppContentDialog.Create(title, panel, Locale.GetString("Cancel"));
                using var cancel = new CancellationTokenSource();
                var finished = false;
                var shown = progressDialog.ShowAsync(AppViewModel.XamlRoot);
                _ = shown.ContinueWith(_ => { if (!finished) cancel.Cancel(); }, TaskScheduler.FromCurrentSynchronizationContext());
                var progress = new Progress<(int done, int total)>(p =>
                {
                    status.Text = string.Format(Locale.GetDialogString("UploadImages.Progress"), Math.Min(p.done + 1, p.total), p.total);
                    bar.IsIndeterminate = false;
                    bar.Maximum = Math.Max(p.total, 1);
                    bar.Value = p.done;
                });
                ImageBatchUpload.Result result;
                try
                {
                    result = await batch.RunAsync(progress, cancel.Token);
                }
                finally
                {
                    finished = true;
                    progressDialog.Close();
                    await shown;
                }
                if (result.NoConfig)
                    return false;

                var summary = string.Format(Locale.GetDialogString(result.Cancelled ? "UploadImages.Cancelled" : "UploadImages.Done"), result.Uploaded, result.Files);
                if (result.Reused > 0)
                    summary += " " + string.Format(Locale.GetDialogString("UploadImages.Reused"), result.Reused);
                object content = summary;
                if (result.Failures.Count > 0)
                {
                    var details = new StackPanel { Spacing = 8 };
                    details.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
                    details.Children.Add(new TextBlock { Text = Locale.GetDialogString("UploadImages.NotUploaded"), TextWrapping = TextWrapping.Wrap });
                    details.Children.Add(new ScrollViewer
                    {
                        MaxHeight = 240,
                        Content = new TextBlock
                        {
                            Text = string.Join("\n", result.Failures.Select(f => $"{f.Address}: {f.Reason}")),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true,
                        },
                    });
                    content = details;
                }
                await AppContentDialog.Create(title, content, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return !result.Cancelled;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return false;
            }
        }

        // The page could not make the export's HTML (a renderer failed): said here, with its reason.
        private async Task<bool> ShowExportError(JToken args)
        {
            var error = args?["error"]?.ToString();
            if (string.IsNullOrEmpty(error)) return false;
            Log.Debug($"export: the page could not make the HTML: {LogText.Error(error)}");
            await AppContentDialog.Create(Locale.GetDialogString("ExportErrorTitle"), error, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
            return true;
        }

        private async Task<bool> PrintHTML(JToken args)
        {
            try
            {
                if (await ShowExportError(args)) return false;
                var html = args["html"].ToString();
                var fileExport = ServiceProvider.GetService<IFileExport>();
                await fileExport.Print(Path.GetDirectoryName(FilePath), html, FileName);
                return true;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return false;
            }
        }

        private async Task<bool> ExportCallback(JToken args)
        {
            try
            {
                if (await ShowExportError(args)) return false;
                var html = args["html"].ToString();
                var filePath = args["context"]["filePath"].ToString();
                var configId = args["context"]["configId"].ToObject<int>();
                var config = await ServiceProvider.GetService<IFileExport>().GetExportConfig(configId);
                await config.LoadExportConfig().Export(ServiceProvider, html, filePath);
                Exported(filePath);
                return true;
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetString("Error"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                return false;
            }
        }

        private bool askToSaveOpened;

        public async Task<bool> AskToSave()
        {
            // Bring the editor's latest text in before deciding there is nothing to save. If the editor did not
            // answer (timeout), do not trust the stale Saved flag — fall through to auto-save or the prompt, so a
            // just-typed edit is not dropped on close.
            var flushed = await EditorViewModel.FlushContentAsync();
            if ((flushed && EditorViewModel.Saved) || (SettingsViewModel.AutoSave && await AutoSaveFile()))
            {
                return true;
            }
            if (askToSaveOpened)
            {
                return false;
            }
            askToSaveOpened = true;
            ContentDialogResult result;
            try
            {
                result = await AppContentDialog.Create(
                    Locale.GetDialogString("AsKToSaveTitle"),
                    Locale.GetDialogString("AsKToSaveContent"),
                    Locale.GetDialogString("Cancel"),
                    Locale.GetDialogString("Save"),
                    Locale.GetDialogString("Don'tSave")).ShowAsync(AppViewModel.XamlRoot);
            }
            finally
            {
                // Reset whatever happens: left set, no tab and no window could be closed again.
                askToSaveOpened = false;
            }
            switch (result)
            {
                case ContentDialogResult.Primary:
                    var saveResult = await Save();
                    return saveResult;
                case ContentDialogResult.Secondary:
                    AutoBackup.DeleteBackup(FilePath, TabsViewModel?.ActiveTab?.DocumentId);
                    return true;
                case ContentDialogResult.None:
                    return false;
            }
            return false;
        }

        /// <summary>
        /// Reopens the documents of the last session as tabs (only for the first window, before the editor is up).
        /// The active one is loaded into the editor state (no message: the editor gets it from GetSettings); the others
        /// come back as tabs not read yet, read after the page has its document (<see cref="SessionLoaded"/>) or when
        /// switched to. Every file was read first, one after another, and the page waited for them all: a blank window
        /// for seconds with long documents among the tabs (E2E SR01). Returns false when nothing was restored.
        /// </summary>
        private async Task<bool> RestoreSession()
        {
            var session = SessionMemory.Load();
            if (session == null || TabsViewModel == null) return false;
            var files = session.Files.Where(f => !TryGetOpenedWindow(f, out _)).ToList();
            if (files.Count == 0) return false;
            var activeFile = session.ActiveIndex < session.Files.Count && files.Contains(session.Files[session.ActiveIndex]) ? session.Files[session.ActiveIndex] : files[0];
            // The tabs first, nothing read: they show at once, whatever reading the files waits for (on w10 the strip
            // came only with the active file read, 2.6 s after the window). The active document goes into the pristine
            // untitled tab, which stays where the active file is in the list.
            var blank = TabsViewModel.ActiveTab;
            var before = true;
            foreach (var file in files)
            {
                if (file == activeFile) { before = false; continue; }
                if (TabsViewModel.FindByPath(file) != null) continue;
                var placeholder = new DocumentTab { FilePath = file, Pending = true };
                if (before) TabsViewModel.Tabs.Insert(Math.Max(0, TabsViewModel.Tabs.IndexOf(blank)), placeholder);
                else TabsViewModel.Tabs.Add(placeholder);
            }
            Log.Debug($"RestoreSession: {TabsViewModel.Tabs.Count} tab(s) shown, reading {Path.GetFileName(activeFile)}");
            var loaded = false;
            try
            {
                // No tab is opened here: LoadFile reuses the pristine untitled one.
                loaded = await LoadFile(activeFile, true, false);
            }
            catch (Exception ex)
            {
                Log.Debug($"RestoreSession: {activeFile}: {ex.Message}");
            }
            var activeTab = loaded ? TabsViewModel.ActiveTab : null;
            if (activeTab == null && TabsViewModel.Tabs.Contains(blank) && TabsViewModel.Tabs.Count > 1)
                TabsViewModel.Tabs.Remove(blank);
            if (activeTab == null)
            {
                // The active one could not be read: the first of the others instead.
                var first = TabsViewModel.Tabs.FirstOrDefault(t => t.Pending);
                if (first == null) return false;
                await TabsViewModel.SwitchTo(first);
            }
            var pending = TabsViewModel.Tabs.Count(t => t.Pending);
            Log.Debug($"RestoreSession: {TabsViewModel.Tabs.Count} tab(s), active={TabsViewModel.ActiveTab?.FilePath}, {pending} to read after it");
            if (pending > 0) sessionLoading = ReadPendingTabsAsync();
            return true;
        }

        private Task sessionLoading = Task.CompletedTask;

        /// <summary>The last session's other tabs read (or given up on): automation waits for it, as for the startup document.</summary>
        public Task SessionLoaded => sessionLoading;

        // After the page has had its document: the reply to GetSettings goes out first.
        private async Task ReadPendingTabsAsync()
        {
            await Task.Delay(200);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var tab in TabsViewModel.Tabs.Where(t => t.Pending).ToList())
            {
                try
                {
                    await LoadIntoTab(tab, mayAsk: false);
                }
                catch (Exception ex)
                {
                    Log.Debug($"RestoreSession: {tab.FilePath}: {ex.Message} (read when switched to)");
                }
            }
            Log.Debug($"RestoreSession: the other tabs read in {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Reads a session tab's file into the tab itself, the editor untouched (it shows another document). A backup that
        /// differs is taken without asking when unsaved changes are kept on close; otherwise it is asked about only with
        /// <paramref name="mayAsk"/> (the tab being switched to) - false: the tab stays not read. Throws when the file
        /// cannot be read.
        /// </summary>
        public async Task<bool> LoadIntoTab(DocumentTab tab, bool mayAsk)
        {
            if (tab == null || !tab.Pending) return true;
            var path = tab.FilePath;
            if (!File.Exists(path)) throw new FileNotFoundException("File does not exist.", path);
            var (text, format) = await TextFileFormat.ReadAsync(path);
            var fileHash = Common.SimpleHash(text);
            string markdown = text;
            var backup = await AutoBackup.GetBackup(path);
            if (backup != null && Common.SimpleHash(backup) != fileHash)
            {
                if (SettingsViewModel.KeepUnsavedOnClose) markdown = backup;
                else if (!mayAsk) return false;
                else markdown = await CheckBackup(path, fileHash) ?? text;
            }
            // Read meanwhile (switched to while this one was reading): that one counts.
            if (!tab.Pending) return true;
            EditionHooks.ReportDocumentOnDisk(path, text, written: false);
            tab.FileFormat = format;
            tab.Markdown = markdown;
            tab.FileHash = fileHash;
            tab.DiskHash = fileHash;
            tab.CurrentHash = Common.SimpleHash(markdown);
            tab.Saved = tab.CurrentHash == fileHash;
            tab.AutoSavedSucc = true;
            tab.AutoSaveHeldBack = null;
            // As LoadFile leaves the editor: a backup taken counts as loaded (its history is the backup's).
            tab.FileLoaded = !tab.Saved;
            tab.History.InitHistory(markdown);
            tab.IsDirty = !tab.Saved;
            tab.Pending = false;
            return true;
        }

        /// <summary>The window's start brings the last session back (no file asked for on the command line).</summary>
        public bool StartsWithSession => string.IsNullOrEmpty(CommandLine.GetOpenFilePath(AppViewModel.CommandLineArgs))
            && (SettingsViewModel.KeepUnsavedOnClose || SettingsViewModel.FileStartupAction == Enums.FileStartupAction.RestoreSession);

        public async Task LoadStartUpMarkdown()
        {
            var path = CommandLine.GetOpenFilePath(AppViewModel.CommandLineArgs);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    await LoadFile(path, true, false);
                }
                catch (Exception)
                {
                    await NewFileFun(false);
                }
            }
            SessionMemory.Session session = null;
            if (string.IsNullOrEmpty(path))
            {
                // Documents kept when the window closed (Settings > Keep unsaved changes when closing) come back with the
                // tabs they were in, whatever the start-up setting says.
                var action = SettingsViewModel.KeepUnsavedOnClose ? Enums.FileStartupAction.RestoreSession : SettingsViewModel.FileStartupAction;
                if (action == Enums.FileStartupAction.RestoreSession) session = SessionMemory.Load();
                switch (action)
                {
                    case Enums.FileStartupAction.OpenLast:
                        await AccessHistory.EnsureInitialized();
                        if (AccessHistory.FileRecentlyOpened.FirstOrDefault() is string lastFile && !TryGetOpenedWindow(lastFile, out _) && File.Exists(lastFile))
                            await LoadFile(lastFile, true);
                        break;
                    case Enums.FileStartupAction.RestoreSession:
                        if (!await RestoreSession())
                            await NewFileFun(false);
                        break;
                    default:
                        await NewFileFun(false);
                        break;
                }
            }
            // Nothing to show (no file asked for, the session's or the last file gone, a new document): the home page,
            // when the settings want it. Before the recovery question, which a recovered document then answers.
            Home?.ShowIfWanted();
            await RecoverUntitledBackupsAsync();
            await RestoreSessionTabs(session);
        }

        private static bool untitledRecoveryDone;

        private async Task<bool> AskToRecover()
        {
            var dialog = AppContentDialog.Create();
            dialog.Title = Locale.GetDialogString("RecoverTitle");
            dialog.Content = Locale.GetDialogString("RecoverContent");
            dialog.PrimaryButtonText = Locale.GetDialogString("Recover");
            dialog.SecondaryButtonText = Locale.GetDialogString("Delete");
            dialog.DefaultButton = ContentDialogButton.Primary;
            return await dialog.ShowAsync(AppViewModel.XamlRoot) == ContentDialogResult.Primary;
        }

        /// <summary>
        /// Untitled documents the last run left unsaved (it crashed, or was killed) come back as tabs of the first window,
        /// each with its own document id - once per process, after asking. Recover reopens them; Delete throws them away.
        /// </summary>
        private async Task RecoverUntitledBackupsAsync()
        {
            if (untitledRecoveryDone || TabsViewModel == null) return;
            untitledRecoveryDone = true;
            var backups = AutoBackup.UntitledBackups();
            if (backups.Count == 0) return;
            if (!SettingsViewModel.KeepUnsavedOnClose && !await AskToRecover())
            {
                foreach (var (documentId, _) in backups) AutoBackup.DeleteBackup(null, documentId);
                return;
            }
            var intoEditor = false;
            foreach (var (documentId, file) in backups)
            {
                string text;
                try { text = await File.ReadAllTextAsync(file); }
                catch (Exception ex) { Log.Debug($"recover untitled {documentId}: {ex.Message}"); continue; }
                if (TabsViewModel.Tabs.Any(t => t.DocumentId == documentId)) continue;
                if (TabsViewModel.IsActiveTabBlank)
                {
                    // Nothing else open (no session): the first one goes into the blank tab, in the editor.
                    TabsViewModel.ActiveTab.RestoreIdentity(documentId);
                    FilePath = null;
                    FileFormat = TextFileFormat.Default;
                    EditorViewModel.FileHash = Common.SimpleHash(Common.DefaultMarkdwn);
                    EditorViewModel.Markdown = text;
                    EditorViewModel.CurrentHash = Common.SimpleHash(text);
                    EditorViewModel.Saved = false;
                    EditorViewModel.AutoSavedSucc = false;
                    EditorViewModel.FileLoaded = true;
                    EditorViewModel.History.InitHistory(text);
                    TabsViewModel.ActiveTab.BackupHash = EditorViewModel.CurrentHash;
                    intoEditor = true;
                    continue;
                }
                // The others come back behind the active one, as the session's other tabs do: each made the active tab
                // in turn, the start showed the last of them instead of the tab the reader had closed on.
                var tab = new DocumentTab
                {
                    Markdown = text,
                    FileHash = Common.SimpleHash(Common.DefaultMarkdwn),
                    CurrentHash = Common.SimpleHash(text),
                    Saved = false,
                    AutoSavedSucc = false,
                    FileLoaded = true,
                    IsDirty = true,
                };
                tab.RestoreIdentity(documentId);
                tab.History.InitHistory(text);
                tab.BackupHash = tab.CurrentHash;
                TabsViewModel.Tabs.Add(tab);
            }
            Log.Debug($"recovered {backups.Count} untitled document(s)");
            if (intoEditor) EditorViewModel.PostLoadFile(EditorViewModel.Markdown);
        }

        /// <summary>
        /// Once the untitled documents are back from their backups: the tabs in the order they were closed in (the untitled
        /// ones came after the files), and the tab the reader closed on shown - an untitled one is named by its document id,
        /// having no file in the list.
        /// </summary>
        private async Task RestoreSessionTabs(SessionMemory.Session session)
        {
            var tabs = TabsViewModel;
            if (session == null || tabs == null) return;
            if (session.Order is { Count: > 0 } order)
            {
                var place = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < order.Count; i++) place.TryAdd(order[i], i);
                int Place(DocumentTab t) => place.TryGetValue(SessionMemory.OrderKey(t == tabs.ActiveTab ? FilePath : t.FilePath, t.DocumentId), out var i) ? i : int.MaxValue;
                // Stable: tabs the session did not have keep their order, after the ones it had.
                var wanted = tabs.Tabs.Select((t, i) => (Tab: t, Place: Place(t), Now: i)).OrderBy(x => x.Place).ThenBy(x => x.Now).Select(x => x.Tab).ToList();
                for (var i = 0; i < wanted.Count; i++)
                {
                    var now = tabs.Tabs.IndexOf(wanted[i]);
                    if (now != i) tabs.Tabs.Move(now, i);
                }
            }
            if (session.ActiveDocumentId is string id && tabs.Tabs.FirstOrDefault(t => t.DocumentId == id) is { } shown && shown != tabs.ActiveTab)
            {
                Log.Debug("RestoreSession: back on the untitled document the session was closed on");
                await tabs.SwitchTo(shown);
            }
        }

        private async void Export(ExportConfig config)
        {
            try
            {
                await ExportAsync(config);
            }
            catch (Exception ex)
            {
                // A configuration that cannot be read (its JSON, a bad extension) or a picker that fails: said, not the
                // app ended.
                Log.Debug($"export: {ex}");
                await AppContentDialog.Create(Locale.GetDialogString("ExportErrorTitle"), ex.Message, Locale.GetString("Ok")).ShowAsync(AppViewModel.XamlRoot);
            }
        }

        /// <summary>
        /// An export written to <paramref name="filePath"/>: what Settings > Export > After export says - a notice, the
        /// file or its folder opened, or nothing. Every export format comes here (the edition's too).
        /// </summary>
        public void Exported(string filePath)
        {
            Log.Debug($"export: written {filePath}, then {SettingsViewModel.AfterExport}");
            try
            {
                switch (SettingsViewModel.AfterExport)
                {
                    case Enums.ExportAfterAction.Notify: AppViewModel.UIViewModel.ShowExported(filePath); break;
                    case Enums.ExportAfterAction.OpenFile: Common.OpenFile(filePath); break;
                    case Enums.ExportAfterAction.OpenFolder: Common.OpenFileLocation(filePath); break;
                }
            }
            catch (Exception ex)
            {
                // No program for the file type, say: the export itself is done, so the notice says where it is.
                Log.Debug($"export: after export failed: {ex.Message}");
                AppViewModel.UIViewModel.ShowExported(filePath);
            }
        }

        /// <summary>An export with the file already chosen, as after the save dialog (the test host skips the dialog).</summary>
        public void ExportTo(ExportConfig config, string filePath)
        {
            string basePath = null;
            if (config.Type == Enums.ExportType.PDF || config.Type == Enums.ExportType.Image)
                basePath = ImageBasePath;
            MarkdownEditor?.PostMessage("Export", new
            {
                type = "export",
                title = Path.GetFileNameWithoutExtension(filePath),
                context = new { configId = config.Id, filePath },
                basePath,
                options = config.LoadExportConfig()
            });
        }

        private async Task ExportAsync(ExportConfig config)
        {
            var filePicker = new FileSavePicker();
            filePicker.SetOwnerWindow(AppViewModel.MainWindow);
            config.FileExtensions.ForEach(x => filePicker.FileTypeChoices.Add(x.name, new List<string> { x.extension }));
            // Default to the document's own name (a PDF of readme.md is offered as readme.pdf), the extension
            // coming from the chosen format; an unsaved document has no name, so fall back to "untitled".
            filePicker.SuggestedFileName = FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : (FileName ?? "untitled");
            var file = await FilePickersExtensions.ShowAsync(() => filePicker.PickSaveFileAsync());
            if (file == null) return;
            ExportTo(config, file.Path);
        }

        private void Print()
        {
            MarkdownEditor?.PostMessage("Export", new
            {
                type = "print",
                basePath = ImageBasePath,
                title = FileName ?? "untitled"
            });
        }

        private async void Import()
        {
            try
            {
                var filePicker = new FileOpenPicker() { FileTypeFilter = { ".html" } };
                filePicker.SetOwnerWindow(AppViewModel.MainWindow);
                var file = await FilePickersExtensions.ShowAsync(() => filePicker.PickSingleFileAsync());
                if (file != null)
                {
                    var text = await File.ReadAllTextAsync(file.Path);
                    MarkdownEditor?.PostMessage("ImportFile", new { type = Path.GetExtension(file.Path).Substring(1), text });
                }
            }
            catch (Exception ex)
            {
                await AppContentDialog.Create(Locale.GetDialogString("ImportErrorTitle"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
            }
        }

        private async void OnStartup()
        {
            try
            {
                if (string.IsNullOrEmpty(WorkFolder))
                {
                    switch (SettingsViewModel.FolderStartupAction)
                    {
                        case Enums.FolderStartupAction.OpenLast:
                            await AccessHistory.EnsureInitialized();
                            if (AccessHistory.FolderRecentlyOpened.FirstOrDefault() is string lastFolder && Directory.Exists(lastFolder))
                                await LoadFolder(lastFolder);
                            break;
                        case Enums.FolderStartupAction.OpenFolder:
                            if (Directory.Exists(SettingsViewModel.StartupOpenFolder))
                                await LoadFolder(SettingsViewModel.StartupOpenFolder);
                            break;
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        public static bool TryGetOpenedWindow(string filePath, out IntPtr window)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                window = default;
                return false;
            }
            window = AppViewModel.GetInstances().Where(x => x.FileViewModel.FilePath?.ToLower() == filePath.ToLower() || (x.TabsViewModel?.IsOpenInAnyTab(filePath) ?? false)).FirstOrDefault()?.MainWindow ?? default;
            return window != default;
        }

        public bool RenameFile(string to)
        {
            if (!File.Exists(FilePath))
                return false;
            var fileOperation = ServiceProvider.GetService<IFileOperation>();
            var oldPath = FilePath;
            if (fileOperation.Rename(oldPath, to))
            {
                Favorites.Shared.Rename(oldPath, to);
                FilePath = to;
                EditionHooks.ReportActivity(ActivityKind.DocumentRenamed, AppViewModel, oldPath);
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            CursorMemory.Flush();
            saveFileTimer.Stop();
            fileReloadTimer.Stop();
            StopWatchFile();
            disposables.Dispose();
        }

        private void OnFilePathChanged()
        {
            StartWatchFile();
        }

        private void IgnoreOwnFileWrite()
        {
            ignoreExternalChangeUntil = DateTime.UtcNow.AddMilliseconds(1000);
        }

        private void StartWatchFile()
        {
            StopWatchFile();
            if (!SettingsViewModel.AutoReload || string.IsNullOrEmpty(FilePath))
                return;
            var dir = Path.GetDirectoryName(FilePath);
            var name = Path.GetFileName(FilePath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) || !Directory.Exists(dir))
                return;
            try
            {
                fileWatcher = new FileSystemWatcher
                {
                    Path = dir,
                    Filter = name,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false
                };
                fileWatcher.Changed += OnDiskFileEvent;
                fileWatcher.Created += OnDiskFileEvent;
                fileWatcher.Deleted += OnDiskFileEvent;
                fileWatcher.Renamed += OnDiskFileRenamed;
                fileWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                StopWatchFile();
            }
        }

        private void StopWatchFile()
        {
            if (fileWatcher == null)
                return;
            fileWatcher.EnableRaisingEvents = false;
            fileWatcher.Changed -= OnDiskFileEvent;
            fileWatcher.Created -= OnDiskFileEvent;
            fileWatcher.Deleted -= OnDiskFileEvent;
            fileWatcher.Renamed -= OnDiskFileRenamed;
            fileWatcher.Dispose();
            fileWatcher = null;
        }

        private void OnDiskFileEvent(object sender, FileSystemEventArgs e)
        {
            ScheduleReloadFromDisk();
        }

        // A saved file is written by replacing it: the content goes to a temporary file, and the original is
        // renamed aside before the new one takes its place. That internal rename must not be mistaken for the
        // user renaming the file in Explorer — following it pointed the editor at a "*.md~RF….TMP" backup.
        private static bool LooksLikeSaveArtifact(string path)
        {
            var name = Path.GetFileName(path) ?? string.Empty;
            return name.EndsWith(".TMP", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("~RF") ||
                   (name.StartsWith(".") && name.Contains(".tmp"));
        }

        private void OnDiskFileRenamed(object sender, RenamedEventArgs e)
        {
            dispatcherQueue?.TryEnqueue(() =>
            {
                if (disposables.IsDisposed)
                    return;
                // The document itself given another name: followed even just after our own write. The atomic replace
                // never renames the document to a name of its own (only to a save artifact, ~RF….TMP), and a rename
                // ignored here was ignored for good - the window kept a name the file no longer had.
                if (!string.IsNullOrEmpty(FilePath) &&
                    string.Equals(e.OldFullPath, FilePath, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(e.FullPath) &&
                    !LooksLikeSaveArtifact(e.FullPath))
                {
                    var oldPath = FilePath;
                    FilePath = e.FullPath;
                    _ = AccessHistory.RecordFileHistory(FilePath);
                    EditionHooks.ReportActivity(ActivityKind.DocumentRenamed, AppViewModel, oldPath);
                    return;
                }
                // Our own write, in progress: the rename is part of the atomic replace, not the user's doing.
                if (DateTime.UtcNow < ignoreExternalChangeUntil)
                    return;
                ScheduleReloadFromDisk();
            });
        }

        private void ScheduleReloadFromDisk()
        {
            if (DateTime.UtcNow < ignoreExternalChangeUntil)
                return;
            dispatcherQueue?.TryEnqueue(() =>
            {
                if (disposables.IsDisposed || DateTime.UtcNow < ignoreExternalChangeUntil)
                    return;
                fileReloadTimer.Stop();
                fileReloadTimer.Start();
            });
        }

        private async void FileReloadTimerTick(object sender, object e)
        {
            fileReloadTimer.Stop();
            if (disposables.IsDisposed || DateTime.UtcNow < ignoreExternalChangeUntil)
                return;
            await HandleExternalFileChange();
        }

        private async Task HandleExternalFileChange()
        {
            if (!SettingsViewModel.AutoReload || string.IsNullOrEmpty(FilePath) || reloadDialogOpened)
                return;

            var path = FilePath;
            string text = null;
            TextFileFormat format = null;
            for (var i = 0; i < 10; i++)
            {
                if (disposables.IsDisposed || FilePath != path)
                    return;
                try
                {
                    if (!File.Exists(path))
                    {
                        if (EditorViewModel.Saved)
                        {
                            EditorViewModel.FileHash = EditorViewModel.CurrentHash == 0 ? 1UL : 0UL;
                            EditorViewModel.Saved = false;
                        }
                        return;
                    }
                    var reloaded = await TextFileFormat.ReadAsync(path);
                    text = reloaded.Text;
                    // Hold the format locally; committing it to the shared FileFormat here — before the identity
                    // re-check below — would stamp this file's encoding/line-ending onto whatever document the
                    // reader switched to while the read was in flight.
                    format = reloaded.Format;
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
                catch
                {
                    return;
                }
            }
            if (text == null || disposables.IsDisposed || FilePath != path)
                return;
            FileFormat = format;

            var diskHash = Common.SimpleHash(text);
            if (diskHash == EditorViewModel.FileHash || diskHash == DiskHash)
                return;
            if (diskHash == EditorViewModel.CurrentHash)
            {
                EditorViewModel.FileHash = diskHash;
                EditorViewModel.Saved = true;
                return;
            }

            if (EditorViewModel.Saved && !SettingsViewModel.AskBeforeReload)
            {
                ApplyDiskText(text);
                return;
            }

            reloadDialogOpened = true;
            try
            {
                var contentKey = EditorViewModel.Saved ? "ReloadFromDiskContentClean" : "ReloadFromDiskContent";
                var contentFallback = EditorViewModel.Saved
                    ? "磁盘上的文件已更改，是否重新加载？"
                    : "磁盘上的文件已更改。重新加载将丢失当前未保存的更改。";
                var result = await AppContentDialog.Create(
                    Locale.GetDialogString("ReloadFromDiskTitle") ?? "文件已在外部被修改",
                    Locale.GetDialogString(contentKey) ?? contentFallback,
                    Locale.GetDialogString("Keep") ?? "保留",
                    Locale.GetDialogString("Reload") ?? "重新加载").ShowAsync(AppViewModel.XamlRoot);
                if (disposables.IsDisposed || FilePath != path)
                    return;
                if (result == ContentDialogResult.Primary)
                    ApplyDiskText(text);
                else
                {
                    EditorViewModel.FileHash = diskHash;
                    DiskHash = diskHash;
                    EditorViewModel.Saved = EditorViewModel.CurrentHash == diskHash;
                }
            }
            finally
            {
                reloadDialogOpened = false;
            }
        }

        /// <summary>
        /// The file read again in the encoding the reader chose (the status bar's encoding menu) - one with no byte order
        /// mark, read as UTF-8, that is GBK or Big5. Only a document with nothing unsaved: what was typed would be lost.
        /// </summary>
        public async Task<bool> ReopenWithEncodingAsync(string encodingName)
        {
            var path = FilePath;
            if (string.IsNullOrEmpty(path) || !EditorViewModel.Saved || !File.Exists(path)) return false;
            var (text, format) = await TextFileFormat.ReadAsync(path, TextFileFormat.EncodingByName(encodingName));
            if (disposables.IsDisposed || FilePath != path || !EditorViewModel.Saved) return false;
            FileFormat = format;
            ApplyDiskText(text);
            return true;
        }

        /// <summary>The file to be written with this line ending ("\n", "\r\n"): the document turns unsaved.</summary>
        public void ChangeLineEnding(string lineEnding)
        {
            if (FileFormat.LineEnding == lineEnding) return;
            FileFormat = FileFormat.WithLineEnding(lineEnding);
            MarkUnsavedFormat();
        }

        /// <summary>The file to be written as UTF-8 (the text as shown): the document turns unsaved.</summary>
        public void ConvertToUtf8()
        {
            FileFormat = FileFormat.AsUtf8();
            EditorViewModel.AutoSaveHeldBack = null;
            MarkUnsavedFormat();
        }

        // The text is unchanged but the bytes on disk will not be: saved as when the file went missing, so a save (and
        // auto-save) writes it.
        private void MarkUnsavedFormat()
        {
            if (FilePath == null) return;
            EditorViewModel.FileHash = EditorViewModel.CurrentHash == 0 ? 1UL : 0UL;
            EditorViewModel.Saved = false;
        }

        private void ApplyDiskText(string text)
        {
            if (!string.Equals(EditorViewModel.Markdown, text, StringComparison.Ordinal))
                TabsViewModel?.ActiveTab?.NoteTextChanged();
            EditorViewModel.FirstStart = false;
            EditorViewModel.FileHash = Common.SimpleHash(text);
            DiskHash = EditorViewModel.FileHash;
            EditorViewModel.Markdown = text;
            EditorViewModel.CurrentHash = EditorViewModel.FileHash;
            EditorViewModel.Saved = true;
            EditorViewModel.FileLoaded = false;
            EditorViewModel.AutoSavedSucc = true;
            EditorViewModel.AutoSaveHeldBack = null;
            EditorViewModel.History.InitHistory(text);
            AutoBackup.DeleteBackup(FilePath);
            EditorViewModel.PostLoadFile(text);
        }

        private void Exit()
        {
            Utilities.Log.Debug($"exit requested\n{Environment.StackTrace}");
            var SC_CLOSE = 0xF060;
            PInvoke.PostMessage(AppViewModel.MainWindow, (uint)PInvoke.WindowMessage.WM_SYSCOMMAND, (nint)SC_CLOSE, IntPtr.Zero);
        }
    }
}
