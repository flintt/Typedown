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
using System.Threading.Tasks;
using Typedown.Core.Controls;
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
        public Command<Unit> ExitCommand { get; } = new();

        private readonly DispatcherTimer saveFileTimer = new();

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
            if (disposables.IsDisposed)
            {
                return;
            }
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

        private async Task NewFileFun(bool postMessage = true)
        {
            if (!await AskToSave()) return;
            FilePath = null;
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
            EditorViewModel.History.InitHistory(Common.DefaultMarkdwn);
            if (postMessage)
            {
                MarkdownEditor?.PostMessage("LoadFile", EditorViewModel.Markdown);
            }
        }

        public async Task<bool> OpenFile(string filePath = null)
        {
            if (!await AskToSave())
                return false;
            filePath ??= await AppViewModel.MainWindow.PickMarkdownFileAsync();
            if (filePath == null)
                return false;
            return await LoadFile(filePath, true);
        }

        public async Task<bool> OpenFolder(string folderPath = null)
        {
            folderPath ??= await AppViewModel.MainWindow.PickMarkdownFolderAsync();
            if (folderPath == null)
                return false;
            if (!await LoadFolder(folderPath))
                return false;
            SettingsViewModel.SidePaneOpen = true;
            SettingsViewModel.SidePaneIndex = 0;
            return true;
        }

        private async Task<bool> LoadFile(string path, bool skipSavedCheck = false, bool postMessage = true)
        {
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
                else if (!skipSavedCheck && !await AskToSave())
                {
                    return false;
                }
                var text = await File.ReadAllTextAsync(path);
                EditorViewModel.FirstStart = false;
                EditorViewModel.FileHash = Common.SimpleHash(text);
                FilePath = path;
                _ = AccessHistory.RecordFileHistory(FilePath);
                var backup = await CheckBackup(path, EditorViewModel.CurrentHash);
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
                    MarkdownEditor?.PostMessage("LoadFile", new { text = EditorViewModel.Markdown, basePath = ImageBasePath });
                }
                return true;
            }
            catch (Exception ex)
            {
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

        private async Task<bool> WriteAllText(string path, string text, bool alert = true)
        {
            try
            {
                IgnoreOwnFileWrite();
                await File.WriteAllTextAsync(path, text);
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
            if (FilePath == null)
            {
                var result = await SaveAs();
                return result != null;
            }
            else
            {
                var result = await WriteAllText(FilePath, EditorViewModel.Markdown, alert);
                if (result)
                {
                    EditorViewModel.FileHash = EditorViewModel.CurrentHash;
                    EditorViewModel.Saved = true;
                    AutoBackup.DeleteBackup(FilePath);
                    _ = AccessHistory.RecordFileHistory(FilePath);
                }
                return result;
            }
        }

        private async Task<string> SaveAs()
        {
            try
            {
                var filePicker = new FileSavePicker();
                filePicker.SetOwnerWindow(AppViewModel.MainWindow);
                filePicker.FileTypeChoices.Add("Markdown Files", FileTypeHelper.Markdown.ToList());
                filePicker.SuggestedFileName = FileName ?? "untitled";
                var file = await filePicker.PickSaveFileAsync();
                if (file != null)
                {
                    var result = await WriteAllText(file.Path, EditorViewModel.Markdown);
                    if (result)
                    {
                        AutoBackup.DeleteBackup(FilePath);
                        FilePath = file.Path;
                        EditorViewModel.FileHash = EditorViewModel.CurrentHash;
                        EditorViewModel.Saved = true;
                        _ = AccessHistory.RecordFileHistory(FilePath);
                        return file.Path;
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
            window = AppViewModel.GetInstances().Where(x => x.FileViewModel.FilePath?.ToLower() == filePath.ToLower()).FirstOrDefault()?.MainWindow ?? default;
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

            string text = null;
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        if (EditorViewModel.Saved)
                        {
                            EditorViewModel.FileHash = EditorViewModel.CurrentHash == 0 ? 1UL : 0UL;
                            EditorViewModel.Saved = false;
                        }
                        return;
                    }
                    text = await File.ReadAllTextAsync(FilePath);
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
            if (text == null)
                return;

            var diskHash = Common.SimpleHash(text);
            if (diskHash == EditorViewModel.FileHash)
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
                if (result == ContentDialogResult.Primary)
                    ApplyDiskText(text);
                else
                    EditorViewModel.FileHash = diskHash;
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
            EditorViewModel.Markdown = text;
            EditorViewModel.CurrentHash = EditorViewModel.FileHash;
            EditorViewModel.Saved = true;
            EditorViewModel.FileLoaded = false;
            EditorViewModel.AutoSavedSucc = true;
            EditorViewModel.History.InitHistory(text);
            AutoBackup.DeleteBackup(FilePath);
            MarkdownEditor?.PostMessage("LoadFile", new { text, basePath = ImageBasePath });
        }

        private void Exit()
        {
            var SC_CLOSE = 0xF060;
            PInvoke.PostMessage(AppViewModel.MainWindow, (uint)PInvoke.WindowMessage.WM_SYSCOMMAND, (nint)SC_CLOSE, IntPtr.Zero);
        }
    }
}
