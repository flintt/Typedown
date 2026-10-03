using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Typedown.Automation;

// End-to-end cases against a running automation test host. Every case checks the API's answer, the revision the
// window holds and the bytes on disk; interleavings are forced with test.barrier.* (arm -> wait until hit -> act ->
// release), never with sleeps; a person's keystroke is real input (SendInput) into the window.
//
//   Typedown.AutomationE2E --root <test data root> --pid <host process id> --fixtures <dir> --out <result.json>

internal static class Program
{
    private sealed class NoHandler : IJsonRpcHandler
    {
        public Task<JToken?> HandleRequestAsync(JsonRpcRequest r, CancellationToken ct) => throw new AutomationException(AutomationErrorKind.method_not_found, "no");
        public Task HandleNotificationAsync(JsonRpcRequest n, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Client : IDisposable
    {
        private readonly NamedPipeClientStream pipe;
        public readonly JsonRpcConnection Connection;
        private readonly Task running;

        public Client(string endpoint)
        {
            pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(1000);
            Connection = new JsonRpcConnection(new MessageFraming(pipe, 64L << 20), new NoHandler());
            running = Connection.RunAsync();
        }

        public async Task<JToken> Call(string method, object? parameters = null)
        {
            var p = parameters == null ? null : JToken.FromObject(parameters);
            return (await Connection.SendRequestAsync(method, p).WaitAsync(TimeSpan.FromSeconds(60)))!;
        }

        public async Task<int> ErrorCode(string method, object? parameters = null)
        {
            try { await Call(method, parameters); return 0; }
            catch (JsonRpcRemoteException e) { return e.Code; }
        }

        public async Task<string?> ErrorKind(string method, object? parameters = null)
        {
            try { await Call(method, parameters); return null; }
            catch (JsonRpcRemoteException e) { return (string?)e.ErrorData?["kind"]; }
        }

        public void Dispose() { Connection.Dispose(); pipe.Dispose(); }
    }

    private static readonly List<JObject> results = new();
    private static string outputDir = ".";
    private static string[]? only;

    /// <summary>A picture of the screen next to result.json, for a failure that depends on what is shown.</summary>
    private static string Screenshot(string name)
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            var path = Path.Combine(outputDir, name + ".png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch (Exception e) { return "no screenshot: " + e.Message; }
    }
    private static string hostExe = "";
    private static string testRoot = "";
    private static string endpoint = "";
    private static int hostPid;
    private static string fixtures = "";

    private static async Task<int> Main(string[] args)
    {
        string Arg(string name) => args[Array.IndexOf(args, name) + 1];
        var root = Arg("--root");
        hostPid = int.Parse(Arg("--pid"));
        hostExe = Arg("--exe");
        testRoot = root;
        fixtures = Arg("--fixtures");
        var output = Arg("--out");
        outputDir = Path.GetDirectoryName(Path.GetFullPath(output))!;
        only = args.Contains("--only") ? Arg("--only").Split(',') : null;
        var started = DateTime.UtcNow;
        var environmentError = (string?)null;
        try
        {
            var endpointFile = Path.Combine(root, "automation-endpoint.txt");
            endpoint = await ReadEndpointFile(endpointFile) ?? throw new InvalidOperationException("the test host never published its endpoint");

            using (var probe = await Connect())
            {
                var init = await Initialize(probe, "e2e-probe");
                if ((string?)init["server"]?["buildType"] != BuildTypes.AutomationTestHost)
                    throw new InvalidOperationException($"connected to a '{init["server"]?["buildType"]}' build, not the automation test host");
                await WaitForWindow(probe);
            }

            await Case("S0 open, write, read back exactly, save bytes, stale write refused", S0);
            await Case("S1 a real keystroke is an edit of the reader: revision advances, the text is in", S1);
            await Case("R01 a write held after its flush while the reader switches tabs never writes the other document", R01);
            await Case("R02 undo and redo stay in their own document", R02);
            await Case("R03 a keystroke while a saving write is held: the saved file is the written revision, the keystroke is kept", R03);
            await Case("B01 settings: one window's change reaches the other window; window modes stay with their window", B01);
            await Case("S2 settings.set reaches every window and the settings file; a stale revision is refused", S2);
            await Case("S3 the newer settings: language, tab size, compact mode and word count reach the window and go back", S3);
            await Case("V01 window.setView: source mode with the outline in a restored 1100x720 window, then reading; a mode switch waits for a held write", V01);
            await Case("N01 back from the settings page (the editor page loads again), a file opened at once is read and written without a timeout", N01);
            await Case("K01 a font size changed through the API while the reader types: the keys still reach the page", K01);
            await Case("B02 an open settings page shows a font size changed from elsewhere", B02);
            await Case("F01 a written document with protected payloads keeps every one of them through a real first keystroke", F01);
            await Case("M01 a written text survives visual, reading and source mode switches byte for byte, then saves exactly", M01);
            await Case("P01 awaitPresentation: a revealed write in a background tab of a minimized window reports it drawn; it needs reveal", P01);
            await Case("W01 the editor page reloads after it applied a write and before the commit: nothing is committed, host and page agree", W01);
            await Case("W02 a mode switch while a write is held after the page applied it: the write commits and every mode shows it", W02);
            await Case("W03 the tab switched away and back while a write is held: refused and restored; switched away only: committed into the tab", W03);
            await Case("MC01 typedownctl mcp as its own process: read, replace text with reveal, a stale revision is a conflict that says to read again", MC01);
            await Case("EX01 the PowerShell and Python client examples edit through the real pipe", EX01);
            await Case("EQ01 the equivalence scenario (compared with the Uno edition's answers offline)", EQ01);
            await Case("R04 untitled and background documents come back from their backups after a kill, each its own", R04);
            await Case("C01 document.close: a saved document closes, an unsaved one (a fresh keystroke too) is refused, the last one leaves an empty document", C01);
            await Case("Q02 a window closed at once after it opened (its web view still being created): the process lives on", Q02);
            await Case("D01 the window's UI thread runs every callback posted to it, from many threads at once", D01);
            await Case("K02 Ctrl+, twice opens and closes the settings: the caret and the keyboard are where they were", K02);
            await Case("K03 the same with an untitled document (no per-file caret memory)", K03);
            await Case("FS01 in full screen the main page starts at the top edge of the screen", FS01);
            await Case("RV01 reveal: \"change\" scrolls a change off screen into view, the caret where it was; \"document\" leaves the page", RV01);
            // Last: it ends the test host.
            await Case("Q01 two windows closed one after the other: the process exits (it stayed, headless)", Q01);
        }
        catch (Exception e)
        {
            environmentError = e.Message;
        }
        var failed = results.Count(r => !(bool)r["passed"]!);
        var summary = new JObject
        {
            ["started"] = started.ToString("O"),
            ["seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds, 1),
            ["environmentError"] = environmentError,
            ["passed"] = results.Count(r => (bool)r["passed"]!),
            ["failed"] = failed,
            ["cases"] = new JArray(results),
        };
        File.WriteAllText(output, summary.ToString(Formatting.Indented));
        Console.WriteLine(summary.ToString(Formatting.Indented));
        return environmentError != null ? 3 : failed > 0 ? 1 : 0;
    }

    /// <summary>Connects, retrying while the endpoint is not there yet (a host that has just started), up to ~30 s.</summary>
    private static async Task<Client> Connect()
    {
        for (var attempt = 0; ; attempt++)
        {
            await Task.Yield();
            try { return new Client(endpoint); }
            catch (TimeoutException) when (attempt < 30) { }
        }
    }

    private static readonly string[] AllScopes = { Scopes.AppRead, Scopes.DocumentRead, Scopes.DocumentWrite, Scopes.DocumentSave, Scopes.WindowFocus, Scopes.SettingsRead, Scopes.SettingsWrite, Scopes.WindowView };

    private static Task<JToken> Initialize(Client c, string name) => c.Call("system.initialize", new
    {
        apiVersion = 1,
        client = new { id = "0f0f0f0f-0000-4000-8000-00000000e2e0", name, version = "1" },
        requestedScopes = AllScopes,
    });

    private static async Task<Client> Session(string name)
    {
        var c = await Connect();
        await Initialize(c, name);
        return c;
    }

    private static async Task WaitForWindow(Client c)
    {
        for (var i = 0; i < 300; i++)
        {
            var windows = (JArray)(await c.Call("window.list"))["windows"]!;
            if (windows.Count > 0) return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("the test host opened no window");
    }

    private sealed class CaseFailed : Exception { public CaseFailed(string m) : base(m) { } }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new CaseFailed(what);
    }

    private static async Task Case(string name, Func<List<string>, Task> body)
    {
        if (only != null && !only.Any(o => name.StartsWith(o + " "))) return;
        var notes = new List<string>();
        var watch = Stopwatch.StartNew();
        try
        {
            // Every case is bounded: a hang is a failure with what it got to, never a run that never ends.
            var run = body(notes);
            if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(120))) != run) throw new CaseFailed("the case did not finish within 120 s");
            await run;
            results.Add(new JObject { ["name"] = name, ["passed"] = true, ["ms"] = watch.ElapsedMilliseconds, ["notes"] = new JArray(notes) });
        }
        catch (Exception e)
        {
            // A call that got no answer: what the screen showed (a dialog holding the window, say) is the first clue.
            if (e is TimeoutException || e is CaseFailed && e.Message.Contains("did not finish"))
            {
                notes.Add("screen at the timeout: " + Screenshot(name.Split(' ')[0] + "-timeout-" + DateTime.Now.ToString("HHmmss")));
                // And the host's threads at that moment: a window's UI thread stuck shows nothing in the log.
                notes.Add("host dump at the timeout: " + HangDump(name.Split(' ')[0] + "-timeout-" + DateTime.Now.ToString("HHmmss")));
            }
            results.Add(new JObject { ["name"] = name, ["passed"] = false, ["ms"] = watch.ElapsedMilliseconds, ["error"] = e is CaseFailed ? e.Message : e.ToString(), ["notes"] = new JArray(notes) });
        }
    }

    // ---- helpers over the API ----

    private static string Fixture(string name, string text)
    {
        var path = Path.Combine(fixtures, name);
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        return path;
    }

    private static string Disk(string path) => new UTF8Encoding(false).GetString(File.ReadAllBytes(path));

    private static async Task<string> Open(Client c, string path) => (string)(await c.Call("document.open", new { path, reveal = "document" }))["documentId"]!;

    private static async Task<JToken> Get(Client c, string id) => await c.Call("document.get", new { documentId = id, consistency = "latest", include = new[] { "text" } });

    private static async Task<long> Revision(Client c, string id) => (long)(await c.Call("document.get", new { documentId = id, consistency = "latest" }))["revision"]!;

    // ---- cases ----

    private static async Task S0(List<string> notes)
    {
        using var c = await Session("e2e S0");
        var path = Fixture("s0.md", "# S0\n\nalpha\n");
        var id = await Open(c, path);
        var doc = await Get(c, id);
        Check((string)doc["text"]! == "# S0\n\nalpha\n", "the opened text is the file's text");
        var r = (long)doc["revision"]!;
        var written = await c.Call("document.replace", new { documentId = id, baseRevision = r, text = "# S0\n\nalpha written\n", reveal = "document", normalizationPolicy = "allowUnknown" });
        Check((long)written["revision"]! == r + 1, $"the write advanced the revision ({written["revision"]} after {r})");
        notes.Add($"normalization {written["normalization"]?["pendingNormalization"]}");
        Check((string)(await Get(c, id))["text"]! == "# S0\n\nalpha written\n", "latest read returns exactly what was written");
        Check(await c.ErrorCode("document.replace", new { documentId = id, baseRevision = r, text = "stale\n" }) == -32012, "a stale base revision is a revision_conflict");
        await c.Call("document.save", new { documentId = id, baseRevision = r + 1 });
        Check(Disk(path) == "# S0\n\nalpha written\n", "the file holds exactly the saved revision");
        // Saving goes through a hidden, temporary-marked temp file renamed over the target: the target must not keep
        // those marks (1.2.27-1.2.30 left every saved file hidden, and sync clients skip such files).
        var attributes = File.GetAttributes(path);
        Check((attributes & (FileAttributes.Hidden | FileAttributes.Temporary)) == 0, $"the saved file is neither hidden nor temporary ({attributes})");
        Check(WindowTitle().Contains("AUTOMATION TEST HOST"), $"the title marks the test host ({WindowTitle()})");
    }

