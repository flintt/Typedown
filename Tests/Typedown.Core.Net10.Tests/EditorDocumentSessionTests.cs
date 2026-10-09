using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Typedown.Contracts.Editor;
using Typedown.Contracts.Platform;
using Typedown.Core.Editor;
using Typedown.Core.Services;

namespace Typedown.Core.Net10.Tests;

[TestClass]
public sealed class EditorDocumentSessionTests
{
    [TestMethod]
    public async Task GetSettingsUsesTheOpenedDocumentAndStoredEditorOptions()
    {
        using var files = new TemporaryFiles();
        var document = files.WriteBytes(
            "opened.md",
            new UTF8Encoding(true).GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("# Opened\r\nsecond\r\n"))
                .ToArray());
        File.WriteAllText(
            files.Paths.SettingsFilePath,
            "{\"SourceCode\":true,\"FontSize\":19.0}");
        var bridge = new FakeEditorBridge();
        using var session = new EditorDocumentSession(
            bridge,
            new JsonSettingsStore(files.Paths.SettingsFilePath),
            files.Paths);

        await session.OpenAsync(document);
        bridge.Receive(new
        {
            type = "invoke",
            id = "invoke_0",
            name = "GetSettings",
            args = (object?)null,
        });

        var response = await bridge.WaitForPostedAsync(message =>
            message.Value<string>("name") == "invoke_0");
        var reply = response["args"]!;
        Assert.AreEqual(0, reply.Value<int>("code"));
        var settings = reply["data"]!;
        Assert.AreEqual("# Opened\nsecond\n", settings.Value<string>("markdown"));
        Assert.AreEqual(Path.GetDirectoryName(document), settings.Value<string>("basePath"));
        Assert.IsTrue(settings.Value<bool>("sourceCode"));
        Assert.AreEqual(19d, settings.Value<double>("fontSize"));
        Assert.AreEqual(1, settings.Value<int>("loadId"));
    }

    [TestMethod]
    public async Task SaveFlushesThePageAndPreservesBomAndLineEndings()
    {
        using var files = new TemporaryFiles();
        var preamble = new UTF8Encoding(true).GetPreamble();
        var document = files.WriteBytes(
            "preserve.md",
            preamble.Concat(Encoding.UTF8.GetBytes("first\r\nline\r\n")).ToArray());
        var bridge = new FakeEditorBridge
        {
            FlushedText = "# Changed\nsecond\n",
        };
        using var session = new EditorDocumentSession(
            bridge,
            new JsonSettingsStore(files.Paths.SettingsFilePath),
            files.Paths);

        await session.OpenAsync(document);
        bridge.Receive(new
        {
            type = "invoke",
            id = "invoke_0",
            name = "GetSettings",
            args = (object?)null,
        });
        await bridge.WaitForPostedAsync(message =>
            message.Value<string>("name") == "invoke_0");
        bridge.ReceiveDiffMessage(
            "MarkdownChange",
            new { text = "# Pending\n", loadId = 1 });
        await bridge.WaitUntilAsync(() => session.IsDirty);

        Assert.IsTrue(await session.SaveAsync());

        var expected = preamble
            .Concat(Encoding.UTF8.GetBytes("# Changed\r\nsecond\r\n"))
            .ToArray();
        CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(document));
        Assert.AreEqual("# Changed\nsecond\n", session.Text);
        Assert.IsFalse(session.IsDirty);
    }

    [TestMethod]
    public async Task DiffWithoutAPageBaselineCannotReplaceDocumentText()
    {
        using var files = new TemporaryFiles();
        var document = files.WriteBytes("safe.md", Encoding.UTF8.GetBytes("keep me\n"));
        var bridge = new FakeEditorBridge();
        using var session = new EditorDocumentSession(
            bridge,
            new JsonSettingsStore(files.Paths.SettingsFilePath),
            files.Paths);
        await session.OpenAsync(document);

        bridge.Receive(new
        {
            type = "diffmsg",
            diff = true,
            name = "MarkdownChange",
            args = "malicious",
            start = 0,
            end = 0,
        });
        await Task.Delay(50);

        Assert.AreEqual("keep me\n", session.Text);
        Assert.IsFalse(session.IsDirty);
    }

    private sealed class FakeEditorBridge : IMarkdownEditorBridge
    {
        private readonly List<JObject> posted = new();

        public EditorBridgeState State => EditorBridgeState.Ready;
        public string? FlushedText { get; init; }

        public event EventHandler<EditorRawMessageReceivedEventArgs>? RawMessageReceived;
        public event EventHandler<EditorBridgeStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public bool TryPostJson(string json)
        {
            var message = JObject.Parse(json);
            lock (posted) posted.Add(message);
            if (message.Value<string>("name") == "FlushContent" && FlushedText is not null)
            {
                var token = message["args"]!.Value<int>("token");
                ReceiveDiffMessage("ContentFlushed", new { token, text = FlushedText });
            }
            return true;
        }

        public void Receive(object message) =>
            RawMessageReceived?.Invoke(
                this,
                new EditorRawMessageReceivedEventArgs(JsonConvert.SerializeObject(message)));

        public void ReceiveDiffMessage(string name, object args) => Receive(new
        {
            type = "diffmsg",
            diff = false,
            name,
            args = JsonConvert.SerializeObject(args),
        });

        public async Task<JObject> WaitForPostedAsync(
            Func<JObject, bool> predicate,
            int timeoutMilliseconds = 2000)
        {
            JObject? result = null;
            await WaitUntilAsync(() =>
            {
                lock (posted) result = posted.FirstOrDefault(predicate);
                return result is not null;
            }, timeoutMilliseconds);
            return result!;
        }

        public async Task WaitUntilAsync(
            Func<bool> predicate,
            int timeoutMilliseconds = 2000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline)
                    Assert.Fail("Timed out waiting for the editor session.");
                await Task.Delay(10);
            }
        }

        public void Focus() { }
        public void Dispose() { }
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "typedown-core-net10-tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryFiles()
        {
            Directory.CreateDirectory(root);
            Paths = new TestPathProvider(root);
        }

        public TestPathProvider Paths { get; }

        public string WriteBytes(string name, byte[] bytes)
        {
            var path = Path.Combine(root, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private sealed class TestPathProvider : IAppDataPathProvider
    {
        public TestPathProvider(string root)
        {
            PersistentDataDirectory = root;
            CacheDirectory = Path.Combine(root, "cache");
            SettingsFilePath = Path.Combine(root, "Settings.json");
            DatabaseFilePath = Path.Combine(root, "Storage.db");
            BackupDirectory = Path.Combine(root, "Backup");
            ThemesDirectory = Path.Combine(root, "themes");
            SessionFilePath = Path.Combine(root, "session.json");
            CursorFilePath = Path.Combine(root, "cursors.json");
            ImageUploadHistoryFilePath = Path.Combine(root, "ImageUploadHistory.json");
            HedgeDocSharesFilePath = Path.Combine(root, "hedgedoc-shares.json");
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(BackupDirectory);
            Directory.CreateDirectory(ThemesDirectory);
        }

        public string PersistentDataDirectory { get; }
        public string CacheDirectory { get; }
        public string SettingsFilePath { get; }
        public string DatabaseFilePath { get; }
        public string BackupDirectory { get; }
        public string ThemesDirectory { get; }
        public string SessionFilePath { get; }
        public string CursorFilePath { get; }
        public string ImageUploadHistoryFilePath { get; }
        public string HedgeDocSharesFilePath { get; }
    }
}
