using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    [TestClass]
    public class SafeFileAndBackupTests
    {
        [TestMethod]
        public async Task AtomicWriteReplacesContentWithoutLeavingATemporaryFile()
        {
            using var directory = new TestDirectory();
            var path = directory.File("document.md");
            await File.WriteAllTextAsync(path, "old\n", new UTF8Encoding(false));

            await SafeFile.WriteAllTextAtomicAsync(path, "new\n", new UTF8Encoding(false));

            Assert.AreEqual("new\n", await File.ReadAllTextAsync(path));
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(directory.Path));
        }

        // A write that never finished (the app ended) or a failed replace's kept copy left a temp file beside the document
        // for good: one a week old goes with the next write, a newer one (a recovery copy to be found) stays (R05).
        [TestMethod]
        public async Task OldTempFilesGoNewOnesStay()
        {
            using var directory = new TestDirectory();
            var path = directory.File("document.md");
            await File.WriteAllTextAsync(path, "old\n");
            var stale = directory.File(".document.md.0123456789abcdef.tmp");
            var recent = directory.File(".document.md.fedcba9876543210.tmp");
            var other = directory.File(".another.md.0123456789abcdef.tmp");
            foreach (var f in new[] { stale, recent, other }) await File.WriteAllTextAsync(f, "left\n");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-8));
            File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-8));

            await SafeFile.WriteAllTextAtomicAsync(path, "new\n");

            Assert.IsFalse(File.Exists(stale), "this file's week-old temp file removed");
            Assert.IsTrue(File.Exists(recent), "a newer one kept");
            Assert.IsTrue(File.Exists(other), "another file's left alone");
            Assert.AreEqual("new\n", await File.ReadAllTextAsync(path));
        }

        // The typography designer, version history and writing statistics wait for the write on the window's thread
        // (GetResult). A write completing later must not need that thread to go on: the window hung for good when it did
        // (the user's x280, 2026-11-05). Here a thread whose context runs nothing it is handed while it waits, as a
        // window's thread blocked in GetResult.
        [TestMethod]
        public void AWriteWaitedForOnABusyThreadEnds()
        {
            using var directory = new TestDirectory();
            var path = directory.File("preset.json");
            Exception failure = null;
            var worker = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new HeldContext());
                try { SafeFile.WriteAllTextAtomicAsync(path, new string('x', 4 << 20)).GetAwaiter().GetResult(); }
                catch (Exception ex) { failure = ex; }
            }) { IsBackground = true };
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(20)), "the write waited for on a thread that is held never ended");
            Assert.IsNull(failure, failure?.ToString());
            Assert.AreEqual(4 << 20, new FileInfo(path).Length);
        }

        // A context that keeps what it is handed and never runs it (its thread is busy waiting).
        private sealed class HeldContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state) { }
            public override void Send(SendOrPostCallback d, object state) { }
        }

        [TestMethod]
        public async Task FailedTemporaryWriteLeavesTheOriginalUntouched()
        {
            using var directory = new TestDirectory();
            var path = directory.File(new string('x', 245) + ".md");
            await File.WriteAllTextAsync(path, "original\n");

            await AssertAtomicWriteFailure(() => SafeFile.WriteAllTextAtomicAsync(path, "replacement\n"));

            Assert.AreEqual("original\n", await File.ReadAllTextAsync(path));
        }

        [TestMethod]
        public async Task LockedWindowsTargetFailsWithoutTruncatingTheOriginal()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Inconclusive("Windows sharing locks are required for this assertion.");

            using var directory = new TestDirectory();
            var path = directory.File("locked.md");
            await File.WriteAllTextAsync(path, "original\n");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await AssertAtomicWriteFailure(() => SafeFile.WriteAllTextAtomicAsync(path, "replacement\n"));

            Assert.AreEqual("original\n", await File.ReadAllTextAsync(path));
            var recovery = Directory.GetFiles(directory.Path, ".locked.md.*.tmp");
            Assert.IsTrue(recovery.Length > 0, "the completed recovery copy was not retained");
            Assert.AreEqual("replacement\n", await File.ReadAllTextAsync(recovery[0]));
        }

        [TestMethod]
        public async Task BackupCanBeReadOverwrittenAndDeleted()
        {
            using var directory = new TestDirectory();
            var backup = new AutoBackup(directory.Path);
            const string document = @"C:\docs\guide.md";

            Assert.IsTrue(await backup.Backup(document, "first"));
            Assert.AreEqual("first", await backup.GetBackup(document));
            Assert.IsTrue(await backup.Backup(document, "second"));
            Assert.AreEqual("second", await backup.GetBackup(document));

            backup.DeleteBackup(document);
            Assert.IsNull(await backup.GetBackup(document));
        }

        [TestMethod]
        public async Task AGoneFilesBackupComesBackAsAnUntitledDocument()
        {
            using var directory = new TestDirectory();
            var backup = new AutoBackup(directory.Path);
            const string document = @"C:\docs\gone.md";
            Assert.IsTrue(await backup.Backup(document, "unsaved words"));

            Assert.IsTrue(backup.AdoptAsUntitled(document));
            Assert.IsNull(await backup.GetBackup(document), "the file's own backup is moved, not copied");
            var untitled = backup.UntitledBackups();
            Assert.AreEqual(1, untitled.Count);
            Assert.AreEqual("unsaved words", await File.ReadAllTextAsync(untitled[0].file));
            Assert.IsFalse(backup.AdoptAsUntitled(document), "nothing left to take over");
        }

        [TestMethod]
        public void BackupFileNameRemainsCompatibleWithStableBuild()
        {
            using var directory = new TestDirectory();
            var backup = new AutoBackup(directory.Path);

            Assert.AreEqual(
                "2nfnd9_guide.md",
                Path.GetFileName(backup.GetBackupFilePath("guide.md")));
            Assert.AreEqual(
                "untitled_0123456789abcdef0123456789abcdef.md",
                Path.GetFileName(backup.GetBackupFilePath(null, "0123456789abcdef0123456789abcdef")));
        }

        private static async Task AssertAtomicWriteFailure(Func<Task> action)
        {
            Exception failure = null;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            Assert.IsTrue(failure is IOException || failure is UnauthorizedAccessException,
                $"expected an I/O failure, got {failure?.GetType().FullName ?? "no exception"}");
        }
    }
}