    private static async Task S1(List<string> notes)
    {
        using var c = await Session("e2e S1");
        var id = await Open(c, Fixture("s1.md", "# S1\n\nbefore\n"));
        await c.Call("document.focus", new { documentId = id });
        var before = await Get(c, id);
        await TypeInto(c, id, "Q");
        await WaitForPage(c, id, t => t.Contains('Q'), "the keystroke");
        var after = await Get(c, id);
        var text = (string)after["text"]!;
        Check(text.Contains('Q') && text.StartsWith("# S1\n\nbefore"), $"the keystroke is in the text, the rest intact ({JsonConvert.SerializeObject(text)})");
        Check(!(bool)after["saved"]!, "the keystroke leaves the document unsaved");
        Check((long)after["revision"]! > (long)before["revision"]!, "the keystroke advanced the revision");
    }

    private static async Task R01(List<string> notes)
    {
        using var writer = await Session("e2e R01 writer");
        using var driver = await Session("e2e R01 driver");
        var pathA = Fixture("r01-a.md", "# A\n\nalpha\n");
        var pathB = Fixture("r01-b.md", "# B\n\nbeta\n");
        var b = await Open(driver, pathB);
        var a = await Open(driver, pathA);
        var rA = await Revision(driver, a);
        var barrier = (string)(await driver.Call("test.barrier.arm", new { point = EditBarrierPoints.BeforeFlushReply, documentId = a }))["barrierId"]!;
        var write = writer.Call("document.replace", new { documentId = a, baseRevision = rA, text = "# A\n\nalpha by API\n", save = true, normalizationPolicy = "allowUnknown" });
        var hit = await driver.Call("test.barrier.waitHit", new { barrierId = barrier, timeoutMs = 20000 });
        Check((bool)hit["hit"]!, "the write reached the barrier after its flush");
        await driver.Call("document.focus", new { documentId = b }); // the reader switches to B meanwhile
        await driver.Call("test.barrier.release", new { barrierId = barrier });
        string outcome;
        try { var result = await write; outcome = $"committed r{result["revision"]} saved={result["saved"]}"; }
        catch (JsonRpcRemoteException e) { outcome = $"refused {e.ErrorData?["kind"]}"; }
        notes.Add(outcome);
        Check(Disk(pathB) == "# B\n\nbeta\n", "B's file is untouched");
        var diskA = Disk(pathA);
        Check(diskA == "# A\n\nalpha\n" || diskA == "# A\n\nalpha by API\n", $"A's file is either the old text or the written revision ({JsonConvert.SerializeObject(diskA)})");
        Check((string)(await Get(driver, b))["text"]! == "# B\n\nbeta\n", "B's text in the window is untouched");
    }

    private static async Task R02(List<string> notes)
    {
        using var c = await Session("e2e R02");
        var a = await Open(c, Fixture("r02-a.md", "# A\n\none\n"));
        var b = await Open(c, Fixture("r02-b.md", "# B\n\ntwo\n"));
        await c.Call("document.replace", new { documentId = a, baseRevision = await Revision(c, a), text = "# A\n\none edited\n", reveal = "document", normalizationPolicy = "allowUnknown" });
        await c.Call("document.replace", new { documentId = b, baseRevision = await Revision(c, b), text = "# B\n\ntwo edited\n", reveal = "document", normalizationPolicy = "allowUnknown" });
        await c.Call("document.undo", new { documentId = a, baseRevision = await Revision(c, a), reveal = "document" });
        Check((string)(await Get(c, a))["text"]! == "# A\n\none\n", "undo in A restores A");
        Check((string)(await Get(c, b))["text"]! == "# B\n\ntwo edited\n", "B is not touched by A's undo");
        await c.Call("document.undo", new { documentId = b, baseRevision = await Revision(c, b), reveal = "document" });
        Check((string)(await Get(c, b))["text"]! == "# B\n\ntwo\n", "undo in B restores B");
        await c.Call("document.redo", new { documentId = a, baseRevision = await Revision(c, a), reveal = "document" });
        Check((string)(await Get(c, a))["text"]! == "# A\n\none edited\n", "redo in A brings A's edit back");
        Check((string)(await Get(c, b))["text"]! == "# B\n\ntwo\n", "B is not touched by A's redo");
    }

    private static async Task R03(List<string> notes)
    {
        using var writer = await Session("e2e R03 writer");
        using var driver = await Session("e2e R03 driver");
        var path = Fixture("r03.md", "# R03\n\nstart\n");
        var id = await Open(driver, path);
        await driver.Call("document.focus", new { documentId = id });
        var r = await Revision(driver, id);
        var barrier = (string)(await driver.Call("test.barrier.arm", new { point = EditBarrierPoints.AfterEditorMutationBeforeReport, documentId = id }))["barrierId"]!;
        var write = writer.Call("document.replace", new { documentId = id, baseRevision = r, text = "# R03\n\nwritten\n", save = true, normalizationPolicy = "allowUnknown" });
        Check((bool)(await driver.Call("test.barrier.waitHit", new { barrierId = barrier, timeoutMs = 20000 }))["hit"]!, "the write reached the barrier with the page already changed");
        await TypeIntoWindow(driver, (string)(await driver.Call("document.get", new { documentId = id, consistency = "snapshot" }))["windowId"]!, "Z"); // the reader's keystroke on top of the write, while the host has not committed it
        await WaitForPage(driver, id, t => t.Contains('Z'), "the keystroke");
        await driver.Call("test.barrier.release", new { barrierId = barrier });
        var result = await write;
        var saved = Disk(path);
        notes.Add($"write r{result["revision"]} saved={result["saved"]}");
        Check(saved == "# R03\n\nwritten\n", $"the file holds exactly the written revision ({JsonConvert.SerializeObject(saved)})");
        var now = await Get(driver, id);
        Check(((string)now["text"]!).Contains('Z'), "the keystroke is not lost");
        Check((long)now["revision"]! > (long)result["revision"]!, "the keystroke is a later revision than the write");
        Check(!(bool)now["saved"]!, "the keystroke leaves the document unsaved");
    }

    /// <summary>
    /// settings.set as a client should do it: against the revision just read, again after a conflict. The revision also
    /// advances when the app stores a window's placement (a moved or resized window), so a conflict can come from that.
    /// </summary>
    private static async Task<long> SetSetting(Client c, string key, JToken value)
    {
        for (var attempt = 0; ; attempt++)
        {
            var revision = (long)(await c.Call("settings.get", new { keys = new[] { key } }))["settingsRevision"]!;
            try { return (long)(await c.Call("settings.set", new { key, value, baseSettingsRevision = revision }))["settingsRevision"]!; }
            catch (JsonRpcRemoteException e) when ((string?)e.ErrorData?["kind"] == "revision_conflict" && attempt < 3) { }
        }
    }

    private static async Task S3(List<string> notes)
    {
        using var c = await Session("e2e S3");
        var windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;
        var keys = new[] { "ui.language", "editor.tabSize", "appearance.compactMode", "status.wordCount" };
        var got = await c.Call("settings.get", new { keys });
        var original = (JObject)got["values"]!;
        var revision = (long)got["settingsRevision"]!;
        async Task<JToken> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        async Task Set(string key, JToken value) => revision = await SetSetting(c, key, value);
        async Task<string> Menu() => string.Join(" ", ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Select(t => (string)t!));
        string? menuBefore = null;
        try
        {
            var language = (string)original["ui.language"]! == "ja" ? "en" : "ja";
            menuBefore = await Menu();
            await Set("ui.language", language);
            Check((string)(await Setting("Language"))! == language, $"the window's language is {language}");
            // The menu bar follows without a restart (it kept the old language when the change came through the API).
            string menuAfter = menuBefore;
            for (var i = 0; i < 40 && (menuAfter == menuBefore || menuAfter == ""); i++) { await Task.Delay(250); try { menuAfter = await Menu(); } catch (JsonRpcRemoteException) { } }
            notes.Add($"menu: {menuBefore} -> {menuAfter}");
            Check(menuAfter != menuBefore && menuAfter != "", $"the menu bar follows the language ({menuBefore} -> {menuAfter})");
            // Menus made again in source mode still follow it: the language is changed back while the window is in source
            // mode, and Paragraph and Format stay hidden; back in visual mode all five are there.
            async Task<int> MenuCount() => ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Count;
            await c.Call("window.setView", new { windowId, mode = "source" });
            await Set("ui.language", original["ui.language"]!);
            var menuBack = "";
            // In source mode the bar shows three of the five titles: "back" is no longer the other language's menu.
            for (var i = 0; i < 40 && (menuBack == menuAfter || menuBack == ""); i++) { await Task.Delay(250); try { menuBack = await Menu(); } catch (JsonRpcRemoteException) { } }
            var inSource = await MenuCount();
            await c.Call("window.setView", new { windowId, mode = "visual" });
            var inVisual = 0;
            for (var i = 0; i < 20 && inVisual != 5; i++) { await Task.Delay(150); inVisual = await MenuCount(); }
            Check(menuBack != menuAfter && menuBack != "" && inSource == 3 && inVisual == 5,
                $"menus made again in source mode: {inSource} shown ({menuBack}); in visual mode {inVisual}");
            var tab = (int)original["editor.tabSize"]! == 2 ? 4 : 2;
            await Set("editor.tabSize", tab);
            Check((int)await Setting("TabSize") == tab, $"tab size {tab}");
            var compact = !(bool)original["appearance.compactMode"]!;
            await Set("appearance.compactMode", compact);
            Check((bool)await Setting("AppCompactMode") == compact, $"compact mode {compact}");
            await Set("status.wordCount", "characters");
            Check((int)await Setting("WordCountMethod") == 0, "word count by characters");
            // Valid in the catalog (the Uno edition has them), refused by this edition after the revision check.
            async Task<long> Current() => (long)(await c.Call("settings.get", new { keys = new[] { "ui.language" } }))["settingsRevision"]!;
            Check(await c.ErrorKind("settings.set", new { key = "status.wordCount", value = "paragraphs", baseSettingsRevision = await Current() }) == "setting_invalid",
                "paragraphs is not a word count this edition has");
            Check(await c.ErrorKind("settings.set", new { key = "markdown.listIndentation", value = "tab", baseSettingsRevision = await Current() }) == "setting_invalid",
                "tab is not a list indentation this edition has");
            var described = (JArray)(await c.Call("settings.describe"))["settings"]!;
            notes.Add($"{described.Count} settings described; language {language}, tab {tab}, compact {compact}");
        }
        finally
        {
            foreach (var key in keys) await Set(key, original[key]!);
            // The language put back rebuilds the main page and reloads the editor page once more: the case ends only when
            // that is done, or the next case starts on a page about to go (a write to it was refused, editor_not_ready).
            if (menuBefore != null)
            {
                var menu = "";
                for (var i = 0; i < 40 && menu != menuBefore; i++) { await Task.Delay(250); try { menu = await Menu(); } catch (JsonRpcRemoteException) { } }
                var active = (string?)((JArray)(await c.Call("window.list"))["windows"]!).First(w => (string)w["windowId"]! == windowId)["activeDocumentId"];
                if (active != null) await c.Call("document.get", new { documentId = active, consistency = "latest" });
            }
        }
        var back = (JObject)(await c.Call("settings.get", new { keys }))["values"]!;
        Check(JToken.DeepEquals(back, original), $"every setting is back ({back.ToString(Formatting.None)})");
    }

