using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// HJ01 (measurement with thresholds; branch perf/pretext-heights): how far the scroll bar is off while a long document
/// is drawn a part at a time, in the real app with Windows' fonts - what Tools/EditorBench/height-jump-check.js measures
/// in headless Chromium. Two documents of mixed Chinese/English/Japanese/Korean prose with headings, lists, code, quotes
/// and tables (about 300k and 1M characters, EditorBench fixtures/mixed), each opened at the top and drawn again from
/// the middle (left half way, a small document between, nothing kept), once with the block heights predicted by pretext
/// (predictHeights.js) and once with window.__typedownPretextOff (the old per-character estimate), in the same run.
///
/// Every frame while drawn (in the page): the page's height against its real one (the page wholly laid out at the end,
/// content-visibility forced visible, contained as the long-document rule keeps it where it stays on), the thumb's
/// position against where it belongs for the block being read, how far that block moved, the gaps between frames.
///
/// Thresholds, per document and place, with pretext: the height off by at most 2.5% on average while drawn, at most
/// 1.5% once all of the rest is predicted, the thumb at most 1% off (opened in the middle), the height once drawn
/// within 1% of the real one. The old estimate must fail at least one of them for each document (it did by 10-17% on
/// average in EditorBench, and kept 1M documents 17% short once drawn) - else the case does not measure anything.
/// </summary>
internal static partial class Program
{
    private const double HjMaxMean = 2.5, HjMaxSettled = 1.5, HjMaxThumb = 1.0, HjMaxEnd = 1.0;

