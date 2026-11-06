using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Typedown.Automation;
using Typedown.Core.Models;
using Typedown.Core.Utilities;
using Typedown.Core.ViewModels;

namespace Typedown.Core.Services
{
    /// <summary>
    /// A Windows tab as the automation edit coordinator sees it (Typedown.Automation.IEditableDocument). Every member
    /// runs on the tab's window dispatcher: the coordinator is started there and stays there. The active tab is edited
    /// through its editor page; a background tab only has the snapshot the tab holds.
    /// </summary>
    public sealed class AutomationDocument : IEditableDocument, IEditSourceAware
    {
        public const int FlushTimeoutMs = 2000;
        public const int ApplyTimeoutMs = 10000;
        public const int ReloadTimeoutMs = 15000;

        private readonly AppViewModel app;
        private readonly DocumentTab tab;

        public AutomationDocument(AppViewModel app, DocumentTab tab)
        {
            this.app = app;
            this.tab = tab;
        }

        private EditorViewModel Editor => app.EditorViewModel;

        public string DocumentId => tab.DocumentId;
        public long Revision => tab.Revision;
        public bool IsActive => app.TabsViewModel?.ActiveTab == tab;
        public string Text => IsActive ? Editor.Markdown : tab.Markdown ?? "";
        public bool Saved => IsActive ? Editor.Saved : tab.Saved;

        public async Task<bool> FlushAsync(CancellationToken cancellationToken) =>
            await Editor.SyncWithPageAsync(ReloadTimeoutMs, FlushTimeoutMs);

        private sealed class State
        {
            public string Text;
            public object Cursor;
            public double ScrollTop;
            public int LoadId;
        }

        public object CaptureState() => new State { Text = Editor.Markdown, Cursor = Editor.CurrentCursor, ScrollTop = Editor.LastScrollY, LoadId = Editor.LoadId };

        // The load the page was on when it applied the edit: another load since means it no longer shows the edit.
        private int appliedLoadId;

        // The client the edit in flight is from (review mode): told to the page with it, and to the edition once committed.
        private EditSource editSource;

        public void NoteEditSource(EditSource source) => editSource = source;

        public async Task<ApplyReply> ApplyInEditorAsync(ApplyCommand command, CancellationToken cancellationToken)
        {
            Editor.HoldEditorReports();
            appliedLoadId = Editor.LoadId;
            var reply = await Editor.ApplyDocumentEditAsync(command.OperationId, command.TargetRevision, command.BaseContentHash, command.Text, command.ScrollToChange, ApplyTimeoutMs, command.Source);
            if (reply == null) throw new TimeoutException("The editor did not answer ApplyDocumentEdit.");
            switch (reply["outcome"]?.ToString())
            {
                case "conflict":
                    // Nothing was applied: whatever the reader typed is theirs, as usual.
                    Editor.ReleaseEditorReports(apply: true);
                    return new ApplyReply(ApplyOutcome.Conflict);
                case "applied":
                    var n = reply["normalization"];
                    if (n == null) return new ApplyReply(ApplyOutcome.Failed);
                    var normalization = new NormalizationInfo(
                        n["pendingNormalization"]?.ToString() ?? PendingNormalization.Unknown,
                        n["sourceHash"]?.ToString() ?? "",
                        n["normalizedHash"]?.Type == JTokenType.String ? n["normalizedHash"].ToString() : null,
                        n["reasons"] is JArray reasons ? reasons.Select(r => r.ToString()).ToList() : new System.Collections.Generic.List<string>(),
                        n["classifierVersion"]?.Value<int?>() ?? 0);
                    return new ApplyReply(ApplyOutcome.Applied, reply["sourceHash"]?.ToString(), normalization);
                default:
                    return new ApplyReply(ApplyOutcome.Failed);
            }
        }

        public bool EditorStillHoldsEdit() => !IsActive || Editor.LoadId == appliedLoadId;

