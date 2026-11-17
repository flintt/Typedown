using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // What the page's isolation (Typedown PR #3: no --disable-web-security, no file access from files, an image-only
    // endpoint for local and http pictures) changes for documents that reached past pictures: local audio and video, a
    // local page in an iframe, a picture over the endpoint's 32 MB, and local files named in the person's own CSS.
    // It notes what loads and checks nothing about it - run on either side of PR #3, the two notes compare.
    private static async Task WS02(List<string> notes)
    {
        using var c = await Session("e2e WS02");
        var file = Fixture("ws02.md", "# WS02\n\n<audio controls src=\"ws02.wav\"></audio>\n\n<video controls src=\"ws02.wav\"></video>\n\n" +
            "<iframe src=\"ws02-frame.html\"></iframe>\n\n![big](ws02-big.bmp)\n\n![small](ws02-bg.png)\n\nText\n");
        var dir = Path.GetDirectoryName(file)!;
        File.WriteAllBytes(Path.Combine(dir, "ws02.wav"), Wav());
        File.WriteAllText(Path.Combine(dir, "ws02-frame.html"), "<!doctype html><script>parent.postMessage('ws02-frame', '*')</script><p>frame</p>");
        File.WriteAllBytes(Path.Combine(dir, "ws02-big.bmp"), Bmp(3500, 3500));
        File.WriteAllBytes(Path.Combine(dir, "ws02-bg.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var font = Path.Combine(dir, "ws02.ttf");
        File.Copy(@"C:\Windows\Fonts\arial.ttf", font, true);
        notes.Add($"big picture {new FileInfo(Path.Combine(dir, "ws02-big.bmp")).Length / 1024 / 1024} MB");

        var id = await Open(c, file);
        var windowId = await WindowIdOf(c, id);
        async Task<JToken?> Eval(string script) => (await c.Call("test.editor.eval", new { windowId, script }))["result"];
        // The small picture first: once it is drawn the page has had its turn at the others.
        Check(await Eventually(async () => (bool?)await Eval("[...document.querySelectorAll('#ag-editor-id img')].some(i => /ws02-bg/.test(decodeURIComponent(i.src)) && i.complete && i.naturalWidth === 1)") == true, 15000), "a small local picture is drawn");
        // The frame's message: listen, then load it again.
        await Eval("(() => { window.__ws02Frame = false; addEventListener('message', e => { if (e.data === 'ws02-frame') window.__ws02Frame = true }); document.querySelectorAll('#ag-editor-id iframe').forEach(f => { const s = f.src; f.src = 'about:blank'; f.src = s }) })()");
        await Task.Delay(8000);
        var page = await Eval(@"(() => {
            const q = s => document.querySelector('#ag-editor-id ' + s);
            const media = e => e ? { src: e.currentSrc || e.src, ready: e.readyState, error: e.error ? e.error.code : null, seconds: isFinite(e.duration) ? Math.round(e.duration * 10) / 10 : null } : 'not in the page';
            // By its alt: a local picture's address is the app's own (the endpoint since PR #3), not always its file's.
            const big = [...document.querySelectorAll('#ag-editor-id img')].find(i => i.alt === 'big' || /ws02-big/.test(decodeURIComponent(i.src)));
            const pictures = [...document.querySelectorAll('#ag-editor-id img')].map(i => (i.alt || '?') + ' ' + i.src.slice(0, 50) + ' ' + i.naturalWidth);
            const f = q('iframe');
            return { origin: location.origin, audio: media(q('audio')), video: media(q('video')),
                frame: f ? { src: f.src, loaded: !!window.__ws02Frame } : 'not in the page',
                big: big ? { complete: big.complete, width: big.naturalWidth } : 'not in the page', pictures };
        })()");
        notes.Add("document: " + page?.ToString(Newtonsoft.Json.Formatting.None));

        var before = (string?)(await c.Call("test.settings.get", new { windowId, name = "CustomCss" }))["value"] ?? "";
        try
        {
            var css = "@font-face { font-family: ws02font; src: url(\"" + new Uri(font).AbsoluteUri + "\"); }\n" +
                "#ag-editor-id { background-image: url(\"" + new Uri(Path.Combine(dir, "ws02-bg.png")).AbsoluteUri + "\"); }\n";
            await c.Call("test.settings.set", new { windowId, name = "CustomCss", value = css });
            await Task.Delay(1500);
            await Eval("(() => { window.__ws02Font = null; document.fonts.load('16px ws02font').then(f => window.__ws02Font = f.length ? 'loaded' : 'none', e => window.__ws02Font = 'error: ' + e.message) })()");
            await Eventually(async () => (await Eval("window.__ws02Font"))?.Type == JTokenType.String, 10000);
            await Task.Delay(1500);
            var styled = await Eval(@"({ font: window.__ws02Font, faces: [...document.fonts].filter(f => f.family.includes('ws02font')).map(f => f.status),
                background: getComputedStyle(document.querySelector('#ag-editor-id')).backgroundImage.slice(0, 40),
                backgroundFetched: performance.getEntriesByType('resource').filter(e => /ws02-bg/.test(decodeURIComponent(e.name))).map(e => e.initiatorType + ' ' + (e.responseStatus ?? '')) })");
            notes.Add("own CSS: " + styled?.ToString(Newtonsoft.Json.Formatting.None));
        }
        finally
        {
            await c.Call("test.settings.set", new { windowId, name = "CustomCss", value = before });
        }
    }

    // One second of silence, 8 kHz 8-bit mono.
    private static byte[] Wav()
    {
        const int samples = 8000;
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + samples); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(8000); w.Write((short)1); w.Write((short)8);
        w.Write("data"u8); w.Write(samples);
        for (var i = 0; i < samples; i++) w.Write((byte)128);
        w.Flush();
        return m.ToArray();
    }

    // A grey 24-bit BMP, uncompressed: width x height x 3 bytes.
    private static byte[] Bmp(int width, int height)
    {
        var row = (width * 3 + 3) / 4 * 4;
        var size = 54 + row * height;
        var b = new byte[size];
        Array.Fill(b, (byte)0x80, 54, size - 54);
        b[0] = (byte)'B'; b[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(b, 2);
        BitConverter.GetBytes(54).CopyTo(b, 10);
        BitConverter.GetBytes(40).CopyTo(b, 14);
        BitConverter.GetBytes(width).CopyTo(b, 18);
        BitConverter.GetBytes(height).CopyTo(b, 22);
        BitConverter.GetBytes((short)1).CopyTo(b, 26);
        BitConverter.GetBytes((short)24).CopyTo(b, 28);
        BitConverter.GetBytes(row * height).CopyTo(b, 34);
        return b;
    }
}
