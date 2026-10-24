using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Where the block at the top of the window is, frame by frame, from 300 ms after the long document is first drawn
    // (LoadDrawn, as JtMessagesScript hears it): its top, or null while it is not on the page; recorded when it moves or
    // the page scrolls more than a window's height. A far jump (the page 100,000 px away) shows no block in two frames
    // alike and no move between them in JT02's counts: here the block held is gone from the window.
    private const string JhStartScript = @"(()=>{const s=window.__jh={rec:[],ref:null,on:true,t0:performance.now()};const topBlock=()=>{const root=document.getElementById('ag-editor-id');if(!root)return null;const kids=root.children;let lo=0,hi=kids.length-1;while(lo<hi){const m=(lo+hi)>>1;if(kids[m].getBoundingClientRect().bottom<=0)lo=m+1;else hi=m}for(let k=lo;k<kids.length;k++){const e=kids[k];if(e.id&&e.textContent.trim())return e}return null};const tick=()=>{if(!s.on)return;const now=Math.round(performance.now()-s.t0);const y=Math.round(window.scrollY),h=document.documentElement.scrollHeight;if(!s.ref){const d=(window.__jtSaid||[]).find(x=>x[1]==='LoadDrawn');if(d&&now>d[0]+300){const e=topBlock();if(e)s.ref={id:e.id,top:Math.round(e.getBoundingClientRect().top),y,h,at:now}}}else{const e=document.getElementById(s.ref.id);const top=e?Math.round(e.getBoundingClientRect().top):null;const last=s.rec.length?s.rec[s.rec.length-1]:[0,s.ref.top,s.ref.y];if((last[1]!==top||Math.abs(last[2]-y)>innerHeight)&&s.rec.length<300)s.rec.push([now,top,y,h])}requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    private const string JhStopScript = @"(()=>{const s=window.__jh;if(!s)return 'null';s.on=false;let max=0,gone=0;for(const r of s.rec){if(r[1]===null)gone++;else max=Math.max(max,Math.abs(r[1]-s.ref.top))}return JSON.stringify({ref:s.ref,maxDev:max,gone,n:s.rec.length,first:s.rec.slice(0,12),last:s.rec.slice(-6),endY:Math.round(window.scrollY),endH:document.documentElement.scrollHeight})})()";

    // Back to a long document left at its very end, nothing touched for 15 s: the block that was in the window stays
    // there. Reported (hp, 1.4.0.12, 2026-10-24): a 1.5 MB document back at its end (block 2927 of 2938), predicting on, a
    // window nearly the screen's with the side pane open; 6 s later the page went 100,000 px up with nobody at it, and
    // the next switch back put it where it had gone (block 2458).
    private static async Task JT04(List<string> notes)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var c = await Session("e2e JT04");
        string Embedded(string name, bool gz)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new CaseFailed("no resource " + name);
            using var source = gz ? (Stream)new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress) : stream;
            using var reader = new StreamReader(source, new System.Text.UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        // The long document it was reported with, where a test machine keeps a private copy (never in the repository);
        // otherwise a generated one.
        var privateCopy = new[] { @"C:\e2e\private", @"E:\src\private" }.Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.md").OrderBy(f => f, StringComparer.Ordinal)).FirstOrDefault();
        var heavyPath = privateCopy != null ? Path.Combine(fixtures, "jt04-private.md") : Fixture("jt04-mixed-1m.md", Embedded("hj/mixed-1m.md.gz", true));
        if (privateCopy != null) File.Copy(privateCopy, heavyPath, true);
        var shortPath = Fixture("jt04-showcase.md", Embedded("showcase.md", false));
        notes.Add($"long: {(privateCopy != null ? "the private copy" : "mixed 1M")} ({new FileInfo(heavyPath).Length / 1024} KB), short: showcase");
        var heavy = await Open(c, heavyPath);
        var windowId = await WindowIdOf(c, heavy);
        var window = await WindowOf(windowId, c);
        async Task<bool> Pending() => (bool?)(await c.Call("test.editor.eval", new { windowId, script = "!!(window.__typedownMuya&&window.__typedownMuya.renderPending)" }))["result"] == true;
        async Task Drawn(int seconds) { for (var i = 0; i < seconds * 4 && await Pending(); i++) await Task.Delay(250); }
        var other = await Open(c, shortPath);
        await Task.Delay(2000);
        var results = new List<(string Label, JObject Page)>();
        GetWindowRect(window, out var normal);
        // The reader's settings on hp.
        var was = new Dictionary<string, JToken?>();
        var reader = new Dictionary<string, object> { ["PredictHeights"] = true, ["EditorAreaWidth"] = "1200px", ["LineHeight"] = 1.6, ["AppCompactMode"] = true };
        try
        {
            foreach (var name in reader.Keys) was[name] = (await c.Call("test.settings.get", new { windowId, name }))["value"];
            foreach (var (name, value) in reader) await c.Call("test.settings.set", new { windowId, name, value });
            ShowWindow(window, 3 /* SW_MAXIMIZE */);
            await c.Call("window.setView", new { windowId, sidePane = new { open = true, page = "outline" } });
            await Task.Delay(1500);
            foreach (var predict in new[] { true, false })
            foreach (var (away, minimized) in new[] { (3, false), (60, false), (90, true) })
            {
                var label = $"predict {(predict ? "on" : "off")}, away {away} s{(minimized ? ", the window minimized" : "")}";
                await c.Call("test.settings.set", new { windowId, name = "PredictHeights", value = predict });
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(1000);
                await Drawn(90);
                await c.Call("test.editor.eval", new { windowId, script = "window.scrollTo(0, document.documentElement.scrollHeight); 'ok'" });
                await Task.Delay(1500);
                await c.Call("document.focus", new { documentId = other });
                // Minimized: the web view hidden and its page possibly suspended while away, as a reader leaving the app.
                if (minimized) ShowWindow(window, 6 /* SW_MINIMIZE */);
                await Task.Delay(away * 1000);
                if (minimized) { ShowWindow(window, 3 /* SW_MAXIMIZE */); await Task.Delay(1500); }
                await c.Call("test.editor.eval", new { windowId, script = JtMessagesScript });
                await c.Call("test.editor.eval", new { windowId, script = JhStartScript });
                await c.Call("document.focus", new { documentId = heavy });
                await Task.Delay(15000);
                var page = JObject.Parse((string?)(await c.Call("test.editor.eval", new { windowId, script = JhStopScript }))["result"] ?? "{}");
                var said = (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__jtSaid||[])" }))["result"];
                notes.Add($"{label}: {page.ToString(Newtonsoft.Json.Formatting.None)}; the page told the host: {said}");
                results.Add((label, page));
                await Drawn(90);
            }
        }
        finally
        {
            foreach (var (name, value) in was) try { if (value != null) await c.Call("test.settings.set", new { windowId, name, value }); } catch { }
            try { await c.Call("window.setView", new { windowId, sidePane = new { open = false } }); } catch { }
            try { ShowWindow(window, 9 /* SW_RESTORE */); MbSetWindowPos(window, IntPtr.Zero, normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, 0x0004 | 0x0010); } catch { }
            foreach (var d in new[] { heavy, other }) try { await c.Call("document.close", new { documentId = d }); } catch { }
        }
        foreach (var (label, page) in results)
        {
            Check(page["ref"] is JObject, $"{label}: the long document was drawn and a block taken in the window");
            Check((int?)page["gone"] == 0 && (int?)page["maxDev"] <= 2, $"{label}: the block in the window when the long document came stays there for 15 s (moved up to {page["maxDev"]} px, {page["gone"]} frames off the page; page {page["ref"]?["y"]} -> {page["endY"]} of {page["ref"]?["h"]} -> {page["endH"]})");
        }
    }
}
