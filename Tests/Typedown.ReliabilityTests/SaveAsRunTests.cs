using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Typedown.Core.Services;

namespace Typedown.ReliabilityTests;

/// <summary>
/// Save as (Services.SaveAsRun) with each of its waits held: untitled A saved as while untitled B takes the same tab
/// (the audit of 2026-11-24, R01: waiting for the lock or the picker, A's file got B's text and B took the path). Before
/// the write nothing is written; after it A's file is kept and B is given nothing.
/// </summary>
[TestClass]
public class SaveAsRunTests
{
    private sealed class Tab
    {
        public string DocumentId = "doc-A";
        public string Text = "document A";
        public string? Path;
        public bool Saved;
    }

    private string dir = "";

    [TestInitialize]
    public void Setup() => dir = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "saveas-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(dir, true); } catch { } }

    // B comes into the tab at `stage`; what was written, returned and given.
    private async Task<(string? file, string? returned, Tab tab, List<string> said)> Run(string stage)
    {
        var tab = new Tab();
        var target = System.IO.Path.Combine(dir, "a.md");
        var id = tab.DocumentId; // taken as the command came in, before any wait
        var saveLock = new SemaphoreSlim(1, 1);
        var picker = new TaskCompletionSource<string?>();
        var said = new List<string>();
        void BComes() { tab.DocumentId = "doc-B"; tab.Text = "document B"; }
        if (stage == "lock") await saveLock.WaitAsync();
        var run = new SaveAsRun
        {
            Still = () => tab.DocumentId == id,
            Lock = saveLock,
            Target = stage == "picker" ? null : target,
            Pick = () => picker.Task!,
            Refused = _ => Task.FromResult(false),
            Flush = async (gone, _) =>
            {
                await Task.Yield();
                if (stage == "flush") BComes();
                return !gone();
            },
            Text = () => tab.Text,
            Write = async (path, text) =>
            {
                await File.WriteAllTextAsync(path, text);
                if (stage == "write") BComes();
                return true;
            },
            Commit = (path, text) => { tab.Path = path; tab.Saved = true; return path; },
            Say = said.Add,
        }.RunAsync();
        if (stage == "lock") { BComes(); saveLock.Release(); }
        if (stage == "picker") { BComes(); picker.SetResult(target); }
        var returned = await run;
        Assert.AreEqual(1, saveLock.CurrentCount, "the lock is given back");
        return (File.Exists(target) ? File.ReadAllText(target) : null, returned, tab, said);
    }

    [TestMethod]
    public async Task NothingChangingWritesAAndGivesItThePath()
    {
        var (file, returned, tab, _) = await Run("none");
        Assert.AreEqual("document A", file);
        Assert.IsNotNull(returned);
        Assert.IsTrue(tab.Saved);
    }

    [TestMethod]
    [DataRow("lock")]
    [DataRow("picker")]
    [DataRow("flush")]
    public async Task BComingBeforeTheWriteWritesNothing(string stage)
    {
        var (file, returned, tab, said) = await Run(stage);
        Assert.IsNull(file, "no file written - least of all with B's text");
        Assert.IsNull(returned);
        Assert.IsNull(tab.Path, "B is not given the path");
        Assert.IsFalse(tab.Saved, "nor marked saved");
        Assert.AreEqual(1, said.Count, "the stop is logged: " + string.Join("; ", said));
    }

    [TestMethod]
    public async Task BComingDuringTheWriteKeepsAsFileAndGivesBNothing()
    {
        var (file, returned, tab, _) = await Run("write");
        Assert.AreEqual("document A", file, "A's text, taken before B came");
        Assert.IsNull(returned);
        Assert.IsNull(tab.Path, "B keeps its own (no) path");
        Assert.IsFalse(tab.Saved, "and is not marked saved");
    }

    [TestMethod]
    public async Task APickerCancelledWritesNothing()
    {
        var saveLock = new SemaphoreSlim(1, 1);
        var written = false;
        var returned = await new SaveAsRun
        {
            Still = () => true,
            Lock = saveLock,
            Pick = () => Task.FromResult<string?>(null)!,
            Refused = _ => Task.FromResult(false),
            Flush = (_, _) => Task.FromResult(true),
            Text = () => "x",
            Write = (_, _) => { written = true; return Task.FromResult(true); },
            Commit = (p, _) => p,
        }.RunAsync();
        Assert.IsNull(returned);
        Assert.IsFalse(written);
        Assert.AreEqual(1, saveLock.CurrentCount);
    }
}
