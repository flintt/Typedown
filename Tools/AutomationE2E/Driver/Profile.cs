using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

/// <summary>
/// PF03: where the test host's CPU goes while it opens large documents - an EventPipe sample trace of the host taken
/// during three opens, summarized as the methods most often on the stack (inclusive) and on top of it (exclusive).
/// </summary>
internal static partial class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr OpenThread(int access, bool inherit, int id);
    [System.Runtime.InteropServices.DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out IntPtr info, int size, IntPtr returned);

    /// <summary>The module a thread of a process started in (its start address), as module+offset.</summary>
    private static string ThreadStartModule(int pid, int tid)
    {
        var h = OpenThread(0x0040 /* THREAD_QUERY_INFORMATION */, false, tid);
        if (h == IntPtr.Zero) return "?";
        try
        {
            if (NtQueryInformationThread(h, 9 /* ThreadQuerySetWin32StartAddress */, out var start, IntPtr.Size, IntPtr.Zero) != 0) return "?";
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            foreach (System.Diagnostics.ProcessModule m in p.Modules)
            {
                var b = m.BaseAddress.ToInt64();
                if (start.ToInt64() >= b && start.ToInt64() < b + m.ModuleMemorySize) return $"{m.ModuleName}+0x{start.ToInt64() - b:x}";
            }
            return $"0x{start.ToInt64():x}";
        }
        finally { CloseHandle(h); }
    }

    private static async Task PF03(List<string> notes)
    {
        using var c = await Session("e2e PF03");
        var firstWindow = (string)((Newtonsoft.Json.Linq.JArray)(await c.Call("window.list"))["windows"]!)[0]!["windowId"]!;
        for (var i = 0; i < 300; i++)
        {
            try { if ((string?)(await c.Call("test.editor.eval", new { windowId = firstWindow, script = "document.readyState" }))["result"] == "complete") break; }
            catch (Typedown.Automation.JsonRpcRemoteException) { }
            await Task.Delay(100);
        }
        await Task.Delay(5000);

        Dictionary<int, double> Threads()
        {
            var d = new Dictionary<int, double>();
            using var p = System.Diagnostics.Process.GetProcessById(hostPid);
            foreach (System.Diagnostics.ProcessThread t in p.Threads)
                try { d[t.Id] = t.TotalProcessorTime.TotalMilliseconds; } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            return d;
        }
        var threadsBefore = Threads();
        var client = new DiagnosticsClient(hostPid);
        var providers = new[]
        {
            new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
            new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)(ClrTraceEventParser.Keywords.Default)),
        };
        var file = Path.Combine(outputDir, "pf03.nettrace");
        using (var session = client.StartEventPipeSession(providers, requestRundown: true))
        {
            var copy = Task.Run(async () => { using var f = File.Create(file); await session.EventStream.CopyToAsync(f); });
            for (var round = 0; round < 3; round++)
            {
                var marker = $"PF03-END-{round}";
                var id = await Open(c, Fixture($"pf03-{round}.md", LargeDocument(marker)));
                var windowId = await WindowIdOf(c, id);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!await PageShows(c, windowId, marker) && watch.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(20);
                await Task.Delay(1500);
            }
            session.Stop();
            await copy;
        }

        var threadsAfter = Threads();
        var hot = threadsAfter.Select(t => (id: t.Key, ms: t.Value - threadsBefore.GetValueOrDefault(t.Key))).OrderByDescending(t => t.ms).Take(3).ToList();
        notes.Add("busiest threads: " + string.Join(", ", hot.Select(t => $"{t.id} {t.ms:0} ms ({ThreadStartModule(hostPid, t.id)})")));
        var etlx = TraceLog.CreateFromEventPipeDataFile(file);
        using var log = TraceLog.OpenOrConvert(etlx);
        var inclusive = new Dictionary<string, int>();
        var exclusive = new Dictionary<string, int>();
        var samples = 0;
        var byThread = hot.ToDictionary(t => t.id, t => new Dictionary<string, int>());
        var threadSamples = hot.ToDictionary(t => t.id, t => 0);
        foreach (var e in log.Events.Where(e => e.ProviderName == "Microsoft-DotNETCore-SampleProfiler"))
        {
            var stack = e.CallStack();
            if (stack == null) continue;
            // Only samples of threads doing managed work (a waiting thread's sample sits in a wait).
            var frames = new List<string>();
            for (var s = stack; s != null; s = s.Caller)
            {
                var name = s.CodeAddress.FullMethodName;
                if (string.IsNullOrEmpty(name)) name = s.CodeAddress.ModuleName + "!?";
                frames.Add(name);
            }
            if (byThread.TryGetValue(e.ThreadID, out var mine))
            {
                threadSamples[e.ThreadID]++;
                var key = string.Join(" < ", frames.Take(6).Select(f => f.Length > 70 ? f.Substring(0, 70) : f));
                mine[key] = mine.GetValueOrDefault(key) + 1;
            }
            if (frames.Count == 0 || frames[0].Contains("Wait") || frames[0].Contains("Sleep") || frames.Any(f => f.Contains("GetMessage") || f.Contains("WaitForMultiple"))) continue;
            samples++;
            exclusive[frames[0]] = exclusive.GetValueOrDefault(frames[0]) + 1;
            foreach (var f in frames.Distinct()) inclusive[f] = inclusive.GetValueOrDefault(f) + 1;
        }
        notes.Add($"{samples} busy samples");
        foreach (var (id, ms) in hot)
        {
            notes.Add($"thread {id} ({ms:0} ms CPU): {threadSamples[id]} samples");
            foreach (var kv in byThread[id].OrderByDescending(kv => kv.Value).Take(6)) notes.Add($"  {kv.Value,5} {kv.Key}");
        }
        foreach (var kv in inclusive.Where(kv => !kv.Key.StartsWith("System.Threading") && !kv.Key.Contains("ThreadStart")).OrderByDescending(kv => kv.Value).Take(45))
            notes.Add($"incl {kv.Value,5} {kv.Key}");
        foreach (var kv in exclusive.OrderByDescending(kv => kv.Value).Take(20))
            notes.Add($"excl {kv.Value,5} {kv.Key}");
    }
}
