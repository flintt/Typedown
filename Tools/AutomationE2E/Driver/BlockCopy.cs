using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// CB01 and CB02: the copy buttons of a display formula and of a table (the page's ContentState.copyBlock), and the
/// context menu's Copy table, used with the real mouse in the dark theme, as DG03 uses a diagram's.
/// </summary>
internal static partial class Program
{
    private const string Cb01Formula = "$$\n\\int_0^1 x^2 \\, dx = \\frac{1}{3}\n$$";
    // Written compact, as a reader writes it: the copy is the table as the document has it, not as the editor pads it.
    private const string Cb02Table = "|Name|**Score**|Note|\n|:-|:-:|-:|\n|*Ann*|`10`|[site](https://example.com)|\n|Bob|$x^2$|~~old~~|";

    /// <summary>
    /// CB01: a display formula's copy button. The pointer over the formula shows it (an inline formula has none); a click
    /// puts one item on the Windows clipboard: a picture of the formula (CF_DIB, mostly white: black on white in the dark
    /// theme), RTF with the picture (what Word takes before plain text), the formula's MathML and no HTML (Word would take
    /// that before all of them),
    /// and the formula's Markdown as the document has it ($$ ... $$) as text. The document, its revision and its saved
    /// state stay as they were and the formula's source is not opened. Where Word is installed the copy pastes as one
    /// equation Word can edit, not a picture (the app puts the formula's MathML there in the shape Word writes it itself,
    /// under "MathML" and "MathML Presentation"; a picture failed the reader, 2026-10-28). Pasted back into the document it is the formula again.
    /// Then, for the record only (ProbeWordMathML): what Word puts on the clipboard for an equation of its own, and what
    /// it makes of each form the app could put there.
    /// </summary>
    private static async Task CB01(List<string> notes)
    {
        using var c = await Session("e2e CB01");
        var path = Fixture("cb01.md", "# CB01\n\nBefore the formula, with $a+b$ inline.\n\n" + Cb01Formula + "\n\nAfter the formula.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        string? Eval(JToken r) => (string?)r["result"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            const string figure = "document.querySelector('#ag-editor-id figure[data-role=MULTIPLEMATH]')";
            var drawn = false;
            for (var i = 0; i < 50 && !drawn; i++)
            {
                await Task.Delay(200);
                drawn = Eval(await c.Call("test.editor.eval", new { windowId, script = $"String(!!{figure}?.querySelector('.ag-container-preview .katex'))" })) == "true";
            }
            Check(drawn, "the formula is drawn");
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = "String(!!document.querySelector('#ag-editor-id p .katex') && !document.querySelector('#ag-editor-id p .ag-diagram-copy'))" })) == "true",
                "the inline formula is drawn and has no copy button");
            var before = await Get(c, id);
            var text = (string)before["text"]!;
            var revision = await Revision(c, id);

            EmptyTheClipboard();
            await Activate(window);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await c.Call("test.editor.eval", new { windowId, script = $"{figure}.scrollIntoView({{ block: 'center' }})" });
            await Task.Delay(400);
            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-container-preview').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            var opacity = Eval(await c.Call("test.editor.eval", new { windowId, script = $"getComputedStyle({figure}.querySelector('.ag-diagram-copy')).opacity" }));
            notes.Add($"the button's opacity with the pointer over the formula: {opacity}");
            Check(double.TryParse(opacity, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var o) && o >= 0.4, $"the pointer over the formula shows its copy button (opacity {opacity})");

            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            await LeftClick();

            string? clip = null;
            for (var i = 0; i < 50 && clip != Cb01Formula; i++) { await Task.Delay(200); clip = ClipboardText(); }
            Check(clip == Cb01Formula, $"the clipboard's text is the formula's Markdown, $$ ... $$ (got {JsonConvert.SerializeObject(clip)})");
            var picture = ClipboardDib();
            notes.Add(picture == null ? "no CF_DIB on the clipboard" : $"CF_DIB {picture.Value.width}x{picture.Value.height}, {picture.Value.bits} bits, {picture.Value.white:P0} of the pixels white");
            notes.Add("clipboard formats: " + string.Join(", ", ClipboardFormatNames()));
            Check(picture is { width: >= 100, height: >= 40 }, "the clipboard holds a picture of the formula (CF_DIB, at least 100x40)");
            Check(picture is { white: >= 0.6 and < 1 }, "the picture is mostly white with ink on it: black on white, in the dark theme too");
            Check(ClipboardHasFormat("Rich Text Format"), "the picture is there as RTF too (Word takes it before the text)");
            Check(ClipboardHtml() == null, "no HTML on the clipboard (Word would take it before the picture)");
            // The formula's MathML, for Word's editable equations, as Word writes it: XML declaration, mml: prefix, no TeX annotation.
            var ours = CaptureClipboard();
            foreach (var f in ours.Where(f => f.name.Contains("MathML", StringComparison.OrdinalIgnoreCase)))
                notes.Add($"ours: {DescribeFormat(f)}");
            Check(ours.Any(f => f.name == "MathML" && Encoding.UTF8.GetString(f.bytes) is var m && m.StartsWith("<?xml") && m.Contains("<mml:math") && !m.Contains("annotation")),
                "the clipboard holds the formula's MathML as Word writes it (format \"MathML\": XML declaration, mml: prefix, no annotation)");
            notes.Add("the button shows it copied: " + Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.querySelector('.ag-diagram-copy').classList.contains('ag-code-copied'))" })));
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.classList.contains('ag-active'))" })) == "false", "the formula's source was not opened for editing");

            if (PasteIntoWord(System.IO.Path.GetDirectoryName(path)!) is { } word)
            {
                notes.Add($"Word: {word.shapes} picture(s), {word.media} embedded, {word.equations} equation(s); {word.detail}");
                notes.Add(word.equations >= 1 ? "Word made an EQUATION of the copy (the MathML was taken)" : "Word made a PICTURE of the copy (the MathML was not taken)");
                Check(word.equations == 1 && word.shapes == 0, "Word: the copy pastes as one equation Word can edit, not a picture");
            }
            else
                notes.Add("Word: not installed, not tried");

            await Task.Delay(800);
            var after = await Get(c, id);
            Check((string)after["text"]! == text, "the document's text is as it was");
            Check(await Revision(c, id) == revision, "the document's revision is as it was (no edit, nothing in its history)");
            Check((bool)after["saved"]! == (bool)before["saved"]!, "the document's saved state is as it was");

            // Pasted back into the document (the copy is still on the clipboard): the formula again.
            await PasteAtTheEnd(c, windowId, window);
            string pasted = "";
            for (var i = 0; i < 30 && Regex.Matches(pasted, Regex.Escape("\\int_0^1 x^2")).Count < 2; i++) { await Task.Delay(200); pasted = (string)(await Get(c, id))["text"]!; }
            notes.Add("after the paste: " + JsonConvert.SerializeObject(pasted));
            Check(Regex.Matches(pasted, Regex.Escape(Cb01Formula)).Count == 2 && !pasted.Contains("<img"), "pasting the copy back gives the formula's Markdown again");

            // For the record (notes, not checks): which clipboard forms Word makes an equation of. Word's own copy of an
            // equation first (made from the MathML as text, which it does take): the formats it puts on the clipboard and
            // what is in its MathML ones; then the formula's MathML under each name alone with the TeX as text, Word's own
            // MathML bytes with and without our picture and RTF, and our item as the app wrote it.
            if (Type.GetTypeFromProgID("Word.Application") != null)
                ProbeWordMathML(System.IO.Path.GetDirectoryName(path)!, ours, notes);
        }
        finally
        {
            if (themeBefore != null) await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore });
        }
    }

    /// <summary>
    /// CB02: a table's copy button and the context menu's Copy table. The pointer over the table shows the button just
    /// above its top right corner; a click puts one item on the clipboard: HTML (CF_HTML) with the table - three header
    /// cells in &lt;th&gt;, the columns' alignment, bold, italic, code and a link - and the table's Markdown as the
    /// document has it (compact, as written) as text; no picture. Word pastes it as one table of 3 rows and 3 columns
    /// with the header's texts. The document, its revision and saved state stay as they were. Right-clicked in a cell
    /// the menu has Copy table (on a paragraph it has not), which copies the same, in the visual editor and in reading
    /// mode; pasted back into the document it is the table's Markdown again, not its HTML turned into Markdown.
    /// </summary>
    private static async Task CB02(List<string> notes)
    {
        using var c = await Session("e2e CB02");
        var path = Fixture("cb02.md", "# CB02\n\nBefore the table.\n\n" + Cb02Table + "\n\nAfter the table.\n");
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var themeBefore = (int?)(await c.Call("test.settings.get", new { windowId, name = "AppTheme" }))["value"];
        string? Eval(JToken r) => (string?)r["result"];
        try
        {
            await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = 2 });
            const string figure = "document.querySelector('#ag-editor-id figure[data-role=TABLE]')";
            var drawn = false;
            for (var i = 0; i < 50 && !drawn; i++)
            {
                await Task.Delay(200);
                drawn = Eval(await c.Call("test.editor.eval", new { windowId, script = $"String(!!{figure}?.querySelector('tbody td'))" })) == "true";
            }
            Check(drawn, "the table is drawn");
            var before = await Get(c, id);
            var text = (string)before["text"]!;
            var revision = await Revision(c, id);

            EmptyTheClipboard();
            await Activate(window);
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            await c.Call("test.editor.eval", new { windowId, script = $"{figure}.scrollIntoView({{ block: 'center' }})" });
            await Task.Delay(400);
            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('tbody td').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            var place = Eval(await c.Call("test.editor.eval", new { windowId, script = $"(() => {{ const b = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(), t = {figure}.querySelector('table').getBoundingClientRect(); return JSON.stringify({{ opacity: +getComputedStyle({figure}.querySelector('.ag-diagram-copy')).opacity, right: Math.round(b.right - t.right), above: b.bottom <= t.top + 1 }}) }})()" }));
            notes.Add($"the button with the pointer over the table: {place}");
            var shown = JObject.Parse(place ?? "{}");
            Check((double?)shown["opacity"] >= 0.4, "the pointer over the table shows its copy button");
            Check(Math.Abs((int?)shown["right"] ?? 99) <= 2 && (bool?)shown["above"] == true, "the button sits just above the table's top right corner, over no cell");

            await MovePointerTo(await PagePointOnScreen(c, windowId, $"(() => {{ const r = {figure}.querySelector('.ag-diagram-copy').getBoundingClientRect(); return {{ x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }} }})()"));
            await LeftClick();
            await CheckTableOnClipboard("the button", notes);
            notes.Add("clipboard formats: " + string.Join(", ", ClipboardFormatNames()));
            Check(ClipboardDib() == null, "no picture on the clipboard");
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = $"String({figure}.classList.contains('ag-active'))" })) == "false", "no cell was opened for editing");

            if (PasteIntoWord(System.IO.Path.GetDirectoryName(path)!) is { } word)
            {
                var tables = string.Join("; ", word.tables.Select(t => $"{t.rows}x{t.columns} [{t.header}]"));
                notes.Add($"Word: {word.tables.Count} table(s) {tables}; {word.shapes} picture(s)");
                Check(word.tables.Count == 1 && word.tables[0] is { rows: 3, columns: 3, header: "Name|Score|Note" }, "Word: the copy pastes as one table of 3 rows and 3 columns with the header's texts");
            }
            else
                notes.Add("Word: not installed, not tried");

            await Task.Delay(800);
            var after = await Get(c, id);
            Check((string)after["text"]! == text, "the document's text is as it was");
            Check(await Revision(c, id) == revision, "the document's revision is as it was (no edit, nothing in its history)");
            Check((bool)after["saved"]! == (bool)before["saved"]!, "the document's saved state is as it was");

            // The context menu: on a paragraph no Copy table; in a cell Copy table, which copies the table - in the visual
            // editor and in reading mode. The page says where the right-click is as the button goes down
            // (ContextMenuTarget); the item shows by that.
            await MenuCopyTable(c, windowId, window, "visual", text, id, notes);
            await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = true });
            await Task.Delay(800);
            try { await MenuCopyTable(c, windowId, window, "reading", text, id, notes); }
            finally
            {
                await c.Call("test.settings.set", new { windowId, name = "ReadOnly", value = false });
                await Task.Delay(800);
            }

            // Pasted back into the document: the table's Markdown again - its alignment, its formula and its bold - not
            // its HTML turned into Markdown.
            await PasteAtTheEnd(c, windowId, window);
            string pasted = "";
            for (var i = 0; i < 30 && Regex.Matches(pasted, Regex.Escape("$x^2$")).Count < 2; i++) { await Task.Delay(200); pasted = (string)(await Get(c, id))["text"]!; }
            notes.Add("after the paste: " + JsonConvert.SerializeObject(pasted));
            Check(Eval(await c.Call("test.editor.eval", new { windowId, script = "String(document.querySelectorAll('#ag-editor-id figure[data-role=TABLE]').length)" })) == "2", "pasting the copy back makes a second table");
            Check(Regex.Matches(pasted, Regex.Escape("$x^2$")).Count == 2 && Regex.Matches(pasted, @"\|\s*:-+:\s*\|").Count == 2 && Regex.Matches(pasted, Regex.Escape("**Score**")).Count == 2 && !pasted.Contains("<t"),
                "pasting the copy back gives the table's Markdown (alignment, formula and bold as they were)");
        }
        finally
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            if (themeBefore != null) await c.Call("test.settings.set", new { windowId, name = "AppTheme", value = themeBefore });
        }
    }

    private const uint CfUnicodeText = 13, CfDib = 8;

    /// <summary>A captured clipboard format, for the notes: its name, size, encoding and start.</summary>
    private static string DescribeFormat((uint format, string name, byte[] bytes) f)
    {
        var utf16 = f.bytes.Length > 1 && f.bytes[1] == 0;
        var shown = utf16 ? Encoding.Unicode.GetString(f.bytes) : Encoding.UTF8.GetString(f.bytes);
        shown = shown.TrimEnd('\0');
        return $"\"{f.name}\" {f.bytes.Length} bytes, {(utf16 ? "UTF-16" : "8-bit")}, {(f.bytes.Length > 0 && f.bytes[^1] == 0 ? "zero-ended" : "not zero-ended")}: {JsonConvert.SerializeObject(shown.Length > 160 ? shown[..160] + "..." : shown)}";
    }

    /// <summary>
    /// The clipboard's formats that are memory (text, CF_DIB, RTF, HTML, registered ones) with their bytes; handles
    /// (bitmaps, metafiles) and the text forms Windows makes from CF_UNICODETEXT are left out.
    /// </summary>
    private static List<(uint format, string name, byte[] bytes)> CaptureClipboard(Func<string, bool>? wanted = null)
    {
        var formats = new List<(uint, string, byte[])>();
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                if (format is 1 or 2 or 3 or 7 or 9 or 14 or 16 or 17 || format >= 0x80 && format < 0xC000) continue;
                var name = new StringBuilder(128);
                var named = GetClipboardFormatName(format, name, name.Capacity) > 0 ? name.ToString() : format.ToString();
                if (wanted != null && !wanted(named)) continue;
                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                var size = (int)GlobalSize(handle);
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    formats.Add((format, named, bytes));
                }
                finally { GlobalUnlock(handle); }
            }
        }
        finally { CloseClipboard(); }
        return formats;
    }

    /// <summary>These formats, and only these, put on the clipboard as one item.</summary>
    private static void PutFormats(IEnumerable<(uint format, byte[] bytes)> formats)
    {
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            EmptyClipboard();
            foreach (var (format, bytes) in formats) SetClipboardBytes(format, bytes);
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// CB01's notes on Word and MathML: what Word itself puts on the clipboard for an equation, and what it makes of the
    /// forms the app could put there. <paramref name="ours"/>: the app's copy of the formula, captured.
    /// </summary>
    private static void ProbeWordMathML(string folder, List<(uint format, string name, byte[] bytes)> ours, List<string> notes)
    {
        var tex = ours.FirstOrDefault(f => f.format == CfUnicodeText).bytes ?? Encoding.Unicode.GetBytes("x\0");
        var ourMathML = ours.FirstOrDefault(f => f.name == "MathML").bytes;
        if (ourMathML == null) { notes.Add("probe: no MathML of ours to try"); return; }
        var mathml = Encoding.UTF8.GetString(ourMathML).TrimEnd('\0');
        var picture = ours.Where(f => f.format == CfDib || f.name == "Rich Text Format").Select(f => (f.format, f.bytes)).ToList();
        (uint, byte[]) Named(string name, byte[] bytes) => (RegisterClipboardFormat(name), bytes);
        string Paste(string label)
        {
            try
            {
                var word = PasteIntoWord(folder);
                var line = $"probe {label}: {word?.equations} equation(s), {word?.shapes} picture(s), {word?.tables.Count} table(s)";
                notes.Add(line);
                return line;
            }
            catch (Exception ex) { notes.Add($"probe {label}: {ex.GetType().Name} {ex.Message}"); return ""; }
        }

        // Word's own copy of an equation.
        var reference = new List<(uint format, string name, byte[] bytes)>();
        var type = Type.GetTypeFromProgID("Word.Application")!;
        // As PasteIntoWord: a Word already running (the reader's) is left as it is, only the document made here closed.
        var before = System.Diagnostics.Process.GetProcessesByName("WINWORD").Select(p => p.Id).ToHashSet();
        dynamic word = Activator.CreateInstance(type)!;
        var own = System.Diagnostics.Process.GetProcessesByName("WINWORD").Any(p => !before.Contains(p.Id));
        dynamic? document = null;
        try
        {
            if (own) word.Visible = false;
            document = word.Documents.Add();
            PutFormats(new[] { (CfUnicodeText, Encoding.Unicode.GetBytes(mathml + "\0")) });
            document.Content.Paste();
            if ((int)document.OMaths.Count > 0)
            {
                document.OMaths[1].Range.Copy();
                Thread.Sleep(500);
                notes.Add("Word's copy of an equation: " + string.Join(", ", ClipboardFormatNames()));
                reference = CaptureClipboard(name => name.Contains("MathML", StringComparison.OrdinalIgnoreCase));
                foreach (var f in reference) notes.Add("Word's: " + DescribeFormat(f));
            }
            else notes.Add("probe: Word made no equation of the MathML as text this time");
            document.Close(0);
            document = null;
        }
        catch (Exception ex) { notes.Add($"probe: Word's copy failed: {ex.GetType().Name} {ex.Message}"); }
        finally
        {
            if (document != null) document.Close(0);
            if (own) word.Quit(0);
            Marshal.FinalReleaseComObject(word);
        }

        var ourBytes = Encoding.UTF8.GetBytes(mathml + "\0");
        foreach (var name in new[] { "MathML", "MathML Presentation", "application/mathml+xml" })
        {
            PutFormats(new[] { (CfUnicodeText, tex), Named(name, ourBytes) });
            Paste($"TeX text + our MathML as \"{name}\"");
        }
        PutFormats(new[] { (CfUnicodeText, tex) }.Concat(new[] { "MathML", "MathML Presentation", "application/mathml+xml" }.Select(n => Named(n, ourBytes))));
        Paste("TeX text + our MathML under all three names");
        if (reference.Count > 0)
        {
            PutFormats(new[] { (CfUnicodeText, tex) }.Concat(reference.Select(f => (f.format, f.bytes))));
            Paste("TeX text + Word's own MathML formats");
            PutFormats(new[] { (CfUnicodeText, tex) }.Concat(picture).Concat(reference.Select(f => (f.format, f.bytes))));
            Paste("TeX text + our picture and RTF + Word's own MathML formats");
        }
        PutFormats(ours.Where(f => !f.name.StartsWith("Ole") && f.name != "DataObject").Select(f => (f.format, f.bytes)));
        Paste("our item as the app wrote it, put again");
    }

    /// <summary>
    /// CB02's menu: right-clicked on a paragraph it has no Copy table; in a cell it has, and the item copies the table.
    /// A failure says what the menu had and what the page told the host of the right-click.
    /// </summary>
    private static async Task MenuCopyTable(Client c, string windowId, IntPtr window, string mode, string text, string id, List<string> notes)
    {
        async Task<string> Told() => (string?)(await c.Call("test.editor.eval", new { windowId, script = "JSON.stringify(window.__typedownContextMenuTarget ?? null)" }))["result"] ?? "?";
        var paragraph = await ContextMenuAt(c, windowId, window, "#ag-editor-id p");
        var toldParagraph = await Told();
        Send(Key(0x1B, false), Key(0x1B, true));
        await Task.Delay(400);
        Check(!paragraph.ContainsKey("CopyTableItem"), $"{mode}: the menu on a paragraph has no Copy table (the page told the host {toldParagraph}; the menu had {string.Join(", ", paragraph.Keys)})");
        EmptyTheClipboard();
        var menu = await ContextMenuAt(c, windowId, window, "#ag-editor-id figure[data-role=TABLE] tbody td", "CopyTableItem");
        var told = await Told();
        notes.Add($"{mode}: menu in a cell: {string.Join(", ", menu.Keys)}; the page told the host {told}");
        if (!menu.TryGetValue("CopyTableItem", out var copyTable) || !copyTable.Current.IsEnabled)
        {
            Send(Key(0x1B, false), Key(0x1B, true));
            throw new CaseFailed($"{mode}: the menu in a table cell has no Copy table{(copyTable != null ? " enabled" : "")}: the page told the host {told} (table true = the item should show); the menu had {string.Join(", ", menu.Keys)}; screen: {Screenshot("cb02-menu-" + mode)}");
        }
        ((System.Windows.Automation.InvokePattern)copyTable.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
        await CheckTableOnClipboard($"{mode}: the menu's Copy table", notes);
        await Task.Delay(500);
        Check((string)(await Get(c, id))["text"]! == text, $"{mode}: the document's text is as it was after the menu's copy");
    }

    /// <summary>The table copy of CB02 on the clipboard: its Markdown as text, and HTML with the table, its header row, alignment and formatting.</summary>
    private static async Task CheckTableOnClipboard(string how, List<string> notes)
    {
        string? clip = null;
        for (var i = 0; i < 50 && clip != Cb02Table; i++) { await Task.Delay(200); clip = ClipboardText(); }
        Check(clip == Cb02Table, $"{how}: the clipboard's text is the table as the document has it (got {JsonConvert.SerializeObject(clip)})");
        var html = ClipboardHtml() ?? "";
        notes.Add($"{how}: HTML {JsonConvert.SerializeObject(html.Length > 900 ? html[..900] : html)}");
        Check(html.Contains("<table") && Regex.Matches(html, "<th[ >]").Count == 3 && Regex.Matches(html, "<tr[ >]").Count == 3,
            $"{how}: the clipboard's HTML is the table: a header row of 3 <th> and 3 rows");
        Check(html.Contains("align=\"left\"") && html.Contains("align=\"center\"") && html.Contains("align=\"right\""), $"{how}: with each column's alignment");
        Check(html.Contains("<strong>Score</strong>") && html.Contains("<em>Ann</em>") && html.Contains("<code>10</code>") && html.Contains("href=\"https://example.com\""),
            $"{how}: with the cells' bold, italic, code and link");
    }

    /// <summary>Ctrl+End, Enter and Ctrl+V in the editor: the clipboard pasted in a new paragraph at the document's end.</summary>
    private static async Task PasteAtTheEnd(Client c, string windowId, IntPtr window)
    {
        await Activate(window);
        await c.Call("test.editor.focus", new { windowId });
        await Task.Delay(200);
        Send(Key(0x11, false), Key(0x23, false), Key(0x23, true), Key(0x11, true));
        Keys((ushort)0x0D, ("ctrl", (ushort)0x56));
    }

    /// <summary>Whether the clipboard holds the format of that name (a registered one: "Rich Text Format", "HTML Format").</summary>
    private static bool ClipboardHasFormat(string name) => IsClipboardFormatAvailable(RegisterClipboardFormat(name));

    /// <summary>The names of the formats on the clipboard, for the notes (standard ones by number).</summary>
    private static List<string> ClipboardFormatNames()
    {
        var names = new List<string>();
        for (var i = 0; i < 10 && !OpenClipboard(IntPtr.Zero); i++) Thread.Sleep(50);
        try
        {
            for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                var name = new StringBuilder(128);
                names.Add(GetClipboardFormatName(format, name, name.Capacity) > 0 ? name.ToString() : format.ToString());
            }
        }
        finally { CloseClipboard(); }
        return names;
    }

    private static void SetClipboardBytes(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        var pointer = GlobalLock(memory);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == IntPtr.Zero) throw new CaseFailed($"the clipboard did not take format {format}");
    }

    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
}
