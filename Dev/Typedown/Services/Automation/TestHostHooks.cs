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
        }
#else
        public static IEditBarriers Barriers => null;

        public static void AddMethods(MethodTable methods) { }
#endif
    }
}
