using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// The first-edit notice muted for one document (the card's "Don't show again for this document", 2026-11-07): by
    /// its path, following it when it is renamed or saved under another name, an untitled one by its id.
    /// </summary>
    [TestClass]
    public class FirstEditMutesTests
    {
        private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;

        private static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        [TestMethod]
        public async Task AMuteIsForThatDocumentAndKeptInItsFile()
        {
            using var directory = new TestDirectory();
            var file = directory.File("first-edit-mutes.json");
            var store = new FirstEditMutesStore(file);
            store.Mute(P("notes", "a.md"), "doc-a");
            Assert.IsTrue(store.IsMuted(P("notes", "a.md"), "doc-a"));
            Assert.IsFalse(store.IsMuted(P("notes", "b.md"), "doc-b"), "another document still gets the notice");
            await store.FlushAsync();

            var again = new FirstEditMutesStore(file);
            Assert.IsTrue(again.IsMuted(P("notes", "a.md"), "another id"), "kept by path, across runs");
            Assert.IsFalse(again.IsMuted(P("notes", "b.md"), null));
        }

        [TestMethod]
        public void ARenamedDocumentOrFolderTakesItsMuteAlong()
        {
            using var directory = new TestDirectory();
            var store = new FirstEditMutesStore(directory.File("first-edit-mutes.json"));
            store.Mute(P("notes", "a.md"), null);
            store.Mute(P("notes", "sub", "c.md"), null);

            store.Follow(P("notes", "a.md"), P("notes", "renamed.md"), keepOld: false);
            Assert.IsTrue(store.IsMuted(P("notes", "renamed.md"), null));
            Assert.IsFalse(store.IsMuted(P("notes", "a.md"), null), "a new a.md is another document");

            store.Follow(P("notes", "sub"), P("notes", "moved"), keepOld: false);
            Assert.IsTrue(store.IsMuted(P("notes", "moved", "c.md"), null));
            Assert.IsFalse(store.IsMuted(P("notes", "sub", "c.md"), null));
        }

        [TestMethod]
        public void SavedUnderAnotherNameBothStayMuted()
        {
            using var directory = new TestDirectory();
            var store = new FirstEditMutesStore(directory.File("first-edit-mutes.json"));
            store.Mute(P("notes", "a.md"), null);
            store.Follow(P("notes", "a.md"), P("notes", "copy.md"), keepOld: true);
            Assert.IsTrue(store.IsMuted(P("notes", "a.md"), null));
            Assert.IsTrue(store.IsMuted(P("notes", "copy.md"), null));
        }

        [TestMethod]
        public async Task AnUntitledDocumentIsMutedByItsIdUntilItIsSaved()
        {
            using var directory = new TestDirectory();
            var file = directory.File("first-edit-mutes.json");
            var store = new FirstEditMutesStore(file);
            store.Mute(null, "untitled-1");
            Assert.IsTrue(store.IsMuted(null, "untitled-1"));
            Assert.IsFalse(store.IsMuted(null, "untitled-2"));
            await store.FlushAsync();
            Assert.IsFalse(File.Exists(file), "nothing written for an untitled document");

            store.Follow(null, P("notes", "saved.md"), keepOld: false, documentId: "untitled-1");
            Assert.IsTrue(store.IsMuted(P("notes", "saved.md"), "untitled-1"));
            await store.FlushAsync();
            Assert.IsTrue(new FirstEditMutesStore(file).IsMuted(P("notes", "saved.md"), null), "saved: kept by its path");

            store.Follow(null, P("notes", "other.md"), keepOld: false, documentId: "untitled-2");
            Assert.IsFalse(store.IsMuted(P("notes", "other.md"), null), "an untitled document never muted is not muted once saved");
        }
    }
}
