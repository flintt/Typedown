using System.ComponentModel;
using Typedown.Core.Utilities;

namespace Typedown.Core.Models
{
    /// <summary>
    /// A file or folder in a list of them (favourites, recent files and folders): its name, the folder it is in, and
    /// whether it is known to be gone (shown dimmed, never removed by itself).
    /// </summary>
    public partial class PathItem : INotifyPropertyChanged
    {
        public PathItem(string path, bool folder)
        {
            Path = path;
            Folder = folder;
            Name = RecentPaths.Name(path);
            Location = RecentPaths.ParentPath(path) ?? string.Empty;
        }

        public string Path { get; }

        public bool Folder { get; }

        public string Name { get; }

        /// <summary>The folder it is in, whole.</summary>
        public string Location { get; }

        /// <summary>The file's last change, as the list shows it (filled in off the UI thread; empty until then).</summary>
        public string Modified { get; set; } = string.Empty;

        public bool Missing { get; set; }

        public bool IsFavorite { get; set; }

        public double Opacity => Missing ? 0.45 : 1;

        /// <summary>A folder or a document (Segoe Fluent Icons).</summary>
        public string Glyph => Folder ? "\uF12B" : "\uE8A5";

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
