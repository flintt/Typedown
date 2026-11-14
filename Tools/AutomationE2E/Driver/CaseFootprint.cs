using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// What the test host holds before and after each case: its private memory and its processes' (the WebView2 renderer's
/// the largest), its windows, and the documents open in them. The host is one process for the whole run, so a case
/// meets what the cases before it left: MM01 started at 1070-1190 MB in a full run against 656 MB after two cases, and
/// SR01 met untitled documents two cases back had left (2026-11-14). Kept in each case's result ("footprint") and in the
/// test history; a case that leaves more documents open than it found is named in its notes.
/// </summary>
internal static partial class Program
{
    private sealed record Footprint(double PrivateMb, int Processes, double LargestMb, string Largest, int Windows, List<(string Id, string Name, bool Saved)> Documents)
    {
        public JObject ToJson() => new()
        {
            ["privateMb"] = Math.Round(PrivateMb),
            ["processes"] = Processes,
            ["largestMb"] = Math.Round(LargestMb),
            ["largest"] = Largest,
            ["windows"] = Windows,
            ["documents"] = Documents.Count,
            ["unsaved"] = Documents.Count(d => !d.Saved),
        };
    }

    /// <summary>The host's footprint now, or null when there is no host to ask (it is being restarted, say).</summary>
    private static async Task<Footprint?> TakeFootprint()
    {
        try
        {
            var tree = TreeMemory(hostPid);
            var largest = TreeMemoryEach(hostPid).OrderByDescending(p => p.PrivateMb).FirstOrDefault();
            using var c = await Session("e2e footprint").WaitAsync(TimeSpan.FromSeconds(5));
            var windows = (JArray)(await c.Call("window.list").WaitAsync(TimeSpan.FromSeconds(5)))["windows"]!;
            var documents = new List<(string, string, bool)>();
            foreach (var w in windows)
                foreach (var d in (JArray)(await c.Call("document.list", new { windowId = (string)w["windowId"]! }).WaitAsync(TimeSpan.FromSeconds(5)))["documents"]!)
                {
                    var path = (string?)d["path"];
                    documents.Add(((string)d["documentId"]!, string.IsNullOrEmpty(path) ? "untitled" : Path.GetFileName(path), (bool?)d["saved"] ?? true));
                }
            return new Footprint(tree.privateMb, tree.processes, largest.PrivateMb, largest.Name ?? "", windows.Count, documents);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The footprint before and after for the result, and a note line; the documents the case left open named.</summary>
    private static JObject? NoteFootprint(Footprint? before, Footprint? after, List<string> notes)
    {
        if (before == null && after == null) return null;
        string Mb(Footprint? f) => f == null ? "?" : $"{f.PrivateMb:F0}";
        notes.Add($"footprint: private {Mb(before)} -> {Mb(after)} MB ({before?.Processes.ToString() ?? "?"} -> {after?.Processes.ToString() ?? "?"} processes, largest {after?.Largest ?? before?.Largest} {after?.LargestMb ?? before?.LargestMb:F0} MB); "
            + $"windows {before?.Windows.ToString() ?? "?"} -> {after?.Windows.ToString() ?? "?"}; documents {before?.Documents.Count.ToString() ?? "?"} -> {after?.Documents.Count.ToString() ?? "?"}, unsaved {before?.Documents.Count(d => !d.Saved).ToString() ?? "?"} -> {after?.Documents.Count(d => !d.Saved).ToString() ?? "?"}");
        if (before != null && after != null)
        {
            // More than it found: the new ones named (closing a window's last document leaves a new untitled one, so a
            // new id alone is not one left open).
            var known = before.Documents.Select(d => d.Id).ToHashSet();
            var left = after.Documents.Count > before.Documents.Count ? after.Documents.Where(d => !known.Contains(d.Id)).ToList() : new();
            if (after.Windows > before.Windows || left.Count > 0)
                notes.Add($"left open by this case: {after.Windows - before.Windows} window(s) more; {left.Count} document(s): " + string.Join(", ", left.Select(d => d.Name + (d.Saved ? "" : " (unsaved)"))));
        }
        return new JObject { ["before"] = before?.ToJson(), ["after"] = after?.ToJson() };
    }
}
