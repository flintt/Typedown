using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>
    /// A file changed on disk while it was open (a reader: "only the changes before can be thrown away?"): a save that finds
    /// another program's version it has not seen does not write over it unasked, and the version the reader does not keep
    /// is kept as a copy of its own (the E2E cases XF01 and XF02 the dialogs).
    /// </summary>
    [TestClass]
    public class DiskConflictTests
    {
        [TestMethod]
        public void ASaveFindingTextNotSeenIsAConflict()
        {
            // known: last read or written 10; the editor loaded 11 (its normalising); writing 12.
            Assert.IsTrue(DiskConflict.ChangedSinceKnown(onDisk: 99, known: 10, loaded: 11, toWrite: 12), "another program's text");
            Assert.IsFalse(DiskConflict.ChangedSinceKnown(onDisk: 10, known: 10, loaded: 11, toWrite: 12), "what was last read or written");
            Assert.IsFalse(DiskConflict.ChangedSinceKnown(onDisk: 11, known: 10, loaded: 11, toWrite: 12), "what the editor loaded");
            Assert.IsFalse(DiskConflict.ChangedSinceKnown(onDisk: 12, known: 10, loaded: 11, toWrite: 12), "what is about to be written: nothing lost");
            Assert.IsFalse(DiskConflict.ChangedSinceKnown(onDisk: 99, known: 0, loaded: 11, toWrite: 12), "nothing known of the disk (a new document)");
        }

        [TestMethod]
        public void AKeptCopyIsNamedForItsDocumentItsTimeAndWhoseVersion()
        {
            var time = new DateTime(2026, 11, 4, 21, 30, 15);
            Assert.AreEqual("notes 2026-11-04 213015 on disk.md", DiskConflict.KeptCopyName("notes.md", time, "on disk"));
            Assert.AreEqual("config 2026-11-04 213015 unsaved.json", DiskConflict.KeptCopyName("config.json", time, "unsaved"));
            Assert.AreEqual("untitled 2026-11-04 213015 unsaved.md", DiskConflict.KeptCopyName(null, time, "unsaved"));
        }

        [TestMethod]
        public async Task AKeptCopyStaysThroughTheDocumentsBackupGoing()
        {
            using var directory = new TestDirectory();
            var backup = new AutoBackup(directory.Path);
            var document = Path.Combine(directory.Path, "notes.md");

            var first = await backup.KeepCopy(document, "the disk's text", "on disk");
            var second = await backup.KeepCopy(document, "the disk's text again", "on disk");
            Assert.IsNotNull(first);
            Assert.AreNotEqual(first, second, "two in the same second do not overwrite each other");
            Assert.AreEqual(backup.KeptFolder, Path.GetDirectoryName(first));
            Assert.AreEqual("the disk's text", await File.ReadAllTextAsync(first));

            // A save of the document deletes its backup, never what was kept.
            await backup.Backup(document, "unsaved");
            backup.DeleteBackup(document);
            Assert.IsTrue(File.Exists(first) && File.Exists(second));
        }

        [TestMethod]
        public async Task KeptCopiesPastTheLimitGoOldestFirst()
        {
            using var directory = new TestDirectory();
            var backup = new AutoBackup(directory.Path);
            Directory.CreateDirectory(backup.KeptFolder);
            var oldest = Path.Combine(backup.KeptFolder, "old 2020-01-01 000000 on disk.md");
            File.WriteAllText(oldest, "old");
            File.SetLastWriteTimeUtc(oldest, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            for (var i = 0; i < 199; i++)
                File.WriteAllText(Path.Combine(backup.KeptFolder, $"filler {i}.md"), "x");

            await backup.KeepCopy(Path.Combine(directory.Path, "notes.md"), "new", "on disk");
            Assert.AreEqual(200, Directory.GetFiles(backup.KeptFolder).Length);
            Assert.IsFalse(File.Exists(oldest), "the oldest went");
        }
    }
}
