using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// DG03: a diagram's copy button, used with the real mouse in the dark theme. The pointer over the Mermaid diagram shows
/// the button; a click on it puts one item on the Windows clipboard holding a picture (CF_DIB) of the diagram - a
/// sizeable one, mostly white: drawn in the light theme on white, not the dark theme's light lines on nothing - and the
/// text, the block's Markdown fenced with its language. The document, its revision and its saved state stay as they
/// were, and the diagram's source is not opened for editing. Where Word is installed, the copy pasted into it is a picture.
/// </summary>
internal static partial class Program
{
    private static async Task DG03(List<string> notes)
    {
        using var c = await Session("e2e DG03");
        const string block = "```mermaid\ngraph TD\n  A[Start] --> B[Finish]\n```";
        var path = Fixture("dg03.md", "# DG03\n\nBefore the diagram.\n\n" + block + "\n\nAfter the diagram.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            string? Eval(JToken r) => (string?)r["result"];
            var drawn = false;
            for (var i = 0; i < 50 && !drawn; i++)
            {
                await Task.Delay(200);
                drawn = Eval(await c.Call("test.editor.eval", new { windowId, script = "String(!!document.querySelector('#ag-editor-id figure[data-role=MERMAID] .ag-container-preview svg'))" })) == "true";
            }
            Check(drawn, "the Mermaid diagram is drawn");
            var before = await Get(c, id);
            var text = (string)before["text"]!;
            var revision = await Revision(c, id);
            notes.Add($"before: revision {revision}, saved {before["saved"]}");

            EmptyTheClipboard();
            await Activate(window);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            async Task<(int x, int y)> ScreenOf(string script)
            {
                var at = (await c.Call("test.editor.eval", new { windowId, script }))["result"]!;
                if (at.Type != JTokenType.Object) throw new CaseFailed($"no element for {script}: {at}");
                var screen = await c.Call("test.editor.screenPoint", new { windowId, x = (int)at["x"]!, y = (int)at["y"]! });
                return ((int)screen["x"]!, (int)screen["y"]!);
            }
            // As a mouse moves: put somewhere, then nudged across a few pixels so the page sees the pointer arrive.
            async Task MoveTo((int x, int y) to)
            {
                SetCursorPos(to.x - 12, to.y);
                for (var i = 0; i < 6; i++)
                {
                    await Task.Delay(40);
                    Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dx = 2, dy = 0, dwFlags = 0x0001 } } });
                }
                await Task.Delay(400);
            }
            const string figure = "document.querySelector('#ag-editor-id figure[data-role=MERMAID]')";
            await c.Call("test.editor.eval", new { windowId, script = $"{figure}.scrollIntoView({{ block: 'center' }})" });
            await Task.Delay(400);
            await MoveTo(await ScreenOf($"(() => {{ const r = {figure}.querySelector('.ag-container-preview').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            var opacity = Eval(await c.Call("test.editor.eval", new { windowId, script = $"getComputedStyle({figure}.querySelector('.ag-diagram-copy')).opacity" }));
            notes.Add($"the button's opacity with the pointer over the diagram: {opacity}");
            Check(double.TryParse(opacity, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o) && o >= 0.4, $"the pointer over the diagram shows its copy button (opacity {opacity})");

            await MoveTo(await ScreenOf($"(() => {{ const r = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0002 } } });
            await Task.Delay(80);
            Send(new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0004 } } });

            string? clip = null;
            for (var i = 0; i < 50 && clip != block; i++) { await Task.Delay(200); clip = ClipboardText(); }
            Check(clip == block, $"the clipboard's text is the block's Markdown fenced with its language (got {JsonConvert.SerializeObject(clip)})");
            var picture = ClipboardDib();
            notes.Add(picture == null ? "no CF_DIB on the clipboard" : $"CF_DIB {picture.Value.width}x{picture.Value.height}, {picture.Value.bits} bits, {picture.Value.white:P0} of the pixels white");
            Check(picture != null, "the clipboard holds a picture (CF_DIB)");
            Check(picture is { width: >= 100, height: >= 100 }, "the picture is of a sizeable diagram (at least 100x100)");
            Check(picture is { white: >= 0.6 }, "the picture is mostly white: the light theme on white, in the dark theme too");
            var copied = Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.querySelector('.ag-diagram-copy').classList.contains('ag-code-copied'))" }));
            notes.Add($"the button shows it copied: {copied}");
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.classList.contains('ag-active'))" })) == "false", "the diagram's source was not opened for editing");

            // Pasted into Word (only where it is installed): the picture, not the text - Word takes the bitmap before
            // plain text.
            if (PasteIntoWord(System.IO.Path.GetDirectoryName(path)!) is { } word)
            {
                notes.Add($"Word: {word.shapes} picture(s) pasted, {word.media} embedded; {word.detail}");
                Check(word.shapes == 1 && word.media == 1, "Word: the copy pastes as one picture");
            }
            else
                notes.Add("Word: not installed, not tried");

            await Task.Delay(800);
            var after = await Get(c, id);
            Check((string)after["text"]! == text, "the document's text is as it was");
            Check(await Revision(c, id) == revision, "the document's revision is as it was (no edit, nothing in its history)");
            Check((bool)after["saved"]! == (bool)before["saved"]!, "the document's saved state is as it was");
        }
        finally
        {
            if (themeBefore != null) await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore });
        }
    }

    private static void EmptyTheClipboard()
    {
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// The clipboard's picture as a device-independent bitmap (CF_DIB): its size, bits per pixel and the share of its
    /// pixels that are white or nearly (every channel 235 or more, sampled on a grid); null when there is none or it is
    /// not a plain 24 or 32 bit one.
    /// </summary>
    private static (int width, int height, int bits, double white)? ClipboardDib()
    {
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            var handle = GetClipboardData(8);
            if (handle == IntPtr.Zero) return null;
            var pointer = GlobalLock(handle);
            try
            {
                var size = (int)GlobalSize(handle);
                var data = new byte[size];
                Marshal.Copy(pointer, data, 0, size);
                var headerSize = BitConverter.ToInt32(data, 0);
                var width = BitConverter.ToInt32(data, 4);
                var height = BitConverter.ToInt32(data, 8);
                var bits = BitConverter.ToInt16(data, 14);
                var compression = BitConverter.ToInt32(data, 16);
                var colours = BitConverter.ToInt32(data, 32);
                if (bits != 24 && bits != 32 || compression != 0 && compression != 3) return (width, Math.Abs(height), bits, 0);
                // With BI_BITFIELDS a 40-byte header is followed by three colour masks.
                var offset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + colours * 4;
                var stride = (width * bits / 8 + 3) & ~3;
                var rows = Math.Abs(height);
                int white = 0, all = 0;
                for (var y = 0; y < rows; y += Math.Max(1, rows / 60))
                    for (var x = 0; x < width; x += Math.Max(1, width / 60))
                    {
                        var at = offset + y * stride + x * bits / 8;
                        if (at + 2 >= data.Length) continue;
                        all++;
                        if (data[at] >= 235 && data[at + 1] >= 235 && data[at + 2] >= 235) white++;
                    }
                return (width, rows, bits, all == 0 ? 0 : (double)white / all);
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
}
