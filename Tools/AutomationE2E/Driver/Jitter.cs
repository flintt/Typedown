using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Every frame while it is drawn: where the blocks in the window are on screen. Reported: switching between long
    // documents (and a cold start with them), the text on screen shook slightly for a while; short ones did not.
    private const string JtStartScript = @"(()=>{window.__jt={frames:[],on:true};const tick=()=>{const s=window.__jt;if(!s||!s.on)return;const root=document.getElementById('ag-editor-id');const seen=[];if(root){for(const e of root.children){const r=e.getBoundingClientRect();if(r.bottom<=0)continue;if(r.top>=innerHeight)break;if(e.id&&e.textContent.trim())seen.push([e.id,Math.round(r.top*10)/10,Math.round(r.left*10)/10,Math.round(r.width*10)/10]);if(seen.length>=4)break}}s.frames.push([Math.round(performance.now()),window.scrollY,!!(window.__typedownMuya&&window.__typedownMuya.renderPending),seen]);requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    // The moves of blocks seen in two frames one after the other: up/down and sideways, while drawn and after.
    private const string JtStopScript = @"(()=>{const s=window.__jt;if(!s)return 'null';s.on=false;const f=s.frames;let moves=0,movesDrawing=0,side=0,maxDy=0,sumDy=0,firstPending=-1,lastPending=-1;const list=[];for(let i=1;i<f.length;i++){const a=new Map(f[i-1][3].map(x=>[x[0],x]));if(f[i][2]){if(firstPending<0)firstPending=i;lastPending=i}for(const b of f[i][3]){const p=a.get(b[0]);if(!p)continue;const dy=b[1]-p[1],dx=b[2]-p[2],dw=b[3]-p[3];if(Math.abs(dy)>=0.5){moves++;if(f[i][2])movesDrawing++;sumDy+=Math.abs(dy);maxDy=Math.max(maxDy,Math.abs(dy));if(list.length<12)list.push([f[i][0]-f[0][0],Math.round(dy*10)/10,f[i][1]-f[i-1][1],f[i][2]?'d':'']);break}if(Math.abs(dx)>=0.5||Math.abs(dw)>=0.5){side++;if(list.length<12)list.push([f[i][0]-f[0][0],'x'+Math.round(dx*10)/10,'w'+Math.round(dw*10)/10]);break}}}return JSON.stringify({frames:f.length,drawingFrames:f.filter(x=>x[2]).length,moves,movesDrawing,movesAfter:moves-movesDrawing,sideways:side,maxDy:Math.round(maxDy*10)/10,sumDy:Math.round(sumDy),first:list})})()";

    private static async Task JT01(List<string> notes)
    {
        using var c = await Session("e2e JT01");
        string Embedded(string name)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var gz = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gz, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var demoCopy = new[] { @"C:\e2e\private\demo1.md", @"E:\src\private\demo1.md" }.FirstOrDefault(File.Exists);
        var heavyPath = demoCopy != null ? Path.Combine(fixtures, "jt01-demo1.md") : Fixture("jt01-mixed-1m.md", Embedded("hj/mixed-1m.md.gz"));
        if (demoCopy != null) File.Copy(demoCopy, heavyPath, true);
        var otherPath = Fixture("jt01-mixed-300k.md", Embedded("hj/mixed-300k.md.gz"));
        notes.Add($"heavy: {Path.GetFileName(heavyPath)} ({new FileInfo(heavyPath).Length / 1024} KB), other: 300k mixed");
        var heavy = await Open(c, heavyPath);
        var windowId = await WindowIdOf(c, heavy);
        var other = await Open(c, otherPath);
        await Task.Delay(4000);
        var results = new List<(string Label, JObject R)>();
        // Each switch watched for 6 s from just before it.
        foreach (var (to, label) in new[] { (heavy, "to heavy 1"), (other, "to 300k 1"), (heavy, "to heavy 2"), (other, "to 300k 2") })
        {
            await c.Call("test.editor.eval", new { windowId, script = JtStartScript });
            await c.Call("document.focus", new { documentId = to });
            await Task.Delay(6000);
            var r = JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script = JtStopScript }))["result"] ?? "{}");
            notes.Add($"{label}: {r.ToString(Newtonsoft.Json.Formatting.None)}");
            results.Add((label, r));
        }
        foreach (var d in new[] { heavy, other }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        // The text in the window still once there: no block moved by half a pixel or more between two frames.
        foreach (var (label, r) in results)
            Check((int?)r["moves"] == 0 && (int?)r["sideways"] == 0, $"{label}: the text on screen does not move once shown ({r["moves"]} moves up/down, {r["movesDrawing"]} of them while drawn, largest {r["maxDy"]} px, {r["sideways"]} sideways)");
    }
}
