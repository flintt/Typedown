using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Typedown.Core.Interfaces;
using Typedown.Core.Models;
using Typedown.Core.Utilities;

namespace Typedown.Core.ViewModels
{
    /// <summary>
    /// Document tabs for one window (upstream #30, #73, #85). There is a single editor; switching tabs snapshots
    /// the current document into its <see cref="DocumentTab"/> and restores the target one into the editor.
    /// </summary>
    public sealed partial class TabsViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public FileViewModel FileViewModel => ServiceProvider.GetService<FileViewModel>();

        public EditorViewModel EditorViewModel => ServiceProvider.GetService<EditorViewModel>();

        public IMarkdownEditor MarkdownEditor => ServiceProvider.GetService<IMarkdownEditor>();

        public ObservableCollection<DocumentTab> Tabs { get; } = new();

        public DocumentTab ActiveTab { get; private set; }

        public Command<DocumentTab> SwitchTabCommand { get; } = new();

        public Command<DocumentTab> CloseTabCommand { get; } = new();

        public Command<Unit> CloseActiveTabCommand { get; } = new();

        public Command<Unit> NextTabCommand { get; } = new();

        public Command<Unit> PreviousTabCommand { get; } = new();

        private readonly CompositeDisposable disposables = new();

        private bool switching;

        private DocumentTab tabBeforeNew;

        public event PropertyChangedEventHandler PropertyChanged;

