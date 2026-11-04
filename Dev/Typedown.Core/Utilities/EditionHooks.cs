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
    /// read from and written to disk, what happens around the writing (<see cref="Activity"/>) and a panel beside the
    /// document (<see cref="ShowSidePanel"/>). Typedown itself adds nothing here.
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

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AppViewModel, SidePanel> sidePanels = new();

        /// <summary>
        /// A panel of the edition's beside the document in one window (one at a time: another shown in its place), with its
        /// title and a close button above it. The content is made for that window; the panel stays while the window does
        /// (back from the settings page, it is there again) until closed - by its button, or <see cref="CloseSidePanel"/>.
        /// </summary>
        public static void ShowSidePanel(AppViewModel app, SidePanel panel)
        {
            if (app == null || panel == null) return;
            if (sidePanels.TryGetValue(app, out var shown) && shown != panel)
            {
                sidePanels.Remove(app);
                try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionSidePanel", ex.ToString()); }
            }
            sidePanels.AddOrUpdate(app, panel);
            SidePanelChanged?.Invoke(app);
        }

        /// <summary>The window's panel taken away, if it is the one with this id; its <see cref="SidePanel.Closed"/> is called.</summary>
        public static void CloseSidePanel(AppViewModel app, string id)
        {
            if (app == null || !sidePanels.TryGetValue(app, out var shown) || shown.Id != id) return;
            sidePanels.Remove(app);
            SidePanelChanged?.Invoke(app);
            try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionSidePanel", ex.ToString()); }
        }

        /// <summary>The id of the panel shown in the window, or null.</summary>
        public static string SidePanelShown(AppViewModel app) => app != null && sidePanels.TryGetValue(app, out var shown) ? shown.Id : null;

        internal static SidePanel SidePanelOf(AppViewModel app) => app != null && sidePanels.TryGetValue(app, out var shown) ? shown : null;

        internal static event Action<AppViewModel> SidePanelChanged;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AppViewModel, EditorOverlay> overlays = new();

        /// <summary>
        /// An edition's page over the editor area of one window (one at a time), above the home page, the scroll bars and
        /// Find: the document stays underneath, as it was. It goes when <see cref="CloseOverlay"/> is called, or another
        /// tab or file is shown in the window (the reader went back to the writing); it stays while the window does (back
        /// from the settings page, it is there again).
        /// </summary>
        public static void ShowOverlay(AppViewModel app, EditorOverlay overlay)
        {
            if (app == null || overlay == null) return;
            if (overlays.TryGetValue(app, out var shown) && shown != overlay)
            {
                overlays.Remove(app);
                try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionOverlay", ex.ToString()); }
            }
            overlays.AddOrUpdate(app, overlay);
            OverlayChanged?.Invoke(app);
        }

        /// <summary>The window's overlay taken away, if it is the one with this id (null: whichever); its <see cref="EditorOverlay.Closed"/> is called.</summary>
        public static void CloseOverlay(AppViewModel app, string id = null)
        {
            if (app == null || !overlays.TryGetValue(app, out var shown) || (id != null && shown.Id != id)) return;
            overlays.Remove(app);
            OverlayChanged?.Invoke(app);
            try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionOverlay", ex.ToString()); }
        }

        /// <summary>The id of the overlay shown in the window, or null.</summary>
        public static string OverlayShown(AppViewModel app) => app != null && overlays.TryGetValue(app, out var shown) ? shown.Id : null;

        internal static EditorOverlay OverlayOf(AppViewModel app) => app != null && overlays.TryGetValue(app, out var shown) ? shown : null;

        internal static event Action<AppViewModel> OverlayChanged;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AppViewModel, EditorBar> bars = new();

        /// <summary>
        /// A bar of the edition's across the top of one window's document area (one at a time), below the tab bar and above
        /// the editor or the home page: the editor is moved down for it, nothing under it is covered. It stays while the
        /// window does until <see cref="CloseBar"/> is called.
        /// </summary>
        public static void ShowBar(AppViewModel app, EditorBar bar)
        {
            if (app == null || bar == null) return;
            if (bars.TryGetValue(app, out var shown) && shown != bar)
            {
                bars.Remove(app);
                try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionBar", ex.ToString()); }
            }
            bars.AddOrUpdate(app, bar);
            BarChanged?.Invoke(app);
        }

        /// <summary>The window's bar taken away, if it is the one with this id (null: whichever); its <see cref="EditorBar.Closed"/> is called.</summary>
        public static void CloseBar(AppViewModel app, string id = null)
        {
            if (app == null || !bars.TryGetValue(app, out var shown) || (id != null && shown.Id != id)) return;
            bars.Remove(app);
            BarChanged?.Invoke(app);
            try { shown.Closed?.Invoke(); } catch (Exception ex) { Log.WriteLocal("EditionBar", ex.ToString()); }
        }

        /// <summary>The id of the bar shown in the window, or null.</summary>
        public static string BarShown(AppViewModel app) => app != null && bars.TryGetValue(app, out var shown) ? shown.Id : null;

        internal static EditorBar BarOf(AppViewModel app) => app != null && bars.TryGetValue(app, out var shown) ? shown : null;

        internal static event Action<AppViewModel> BarChanged;

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

        internal static void ReportDocumentOnDisk(string path, string text, bool written, bool manual = false, AppViewModel window = null)
        {
            try
            {
                DocumentOnDisk?.Invoke(new DocumentOnDiskReport { Path = path, Text = text, Written = written, Manual = manual, Window = window });
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
                // Typedown's own record by path follows here too: every rename in the folder pane comes this way.
                Services.FirstEditMutes.Shared.Follow(oldPath, newPath, keepOld: false);
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
            // The documents muted for the first-edit notice follow the window's document to its new path (saved under
            // another name, the old file still there: both stay muted), whether an edition listens or not.
            if (kind == ActivityKind.DocumentRenamed && window?.FileViewModel?.FilePath is { } newPath)
            {
                try { Services.FirstEditMutes.Shared.Follow(oldPath, newPath, keepOld: oldPath != null && System.IO.File.Exists(oldPath), window.TabsViewModel?.ActiveTab?.DocumentId); }
                catch (Exception ex) { Log.WriteLocal("FirstEditMutesFollow", ex.ToString()); }
            }
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

        // ---- review mode ----

        /// <summary>
        /// Review mode's state for a document as it is loaded into a window's page (the window, the file - null for an
        /// untitled one - and the document's id): an object the page takes as its review payload (services/reviewSession.ts),
        /// or null when the document is not in review. Typedown sets none.
        /// </summary>
        public static Func<AppViewModel, string, string, object> ReviewState { get; set; }

        internal static object ReviewFor(AppViewModel window, string path, string documentId)
        {
            try
            {
                return ReviewState?.Invoke(window, path, documentId);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionReview", ex.ToString());
                return null;
            }
        }

        /// <summary>The page's revisions of the document shown (review mode), as it reports them while they change. Raised on the window's thread.</summary>
        public static event Action<ReviewReport> ReviewReported;

        internal static void ReportReview(ReviewReport report)
        {
            try
            {
                ReviewReported?.Invoke(report);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionReview", ex.ToString());
            }
        }

        /// <summary>Review mode's state for the document a window shows changed: its page takes it (null: no review).</summary>
        public static void PostReview(AppViewModel window, object review) => window?.EditorViewModel?.PostReview(review);

        /// <summary>One of review mode's commands for a window's page: next, previous, accept, reject, acceptAll, rejectAll (of one source, '' the reader).</summary>
        public static void PostReviewCommand(AppViewModel window, string command, string source = null) => window?.EditorViewModel?.PostReviewCommand(command, source);

        /// <summary>An automation client's edit was committed to a document (the one shown or a tab in the background). Raised on the window's thread.</summary>
        public static event Action<AutomationEditReport> AutomationEdited;

        internal static void ReportAutomationEdit(AutomationEditReport report)
        {
            try
            {
                AutomationEdited?.Invoke(report);
            }
            catch (Exception ex)
            {
                Log.WriteLocal("EditionAutomationEdit", ex.ToString());
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

        /// <summary>Written by the reader's own save (Save, Save As, Ctrl+S), not an automatic one.</summary>
        public bool Manual { get; set; }

        /// <summary>The window the document is in, when known.</summary>
        public AppViewModel Window { get; set; }
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

    /// <summary>The page's review mode revisions of a window's document (<see cref="EditionHooks.ReviewReported"/>).</summary>
    public sealed class ReviewReport
    {
        public AppViewModel Window { get; set; }

        /// <summary>The document's file, or null for an untitled one.</summary>
        public string Path { get; set; }

        public string DocumentId { get; set; }

        /// <summary>
        /// The page's ReviewState: "revisions" (each with id, kind, baseStart, baseEnd, oldText, newText, source, session,
        /// time, line, block), "inserted", "deleted", "sources" (name and count), "outcomes" since the last report (id,
        /// outcome, at, source, kind, oldText, newText), and "baseline" when it changed (a revision accepted).
        /// </summary>
        public Newtonsoft.Json.Linq.JObject State { get; set; }
    }

    /// <summary>An automation client's edit committed (<see cref="EditionHooks.AutomationEdited"/>).</summary>
    public sealed class AutomationEditReport
    {
        public AppViewModel Window { get; set; }
        public string Path { get; set; }
        public string DocumentId { get; set; }
        /// <summary>The client's program name and session, as it declared them.</summary>
        public string Name { get; set; }
        public string SessionId { get; set; }
        /// <summary>The document was the one shown (its page marked the edit itself), not a tab in the background.</summary>
        public bool Active { get; set; }
    }

    /// <summary>An edition's page over the editor area (<see cref="EditionHooks.ShowOverlay"/>).</summary>
    public sealed class EditorOverlay
    {
        public string Id { get; init; }
        public Microsoft.UI.Xaml.UIElement Content { get; init; }
        /// <summary>Called when it is closed or another takes its place.</summary>
        public Action Closed { get; init; }
    }

    /// <summary>An edition's bar across the top of the document area (<see cref="EditionHooks.ShowBar"/>).</summary>
    public sealed class EditorBar
    {
        public string Id { get; init; }
        public Microsoft.UI.Xaml.UIElement Content { get; init; }
        /// <summary>Called when it is closed or another takes its place.</summary>
        public Action Closed { get; init; }
    }

    /// <summary>An edition's panel beside the document (<see cref="EditionHooks.ShowSidePanel"/>).</summary>
    public sealed class SidePanel
    {
        public string Id { get; init; }
        public string Title { get; init; }
        public Microsoft.UI.Xaml.UIElement Content { get; init; }
        /// <summary>Its width in view pixels.</summary>
        public double Width { get; init; } = 340;
        /// <summary>Called when it is closed or another takes its place.</summary>
        public Action Closed { get; init; }
    }
}
