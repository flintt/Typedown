#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// The side pane's search results as the list shows them: the names group on top (folders and files whose names
    /// match; the first <see cref="NamesPreview"/> until "show all" is chosen) and the contents group below (all as
    /// found). Each group can be collapsed; both start expanded. The <see cref="FolderSearch"/> hits are applied here
    /// in the order they came, on the thread the list is shown on.
    /// </summary>
    public sealed class FolderSearchResults : INotifyPropertyChanged
    {
        public const int NamesPreview = 5;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The names group's rows shown: the first five, or all once <see cref="ShowAllNames"/> was chosen.</summary>
        public ObservableCollection<FolderSearchHit> ShownNames { get; } = new();

        /// <summary>The contents group's rows: the files whose contents matched and whose names did not.</summary>
        public ObservableCollection<FolderSearchHit> Contents { get; } = new();

        private readonly List<FolderSearchHit> names = new();
        private readonly Dictionary<string, FolderSearchHit> namedFiles = new(StringComparer.Ordinal);
        private bool showAll;
        private bool namesExpanded = true;
        private bool contentsExpanded = true;

        /// <summary>Every name match found (the header's count), shown or not.</summary>
        public int NameCount => names.Count;

        public int ContentCount => Contents.Count;

        /// <summary>The names group has more than it shows: the "show all" row is there.</summary>
        public bool CanShowAllNames => !showAll && names.Count > NamesPreview;

        public bool NamesExpanded
        {
            get => namesExpanded;
            set { if (namesExpanded == value) return; namesExpanded = value; OnPropertyChanged(nameof(NamesExpanded)); }
        }

        public bool ContentsExpanded
        {
            get => contentsExpanded;
            set { if (contentsExpanded == value) return; contentsExpanded = value; OnPropertyChanged(nameof(ContentsExpanded)); }
        }

        /// <summary>
        /// A hit from the search, put where it belongs. Returns the row it changed: the new row, or for a file listed by
        /// its name whose content matches too, that row (now marked); null when nothing changed.
        /// </summary>
        public FolderSearchHit Apply(FolderSearchHit hit)
        {
            switch (hit.Kind)
            {
                case FolderSearchHitKind.Name:
                    names.Add(hit);
                    if (!hit.IsFolder) namedFiles[hit.FullPath] = hit;
                    if (showAll || ShownNames.Count < NamesPreview) ShownNames.Add(hit);
                    OnPropertyChanged(nameof(NameCount));
                    OnPropertyChanged(nameof(CanShowAllNames));
                    return hit;
                case FolderSearchHitKind.AlsoContent:
                    if (!namedFiles.TryGetValue(hit.FullPath, out var named) || named.AlsoContent) return null;
                    named.MarkAlsoContent(hit.Snippet);
                    return named;
                default:
                    Contents.Add(hit);
                    OnPropertyChanged(nameof(ContentCount));
                    return hit;
            }
        }

        /// <summary>The "show all" row chosen: the names group lists every name match.</summary>
        public void ShowAllNames()
        {
            if (showAll) return;
            showAll = true;
            for (var i = ShownNames.Count; i < names.Count; i++) ShownNames.Add(names[i]);
            OnPropertyChanged(nameof(CanShowAllNames));
        }

        /// <summary>A new query: no rows, the names group back to its first five. Collapsed groups stay collapsed.</summary>
        public void Clear()
        {
            names.Clear();
            namedFiles.Clear();
            ShownNames.Clear();
            Contents.Clear();
            showAll = false;
            OnPropertyChanged(nameof(NameCount));
            OnPropertyChanged(nameof(ContentCount));
            OnPropertyChanged(nameof(CanShowAllNames));
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
