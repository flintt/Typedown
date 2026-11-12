using System;
using Typedown.Automation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Typedown.Mcp
{
    /// <summary>
    /// The MCP side (stdio, one JSON-RPC message per line): tools mapped onto the automation API, and a few done here on
    /// the text read (several edits as one, part of a document, what became of this session's last edit). Every write
    /// carries the revision it was based on; a conflict or a mismatched match count comes back as an error telling the
    /// agent to read the document again, and nothing here retries with old text.
    /// </summary>
    public sealed class McpServer
    {
        public static readonly string[] ProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly TextReader input;
        private readonly TextWriter output;
        private readonly TypedownConnection typedown;
        private string clientName = "";

        public McpServer(TextReader input, TextWriter output, Func<Func<string>, TypedownConnection> connection)
        {
            this.input = input;
            this.output = output;
            typedown = connection(() => ClientName);
        }

        /// <summary>What the Typedown window title shows: the agent's own name when it gave one.</summary>
        public string ClientName => string.IsNullOrWhiteSpace(clientName) ? Brand.McpServerName : $"{clientName} (MCP)";

        public async Task RunAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await input.ReadLineAsync().ConfigureAwait(false);
                if (line == null) break;
                if (line.Trim().Length == 0) continue;
                JObject message;
                try { message = JObject.Parse(line); }
                catch (JsonException)
                {
                    await WriteAsync(new JObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JObject { ["code"] = -32700, ["message"] = "Parse error" } }).ConfigureAwait(false);
                    continue;
                }
                var reply = await HandleAsync(message, ct).ConfigureAwait(false);
                if (reply != null) await WriteAsync(reply).ConfigureAwait(false);
            }
        }

        private async Task WriteAsync(JObject message)
        {
            await output.WriteLineAsync(message.ToString(Formatting.None)).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }

        public async Task<JObject?> HandleAsync(JObject message, CancellationToken ct)
        {
            var id = message["id"];
            var method = (string?)message["method"];
            // Notifications (no id) get no reply; responses from the client are not expected.
            if (id == null || id.Type == JTokenType.Null) return null;
            try
            {
                JToken result = method switch
                {
                    "initialize" => Initialize(message["params"] as JObject),
                    "ping" => new JObject(),
                    "tools/list" => new JObject { ["tools"] = ToolList() },
                    "tools/call" => await CallToolAsync(message["params"] as JObject, ct).ConfigureAwait(false),
                    _ => throw new RpcError(-32601, $"Method not found: {method}"),
                };
                return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            }
            catch (RpcError e)
            {
                return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = e.Code, ["message"] = e.Message } };
            }
        }

        private JObject Initialize(JObject? p)
        {
            var requested = (string?)p?["protocolVersion"];
            clientName = Clean((string?)p?["clientInfo"]?["name"]);
            return new JObject
            {
                ["protocolVersion"] = requested != null && ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0],
                ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false } },
                ["serverInfo"] = new JObject { ["name"] = Brand.McpServerName, ["title"] = Brand.Name, ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString() ?? "" },
                ["instructions"] = Instructions,
            };
        }

        private static string Clean(string? name) =>
            new string((name ?? "").Where(c => !char.IsControl(c) && !(c >= '‪' && c <= '‮') && !(c >= '⁦' && c <= '⁩')).Take(30).ToArray()).Trim();

        public const string Instructions =
            $"{Brand.Name} edits Markdown documents that are open in its windows while a person watches. Always read a document " +
            "(typedown_read_document) right before changing it and pass the revision you read as baseRevision. For a long " +
            "document read only the part you need (heading, or startLine/endLine): the revision is the whole document's either way. " +
            "Prefer typedown_replace_text with a short passage that occurs exactly as often as you mean to change it; for a change " +
            "in several places (moving a passage, renaming throughout) use typedown_edit_text with all the finds at once - one " +
            "revision, all or nothing - and do not replace the whole document. When a write fails with revision_conflict or " +
            "match_count_mismatch, read the document again and redo the edit against the new text; never resend an edit computed " +
            "from old text. The person may review your edits and reject them: a read reports what became of this session's last " +
            "edit (lastEditThisSession; when the document is in review, its review field says whether the person accepted or rejected it) - check it before building on that edit. Each read and write reports normalization: " +
            "pendingNormalization 'none' or 'knownFormatting' means nothing to do; 'unknown' means the person's first edit in the " +
            "visual editor may reformat the document (reasons say how - 'formatting-changed' is layout only, such as list markers " +
            "or spacing, no words lost); a write is refused then unless you pass allowFormattingChanges: true, which is fine when " +
            "only formatting may change. When the person is watching, pass scrollToChange so they see where the text changed.";

        // ---- tools -------------------------------------------------------------------------------------------

        private static JObject Schema(JObject properties, params string[] required) => new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray(required),
            ["additionalProperties"] = false,
        };

        private static JObject Prop(string type, string description) => new() { ["type"] = type, ["description"] = description };

        private static readonly JObject WriteOptions = new()
        {
            ["save"] = Prop("boolean", "Also save the file after the edit (not for untitled documents). Default false."),
            ["allowFormattingChanges"] = Prop("boolean", $"Write even when the result's pendingNormalization would be 'unknown': {Brand.Name} cannot prove its visual editor keeps the exact Markdown, so the person's first edit there may reformat it (reasons say how; 'formatting-changed' is layout only - list markers, spacing, table alignment - no words lost). Default false: such a write is refused (normalization_unclassified) and nothing changes. Pass true when only formatting may change; text that would lose content is refused either way (content_not_roundtrippable)."),
            ["reveal"] = Prop("boolean", "Bring the document's window and tab to the front so the person sees the change. Default false."),
            ["scrollToChange"] = Prop("boolean", "As reveal, and scroll the person's view to the first changed passage when it is off screen; their cursor stays where it is. Default false."),
        };

        private static JObject With(JObject properties, JObject extra)
        {
            var all = (JObject)properties.DeepClone();
            foreach (var p in extra.Properties()) all[p.Name] = p.Value.DeepClone();
            return all;
        }

        private static JArray ToolList() => new()
        {
            new JObject
            {
                ["name"] = "typedown_list_documents",
                ["title"] = "List open documents",
                ["description"] = $"The documents open in {Brand.Name}'s windows: id, window, path (null when untitled), title, revision, whether saved, whether it is the tab shown in its window. No text.",
                ["inputSchema"] = Schema(new JObject { ["windowId"] = Prop("string", "Only this window's documents.") }),
                ["annotations"] = new JObject { ["readOnlyHint"] = true, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_read_document",
                ["title"] = "Read a document",
                ["description"] = $"The document's current text (including what the person typed and {Brand.Name} has not yet reported), its revision and content hash. Use the revision as baseRevision for the next edit. " +
                    "For a long document give heading (a heading's section) or startLine/endLine: text is then only that part, range says which lines (1-based) of totalLines, and revision and contentHash are still the whole document's. " +
                    "normalizationNote says in words what normalization means for your next write. lastEditThisSession says whether the text your last edit wrote is still there (gone: the person rejected it in review, undid or rewrote it).",
                ["inputSchema"] = Schema(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["includeHeadings"] = Prop("boolean", "Also return the headings (level, text, slug). Default false."),
                    ["heading"] = Prop("string", "Return only this heading's section: from the heading to the next heading of the same or a higher level. Its text, as written after the #s (case does not matter)."),
                    ["headingOccurrence"] = Prop("integer", "When several headings have that text: which one, from 1."),
                    ["startLine"] = Prop("integer", "Return only lines from this one (1-based)."),
                    ["endLine"] = Prop("integer", "Return only lines up to this one, included."),
                }, "documentId"),
                ["annotations"] = new JObject { ["readOnlyHint"] = true, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_replace_text",
                ["title"] = "Replace text in a document",
                ["description"] = "Replaces every occurrence of find with replacement, only if find occurs exactly expectedCount times in the revision you read. Choose a short, distinctive find; the document uses \\n line endings.",
                ["inputSchema"] = Schema(With(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["baseRevision"] = Prop("integer", "The revision you read the text at."),
                    ["find"] = Prop("string", "Exact text to find (not a pattern)."),
                    ["replacement"] = Prop("string", "What replaces each occurrence."),
                    ["expectedCount"] = Prop("integer", "How many times find occurs. Default 1."),
                }, WriteOptions), "documentId", "baseRevision", "find", "replacement"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_edit_text",
                ["title"] = "Make several edits at once",
                ["description"] = "Applies several find/replace edits as one change: one new revision, one step to undo, one change for the person to review. Every find is counted in the revision you read, all must occur exactly expectedCount times and no two may overlap; otherwise nothing is changed and the error names the edit (editIndex). Use it to move a passage (remove it in one place, insert it in another) or to change several places together, instead of replacing the whole document. Finds are exact text, not patterns; the document uses \\n line endings.",
                ["inputSchema"] = Schema(With(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["baseRevision"] = Prop("integer", "The revision you read the text at."),
                    ["edits"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["description"] = "The edits, all counted against the same text you read.",
                        ["items"] = Schema(new JObject
                        {
                            ["find"] = Prop("string", "Exact text to find (not a pattern)."),
                            ["replacement"] = Prop("string", "What replaces each occurrence ('' removes it)."),
                            ["expectedCount"] = Prop("integer", "How many times find occurs. Default 1."),
                        }, "find", "replacement"),
                    },
                }, WriteOptions), "documentId", "baseRevision", "edits"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_replace_document",
                ["title"] = "Replace a document's whole text",
                ["description"] = "Replaces the whole text of the document, only if it is still at baseRevision. Prefer typedown_replace_text for one change and typedown_edit_text for several: a whole new text is long to send and hard for the person to review. The text must use \\n line endings.",
                ["inputSchema"] = Schema(With(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["baseRevision"] = Prop("integer", "The revision you read the text at."),
                    ["text"] = Prop("string", "The new text of the whole document."),
                }, WriteOptions), "documentId", "baseRevision", "text"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = true, ["idempotentHint"] = true, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_save_document",
                ["title"] = "Save a document",
                ["description"] = "Saves the document to its file. Untitled documents cannot be saved this way (path_required).",
                ["inputSchema"] = Schema(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["baseRevision"] = Prop("integer", "Save only if the document is still at this revision."),
                }, "documentId"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = true, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_close_document",
                ["title"] = "Close a document",
                ["description"] = "Closes a document's tab. Only a saved document is closed: one with unsaved changes is refused (unsaved_changes) and nothing is lost - save it first or leave it open. The window's only document is replaced by an empty one; the window stays.",
                ["inputSchema"] = Schema(new JObject
                {
                    ["documentId"] = Prop("string", "From typedown_list_documents."),
                    ["baseRevision"] = Prop("integer", "Close only if the document is still at this revision."),
                }, "documentId"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = false, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_get_view",
                ["title"] = "Read how a window shows its document",
                ["description"] = "The window's editing mode (visual, source or reading), side pane, status bar, focus and typewriter mode, and its bounds on screen in pixels.",
                ["inputSchema"] = Schema(new JObject { ["windowId"] = Prop("string", "From typedown_list_documents.") }, "windowId"),
                ["annotations"] = new JObject { ["readOnlyHint"] = true, ["openWorldHint"] = false },
            },
            new JObject
            {
                ["name"] = "typedown_set_view",
                ["title"] = "Change how a window shows its document",
                ["description"] = "Switches the editing mode, opens or closes the side pane (files or outline), shows or hides the status bar, turns focus or typewriter mode on or off, or moves and resizes the window. Mode, pane, status bar, focus and typewriter are the person's own settings and stay as set; change them only when asked, and put them back afterwards. The text is not changed. Returns the view as it is afterwards.",
                ["inputSchema"] = Schema(new JObject
                {
                    ["windowId"] = Prop("string", "From typedown_list_documents."),
                    ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("visual", "source", "reading"), ["description"] = "visual (rendered, editable), source (the raw Markdown) or reading (rendered, not editable)." },
                    ["sidePane"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { ["open"] = Prop("boolean", "Show the side pane."), ["page"] = new JObject { ["type"] = "string", ["enum"] = new JArray("files", "outline") } },
                        ["additionalProperties"] = false,
                    },
                    ["statusBar"] = Prop("boolean", "Show the status bar."),
                    ["focusMode"] = Prop("boolean", "Dim everything but the current paragraph."),
                    ["typewriter"] = Prop("boolean", "Keep the current line in the middle of the window."),
                    ["bounds"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "The window's outer bounds in screen pixels; give any of them. A maximized window is restored first.",
                        ["properties"] = new JObject { ["x"] = Prop("integer", ""), ["y"] = Prop("integer", ""), ["width"] = Prop("integer", "At least 480."), ["height"] = Prop("integer", "At least 320.") },
                        ["additionalProperties"] = false,
                    },
                }, "windowId"),
                ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = true, ["openWorldHint"] = false },
            },
        };

        private async Task<JObject> CallToolAsync(JObject? p, CancellationToken ct)
        {
            var name = (string?)p?["name"] ?? throw new RpcError(-32602, "tools/call needs a tool name.");
            var args = p?["arguments"] as JObject ?? new JObject();
            try
            {
                JToken result = name switch
                {
                    "typedown_list_documents" => await typedown.CallAsync("document.list", Pick(args, "windowId"), ct).ConfigureAwait(false),
                    "typedown_read_document" => await ReadAsync(args, ct).ConfigureAwait(false),
                    "typedown_replace_text" => Wrote(args, new[] { new TextEdits.Edit((string)Required(args, "find", JTokenType.String)!, (string)Required(args, "replacement", JTokenType.String)!, (int?)args["expectedCount"] ?? 1) },
                        await typedown.CallAsync("document.replaceText", WriteParams(args, new JObject
                        {
                            ["find"] = Required(args, "find", JTokenType.String),
                            ["replacement"] = Required(args, "replacement", JTokenType.String),
                            ["expectedCount"] = args["expectedCount"] ?? 1,
                        }), ct).ConfigureAwait(false)),
                    "typedown_edit_text" => await EditTextAsync(args, ct).ConfigureAwait(false),
                    "typedown_replace_document" => Wrote(args, Array.Empty<TextEdits.Edit>(),
                        await typedown.CallAsync("document.replace", WriteParams(args, new JObject
                        {
                            ["text"] = Required(args, "text", JTokenType.String),
                        }), ct).ConfigureAwait(false)),
                    "typedown_save_document" => await typedown.CallAsync("document.save", Pick(args, "documentId", "baseRevision"), ct).ConfigureAwait(false),
                    "typedown_close_document" => await typedown.CallAsync("document.close", Pick(args, "documentId", "baseRevision"), ct).ConfigureAwait(false),
                    "typedown_get_view" => await typedown.CallAsync("window.getView", Pick(args, "windowId"), ct).ConfigureAwait(false),
                    "typedown_set_view" => await typedown.CallAsync("window.setView", Pick(args, "windowId", "mode", "sidePane", "statusBar", "focusMode", "typewriter", "bounds"), ct).ConfigureAwait(false),
                    _ => throw new RpcError(-32602, $"Unknown tool: {name}"),
                };
                return Success(result);
            }
            catch (ApiError e)
            {
                return Failure(e.Error, Guidance(name, e.Kind, e.Error["data"] as JObject));
            }
            catch (NotConnectedException e)
            {
                var error = new JObject { ["code"] = 0, ["message"] = e.Message, ["data"] = new JObject { ["kind"] = "not_connected" } };
                return Failure(error, name.Contains("replace") || name.Contains("edit") || name.Contains("save")
                    ? $"{Brand.Name} could not be reached. If a write was under way it may or may not have been applied: once {Brand.Name} is back, read the document before doing anything else. If {Brand.Name} is running, the person has to turn on Settings > General > Allow local automation."
                    : $"{Brand.Name} is not running, or its automation switch is off (Settings > General > Allow local automation). Ask the person.");
            }
            catch (ArgumentException e)
            {
                var error = new JObject { ["code"] = -32602, ["message"] = e.Message, ["data"] = new JObject { ["kind"] = "invalid_params", ["field"] = e.ParamName } };
                return Failure(error, "Fix the arguments as the message says.");
            }
        }

        private async Task<JToken> ReadAsync(JObject args, CancellationToken ct)
        {
            var include = new JArray("text");
            if ((bool?)args["includeHeadings"] == true) include.Add("headings");
            var p = Pick(args, "documentId");
            if (p["documentId"] == null) throw new ArgumentException("documentId is required.", "documentId");
            p["consistency"] = "latest";
            p["include"] = include;
            var result = await typedown.CallAsync("document.get", p, ct).ConfigureAwait(false);
            if (result is not JObject read || read["text"] is not JValue { Type: JTokenType.String } textValue) return result;
            var text = (string)textValue!;
            var documentId = (string)p["documentId"]!;
            // What became of this session's last edit, judged on the whole text before a part of it is cut out.
            if (lastWrites.TryGetValue(documentId, out var last))
            {
                var status = LastEditStatus(last, (long?)read["revision"], text);
                // And what review mode says of it, when the document is in review: the person's own verdict, not a guess
                // from the text.
                if (last.OperationId != null && await ReviewOfAsync(documentId, last.OperationId, ct).ConfigureAwait(false) is JObject review)
                    status["review"] = review;
                read["lastEditThisSession"] = status;
            }
            NoteNormalization(read);

            // A part of the document: a heading's section, or lines.
            var heading = (string?)args["heading"];
            var start = (int?)args["startLine"];
            var end = (int?)args["endLine"];
            if (heading == null && start == null && end == null) return read;
            var total = TextEdits.Lines(text).Length;
            var range = new JObject { ["totalLines"] = total };
            if (heading != null)
            {
                var (from, to, found, matches) = TextEdits.Section(text, heading, (int?)args["headingOccurrence"] ?? 0);
                if (found == null)
                {
                    var known = string.Join("; ", TextEdits.Headings(text).Take(60).Select(h => $"{new string('#', h.Level)} {h.Text} (line {h.Line})"));
                    throw new ArgumentException(matches > 1
                        ? $"{matches} headings read \"{heading}\": give headingOccurrence (1 to {matches})."
                        : $"No heading reads \"{heading}\". The headings: {known}", "heading");
                }
                (start, end) = (from, to);
                range["heading"] = new JObject { ["text"] = found.Text, ["level"] = found.Level, ["line"] = found.Line };
            }
            var (part, s, e) = TextEdits.Slice(text, start ?? 1, end ?? total);
            read["text"] = part;
            range["startLine"] = s;
            range["endLine"] = e;
            read["range"] = range;
            return read;
        }

        // Several find/replace edits as one write: counted and applied here on the latest text at baseRevision, then the
        // whole result written with that baseRevision - one revision, and nothing changed if any edit does not fit.
        private async Task<JToken> EditTextAsync(JObject args, CancellationToken ct)
        {
            var documentId = (string)Required(args, "documentId", JTokenType.String)!;
            var baseRevision = (long)Required(args, "baseRevision", JTokenType.Integer);
            if (args["edits"] is not JArray list || list.Count == 0) throw new ArgumentException("edits is required: one or more { find, replacement, expectedCount }.", "edits");
            var edits = list.Select((e, i) => e is JObject o && o["find"]?.Type == JTokenType.String && o["replacement"]?.Type == JTokenType.String
                ? new TextEdits.Edit((string)o["find"]!, (string)o["replacement"]!, (int?)o["expectedCount"] ?? 1)
                : throw new ArgumentException($"edits[{i}] needs find and replacement (strings).", "edits")).ToList();
            var read = (JObject)await typedown.CallAsync("document.get", new JObject { ["documentId"] = documentId, ["consistency"] = "latest", ["include"] = new JArray("text") }, ct).ConfigureAwait(false);
            var revision = (long?)read["revision"];
            if (revision != baseRevision)
                throw new ApiError(new JObject { ["code"] = -32012, ["message"] = $"The document is at revision {revision}, not {baseRevision}.", ["data"] = new JObject { ["kind"] = "revision_conflict", ["revision"] = revision } });
            var (text, refusal) = TextEdits.Apply((string)read["text"]!, edits);
            if (refusal != null)
                throw new ApiError(new JObject
                {
                    ["code"] = refusal.Kind == "match_count_mismatch" ? -32019 : -32602,
                    ["message"] = refusal.Message,
                    ["data"] = new JObject { ["kind"] = refusal.Kind, ["editIndex"] = refusal.Index, ["actualCount"] = refusal.ActualCount, ["expectedCount"] = edits[refusal.Index].ExpectedCount },
                });
            // Kept with how often each replacement is in the new text (copies already there counted too), so a read can
            // tell when some of them went.
            var kept = edits.Select(e => new TextEdits.Edit(e.Find, e.Replacement, Occurrences(text!, e.Replacement))).ToList();
            return Wrote(args, kept, await typedown.CallAsync("document.replace", WriteParams(args, new JObject { ["text"] = text }), ct).ConfigureAwait(false));
        }

        private static int Occurrences(string text, string s)
        {
            if (s.Length == 0) return 0;
            var n = 0;
            for (var at = text.IndexOf(s, StringComparison.Ordinal); at >= 0; at = text.IndexOf(s, at + s.Length, StringComparison.Ordinal)) n++;
            return n;
        }

        // ---- this session's last edit ---------------------------------------------------------------------------

        /// <summary>A write this session made: its revision, and the text it put in (what a read looks for again).</summary>
        private sealed class LastWrite
        {
            public LastWrite(long revision, IReadOnlyList<(string Text, int Count)> wrote, string? operationId) { Revision = revision; Wrote = wrote; OperationId = operationId; }
            public long Revision { get; }
            /// <summary>The write's operationId: review mode is asked about it (document.reviewStatus).</summary>
            public string? OperationId { get; }
            public IReadOnlyList<(string Text, int Count)> Wrote { get; }
        }

        private readonly Dictionary<string, LastWrite> lastWrites = new();

        /// <summary>A successful write's result, kept as this session's last edit of the document; returned as it is.</summary>
        private JToken Wrote(JObject args, IReadOnlyList<TextEdits.Edit> edits, JToken result)
        {
            if (result is JObject r && (long?)r["revision"] is long revision && (string?)args["documentId"] is string documentId)
            {
                lastWrites[documentId] = new LastWrite(revision, edits.Where(e => e.Replacement.Length > 0).Select(e => (e.Replacement, e.ExpectedCount)).ToList(), (string?)r["operationId"]);
                NoteNormalization(r);
            }
            return result;
        }

        /// <summary>
        /// Review mode's verdict on one write (document.reviewStatus): its status (pending, accepted, rejected, partly), the
        /// counts and a note. Null when review mode is not available, the document is not in review and holds nothing of the
        /// edit, or the host does not have the method (an older one).
        /// </summary>
        private async Task<JObject?> ReviewOfAsync(string documentId, string operationId, CancellationToken ct)
        {
            JToken answer;
            try { answer = await typedown.CallAsync("document.reviewStatus", new JObject { ["documentId"] = documentId, ["operationIds"] = new JArray(operationId) }, ct).ConfigureAwait(false); }
            catch (Exception) { return null; }
            if (answer is not JObject r || (bool?)r["available"] != true) return null;
            var op = (r["operations"] as JArray)?.OfType<JObject>().FirstOrDefault();
            var status = (string?)op?["status"];
            if (op == null || status == null || (status == "none" && (bool?)r["on"] != true)) return null;
            op.Remove("operationId");
            op["note"] = status switch
            {
                "pending" => "Your last edit waits in review: the person has not accepted or rejected it yet.",
                "accepted" => "The person accepted your last edit in review.",
                "rejected" => "The person rejected your last edit in review. Do not build on it; decide again from the current text.",
                "partly" => "The person decided on part of your last edit in review (see pending/accepted/rejected). Check those places before going on.",
                _ => "The document is in review, but review mode holds nothing of your last edit.",
            };
            return op;
        }

        /// <summary>
        /// Whether the text this session's last edit wrote is still in the document. Judged on the text, not on the review:
        /// the person may have rejected the edit in review, undone it or written over it, and the automation API does not
        /// say which (an agent learnt its first edits were rejected only by reading the whole text again and comparing).
        /// </summary>
        private static JObject LastEditStatus(LastWrite last, long? revision, string text)
        {
            var status = new JObject { ["revision"] = last.Revision };
            if (revision == last.Revision)
            {
                status["status"] = "unchanged";
                status["note"] = "The document is as your last edit left it.";
                return status;
            }
            if (last.Wrote.Count == 0)
            {
                status["status"] = "changed";
                status["note"] = $"The document changed after your last edit (revision {last.Revision}, now {revision}); that edit wrote no text to look for. Compare the text with what you meant.";
                return status;
            }
            var present = last.Wrote.Count(w => Occurrences(text, w.Text) >= w.Count);
            if (present == last.Wrote.Count)
            {
                status["status"] = "present";
                status["note"] = $"The document changed after your last edit (revision {last.Revision}, now {revision}), but the text that edit wrote is still there.";
            }
            else if (present == 0)
            {
                status["status"] = "gone";
                status["note"] = $"The text your last edit wrote (revision {last.Revision}) is no longer in the document: the person rejected it in review, undid it or rewrote it. Do not build on that edit; decide again from this text.";
            }
            else
            {
                status["status"] = "partly";
                status["note"] = $"Only part of the text your last edit wrote (revision {last.Revision}) is still in the document: some of it was rejected, undone or rewritten. Check those places before going on.";
            }
            return status;
        }

        /// <summary>What normalization means for the next write, in words, beside the fields.</summary>
        private static void NoteNormalization(JObject result)
        {
            var n = result["normalization"] as JObject;
            var pending = (string?)n?["pendingNormalization"];
            var reasons = string.Join(", ", (n?["reasons"] as JArray)?.Select(r => (string?)r) ?? Array.Empty<string>());
            string? note = pending switch
            {
                "none" => "The visual editor keeps this text exactly. Nothing to do.",
                "knownFormatting" => "The visual editor will only make known, harmless formatting changes. Nothing to do.",
                "unknown" when reasons == "notEvaluated" => $"{Brand.Name} has not checked this document in its visual editor yet (its tab was not shown). A write is refused unless you pass allowFormattingChanges: true; pass it if formatting changes on the person's first visual edit are acceptable.",
                "unknown" => $"The person's first edit in the visual editor may reformat this text ({(reasons.Length == 0 ? "not classified" : reasons)}; 'formatting-changed' is layout only, such as list markers or spacing, no words lost). Writes are refused unless you pass allowFormattingChanges: true; pass it when only formatting may change.",
                "unsafe" => "The visual editor would lose part of this text (usually raw HTML) on the person's first edit there. Writes that keep such text are refused; write that part differently, or ask the person.",
                _ => null,
            };
            if (note != null) result["normalizationNote"] = note;
        }

        private static JObject WriteParams(JObject args, JObject specific)
        {
            var p = new JObject
            {
                ["documentId"] = Required(args, "documentId", JTokenType.String),
                ["baseRevision"] = Required(args, "baseRevision", JTokenType.Integer),
            };
            foreach (var s in specific.Properties()) p[s.Name] = s.Value;
            if ((bool?)args["save"] == true) p["save"] = true;
            if ((bool?)args["allowFormattingChanges"] == true) p["normalizationPolicy"] = "allowUnknown";
            if ((bool?)args["scrollToChange"] == true) p["reveal"] = "change";
            else if ((bool?)args["reveal"] == true) p["reveal"] = "document";
            return p;
        }

        private static JToken Required(JObject args, string name, JTokenType type) =>
            args[name] is JToken t && t.Type == type ? t : throw new ArgumentException($"{name} is required ({type.ToString().ToLowerInvariant()}).", name);

        private static JObject Pick(JObject args, params string[] names)
        {
            var p = new JObject();
            foreach (var n in names) if (args[n] is JToken t && t.Type != JTokenType.Null) p[n] = t;
            return p;
        }

        private static JObject Success(JToken result)
        {
            var structured = result as JObject ?? new JObject { ["result"] = result };
            return new JObject
            {
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = structured.ToString(Formatting.None) }),
                ["structuredContent"] = structured,
                ["isError"] = false,
            };
        }

        /// <summary>
        /// The error exactly as Typedown gave it (the same object typedownctl --json prints under "error"), plus what the
        /// agent should do next.
        /// </summary>
        private static JObject Failure(JObject error, string next)
        {
            var structured = new JObject { ["error"] = error, ["next"] = next };
            return new JObject
            {
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = $"{error["message"]} [{error["data"]?["kind"]}] {next}\n{structured.ToString(Formatting.None)}" }),
                ["structuredContent"] = structured,
                ["isError"] = true,
            };
        }

        public static string Guidance(string tool, string kind, JObject? data) => kind switch
        {
            "revision_conflict" =>
                $"The document is no longer at baseRevision (it is at {data?["revision"] ?? "a newer revision"}): the person or another program changed it. Nothing was written. Call typedown_read_document again and redo the edit against the new text; do not resend this edit.",
            "match_count_mismatch" when data?["editIndex"] != null =>
                $"edits[{data["editIndex"]}].find occurs {data["actualCount"]} time(s), not {data["expectedCount"]}. Nothing was written - none of the edits. Read the document again and fix that edit's find (or its expectedCount); do not retry with the old text.",
            "edits_overlap" =>
                $"Two edits change the same text ({data?["editIndex"]} and an earlier one). Nothing was written. Merge them into one edit, or choose finds that do not overlap.",
            "match_count_mismatch" =>
                $"find occurs {data?["actualCount"] ?? "a different number of"} time(s), not {data?["expectedCount"] ?? "as expected"}. Nothing was written. Read the document again and choose a find that occurs exactly as often as you mean to replace; do not retry with the old text.",
            "normalization_unclassified" =>
                $"Nothing was written: {Brand.Name} cannot show that its visual editor keeps this text's exact formatting. If formatting changes on the person's first visual edit are acceptable, repeat the edit with allowFormattingChanges: true (reading the document first if you are unsure it is still at baseRevision).",
            "content_not_roundtrippable" =>
                $"Nothing was written: the visual editor would lose part of this text ({string.Join(", ", (data?["reasons"] as JArray)?.Select(r => (string?)r) ?? Array.Empty<string>())}), usually raw HTML. Write that part differently, or ask the person.",
            "editor_not_ready" or "content_sync_timeout" =>
                $"{Brand.Name}'s editor did not confirm in time, or reloaded; the document is as it was before this call. Read the document again, then retry.",
            "editor_inconsistent" =>
                "The document refuses writes until the person reopens it. Tell the person; do not keep trying.",
            "presentation_timeout" =>
                $"The change was applied (revision {data?["revision"]}); only showing it on screen was not confirmed. Do not repeat the edit.",
            "document_not_found" => "That document is closed or the id is wrong. Call typedown_list_documents.",
            "window_not_found" => "That window is closed. Call typedown_list_documents.",
            "path_required" => "The document is untitled; only the person can choose where to save it.",
            "unsaved_changes" => "The document has unsaved changes, so it was not closed and nothing was lost. Save it first with typedown_save_document (or ask the person), then close it.",
            "save_failed" when (string?)data?["reason"] == "encoding_not_exact" =>
                $"Not saved: {Brand.Name} could not read this file's encoding exactly, and saving would replace the characters shown as \uFFFD for good. Any edit stays in the document, unsaved. Do not retry; tell the person - only they can choose to save it (File > Save asks them) or reopen it in another encoding.",
            "save_failed" when (string?)data?["reason"] == "changed_on_disk" =>
                $"Not saved: another program changed this file since {Brand.Name} read it, and saving would write over that version. Any edit stays in the document, unsaved. Do not retry; tell the person - {Brand.Name} asks them in its window which version to keep (neither is lost).",
            "save_failed" => "Saving failed; the edit stays in the document, unsaved. Tell the person.",
            "scope_required" => $"This {Brand.Name} does not allow that operation for automation clients.",
            "busy" => $"{Brand.Name} is busy with this document. Wait a moment, read the document again, then retry.",
            "invalid_params" => "Fix the arguments as the message says.",
            _ => "Read the document again before trying anything else.",
        };

        private sealed class RpcError : Exception
        {
            public RpcError(int code, string message) : base(message) => Code = code;
            public int Code { get; }
        }
    }
}
