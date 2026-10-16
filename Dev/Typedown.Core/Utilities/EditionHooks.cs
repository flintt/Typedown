using System;
using System.Collections.Generic;
using System.Linq;
using Typedown.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// Places where an edition (Edition.Initialize) adds to every window: styles for the editor page, entries in the File
    /// menu, its Export submenu and the View menu, items in the status bar, sections of the home page, the word counts the page reports, the documents
    /// read from and written to disk, and what happens around the writing (<see cref="Activity"/>). Typedown itself adds
    /// nothing here.
    /// </summary>
    public static class EditionHooks
    {
        private static string editorCss = "";

        /// <summary>
        /// CSS the editor page applies after the theme's and before the user's own (Settings > Editor), so a user's rule
        /// still has the last word. HTML and PDF exports carry it too, as they carry the other two.
        /// </summary>
        public static string EditorCss => editorCss;

        /// <summary><see cref="EditorCss"/> changed: every open editor page applies it.</summary>
        public static event Action EditorCssChanged;

        public static void SetEditorCss(string css)
        {
            css ??= "";
            if (css == editorCss) return;
            editorCss = css;
            EditorCssChanged?.Invoke();
        }

        private static readonly List<Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>>> viewMenu = new();
        private static readonly List<Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>>> fileMenu = new();
        private static readonly List<Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>>> exportMenu = new();

        /// <summary>
        /// Entries for the View menu, below the theme submenu. <paramref name="build"/> makes them for one window when its
        /// menu bar is built, so each window has entries of its own.
        /// </summary>
        public static void AddViewMenuItems(Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>> build)
        {
            lock (viewMenu) viewMenu.Add(build);
        }

        /// <summary>Entries for the File menu, below Save As; made for each window as the View menu's are.</summary>
        public static void AddFileMenuItems(Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>> build)
        {
            lock (fileMenu) fileMenu.Add(build);
        }

        /// <summary>Entries for File > Export, after the export configurations; made for each window as the View menu's are.</summary>
        public static void AddExportMenuItems(Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>> build)
        {
            lock (exportMenu) exportMenu.Add(build);
        }

        private static readonly List<Func<AppViewModel, IEnumerable<StatusBarItem>>> statusBar = new();

        /// <summary>
        /// Items for the status bar, left of the count; made for each window's bar when it first loads (a bar switched off
        /// and on again is a new one, and asks again). They take part in the bar's layout: on a narrow window they are the
        /// first to go into its "..." menu, and one with a <see cref="StatusBarItem.Name"/> can be switched off from the
        /// bar's right-click menu like the bar's own items.
        /// </summary>
        public static void AddStatusBarItems(Func<AppViewModel, IEnumerable<StatusBarItem>> build)
        {
            lock (statusBar) statusBar.Add(build);
        }

        internal static IReadOnlyList<StatusBarItem> StatusBarItems(AppViewModel viewModel)
        {
            List<Func<AppViewModel, IEnumerable<StatusBarItem>>> builders;
            lock (statusBar) builders = statusBar.ToList();
            var items = new List<StatusBarItem>();
            foreach (var build in builders)
            {
                try
                {
                    items.AddRange((build(viewModel) ?? Enumerable.Empty<StatusBarItem>()).Where(x => x?.Element != null && !string.IsNullOrEmpty(x.Id)));
                }
                catch (Exception ex)
                {
                    Log.WriteLocal("EditionStatusBar", ex.ToString());
                }
            }
            return items;
        }

        private static readonly List<Func<AppViewModel, IEnumerable<HomeSection>>> homeSections = new();

        /// <summary>
        /// Sections for the home page, under its New and Open buttons and above its own lists (favourites, recent files
        /// and folders), so they are in view without scrolling; made for each window's home page when it first loads (a page made again - back from the settings
        /// page - asks again), in the order the builders were added.
        /// </summary>
        public static void AddHomeSections(Func<AppViewModel, IEnumerable<HomeSection>> build)
        {
            lock (homeSections) homeSections.Add(build);
        }

        internal static IReadOnlyList<HomeSection> HomeSections(AppViewModel viewModel)
        {
            List<Func<AppViewModel, IEnumerable<HomeSection>>> builders;
            lock (homeSections) builders = homeSections.ToList();
            var sections = new List<HomeSection>();
            foreach (var build in builders)
            {
                try
                {
                    sections.AddRange((build(viewModel) ?? Enumerable.Empty<HomeSection>()).Where(x => x?.Element != null && !string.IsNullOrEmpty(x.Id)));
                }
                catch (Exception ex)
                {
                    Log.WriteLocal("EditionHomeSection", ex.ToString());
                }
            }
            return sections;
        }

        internal static IReadOnlyList<MenuFlyoutItemBase> ExportMenuItems(AppViewModel viewModel) => MenuItems(exportMenu, viewModel);

        internal static IReadOnlyList<MenuFlyoutItemBase> ViewMenuItems(AppViewModel viewModel) => MenuItems(viewMenu, viewModel);

        internal static IReadOnlyList<MenuFlyoutItemBase> FileMenuItems(AppViewModel viewModel) => MenuItems(fileMenu, viewModel);

        private static IReadOnlyList<MenuFlyoutItemBase> MenuItems(List<Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>>> menu, AppViewModel viewModel)
        {
            List<Func<AppViewModel, IEnumerable<MenuFlyoutItemBase>>> builders;
            lock (menu) builders = menu.ToList();
            var items = new List<MenuFlyoutItemBase>();
            foreach (var build in builders)
            {
                try
                {
                    items.AddRange(build(viewModel) ?? Enumerable.Empty<MenuFlyoutItemBase>());
                }
                catch (Exception ex)
                {
                    Log.WriteLocal("EditionMenu", ex.ToString());
                }
            }
            return items;
        }

        /// <summary>
        /// The word count of a document as the editor page reported it: on loading it (a new <see cref="WordCountReport.LoadId"/>)
        /// and after its changes. Raised on the window's thread.
        /// </summary>
        public static event Action<WordCountReport> WordCountReported;

        /// <summary>
        /// A document's text as it is on disk: read when the file was opened, or written by a save. Raised on the window's
        /// thread, after the file was read or written.
        /// </summary>
        public static event Action<DocumentOnDiskReport> DocumentOnDisk;

        internal static void ReportDocumentOnDisk(string path, string text, bool written)
        {
            try
            {
                DocumentOnDisk?.Invoke(new DocumentOnDiskReport { Path = path, Text = text, Written = written });
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionDocumentOnDisk", ex.ToString());
            }
        }

        /// <summary>
        /// What happens around the writing, as it happens: a window activated or left, the mode changed, another document
        /// shown, a document saved under a new name or its file renamed (in the folder pane, or outside the app while
        /// open), a paste, an undo or a redo, the page scrolled (at most once every ten seconds a window). Only the event,
        /// for an edition to make something of (a timeline of a writing day); raised on the window's thread.
        /// </summary>
        public static event Action<ActivityReport> Activity;

        /// <summary>
        /// A file or folder that is not the window's document renamed or moved in the folder pane (the window's document
        /// renamed is <see cref="ActivityKind.DocumentRenamed"/>): its old path and its new one. Raised on the window's
        /// thread, after the move.
        /// </summary>
        public static event Action<string, string> PathRenamed;

        public static void ReportPathRenamed(string oldPath, string newPath)
        {
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;
            try
            {
                PathRenamed?.Invoke(oldPath, newPath);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionPathRenamed", ex.ToString());
            }
        }

        /// <summary>Raises <see cref="Activity"/> with the window's document and mode (public: the window itself is in the app project).</summary>
        public static void ReportActivity(ActivityKind kind, AppViewModel window, string oldPath = null)
        {
            if (Activity == null || window == null) return;
            try
            {
                var settings = window.SettingsViewModel;
                Activity.Invoke(new ActivityReport
                {
                    Kind = kind,
                    At = DateTime.Now,
                    Editor = window.EditorViewModel,
                    DocumentId = window.TabsViewModel?.ActiveTab?.DocumentId,
                    Path = window.FileViewModel?.FilePath,
                    OldPath = oldPath,
                    Mode = settings == null ? null : settings.ReadOnly ? "reading" : settings.SourceCode ? "source" : "visual",
                    FocusMode = settings?.FocusMode ?? false,
                    Typewriter = settings?.Typewriter ?? false,
                });
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionActivity", ex.ToString());
            }
        }

        internal static void ReportWordCount(WordCountReport report)
        {
            try
            {
                WordCountReported?.Invoke(report);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionWordCount", ex.ToString());
            }
        }
    }

    public enum ActivityKind
    {
        WindowActivated, WindowDeactivated, ModeChanged, DocumentShown, DocumentRenamed, Pasted, Undone, Redone,

        /// <summary>The page moved: raised at most once every ten seconds a window, a sign that someone is there.</summary>
        Scrolled,
    }

    /// <summary>One event of <see cref="EditionHooks.Activity"/>, with the window's document and mode as they are after it.</summary>
    public sealed class ActivityReport
    {
        public ActivityKind Kind { get; set; }

        public DateTime At { get; set; }

        /// <summary>The window's editor (as in <see cref="WordCountReport.Editor"/>).</summary>
        public object Editor { get; set; }

        /// <summary>The document shown in the window: its id stays with it through a rename.</summary>
        public string DocumentId { get; set; }

        /// <summary>Its file, or null for an untitled one.</summary>
        public string Path { get; set; }

        /// <summary>
        /// For <see cref="ActivityKind.DocumentRenamed"/>: the file it had before (null when it was untitled). Saved under
        /// a new name, that file is still there; renamed, it is not.
        /// </summary>
        public string OldPath { get; set; }

        /// <summary>visual, source or reading.</summary>
        public string Mode { get; set; }

        public bool FocusMode { get; set; }

        public bool Typewriter { get; set; }
    }

    /// <summary>A document's text as read from its file or written to it (<see cref="EditionHooks.DocumentOnDisk"/>).</summary>
    public sealed class DocumentOnDiskReport
    {
        public string Path { get; set; }

        public string Text { get; set; }

        /// <summary>Written (a save, an automatic one too) rather than read (the file opened).</summary>
        public bool Written { get; set; }
    }

    /// <summary>
    /// An item an edition adds to the status bar (<see cref="EditionHooks.AddStatusBarItems"/>). The bar shows it in a
    /// slot of its own: the element's own Visibility stays the edition's, and an element collapsed there is left out
    /// altogether (no room taken, no entry in either menu). The element may be loaded and unloaded more than once; what
    /// it subscribes to is best tied to its Loaded and Unloaded.
    /// </summary>
    public sealed class StatusBarItem
    {
        /// <summary>A name of its own (not one of the bar's: modes, heading, format, save, count): kept in the hidden items setting, and in automation ids ("StatusItem-" and "Overflow-" with it).</summary>
        public string Id { get; set; }

        /// <summary>Its entry in the bar's right-click menu, to switch it off and on; null: no entry, always shown.</summary>
        public string Name { get; set; }

        public Microsoft.UI.Xaml.FrameworkElement Element { get; set; }

        /// <summary>Its entry in the "..." menu when the window is too narrow for it: the text at that moment (null: <see cref="Name"/>).</summary>
        public Func<string> MenuText { get; set; }

        /// <summary>What that entry does (a click on the item, say); null: no entry in the "..." menu.</summary>
        public Action Invoke { get; set; }
    }

    /// <summary>
    /// A section an edition adds to the home page (<see cref="EditionHooks.AddHomeSections"/>): a heading of the page's
    /// own style over the element, under the page's buttons. The element's own Visibility stays the edition's: collapsed,
    /// the whole section goes, heading and all. The element may be loaded and unloaded more than once (the home page
    /// hides and shows); what it subscribes to is best tied to its Loaded and Unloaded.
    /// </summary>
    public sealed class HomeSection
    {
        /// <summary>A name of its own: in the section's automation id ("HomeSection-" with it).</summary>
        public string Id { get; set; }

        /// <summary>The heading above it; null or empty: none.</summary>
        public string Title { get; set; }

        public Microsoft.UI.Xaml.FrameworkElement Element { get; set; }
    }

    /// <summary>A word count the editor page reported (<see cref="EditionHooks.WordCountReported"/>).</summary>
    public sealed class WordCountReport
    {
        /// <summary>The window's editor the report came from: counts of different editors are of different documents.</summary>
        public object Editor { get; set; }

        /// <summary>
        /// Which load of a document the count is of. Within one load a change in the count is an edit; a new load (another
        /// document, a tab switched to, the file reloaded) starts again from its first count.
        /// </summary>
        public int LoadId { get; set; }

        public int Words { get; set; }

        public int Characters { get; set; }

        /// <summary>The document's file, or null for an untitled one.</summary>
        public string Path { get; set; }

        /// <summary>
        /// The count is of text an automation client wrote (document.replace and the like, an edition's own edits through
        /// the same path), nobody having typed since: a change in it is not the reader's writing.
        /// </summary>
        public bool Automation { get; set; }
    }
}
