using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// PL01: Settings > Editor > Draw Mermaid diagrams with, in the app: Pele chosen, a document's flowchart and agentflow
/// (a kind this Mermaid cannot draw) are drawn by Pele; Mermaid chosen again, the flowchart is drawn by Mermaid.
/// </summary>
internal static partial class Program
{
    private static async Task PL01(List<string> notes)
    {
        using var c = await Session("e2e PL01");
        var text = "# PL01\n\n```mermaid\ngraph TD\n  A[Start] --> B[End]\n```\n\n```mermaid\nagentflow-beta TB\n  flow work[\"Work\"]\n    plan[\"Make a plan\"]@{ shape: task }\n    answer[\"Answer\"]@{ shape: action }\n    plan --> answer\n  end\n```\n\nend\n";
        var id = await Open(c, Fixture("pl01.md", text));
        var windowId = await WindowIdOf(c, id);
        var before = (string?)(await c.Call("test.settings.get", new { windowId, name = "MermaidRenderer" }))["value"];
        // Each Mermaid block: "pele", "mermaid" (an SVG without Pele's colours), or "-" (none drawn).
        async Task<string> Drawn() => (string?)(await c.Call("test.editor.eval", new { windowId, script =
            "[...document.querySelectorAll('#ag-editor-id figure[data-role=\"MERMAID\"] .ag-container-preview')].map(p => { const s = p.querySelector('svg'); return !s ? '-' : /--pele-/.test(s.getAttribute('style') || '') ? 'pele' : 'mermaid'; }).join(',')" }))["result"] ?? "";
        async Task<string> Until(string want)
        {
            var got = "";
            for (var i = 0; i < 50 && got != want; i++) { await Task.Delay(200); got = await Drawn(); }
            return got;
        }
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "MermaidRenderer", value = "pele" });
            var pele = await Until("pele,pele");
            notes.Add("with Pele: " + pele);
            Check(pele == "pele,pele", "with Pele chosen, both diagrams are drawn by Pele, the agentflow too");
            await c.Call("test.settings.set", new { windowId, name = "MermaidRenderer", value = "mermaid" });
            var mermaid = await Until("mermaid,-");
            notes.Add("with Mermaid: " + mermaid);
            Check(mermaid.StartsWith("mermaid"), "with Mermaid chosen again, the flowchart is drawn by Mermaid");
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId, name = "MermaidRenderer", value = before ?? "mermaid" });
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }

    /// <summary>
    /// PL02: the same choice made on the settings page, with its list, as a person makes it - PL01 sets it through the
    /// test host, past the page. Chosen there, Pele and then Mermaid again ended the process (a stack overflow, 1.4.0.29
    /// on hp with a document of diagrams open).
    /// </summary>
    private static async Task PL02(List<string> notes)
    {
        using var host = System.Diagnostics.Process.GetProcessById(hostPid);
        using var c = await Session("e2e PL02");
        var text = "# PL02\n\n```mermaid\ngraph TD\n  A[Start] --> B[End]\n```\n\nend\n";
        var id = await Open(c, Fixture("pl02.md", text));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var before = (string?)(await c.Call("test.settings.get", new { windowId, name = "MermaidRenderer" }))["value"];
        async Task Choose(string shown, string value)
        {
            var box = await WaitFor(window, "MermaidRendererBox");
            ((System.Windows.Automation.ExpandCollapsePattern)box.GetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern)).Expand();
            System.Windows.Automation.AutomationElement? item = null;
            var listed = await Eventually(async () =>
            {
                await Task.CompletedTask;
                item = Deep.First(System.Windows.Automation.AutomationElement.FromHandle(window), new System.Windows.Automation.AndCondition(
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, shown),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.ListItem)));
                return item != null;
            }, 3000);
            Check(listed, $"the list offers {shown}");
            ((System.Windows.Automation.SelectionItemPattern)item!.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select();
            // A stack overflow takes a moment to end the process: given time to.
            for (var i = 0; i < 12 && !host.HasExited; i++) await Task.Delay(250);
            Check(!host.HasExited, $"the app lives on after {shown} is chosen in the list (exit code {(host.HasExited ? host.ExitCode.ToString("x8") : "-")})");
            var now = (string?)(await c.Call("test.settings.get", new { windowId, name = "MermaidRenderer" }))["value"];
            notes.Add($"{shown} chosen: the setting is {now}");
            Check(now == value, $"the setting is {value} after {shown} is chosen ({now})");
        }
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "MermaidRenderer", value = "mermaid" });
            await c.Call("test.window.navigate", new { windowId, route = "Settings/Editor" });
            await Activate(window);
            await Choose("Pele", "pele");
            await Choose("Mermaid", "mermaid");
        }
        finally
        {
            if (!host.HasExited)
            {
                try { await c.Call("test.window.navigate", new { windowId, route = "Main" }); } catch { }
                try { await c.Call("test.settings.set", new { windowId, name = "MermaidRenderer", value = before ?? "mermaid" }); } catch { }
                try { await c.Call("document.close", new { documentId = id }); } catch { }
            }
        }
    }
}
