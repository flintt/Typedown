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
}
