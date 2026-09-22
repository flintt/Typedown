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
        public string FilePath { get; set; }

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

        public string Title => string.IsNullOrEmpty(FilePath) ? Locale.GetString("Untitled") : Path.GetFileName(FilePath);

        public string DisplayTitle => IsDirty ? $"{Title} •" : Title;

        public string ToolTip => string.IsNullOrEmpty(FilePath) ? Title : FilePath;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
