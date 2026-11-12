using Typedown.Core.Models;
using Typedown.Core.ViewModels;

namespace Typedown.Core.Services
{
    /// <summary>
    /// The document an asynchronous operation is for - an open, a save, a backup, a picture inserted - taken as it begins,
    /// and asked again after each wait before anything it brings back is put anywhere. The tab, the document's identity in
    /// it, and whether it is the one shown: each operation held some of these its own way (a path, the tab object, the
    /// editor's load number, nothing at all after a dialog), and a tab given another document kept the same object and,
    /// for an untitled one, the same (no) path (the modularity audit's M01; A07 and A08 were of these). What a failed check
    /// means stays the operation's own: an open puts nothing into the tab shown, a save keeps its written file, a picture
    /// is not inserted.
    /// </summary>
    public sealed class DocumentOperation
    {
        private readonly TabsViewModel tabs;

        /// <summary>The tab the operation is for (null in a window with no tabs: every check then holds).</summary>
        public DocumentTab Tab { get; }

        /// <summary>The document's identity in <see cref="Tab"/> when the operation began (or was <see cref="Renew"/>ed).</summary>
        public string DocumentId { get; private set; }

        private DocumentOperation(TabsViewModel tabs, DocumentTab tab)
        {
            this.tabs = tabs;
            Tab = tab;
            DocumentId = tab?.DocumentId;
        }

        /// <summary>For <paramref name="tab"/>, or the tab shown now.</summary>
        public static DocumentOperation Of(TabsViewModel tabs, DocumentTab tab = null) => new(tabs, tab ?? tabs?.ActiveTab);

        /// <summary>The tab is still open and still holds the same document (not closed, not given another).</summary>
        public bool SameDocument => Tab == null || (Tab.DocumentId == DocumentId && (tabs == null || tabs.Tabs.Contains(Tab)));

        /// <summary>The same document, and it is the one shown: what the editor and the window's state are about.</summary>
        public bool Shown => SameDocument && (Tab == null || tabs == null || tabs.ActiveTab == Tab);

        /// <summary>The operation gave the tab a new document itself (an open into a reused tab): this is the one it is for.</summary>
        public void Renew() => DocumentId = Tab?.DocumentId;

        public override string ToString() => $"document {DocumentId?.Substring(0, System.Math.Min(8, DocumentId.Length))} in {(Tab == null ? "no tab" : Tab.Title)}";
    }
}
