using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Typedown.Automation
{
    /// <summary>
    /// document.reviewStatus: what review mode has made of each automation edit, by its operationId - its revisions still
    /// waiting for the reader, and those the reader accepted or rejected. Read from review mode's record of the document:
    /// <c>{ on, revisions: [{ op, ... }], outcomes: [{ op, outcome, ... }] }</c> (the page tags each revision and decision
    /// with the edit it came from).
    /// </summary>
    public static class ReviewStatus
    {
        /// <param name="record">Review mode's record of the document; null when review mode is not available for it (an
        /// edition without it, an untitled document).</param>
        /// <param name="operationIds">The edits asked about; null or empty: every edit the record knows of.</param>
        public static JObject Summarize(JObject? record, IReadOnlyList<string>? operationIds)
        {
            var result = new JObject { ["available"] = record != null, ["on"] = record?["on"]?.Type == JTokenType.Boolean && (bool)record["on"]! };
            var pending = new Dictionary<string, int>(StringComparer.Ordinal);
            var accepted = new Dictionary<string, int>(StringComparer.Ordinal);
            var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
            static int Count(Dictionary<string, int> counts, string op) => counts.TryGetValue(op, out var n) ? n : 0;
            static string? Op(JToken t) => t is JObject o && o["op"]?.Type == JTokenType.String ? (string?)o["op"] : null;
            foreach (var revision in record?["revisions"] as JArray ?? new JArray())
                if (Op(revision) is { } op) pending[op] = Count(pending, op) + 1;
            foreach (var outcome in record?["outcomes"] as JArray ?? new JArray())
            {
                if (Op(outcome) is not { } op) continue;
                var which = (string?)outcome["outcome"] == "rejected" ? rejected : accepted;
                which[op] = Count(which, op) + 1;
            }
            var asked = operationIds is { Count: > 0 }
                ? operationIds.Distinct(StringComparer.Ordinal).ToList()
                : pending.Keys.Concat(accepted.Keys).Concat(rejected.Keys).Distinct(StringComparer.Ordinal).ToList();
            result["operations"] = new JArray(asked.Select(op =>
            {
                int p = Count(pending, op), a = Count(accepted, op), r = Count(rejected, op);
                return new JObject
                {
                    ["operationId"] = op,
                    ["pending"] = p,
                    ["accepted"] = a,
                    ["rejected"] = r,
                    ["status"] = StatusOf(p, a, r),
                };
            }));
            return result;
        }

        /// <summary>
        /// none: review mode holds nothing of the edit (it was off, or the edit changed nothing it tracks); pending: all of
        /// it waits for the reader; accepted / rejected: all of it decided one way; partly: some decided, or both ways.
        /// </summary>
        public static string StatusOf(int pending, int accepted, int rejected) =>
            pending + accepted + rejected == 0 ? "none"
            : accepted + rejected == 0 ? "pending"
            : pending == 0 && rejected == 0 ? "accepted"
            : pending == 0 && accepted == 0 ? "rejected"
            : "partly";
    }
}