        public TabsViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            ActiveTab = new DocumentTab();
            Tabs.Add(ActiveTab);
            SwitchTabCommand.OnExecute.Subscribe(async tab => await SwitchTo(tab));
            CloseTabCommand.OnExecute.Subscribe(async tab => await CloseTab(tab));
            CloseActiveTabCommand.OnExecute.Subscribe(async _ => await CloseTab(ActiveTab));
            NextTabCommand.OnExecute.Subscribe(async _ => await SwitchRelative(1));
            PreviousTabCommand.OnExecute.Subscribe(async _ => await SwitchRelative(-1));
            // Keep the active tab's label in sync with the editor state.
            disposables.Add(FileViewModel.WhenPropertyChanged(nameof(FileViewModel.FilePath)).Subscribe(_ => { if (!switching) ActiveTab.FilePath = FileViewModel.FilePath; }));
            disposables.Add(EditorViewModel.WhenPropertyChanged(nameof(EditorViewModel.Saved)).Subscribe(_ => { if (!switching) ActiveTab.IsDirty = !EditorViewModel.Saved; }));
        }

        public DocumentTab FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full;
            try { full = Path.GetFullPath(path); } catch { full = path; }
            return Tabs.FirstOrDefault(t => !string.IsNullOrEmpty(t.FilePath) && string.Equals(SafeFullPath(t.FilePath), full, StringComparison.OrdinalIgnoreCase));
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); } catch { return path; }
        }

        /// <summary>True when the active tab is a pristine untitled document that a new/open action may reuse.</summary>
        public bool IsActiveTabBlank => FileViewModel.FilePath == null && EditorViewModel.Saved && EditorViewModel.CurrentHash == Common.SimpleHash(Common.DefaultMarkdwn);

        /// <summary>Copies the live editor state into the active tab.</summary>
        public void SnapshotActive()
        {
            var tab = ActiveTab;
            var editor = EditorViewModel;
            tab.FilePath = FileViewModel.FilePath;
            tab.Markdown = editor.Markdown;
            tab.CurrentHash = editor.CurrentHash;
            tab.FileHash = editor.FileHash;
            tab.DiskHash = FileViewModel.DiskHash;
            tab.Saved = editor.Saved;
            tab.AutoSavedSucc = editor.AutoSavedSucc;
            tab.FileLoaded = editor.FileLoaded;
            tab.Cursor = editor.CurrentCursor;
            tab.History = editor.History;
            tab.IsDirty = !editor.Saved;
        }

        /// <summary>
        /// Snapshots the current document and makes a fresh tab active; the caller then loads content into the editor
        /// through the normal file flows (which set FilePath, hashes and post LoadFile).
        /// </summary>
        public DocumentTab BeginNewTab()
        {
            SnapshotActive();
            tabBeforeNew = ActiveTab;
            var tab = new DocumentTab();
            Tabs.Add(tab);
            ActiveTab = tab;
            return tab;
        }

        /// <summary>Undo <see cref="BeginNewTab"/> when loading failed before the editor was touched.</summary>
        public void AbortNewTab(DocumentTab tab)
        {
            if (tab == null || !Tabs.Contains(tab) || tab != ActiveTab) return;
            Tabs.Remove(tab);
            ActiveTab = tabBeforeNew != null && Tabs.Contains(tabBeforeNew) ? tabBeforeNew : Tabs.FirstOrDefault();
            tabBeforeNew = null;
        }

        public async Task SwitchTo(DocumentTab tab)
        {
            if (tab == null || tab == ActiveTab || !Tabs.Contains(tab)) return;
            switching = true;
            try
            {
                SnapshotActive();
                ActiveTab = tab;
                Restore(tab);
            }
            finally
            {
                switching = false;
            }
            await FileViewModel.CheckExternalChangeAfterSwitch();
        }

        private async Task SwitchRelative(int delta)
        {
            if (Tabs.Count < 2) return;
            var index = (Tabs.IndexOf(ActiveTab) + delta + Tabs.Count) % Tabs.Count;
            await SwitchTo(Tabs[index]);
        }

        private void Restore(DocumentTab tab)
        {
            var editor = EditorViewModel;
            FileViewModel.SetFilePathFromTab(tab.FilePath);
            editor.History = tab.History ?? new ContentHistory();
            editor.Markdown = tab.Markdown ?? Common.DefaultMarkdwn;
            editor.FileHash = tab.FileHash;
            FileViewModel.DiskHash = tab.DiskHash;
            editor.CurrentHash = tab.CurrentHash;
            editor.Saved = tab.Saved;
            editor.AutoSavedSucc = tab.AutoSavedSucc;
            editor.FileLoaded = true; // the FileLoaded handshake must not reset the restored history/hash
            editor.CurrentCursor = tab.Cursor;
            tab.IsDirty = !tab.Saved;
            MarkdownEditor?.PostMessage("LoadFile", new { text = editor.Markdown, basePath = FileViewModel.ImageBasePath, cursor = tab.Cursor });
        }

        public async Task<bool> CloseTab(DocumentTab tab)
        {
            if (tab == null || !Tabs.Contains(tab)) return false;
            if (Tabs.Count == 1)
            {
                // Closing the only document closes the window (the window's own close flow asks to save).
                FileViewModel.ExitCommand.Execute(default);
                return true;
            }
            if (tab != ActiveTab) await SwitchTo(tab);
            if (!await FileViewModel.AskToSave()) return false;
            // Saved or explicitly discarded: nothing in the editor needs to survive.
            EditorViewModel.Saved = true;
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            var next = Tabs[Math.Min(index, Tabs.Count - 1)];
            switching = true;
            try
            {
                ActiveTab = next;
                Restore(next);
            }
            finally
            {
                switching = false;
            }
            await FileViewModel.CheckExternalChangeAfterSwitch();
            return true;
        }

        /// <summary>Asks to save every dirty tab (window close). Returns false if the user cancels.</summary>
        public async Task<bool> AskToSaveAll()
        {
            foreach (var tab in Tabs.ToList())
            {
                var dirty = tab == ActiveTab ? !EditorViewModel.Saved : !tab.Saved;
                if (!dirty) continue;
                await SwitchTo(tab);
                await FileViewModel.AutoSaveFile();
                if (!EditorViewModel.Saved && !await FileViewModel.AskToSave())
                    return false;
            }
            return true;
        }

        public bool IsOpenInAnyTab(string path) => FindByPath(path) != null;

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
