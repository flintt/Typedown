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
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

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

        public string WorkFolder { get; private set; } = null;

        /// <summary>True when the user chose the workspace folder (Open Folder), false when it was restored at startup.</summary>
        public bool WorkFolderIsExplicit { get; private set; }

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
        public Command<Unit> ClearHistoryCommand { get; } = new();
        public Command<Unit> SaveCommand { get; } = new();
        public Command<Unit> SaveAsCommand { get; } = new();
        public Command<Unit> ImportCommand { get; } = new();
        public Command<ExportConfig> ExportCommand { get; } = new();
        public Command<Unit> PrintCommand { get; } = new();
        public Command<Unit> ShareToHedgeDocCommand { get; } = new();
        public Command<Unit> ExitCommand { get; } = new();

        private readonly DispatcherTimer saveFileTimer = new();
        private readonly SemaphoreSlim saveLock = new(1, 1);
        private bool saveTimerRunning;

        private readonly DispatcherTimer fileReloadTimer = new();

        private FileSystemWatcher fileWatcher;

        private DispatcherQueue dispatcherQueue;

        private DateTime ignoreExternalChangeUntil = DateTime.MinValue;

        private bool reloadDialogOpened;

        public AutoBackup AutoBackup => ServiceProvider.GetService<AutoBackup>();

        public IMarkdownEditor MarkdownEditor => ServiceProvider.GetService<IMarkdownEditor>();

        private readonly CompositeDisposable disposables = new();

        public FileViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            NewFileCommand.OnExecute.Subscribe(async _ => await NewFileFun());
            OpenFileCommand.OnExecute.Subscribe(async x => await OpenFile(x));
            OpenFolderCommand.OnExecute.Subscribe(async x => await OpenFolder(x));
            SaveAsCommand.OnExecute.Subscribe(async _ => await SaveAs());
            SaveCommand.OnExecute.Subscribe(async _ => await Save());
            ExitCommand.OnExecute.Subscribe(_ => Exit());
            ClearHistoryCommand.OnExecute.Subscribe(x => { _ = AccessHistory.ClearHistory(); });
            ExportCommand.OnExecute.Subscribe(Export);
            PrintCommand.OnExecute.Subscribe(_ => Print());
            ShareToHedgeDocCommand.OnExecute.Subscribe(_ => ShareToHedgeDoc());
            ImportCommand.OnExecute.Subscribe(_ => Import());
            RemoteInvoke.Handle<JToken, bool>("ExportCallback", ExportCallback);
            RemoteInvoke.Handle<JToken, bool>("PrintHTML", PrintHTML);
            saveFileTimer.Interval = TimeSpan.FromSeconds(5);
            saveFileTimer.Tick += SaveFileTimerTick;
            saveFileTimer.Start();
            dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            fileReloadTimer.Interval = TimeSpan.FromMilliseconds(400);
            fileReloadTimer.Tick += FileReloadTimerTick;
            disposables.Add(SettingsViewModel.WhenPropertyChanged(nameof(SettingsViewModel.AutoReload)).Subscribe(_ => StartWatchFile()));
            _ = CoreApplication.GetCurrentView().CoreWindow.Dispatcher.RunIdleAsync(() => OnStartup());
        }

        private async void SaveFileTimerTick(object sender, object e)
        {
            if (disposables.IsDisposed || saveTimerRunning)
            {
                return;
            }
            CursorMemory.Flush();
            saveTimerRunning = true;
            try
            {
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

        private async Task<bool> AutoBackupFile()
        {
            if (EditorViewModel.FileHash != EditorViewModel.CurrentHash && !string.IsNullOrWhiteSpace(EditorViewModel.Markdown))
                return await AutoBackup.Backup(FilePath, EditorViewModel.Markdown);
            else
                AutoBackup.DeleteBackup(FilePath);
            return true;
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
            // Tabs (upstream #73): a new document opens in its own tab unless the current one is a pristine untitled tab.
            if (inNewTab && TabsViewModel != null && !TabsViewModel.IsActiveTabBlank)
                TabsViewModel.BeginNewTab();
            else if (!await AskToSave())
                return;
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
            if (!await LoadFolder(folderPath))
                return false;
            WorkFolderIsExplicit = true;
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
                    _ = AppViewModel.XamlRoot?.Content?.Dispatcher?.RunIdleAsync(() => PInvoke.SetForegroundWindow(window));
                    return false;
                }
                if (!File.Exists(path))
                {
                    _ = AccessHistory.RemoveFileHistory(path);
                    throw new FileNotFoundException("File does not exist.");
                }
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
                EditorViewModel.History.InitHistory(EditorViewModel.Markdown);
                if (postMessage)
                {
                    var cursor = SettingsViewModel.RememberCursorPosition ? CursorMemory.Get(path) : null;
                    EditorViewModel.PostLoadFile(EditorViewModel.Markdown, cursor);
                }
                if (TabsViewModel != null)
                    TabsViewModel.ActiveTab.IsPreview = preview;
                FileOpened?.Invoke(path, preview);
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

        private async Task<bool> LoadFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    _ = AccessHistory.RemoveFolderHistory(folderPath);
                    throw new FileNotFoundException("Folder does not exist.");
                }
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
                // An empty buffer over a file that has content, while the editor has not yet handed this document
                // back, is not a document the reader emptied: it is a save that fired between opening the file
                // and the editor answering (a hang, a crash-reload, an exit on the way). A reader lost an
                // afternoon's work to a file "overwritten with 0K"; nothing here writes that.
                if (text.Length == 0 && !EditorViewModel.FileLoaded && File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    Log.Debug($"save skipped: the editor has not loaded this document yet, and the buffer is empty while the file has {new FileInfo(path).Length} bytes");
                    return false;
                }
                IgnoreOwnFileWrite();
                await SafeFile.WriteAllBytesAtomicAsync(path, (FileFormat ?? TextFileFormat.Default).GetBytes(text));
                IgnoreOwnFileWrite();
                return true;
            }
            catch (Exception ex)
            {
                if (alert)
                {
                    await AppContentDialog.Create(Locale.GetDialogString("SaveErrorTitle"), ex.Message, Locale.GetDialogString("Ok")).ShowAsync(AppViewModel.XamlRoot);
                }
                return false;
            }
        }

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
                await EditorViewModel.FlushContentAsync(); // what we write must be what is on screen
                var markdown = EditorViewModel.Markdown;
                var hash = Common.SimpleHash(markdown);
                var result = await WriteAllText(path, markdown, alert);
                if (result && !disposables.IsDisposed && FilePath == path)
                {
                    EditorViewModel.FileHash = hash;
                    DiskHash = hash;
                    EditorViewModel.Saved = EditorViewModel.CurrentHash == hash; // CurrentHash tracks the live buffer; avoids an O(n) string compare per save
                    if (EditorViewModel.Saved)
                        AutoBackup.DeleteBackup(path);
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
                var filePicker = new FileSavePicker();
                filePicker.SetOwnerWindow(AppViewModel.MainWindow);
                filePicker.FileTypeChoices.Add("Markdown Files", FileTypeHelper.Markdown.ToList());
                filePicker.SuggestedFileName = FileName ?? "untitled";
                var file = await filePicker.PickSaveFileAsync();
                if (file != null && !disposables.IsDisposed && FilePath == originalPath)
                {
                    await EditorViewModel.FlushContentAsync();
                    var markdown = EditorViewModel.Markdown;
                    var hash = Common.SimpleHash(markdown);
                    var result = await WriteAllText(file.Path, markdown);
                    if (result && !disposables.IsDisposed && FilePath == originalPath)
                    {
                        FilePath = file.Path;
                        EditorViewModel.FileHash = hash;
                        DiskHash = hash;
                        EditorViewModel.Saved = EditorViewModel.CurrentHash == hash; // CurrentHash tracks the live buffer; avoids an O(n) string compare per save
                        if (EditorViewModel.Saved)
                            AutoBackup.DeleteBackup(originalPath);
                        _ = AccessHistory.RecordFileHistory(FilePath);
                        return EditorViewModel.Saved ? file.Path : null;
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
                    if (choice == Windows.UI.Xaml.Controls.ContentDialogResult.Secondary)
                    {
                        await HedgeDocShareDialog.ShowAsync(AppViewModel.XamlRoot, previousResult, null);
                        return;
                    }
                    if (choice != Windows.UI.Xaml.Controls.ContentDialogResult.Primary)
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

        private async Task<bool> PrintHTML(JToken args)
        {
            try
            {
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
                var html = args["html"].ToString();
                var filePath = args["context"]["filePath"].ToString();
                var configId = args["context"]["configId"].ToObject<int>();
                var config = await ServiceProvider.GetService<IFileExport>().GetExportConfig(configId);
                await config.LoadExportConfig().Export(ServiceProvider, html, filePath);
                if (SettingsViewModel.OpenFolderAfterExport)
                    Common.OpenFileLocation(filePath);
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
            if (EditorViewModel.Saved || (SettingsViewModel.AutoSave && await AutoSaveFile()))
            {
                return true;
            }
            if (askToSaveOpened)
            {
                return false;
            }
            askToSaveOpened = true;
            var result = await AppContentDialog.Create(
                Locale.GetDialogString("AsKToSaveTitle"),
                Locale.GetDialogString("AsKToSaveContent"),
                Locale.GetDialogString("Cancel"),
                Locale.GetDialogString("Save"),
                Locale.GetDialogString("Don'tSave")).ShowAsync(AppViewModel.XamlRoot);
            askToSaveOpened = false;
            switch (result)
            {
                case ContentDialogResult.Primary:
                    var saveResult = await Save();
                    return saveResult;
                case ContentDialogResult.Secondary:
                    AutoBackup.DeleteBackup(FilePath);
                    return true;
                case ContentDialogResult.None:
                    return false;
            }
            return false;
        }

        /// <summary>
        /// Reopens the documents of the last session as tabs (only for the first window, before the editor is up).
        /// Each file is loaded into the editor state in turn (no message: the editor gets the active document from
        /// GetSettings); the previously active tab is switched to at the end. Returns false when nothing was restored.
        /// </summary>
        private async Task<bool> RestoreSession()
        {
            var session = SessionMemory.Load();
            if (session == null || TabsViewModel == null) return false;
            var opened = 0;
            foreach (var file in session.Files)
            {
                if (TryGetOpenedWindow(file, out _)) continue;
                try
                {
                    // No tab is opened here: LoadFile adds one itself whenever the active document is not a
                    // pristine untitled one. Starting a tab first only meant it saw the previous document still
                    // loaded, opened a second tab for the file and left the first as a copy of its predecessor —
                    // two documents came back as three, one of them twice.
                    if (await LoadFile(file, true, false)) opened++;
                }
                catch (Exception ex)
                {
                    Log.Debug($"RestoreSession: {file}: {ex.Message}");
                }
            }
            if (opened == 0) return false;
            var active = TabsViewModel.FindByPath(session.ActiveIndex < session.Files.Count ? session.Files[session.ActiveIndex] : null);
            if (active != null && active != TabsViewModel.ActiveTab)
                await TabsViewModel.SwitchTo(active);
            Log.Debug($"RestoreSession: {opened} tab(s), active={TabsViewModel.ActiveTab?.FilePath}");
            return true;
        }

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
            else
            {
                switch (SettingsViewModel.FileStartupAction)
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
        }

        private async void Export(ExportConfig config)
        {
            var filePicker = new FileSavePicker();
            filePicker.SetOwnerWindow(AppViewModel.MainWindow);
            config.FileExtensions.ForEach(x => filePicker.FileTypeChoices.Add(x.name, new List<string> { x.extension }));
            var file = await filePicker.PickSaveFileAsync();
            if (file == null) return;
            string basePath = null;
            if (config.Type == Enums.ExportType.PDF || config.Type == Enums.ExportType.Image)
                basePath = ImageBasePath;
            MarkdownEditor?.PostMessage("Export", new
            {
                type = "export",
                title = file.DisplayName,
                context = new { configId = config.Id, filePath = file.Path },
                basePath,
                options = config.LoadExportConfig()
            });
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
                var file = await filePicker.PickSingleFileAsync();
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
            if (fileOperation.Rename(FilePath, to))
            {
                FilePath = to;
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

        private void OnDiskFileRenamed(object sender, RenamedEventArgs e)
        {
            dispatcherQueue?.TryEnqueue(() =>
            {
                if (disposables.IsDisposed)
                    return;
                if (!string.IsNullOrEmpty(FilePath) &&
                    string.Equals(e.OldFullPath, FilePath, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(e.FullPath))
                {
                    FilePath = e.FullPath;
                    _ = AccessHistory.RecordFileHistory(FilePath);
                    return;
                }
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
                    FileFormat = reloaded.Format;
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

        private void ApplyDiskText(string text)
        {
            EditorViewModel.FirstStart = false;
            EditorViewModel.FileHash = Common.SimpleHash(text);
            DiskHash = EditorViewModel.FileHash;
            EditorViewModel.Markdown = text;
            EditorViewModel.CurrentHash = EditorViewModel.FileHash;
            EditorViewModel.Saved = true;
            EditorViewModel.FileLoaded = false;
            EditorViewModel.AutoSavedSucc = true;
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
