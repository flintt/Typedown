#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Typedown.Core.Utilities
{
    /// <summary>
    /// One entry of the find bar's results list ("Search in document" in the side pane): a match of the bar's search with
    /// the text around it, as the page sent it (services/findResults.ts) - plain text, the match at
    /// [<see cref="Before"/>.Length, +<see cref="Match"/>.Length).
    /// </summary>
    public sealed class FindResultEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The match's place among all the matches, from 0 (the bar's "3 of 85" is <see cref="Index"/> + 1).</summary>
        public int Index { get; init; }

        public string Before { get; init; }

        public string Match { get; init; }

        public string After { get; init; }

        /// <summary>The whole preview line.</summary>
        public string Text => Before + Match + After;

        /// <summary>The match the bar is on (the editor's selection).</summary>
        public bool IsCurrent => isCurrent;

        // A field with a get-only property, raised by hand: the host's property weaver leaves it alone.
        private bool isCurrent;

        internal void SetCurrent(bool value)
        {
            if (isCurrent == value) return;
            isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }

        /// <summary>The preview line [start, end) of the match, cut into its three parts (out-of-range offsets clamped).</summary>
        public static FindResultEntry FromPreview(int index, string text, int start, int end)
        {
            text ??= "";
            start = Math.Clamp(start, 0, text.Length);
            end = Math.Clamp(end, start, text.Length);
            return new FindResultEntry { Index = index, Before = text[..start], Match = text[start..end], After = text[end..] };
        }
    }

    /// <summary>
    /// The find bar's search as the host knows it: how many matches and which is chosen (the page's FindState, after every
    /// search, step or edit), and while the side pane's list is open, its entries (FindResults: a preview of each of the
    /// first <see cref="Cap"/> matches, the count exact). Applied on the window's thread.
    /// </summary>
    public sealed class FindResultsList : INotifyPropertyChanged
    {
        /// <summary>The page sends at most this many entries (services/findResults.ts FIND_RESULTS_CAP).</summary>
        public const int Cap = 500;

        public event PropertyChangedEventHandler PropertyChanged;

        private IReadOnlyList<FindResultEntry> entries = Array.Empty<FindResultEntry>();
        private int total;
        private int current = -1;
        private bool capped;
        private int version;

        /// <summary>The entries shown: replaced as a whole for each list the page sends (one change for the list view, not 500).</summary>
        public IReadOnlyList<FindResultEntry> Entries => entries;

        /// <summary>Every match of the search, shown or not.</summary>
        public int Total => total;

        /// <summary>The match chosen, from 0; -1: none.</summary>
        public int Current => current;

        /// <summary>More matches than entries: the list says to keep typing.</summary>
        public bool Capped => capped;

        /// <summary>The page's number for the list shown, sent back with an entry chosen (FindActivate).</summary>
        public int Version => version;

        /// <summary>The entry of the match chosen, if it is among those shown.</summary>
        public FindResultEntry CurrentEntry => current >= 0 && current < entries.Count ? entries[current] : null;

        /// <summary>The page's FindState: the count and the match chosen.</summary>
        public void SetState(int total, int current)
        {
            total = Math.Max(0, total);
            current = total == 0 ? -1 : Math.Clamp(current, -1, total - 1);
            var totalChanged = this.total != total;
            this.total = total;
            if (totalChanged) Raise(nameof(Total));
            SetCurrent(current);
        }

        /// <summary>The page's FindState message ({ total, index }).</summary>
        public void ApplyState(JToken args)
        {
            if (args is not JObject o) return;
            SetState(o.Value<int?>("total") ?? 0, o.Value<int?>("index") ?? -1);
        }

        /// <summary>The page's FindResults message ({ version, total, index, capped, entries: [{ text, start, end }] }).</summary>
        public void Apply(JToken args)
        {
            if (args is not JObject o) return;
            var list = new List<FindResultEntry>();
            if (o["entries"] is JArray array)
            {
                foreach (var item in array)
                {
                    if (list.Count >= Cap) break;
                    list.Add(FindResultEntry.FromPreview(list.Count, item.Value<string>("text"), item.Value<int?>("start") ?? 0, item.Value<int?>("end") ?? 0));
                }
            }
            version = o.Value<int?>("version") ?? version;
            var t = Math.Max(o.Value<int?>("total") ?? list.Count, list.Count);
            var wasCapped = capped;
            capped = o.Value<bool?>("capped") ?? t > list.Count;
            entries = list;
            current = -1;
            Raise(nameof(Entries));
            Raise(nameof(Version));
            if (wasCapped != capped) Raise(nameof(Capped));
            SetState(t, o.Value<int?>("index") ?? -1);
            // The entry of the match chosen is marked even if the index did not change (a new list's entries are new).
            CurrentEntry?.SetCurrent(true);
        }

        /// <summary>No search (another document, the bar closed): nothing listed.</summary>
        public void Clear()
        {
            entries = Array.Empty<FindResultEntry>();
            var wasCapped = capped;
            capped = false;
            Raise(nameof(Entries));
            if (wasCapped) Raise(nameof(Capped));
            SetState(0, -1);
        }

        private void SetCurrent(int value)
        {
            if (current == value) return;
            CurrentEntry?.SetCurrent(false);
            current = value;
            CurrentEntry?.SetCurrent(true);
            Raise(nameof(Current));
            Raise(nameof(CurrentEntry));
        }

        /// <summary>The bar's position text: "{0} of {1}" filled in (1-based), or <paramref name="none"/> when there is no
        /// match; empty when nothing is searched for.</summary>
        public static string PositionText(string format, string none, bool searching, int current, int total)
        {
            if (!searching) return "";
            if (total <= 0) return none ?? "";
            return string.Format(format ?? "{0}/{1}", current >= 0 ? (current + 1).ToString() : "-", total);
        }

        /// <summary>What a screen reader says for an entry: "12 of 85: …the line…".</summary>
        public static string SpokenName(string positionFormat, FindResultEntry entry, int total) =>
            string.Format(positionFormat ?? "{0}/{1}", entry.Index + 1, total) + ": " + entry.Text;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Where the side pane goes back to when the find bar's results list closes: the list is shown over the pane's page
    /// (Files, Outline, the folder search) and opens the pane if it was closed. Remembered when the list opens (once - a
    /// second "show all results" keeps the first), given back when it closes, forgotten when the reader takes the pane
    /// elsewhere meanwhile (closes it, opens the folder search).
    /// </summary>
    public sealed class SidePaneReturn
    {
        /// <summary>The list is open over the pane.</summary>
        public bool Active { get; private set; }

        public bool WasOpen { get; private set; }

        public int WasPage { get; private set; }

        /// <summary>The list opens: the pane as it is now remembered. The pane is to be open now.</summary>
        public void Open(bool paneOpen, int page)
        {
            if (Active) return;
            Active = true;
            WasOpen = paneOpen;
            WasPage = page;
        }

        /// <summary>The list closes: the pane as it was before (open, page), or null when there is nothing to go back to.</summary>
        public (bool Open, int Page)? Close()
        {
            if (!Active) return null;
            Active = false;
            return (WasOpen, WasPage);
        }

        /// <summary>The reader took the pane elsewhere: nothing to go back to.</summary>
        public void Forget() => Active = false;
    }
}
