using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Typedown.Core.Utilities;

namespace Typedown.Core.Services
{
    /// <summary>A button (or a link under the text) on a notice, and in its line in the notices list.</summary>
    public sealed class NoticeAction
    {
        public string Text { get; init; }

        public string AutomationId { get; init; }

        public Action Run { get; init; }

        /// <summary>A link under the text (the first edit's "Don't show again for this document"), not a button beside it.</summary>
        public bool Link { get; init; }

        /// <summary>Not offered again in the notices list (it only makes sense on the card).</summary>
        public bool CardOnly { get; init; }
    }

    /// <summary>
    /// A card at the window's bottom right (UIViewModel.ShowNotice): the app's own (an export written, the first edit's
    /// rewrite, a copy the clipboard never took) and an edition's (EditionHooks.ShowCard), in one frame and one stack -
    /// each had its own copy of the frame, its own timer and its own handlers (the user, 2026-11-23). One shown with the
    /// key of one there takes its place. What is worth having later goes into the notices list too (NoticeHistory).
    /// </summary>
    public sealed class Notice
    {
        public string Key { get; init; }

        /// <summary>The card's; its text is this + "Text", its close button this + "Close" (as the E2E cases find them).</summary>
        public string AutomationId { get; init; }

        public string Text { get; init; }

        /// <summary>What an edition puts in the card instead of a text and buttons (it brings its own buttons).</summary>
        public UIElement Content { get; init; }

        public IReadOnlyList<NoticeAction> Actions { get; init; } = Array.Empty<NoticeAction>();

        /// <summary>The card goes by itself after this; null: it stays till closed.</summary>
        public TimeSpan? Timeout { get; init; }

        public bool Closable { get; init; } = true;

        /// <summary>Kept in the notices list (the status bar's bell) for as long as the app runs.</summary>
        public bool InHistory { get; init; }

        /// <summary>Its line in the list when it is not <see cref="Text"/> (an edition's card has none).</summary>
        public string HistoryText { get; init; }

        /// <summary>Its buttons in the list when not its card's (less those that only make sense on the card).</summary>
        public IReadOnlyList<NoticeAction> HistoryActions { get; init; }

        /// <summary>The card gone: closed, timed out, taken away or replaced.</summary>
        public Action Closed { get; init; }
    }

    /// <summary>A notice as the list keeps it.</summary>
    public sealed class NoticeEntry
    {
        public DateTime Time { get; init; }

        public string Key { get; init; }

        public string Text { get; init; }

        /// <summary>The document it was about (its file name), or null.</summary>
        public string Where { get; init; }

        public IReadOnlyList<NoticeAction> Actions { get; init; } = Array.Empty<NoticeAction>();

        /// <summary>Added without a card (a notice the reader turned off: only here now).</summary>
        public bool Quiet { get; init; }
    }

    /// <summary>
    /// The notices of this run, newest first, the app's (every window's): the status bar's bell shows how many are unread
    /// and lists them. Not kept after the app ends (an export's place or "a newer release" would be stale; the user's
    /// call, 2026-11-23). At most <see cref="Max"/>. The same notice again (key, text and document) is not added twice -
    /// a document whose first-edit notice was turned off would add one at every load. <see cref="Changed"/> is raised on
    /// the thread that made the change.
    /// </summary>
    public static class NoticeHistory
    {
        public const int Max = 50;

        private static readonly object gate = new();
        private static readonly List<NoticeEntry> entries = new();
        private static int unread;

        public static event Action Changed;

        public static IReadOnlyList<NoticeEntry> Entries
        {
            get { lock (gate) return entries.ToList(); }
        }

        public static int Unread
        {
            get { lock (gate) return unread; }
        }

        public static void Add(NoticeEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Text)) return;
            lock (gate)
            {
                if (entries.Any(e => e.Key == entry.Key && e.Text == entry.Text && e.Where == entry.Where)) return;
                entries.Insert(0, entry);
                if (entries.Count > Max) entries.RemoveRange(Max, entries.Count - Max);
                unread = Math.Min(unread + 1, entries.Count);
            }
            Log.Debug($"notices: {entry.Key}{(entry.Quiet ? " (quiet)" : "")}: {entry.Text}");
            RaiseChanged();
        }

        /// <summary>The list opened: nothing unread.</summary>
        public static void MarkRead()
        {
            lock (gate)
            {
                if (unread == 0) return;
                unread = 0;
            }
            RaiseChanged();
        }

        public static void Clear()
        {
            lock (gate)
            {
                entries.Clear();
                unread = 0;
            }
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Log.Debug($"notices: Changed: {ex.Message}"); }
        }
    }
}
