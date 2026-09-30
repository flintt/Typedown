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

        public void Dispose() { Connection.Dispose(); pipe.Dispose(); }
    }

    private static readonly List<JObject> results = new();
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
        var started = DateTime.UtcNow;
        var environmentError = (string?)null;
        try
        {
            var endpointFile = Path.Combine(root, "automation-endpoint.txt");
            for (var i = 0; i < 300 && !File.Exists(endpointFile); i++) await Task.Delay(100); // the host starting up
            if (!File.Exists(endpointFile)) throw new InvalidOperationException("the test host never published its endpoint");
            endpoint = File.ReadAllText(endpointFile).Trim();

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
            await Case("B02 an open settings page shows a font size changed from elsewhere", B02);
            await Case("F01 a written document with protected payloads keeps every one of them through a real first keystroke", F01);
            await Case("M01 a written text survives visual, reading and source mode switches byte for byte, then saves exactly", M01);
            await Case("R04 untitled and background documents come back from their backups after a kill, each its own", R04);
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

    private static readonly string[] AllScopes = { Scopes.AppRead, Scopes.DocumentRead, Scopes.DocumentWrite, Scopes.DocumentSave, Scopes.WindowFocus, Scopes.SettingsRead, Scopes.SettingsWrite };

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
        await TypeIntoWindow("Z", await WindowOf((string)(await driver.Call("document.get", new { documentId = id, consistency = "snapshot" }))["windowId"]!, driver)); // the reader's keystroke on top of the write, while the host has not committed it
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
        notes.Add($"{windows.Count} window(s), font {font}");
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
            await c.Call("settings.set", new { key = "editor.fontSize", value = font, baseSettingsRevision = (long)got["settingsRevision"]! });
            await WaitForNumberBox(await windowHandle, font, notes, "the open settings page follows a change made elsewhere");
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
        for (var i = 0; i < 300 && !File.Exists(Path.Combine(testRoot, "automation-endpoint.txt")); i++) await Task.Delay(100);
        var restartedEndpoint = File.ReadAllText(Path.Combine(testRoot, "automation-endpoint.txt")).Trim();
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
        await TypeIntoWindow(text, await WindowOf(windowId, c));
    }

    private static async Task TypeIntoWindow(string text, IntPtr window = default)
    {
        if (window == IntPtr.Zero) window = MainWindow();
        await Activate(window);
        if (GetForegroundWindow() != window) throw new CaseFailed("the test host window could not be brought to the front (is the desktop locked?)");
        // Keyboard focus into the editor as a person gives it: a click in the text area, then Ctrl+End.
        GetWindowRect(window, out var rect);
        // Towards the right of the text area: the left margin holds Muya's block menu button, which a click opens and
        // which then takes the keys. Escape closes whatever a click may have opened; Ctrl+End puts the caret at the end.
        SetCursorPos(rect.Right - 80, rect.Top + (rect.Bottom - rect.Top) * 2 / 5);
        Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } }, new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });
        await Task.Delay(200); // the click's caret placement settles before the keys; the result is still checked below
        Send(Key(0x1B, false), Key(0x1B, true));
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
        throw new CaseFailed($"{what} never reached the page ({JsonConvert.SerializeObject(text)})");
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
}
