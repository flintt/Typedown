using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// MS01: a caret put by a click in the middle of a long document stays where the reader was editing across View >
/// Source mode and back - on the same line of the Markdown, and at the same height in the window. Each editor put back
/// its own last scroll position, and in source mode the caret was out of sight (a reader, 2026-11-05).
/// </summary>
internal static partial class Program
{
    private static async Task MS01(List<string> notes)
    {
        using var c = await Session("e2e MS01");
        var filler = string.Join("\n\n", Enumerable.Range(0, 60).Select(i => $"Paragraph {i} of the filler, long enough to wrap once or twice in the editor so that the two editors' heights differ."));
        var text = "# MS01\n\n" + filler + "\n\nThe paragraph the reader edits, with the ms01 marker in it.\n\n" + filler + "\n";
        var id = await Open(c, Fixture("ms01.md", text));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await WaitForPage(c, id, t => t.Contains("ms01 marker"), "the document");
        async Task<JToken?> Eval(string script) => (await c.Call("test.editor.eval", new { windowId, script }))["result"];
        async Task<string> PageMode() => (string?)await Eval("document.querySelector('.CodeMirror') ? 'source' : 'visual'") ?? "?";
        try
        {
            // The marker's word, scrolled into the window (about 60% down) and measured in one go, and measured again a
            // moment on: clicked only once it is in the window and has stopped moving - at first the editor's own scroll as
            // the document came put the page back (W10 job 197: the word at y 2461 of a page 591 high).
            const string word = "const p = [...document.querySelectorAll('#ag-editor-id p')].find(e => e.textContent.includes('ms01 marker')); if (!p) return null; const walker = document.createTreeWalker(p, NodeFilter.SHOW_TEXT); let n; while ((n = walker.nextNode()) && !n.textContent.includes('ms01')); const r = document.createRange(); const i = n.textContent.indexOf('ms01') + 2; r.setStart(n, i); r.setEnd(n, i + 1);";
            const string place = "(() => { " + word + " let b = r.getBoundingClientRect(); const want = window.innerHeight * 0.6; if (Math.abs(b.top - want) > 40) { p.scrollIntoView({ block: 'center' }); b = r.getBoundingClientRect(); } return { x: Math.round(b.left), y: Math.round(b.top + b.height / 2), h: window.innerHeight } })()";
            const string measure = "(() => { " + word + " const b = r.getBoundingClientRect(); return { x: Math.round(b.left), y: Math.round(b.top + b.height / 2), h: window.innerHeight } })()";
            JToken? at = null;
            for (var tries = 0; tries < 10 && at == null; tries++)
            {
                var first = await Eval(place);
                if (first == null || first.Type != JTokenType.Object) throw new CaseFailed("the marker is not on the page");
                await Task.Delay(500);
                var now = await Eval(measure);
                var y = (int)now!["y"]!;
                if (y > 20 && y < (int)now["h"]! - 20 && Math.Abs(y - (int)first["y"]!) <= 2) at = now;
            }
            if (at == null) throw new CaseFailed("the marker's word could not be scrolled into the window and kept there");
            notes.Add($"clicked at {at["x"]},{at["y"]} of a page {at["h"]} high");
            var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
            await Click(window, (int)screen["x"]!, (int)screen["y"]!);
            await Task.Delay(700);
            const string visualCaret = "(() => { const s = window.getSelection(); if (!s.rangeCount) return null; const r = s.getRangeAt(0).getBoundingClientRect(); return { top: Math.round(r.top), text: (s.anchorNode && s.anchorNode.textContent || '').slice(0, 80) } })()";
            const string sourceCaret = "(() => { const cm = document.querySelector('.CodeMirror').CodeMirror; const p = cm.getCursor(); const info = cm.getScrollInfo(); return { top: Math.round(cm.cursorCoords(p, 'window').top), text: cm.getLine(p.line), atEnd: info.top >= info.height - info.clientHeight - 1 } })()";
            var before = await Eval(visualCaret);
            Check(before != null && ((string)before["text"]!).Contains("ms01 marker"), $"the click put the caret in the paragraph ({before})");
            notes.Add($"visual: caret at {before!["top"]}");

            await ClickViewMenuEntry(window, "SourceCodeModeItem");
            for (var i = 0; i < 30 && await PageMode() != "source"; i++) await Task.Delay(150);
            Check(await PageMode() == "source", "View > Source mode shows source mode");
            await Task.Delay(800);
            var source = await Eval(sourceCaret);
            notes.Add($"source: caret at {source!["top"]} on '{((string)source["text"]!)[..Math.Min(50, ((string)source["text"]!).Length)]}'");
            Check(((string)source["text"]!).Contains("ms01 marker"), "in source mode the caret is on the paragraph's line");
            Check(Math.Abs((int)source["top"]! - (int)before["top"]!) <= 8, $"and at the same height in the window ({before["top"]} visual, {source["top"]} source)");

            await ClickViewMenuEntry(window, "SourceCodeModeItem");
            for (var i = 0; i < 30 && await PageMode() != "visual"; i++) await Task.Delay(150);
            Check(await PageMode() == "visual", "View > Source mode again shows the visual editor");
            await Task.Delay(1000);
            var back = await Eval(visualCaret);
            notes.Add($"visual again: caret at {back?["top"]}");
            Check(back != null && ((string)back["text"]!).Contains("ms01 marker"), "back in the visual editor the caret is in the paragraph");
            Check(back != null && Math.Abs((int)back["top"]! - (int)source["top"]!) <= 8, $"and at the same height ({source["top"]} source, {back?["top"]} visual)");
        }
        finally
        {
            if (await PageMode() == "source") await c.Call("test.settings.set", new { windowId, name = "SourceCode", value = false });
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
