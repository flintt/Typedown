using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Typedown.Mcp;
using Xunit;

namespace Typedown.Automation.Tests
{
    /// <summary>The MCP server (typedownctl mcp) against a real pipe server and the fake host, message by message.</summary>
    public class McpTests : IAsyncLifetime
    {
        private readonly string endpoint = "td-mcp-" + Guid.NewGuid().ToString("N");
        private readonly FakeHost host = new();
        private readonly FakeViewHost view = new();
        private AutomationServer server = null!;
        private FakeDocument doc = null!;
        private McpServer mcp = null!;
        private int id;

        public Task InitializeAsync()
        {
            var info = new ServerInfo { Version = "1.2.30", Platform = "test" };
            server = new AutomationServer(() => new PlainPipeListener(endpoint),
                () => new AutomationSession(info, ViewMethods.AddTo(DocumentMethods.AddTo(new MethodTable(BuildTypes.Application), host, info.InstanceId), view)), info.MaxMessageBytes);
            doc = host.Add("# Title\n\nold text\n", "/tmp/a.md");
            server.Start();
            mcp = New(endpoint);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => server.StopAsync();

        private static McpServer New(string endpoint) =>
            new(TextReader.Null, TextWriter.Null, name => new TypedownConnection(endpoint, "5c05cf79-35ef-4ff5-9dce-cf3fcb63b42c", name, Cli.Cli.ConnectPipeAsync));

        private async Task<JObject> Send(string method, JObject? parameters = null, McpServer? server = null) =>
            (await (server ?? mcp).HandleAsync(new JObject { ["jsonrpc"] = "2.0", ["id"] = ++id, ["method"] = method, ["params"] = parameters ?? new JObject() }, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(20)))!;

        private async Task<JObject> Tool(string name, JObject arguments, McpServer? server = null) =>
            (JObject)(await Send("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments }, server))["result"]!;

        [Fact]
        public async Task Initialize_negotiates_the_version_and_lists_the_tools()
        {
            var init = await Send("initialize", new JObject { ["protocolVersion"] = "2025-03-26", ["capabilities"] = new JObject(), ["clientInfo"] = new JObject { ["name"] = "Agent‮", ["version"] = "1" } });
            Assert.Equal("2025-03-26", (string)init["result"]!["protocolVersion"]!);
            Assert.NotNull(init["result"]!["capabilities"]!["tools"]);
            Assert.Equal("Agent (MCP)", mcp.ClientName);
            var unknownVersion = await Send("initialize", new JObject { ["protocolVersion"] = "1999-01-01" }, New(endpoint));
            Assert.Equal(McpServer.ProtocolVersions[0], (string)unknownVersion["result"]!["protocolVersion"]!);

            var tools = (JArray)(await Send("tools/list"))["result"]!["tools"]!;
            Assert.Equal(new[] { "typedown_list_documents", "typedown_read_document", "typedown_replace_text", "typedown_edit_text", "typedown_replace_document", "typedown_save_document",
                "typedown_close_document", "typedown_get_view", "typedown_set_view" },
                tools.Select(t => (string)t["name"]!));
            foreach (var t in tools)
            {
                Assert.Equal("object", (string)t["inputSchema"]!["type"]!);
                foreach (var required in (JArray)t["inputSchema"]!["required"]!)
                    Assert.NotNull(t["inputSchema"]!["properties"]![(string)required!]);
            }
            Assert.Empty((JObject)(await Send("ping"))["result"]!);
            Assert.Equal(-32601, (int)(await Send("resources/list"))["error"]!["code"]!);
            Assert.Null(await mcp.HandleAsync(new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }, CancellationToken.None));
        }

        [Fact]
        public async Task Read_then_replace_text_then_save()
        {
            var list = await Tool("typedown_list_documents", new JObject());
            Assert.False((bool)list["isError"]!);
            Assert.Equal(doc.DocumentId, (string)list["structuredContent"]!["documents"]![0]!["documentId"]!);

            var read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId, ["includeHeadings"] = true });
            Assert.Equal("# Title\n\nold text\n", (string)read["structuredContent"]!["text"]!);
            Assert.Equal("Title", (string)read["structuredContent"]!["headings"]![0]!["text"]!);
            var revision = (long)read["structuredContent"]!["revision"]!;

            var write = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = revision, ["find"] = "old", ["replacement"] = "new", ["save"] = true });
            Assert.False((bool)write["isError"]!);
            Assert.Equal(revision + 1, (long)write["structuredContent"]!["revision"]!);
            Assert.True((bool)write["structuredContent"]!["saved"]!);
            Assert.Equal("# Title\n\nnew text\n", doc.Text);
            // The text content carries the same result for clients that ignore structuredContent.
            Assert.Equal(revision + 1, (long)JObject.Parse((string)write["content"]![0]!["text"]!)["revision"]!);