    private static string HjFixture(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("hj/" + name) ?? throw new InvalidOperationException("no resource hj/" + name);
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    // Started before a load: every frame's height, scroll position, the read block's place and whether the drawing is
    // still going, until stopped.
    private const string HjStartScript = @"(()=>{window.__hj={frames:[],key:null,on:true};const tick=()=>{const s=window.__hj;if(!s||!s.on)return;const el=s.key?document.getElementById(s.key):null;s.frames.push([performance.now(),document.documentElement.scrollHeight,window.scrollY,el?el.getBoundingClientRect().top:null,!!(window.__typedownMuya&&window.__typedownMuya.renderPending)]);requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    // Once drawn: the frames summed up against the page wholly laid out (forced here; synchronous - the evaluation is
    // not awaited). `readTop`: where the read block is meant to be in the window (null: opened at the top).
    private static string HjSummaryScript(int? readTop) => @"(()=>{const s=window.__hj;s.on=false;const fr=s.frames;const vh=innerHeight;
const end={h:document.documentElement.scrollHeight,long:!!document.querySelector('#editor.ag-long-document')};
const st=document.createElement('style');st.id='hj01-real';st.textContent='#ag-editor-id > * { content-visibility: visible !important; contain-intrinsic-size: none !important; '+(end.long?'contain: layout style paint !important;':'')+' }';document.head.appendChild(st);
const realH=document.documentElement.scrollHeight;const el=s.key?document.getElementById(s.key):null;const abs=el?el.getBoundingClientRect().top+scrollY:0;st.remove();
const shown=fr.filter(f=>f[3]!==null);const readTop=" + (readTop?.ToString() ?? "null") + @"??(shown.length?shown[0][3]:0);
const thumbReal=Math.max(0,abs-readTop)/Math.max(1,realH-vh);const log=window.__typedownPretextLog||null;const settledAt=log&&log.doneAtTime!==undefined&&!window.__typedownPretextOff?log.doneAtTime:null;
let worst=0,settled=settledAt===null?null:0,w=0,span=0,thumb=0,drift=0,gap=0,prev=null,pend=0;const t0=shown.length?shown[0][0]:0;
for(const f of shown){const err=Math.abs(f[1]-realH)/realH*100;if(f[4]){pend++;worst=Math.max(worst,err);if(settledAt!==null&&f[0]>=settledAt)settled=Math.max(settled,err);if(prev){w+=err*(f[0]-prev[0]);span+=f[0]-prev[0];gap=Math.max(gap,f[0]-prev[0])}}
const th=f[1]>vh?f[2]/(f[1]-vh):0;if(" + (readTop.HasValue ? "true" : "false") + @")thumb=Math.max(thumb,Math.abs(th-thumbReal)*100);if(f[0]>t0+50)drift=Math.max(drift,Math.abs(f[3]-readTop));prev=f}
const r=x=>x===null?null:Math.round(x*100)/100;
return JSON.stringify({frames:shown.length,drawingFrames:pend,mean:r(span?w/span:worst),worst:r(worst),settled:r(settled),settledAfterMs:settledAt===null?null:Math.round(settledAt-t0),thumb:r(thumb),drift:r(drift),maxGapMs:Math.round(gap),endH:end.h,realH,end:r(Math.abs(end.h-realH)/realH*100),longAtEnd:end.long,blocks:window.__typedownMuya.contentState.blocks.length,prepareMs:log?log.prepareMs:null})})()";

    private static async Task HJ01(List<string> notes)
    {
        using var c = await Session("e2e HJ01");
        var small = await Open(c, Fixture("hj01-small.md", "# HJ01\n\nA short document to switch to.\n"));
        var windowId = await WindowIdOf(c, small);
        async Task<string> Eval(string script) => (string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "";
        async Task<bool> Drawn() => await Eval("String(!!window.__typedownMuya && !window.__typedownMuya.renderPending && document.getElementById('ag-editor-id')?.children.length > 0)") == "true";
        var results = new JObject();
        var opened = new List<string>();
        try
        {
            foreach (var (label, file) in new[] { ("300k", "mixed-300k.md.gz"), ("1m", "mixed-1m.md.gz") })
            {
                var text = HjFixture(file);
                foreach (var off in new[] { false, true })
                {
                    var mode = off ? "old" : "pretext";
                    var tag = $"{label} {mode}";
                    await Eval($"window.__typedownPretextOff = {(off ? "true" : "false")}; 'ok'");

                    // At the top: opened anew.
                    await Eval(HjStartScript);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var id = await Open(c, Fixture($"hj01-{label}-{mode}.md", text));
                    opened.Add(id);
                    await Eventually(async () => await Eval("(()=>{const b=window.__typedownMuya&&window.__typedownMuya.contentState.blocks;if(!b||b.length<100)return 'no';window.__hj.key=b[0].key;return 'yes'})()") == "yes", 30000, 50);
                    if (!await Eventually(Drawn, 120000, 200)) throw new CaseFailed($"{tag}: not drawn within 120 s");
                    var drawnMs = clock.ElapsedMilliseconds;
                    await Task.Delay(1000);
                    var top = JObject.Parse(await Eval(HjSummaryScript(null)));
                    top["drawnMs"] = drawnMs;
                    results[$"{tag} top"] = top;

                    // From the middle: scrolled half way, left for the small document (nothing kept), back.
                    var index = await Eval("(()=>{window.scrollTo(0,document.documentElement.scrollHeight*0.5);const e=document.getElementById('ag-editor-id');const kids=Array.from(e.children);const i=kids.findIndex(k=>k.getBoundingClientRect().bottom>120);return String(i)})()");
                    if (!int.TryParse(index, out var readIndex) || readIndex < 0) throw new CaseFailed($"{tag}: no block in the window half way ({index})");
                    await Task.Delay(1200);
                    await c.Call("document.focus", new { documentId = small });
                    if (!await Eventually(Drawn, 30000, 200)) throw new CaseFailed($"{tag}: the small document not drawn");
                    await Task.Delay(500);
                    await Eval("window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear(); 'ok'");
                    await Eval(HjStartScript);
                    clock.Restart();
                    await c.Call("document.focus", new { documentId = id });
                    await Eventually(async () => await Eval($"(()=>{{const b=window.__typedownMuya&&window.__typedownMuya.contentState.blocks;if(!b||b.length<{readIndex + 1})return 'no';window.__hj.key=b[{readIndex}].key;return 'yes'}})()") == "yes", 30000, 50);
                    // Where the read block stood once its part was drawn is where it is meant to stay.
                    var readTop = 0;
                    await Eventually(async () =>
                    {
                        var t = await Eval("(()=>{const el=document.getElementById(window.__hj.key);return el?String(Math.round(el.getBoundingClientRect().top)):'none'})()");
                        return t != "none" && int.TryParse(t, out readTop);
                    }, 30000, 50);
                    if (!await Eventually(Drawn, 120000, 200)) throw new CaseFailed($"{tag}: not drawn again within 120 s");
                    drawnMs = clock.ElapsedMilliseconds;
                    await Task.Delay(1000);
                    var middle = JObject.Parse(await Eval(HjSummaryScript(readTop)));
                    middle["drawnMs"] = drawnMs;
                    middle["readBlock"] = readIndex;
                    results[$"{tag} middle"] = middle;

                    try { await c.Call("document.close", new { documentId = id }); } catch { }
                    opened.Remove(id);
                    notes.Add($"{tag}: top {top.ToString(Newtonsoft.Json.Formatting.None)}");
                    notes.Add($"{tag}: middle {middle.ToString(Newtonsoft.Json.Formatting.None)}");
                }
            }
        }
        finally
        {
            try { await Eval("window.__typedownPretextOff = false; 'ok'"); } catch { }
            foreach (var id in opened) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = small }); } catch { }
        }
        File.WriteAllText(Path.Combine(outputDir, "hj01.json"), results.ToString());
        notes.Add("all: hj01.json beside the results");

        // With pretext: within the thresholds. The old estimate: outside at least one of them, for each document.
        bool Fails(JObject r, bool middle) => (double)r["mean"]! > HjMaxMean || (double)r["end"]! > HjMaxEnd || (middle && (double)r["thumb"]! > HjMaxThumb);
        foreach (var label in new[] { "300k", "1m" })
        {
            foreach (var place in new[] { "top", "middle" })
            {
                var p = (JObject)results[$"{label} pretext {place}"]!;
                Check((double)p["mean"]! <= HjMaxMean, $"{label} {place}: the height is within {HjMaxMean}% of the real one on average while drawn ({p["mean"]}%)");
                Check(p["settled"]!.Type != JTokenType.Null && (double)p["settled"]! <= HjMaxSettled, $"{label} {place}: ... within {HjMaxSettled}% once all of the rest is predicted ({p["settled"]})");
                Check((double)p["end"]! <= HjMaxEnd, $"{label} {place}: once drawn, within {HjMaxEnd}% of the real height ({p["end"]}%)");
                if (place == "middle") Check((double)p["thumb"]! <= HjMaxThumb, $"{label} middle: the thumb within {HjMaxThumb}% of where it belongs ({p["thumb"]}%)");
            }
            var oldTop = (JObject)results[$"{label} old top"]!;
            var oldMiddle = (JObject)results[$"{label} old middle"]!;
            Check(Fails(oldTop, false) || Fails(oldMiddle, true), $"{label}: the old estimate is outside the thresholds (mean {oldTop["mean"]}% / {oldMiddle["mean"]}%, end {oldTop["end"]}%, thumb {oldMiddle["thumb"]}%) - the case tells the two apart");
        }
    }
}