    private static async Task V01(List<string> notes)
    {
        using var c = await Session("e2e V01");
        using var writer = await Session("e2e V01 writer");
        using var viewer = await Session("e2e V01 viewer");
        const string text = "# V01\n\n## One\n\nfirst\n\n## Two\n\nsecond\n";
        var id = await Open(c, Fixture("v01.md", text));
        await c.Call("document.focus", new { documentId = id });
        var windowId = await WindowIdOf(c, id);
        // Mode, side pane, status bar and bounds are the window's own state, not settings a client can set: changing them
        // (and the placement the window then saves) leaves settingsRevision as it was.
        var settingsBefore = (long)(await c.Call("settings.get", new { keys = new[] { "editor.fontSize" } }))["settingsRevision"]!;
        var window = await WindowOf(windowId, c);
        var before = await c.Call("window.getView", new { windowId });
        var revision = await Revision(c, id);
        async Task<JToken> Setting(string name) => (await c.Call("test.settings.get", new { windowId, name }))["value"]!;
        try
        {
            ShowWindow(window, 3); // SW_MAXIMIZE: bounds restore the window first
            await Task.Delay(300);
            var v = await c.Call("window.setView", new { windowId, mode = "source", sidePane = new { open = true, page = "outline" }, bounds = new { x = 60, y = 50, width = 1100, height = 720 } });
            Check((string)v["mode"]! == "source" && (bool)v["sidePane"]!["open"]! && (string)v["sidePane"]!["page"]! == "outline", $"the reply is the new view ({v.ToString(Formatting.None)})");
            Check(!(bool)v["maximized"]!, "the maximized window was restored");
            GetWindowRect(window, out var rect);
            Check(rect.Left == 60 && rect.Top == 50 && rect.Right - rect.Left == 1100 && rect.Bottom - rect.Top == 720,
                $"the window is at 60,50 1100x720 ({rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top})");
            Check((bool)await Setting("SourceCode") && (bool)await Setting("SidePaneOpen") && (int)await Setting("SidePaneIndex") == 1, "the window's settings say source mode with the outline");
            Check(await WaitForPage(c, id, t => t == text, "source mode") == text, "source mode shows the text");

            v = await c.Call("window.setView", new { windowId, mode = "reading", sidePane = new { open = false } });
            Check((string)v["mode"]! == "reading" && (bool)await Setting("ReadOnly") && !(bool)await Setting("SourceCode"), "reading mode, source mode off");
            Check(!(bool)v["sidePane"]!["open"]!, "the side pane is closed");
            Check(await Revision(c, id) == revision, "no revision from a view change");

            // A mode switch waits for an automation write that is under way in the window's active document.
            const string written = "# V01\n\nwritten while the view waits\n";
            await c.Call("window.setView", new { windowId, mode = "visual" });
            var (write, barrier) = await HoldAfterApply(writer, c, id, written);
            var view = viewer.Call("window.setView", new { windowId, mode = "source" });
            await Task.Delay(700);
            Check(!view.IsCompleted, "the mode switch waits while the write is held");
            await c.Call("test.barrier.release", new { barrierId = barrier });
            var outcome = await Outcome(write);
            Check(outcome.StartsWith("committed"), $"the write commits ({outcome})");
            Check((string)(await view)["mode"]! == "source", "then the mode switches");
            Check(await WaitForPage(c, id, t => t == written, "source mode after the write") == written, "source mode shows the write");
            Check(await c.ErrorKind("window.setView", new { windowId, bounds = new { width = 100 } }) == "invalid_params", "a 100 pixel wide window is refused");
            notes.Add(outcome);
        }
        finally
        {
            var b = before["bounds"]!;
            await c.Call("window.setView", new
            {
                windowId, mode = (string)before["mode"]!, sidePane = new { open = (bool)before["sidePane"]!["open"]!, page = (string)before["sidePane"]!["page"]! },
                statusBar = (bool)before["statusBar"]!, bounds = new { x = (int)b["x"]!, y = (int)b["y"]!, width = (int)b["width"]!, height = (int)b["height"]! },
            });
        }
        // The window saves its placement a moment after a move or resize.
        await Task.Delay(3000);
        var settingsAfter = (long)(await c.Call("settings.get", new { keys = new[] { "editor.fontSize" } }))["settingsRevision"]!;
        notes.Add($"settings revision {settingsBefore} -> {settingsAfter} across the view changes");
        Check(settingsAfter == settingsBefore, "view changes and the saved window placement leave settingsRevision as it was");
    }

