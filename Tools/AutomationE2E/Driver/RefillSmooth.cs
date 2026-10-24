using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // While a long document read far down is drawn back after a switch (the blocks above the window a slice at a time,
    // for 20 s and more), the page keeps drawing frames and answers the wheel. Reported (hp, 2026-10-24): demo1 at 95%
    // took 23 s to draw back, and in that time the page drew about 5 frames a second (150-230 ms apart) - scrolling it
    // stuck.
    // The page's own record while it draws: the time between frames (only while the drawing goes on), the long
    // animation frames with where their time went (script, style and layout, paint), and for each wheel notch how long
    // until the page handled it and until the next frame after it.
    private const string RsStartScript = @"(()=>{const r={gaps:[],wgaps:[],loaf:[],wheels:[],at:performance.now()};window.__rs=r;let last=performance.now();const pending=()=>!!(window.__typedownMuya&&window.__typedownMuya.renderPending);const tick=()=>{const now=performance.now();if(now-r.at>12000)return;if(pending())(window.__rsWheeling?r.wgaps:r.gaps).push(Math.round(now-last));last=now;requestAnimationFrame(tick)};requestAnimationFrame(tick);try{const o=new PerformanceObserver(l=>{for(const e of l.getEntries()){if(r.loaf.length>400)return;const scripts=(e.scripts||[]).reduce((a,s)=>a+s.duration,0);r.loaf.push({d:Math.round(e.duration),script:Math.round(scripts),style:e.styleAndLayoutStart&&e.renderStart?Math.round(e.styleAndLayoutStart-e.renderStart):0,layout:e.styleAndLayoutStart?Math.round(e.startTime+e.duration-e.styleAndLayoutStart):0,inv:(e.scripts||[]).map(s=>(s.invoker||'').slice(0,40)+':'+Math.round(s.duration)+(s.forcedStyleAndLayoutDuration?'/f'+Math.round(s.forcedStyleAndLayoutDuration):'')).slice(0,3).join(' ')})}});o.observe({type:'long-animation-frame',buffered:false});r.observer=o;r.loafOk=true}catch(e){r.loafOk=false}window.addEventListener('wheel',e=>{const handled=performance.now();requestAnimationFrame(()=>{r.wheels.push({lag:Math.round(handled-e.timeStamp),frame:Math.round(performance.now()-e.timeStamp),pending:pending()})})},{passive:true,capture:true});return 'ok'})()";
    private const string RsStopScript = @"(()=>{const r=window.__rs||{};try{r.observer&&r.observer.disconnect()}catch(e){}const s=a=>{a=[...a].sort((x,y)=>x-y);const q=p=>a.length?a[Math.min(a.length-1,Math.floor(a.length*p))]:null;return{n:a.length,p50:q(0.5),p90:q(0.9),max:a.length?a[a.length-1]:null}};const lf=r.loaf||[];const sum=k=>lf.reduce((a,e)=>a+(e[k]||0),0);return JSON.stringify({gaps:s(r.gaps||[]),wheelGaps:s(r.wgaps||[]),wheelHandled:s((r.wheels||[]).map(w=>w.lag)),wheelFrame:s((r.wheels||[]).map(w=>w.frame)),wheelsWhileDrawing:(r.wheels||[]).filter(w=>w.pending).length,loafOk:r.loafOk,loaf:{n:lf.length,ms:sum('d'),script:sum('script'),style:sum('style'),layout:sum('layout')},loafTop:lf.slice().sort((a,b)=>b.d-a.d).slice(0,4),progressive:(window.__typedownProgressiveLog||[]).slice(-1)[0]||null,pending:!!(window.__typedownMuya&&window.__typedownMuya.renderPending)})})()";

    private static async Task JT03(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e JT03");
        string Embedded(string name, bool gz)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var source = gz ? (Stream)new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress) : stream;
            using var reader = new StreamReader(source, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        var demoCopy = new[] { @"C:\e2e\private\demo1.md", @"E:\src\private\demo1.md" }.FirstOrDefault(File.Exists);
        var heavyPath = demoCopy != null ? Path.Combine(fixtures, "jt03-demo1.md") : Fixture("jt03-mixed-1m.md", Embedded("hj/mixed-1m.md.gz", true));
        if (demoCopy != null) File.Copy(demoCopy, heavyPath, true);
        var shortPath = Fixture("jt03-showcase.md", Embedded("showcase.md", false));
        notes.Add($"long: {(demoCopy != null ? "demo1" : "mixed 1M")} ({new FileInfo(heavyPath).Length / 1024} KB), short: showcase");
        var heavy = await Open(c, heavyPath);
        var windowId = await WindowIdOf(c, heavy);
        var window = await WindowOf(windowId, c);
        async Task<bool> Pending() => (bool?)(await c.Call("test.editor.eval", new { windowId, script = "!!(window.__typedownMuya&&window.__typedownMuya.renderPending)" }))["result"] == true;
        async Task Drawn(int seconds) { for (var i = 0; i < seconds * 4 && await Pending(); i++) await Task.Delay(250); }
        var other = await Open(c, shortPath);
        await Task.Delay(2000);
        var results = new List<(string Label, JObject Page)>();
        try
        {
            foreach (var at in new[] { 0.95, 0.6 })
            {
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(1000);
                await Drawn(120);
                await c.Call("test.editor.eval", new { windowId, script = $"window.scrollTo(0, Math.round(document.documentElement.scrollHeight * {at.ToString(System.Globalization.CultureInfo.InvariantCulture)})); 'ok'" });
                await Task.Delay(1500);
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(3000);
                await c.Call("test.editor.eval", new { windowId, script = RsStartScript });
                await c.Call("document.focus", new { documentId = heavy });
                // The reader reads on while it draws: the wheel turned, a notch every 150 ms for 6 s, four down and four back up.
                await Task.Delay(1500);
                GetWindowRect(window, out var r);
                int x = (r.Left + r.Right) / 2, y = (r.Top + r.Bottom) / 2;
                await c.Call("test.editor.eval", new { windowId, script = "window.__rsWheeling=true;'ok'" });
                for (var i = 0; i < 40; i++)
                {
                    Wheel(x, y, i % 8 < 4 ? -120 : 120);
                    await Task.Delay(150);
                }
                await c.Call("test.editor.eval", new { windowId, script = "window.__rsWheeling=false;'ok'" });
                await Task.Delay(1500);
                var page = JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script = RsStopScript }))["result"] ?? "{}");
                var label = $"at {at:P0}";
                notes.Add($"{label}: {page.ToString(Newtonsoft.Json.Formatting.None)}");
                results.Add((label, page));
                await Drawn(120);
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(2000);
            }
        }
        finally
        {
            foreach (var d in new[] { heavy, other }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
        foreach (var (label, page) in results)
        {
            // While the reader turns the wheel: what they see move. (Between, with nobody at the page, the drawing has it
            // in slices a frame apart or more - reported above, not checked: nothing on the screen moves then.)
            var gaps = page["wheelGaps"]!;
            Check((int?)gaps["n"] >= 20, $"{label}: the page drew frames while the wheel turned and the document was still drawn ({gaps["n"]} measured)");
            Check((int?)gaps["p50"] <= 50, $"{label}: while the wheel turns, the frames come at most 50 ms apart in the middle (median {gaps["p50"]} ms, 90% {gaps["p90"]}, longest {gaps["max"]})");
            var wheel = page["wheelFrame"]!;
            Check((int?)wheel["n"] >= 10 && (int?)wheel["p50"] <= 100, $"{label}: a notch of the wheel reaches the next frame within 100 ms in the middle ({wheel["n"]} notches, median {wheel["p50"]} ms, 90% {wheel["p90"]})");
        }
    }
}
