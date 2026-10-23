using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// HJ01 (measurement with thresholds; branch perf/pretext-heights): how far the scroll bar is off while a long document
/// is drawn a part at a time, in the real app with Windows' fonts - what Tools/EditorBench/height-jump-check.js measures
/// in headless Chromium. Three documents of mixed Chinese/English/Japanese/Korean prose with headings, lists, code,
/// quotes and tables (about 100k, 300k and 1M characters, EditorBench fixtures/mixed), each opened at the top and drawn
/// again from the middle (left half way, a small document between, nothing kept), once with the block heights
/// predicted (predictHeights.js) and once with window.__typedownPretextOff (the old per-character estimate), in the same
/// run.
///
/// Every frame while drawn (in the page): the page's height against its real one (the page wholly laid out once drawn
/// and the blocks guessed while drawn predicted, content-visibility forced visible, contained as the long-document rule
/// keeps it where it stays on), the thumb's position against where it belongs for the block being read, how far that
/// block moved, the gaps between frames, the drawing's own time.
///
/// Thresholds, per document and place, with predictions: the height off by at most 2.5% on average while drawn and no
/// more than with the old estimate, within 1% once drawn, the thumb at most 1% off (opened in the middle), drawn in at
/// most 1.2 times the old estimate's time (the medians of three drawings each), the longest frame gap at most 50 ms over its. The old estimate must fail one
/// of the height thresholds for the 300k and 1M documents - else the case does not measure anything.
///
/// HJ02: where a heading far down lands when the outline jumps to it (the host's ScrollTo, EditorViewModel.JumpBySlug)
/// while the document is still drawn, and once more once it is: the page's half is Tools/EditorBench/heading-jump-page.js
/// (heading-jump-check.js in headless Chromium). Thresholds: with predictions the heading ends within 50 px of its place
/// and moves no more often than with the old estimate; where the long-document rule stays once drawn (1M) the old
/// estimate must end farther off or move more.
/// </summary>
internal static partial class Program
{
    private const double HjMaxMean = 2.5, HjMaxThumb = 1.0, HjMaxEnd = 1.0, HjMaxSlower = 1.2, HjMaxLanding = 50;