    private static async Task B01(List<string> notes)
    {
        using var c = await Session("e2e B01");
        var first = (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;
        var second = (string)(await c.Call("test.window.open"))["windowId"]!;
        async Task<JToken> Value(string window, string name) => (await c.Call("test.settings.get", new { windowId = window, name }))["value"]!;
        async Task Until(string window, string name, Func<JToken, bool> ok, string what)
        {
            for (var i = 0; i < 100; i++) { if (ok(await Value(window, name))) return; await Task.Delay(50); }
            throw new CaseFailed($"{what} (still {await Value(window, name)})");
        }
        var font = (double)await Value(first, "FontSize") == 21 ? 19.0 : 21.0;
        await c.Call("test.settings.set", new { windowId = first, name = "FontSize", value = font });
        await Until(second, "FontSize", v => (double)v == font, $"the second window follows the first window's font size {font}");
        await c.Call("test.settings.set", new { windowId = second, name = "TextDirection", value = "rtl" });
        await Until(first, "TextDirection", v => (string)v! == "rtl", "the first window follows the second window's text direction");
        await c.Call("test.settings.set", new { windowId = second, name = "TextDirection", value = "auto" });
        await Until(first, "TextDirection", v => (string)v! == "auto", "and back");
        // Window-local: switching one window to source mode must not switch the other.
        var sourceBefore = (bool)await Value(second, "SourceCode");
        await c.Call("test.settings.set", new { windowId = first, name = "SourceCode", value = !sourceBefore });
        await Until(first, "SourceCode", v => (bool)v == !sourceBefore, "the first window's own mode changes");
        await c.Call("window.list"); // a round trip through both windows' queues after the change
        Check((bool)await Value(second, "SourceCode") == sourceBefore, "the second window keeps its mode");
        await c.Call("test.settings.set", new { windowId = first, name = "SourceCode", value = sourceBefore });
        notes.Add($"font {font}, text direction rtl/auto, source mode stayed per window");
    }

    private static async Task S2(List<string> notes)
    {
        using var c = await Session("e2e S2");
        var windows = ((JArray)(await c.Call("window.list"))["windows"]!).Select(w => (string)w["windowId"]!).ToList();
        var got = await c.Call("settings.get", new { keys = new[] { "editor.fontSize" } });
        var revision = (long)got["settingsRevision"]!;
        var font = (int)got["values"]!["editor.fontSize"]! == 23 ? 24 : 23;
        var set = await c.Call("settings.set", new { key = "editor.fontSize", value = font, baseSettingsRevision = revision });
        Check((long)set["settingsRevision"]! > revision, "the settings revision advanced");
        foreach (var window in windows)
            Check((double)(await c.Call("test.settings.get", new { windowId = window, name = "FontSize" }))["value"]! == font, $"window {window[..7]} has font size {font}");
        foreach (var window in windows)
        {
            // The editor page itself is drawn with it, not only the host's setting.
            string? drawn = null;
            for (var i = 0; i < 100 && drawn != $"{font}px"; i++)
            {
                try { drawn = (string?)(await c.Call("test.editor.style", new { windowId = window }))["fontSize"]; }
                catch (JsonRpcRemoteException e) { drawn = "error: " + e.Message; }
                if (drawn != $"{font}px") await Task.Delay(50);
            }
            Check(drawn == $"{font}px", $"window {window[..7]} ({windows.IndexOf(window) + 1} of {windows.Count})'s editor page is drawn at {font}px ({drawn})");
        }
        // The other exposed keys reach the pages too: line height, text direction; the theme reaches every window.
        var rev = (long)(await c.Call("settings.get"))["settingsRevision"]!;
        rev = (long)(await c.Call("settings.set", new { key = "editor.lineHeight", value = 1.83, baseSettingsRevision = rev }))["settingsRevision"]!;
        rev = (long)(await c.Call("settings.set", new { key = "editor.textDirection", value = "rtl", baseSettingsRevision = rev }))["settingsRevision"]!;
        foreach (var window in windows)
        {
            JToken? style = null;
            for (var i = 0; i < 100; i++)
            {
                try { style = await c.Call("test.editor.style", new { windowId = window }); } catch (JsonRpcRemoteException) { }
                if ((string?)style?["direction"] == "rtl" && Math.Abs(double.Parse(((string)style!["lineHeight"]!).Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture) - font * 1.8) < 0.6) break;
                await Task.Delay(50);
            }
            Check((string?)style?["direction"] == "rtl", $"window {window[..7]}'s page is right-to-left ({style})");
            Check(style != null && Math.Abs(double.Parse(((string)style["lineHeight"]!).Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture) - font * 1.8) < 0.6, $"window {window[..7]}'s page line height is {font}*1.8 (1.83 rounded) ({style})");
        }
        rev = (long)(await c.Call("settings.set", new { key = "editor.textDirection", value = "auto", baseSettingsRevision = rev }))["settingsRevision"]!;
        rev = (long)(await c.Call("settings.set", new { key = "editor.lineHeight", value = 1.6, baseSettingsRevision = rev }))["settingsRevision"]!;
        rev = (long)(await c.Call("settings.set", new { key = "appearance.theme", value = new { kind = "builtIn", id = "dark" }, baseSettingsRevision = rev }))["settingsRevision"]!;
        foreach (var window in windows)
            Check((int)(await c.Call("test.settings.get", new { windowId = window, name = "AppTheme" }))["value"]! == 2, $"window {window[..7]} uses the dark theme");
        Check((string)(await c.Call("settings.get", new { keys = new[] { "appearance.theme" } }))["values"]!["appearance.theme"]!["id"]! == "dark", "settings.get reads the theme back");
        await c.Call("settings.set", new { key = "appearance.theme", value = new { kind = "builtIn", id = "system" }, baseSettingsRevision = rev });
        var file = JObject.Parse(File.ReadAllText(Path.Combine(testRoot, "data", "Settings.json")));
        Check((double)file["FontSize"]! == font, $"Settings.json holds {font} ({file["FontSize"]})");
        Check((int)(await c.Call("settings.get", new { keys = new[] { "editor.fontSize" } }))["values"]!["editor.fontSize"]! == font, "settings.get reads it back");
        Check(await c.ErrorCode("settings.set", new { key = "editor.fontSize", value = 12, baseSettingsRevision = revision }) == -32012, "a stale settings revision is refused");
        Check(await c.ErrorCode("settings.set", new { key = "SourceCode", value = true, baseSettingsRevision = (long)set["settingsRevision"]! }) == -32020, "a window mode is not an external setting");
        // Leave the windows as they were: a later case that clicks and types into a page (F01) found it re-laid out
        // under a font size nobody put back, and its keystroke went elsewhere.
        var original = (int)got["values"]!["editor.fontSize"]!;
        await SetSetting(c, "editor.fontSize", original);
        foreach (var window in windows)
        {
            string? drawn = null;
            for (var i = 0; i < 100 && drawn != $"{original}px"; i++)
            {
                try { drawn = (string?)(await c.Call("test.editor.style", new { windowId = window }))["fontSize"]; } catch (JsonRpcRemoteException) { }
                if (drawn != $"{original}px") await Task.Delay(50);
            }
        }
        notes.Add($"{windows.Count} window(s), font {font}, back to {original}");
    }

    private static async Task B02(List<string> notes)
    {
        using var c = await Session("e2e B02");
        var windows = ((JArray)(await c.Call("window.list"))["windows"]!).Select(w => (string)w["windowId"]!).ToList();
        if (windows.Count < 2) windows.Add((string)(await c.Call("test.window.open"))["windowId"]!);
        var settingsWindow = windows[1];
        await c.Call("test.window.navigate", new { windowId = settingsWindow, route = "Settings/Editor" });
        try
        {
            var got = await c.Call("settings.get", new { keys = new[] { "editor.fontSize" } });
            var before = (int)got["values"]!["editor.fontSize"]!;
            var windowHandle = WindowOf(settingsWindow, c);
            await WaitForNumberBox(await windowHandle, before, notes, "the settings page shows the current size");
            var font = before == 25 ? 26 : 25;
            await SetSetting(c, "editor.fontSize", font);
            await WaitForNumberBox(await windowHandle, font, notes, "the open settings page follows a change made elsewhere");
            await SetSetting(c, "editor.fontSize", before);
        }
        finally
        {
            await c.Call("test.window.navigate", new { windowId = settingsWindow, route = "Main" });
        }
    }

    private static async Task<IntPtr> WindowOf(string windowId, Client c) =>
        new IntPtr((long)(await c.Call("test.window.handle", new { windowId }))["hwnd"]!);

    private static async Task WaitForNumberBox(IntPtr window, int expected, List<string> notes, string what)
    {
        string seen = "";
        for (var i = 0; i < 80; i++)
        {
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(window);
                var edits = root.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit));
                var values = new List<string>();
                foreach (System.Windows.Automation.AutomationElement edit in edits)
                    if (edit.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var pattern))
                        values.Add(((System.Windows.Automation.ValuePattern)pattern).Current.Value);
                seen = string.Join(" | ", values);
                if (values.Any(v => new string(v.TakeWhile(char.IsDigit).ToArray()) == expected.ToString())) { notes.Add($"{what}: {seen}"); return; }
            }
            catch (Exception e) { seen = e.Message; }
            await Task.Delay(100);
        }
        throw new CaseFailed($"{what}: expected {expected}, the page shows [{seen}]");
    }

    // What the editor page holds - focus, the caret's block - for failure messages.
    private const string PageStateScript = @"(() => {
        const s = getSelection(); const n = s && s.anchorNode; const e = n && (n.nodeType === 1 ? n : n.parentElement);
        const blk = e && e.closest('[id^=""ag-""]');
        return { hasFocus: document.hasFocus(), active: document.activeElement && (document.activeElement.id || document.activeElement.tagName), caret: blk && blk.id, editor: !!window.__typedownMuya };
    })()";

    /// <summary>A full dump of the test host (dotnet-dump, where installed) next to the screenshots; why not, otherwise.</summary>
    private static string HangDump(string label)
    {
        try
        {
            var tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "dotnet-dump.exe");
            if (!File.Exists(tool)) return "no dotnet-dump";
            var file = Path.Combine(outputDir, label + ".dmp");
            using var dump = Process.Start(new ProcessStartInfo(tool, $"collect -p {hostPid} --type Full -o \"{file}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            return dump.WaitForExit(180000) && File.Exists(file) ? file : "dump failed";
        }
        catch (Exception e)
        {
            return "dump failed: " + e.Message;
        }
    }

    private static async Task D01(List<string> notes)
    {
        using var c = await Session("e2e D01");
        var windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;
        var result = await c.Call("test.dispatcher.stress", new { windowId, threads = 8, posts = 1000 });
        notes.Add(result.ToString(Formatting.None));
        Check((long)result["ran"]! == (long)result["posted"]!, $"every posted callback ran ({result["ran"]} of {result["posted"]})");
    }

    private static async Task<string> PageState(Client c, string windowId)
    {
        try { return (await c.Call("test.editor.eval", new { windowId, script = PageStateScript }))["result"]!.ToString(Formatting.None); }
        catch (Exception e) { return "no state: " + e.Message; }
    }

    private static async Task N01(List<string> notes)
    {
        using var c = await Session("e2e N01");
        const string text = "---\ntitle: kept\n---\n\n# N01\n\nbody\n";
        var windowId = (string)((JArray)(await c.Call("window.list"))["windows"]!)[0]["windowId"]!;
        for (var round = 0; round < 4; round++)
        {
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Editor" });
            await Task.Delay(800);
            await c.Call("test.window.navigate", new { windowId, route = "Main" });
            var id = await Open(c, Fixture($"n01-{round}.md", "# N01\n"));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var kind = await c.ErrorKind("document.get", new { documentId = id, consistency = "latest" });
            Check(kind == null, $"round {round}: the first latest read after returning from the settings page succeeds ({kind} after {clock.ElapsedMilliseconds} ms; page {await PageState(c, windowId)})");
            await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text, normalizationPolicy = "allowUnknown" });
            Check((string)(await Get(c, id))["text"]! == text, $"round {round}: the write is the text");
            notes.Add($"round {round}: read in {clock.ElapsedMilliseconds} ms");
        }
    }

    private static async Task K01(List<string> notes)
    {
        using var c = await Session("e2e K01");
        var id = await Open(c, Fixture("k01.md", "# K01\n\nThe end.\n"));
        var windowId = await WindowIdOf(c, id);
        await TypeInto(c, id, "A");
        await WaitForPage(c, id, t => t.Contains('A'), "the first keystroke");
        // Another program changes the font size and the line height while the reader has the keyboard.
        using (var other = await Session("e2e K01 settings"))
        {
            var font = (int)(await other.Call("settings.get", new { keys = new[] { "editor.fontSize" } }))["values"]!["editor.fontSize"]!;
            await SetSetting(other, "editor.fontSize", font + 1);
            await Task.Delay(500);
            await SetSetting(other, "editor.fontSize", font);
            await Task.Delay(500);
        }
        var state = await PageState(c, windowId);
        // No new focus from the host: the keys go wherever the page's own focus is, as they do for the reader.
        Send(Key(0x11, false), Key(0x23, false), Key(0x23, true), Key(0x11, true));
        var inputs = new[] { new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = 'B', dwFlags = 0x0004 } } }, new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = 'B', dwFlags = 0x0006 } } } };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        string? text = null;
        for (var i = 0; i < 40 && text?.Contains('B') != true; i++) { await Task.Delay(100); text = (string?)(await c.Call("test.editor.pageText", new { documentId = id }))["text"]; }
        Check(text?.Contains('B') == true, $"the keystroke after the font change reached the page (page after the change: {state}; text {JsonConvert.SerializeObject(text)})");
        notes.Add("page after the change: " + state);
    }

    // Ctrl+, opens the settings and Ctrl+, closes them: the reader is back where they were, caret and keyboard. Closing
    // the settings builds the main page again and reloads the editor page; the caret came back from the per-file memory
    // only (or not at all), and the keyboard was not given back.
    private static Task K02(List<string> notes) => SettingsRoundTrip(notes, untitled: false);

    // The same with an untitled document: no file, so no per-file caret memory to come back from.
    private static Task K03(List<string> notes) => SettingsRoundTrip(notes, untitled: true);

    private static async Task SettingsRoundTrip(List<string> notes, bool untitled)
    {
        using var c = await Session(untitled ? "e2e K03" : "e2e K02");
        const string text0 = "# K02\n\nfirst line\n\nsecond line\n\nthird line\n";
        string id;
        if (untitled)
        {
            id = (string)(await c.Call("document.create", new { text = text0, reveal = "document" }))["documentId"]!;
            await c.Call("document.get", new { documentId = id, consistency = "latest" });
        }
        else id = await Open(c, Fixture("k02.md", text0));
        var windowId = await WindowIdOf(c, id);
        await TypeInto(c, id, "A");
        await WaitForPage(c, id, t => t.Contains("third lineA"), "the first keystroke");
        // Up to the second paragraph (one Up: the blank lines between paragraphs are no lines here), its end, a letter.
        Send(Key(0x26, false), Key(0x26, true));
        await Task.Delay(150);
        Send(Key(0x23, false), Key(0x23, true));
        await Task.Delay(150);
        TypeChar('B');
        await WaitForPage(c, id, t => t.Contains("second lineB"), "B at the end of the second paragraph");
        var before = await PageState(c, windowId);
        // Ctrl+, twice, as the reader presses it: open the settings, close them.
        async Task<int> Menus() => ((JArray)(await c.Call("test.window.menuTitles", new { windowId }))["titles"]!).Count;
        await CtrlComma();
        await Task.Delay(2000);
        var inSettings = await Menus();
        if (inSettings != 0) notes.Add("screen after the first Ctrl+,: " + Screenshot("k02-settings"));
        await CtrlComma();
        await Task.Delay(1500);
        var back = await Menus();
        notes.Add($"menus: {inSettings} with the settings open, {back} after");
        Check(inSettings == 0 && back > 0, $"Ctrl+, opened the settings and Ctrl+, closed them (menus {inSettings}, then {back})");
        await c.Call("document.get", new { documentId = id, consistency = "latest" });
        await Task.Delay(500);
        var after = await PageState(c, windowId);
        notes.Add($"before {before}; after {after}");
        TypeChar('C');
        string? text = null;
        for (var i = 0; i < 30 && text?.Contains('C') != true; i++) { await Task.Delay(100); text = (string?)(await c.Call("test.editor.pageText", new { documentId = id }))["text"]; }
        Check(text?.Contains("second lineBC") == true, $"a letter typed after the settings closed goes where the caret was ({JsonConvert.SerializeObject(text)})");
    }

    // Ctrl+, as a person types it: the settings shortcut is a low-level keyboard hook reading the key state, and Ctrl
    // sent in the same batch as the comma was not down yet when the hook looked.
    private static async Task CtrlComma()
    {
        Send(Key(0x11, false));
        await Task.Delay(80);
        Send(Comma(false), Comma(true));
        await Task.Delay(80);
        Send(Key(0x11, true));
    }

    // The comma key with its scan code: the editor page (Chromium) reads the key from it, and a bare VK_OEM_COMMA
    // was not Ctrl+, there - the settings never opened.
    private static INPUT Comma(bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0xBC, wScan = 0x33, dwFlags = up ? 0x0002u : 0u } } };

    // F11 puts the window in full screen: the main page then starts at the top edge of the screen. A 4 px row kept for
    // compact mode's top border stayed above it, a strip of window background along the top.
    private static async Task FS01(List<string> notes)
    {
        using var c = await Session("e2e FS01");
        var id = await Open(c, Fixture("fs01.md", "# FS01\n\nText\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await Activate(window);
        async Task<JToken> Layout() => await c.Call("test.window.layout", new { windowId });
        var before = await Layout();
        Send(Key(0x7A, false), Key(0x7A, true));
        JToken full = before;
        for (var i = 0; i < 30 && !(bool)full["fullScreen"]!; i++) { await Task.Delay(100); full = await Layout(); }
        await Task.Delay(800);
        full = await Layout();
        notes.Add($"before {before.ToString(Formatting.None)}; full screen {full.ToString(Formatting.None)}");
        try
        {
            Check((bool)full["fullScreen"]!, "F11 put the window in full screen");
            Check((double)full["mainPageTop"]! == 0, $"in full screen the main page starts at the top edge ({full["mainPageTop"]} px below it)");
        }
        finally
        {
            await Activate(window);
            Send(Key(0x7A, false), Key(0x7A, true));
            JToken after = full;
            for (var i = 0; i < 30 && (bool)after["fullScreen"]!; i++) { await Task.Delay(100); after = await Layout(); }
            await Task.Delay(500);
            after = await Layout();
            notes.Add("after " + after.ToString(Formatting.None));
            Check(!(bool)after["fullScreen"]! && (double)after["mainPageTop"]! == (double)before["mainPageTop"]!, "F11 again leaves full screen, the layout as it was");
        }
    }

    // reveal: "change" (spec 2.3): a write scrolls its first changed block into view when it is off screen and leaves
    // the caret where the reader had it; "document" leaves the page where it was, and so does a change already on screen.
    private static async Task RV01(List<string> notes)
    {
        using var c = await Session("e2e RV01");
        var text = new StringBuilder("# RV01\n\n");
        for (var i = 1; i <= 120; i++) text.Append($"Paragraph {i} of the long document.\n\n");
        text.Append("Last paragraph.\n");
        var id = await Open(c, Fixture("rv01.md", text.ToString()));
        var windowId = await WindowIdOf(c, id);
        await c.Call("test.editor.focus", new { windowId });
        await Task.Delay(500);
        async Task ToTop() { await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, 0), 0" }); await Task.Delay(300); }
        // Where the page is, the window height, where the block holding mark is (null: not drawn), and the text the
        // caret is in.
        async Task<JToken> State(string mark)
        {
            var script = "(() => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n, e = null; " +
                $"while ((n = w.nextNode())) if (n.textContent.includes({JsonConvert.ToString(mark)})) {{ e = n.parentElement; break }} " +
                "const r = e && e.getBoundingClientRect(); const s = getSelection(); " +
                "return { y: Math.round(scrollY), h: innerHeight, top: r ? Math.round(r.top) : null, " +
                "caret: s && s.anchorNode ? (s.anchorNode.textContent || '').slice(0, 30) : null, apply: window.__typedownLastApply || null } })()";
            return (await c.Call("test.editor.eval", new { windowId, script }))["result"]!;
        }
        async Task<JToken> Replace(string find, string replacement, string reveal, string normalizationPolicy = "requireKnownSafe")
        {
            await c.Call("document.replaceText", new { documentId = id, baseRevision = await Revision(c, id), find, replacement, expectedCount = 1, reveal, normalizationPolicy });
            await Task.Delay(600);
            return await State(replacement.Split('\n')[0]);
        }
        bool Shown(JToken s) => s["top"]!.Type == JTokenType.Integer && (int)s["top"]! >= 0 && (int)s["top"]! < (int)s["h"]!;

        await ToTop();
        var caret = (string?)(await State("RV01"))["caret"];
        notes.Add($"caret before: {caret}");
        Check(!string.IsNullOrEmpty(caret), "the caret is in the document");

        var a = await Replace("Last paragraph.", "Last paragraph, A.", "document");
        notes.Add("document, at the end: " + a.ToString(Formatting.None));
        Check((int)a["y"]! < 5, "reveal: \"document\" leaves the page at the top");

        var b = await Replace("Last paragraph, A.", "Last paragraph, B.", "change");
        notes.Add("change, at the end: " + b.ToString(Formatting.None));
        Check((int)b["y"]! > 0 && Shown(b), "reveal: \"change\" scrolls the change at the end into view");
        Check((string?)b["caret"] == caret, "the caret stays where it was");

        await ToTop();
        var d = await Replace("Paragraph 2 of", "Paragraph 2 (changed) of", "change");
        notes.Add("change on screen: " + d.ToString(Formatting.None));
        Check((int)d["y"]! < 5, "a change already on screen does not move the page");

        await ToTop();
        await c.Call("document.replaceText", new { documentId = id, baseRevision = await Revision(c, id), find = "Paragraph 100 of the long document.\n\n", replacement = "", expectedCount = 1, reveal = "change" });
        await Task.Delay(600);
        var deleted = await State("Paragraph 101 of");
        notes.Add("deletion: " + deleted.ToString(Formatting.None));
        Check((int)deleted["y"]! > 0 && Shown(deleted), "a deletion scrolls the block now in its place into view");

        try
        {
            await c.Call("window.setView", new { windowId, mode = "source" });
            await Task.Delay(500);
            await ToTop();
            // Source mode has no visual editor to tell what it would rewrite: every write there is "unknown".
            await Replace("Paragraph 120 of", "Paragraph 120 (source) of", "change", "allowUnknown");
            // CodeMirror marks only the changed characters, which splits the line into text nodes.
            var s = await State("Paragraph 120 ");
            notes.Add("source mode: " + s.ToString(Formatting.None));
            Check((int)s["y"]! > 0 && Shown(s), "in source mode the change is scrolled into view");
        }
        finally
        {
            await c.Call("window.setView", new { windowId, mode = "visual" });
        }

        // A new reference definition changes how other blocks may render: the editor loads the whole document, and the
        // hold that puts the page back where it was must hold it at the change instead.
        await ToTop();
        await Replace("Last paragraph, B.", "Last paragraph, [D][r].\n\n[r]: https://example.com/", "change");
        // The link splits the line into text nodes; nothing else says "Last paragraph, " by now.
        var wholeState = await State("Last paragraph, ");
        notes.Add("whole document: " + wholeState.ToString(Formatting.None));
        Check((string?)wholeState["apply"] == "whole", "a new reference definition loads the whole document");
        Check((int)wholeState["y"]! > 0 && Shown(wholeState), "after a whole load the change is in view too");
    }

    // The endpoint name a starting host writes, or null after 30 s. The host may still have the file open: the read
    // that came right after File.Exists failed with a sharing violation (Q01, once in a full run).
    private static async Task<string?> ReadEndpointFile(string path)
    {
        for (var i = 0; i < 300; i++)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } name) return name;
            }
            catch (IOException) { }
            await Task.Delay(100);
        }
        return null;
    }

    private static void TypeChar(char ch)
    {
        var inputs = new[] { new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 } } }, new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0006 } } } };
        if (SendInput(2, inputs, Marshal.SizeOf<INPUT>()) != 2) throw new CaseFailed("SendInput was refused (is the desktop locked?)");
    }

    private static async Task F01(List<string> notes)
    {
        using var c = await Session("e2e F01");
        var id = await Open(c, Fixture("f01.md", "# F01\n"));
        // Written through the API, then typed into by a person: every protected payload must survive the first
        // visual edit (checked by exact strings, not by the editor's classifier).
        var keep = new[]
        {
            "---\ntitle: \"F01: kept\"\n---",
            "[site](https://example.com/a_b?x=1&y=2 \"Title\")",
            "![alt](./img/a%20b.png \"Pic\")",
            "claim[^note]",
            "[^note]: The note.",
            "- [x] done",
            "- [ ] todo",
            "```ts {title=\"x.ts\"}\nconst a:\tnumber = 1;\n```",
            "$$\nE = mc^2\n$$",
            "<kbd>Ctrl</kbd>",
        };
        // A plain last paragraph: Ctrl+End lands the caret there, not in an image or a maths block.
        var text = keep[0] + "\n\n# F01\n\n" + string.Join("\n\n", keep.Skip(1).Take(3)) + "\n\n" + keep[5] + "\n" + keep[6] + "\n\n" + string.Join("\n\n", keep.Skip(7)) + "\n\n" + keep[4] + "\n\nThe end.\n";
        var written = await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text, normalizationPolicy = "allowUnknown", reveal = "document" });
        notes.Add($"written, normalization {written["normalization"]?["pendingNormalization"]} {written["normalization"]?["reasons"]?.ToString(Formatting.None)}");
        Check((string?)written["normalization"]?["pendingNormalization"] != "unsafe", "the written text is not predicted to lose anything");
        await TypeInto(c, id, "Q");
        var after = await WaitForPage(c, id, t => t.Contains('Q'), "the keystroke");
        var latest = (string)(await Get(c, id))["text"]!;
        Check(latest == after, "latest read is what the page holds");
        var lost = keep.Where(k => !latest.Contains(k)).ToList();
        Check(lost.Count == 0, $"every protected payload survives the first edit (lost: {JsonConvert.SerializeObject(lost)}; text {JsonConvert.SerializeObject(latest)})");
    }

    private static async Task M01(List<string> notes)
    {
        using var c = await Session("e2e M01");
        var path = Fixture("m01.md", "# M01\n\nstart\n");
        var id = await Open(c, path);
        var window = (string)(await c.Call("document.focus", new { documentId = id }))["windowId"]!;
        // Text Muya would write back differently (setext heading, '*' bullets, a ragged table): only the mapping to the
        // source keeps it exact while nobody edits.
        const string text = "Title\n=====\n\n* one\n* two\n\n|a|b|\n|-|-|\n|1|2|\n";
        var written = await c.Call("document.replace", new { documentId = id, baseRevision = await Revision(c, id), text, normalizationPolicy = "allowUnknown" });
        var revision = (long)written["revision"]!;
        notes.Add($"written r{revision}, normalization {written["normalization"]?["pendingNormalization"]}");
        foreach (var (source, reading, name) in new[] { (false, true, "reading"), (true, false, "source"), (false, false, "visual"), (false, true, "reading"), (false, false, "visual") })
        {
            await c.Call("test.settings.set", new { windowId = window, name = "SourceCode", value = source });
            await c.Call("test.settings.set", new { windowId = window, name = "ReadOnly", value = reading });
            var page = await WaitForPage(c, id, t => t == text, $"the {name} view's text");
            var doc = await Get(c, id);
            Check((string)doc["text"]! == text, $"{name}: latest read is the written text ({JsonConvert.SerializeObject((string)doc["text"]!)})");
            Check((long)doc["revision"]! == revision, $"{name}: no revision without an edit (r{doc["revision"]})");
            Check(!(bool)doc["saved"]!, $"{name}: still unsaved, not marked saved or re-dirtied oddly");
        }
        await c.Call("test.settings.set", new { windowId = window, name = "SourceCode", value = false });
        await c.Call("test.settings.set", new { windowId = window, name = "ReadOnly", value = false });
        await c.Call("document.save", new { documentId = id, baseRevision = revision });
        Check(Disk(path) == text, $"the file holds exactly the written text ({JsonConvert.SerializeObject(Disk(path))})");
    }

    private static async Task P01(List<string> notes)
    {
        using var c = await Session("e2e P01");
        var a = await Open(c, Fixture("p01-a.md", "# A\n\nalpha\n"));
        await Open(c, Fixture("p01-b.md", "# B\n\nbeta\n")); // A is now a background tab
        var windowId = (string)(await c.Call("document.get", new { documentId = a, consistency = "snapshot" }))["windowId"]!;
        var rA = await Revision(c, a);
        Check(await c.ErrorCode("document.replace", new { documentId = a, baseRevision = rA, text = "x\n", awaitPresentation = true }) == -32602,
            "awaitPresentation without reveal is invalid_params");
        Check(await Revision(c, a) == rA, "the refused write changed nothing");

        var window = await WindowOf(windowId, c);
        ShowWindow(window, 6); // SW_MINIMIZE: reveal has to bring the window back
        await Task.Delay(500);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        JToken written;
        try
        {
            written = await c.Call("document.replace", new { documentId = a, baseRevision = rA, text = "# A\n\nalpha shown\n", reveal = "document", awaitPresentation = true, normalizationPolicy = "allowUnknown" });
        }
        catch (JsonRpcRemoteException e)
        {
            throw new CaseFailed($"{e.ErrorData?["kind"]} after {watch.ElapsedMilliseconds} ms: {e.ErrorData?.ToString(Formatting.None)}");
        }
        notes.Add($"presentation {written["presentation"]?.ToString(Formatting.None)} in {watch.ElapsedMilliseconds} ms");
        var presentation = written["presentation"];
        Check(presentation != null && (bool)presentation["windowVisible"]! && (bool)presentation["tabActive"]! && (bool)presentation["pageFramesPassed"]! && (bool)presentation["hostRenderPassed"]!,
            "every presentation target was observed");
        Check((long)written["revision"]! == rA + 1, "the write committed");
        var windows = (JArray)(await c.Call("window.list"))["windows"]!;
        Check(windows.Any(w => (string)w["windowId"]! == windowId && (string?)w["activeDocumentId"] == a), "the written document is the window's active tab");
        Check(!IsIconic(window), "the window is no longer minimized");
        Check((string)(await Get(c, a))["text"]! == "# A\n\nalpha shown\n", "latest read returns the written text");
    }

    private static async Task<string> WindowIdOf(Client c, string documentId) =>
        (string)(await c.Call("document.get", new { documentId, consistency = "snapshot" }))["windowId"]!;

    // Starts a write and holds it where the page has applied it but the host has not committed it yet.
    private static async Task<(Task<JToken> write, string barrier)> HoldAfterApply(Client writer, Client driver, string documentId, string text)
    {
        var barrier = (string)(await driver.Call("test.barrier.arm", new { point = EditBarrierPoints.AfterEditorMutationBeforeReport, documentId }))["barrierId"]!;
        var write = writer.Call("document.replace", new { documentId, baseRevision = await Revision(driver, documentId), text, normalizationPolicy = "allowUnknown" });
        var waiting = driver.Call("test.barrier.waitHit", new { barrierId = barrier, timeoutMs = 20000 });
        // A write refused before it reached the page (the page reloading, say) ends first: say why, without the wait.
        if (await Task.WhenAny(waiting, write) == write && !waiting.IsCompleted)
            throw new CaseFailed($"the write ended before it reached the page: {await Outcome(write)}");
        var hit = await waiting;
        if (!(bool)hit["hit"]!) throw new CaseFailed("the write never reached the page");
        return (write, barrier);
    }

    private static async Task<string> Outcome(Task<JToken> write)
    {
        try { var r = await write; return $"committed r{r["revision"]}"; }
        catch (JsonRpcRemoteException e) { return $"refused {e.ErrorData?["kind"]} {e.ErrorData?["reason"]}"; }
    }

    private static async Task W01(List<string> notes)
    {
        using var writer = await Session("e2e W01 writer");
        using var driver = await Session("e2e W01 driver");
        const string original = "# W01\n\noriginal\n";
        var a = await Open(driver, Fixture("w01.md", original));
        var windowId = await WindowIdOf(driver, a);
        var r = await Revision(driver, a);
        var (write, barrier) = await HoldAfterApply(writer, driver, a, "# W01\n\nwritten\n");
        Check((await WaitForPage(driver, a, t => t.Contains("written"), "the applied write")).Contains("written"), "the page shows the write before the commit");
        await driver.Call("test.editor.reload", new { windowId });
        await WaitForPage(driver, a, t => t == original, "the reloaded page");
        await driver.Call("test.barrier.release", new { barrierId = barrier });
        var outcome = await Outcome(write);
        notes.Add(outcome);
        Check(outcome.Contains("editorReloaded"), "the write is refused because the page reloaded");
        var doc = await Get(driver, a);
        Check((string)doc["text"]! == original && (long)doc["revision"]! == r, "host text and revision are the ones before the write");
        Check(await WaitForPage(driver, a, t => t == original, "the restored page") == original, "the page shows the same text as the host");
        var again = await driver.Call("document.replace", new { documentId = a, baseRevision = r, text = "# W01\n\nsecond try\n", normalizationPolicy = "allowUnknown" });
        Check((long)again["revision"]! == r + 1, "the document still takes writes");
    }

    private static async Task W02(List<string> notes)
    {
        using var writer = await Session("e2e W02 writer");
        using var driver = await Session("e2e W02 driver");
        var a = await Open(driver, Fixture("w02.md", "# W02\n\noriginal\n"));
        var windowId = await WindowIdOf(driver, a);
        const string written = "# W02\n\nwritten across a mode switch\n";
        try
        {
            var (write, barrier) = await HoldAfterApply(writer, driver, a, written);
            await driver.Call("test.settings.set", new { windowId, name = "SourceCode", value = true });
            await Task.Delay(500);
            await driver.Call("test.barrier.release", new { barrierId = barrier });
            var outcome = await Outcome(write);
            notes.Add(outcome);
            Check(outcome.StartsWith("committed"), "the write commits");
            Check((string)(await Get(driver, a))["text"]! == written, "the host holds the write");
            Check(await WaitForPage(driver, a, t => t == written, "source mode") == written, "source mode shows exactly the write");
            await driver.Call("test.settings.set", new { windowId, name = "SourceCode", value = false });
            Check(await WaitForPage(driver, a, t => t == written, "visual mode") == written, "visual mode shows exactly the write");
            Check((string)(await Get(driver, a))["text"]! == written, "the host still holds the write after switching back");
        }
        finally
        {
            await driver.Call("test.settings.set", new { windowId, name = "SourceCode", value = false });
        }
    }

    private static async Task W03(List<string> notes)
    {
        using var writer = await Session("e2e W03 writer");
        using var driver = await Session("e2e W03 driver");
        const string originalA = "# A\n\nalpha\n", originalB = "# B\n\nbeta\n";
        var b = await Open(driver, Fixture("w03-b.md", originalB));
        var a = await Open(driver, Fixture("w03-a.md", originalA));

        // Away and back: the page reloaded A from the host, which never took the write in.
        var rA = await Revision(driver, a);
        var (write, barrier) = await HoldAfterApply(writer, driver, a, "# A\n\nwritten\n");
        await driver.Call("document.focus", new { documentId = b });
        await driver.Call("document.focus", new { documentId = a });
        await driver.Call("test.barrier.release", new { barrierId = barrier });
        var outcome = await Outcome(write);
        notes.Add("away and back: " + outcome);
        Check(outcome.Contains("editorReloaded"), "away and back: refused");
        Check((string)(await Get(driver, a))["text"]! == originalA && await Revision(driver, a) == rA, "away and back: A is unchanged in the host");
        Check(await WaitForPage(driver, a, t => t == originalA, "A") == originalA, "away and back: the page shows A as the host holds it");
        Check((string)(await Get(driver, b))["text"]! == originalB, "away and back: B is untouched");

        // Away only: the write goes into A's tab, and shows when A comes back.
        const string written = "# A\n\nwritten while away\n";
        (write, barrier) = await HoldAfterApply(writer, driver, a, written);
        await driver.Call("document.focus", new { documentId = b });
        await driver.Call("test.barrier.release", new { barrierId = barrier });
        outcome = await Outcome(write);
        notes.Add("away: " + outcome);
        Check(outcome.StartsWith("committed"), "away: committed");
        Check(await WaitForPage(driver, b, t => t == originalB, "B") == originalB, "away: the page shows B untouched");
        Check((string)(await Get(driver, a))["text"]! == written, "away: A's tab holds the write");
        await driver.Call("document.focus", new { documentId = a });
        Check(await WaitForPage(driver, a, t => t == written, "A again") == written, "away: A shows the write when it comes back");
    }

    // typedownctl published next to the driver (e2e/cli) and run as "typedownctl mcp", started the way an agent host starts it: stdio, one message per line.
    private sealed class McpProcess : IDisposable
    {
        private readonly Process process;
        private int id;

        public McpProcess(string endpoint)
        {
            var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "cli", Brand.CliName + ".dll"));
            if (!File.Exists(dll)) throw new CaseFailed(Brand.CliName + " is not published next to the driver: " + dll);
            process = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { dll, "mcp", "--endpoint", endpoint },
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            })!;
            process.StandardInput.AutoFlush = true;
        }

        public async Task<JObject> Send(string method, object parameters)
        {
            var request = new JObject { ["jsonrpc"] = "2.0", ["id"] = ++id, ["method"] = method, ["params"] = JToken.FromObject(parameters) };
            await process.StandardInput.WriteLineAsync(request.ToString(Formatting.None));
            var line = process.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(line, Task.Delay(20000)) != line) throw new CaseFailed($"typedown-mcp did not answer {method}");
            return JObject.Parse(await line ?? throw new CaseFailed("typedown-mcp closed its output: " + process.StandardError.ReadToEnd()));
        }

        public async Task<JObject> Tool(string name, object arguments) =>
            (JObject)(await Send("tools/call", new { name, arguments }))["result"]!;

        /// <summary>Closing its input is how an agent host stops it; it must then exit by itself.</summary>
        public bool CloseAndWait()
        {
            process.StandardInput.Close();
            return process.WaitForExit(10000);
        }

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
        }
    }

    private static async Task MC01(List<string> notes)
    {
        using var driver = await Session("e2e MC01 driver");
        const string original = "# MC01\n\nThe quick brown fox.\n";
        var a = await Open(driver, Fixture("mc01.md", original));
        await Open(driver, Fixture("mc01-other.md", "# other\n")); // MC01 is a background tab until revealed

        using var mcp = new McpProcess(endpoint);
        var init = await mcp.Send("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "E2E agent", version = "1" } });
        Check((string?)init["result"]?["protocolVersion"] == "2025-06-18", "initialize answers with the protocol version");
        var tools = ((JArray)(await mcp.Send("tools/list", new { }))["result"]!["tools"]!).Select(t => (string)t["name"]!).ToList();
        Check(tools.Count == 8 && tools.Contains("typedown_replace_text") && tools.Contains("typedown_close_document"), $"eight tools ({string.Join(", ", tools)})");

        var list = await mcp.Tool("typedown_list_documents", new { });
        Check(!(bool)list["isError"]! && list["structuredContent"]!["documents"]!.Any(d => (string)d["documentId"]! == a), "the document is listed");
        var read = await mcp.Tool("typedown_read_document", new { documentId = a });
        Check((string)read["structuredContent"]!["text"]! == original, "read returns the exact text");
        var revision = (long)read["structuredContent"]!["revision"]!;

        var write = await mcp.Tool("typedown_replace_text", new { documentId = a, baseRevision = revision, find = "brown", replacement = "red", reveal = true, allowFormattingChanges = true });
        Check(!(bool)write["isError"]!, "replace_text succeeds: " + write["content"]?[0]?["text"]);
        // The notice is set after the reply, from the service thread: give it a moment.
        var title = WindowTitle();
        for (var i = 0; i < 40 && !title.Contains("E2E agent (MCP)"); i++) { await Task.Delay(50); title = WindowTitle(); }
        notes.Add("title after the write: " + title);
        Check(title.Contains("E2E agent (MCP)"), "the window title names the agent");
        const string written = "# MC01\n\nThe quick red fox.\n";
        Check(await WaitForPage(driver, a, t => t == written, "the MCP write") == written, "the revealed page shows the write");

        var stale = await mcp.Tool("typedown_replace_text", new { documentId = a, baseRevision = revision, find = "red", replacement = "blue" });
        Check((bool)stale["isError"]! && (string?)stale["structuredContent"]?["error"]?["data"]?["kind"] == "revision_conflict", "a stale revision is a revision_conflict");
        Check(((string?)stale["structuredContent"]?["next"] ?? "").Contains("typedown_read_document"), "the conflict tells the agent to read again");
        Check((string)(await Get(driver, a))["text"]! == written, "the stale write changed nothing");
        Check(mcp.CloseAndWait(), "typedownctl mcp exits when its input closes");
    }

    // Runs a program to its end (at most 60 s), for the client examples shipped next to the driver (e2e/examples).
    private static (int exit, string stdout, string stderr) RunTool(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { try { process.Kill(true); } catch { } throw new CaseFailed($"{file} did not finish"); }
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static async Task EX01(List<string> notes)
    {
        using var driver = await Session("e2e EX01 driver");
        var examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "examples"));
        var a = await Open(driver, Fixture("ex01.md", "# EX01\n\nold text, old news\n"));

        var ps = RunTool("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
            $". '{Path.Combine(examples, "Typedown-Client.ps1")}'; $c = Connect-Typedown '{endpoint}'; " +
            "Initialize-Typedown $c @('document.read','document.write') | Out-Null; " +
            $"$r = Set-TypedownText $c '{a}' 'old' 'new'; $r | ConvertTo-Json -Compress; $c.Pipe.Dispose()");
        notes.Add($"powershell exit {ps.exit}: {ps.stdout.Trim()} {ps.stderr.Trim()}");
        Check(ps.exit == 0 && ps.stdout.Contains("\"revision\""), "the PowerShell example writes");
        Check((string)(await Get(driver, a))["text"]! == "# EX01\n\nnew text, new news\n", "the PowerShell example replaced both occurrences");

        (int exit, string stdout, string stderr) py;
        try { py = RunTool("python", Path.Combine(examples, "typedown_client.py"), "--endpoint", endpoint, "replace-text", a, "news", "notes"); }
        catch (System.ComponentModel.Win32Exception) { notes.Add("no python on this machine: the Python example was not run"); return; }
        notes.Add($"python exit {py.exit}: {py.stdout.Trim()} {py.stderr.Trim()}");
        Check(py.exit == 0, "the Python example writes");
        Check((string)(await Get(driver, a))["text"]! == "# EX01\n\nnew text, new notes\n", "the Python example replaced the word");

        var expected = Brand.Name + ".Automation.v1." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        // The example as an edition ships it: add-cli (Tools/Installer) gives the copy beside the app the edition's endpoint.
        var shipped = Path.Combine(fixtures, "ex01-shipped");
        Directory.CreateDirectory(shipped);
        File.WriteAllText(Path.Combine(shipped, "typedown_client.py"),
            File.ReadAllText(Path.Combine(examples, "typedown_client.py")).Replace("Typedown.Automation.v1", Brand.Name + ".Automation.v1"));
        var name = RunTool("python", "-c", $"import sys; sys.path.insert(0, r'{shipped}'); import typedown_client; print(typedown_client.Client.default_endpoint())");
        Check(name.stdout.Trim() == expected, $"the Python example finds the application's pipe name ({name.stdout.Trim()} {name.stderr.Trim()})");
    }

    private static Task EQ01(List<string> notes)
    {
        var e2e = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var run = RunTool("python", Path.Combine(e2e, "equivalence", "scenario.py"), "--examples", Path.Combine(e2e, "examples"),
            "--endpoint", endpoint, "--workdir", Path.Combine(fixtures, "equivalence"));
        Check(run.exit == 0, "the scenario ran: " + run.stderr);
        notes.Add("TRANSCRIPT " + run.stdout);
        return Task.CompletedTask;
    }

    private static async Task C01(List<string> notes)
    {
        using var c = await Session("e2e C01");
        async Task<List<string>> Ids(string window) => ((JArray)(await c.Call("document.list", new { windowId = window }))["documents"]!).Select(d => (string)d["documentId"]!).ToList();
        var a = await Open(c, Fixture("c01-a.md", "# A\n\nalpha\n"));
        var b = await Open(c, Fixture("c01-b.md", "# B\n\nbeta\n")); // b is shown, a is a background tab
        var window = await WindowIdOf(c, b);

        // 1. A saved background document.
        var closed = await c.Call("document.close", new { documentId = a });
        Check((bool)closed["closed"]! && (string)closed["windowId"]! == window, "a saved background document closes");
        Check(!(await Ids(window)).Contains(a), "and is gone from the list");
        Check(await c.ErrorKind("document.get", new { documentId = a, consistency = "snapshot" }) == "document_not_found", "its id is not found any more");

        // 2. Unsaved: refused, still open, the text as it was.
        var r = await Revision(c, b);
        await c.Call("document.replace", new { documentId = b, baseRevision = r, text = "# B\n\nchanged\n" });
        Check(await c.ErrorKind("document.close", new { documentId = b }) == "unsaved_changes", "an unsaved document is refused");
        Check((await Ids(window)).Contains(b) && (string)(await Get(c, b))["text"]! == "# B\n\nchanged\n", "it stays open with its text");

        // 3. Saved, it closes.
        await c.Call("document.save", new { documentId = b });
        var c2 = await Open(c, Fixture("c01-c.md", "# C\n\ngamma\n"));
        await c.Call("document.focus", new { documentId = b });
        Check(await c.ErrorKind("document.close", new { documentId = b, baseRevision = 0 }) == "revision_conflict", "a stale baseRevision is a conflict");
        await c.Call("document.close", new { documentId = b, baseRevision = await Revision(c, b) });
        Check(!(await Ids(window)).Contains(b), "once saved it closes");

        // 4. A keystroke the page has not reported yet makes the document unsaved: close asks the page first.
        // The page reports a key within milliseconds, so "not reported yet" is made to happen: the page's change
        // reports are held back and go out only just before its answer to a flush - where a late report would arrive.
        // Close finds the key only if it asks the page; without asking it closed the document with the key in it.
        // The page has to show c first (closing b reloads it, and a key typed into the reload is lost).
        await Revision(c, c2);
        var cWindow = await WindowIdOf(c, c2);
        const string hold = @"(() => {
            const wv = window.chrome.webview;
            if (!wv.__send) wv.__send = wv.postMessage.bind(wv);
            window.__held = [];
            wv.postMessage = m => {
                if (typeof m === 'string' && m.includes('""name"":""MarkdownChange""')) { window.__held.push(m); return; }
                if (typeof m === 'string' && m.includes('""name"":""ContentFlushed""')) { window.__held.splice(0).forEach(h => wv.__send(h)); wv.postMessage = wv.__send; }
                return wv.__send(m);
            };
            return wv.postMessage !== wv.__send;
        })()";
        Check((bool)(await c.Call("test.editor.eval", new { windowId = cWindow, script = hold }))["result"]!, "the page's change reports can be held");
        await TypeInto(c, c2, "Z");
        var held = 0;
        for (var i = 0; i < 60 && held == 0; i++)
        {
            if (i > 0) await Task.Delay(50);
            held = (int)(await c.Call("test.editor.eval", new { windowId = cWindow, script = "window.__held.length" }))["result"]!;
        }
        if (held == 0) throw new CaseFailed($"the keystroke never reached the page; screen: {Screenshot("c01-no-keystroke")}");
        var refused = await c.ErrorKind("document.close", new { documentId = c2 });
        notes.Add($"close right after a keystroke: {refused ?? "closed"}");
        Check(refused == "unsaved_changes", "a keystroke not yet reported counts as unsaved");
        Check(((string)(await Get(c, c2))["text"]!).Contains('Z'), "and the keystroke is in the document");
        await c.Call("document.save", new { documentId = c2 });

        // 5. The window's only document: the window stays, with an empty untitled document.
        foreach (var other in (await Ids(window)).Where(id => id != c2).ToList())
        {
            var info = (JObject)(await c.Call("document.get", new { documentId = other, consistency = "snapshot" }));
            if ((bool)info["saved"]!) await c.Call("document.close", new { documentId = other });
            else notes.Add($"left open (unsaved from an earlier case): {(string?)info["path"] ?? "untitled"}");
        }
        if ((await Ids(window)).Count == 1)
        {
            await c.Call("document.close", new { documentId = c2 });
            var left = ((JArray)(await c.Call("document.list", new { windowId = window }))["documents"]!);
            Check(left.Count == 1 && left[0]!["path"]!.Type == JTokenType.Null, $"the window stays with one empty untitled document ({left.ToString(Formatting.None)})");
        }
        else notes.Add("other unsaved documents in the window: the last-document part is not reached here");
    }

    private static async Task Q02(List<string> notes)
    {
        using var host = Process.GetProcessById(hostPid);
        using var c = await Session("e2e Q02");
        var opened = (string)(await c.Call("test.window.open"))["windowId"]!;
        var window = await WindowOf(opened, c);
        await Task.Delay(200);
        PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
        for (var i = 0; i < 10 && !host.HasExited; i++) await Task.Delay(500);
        Check(!host.HasExited, "the process lives on after a window was closed while its editor was still being created");
        using var again = await Session("e2e Q02 after");
        var left = ((JArray)(await again.Call("window.list"))["windows"]!).Count;
        Check(left == 1, $"one window left ({left})");
    }

    private static async Task Q01(List<string> notes)
    {
        // A host of its own, in a fresh folder: the cases before leave unsaved documents, and closing a window then
        // stops at the save question. This one has nothing unsaved. It is the last case: the suite's host goes.
        using (var previous = Process.GetProcessById(hostPid)) { previous.Kill(); previous.WaitForExit(10000); }
        var root = testRoot + "-q01";
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var started = Process.Start(new ProcessStartInfo(hostExe, $"--automation-test-root \"{root}\"") { UseShellExecute = true })!;
        endpoint = await ReadEndpointFile(Path.Combine(root, "automation-endpoint.txt")) ?? throw new CaseFailed("the test host never published its endpoint");
        hostPid = started.Id;
        testRoot = root;
        await Task.Delay(3000);
        using var host = Process.GetProcessById(hostPid);
        using (var c = await Session("e2e Q01"))
        {
            var windows = ((JArray)(await c.Call("window.list"))["windows"]!).Select(w => (string)w["windowId"]!).ToList();
            if (windows.Count < 2) windows.Add((string)(await c.Call("test.window.open"))["windowId"]!);
            notes.Add($"{windows.Count} window(s)");
            var first = await WindowOf(windows[0], c);
            var second = await WindowOf(windows[1], c);
            await Task.Delay(8000); // both editors loaded: a window closed while its web view is created is Q02
            // XamlWindow.AllWindows lists a window until the garbage collector has finalized it: the first window is
            // kept in memory after it closes, as it was, minutes later, for the reader who saw the process stay.
            await c.Call("test.window.keep", new { windowId = windows[0] });
            // Closed the way the close button closes a window (nothing unsaved here, so no question).
            PostMessage(first, 0x0010, IntPtr.Zero, IntPtr.Zero);
            // Diagnosis: second by second, is the process alive, how many main windows are shown, does a fresh
            // connection get an answer (and the old one?).
            for (var i = 1; i <= 10; i++)
            {
                await Task.Delay(1000);
                string fresh;
                try
                {
                    using var probe = await Session($"e2e Q01 probe {i}").WaitAsync(TimeSpan.FromSeconds(4));
                    fresh = $"{((JArray)(await probe.Call("window.list").WaitAsync(TimeSpan.FromSeconds(4)))["windows"]!).Count} window(s) listed";
                }
                catch (Exception e) { fresh = e.GetType().Name; }
                string old;
                try { old = $"{((JArray)(await c.Call("window.list").WaitAsync(TimeSpan.FromSeconds(4)))["windows"]!).Count} listed"; }
                catch (Exception e) { old = e.GetType().Name; }
                notes.Add($"t+{i}s: exited={host.HasExited} first window alive={IsWindow(first)} visible={IsWindowVisible(first)} second alive={IsWindow(second)}; new connection: {fresh}; old connection: {old}");
                if (i == 2) notes.Add("screen: " + Screenshot("q01-after-first-close"));
                if (host.HasExited) break;
            }
            await Task.Delay(1000); // the log is written in batches
            try
            {
                using var reader = new StreamReader(new FileStream(Path.Combine(testRoot, "logs", "debug.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                foreach (var line in reader.ReadToEnd().Split('\n').Where(l => (l.Contains("window") || l.Contains("automation")) && !l.StartsWith("   at ") && !l.Contains("FileDropTarget")).TakeLast(12))
                    notes.Add("log: " + line.Trim());
            }
            catch (Exception e) { notes.Add("no log: " + e.Message); }
            Check(!host.HasExited, "the process stays while a window is open");
            List<string> left;
            try { left = ((JArray)(await c.Call("window.list"))["windows"]!).Select(w => (string)w["windowId"]!).ToList(); }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException)
            {
                notes.Add($"the connection broke after the first close ({e.Message})");
                throw;
            }
            Check(left.Count == 1 && left[0] == windows[1], $"one window left ({string.Join(", ", left)})");
            PostMessage(second, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }
        var exited = host.WaitForExit(20000);
        if (exited) notes.Add("the process ended");
        else { host.Kill(); host.WaitForExit(10000); }
        try { await Task.Delay(1000); Directory.Delete(testRoot, true); } catch (Exception e) { notes.Add("the folder stays: " + e.Message); }
        Check(exited, "the process exits once its last window is closed (it stayed running with no window)");
    }

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    private static async Task R04(List<string> notes)
    {
        string u1, u2, t;
        var titled = Fixture("r04-t.md", "# T\n\nsaved\n");
        using (var c = await Session("e2e R04 before"))
        {
            var created = await c.Call("document.create", new { text = "# U1\n\nfirst untitled\n", reveal = "document", normalizationPolicy = "allowUnknown" });
            u1 = (string)created["documentId"]!;
            u2 = (string)(await c.Call("document.create", new { text = "# U2\n\nsecond untitled\n", reveal = "document", normalizationPolicy = "allowUnknown" }))["documentId"]!;
            t = await Open(c, titled);
            await c.Call("document.replace", new { documentId = t, baseRevision = await Revision(c, t), text = "# T\n\nedited, not saved\n", normalizationPolicy = "allowUnknown" });
            await c.Call("document.focus", new { documentId = u2 }); // u1 and t are background tabs now
        }
        // The backups exist, with each document's own text, before the host dies.
        var backup = Path.Combine(testRoot, "data", "Backup");
        string Untitled(string id) => Path.Combine(backup, $"untitled_{id}.md");
        using (var c = await Session("e2e R04 backup"))
        {
            // One backup pass now, as the save timer would do within five seconds.
            var pass = await c.Call("test.backup.run");
            Check(((JArray)pass["windows"]!).All(w => (bool?)w["ok"] == true), "the backup pass succeeded in every window: " + pass.ToString(Formatting.None));
        }
        notes.Add($"u1={u1} u2={u2} t={t}");
        Check(File.Exists(Untitled(u1)) && Disk(Untitled(u1)) == "# U1\n\nfirst untitled\n", $"the first untitled document (a background tab) has its own backup ({(File.Exists(Untitled(u1)) ? JsonConvert.SerializeObject(Disk(Untitled(u1))) : "missing")})");
        Check(File.Exists(Untitled(u2)) && Disk(Untitled(u2)) == "# U2\n\nsecond untitled\n", "the second untitled document has its own backup");
        var titledBackup = Directory.GetFiles(backup, "*_r04-t.md").FirstOrDefault();
        Check(titledBackup != null && Disk(titledBackup) == "# T\n\nedited, not saved\n", "the background titled document has its backup");

        notes.Add("backups checked");
        // Killed, not closed: nothing gets to save or clean up.
        using (var dying = Process.GetProcessById(hostPid))
        {
            dying.Kill();
            dying.WaitForExit(10000);
        }
        notes.Add("killed");
        File.Delete(Path.Combine(testRoot, "automation-endpoint.txt"));
        // Shell-started: a child started directly would inherit this driver's redirected output, and the script
        // running the driver would then wait for the test host to exit before it could finish.
        var restarted = Process.Start(new ProcessStartInfo(hostExe, $"--automation-test-root \"{testRoot}\"") { UseShellExecute = true })!;
        hostPid = restarted.Id;
        notes.Add($"restarted pid {hostPid}");
        var restartedEndpoint = await ReadEndpointFile(Path.Combine(testRoot, "automation-endpoint.txt")) ?? throw new CaseFailed("the restarted host never published its endpoint");
        Check(restartedEndpoint == endpoint, $"the restarted host listens on the same endpoint as before ({restartedEndpoint} vs {endpoint})");
        var listenError = Path.Combine(testRoot, "automation-endpoint-error.txt");
        Client? afterClient = null;
        for (var i = 0; i < 30 && afterClient == null; i++)
        {
            if (File.Exists(listenError)) throw new CaseFailed("the restarted host could not listen: " + File.ReadAllText(listenError));
            try { afterClient = await Connect(); }
            catch (TimeoutException) { }
        }
        if (afterClient == null) throw new CaseFailed("the restarted host never accepted a connection");
        using var after = afterClient;
        notes.Add("connected");
        await Initialize(after, "e2e R04 after");
        await WaitForWindow(after);
        notes.Add("window up");
        // The recovery question: answered as a person would, with Enter (Recover is the default button).
        JArray documents = new();
        for (var i = 0; i < 60; i++)
        {
            documents = (JArray)(await after.Call("document.list"))["documents"]!;
            if (documents.Any(d => (string)d["documentId"]! == u1)) break;
            var sw = Stopwatch.StartNew();
            var answered = AnswerDialog();
            notes.Add($"dialog attempt {i}: {(answered ? "answered" : lastDialogProblem[..Math.Min(300, lastDialogProblem.Length)])} ({sw.ElapsedMilliseconds} ms)");
            await Task.Delay(250);
        }
        if (!documents.Any(d => (string)d["documentId"]! == u1)) throw new CaseFailed("the recovery question was not answered: " + lastDialogProblem);
        async Task<string> TextOf(string id) => (string)(await Get(after, id))["text"]!;
        Check(documents.Any(d => (string)d["documentId"]! == u1) && documents.Any(d => (string)d["documentId"]! == u2), "both untitled documents came back with their ids");
        Check(await TextOf(u1) == "# U1\n\nfirst untitled\n", "the first untitled document has its own text");
        Check(await TextOf(u2) == "# U2\n\nsecond untitled\n", "the second untitled document has its own text");
        // The titled one comes back when opened again (its backup is matched by path) - the open asks, Enter recovers.
        var reopening = after.Call("document.open", new { path = titled, reveal = "document" });
        for (var i = 0; i < 60 && !reopening.IsCompleted; i++) { AnswerDialog(); await Task.Delay(250); }
        var reopened = (string)(await reopening)["documentId"]!;
        Check(await TextOf(reopened) == "# T\n\nedited, not saved\n", "the titled document recovers its unsaved text");
        Check(Disk(titled) == "# T\n\nsaved\n", "and its file was never written");
        notes.Add($"recovered {u1[..6]} {u2[..6]} and {Path.GetFileName(titled)}");
    }

    /// <summary>
    /// Answers the app's dialog with its primary button, through UI Automation - the button a person would press.
    /// (A key press does not reach a XAML dialog while keyboard focus is outside the XAML island.) True when a dialog
    /// was answered.
    /// </summary>
    private static string lastDialogProblem = "";

    private static bool AnswerDialog()
    {
        var window = MainWindow();
        if (window == IntPtr.Zero) { lastDialogProblem = "no main window"; return false; }
        try
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(window);
            var button = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "PrimaryButton"));
            if (button == null) { lastDialogProblem = "no PrimaryButton under " + window + ": " + Describe(root, 0); return false; }
            ((System.Windows.Automation.InvokePattern)button.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
            return true;
        }
        catch (Exception e)
        {
            lastDialogProblem = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    /// <summary>A short outline of the UI Automation tree, for a failure message.</summary>
    private static string Describe(System.Windows.Automation.AutomationElement element, int depth)
    {
        if (depth > 6) return "";
        var c = element.Current;
        var text = $"[{c.ControlType.ProgrammaticName.Replace("ControlType.", "")} '{c.Name}' id={c.AutomationId} cls={c.ClassName}";
        var children = element.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        foreach (System.Windows.Automation.AutomationElement child in children) text += " " + Describe(child, depth + 1);
        return text + "]";
    }

    /// <summary>
    /// Brings the window to the front. SetForegroundWindow is refused when another process holds the foreground; a
    /// real click on the window's title strip activates it the way a person would.
    /// </summary>
    private static async Task Activate(IntPtr window)
    {
        AllowSetForegroundWindow(hostPid);
        SetForegroundWindow(window);
        for (var i = 0; i < 20 && GetForegroundWindow() != window; i++) await Task.Delay(25);
        if (GetForegroundWindow() == window) return;
        GetWindowRect(window, out var rect);
        SetCursorPos(rect.Left + 120, rect.Top + 12);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } }, new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
        for (var i = 0; i < 40 && GetForegroundWindow() != window; i++) await Task.Delay(25);
    }

    // ---- real input ----

    private static IntPtr MainWindow() => Process.GetProcessById(hostPid).MainWindowHandle;

    private static string WindowTitle()
    {
        var sb = new StringBuilder(512);
        GetWindowText(MainWindow(), sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Real keystrokes into the window that shows the document (not the process's first window: with two windows the
    /// document may be in the other one). The caller waits for them to reach the page (WaitForPage).
    /// </summary>
    private static async Task TypeInto(Client c, string documentId, string text)
    {
        var windowId = (string)(await c.Call("document.focus", new { documentId }))["windowId"]!;
        await TypeIntoWindow(c, windowId, text);
    }

    private static async Task TypeIntoWindow(Client c, string windowId, string text)
    {
        var window = await WindowOf(windowId, c);
        await Activate(window);
        if (GetForegroundWindow() != window) throw new CaseFailed("the test host window could not be brought to the front (is the desktop locked?)");
        // Keyboard focus into the editor as a person gives it: a click in the text area, then Ctrl+End.
        GetWindowRect(window, out var rect);
        // Keyboard focus into the editor page as the app gives it, not by a click: what lies under a click depends on
        // the document (an image block takes the click, and then the keys). Ctrl+End then puts the caret at the end.
        await c.Call("test.editor.focus", new { windowId });
        await Task.Delay(150); // focus hand-over settling; whether the keys arrived is checked by the caller
        Send(Key(0x11, false), Key(0x23, false), Key(0x23, true), Key(0x11, true));
        var inputs = new List<INPUT>();
        foreach (var ch in text)
        {
            inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 } } });
            inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = 0x0004 | 0x0002 } } });
        }
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) throw new CaseFailed($"SendInput sent {sent} of {inputs.Count} events (is the desktop locked?)");
    }

    private static INPUT Key(ushort vk, bool up) => new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 0x0002u : 0u } } };

    private static void Send(params INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length) throw new CaseFailed("SendInput was refused (is the desktop locked?)");
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    /// <summary>Waits until the page itself holds text satisfying the condition (test.editor.pageText), bounded.</summary>
    private static async Task<string> WaitForPage(Client c, string documentId, Func<string, bool> condition, string what)
    {
        string? text = null;
        for (var i = 0; i < 100; i++)
        {
            text = (string?)(await c.Call("test.editor.pageText", new { documentId }))["text"];
            if (text != null && condition(text)) return text;
            await Task.Delay(50);
        }
        throw new CaseFailed($"{what} never reached the page ({JsonConvert.SerializeObject(text)}); screen: {Screenshot("no-keystroke-" + DateTime.Now.ToString("HHmmss"))}");
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
}
