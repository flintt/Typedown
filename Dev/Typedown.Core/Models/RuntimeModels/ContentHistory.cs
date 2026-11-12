using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Typedown.Core.Models
{
    public class HistoryModel
    {
        private string text;

        // The text in pieces once kept in the history, a picture's base64 among them shared with every other step that
        // has it (EmbeddedPictures): put together when it is read (an undo, a redo).
        private string[] parts;

        public string Text
        {
            get => text ?? (parts == null ? null : string.Concat(parts));
            set { text = value; parts = null; }
        }

        public CursorState Cursor { get; set; } = null;

        /// <summary>The characters this step holds of its own (a shared picture's are counted once, by the pool).</summary>
        internal long OwnChars { get; private set; }

        internal IReadOnlyList<string> Shared { get; private set; } = Array.Empty<string>();

        internal void Keep(EmbeddedPictures pool)
        {
            var (pieces, shared) = pool.Split(text);
            if (shared.Count == 0)
            {
                OwnChars = text?.Length ?? 0;
                return;
            }
            parts = pieces;
            text = null;
            Shared = shared;
            OwnChars = 0;
            foreach (var piece in pieces) OwnChars += piece.Length;
            foreach (var payload in shared) OwnChars -= payload.Length;
        }
    }

    /// <summary>
    /// The base64 of the pictures kept in a document (#52), held once for all the undo history: every step was a copy of
    /// the whole text, and a document with a screenshot of 300 KB in it held a hundred copies of it.
    /// </summary>
    internal sealed class EmbeddedPictures
    {
        // Shorter is left in the text: not worth the pieces.
        internal const int MinShared = 1024;

        private static readonly Regex payload = new(@"data:image/[\w.+-]+(?:;[\w.+-]+=[^;,)\s]*)*;base64,(?<data>[A-Za-z0-9+/=]{" + MinShared + ",})", RegexOptions.Compiled);

        private readonly Dictionary<string, (string Value, int Uses)> pool = new(StringComparer.Ordinal);

        /// <summary>The characters held, each picture once.</summary>
        internal long Chars { get; private set; }

        internal int Count => pool.Count;

        /// <summary>The text in pieces, each picture's base64 the pool's one copy; and the pictures it used (taken once each).</summary>
        internal (string[] Pieces, List<string> Shared) Split(string text)
        {
            var shared = new List<string>();
            if (text == null || !text.Contains("data:image/", StringComparison.Ordinal)) return (null, shared);
            var pieces = new List<string>();
            var at = 0;
            foreach (Match m in payload.Matches(text))
            {
                var data = m.Groups["data"];
                pieces.Add(text.Substring(at, data.Index - at));
                pieces.Add(Take(data.Value));
                shared.Add(pieces[^1]);
                at = data.Index + data.Length;
            }
            if (shared.Count == 0) return (null, shared);
            pieces.Add(text.Substring(at));
            return (pieces.ToArray(), shared);
        }

        private string Take(string data)
        {
            if (pool.TryGetValue(data, out var held))
            {
                pool[data] = (held.Value, held.Uses + 1);
                return held.Value;
            }
            pool[data] = (data, 1);
            Chars += data.Length;
            return data;
        }

        internal void Release(IEnumerable<string> shared)
        {
            foreach (var data in shared)
            {
                if (!pool.TryGetValue(data, out var held)) continue;
                if (held.Uses > 1) pool[data] = (held.Value, held.Uses - 1);
                else
                {
                    pool.Remove(data);
                    Chars -= data.Length;
                }
            }
        }

        internal void Clear()
        {
            pool.Clear();
            Chars = 0;
        }
    }

    // The commit timer is the other part (ContentHistory.Timer.cs, the window's DispatcherTimer); the tests give their own.
    public partial class ContentHistory : INotifyPropertyChanged
    {
        const int deep = 100;
        // Snapshots are whole-document strings; also cap the total size so a multi-MB document cannot pin
        // hundreds of MB of undo history (UTF-16: 32M chars ~= 64 MB).
        const long maxTotalChars = 32L * 1024 * 1024;
        long totalChars = 0;
        readonly List<HistoryModel> histories = new();
        readonly EmbeddedPictures pictures = new();

        /// <summary>The characters the history holds, each embedded picture once (the tests read it).</summary>
        internal long HeldChars => totalChars + pictures.Chars;
        HistoryModel pending = new();
        int index = -1;
        // The caret last reported. Source mode reports it before the change it goes with: a change then had no caret, was
        // never a step (a step needs both), and an undo went past it - the letter just typed and the step before (KY01).
        CursorState lastCursor;
        // A selection reported since the last change: the change that follows replaced it (Enter, a letter, Backspace or
        // Delete, a paste or a cut over it), and the text before it is a step of its own, with that selection. Typing
        // waits in pending until a pause or a line change; a selection within one line and Enter over it changed pending
        // instead, and one undo took the typing away too, or brought back part of it (the user, 2026-11-05, #39).
        CursorState selectionSinceChange;
        // The visual editor reports its caret only with a change (the caret after it), never a selection on its own: the
        // host hears of one from the page's SelectionChange (SelectionMade). The step is then put back with the caret
        // the typing left; source mode reports the selection itself, and its step is put back selected.
        bool selectedSinceChange;

        /// <summary>The page has text selected now (the visual editor's SelectionChange): the next change replaces it.</summary>
        public void SelectionMade() => selectedSinceChange = true;

        partial void InitTimer();
        partial void StartTimer(TimeSpan interval);
        partial void StopTimer();

        public bool Undoable { get; set; }
        public bool Redoable { get; set; }
        public bool IsPending { get => pending.Text != null && pending.Cursor != null; }

        public ContentHistory()
        {
            InitTimer();
        }

        public HistoryModel Undo()
        {
            try
            {
                if (index > 0 || (index == 0 && IsPending))
                {
                    CommitPending();
                    index--;
                    Redoable = true;
                    Undoable = index > 0;
                    return histories[index];
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
            return null;
        }

        public HistoryModel Redo()
        {
            try
            {
                if (index < histories.Count - 1)
                {
                    StopTimer();
                    pending = new();
                    index++;
                    Redoable = index < histories.Count - 1;
                    Undoable = true;
                    return histories[index];
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
            return null;
        }

        public void ClearHistory()
        {
            try
            {
                lastCursor = null;
                selectionSinceChange = null;
                selectedSinceChange = false;
                histories.Clear();
                pictures.Clear();
                totalChars = 0;
                StopTimer();
                pending = new();
                ResetPendingKind();
                index = -1;
                Redoable = false;
                Undoable = false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        public void CommitPending()
        {
            try
            {
                if (!IsPending) return;
                StopTimer();
                for (var i = index + 1; i < histories.Count; i++)
                    Forget(histories[i]);
                histories.RemoveRange(index + 1, histories.Count - (index + 1));
                pending.Keep(pictures);
                histories.Add(pending);
                totalChars += pending.OwnChars;
                index++;
                while (histories.Count > deep || (histories.Count > 1 && HeldChars > maxTotalChars))
                {
                    Forget(histories[0]);
                    histories.RemoveAt(0);
                    index--;
                }
                pending = new();
                ResetPendingKind();
                Redoable = false;
                Undoable = index > 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        private void Forget(HistoryModel step)
        {
            totalChars -= step.OwnChars;
            pictures.Release(step.Shared);
        }

        private void ResetPendingKind()
        {
            pendingKind = EditKind.Unknown;
            pendingInserted = 0;
            pendingSentenceEnded = false;
        }

        private void ResetTimer()
        {
            StopTimer();
            StartTimer(TimeSpan.FromSeconds(3));
        }

        public void CursorChange(CursorState cursor)
        {
            try
            {
                if (cursor != null) lastCursor = cursor;
                if (cursor != null && cursor.Anchor != cursor.Focus) selectionSinceChange = cursor;
                if (pending.Text == null && index > -1)
                {
                    histories[index].Cursor = cursor;
                    return;
                }
                if (cursor == null)
                {
                    return;
                }
                if (IsPending && pending.Cursor.Focus.Line != cursor.Focus.Line)
                {
                    pending.Cursor = cursor;
                    CommitPending();
                    return;
                }
                pending.Cursor = cursor;
                if (pending.Text != null && histories.Count == 0)
                {
                    CommitPending();
                    return;
                }
                StateChange();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        // Compare ignoring trailing newlines without allocating trimmed copies: this runs on every keystroke
        // with the whole document as input.
        private static int LengthWithoutTrailingNewlines(string s)
        {
            var n = s.Length;
            while (n > 0 && (s[n - 1] == '\n' || s[n - 1] == '\r')) n--;
            return n;
        }

        private static bool EqualsIgnoringTrailingNewlines(string a, string b)
        {
            if (a == null || b == null) return a == b;
            var la = LengthWithoutTrailingNewlines(a);
            var lb = LengthWithoutTrailingNewlines(b);
            return la == lb && string.CompareOrdinal(a, 0, b, 0, la) == 0;
        }

        /// <summary>
        /// What made a change, as the page tells (services/changeOrigin.ts) or the host knows: letters typed, Backspace or
        /// Delete a character at a time, any other edit of the reader's, or a change not the reader's (an automation write,
        /// a reload from disk). Unknown: a report that does not say (as before: steps by a pause or a line change).
        /// </summary>
        public enum EditKind { Unknown, Insert, Delete, Other, External }

        /// <summary>Letters typed that make one step at most: about a sentence; it ends sooner at a sentence's end.</summary>
        public const int TypingStepChars = 80;

        // The pending step's kind, the letters typed into it, and whether its last letters ended a sentence.
        EditKind pendingKind;
        int pendingInserted;
        bool pendingSentenceEnded;

        private static bool Blank(string inserted) => inserted != null && inserted.Length > 0 && inserted.Trim().Length == 0;

        // The letters typed end a sentence ("Done. ", "完成。"): the next letters begin a step of their own.
        private static bool EndsASentence(string inserted)
        {
            var typed = inserted?.TrimEnd(' ');
            return !string.IsNullOrEmpty(typed) && "。！？.!?；;…".IndexOf(typed[^1]) >= 0;
        }

        // Step boundaries by the kind of edit (the user, 2026-11-05, #39): the host gets whole-document snapshots, and with a
        // pause or a caret on another line as the only boundaries, everything within a paragraph (one line in Markdown) and
        // three seconds merged - a long paragraph typed fast was one step; a format, a paste, a word deleted, a caret moved
        // within the line went into the typing before them; an automation write or a reload landed in the reader's typing.
        // Now typing is a step while it is letters typed one after another (no other edit, no caret moved, no selection
        // replaced), up to a sentence's end or TypingStepChars; deleting a character at a time is a step of its own; any
        // other edit is a step of its own, closed at once; a change not the reader's is never merged with anything.
        public void ContentChange(string content, EditKind kind = EditKind.Unknown, bool moved = false, string inserted = null)
        {
            try
            {
                if (content == null) return;
                // A report that does not say what made it is no change when only the newlines at the end differ (the page's
                // export settles them); an edit of the reader's is compared as it is: a line added or taken away at the end
                // (Enter, Backspace) was never a step, and an undo then a redo lost it (the audit's 25, 2026-11-09).
                bool Same(string text) => kind == EditKind.Unknown ? EqualsIgnoringTrailingNewlines(text, content) : string.Equals(text, content, StringComparison.Ordinal);
                if ((pending.Text != null && Same(pending.Text)) ||
                    (pending.Text == null && index > -1 && Same(histories[index].Text)))
                {
                    return;
                }
                var replacesSelection = selectionSinceChange != null || selectedSinceChange;
                var boundary = replacesSelection
                    || kind is EditKind.Other or EditKind.External
                    || moved
                    // Typing then deleting, or the other way round (a report that does not say is not a change of kind).
                    || (kind != EditKind.Unknown && pendingKind != EditKind.Unknown && pendingKind != kind)
                    || (kind == EditKind.Insert && ((pendingSentenceEnded && !Blank(inserted)) || pendingInserted >= TypingStepChars));
                // The text before this change a step of its own; the one before a change over a selection put back with that
                // selection (source mode reports it; the visual editor's comes as the caret the typing left).
                if (boundary && pending.Text != null)
                {
                    if (selectionSinceChange != null) pending.Cursor = selectionSinceChange;
                    pending.Cursor ??= lastCursor;
                    CommitPending();
                }
                selectionSinceChange = null;
                selectedSinceChange = false;
                pending.Text = content;
                pending.Cursor ??= lastCursor;
                if (pendingKind == EditKind.Unknown) pendingKind = kind;
                if (kind == EditKind.Insert)
                {
                    pendingInserted += string.IsNullOrEmpty(inserted) ? 1 : inserted.Length;
                    // The space after a sentence stays with it; the next letter begins the next step.
                    if (!Blank(inserted)) pendingSentenceEnded = EndsASentence(inserted);
                }
                if (pending.Cursor != null && histories.Count == 0)
                {
                    CommitPending();
                    return;
                }
                // Any edit but typing or deleting is a step of its own at once; its caret, reported next, goes with it.
                if (kind is EditKind.Other or EditKind.External && pending.Cursor != null)
                {
                    CommitPending();
                    StateChange();
                    return;
                }
                StateChange();
                ResetTimer();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        private void StateChange()
        {
            Redoable = index < histories.Count - 1;
            Undoable = index > 0 || (index == 0 && pending.Text != null && pending.Cursor != null);
        }

        public void InitHistory(string content)
        {
            ClearHistory();
            CursorChange(new(Focus: new(Line: 0, Ch: 0), Anchor: new(Line: 0, Ch: 0)));
            ContentChange(content);
        }
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }

}
