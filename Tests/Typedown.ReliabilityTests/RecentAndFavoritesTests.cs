using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;
using Typedown.Core.Utilities;

namespace Typedown.ReliabilityTests
{
    /// <summary>Recent files and folders, favourites, the folder pane's navigation (docs/todo.md, 2026-10-14).</summary>
    [TestClass]
    public class RecentAndFavoritesTests
    {
        private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;

        private static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        [TestMethod]
        public void AnEntryIsItsNameAndTheFolderItIsIn()
        {
            Assert.AreEqual("a.md — docs", RecentPaths.Label(P("work", "docs", "a.md")));
            Assert.AreEqual("docs — work", RecentPaths.Label(P("work", "docs")));
            Assert.AreEqual("docs — work", RecentPaths.Label(P("work", "docs") + Path.DirectorySeparatorChar));
            Assert.AreEqual("a.md", RecentPaths.Name(P("a.md")));
            Assert.AreEqual(Root, RecentPaths.Parent(P("a.md")));
            Assert.AreEqual(Root, RecentPaths.Label(Root));
        }

        [TestMethod]
        public void TheFoldersAboveAFolderGoUpToItsDrive()
        {
            CollectionAssert.AreEqual(new[] { P("a", "b"), P("a"), Root }, RecentPaths.Ancestors(P("a", "b", "c")).ToArray());
            Assert.AreEqual(0, RecentPaths.Ancestors(Root).Count);
            Assert.AreEqual(0, RecentPaths.Ancestors(null).Count);
        }

        [TestMethod]
        public void InsideMeansTheFolderItselfOrBelowItNotANameThatStartsTheSame()
        {
            Assert.IsTrue(RecentPaths.IsInside(P("a", "b", "c.md"), P("a")));
            Assert.IsTrue(RecentPaths.IsInside(P("a"), P("a") + Path.DirectorySeparatorChar));
            Assert.IsFalse(RecentPaths.IsInside(P("ab", "c.md"), P("a")));
            Assert.IsTrue(RecentPaths.Same(P("a", "b"), P("a", "b") + Path.DirectorySeparatorChar));
        }

        [TestMethod]
        public async Task APathIsMissingOnlyOnceItIsKnownToBeGone()
        {
            using var directory = new TestDirectory();
            var there = directory.File("there.md");
            File.WriteAllText(there, "x");
            var gone = directory.File("gone.md");
            PathProbe.Forget();
            var missing = await PathProbe.MissingAsync(new[] { there, gone, directory.Path });
            CollectionAssert.AreEquivalent(new[] { gone }, missing.ToArray());
        }

        [TestMethod]
        public async Task APathThatDoesNotAnswerInTimeCountsAsThere()
        {
            PathProbe.Forget();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var missing = await PathProbe.MissingAsync(new[] { P("slow", "share.md"), P("fast", "gone.md") }, TimeSpan.FromMilliseconds(200),
                path => { if (path.Contains("slow")) Thread.Sleep(3000); return false; });
            Assert.IsTrue(clock.ElapsedMilliseconds < 2000, $"waited {clock.ElapsedMilliseconds} ms for a path that did not answer");
            CollectionAssert.AreEquivalent(new[] { P("fast", "gone.md") }, missing.ToArray());
            PathProbe.Forget();
        }
    
        [TestMethod]
        public async Task FavouritesAreKeptInTheirFileAndReadBack()
        {
            using var directory = new TestDirectory();
            var file = directory.File("favorites.json");
            var store = new FavoritesStore(file);
            Assert.IsTrue(store.Add(P("notes", "a.md"), folder: false));
            Assert.IsTrue(store.Add(P("notes"), folder: true));
            Assert.IsFalse(store.Add(P("notes", "a.md") , folder: false), "the same file twice");
            await store.FlushAsync();

            var again = new FavoritesStore(file);
            await again.LoadAsync();
            Assert.AreEqual(2, again.Items.Count);
            Assert.IsTrue(again.Contains(P("notes") + Path.DirectorySeparatorChar));
            Assert.IsTrue(again.Items.Single(x => x.Folder).Path == P("notes"));
            Assert.IsTrue(again.Remove(P("notes", "a.md")));
            await again.FlushAsync();
            var third = new FavoritesStore(file);
            await third.LoadAsync();
            Assert.AreEqual(1, third.Items.Count);
            Assert.IsTrue(third.Contains(P("notes")));
        }

        [TestMethod]
        public void AtMostFiftyFavourites()
        {
            using var directory = new TestDirectory();
            var store = new FavoritesStore(directory.File("favorites.json"));
            for (var i = 0; i < FavoritesStore.MaxCount; i++) Assert.IsTrue(store.Add(P("f", i + ".md"), false));
            Assert.IsFalse(store.Add(P("f", "one-more.md"), false));
            Assert.AreEqual(FavoritesStore.MaxCount, store.Items.Count);
        }

        [TestMethod]
        public void ARenameMovesTheFavouriteAndEverythingInARenamedFolder()
        {
            using var directory = new TestDirectory();
            var store = new FavoritesStore(directory.File("favorites.json"));
            store.Add(P("docs", "a.md"), false);
            store.Add(P("docs", "sub"), true);
            store.Add(P("docsother", "b.md"), false);
            Assert.AreEqual(2, store.Rename(P("docs"), P("papers")));
            CollectionAssert.AreEquivalent(new[] { P("papers", "a.md"), P("papers", "sub"), P("docsother", "b.md") }, store.Items.Select(x => x.Path).ToArray());
            Assert.AreEqual(1, store.Rename(P("papers", "a.md"), P("papers", "c.md")));
            Assert.IsTrue(store.Contains(P("papers", "c.md")));
        }

        [TestMethod]
        public async Task AFileThatCannotBeReadIsKeptAsideNotOverwritten()
        {
            using var directory = new TestDirectory();
            var file = directory.File("favorites.json");
            File.WriteAllText(file, "{ not json");
            var store = new FavoritesStore(file);
            await store.LoadAsync();
            Assert.AreEqual(0, store.Items.Count);
            Assert.AreEqual("{ not json", File.ReadAllText(file + ".bad"));
        }
}
}
