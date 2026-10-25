using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Every frame while it is drawn: where the blocks in the window are on screen (and the heights of those in it and of
    // the 40 above it, ^-marked, to name what changed at a move). Reported: switching between long
    // documents (and a cold start with them), the text on screen shook slightly for a while; short ones did not.
    private const string JtStartScript = @"(()=>{window.__jt={frames:[],on:true};const tick=()=>{const s=window.__jt;if(!s||!s.on)return;const root=document.getElementById('ag-editor-id');const seen=[],hs=[];if(root){const kids=root.children;let lo=0,hi=kids.length-1;while(lo<hi){const m=(lo+hi)>>1;if(kids[m].getBoundingClientRect().bottom<=0)lo=m+1;else hi=m}for(let k=Math.max(0,lo-40);k<lo;k++)hs.push(['^'+kids[k].id,Math.round(kids[k].getBoundingClientRect().height*10)/10]);for(let k=lo;k<kids.length;k++){const e=kids[k];const r=e.getBoundingClientRect();if(r.bottom<=0)continue;if(r.top>=innerHeight)break;hs.push([e.id,Math.round(r.height*10)/10]);if(e.id&&e.textContent.trim()&&seen.length<4)seen.push([e.id,Math.round(r.top*10)/10,Math.round(r.left*10)/10,Math.round(r.width*10)/10])}}const ed=document.getElementById('editor');s.frames.push([Math.round(performance.now()),window.scrollY,!!(window.__typedownMuya&&window.__typedownMuya.renderPending),seen,root?Math.round(root.getBoundingClientRect().top*10)/10:null,root?getComputedStyle(root).getPropertyValue('--td-room-above').trim():'',ed?ed.classList.contains('ag-long-document'):null,root&&root.firstElementChild?root.firstElementChild.id+':'+Math.round(root.firstElementChild.getBoundingClientRect().height):'',hs]);requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    // The moves of blocks seen in two frames one after the other: up/down and sideways, while drawn and after.
    private const string JtStopScript = @"(()=>{const s=window.__jt;if(!s)return 'null';s.on=false;const f=s.frames;let moves=0,movesDrawing=0,side=0,maxDy=0,sumDy=0,firstPending=-1,lastPending=-1;const list=[];for(let i=1;i<f.length;i++){const a=new Map(f[i-1][3].map(x=>[x[0],x]));if(f[i][2]){if(firstPending<0)firstPending=i;lastPending=i}for(const b of f[i][3]){const p=a.get(b[0]);if(!p)continue;const dy=b[1]-p[1],dx=b[2]-p[2],dw=b[3]-p[3];if(Math.abs(dy)>=0.5){moves++;if(f[i][2])movesDrawing++;sumDy+=Math.abs(dy);maxDy=Math.max(maxDy,Math.abs(dy));if(list.length<12)list.push([f[i][0]-f[0][0],b[0],Math.round(dy*10)/10,'scroll '+(f[i][1]-f[i-1][1]),'root '+f[i-1][4]+'>'+f[i][4],'room '+f[i-1][5]+'>'+f[i][5],'long '+f[i-1][6]+'>'+f[i][6],'first '+f[i-1][7]+'>'+f[i][7],f[i][2]?'d':'',(()=>{const ph=new Map(f[i-1][8].map(x=>[x[0],x[1]]));return f[i][8].filter(x=>ph.has(x[0])&&Math.abs(ph.get(x[0])-x[1])>=0.5).slice(0,6).map(x=>{const el=document.getElementById(x[0].replace(/^\^/,''));return x[0]+' '+ph.get(x[0])+'>'+x[1]+' now '+(el?Math.round(el.getBoundingClientRect().height*10)/10:'-')+' '+(el?el.tagName+'.'+el.className+(el.querySelector('img,iframe,svg,video')?' has '+el.querySelector('img,iframe,svg,video').tagName:'')+' cis '+getComputedStyle(el).containIntrinsicSize+' cv '+getComputedStyle(el).contentVisibility+' '+JSON.stringify(el.textContent.slice(0,40)):'')})})()]);break}if(Math.abs(dx)>=0.5||Math.abs(dw)>=0.5){side++;if(list.length<12)list.push([f[i][0]-f[0][0],'x'+Math.round(dx*10)/10,'w'+Math.round(dw*10)/10]);break}}}return JSON.stringify({frames:f.length,drawingFrames:f.filter(x=>x[2]).length,moves,movesDrawing,movesAfter:moves-movesDrawing,sideways:side,maxDy:Math.round(maxDy*10)/10,sumDy:Math.round(sumDy),first:list})})()";

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
        // Each switch watched for 6 s from just before it; the last two come back to the middle, as a document read
        // half way does (its place kept).
        foreach (var (to, label) in new[] { (heavy, "to heavy 1"), (other, "to 300k 1"), (heavy, "to heavy 2"), (other, "to 300k 2"), (heavy, "to heavy, middle"), (other, "to 300k, middle"), (heavy, "to heavy, back to its middle") })
        {
            if (label.Contains("middle"))
            {
                // The one being left read half way first.
                await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, document.documentElement.scrollHeight / 2); 'ok'" });
                await Task.Delay(1500);
            }
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