        public async Task<string> RestoreAsync(object captured, CancellationToken cancellationToken)
        {
            var state = (State)captured;
            // The host never took the candidate in, so its own text is still the one before the edit. If another tab
            // has been brought up meanwhile, the page shows that one and this tab's snapshot is already right.
            if (!IsActive) { Editor.ReleaseEditorReports(apply: false); return DocumentText.ContentHash(tab.Markdown ?? ""); }
            Editor.ReleaseEditorReports(apply: false);
            var confirmed = await Editor.ReloadAsync(state.Text, state.Cursor, state.ScrollTop, ReloadTimeoutMs);
            return confirmed == null ? null : DocumentText.ContentHash(confirmed);
        }

        public void Commit(string text, long revision, string operationId)
        {
            if (IsActive)
            {
                Editor.CommitAutomationText(text);
            }
            else
            {
                if (!string.Equals(tab.Markdown, text, StringComparison.Ordinal)) tab.NoteTextChanged();
                tab.Markdown = text;
                tab.CurrentHash = Common.SimpleHash(text);
                tab.Saved = tab.FileHash == tab.CurrentHash;
                tab.IsDirty = !tab.Saved;
                tab.History?.ContentChange(text, Typedown.Core.Models.ContentHistory.EditKind.External);
                tab.History?.CommitPending();
            }
            if (tab.Revision != revision)
                Log.Debug($"automation: {operationId} committed revision {tab.Revision}, expected {revision}");
            // Review mode: a write to a tab in the background has no page to mark it; the edition notes who it is from.
            if (editSource != null)
                EditionHooks.ReportAutomationEdit(new AutomationEditReport { Window = app, Path = tab.FilePath, DocumentId = tab.DocumentId, Name = editSource.Name, SessionId = editSource.SessionId, Active = IsActive });
            editSource = null;
            tab.IsPreview = false; // an edited tab is no longer a throwaway preview
        }

        public void EndEdit()
        {
            // Typing that arrived after the page took the edit becomes the next revision, on top of it - unless the tab
            // was left meanwhile: the editor then shows another document, and this one's typing must not land there.
            Editor.ReleaseEditorReports(apply: IsActive);
        }

        /// <summary>Only the active document is saved in this version; a background tab is focused first.</summary>
        public Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            if (!IsActive) return Task.FromResult(false);
            if (app.FileViewModel.SaveNeedsEncodingConsent) throw EncodingNotExact(tab.Revision, applied: true);
            return SaveOrSayWhy(app, tab.Revision, applied: true);
        }

        // The save, refused with its reason when another program changed the file meanwhile (ChangedOnDisk).
        public static async Task<bool> SaveOrSayWhy(AppViewModel app, long revision, bool applied)
        {
            if (await app.FileViewModel.SaveToExistingPathAsync()) return true;
            if (app.FileViewModel.LastSaveRefusal == FileViewModel.ChangedOnDiskReason) throw ChangedOnDisk(revision, applied);
            return false;
        }

        /// <summary>
        /// The API's save of a file another program changed since it was read, its version not seen yet: refused with
        /// save_failed and reason "changed_on_disk", nothing written - the person is asked in the window which to keep.
        /// </summary>
        public static AutomationException ChangedOnDisk(long revision, bool applied)
        {
            var data = new Dictionary<string, object> { ["reason"] = FileViewModel.ChangedOnDiskReason, ["revision"] = revision };
            if (applied) data["applied"] = true;
            return new AutomationException(AutomationErrorKind.save_failed,
                (applied ? "The edit was applied but not saved: " : "Not saved: ")
                + "another program changed this file since it was read, and saving would write over its version. The person is asked in the window which to keep.",
                data);
        }

        /// <summary>
        /// The API's save of a file that was not read exactly (R07): refused with save_failed and reason
        /// "encoding_not_exact", not a dialog - writing it replaces the bytes shown as U+FFFD, and only the person may
        /// choose that (File > Save asks them; the status bar's encoding menu reads it again in another encoding).
        /// </summary>
        public static AutomationException EncodingNotExact(long revision, bool applied)
        {
            var data = new Dictionary<string, object> { ["reason"] = "encoding_not_exact", ["revision"] = revision };
            if (applied) data["applied"] = true;
            return new AutomationException(AutomationErrorKind.save_failed,
                (applied ? "The edit was applied but not saved: " : "Not saved: ")
                + "this file's encoding could not be read exactly, and saving would replace the characters shown as U+FFFD for good. Only the person can choose to save it (File > Save asks them).",
                data);
        }
    }
}
