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
    private const string RsStartScript = @"(()=>{window.__rsTrack=false;const r={gaps:[],wgaps:[],loaf:[],wheels:[],jump:0,jumps:[],at:performance.now()};window.__rs=r;let last=performance.now();const pending=()=>!!(window.__typedownMuya&&window.__typedownMuya.renderPending);const topBlock=()=>{const root=document.getElementById('ag-editor-id');if(!root)return -1;const kids=root.children;let lo=0,hi=kids.length-1;if(hi<0)return -1;while(lo<hi){const m=(lo+hi)>>1;if(kids[m].getBoundingClientRect().bottom<=0)lo=m+1;else hi=m}const p=window.__typedownMuya&&window.__typedownMuya.contentState&&window.__typedownMuya.contentState.stateRender&&window.__typedownMuya.contentState.stateRender.progressive;return (p?p.lo:0)+lo};let lastTop=-1;const tick=()=>{const now=performance.now();if(now-r.at>16000)return;if(window.__rsWheeling||window.__rsTrack)r.tracking=true;if(r.tracking){const t=topBlock();if(lastTop>=0&&t>=0){const d=t-lastTop;if(Math.abs(d)>Math.abs(r.jump))r.jump=d;if(Math.abs(d)>(window.__rsWheeling?40:1)&&r.jumps.length<6)r.jumps.push([Math.round(now-r.at),lastTop,t,Math.round(window.scrollY)])}if(t>=0)lastTop=t}if(pending())(window.__rsWheeling?r.wgaps:r.gaps).push(Math.round(now-last));last=now;requestAnimationFrame(tick)};requestAnimationFrame(tick);try{const o=new PerformanceObserver(l=>{for(const e of l.getEntries()){if(r.loaf.length>400)return;const scripts=(e.scripts||[]).reduce((a,s)=>a+s.duration,0);r.loaf.push({d:Math.round(e.duration),script:Math.round(scripts),style:e.styleAndLayoutStart&&e.renderStart?Math.round(e.styleAndLayoutStart-e.renderStart):0,layout:e.styleAndLayoutStart?Math.round(e.startTime+e.duration-e.styleAndLayoutStart):0,inv:(e.scripts||[]).map(s=>(s.invoker||'').slice(0,40)+':'+Math.round(s.duration)+(s.forcedStyleAndLayoutDuration?'/f'+Math.round(s.forcedStyleAndLayoutDuration):'')).slice(0,3).join(' ')})}});o.observe({type:'long-animation-frame',buffered:false});r.observer=o;r.loafOk=true}catch(e){r.loafOk=false}window.addEventListener('wheel',e=>{const handled=performance.now();requestAnimationFrame(()=>{r.wheels.push({lag:Math.round(handled-e.timeStamp),frame:Math.round(performance.now()-e.timeStamp),pending:pending()})})},{passive:true,capture:true});return 'ok'})()";
    private const string RsStopScript = @"(()=>{const r=window.__rs||{};try{r.observer&&r.observer.disconnect()}catch(e){}const s=a=>{a=[...a].sort((x,y)=>x-y);const q=p=>a.length?a[Math.min(a.length-1,Math.floor(a.length*p))]:null;return{n:a.length,p50:q(0.5),p90:q(0.9),max:a.length?a[a.length-1]:null}};const lf=r.loaf||[];const sum=k=>lf.reduce((a,e)=>a+(e[k]||0),0);return JSON.stringify({jump:r.jump||0,jumps:r.jumps||[],gaps:s(r.gaps||[]),wheelGaps:s(r.wgaps||[]),wheelHandled:s((r.wheels||[]).map(w=>w.lag)),wheelFrame:s((r.wheels||[]).map(w=>w.frame)),wheelsWhileDrawing:(r.wheels||[]).filter(w=>w.pending).length,loafOk:r.loafOk,loaf:{n:lf.length,ms:sum('d'),script:sum('script'),style:sum('style'),layout:sum('layout')},loafTop:lf.slice().sort((a,b)=>b.d-a.d).slice(0,4),progressive:(window.__typedownProgressiveLog||[]).slice(-1)[0]||null,pending:!!(window.__typedownMuya&&window.__typedownMuya.renderPending)})})()";

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
        GetWindowRect(window, out var normal);
        try
        {
            // And as on hp (Typeleaf 1.4.0.12): demo1 in reading mode at the end (block 2927 of 2938), 1402 x 1070 with the
            // outline open, the wheel turned down all the while and on past the end - the page went 100,000 px up.
            // And with no input at all, at the very end (hp, 1.4.0.12, reading mode, nothing touched: 6-7 s after the switch the
            // page went 100,000 px up): watched for 13 s.
            foreach (var (at, reading, down, predict, still) in new[] { (0.95, false, false, false, false), (0.6, false, false, false, false), (0.99, true, true, false, false), (0.99, true, true, true, false), (1.0, true, false, false, true), (1.0, true, false, true, true) })
            {
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(1000);
                await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = predict });
                if (reading)
                {
                    MbSetWindowPos(window, IntPtr.Zero, 40, 20, 1402, 1070, 0x0004 | 0x0010);
                    await c.Call("window.setView", new { windowId, mode = "reading", sidePane = new { open = true, page = "outline" } });
                    await Task.Delay(1000);
                }
                await Drawn(120);
                await c.Call("test.editor.eval", new { windowId, script = $"window.scrollTo(0, Math.round(document.documentElement.scrollHeight * {at.ToString(System.Globalization.CultureInfo.InvariantCulture)})); 'ok'" });
                await Task.Delay(1500);
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(3000);
                await c.Call("test.editor.eval", new { windowId, script = RsStartScript });
                await c.Call("document.focus", new { documentId = heavy });
                // The reader reads on while it draws: the wheel turned, a notch every 150 ms for 6 s, four down and four back up.
                await Task.Delay(1500);
                await c.Call("test.editor.eval", new { windowId, script = "window.__rsTrack=true;'ok'" });
                GetWindowRect(window, out var r);
                int x = (r.Left + r.Right) / 2, y = (r.Top + r.Bottom) / 2;
                if (!still) await c.Call("test.editor.eval", new { windowId, script = "window.__rsWheeling=true;'ok'" });
                for (var i = 0; i < (still ? 0 : 40); i++)
                {
                    Wheel(x, y, down || i % 8 < 4 ? -120 : 120);
                    await Task.Delay(150);
                }
                await c.Call("test.editor.eval", new { windowId, script = "window.__rsWheeling=false;'ok'" });
                await Task.Delay(still ? 11500 : 1500);
                var page = JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script = RsStopScript }))["result"] ?? "{}");
                var label = $"at {at:P0}{(reading ? ", reading mode, hp's window" + (still ? ", nothing touched" : ", the wheel down") : "")}{(predict ? ", predicting the layout" : "")}";
                notes.Add($"{label}: {page.ToString(Newtonsoft.Json.Formatting.None)}");
                results.Add((label, page));
                await Drawn(120);
                await c.Call("document.focus", new { documentId = other });
                await Task.Delay(2000);
            }
        }
        finally
        {
            try { await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = false }); } catch { }
            try { await c.Call("document.focus", new { documentId = heavy }); await c.Call("window.setView", new { windowId, mode = "visual", sidePane = new { open = false } }); } catch { }
            try { MbSetWindowPos(window, IntPtr.Zero, normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, 0x0004 | 0x0010); } catch { }
            foreach (var d in new[] { heavy, other }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
        foreach (var (label, page) in results)
        {
            // While the reader turns the wheel: what they see move. (Between, with nobody at the page, the drawing has it
            // in slices a frame apart or more - reported above, not checked: nothing on the screen moves then.)
            if (label.Contains("nothing touched"))
            {
                Check(Math.Abs((int?)page["jump"] ?? 0) <= 1, $"{label}: with nothing touched the top of the window stays where it was put back, frame after frame (largest move {page["jump"]} blocks; {page["jumps"]?.ToString(Newtonsoft.Json.Formatting.None)} as [ms, block, block, scrollY])");
                continue;
            }
            var gaps = page["wheelGaps"]!;
            Check((int?)gaps["n"] >= 20, $"{label}: the page drew frames while the wheel turned and the document was still drawn ({gaps["n"]} measured)");
            Check((int?)gaps["p50"] <= 50, $"{label}: while the wheel turns, the frames come at most 50 ms apart in the middle (median {gaps["p50"]} ms, 90% {gaps["p90"]}, longest {gaps["max"]})");
            // The wheel moves the page by a block or two a notch: the block at the top of the window far from the one
            // before it from one frame to the next is the page jumping (hp: 100,000 px up while the wheel went down).
            Check(Math.Abs((int?)page["jump"] ?? 0) <= 40, $"{label}: from frame to frame the top of the window moves by a few blocks at most, not far (largest {page["jump"]} blocks; {page["jumps"]?.ToString(Newtonsoft.Json.Formatting.None)} as [ms, block, block, scrollY])");
            var wheel = page["wheelFrame"]!;
            Check((int?)wheel["n"] >= 10 && (int?)wheel["p50"] <= 100, $"{label}: a notch of the wheel reaches the next frame within 100 ms in the middle ({wheel["n"]} notches, median {wheel["p50"]} ms, 90% {wheel["p90"]})");
        }
    }
}