    private static string HjResource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("hj/" + name) ?? throw new InvalidOperationException("no resource hj/" + name);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static string HjFixture(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("hj/" + name) ?? throw new InvalidOperationException("no resource hj/" + name);
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    // Started before a load: every frame's height, scroll position, the read block's place and whether the drawing is
    // still going, until stopped.
    private const string HjStartScript = @"(()=>{const ps=window.__typedownPretextStats;if(ps)for(const k of Object.keys(ps))delete ps[k];window.__typedownProgressiveLog=[];window.__hj={frames:[],key:null,on:true};const tick=()=>{const s=window.__hj;if(!s||!s.on)return;const el=s.key?document.getElementById(s.key):null;s.frames.push([performance.now(),document.documentElement.scrollHeight,window.scrollY,el?el.getBoundingClientRect().top:null,!!(window.__typedownMuya&&window.__typedownMuya.renderPending)]);requestAnimationFrame(tick)};requestAnimationFrame(tick);return 'ok'})()";

    // Once drawn (and the blocks guessed while drawn predicted, ruleRest): the frames summed up against the page wholly
    // laid out (forced here; synchronous - the evaluation is not awaited). `readTop`: where the read block is meant to be
    // in the window (null: opened at the top). The drawing's time is the page's own (Muya's progressive log).
    private static string HjSummaryScript(int? readTop) => @"(()=>{const s=window.__hj;s.on=false;const fr=s.frames;const vh=innerHeight;
const end={h:document.documentElement.scrollHeight,long:!!document.querySelector('#editor.ag-long-document')};
const st=document.createElement('style');st.id='hj01-real';st.textContent='#ag-editor-id > * { content-visibility: visible !important; contain-intrinsic-size: none !important; '+(end.long?'contain: layout style paint !important;':'')+' }';document.head.appendChild(st);
const realH=document.documentElement.scrollHeight;const el=s.key?document.getElementById(s.key):null;const abs=el?el.getBoundingClientRect().top+scrollY:0;st.remove();
const shown=fr.filter(f=>f[3]!==null);const readTop=" + (readTop?.ToString() ?? "null") + @"??(shown.length?shown[0][3]:0);
const thumbReal=Math.max(0,abs-readTop)/Math.max(1,realH-vh);const log=window.__typedownPretextLog||null;
let worst=0,w=0,span=0,thumb=0,drift=0,driftDrawing=0,driftAt=null,gap=0,prev=null,pend=0;const t0=shown.length?shown[0][0]:0;
for(const f of shown){const err=Math.abs(f[1]-realH)/realH*100;if(f[4]){pend++;worst=Math.max(worst,err);if(prev){w+=err*(f[0]-prev[0]);span+=f[0]-prev[0];gap=Math.max(gap,f[0]-prev[0])}}
const th=f[1]>vh?f[2]/(f[1]-vh):0;if(" + (readTop.HasValue ? "true" : "false") + @")thumb=Math.max(thumb,Math.abs(th-thumbReal)*100);if(f[0]>t0+50){const d=Math.abs(f[3]-readTop);if(d>drift){drift=d;driftAt=Math.round(f[0]-t0)}if(f[4])driftDrawing=Math.max(driftDrawing,d)}prev=f}
const r=x=>x===null?null:Math.round(x*100)/100;const pl=(window.__typedownProgressiveLog||[]).slice(-1)[0]||{};
return JSON.stringify({frames:shown.length,drawingFrames:pend,drawnMs:pl.ms??null,slices:pl.slices??null,mean:r(span?w/span:worst),worst:r(worst),thumb:r(thumb),drift:r(drift),driftDrawing:r(driftDrawing),driftAtMs:driftAt,maxGapMs:Math.round(gap),endH:end.h,realH,end:r(Math.abs(end.h-realH)/realH*100),longAtEnd:end.long,blocks:window.__typedownMuya.contentState.blocks.length,prepareMs:log?log.prepareMs:null,restMs:log&&log.restDoneAt!==undefined?Math.round(log.restDoneAt-t0):null,stats:window.__typedownPretextStats||null})})()";

    // Drawn, and (with predictions) the blocks only guessed while drawn predicted too.
    private const string HjSettledScript = "String(!!window.__typedownMuya && !window.__typedownMuya.renderPending && (!!window.__typedownPretextOff || !!(window.__typedownPretextLog && window.__typedownPretextLog.restDoneAt !== undefined) || window.__typedownMuya.contentState.stateRender.heights.resting === null))";
    private const string HjPendingScript = "String(!!window.__typedownMuya && !window.__typedownMuya.renderPending)";
    // What the page was doing when it did not settle.
    private const string HjStateScript = "JSON.stringify({pending:!!window.__typedownMuya?.renderPending,log:window.__typedownPretextLog||null,resting:!!window.__typedownMuya?.contentState.stateRender.heights.resting,fontsLoading:window.__typedownMuya?.contentState.stateRender.heights.fontsLoading,stats:window.__typedownPretextStats||null})";

    private static async Task HJ01(List<string> notes)
    {
        using var c = await Session("e2e HJ01");
        var small = await Open(c, Fixture("hj01-small.md", "# HJ01\n\nA short document to switch to.\n"));
        var windowId = await WindowIdOf(c, small);
        async Task<string> Eval(string script) => (string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "";
        async Task<bool> Drawn() => await Eval("String(!!window.__typedownMuya && !window.__typedownMuya.renderPending && document.getElementById('ag-editor-id')?.children.length > 0)") == "true";
        var results = new JObject();
        var opened = new List<string>();
        // A drawing's time on the W10 PC varies by a fifth and more from one drawing to the next (300k from the top, the
        // old estimate: 576, 604 and 798 ms in one run; the first drawing of the run 2023 ms): each document is drawn three
        // times in each mode and place, and the medians compared.
        var drawnSamples = new Dictionary<string, List<double>>();
        void Sample(string key, JToken? ms)
        {
            if (ms == null || ms.Type == JTokenType.Null) return;
            (drawnSamples.TryGetValue(key, out var l) ? l : drawnSamples[key] = new List<double>()).Add((double)ms);
        }
        try
        {
            // The first long document of the run drawn once before anything is measured: the page's code and pretext warm.
            {
                var warm = await Open(c, Fixture("hj01-warm.md", HjFixture("mixed-100k.md.gz")));
                await Eventually(async () => await Eval(HjPendingScript) == "true", 60000, 200);
                await Task.Delay(1500);
                try { await c.Call("document.close", new { documentId = warm }); } catch { }
                await Task.Delay(500);
            }
            foreach (var (label, file) in new[] { ("100k", "mixed-100k.md.gz"), ("300k", "mixed-300k.md.gz"), ("1m", "mixed-1m.md.gz") })
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
                    await Eventually(async () => await Eval(HjSettledScript) == "true", 30000, 200);
                    await Task.Delay(1000);
                    var top = JObject.Parse(await Eval(HjSummaryScript(null)));
                    top["openMs"] = clock.ElapsedMilliseconds;
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
                    await Eventually(async () => await Eval(HjSettledScript) == "true", 30000, 200);
                    await Task.Delay(1000);
                    var middle = JObject.Parse(await Eval(HjSummaryScript(readTop)));
                    middle["openMs"] = clock.ElapsedMilliseconds;
                    middle["readBlock"] = readIndex;
                    results[$"{tag} middle"] = middle;
                    Sample($"{tag} top", top["drawnMs"]);
                    Sample($"{tag} middle", middle["drawnMs"]);

                    // Drawn twice more from the top and twice from the middle (left for the small document, nothing kept,
                    // back), for the drawing's time only.
                    foreach (var (place, share) in new[] { ("top", 0.0), ("top", 0.0), ("middle", 0.5), ("middle", 0.5) })
                    {
                        await Eval($"window.scrollTo(0, document.documentElement.scrollHeight * {share.ToString(System.Globalization.CultureInfo.InvariantCulture)}); 'ok'");
                        await Task.Delay(1200);
                        await c.Call("document.focus", new { documentId = small });
                        if (!await Eventually(Drawn, 30000, 200)) throw new CaseFailed($"{tag}: the small document not drawn");
                        await Task.Delay(500);
                        await Eval("window.__typedownKeptDocuments && window.__typedownKeptDocuments.clear(); window.__typedownProgressiveLog = []; 'ok'");
                        await c.Call("document.focus", new { documentId = id });
                        if (!await Eventually(async () => await Eval("String(!!window.__typedownMuya && !window.__typedownMuya.renderPending && (window.__typedownProgressiveLog || []).length > 0)") == "true", 120000, 200)) throw new CaseFailed($"{tag}: not drawn again from the {place} within 120 s");
                        await Eventually(async () => await Eval(HjSettledScript) == "true", 30000, 200);
                        await Task.Delay(500);
                        Sample($"{tag} {place}", JToken.Parse(await Eval("JSON.stringify(((window.__typedownProgressiveLog || []).slice(-1)[0] || {}).ms ?? null)")));
                    }

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
        double? Median(string key) => drawnSamples.TryGetValue(key, out var l) && l.Count > 0 ? l.OrderBy(x => x).ElementAt(l.Count / 2) : null;
        foreach (var (key, l) in drawnSamples) notes.Add($"{key}: drawn in {string.Join(", ", l)} ms, median {Median(key)}");
        File.WriteAllText(Path.Combine(outputDir, "hj01.json"), results.ToString());
        notes.Add("all: hj01.json beside the results");

        // With pretext: within the thresholds, no worse than the old estimate, drawn in at most 1.2 times its time. The old
        // estimate: outside at least one threshold for the 300k and 1M documents (the case tells them apart).
        bool Fails(JObject r, bool middle) => (double)r["mean"]! > HjMaxMean || (double)r["end"]! > HjMaxEnd || (middle && (double)r["thumb"]! > HjMaxThumb);
        foreach (var label in new[] { "100k", "300k", "1m" })
        {
            foreach (var place in new[] { "top", "middle" })
            {
                var p = (JObject)results[$"{label} pretext {place}"]!;
                var o = (JObject)results[$"{label} old {place}"]!;
                Check((double)p["mean"]! <= HjMaxMean, $"{label} {place}: the height is within {HjMaxMean}% of the real one on average while drawn ({p["mean"]}%; old {o["mean"]}%)");
                Check((double)p["mean"]! <= Math.Max((double)o["mean"]!, 0.5), $"{label} {place}: ... no worse than the old estimate ({p["mean"]}% / {o["mean"]}%)");
                Check((double)p["end"]! <= HjMaxEnd, $"{label} {place}: once drawn, within {HjMaxEnd}% of the real height ({p["end"]}%; old {o["end"]}%)");
                if (place == "middle") Check((double)p["thumb"]! <= HjMaxThumb, $"{label} middle: the thumb within {HjMaxThumb}% of where it belongs ({p["thumb"]}%; old {o["thumb"]}%)");
                if (Median($"{label} pretext {place}") is double pm && Median($"{label} old {place}") is double om)
                    Check(pm <= HjMaxSlower * om, $"{label} {place}: drawn in at most {HjMaxSlower} times the old estimate's time (medians of {drawnSamples[$"{label} pretext {place}"].Count}: {pm} vs {om} ms)");
                Check((double)p["maxGapMs"]! <= (double)o["maxGapMs"]! + 50, $"{label} {place}: the longest frame gap at most 50 ms over the old estimate's ({p["maxGapMs"]} vs {o["maxGapMs"]} ms)");
            }
            if (label == "100k") continue;
            var oldTop = (JObject)results[$"{label} old top"]!;
            var oldMiddle = (JObject)results[$"{label} old middle"]!;
            Check(Fails(oldTop, false) || Fails(oldMiddle, true), $"{label}: the old estimate is outside the thresholds (mean {oldTop["mean"]}% / {oldMiddle["mean"]}%, end {oldTop["end"]}%, thumb {oldMiddle["thumb"]}%) - the case tells the two apart");
        }
    }
    private static async Task HJ02(List<string> notes)
    {
        using var c = await Session("e2e HJ02");
        var small = await Open(c, Fixture("hj02-small.md", "# HJ02\n\nA short document.\n"));
        var windowId = await WindowIdOf(c, small);
        async Task<string> Eval(string script) => (string?)(await c.Call("test.editor.eval", new { windowId, script }))["result"] ?? "";
        var page = HjResource("heading-jump-page.js");
        var results = new JObject();
        var opened = new List<string>();
        async Task<JObject> Jump(string tag, double at)
        {
            await Eval(page);
            var started = JObject.Parse(await Eval($"JSON.stringify(window.__hjJump({at.ToString(System.Globalization.CultureInfo.InvariantCulture)}))"));
            if (!(bool)started["ok"]!) return started;
            // The heading chosen (after the load has placed its caret): the host makes the jump, as an outline click does.
            string slug = "";
            if (!await Eventually(async () => (slug = await Eval("window.__hjj && window.__hjj.hostJumps ? window.__hjj.slug : ''")) != "", 30000, 20)) throw new CaseFailed($"{tag}: no heading chosen within 30 s");
            await c.Call("test.outline.jump", new { windowId, slug });
            if (!await Eventually(async () => await Eval("String(!!(window.__hjj && window.__hjj.done))") == "true", 60000, 200)) throw new CaseFailed($"{tag}: the jump was not watched to its end within 60 s");
            return JObject.Parse(await Eval("JSON.stringify(window.__hjSummary())"));
        }
        try
        {
            foreach (var (label, file) in new[] { ("100k", "mixed-100k.md.gz"), ("300k", "mixed-300k.md.gz"), ("1m", "mixed-1m.md.gz") })
            {
                var text = HjFixture(file);
                foreach (var off in new[] { true, false })
                {
                    var mode = off ? "old" : "pretext";
                    var tag = $"{label} {mode}";
                    await Eval($"window.__typedownPretextOff = {(off ? "true" : "false")}; 'ok'");
                    var id = await Open(c, Fixture($"hj02-{label}-{mode}.md", text));
                    opened.Add(id);
                    // As soon as its first part is there: a heading three quarters down, not drawn yet.
                    await Eventually(async () => await Eval("String(!!(window.__typedownMuya && window.__typedownMuya.contentState.blocks.length > 50 && document.getElementById('ag-editor-id')?.children.length > 0))") == "true", 30000, 20);
                    await Eval("(()=>{const ps=window.__typedownPretextStats;if(ps)for(const k of Object.keys(ps))delete ps[k];return 'ok'})()");
                    var drawing = await Jump($"{tag} while drawn", 0.75);
                    var completed = await Eventually(async () => await Eval(HjPendingScript) == "true", 60000, 200);
                    Check(completed, $"{tag}: the drawing completes after the jump ({(completed ? "yes" : await Eval(HjStateScript))})");
                    if (!completed) throw new CaseFailed($"{tag}: not drawn within 60 s: {await Eval(HjStateScript)}");
                    if (!await Eventually(async () => await Eval(HjSettledScript) == "true", 30000, 200)) notes.Add($"{tag}: the guessed blocks not all predicted within 30 s: {await Eval(HjStateScript)}");
                    drawing["stats"] = JToken.Parse(await Eval("JSON.stringify(window.__typedownPretextStats||null)"));
                    var drawn = await Jump($"{tag} once drawn", 0.45);
                    results[$"{tag} drawing"] = drawing;
                    results[$"{tag} drawn"] = drawn;
                    notes.Add($"{tag}: while drawn {drawing.ToString(Newtonsoft.Json.Formatting.None)}");
                    notes.Add($"{tag}: once drawn {drawn.ToString(Newtonsoft.Json.Formatting.None)}");
                    try { await c.Call("document.close", new { documentId = id }); } catch { }
                    opened.Remove(id);
                }
            }
        }
        finally
        {
            try { await Eval("window.__typedownPretextOff = false; 'ok'"); } catch { }
            foreach (var id in opened) try { await c.Call("document.close", new { documentId = id }); } catch { }
            try { await c.Call("document.close", new { documentId = small }); } catch { }
        }
        File.WriteAllText(Path.Combine(outputDir, "hj02.json"), results.ToString());
        foreach (var label in new[] { "100k", "300k", "1m" })
        {
            foreach (var when in new[] { "drawing", "drawn" })
            {
                var p = (JObject)results[$"{label} pretext {when}"]!;
                var o = (JObject)results[$"{label} old {when}"]!;
                Check((bool)p["ok"]!, $"{label} {when}: the jump happened ({p["why"]})");
                if (!(bool)p["ok"]!) continue;
                Check((double)p["endOff"]! <= HjMaxLanding, $"{label} {when}: with predictions the heading ends within {HjMaxLanding} px of its place ({p["endOff"]} px; old {o["endOff"]})");
                Check(!(bool)o["ok"]! || (int)p["moves"]! <= (int)o["moves"]!, $"{label} {when}: ... and moves no more often than with the old estimate ({p["moves"]} vs {o["moves"]})");
            }
            var od = (JObject)results[$"{label} old drawn"]!;
            if ((bool?)od["long"] == true)
            {
                var oDrawing = (JObject)results[$"{label} old drawing"]!;
                var pDrawing = (JObject)results[$"{label} pretext drawing"]!;
                bool Worse(JObject j) => (bool)j["ok"]! && ((double)j["endOff"]! > HjMaxLanding || (int)j["moves"]! > (int)pDrawing["moves"]!);
                Check(Worse(oDrawing) || Worse(od), $"{label}: the old estimate lands farther off or moves more ({oDrawing["endOff"]} px/{oDrawing["moves"]} moves while drawn, {od["endOff"]} px/{od["moves"]} once drawn) - the case tells them apart");
            }
        }
    }
}
