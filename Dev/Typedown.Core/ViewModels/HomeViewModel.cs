using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel;
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
    /// The home page of one window: shown over the editor while its tab is a blank document that asked for it. The tab
    /// stays a blank document - the home page is not a tab of its own - and the editor keeps the keyboard under it, so
    /// the first thing typed (or pasted, dropped, written by an automation client) goes into the document, and the
    /// document then being no longer blank is what hides the home page.
    /// <para>
    /// Shown by itself (Settings > General > On startup > Show the home page on a blank tab, on by default) when a
    /// window starts with a blank tab (nothing to open, the last session's files or the last file gone), and when the
    /// last tab is closed; on a new tab only when "New tabs show the home page" is on. File > Home, the ⌂ button and its
    /// shortcut show it whenever asked.
    /// </para>
    /// </summary>
    public sealed partial class HomeViewModel : INotifyPropertyChanged, IDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        private TabsViewModel Tabs => ServiceProvider.GetService<TabsViewModel>();
        private FileViewModel File => ServiceProvider.GetService<FileViewModel>();
        private EditorViewModel Editor => ServiceProvider.GetService<EditorViewModel>();
        private SettingsViewModel Settings => ServiceProvider.GetService<SettingsViewModel>();
        private IMarkdownEditor MarkdownEditor => ServiceProvider.GetService<IMarkdownEditor>();

        /// <summary>The home page is over the editor now.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>File > Home, the ⌂ button, the shortcut: the home page in a blank tab (one there already, or a new one).</summary>
        public Command<Unit> ShowHomeCommand { get; } = new();

        /// <summary>The home page's New: the blank document it is on, to be written in.</summary>
        public Command<Unit> NewDocumentCommand { get; } = new();

        private readonly CompositeDisposable disposables = new();

        private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

        private bool updateQueued;

        public event PropertyChangedEventHandler PropertyChanged;

        public HomeViewModel(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ShowHomeCommand.OnExecute.Subscribe(async _ => await ShowHomeAsync());
            NewDocumentCommand.OnExecute.Subscribe(_ => { Hide(); MarkdownEditor?.FocusEditor(); });
            // Looked at once whatever changed has settled: a tab switch sets the path, the text and the hashes one after
            // the other, and halfway through the tab being shown is neither the old document nor its own.
            disposables.Add(Tabs.WhenPropertyChanged(nameof(TabsViewModel.ActiveTab)).Subscribe(_ => QueueUpdate()));
            disposables.Add(File.WhenPropertyChanged(nameof(FileViewModel.FilePath)).Subscribe(_ => QueueUpdate()));
            disposables.Add(Editor.WhenPropertyChanged(nameof(EditorViewModel.CurrentHash)).Subscribe(_ => QueueUpdate()));
            disposables.Add(Editor.WhenPropertyChanged(nameof(EditorViewModel.Saved)).Subscribe(_ => QueueUpdate()));
        }

        /// <summary>
        /// A tab is a blank document: no file, nothing typed. Whitespace counts as nothing, as long as it is saved:
        /// the editor gives an empty document back as it likes (a line break, none), and that is still blank.
        /// </summary>
        public static bool IsBlank(DocumentTab tab, bool active, FileViewModel file, EditorViewModel editor)
        {
            if (tab == null) return false;
            return active
                ? file.FilePath == null && editor.Saved && string.IsNullOrWhiteSpace(editor.Markdown)
                : string.IsNullOrEmpty(tab.FilePath) && tab.Saved && string.IsNullOrWhiteSpace(tab.Markdown ?? Common.DefaultMarkdwn);
        }

        private bool ActiveIsBlank => IsBlank(Tabs?.ActiveTab, true, File, Editor);

        /// <summary>
        /// Start-up, the last tab closed, a session or a last file that is no longer there: the home page, when the setting
        /// asks for it and the tab is blank.
        /// </summary>
        public void ShowIfWanted()
        {
            if (Settings.ShowHomePage && ActiveIsBlank) Show();
        }

        /// <summary>The home page on the active tab (it must be blank to show: anything else hides it again).</summary>
        public void Show()
        {
            if (Tabs?.ActiveTab is not { } tab) return;
            tab.ShowHome = true;
            Update();
        }

        /// <summary>The home page off the active tab: its blank document is shown.</summary>
        public void Hide()
        {
            if (Tabs?.ActiveTab is { } tab) tab.ShowHome = false;
            Update();
        }

        private async Task ShowHomeAsync()
        {
            try
            {
                var tabs = Tabs;
                if (tabs == null) return;
                if (!ActiveIsBlank)
                {
                    // A blank tab there already is used; otherwise a new one. The open documents are left as they are.
                    var blank = tabs.Tabs.FirstOrDefault(t => t != tabs.ActiveTab && IsBlank(t, false, File, Editor));
                    if (blank != null) await tabs.SwitchTo(blank);
                    else await File.NewDocumentAsync();
                }
                // The keyboard goes to the home page (HomePage takes it when shown), not to the editor: the blank
                // document may still be on its way to the page, and keys typed into the page before it arrives went
                // into the document shown before (or were lost when the load replaced them). The page hands them on.
                Show();
            }
            catch (Exception ex)
            {
                Log.Debug($"home: {ex.Message}");
            }
        }

        private void QueueUpdate()
        {
            if (updateQueued) return;
            updateQueued = true;
            if (dispatcherQueue == null || !dispatcherQueue.TryEnqueue(() => { updateQueued = false; Update(); }))
            {
                updateQueued = false;
                Update();
            }
        }

        private void Update()
        {
            var tab = Tabs?.ActiveTab;
            var blank = ActiveIsBlank;
            // Written in, a file opened in it: the home page does not come back on this document (an undo to empty
            // leaves the reader in the editor).
            if (tab != null && tab.ShowHome && !blank) tab.ShowHome = false;
            IsVisible = tab != null && tab.ShowHome && blank;
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
