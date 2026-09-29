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
        }
#else
        public static IEditBarriers Barriers => null;

        public static void AddMethods(MethodTable methods) { }
#endif
    }
}