            var whole = await Tool("typedown_replace_document", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = revision + 1, ["text"] = "# Whole\n" });
            Assert.False((bool)whole["isError"]!);
            Assert.Equal("# Whole\n", doc.Text);
            var saved = await Tool("typedown_save_document", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = revision + 2 });
            Assert.False((bool)saved["isError"]!);
        }

        [Fact]
        public async Task ScrollToChange_reveals_the_change()
        {
            var write = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["find"] = "old", ["replacement"] = "new", ["scrollToChange"] = true });
            Assert.False((bool)write["isError"]!);
            Assert.True(doc.LastApply!.ScrollToChange);
            Assert.Equal(Reveal.Document, host.Reveals[^1]);
            write = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 1, ["find"] = "new", ["replacement"] = "old", ["reveal"] = true });
            Assert.False((bool)write["isError"]!);
            Assert.False(doc.LastApply!.ScrollToChange);
            write = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 2, ["find"] = "old", ["replacement"] = "new" });
            Assert.False((bool)write["isError"]!);
            Assert.False(doc.LastApply!.ScrollToChange);
            Assert.Equal(Reveal.None, host.Reveals[^1]);
        }

        [Fact]
        public async Task The_view_tools_read_and_change_the_window()
        {
            var got = await Tool("typedown_get_view", new JObject { ["windowId"] = FakeHost.WindowId });
            Assert.Equal("visual", (string)got["structuredContent"]!["mode"]!);
            var set = await Tool("typedown_set_view", new JObject { ["windowId"] = FakeHost.WindowId, ["mode"] = "source", ["sidePane"] = new JObject { ["open"] = true, ["page"] = "outline" } });
            Assert.False((bool)set["isError"]!);
            Assert.Equal("source", (string)set["structuredContent"]!["mode"]!);
            Assert.Equal("outline", (string)set["structuredContent"]!["sidePane"]!["page"]!);
            var bad = await Tool("typedown_set_view", new JObject { ["windowId"] = FakeHost.WindowId, ["bounds"] = new JObject { ["width"] = 100 } });
            Assert.True((bool)bad["isError"]!);
            Assert.Equal("invalid_params", (string)bad["structuredContent"]!["error"]!["data"]!["kind"]!);
        }

        [Fact]
        public async Task A_conflict_is_the_same_error_typedownctl_reports_and_says_to_read_again()
        {
            await Tool("typedown_replace_document", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["text"] = "# moved on\n" });
            var stale = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["find"] = "moved", ["replacement"] = "x" });
            Assert.True((bool)stale["isError"]!);
            var error = (JObject)stale["structuredContent"]!["error"]!;
            Assert.Equal("revision_conflict", (string)error["data"]!["kind"]!);
            Assert.Contains("typedown_read_document", (string)stale["structuredContent"]!["next"]!);

            var output = new StringWriter();
            var exit = await new Cli.Cli(TextReader.Null, output, TextWriter.Null).RunAsync(new[] { "--json", "--endpoint", endpoint, "--client-id", "5c05cf79-35ef-4ff5-9dce-cf3fcb63b42c",
                "replace-text", doc.DocumentId, "--base-revision", "0", "--find", "moved", "--replacement", "x", "--expected-count", "1" });
            Assert.Equal(Cli.Cli.Conflict, exit);
            Assert.True(JToken.DeepEquals(JObject.Parse(output.ToString())["error"], error), $"CLI {output} vs MCP {error}");
        }

        [Fact]
        public async Task A_match_count_mismatch_writes_nothing_and_is_not_retried()
        {
            var calls = doc.Calls.Count;
            var result = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["find"] = "t", ["replacement"] = "T" });
            Assert.True((bool)result["isError"]!);
            Assert.Equal("match_count_mismatch", (string)result["structuredContent"]!["error"]!["data"]!["kind"]!);
            Assert.Contains("occurs 3 time(s), not 1", (string)result["structuredContent"]!["next"]!);
            Assert.Equal("# Title\n\nold text\n", doc.Text);
            Assert.Equal(0, doc.Revision);
            Assert.Equal(1, doc.Calls.Skip(calls).Count(c => c == "flush")); // one attempt, no second one
            Assert.DoesNotContain("apply", doc.Calls.Skip(calls));
        }

        [Fact]
        public async Task Bad_arguments_and_a_missing_typedown_are_tool_errors()
        {
            var missing = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["find"] = "old", ["replacement"] = "new" });
            Assert.True((bool)missing["isError"]!);
            Assert.Equal("baseRevision", (string)missing["structuredContent"]!["error"]!["data"]!["field"]!);

            var away = New("td-mcp-nobody-" + Guid.NewGuid().ToString("N"));
            var result = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId }, away);
            Assert.True((bool)result["isError"]!);
            Assert.Equal("not_connected", (string)result["structuredContent"]!["error"]!["data"]!["kind"]!);
            Assert.Contains("Allow local automation", (string)result["structuredContent"]!["next"]!);
        }

        // An agent moving a passage from one chapter to another made two writes (two revisions, two reviews) or sent the
        // whole 20,000-character document (the agent's feedback, 2026-11-12).
        [Fact]
        public async Task Several_edits_are_one_revision_or_nothing()
        {
            await Tool("typedown_replace_document", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["text"] = "# One\n\nmove me\n\n# Two\n\nend\n" });
            var edits = new JArray(
                new JObject { ["find"] = "\nmove me\n", ["replacement"] = "\n" },
                new JObject { ["find"] = "\nend\n", ["replacement"] = "\nend\n\nmove me\n" });
            var write = await Tool("typedown_edit_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 1, ["edits"] = edits });
            Assert.False((bool)write["isError"]!, write.ToString());
            Assert.Equal(2, (long)write["structuredContent"]!["revision"]!);
            Assert.Equal("# One\n\n\n# Two\n\nend\n\nmove me\n", doc.Text);

            // One edit that does not fit: none is made, and the error names it.
            var bad = await Tool("typedown_edit_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 2, ["edits"] = new JArray(
                new JObject { ["find"] = "# One", ["replacement"] = "# First" },
                new JObject { ["find"] = "absent", ["replacement"] = "x" }) });
            Assert.True((bool)bad["isError"]!);
            Assert.Equal("match_count_mismatch", (string)bad["structuredContent"]!["error"]!["data"]!["kind"]!);
            Assert.Equal(1, (int)bad["structuredContent"]!["error"]!["data"]!["editIndex"]!);
            Assert.Contains("none of the edits", (string)bad["structuredContent"]!["next"]!);
            Assert.Equal(2, doc.Revision);
            Assert.Contains("# One", doc.Text);

            var overlap = await Tool("typedown_edit_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 2, ["edits"] = new JArray(
                new JObject { ["find"] = "# Two", ["replacement"] = "# 2" },
                new JObject { ["find"] = "Two\n\nend", ["replacement"] = "x" }) });
            Assert.Equal("edits_overlap", (string)overlap["structuredContent"]!["error"]!["data"]!["kind"]!);
            Assert.Equal(2, doc.Revision);

            var stale = await Tool("typedown_edit_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 1, ["edits"] = new JArray(new JObject { ["find"] = "# One", ["replacement"] = "# First" }) });
            Assert.Equal("revision_conflict", (string)stale["structuredContent"]!["error"]!["data"]!["kind"]!);
            Assert.Equal(2, doc.Revision);
        }

        // Read only a part of a long document: a heading's section, or lines; the revision is still the whole one's.
        [Fact]
        public async Task A_part_of_a_document_by_heading_or_lines()
        {
            await Tool("typedown_replace_document", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0,
                ["text"] = "# Book\n\n## Chapter 5\n\nfive\n\n```md\n## not a heading\n```\n\n### Note\n\nnote\n\n## Chapter 7\n\nseven\n" });
            var section = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId, ["heading"] = "chapter 5" });
            Assert.False((bool)section["isError"]!, section.ToString());
            Assert.Equal("## Chapter 5\n\nfive\n\n```md\n## not a heading\n```\n\n### Note\n\nnote\n\n", (string)section["structuredContent"]!["text"]!);
            Assert.Equal(3, (int)section["structuredContent"]!["range"]!["startLine"]!);
            Assert.Equal(14, (int)section["structuredContent"]!["range"]!["endLine"]!);
            Assert.Equal(17, (int)section["structuredContent"]!["range"]!["totalLines"]!);
            Assert.Equal(1, (long)section["structuredContent"]!["revision"]!);

            var lines = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId, ["startLine"] = 15, ["endLine"] = 99 });
            Assert.Equal("## Chapter 7\n\nseven\n", (string)lines["structuredContent"]!["text"]!);
            Assert.Equal(17, (int)lines["structuredContent"]!["range"]!["endLine"]!);

            var missing = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId, ["heading"] = "Chapter 9" });
            Assert.True((bool)missing["isError"]!);
            Assert.Contains("## Chapter 7 (line 15)", (string)missing["structuredContent"]!["error"]!["message"]!);
        }

        // An agent whose first edits were rejected in review found out only by reading the whole text back and comparing
        // (the agent's feedback, 2026-11-12): a read says what became of this session's last edit.
        [Fact]
        public async Task A_read_says_whether_this_sessions_last_edit_is_still_there()
        {
            await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["find"] = "old text", ["replacement"] = "brand new words" });
            var read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            Assert.Equal("unchanged", (string)read["structuredContent"]!["lastEditThisSession"]!["status"]!);

            // Someone else (the person rejecting it, here another session) puts the text back.
            var other = New(endpoint);
            await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 1, ["find"] = "brand new words", ["replacement"] = "old text" }, other);
            read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            Assert.Equal("gone", (string)read["structuredContent"]!["lastEditThisSession"]!["status"]!);
            Assert.Contains("rejected it in review", (string)read["structuredContent"]!["lastEditThisSession"]!["note"]!);
            // The other session never wrote to it before its own edit: its read reports its own edit.
            var theirs = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId }, other);
            Assert.Equal("unchanged", (string)theirs["structuredContent"]!["lastEditThisSession"]!["status"]!);
        }

        // Review mode's own verdict on the last edit, by its operationId (document.reviewStatus), when the document is in
        // review: the agent learns the person rejected it, not only that its text is gone.
        [Fact]
        public async Task A_read_in_review_says_whether_the_person_accepted_or_rejected_the_last_edit()
        {
            var wrote = await Tool("typedown_replace_text", new JObject { ["documentId"] = doc.DocumentId, ["baseRevision"] = 0, ["find"] = "old text", ["replacement"] = "brand new words" });
            var op = (string)wrote["structuredContent"]!["operationId"]!;
            var read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            Assert.Null(read["structuredContent"]!["lastEditThisSession"]!["review"]);

            host.Review = new JObject { ["on"] = true, ["revisions"] = new JArray(new JObject { ["id"] = "r1", ["op"] = op }), ["outcomes"] = new JArray() };
            read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            Assert.Equal("pending", (string)read["structuredContent"]!["lastEditThisSession"]!["review"]!["status"]!);

            host.Review = new JObject { ["on"] = true, ["revisions"] = new JArray(), ["outcomes"] = new JArray(new JObject { ["id"] = "r1", ["op"] = op, ["outcome"] = "rejected" }) };
            read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            var review = read["structuredContent"]!["lastEditThisSession"]!["review"]!;
            Assert.Equal("rejected", (string)review["status"]!);
            Assert.Contains("rejected your last edit", (string)review["note"]!);
        }

        [Fact]
        public async Task A_read_explains_normalization_in_words()
        {
            var read = await Tool("typedown_read_document", new JObject { ["documentId"] = doc.DocumentId });
            var pending = (string?)read["structuredContent"]!["normalization"]?["pendingNormalization"];
            if (pending is "none" or "knownFormatting" or "unknown" or "unsafe")
                Assert.False(string.IsNullOrEmpty((string?)read["structuredContent"]!["normalizationNote"]));
            var tools = (JArray)(await Send("tools/list"))["result"]!["tools"]!;
            var allow = (string)tools.First(t => (string)t["name"]! == "typedown_replace_text")["inputSchema"]!["properties"]!["allowFormattingChanges"]!["description"]!;
            Assert.Contains("formatting-changed", allow);
            Assert.Contains("formatting-changed", McpServer.Instructions);
        }

        [Fact]
        public async Task Runs_over_stdio_one_message_per_line()
        {
            var input = new StringReader(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"clientInfo\":{\"name\":\"t\"}}}\n" +
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n" +
                "not json\n" +
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"typedown_read_document\",\"arguments\":{\"documentId\":\"" + doc.DocumentId + "\"}}}\n");
            var output = new StringWriter();
            await new McpServer(input, output, name => new TypedownConnection(endpoint, "5c05cf79-35ef-4ff5-9dce-cf3fcb63b42c", name, Cli.Cli.ConnectPipeAsync))
                .RunAsync().WaitAsync(TimeSpan.FromSeconds(20));
            var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToList();
            Assert.Equal(3, lines.Count);
            Assert.Equal(1, (int)lines[0]["id"]!);
            Assert.Equal(-32700, (int)lines[1]["error"]!["code"]!);
            Assert.Equal("# Title\n\nold text\n", (string)lines[2]["result"]!["structuredContent"]!["text"]!);
        }
    }
}
