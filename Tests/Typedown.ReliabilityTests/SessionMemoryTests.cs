using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;

namespace Typedown.ReliabilityTests;

/// <summary>
/// The session read at start (Services.SessionMemory.Load) with a file on a network place that does not answer (the
/// audit of 2026-11-24, P10a: each file was looked for twice, one after the other, on the start's path - about 53 s on
/// a Windows with an offline share).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SessionMemoryTests
{
    private string dir = "";

    [TestInitialize]
    public void Setup()
    {
        dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "session-" + Guid.NewGuid().ToString("N"))).FullName;
        SessionMemory.StoreFolder = dir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionMemory.StoreFolder = null;
        SessionMemory.FileExists = File.Exists;
        try { Directory.Delete(dir, true); } catch { }
    }

    [TestMethod]
    public void AFileNotAnsweringDoesNotHoldTheStartAndStaysInTheSession()
    {
        var here = Path.Combine(dir, "here.md");
        File.WriteAllText(here, "x");
        var gone = Path.Combine(dir, "gone.md");
        var offline = @"\\offline-server\share\notes.md";
        var asked = 0;
        SessionMemory.FileExists = path =>
        {
            Interlocked.Increment(ref asked);
            if (path == offline) { Thread.Sleep(10000); return false; }
            return File.Exists(path);
        };
        SessionMemory.Save(new[] { gone, offline, here }, activeIndex: 2);

        var clock = Stopwatch.StartNew();
        var session = SessionMemory.Load();
        clock.Stop();

        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(3), $"Load waited {clock.Elapsed.TotalSeconds:0.0} s for the place that does not answer");
        Assert.IsNotNull(session);
        CollectionAssert.AreEqual(new[] { offline, here }, session!.Files, "the file gone dropped; the one not answering kept, for its open to tell");
        Assert.AreEqual(1, session.ActiveIndex, "the active one followed");
        Assert.AreEqual(3, asked, "each file looked for once");
    }

    [TestMethod]
    public void FilesThatAnswerAreAsBefore()
    {
        var a = Path.Combine(dir, "a.md");
        var b = Path.Combine(dir, "b.md");
        File.WriteAllText(a, "a");
        SessionMemory.Save(new[] { a, b }, activeIndex: 0);
        var session = SessionMemory.Load();
        CollectionAssert.AreEqual(new[] { a }, session!.Files);
        Assert.AreEqual(0, session.ActiveIndex);
    }
}
