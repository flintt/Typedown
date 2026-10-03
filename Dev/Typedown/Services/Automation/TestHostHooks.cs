using System.Linq;
using Typedown.Automation;

namespace Typedown.Services.Automation
{
    /// <summary>
    /// What only the automation test host adds: the edit barriers and the test.* methods (Typedown.Automation.TestHost).
    /// The application build compiles the empty half and never references that assembly; packaging refuses any
    /// output that contains it (Tools/Installer/assert-application-build.ps1).
    /// </summary>
    internal static class TestHostHooks
    {
#if AUTOMATION_TEST_HOST
        private static readonly Typedown.Automation.TestHost.EditBarriers barriers = new();

        public static IEditBarriers Barriers => barriers;

        private static readonly System.Collections.Generic.List<object> keptWindows = new();

        public static void AddMethods(MethodTable methods)
        {
            barriers.AddMethods(methods);
            // What the page itself holds right now - lets a test wait for real input to arrive instead of sleeping.
            methods.Add(new MethodDescriptor("test.editor.pageText", null, "test.editor.pageText/1", async (c, ct) =>
            {
                var documentId = c.Params.RequiredString("documentId", allowEmpty: false);
                var found = await Core.Services.AutomationWindows.FindDocumentAsync(documentId)
                    ?? throw new AutomationException(AutomationErrorKind.document_not_found, "no such document");
                var text = await await Core.Services.AutomationWindows.Registry.OnWindowAsync(found.Window.WindowId, app =>
                    app.TabsViewModel.ActiveTab == found.Tab ? app.EditorViewModel.ReadPageTextAsync(5000) : System.Threading.Tasks.Task.FromResult<string>(null));
                return new Newtonsoft.Json.Linq.JObject { ["text"] = text };
            }));

            // The window's UI thread runs every callback posted to it, from any number of threads at once. Its dispatcher
            // numbered posts with a plain increment: two threads could take the same number, and one callback was
            // dropped - an await continuation that never ran (E2E C01 hung a minute with the UI thread idle).
            methods.Add(new MethodDescriptor("test.dispatcher.stress", null, "test.dispatcher.stress/1", async (c, ct) =>
            {
                var windowId = c.Params.RequiredString("windowId", allowEmpty: false);
                var threads = (int)(c.Params.OptionalInteger("threads") ?? 8);
                var posts = (int)(c.Params.OptionalInteger("posts") ?? 20000);
                var context = await Core.Services.AutomationWindows.Registry.OnWindowAsync(windowId, app => System.Threading.SynchronizationContext.Current);
                var ran = 0;
                var start = new System.Threading.ManualResetEventSlim();
                var senders = Enumerable.Range(0, threads).Select(_ => System.Threading.Tasks.Task.Run(() =>
                {
                    start.Wait();
                    for (var i = 0; i < posts; i++) context.Post(__ => System.Threading.Interlocked.Increment(ref ran), null);
                })).ToArray();
                start.Set();
                await System.Threading.Tasks.Task.WhenAll(senders);
                var total = threads * posts;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (System.Threading.Volatile.Read(ref ran) < total && clock.ElapsedMilliseconds < 20000) await System.Threading.Tasks.Task.Delay(50);
                return new Newtonsoft.Json.Linq.JObject { ["posted"] = total, ["ran"] = System.Threading.Volatile.Read(ref ran), ["context"] = context?.GetType().FullName };
            }));

            // Backups: one pass now in every window, and what the backup folder holds afterwards.
            methods.Add(new MethodDescriptor("test.backup.run", null, "test.backup.run/1", async (c, ct) =>
            {
                var report = new Newtonsoft.Json.Linq.JArray();
                foreach (var window in Core.Services.AutomationWindows.Registry.Snapshot())
                {
                    var entry = await await Core.Services.AutomationWindows.Registry.OnWindowAsync(window.WindowId, async app =>
                    {
                        var busy = app.FileViewModel.SaveTimerBusy;
                        try { return new Newtonsoft.Json.Linq.JObject { ["windowId"] = window.WindowId, ["ok"] = await app.FileViewModel.BackupNowAsync(), ["timerBusy"] = busy }; }
                        catch (System.Exception e) { return new Newtonsoft.Json.Linq.JObject { ["windowId"] = window.WindowId, ["error"] = e.ToString(), ["timerBusy"] = busy }; }
                    });
                    report.Add(entry);
                }
                var folder = System.IO.Path.Combine(Core.Config.GetLocalFolderPath(), "Backup");
                var files = System.IO.Directory.Exists(folder) ? System.IO.Directory.GetFiles(folder).Select(System.IO.Path.GetFileName).ToArray() : new string[0];
                return new Newtonsoft.Json.Linq.JObject { ["windows"] = report, ["files"] = new Newtonsoft.Json.Linq.JArray(files) };
            }));

            // How a window's editor page is drawn, and moving a window to a page (the settings page) of its own.
            methods.Add(new MethodDescriptor("test.editor.style", null, "test.editor.style/1", async (c, ct) =>
                await await Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), async app =>
                {
                    if (app.MarkdownEditor == null) throw new AutomationException(AutomationErrorKind.editor_not_ready, "the window has no editor page (another page is shown)");
                    return await app.EditorViewModel.QueryEditorStyleAsync(5000)
                        ?? throw new AutomationException(AutomationErrorKind.editor_not_ready, "the editor page did not answer within 5 s");
                })));
            // Keyboard focus into the window's editor page, the way the app itself gives it (the editor control's
            // focus is handed on to the web view) - independent of what a click would land on.
            methods.Add(new MethodDescriptor("test.editor.focus", null, "test.editor.focus/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    var editor = app.MarkdownEditor as global::Windows.UI.Xaml.Controls.Control
                        ?? throw new AutomationException(AutomationErrorKind.editor_not_ready, "the window shows no editor page");
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["focused"] = editor.Focus(global::Windows.UI.Xaml.FocusState.Programmatic) };
                })));
            // Runs a script in the window's editor page and returns its JSON result: for diagnosing what the page holds
            // (caret, Muya's state) when a check fails in the real window and not in the page harness.
            methods.Add(new MethodDescriptor("test.editor.eval", null, "test.editor.eval/1", async (c, ct) =>
                await await Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), async app =>
                {
                    var editor = app.MarkdownEditor as Typedown.Controls.MarkdownEditor
                        ?? throw new AutomationException(AutomationErrorKind.editor_not_ready, "the window shows no editor page");
                    var json = await editor.CoreWebView2.ExecuteScriptAsync(c.Params.RequiredString("script"));
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["result"] = Newtonsoft.Json.Linq.JToken.Parse(json) };
                })));
            // Reloads the window's editor page, as the application does after a page error or a crashed web process.
            methods.Add(new MethodDescriptor("test.editor.reload", null, "test.editor.reload/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    var editor = app.MarkdownEditor as Typedown.Controls.MarkdownEditor
                        ?? throw new AutomationException(AutomationErrorKind.editor_not_ready, "the window shows no editor page");
                    editor.CoreWebView2.Reload();
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject();
                })));
            methods.Add(new MethodDescriptor("test.window.handle", null, "test.window.handle/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                    (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["hwnd"] = app.MainWindow.ToInt64() })));
            // The titles of the window's menu bar, as drawn: the menu follows the interface language.
            methods.Add(new MethodDescriptor("test.window.menuTitles", null, "test.window.menuTitles/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    var titles = new Newtonsoft.Json.Linq.JArray();
                    void Walk(global::Windows.UI.Xaml.DependencyObject node)
                    {
                        // The menus shown: Paragraph and Format are hidden in source mode.
                        if (node is global::Microsoft.UI.Xaml.Controls.MenuBarItem item && item.Visibility == global::Windows.UI.Xaml.Visibility.Visible) titles.Add(item.Title);
                        var count = global::Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
                        for (var i = 0; i < count; i++) Walk(global::Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
                    }
                    if (app.XamlRoot?.Content is global::Windows.UI.Xaml.DependencyObject root) Walk(root);
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["titles"] = titles };
                })));
            // Where the main page starts in its window, and whether the window is in full screen: in full screen nothing may
            // sit above it (a 4 px row of window background did, along the top of the screen).
            methods.Add(new MethodDescriptor("test.window.layout", null, "test.window.layout/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    var root = app.XamlRoot?.Content as global::Windows.UI.Xaml.UIElement;
                    global::Windows.UI.Xaml.FrameworkElement? page = null;
                    void Walk(global::Windows.UI.Xaml.DependencyObject node)
                    {
                        if (page != null) return;
                        if (node is Core.Pages.MainPage found) { page = found; return; }
                        var count = global::Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
                        for (var i = 0; i < count; i++) Walk(global::Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
                    }
                    if (root != null) Walk(root);
                    var top = page != null && root != null ? page.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y : double.NaN;
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject
                    {
                        ["fullScreen"] = app.UIViewModel.IsFullScreen,
                        ["mainPageTop"] = top,
                        ["rootHeight"] = (root as global::Windows.UI.Xaml.FrameworkElement)?.ActualHeight ?? double.NaN,
                    };
                })));
            // Holds a window in memory from now on, as anything still referring to it does after it closes:
            // XamlWindow.AllWindows lists a window until the garbage collector has finalized it, closed or not.
            methods.Add(new MethodDescriptor("test.window.keep", null, "test.window.keep/1", (c, ct) =>
                Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    var window = global::Typedown.XamlUI.XamlWindow.AllWindows.OfType<Typedown.Windows.MainWindow>().FirstOrDefault(w => w.Handle == app.MainWindow)
                        ?? throw new AutomationException(AutomationErrorKind.window_not_found, "no main window with that handle");
                    keptWindows.Add(window);
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject();
                })));
            methods.Add(new MethodDescriptor("test.window.navigate", null, "test.window.navigate/1", (c, ct) =>
            {
                var route = c.Params.RequiredString("route", allowEmpty: false);
                return Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), app =>
                {
                    app.NavigateCommand.Execute(route);
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject();
                });
            }));

            // Settings across windows (stage 0b): open another window, change a setting in one, read it in another.
            methods.Add(new MethodDescriptor("test.window.open", null, "test.window.open/1", async (c, ct) =>
            {
                var before = Core.Services.AutomationWindows.Registry.Snapshot().Select(w => w.WindowId).ToList();
                var first = Core.Services.AutomationWindows.Registry.Snapshot().First();
                await Core.Services.AutomationWindows.Registry.OnWindowAsync(first.WindowId, _ => Utilities.Common.OpenNewWindow(new string[0], forceNewWindow: true));
                for (var i = 0; i < 200; i++)
                {
                    var added = Core.Services.AutomationWindows.Registry.Snapshot().FirstOrDefault(w => !before.Contains(w.WindowId));
                    if (added != null) return new Newtonsoft.Json.Linq.JObject { ["windowId"] = added.WindowId };
                    await System.Threading.Tasks.Task.Delay(50, ct);
                }
                throw new AutomationException(AutomationErrorKind.editor_not_ready, "no new window registered");
            }));
            methods.Add(new MethodDescriptor("test.settings.set", null, "test.settings.set/1", (c, ct) =>
            {
                var p = c.Params;
                var name = p.RequiredString("name", allowEmpty: false);
                var value = (c.Request.Params as Newtonsoft.Json.Linq.JObject)?["value"];
                return Core.Services.AutomationWindows.Registry.OnWindowAsync(p.RequiredString("windowId"), app =>
                {
                    var property = app.SettingsViewModel.GetType().GetProperty(name) ?? throw Params.Invalid("name", "unknown");
                    property.SetValue(app.SettingsViewModel, value!.ToObject(property.PropertyType));
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject();
                });
            }));
            methods.Add(new MethodDescriptor("test.settings.get", null, "test.settings.get/1", (c, ct) =>
            {
                var p = c.Params;
                var name = p.RequiredString("name", allowEmpty: false);
                return Core.Services.AutomationWindows.Registry.OnWindowAsync(p.RequiredString("windowId"), app =>
                {
                    var property = app.SettingsViewModel.GetType().GetProperty(name) ?? throw Params.Invalid("name", "unknown");
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["value"] = Newtonsoft.Json.Linq.JToken.FromObject(property.GetValue(app.SettingsViewModel)) };
                });
            }));

            // Image upload without its dialogs: an enabled configuration chosen as Settings > Image > Upload with, and
            // File > Upload local images on the window's active document.
            methods.Add(new MethodDescriptor("test.images.configure", null, "test.images.configure/1", async (c, ct) =>
            {
                var p = c.Params;
                var method = p.OptionalEnum("method", "powershell", "powershell", "s3");
                var settings = p.OptionalObject("config") ?? new Newtonsoft.Json.Linq.JObject();
                return await await Core.Services.AutomationWindows.Registry.OnWindowAsync(p.RequiredString("windowId"), async app =>
                {
                    var upload = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Core.Services.ImageUpload>(app.ServiceProvider);
                    var config = await upload.AddImageUploadConfig("e2e-" + method, method == "s3" ? Core.Enums.ImageUploadMethod.OSS : Core.Enums.ImageUploadMethod.PowerShell);
                    config.IsEnable = true;
                    var model = config.LoadUploadConfig();
                    if (model is Core.Models.UploadConfigModels.PowerShellModel ps)
                        ps.Script = (string)settings["script"] ?? ps.Script;
                    else if (model is Core.Models.UploadConfigModels.OSSConfigModel s3)
                    {
                        s3.ServiceURL = (string)settings["endpoint"] ?? "";
                        s3.RegionEndpoint = (string)settings["region"] ?? "";
                        s3.BucketName = (string)settings["bucket"] ?? "";
                        s3.AccessKey = (string)settings["accessKey"] ?? "";
                        s3.SecretKey = (string)settings["secretKey"] ?? "";
                        s3.PathStyle = (bool?)settings["pathStyle"] ?? false;
                        s3.UploadPath = (string)settings["prefix"] ?? "";
                        s3.ExternalURL = (string)settings["publicUrl"] ?? "";
                    }
                    config.StoreUploadConfig(model);
                    await upload.SaveImageUploadConfig(config);
                    app.SettingsViewModel.DefaultImageUploadConfigId = config.Id;
                    // What the configuration holds: the secret must not be there in plain text.
                    return (Newtonsoft.Json.Linq.JToken?)new Newtonsoft.Json.Linq.JObject { ["id"] = config.Id, ["stored"] = config.Config, ["default"] = upload.DefaultConfig?.Id };
                });
            }));
            methods.Add(new MethodDescriptor("test.images.uploadAll", null, "test.images.uploadAll/1", async (c, ct) =>
                await await Core.Services.AutomationWindows.Registry.OnWindowAsync(c.Params.RequiredString("windowId"), async app =>
                {
                    var result = await new Core.Services.ImageBatchUpload(app).RunAsync(null, ct);
                    return (Newtonsoft.Json.Linq.JToken?)Newtonsoft.Json.Linq.JObject.FromObject(result);
                })));
        }
#else
        public static IEditBarriers Barriers => null;

        public static void AddMethods(MethodTable methods) { }
#endif
    }
}
