using System.ComponentModel;
using System.IO;
using Typedown.Core.Utilities;

namespace Typedown.Core.Models
{
    /// <summary>
    /// One open document in the tab strip. The active tab's state lives in EditorViewModel/FileViewModel;
    /// background tabs keep a snapshot here (text, hashes, cursor, undo history) that is restored on switch.
    /// </summary>
    public partial class DocumentTab : INotifyPropertyChanged
    {
        /// <summary>
        /// The document's identity: from the tab's creation to its close, or until the tab is reused for another
        /// document. Not the path: an untitled document has none, and Save As changes it. An untitled document's
        /// backup is kept under it.
        /// </summary>
        public string DocumentId { get; private set; } = NewDocumentId();

        /// <summary>The tab now holds a different document (a preview or blank tab reused for an open or new file).</summary>
        public void BecomeNewDocument() => DocumentId = NewDocumentId();

        /// <summary>A document recovered from its backup keeps the id it had, and so its backup file.</summary>
        public void RestoreIdentity(string documentId) => DocumentId = documentId;

        private static string NewDocumentId() => System.Guid.NewGuid().ToString("N");

        public string FilePath { get; set; }

        /// <summary>The encoding, byte order mark and line ending the file was opened with.</summary>
        public Utilities.TextFileFormat FileFormat { get; set; } = Utilities.TextFileFormat.Default;

        public string Markdown { get; set; }

        public ulong CurrentHash { get; set; }

        public ulong FileHash { get; set; }

        public ulong DiskHash { get; set; }

        public bool Saved { get; set; } = true;

        public bool AutoSavedSucc { get; set; } = true;

        public bool FileLoaded { get; set; }

        public CursorState Cursor { get; set; }

        public ContentHistory History { get; set; } = new();

        public bool IsDirty { get; set; }

        /// <summary>Content hash last written to this tab's crash backup, so an unchanged background tab is not re-backed each tick.</summary>
        public ulong BackupHash { get; set; }

        /// <summary>Opened by a single click in the file tree: the next single-click open replaces this tab (VS Code style).</summary>
        public bool IsPreview { get; set; }

        public string Title => string.IsNullOrEmpty(FilePath) ? Locale.GetString("Untitled") : Path.GetFileName(FilePath);

        public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

        public string ToolTip => string.IsNullOrEmpty(FilePath) ? Title : FilePath;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
