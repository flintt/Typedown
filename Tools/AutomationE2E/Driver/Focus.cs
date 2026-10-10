using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// FO01: after switching to another window and back (Alt+Tab), and after the editor's context menu closed with Esc,
/// the next letter typed - no click - goes into the document. XamlUI's host moved the keyboard back into the page on
/// those occasions itself; the WinUI 3 web view control is left to do it.
/// </summary>
internal static partial class Program
{
    private static async Task FO01(List<string> notes)
    {
        using var c = await Session("e2e FO01");
        var id = await Open(c, Fixture("fo01.md", "# FO01\n\ntext\n"));
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        await TypeInto(c, id, "a");
        Check(await Eventually(async () => ((string?)(await Get(c, id))["text"] ?? "").Contains("texta"), 3000), "the first letter reached the document");
        var failures = new List<string>();
        async Task Probe(string step, char letter)
        {
            await Task.Delay(400);
            TypeChar(letter);
            var ok = await Eventually(async () => ((string?)(await Get(c, id))["text"] ?? "").Contains(letter), 2500);
            notes.Add($"{step}: '{letter}' {(ok ? "reached the document" : "went nowhere")}");
            if (!ok) failures.Add(step);
        }

        // 1. Another window in front, then this one again (as Alt+Tab does: no click in the window).
        using (var other = new ManualResetEventSlim())
        {
            System.Windows.Forms.Form? form = null;
            var thread = new Thread(() =>
            {
                form = new System.Windows.Forms.Form { Text = "FO01 other window", StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(40, 40), Size = new System.Drawing.Size(300, 200) };
                form.Shown += (_, _) => { form.Activate(); other.Set(); };
                System.Windows.Forms.Application.Run(form);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            other.Wait(5000);
            await Task.Delay(800);
            notes.Add("the other window in front: " + (GetForegroundWindow() != window));
            await Activate(window);
            form?.BeginInvoke(new Action(() => form.Close()));
            thread.Join(3000);
        }
        await Probe("back from another window", 'b');

        // 2. The context menu opened on the text and closed with Esc.
        await ContextMenuAt(c, windowId, window, "#ag-editor-id p");
        await Task.Delay(300);
        Send(Key(0x1B, false), Key(0x1B, true));
        await Probe("the context menu closed with Esc", 'c');

        Check(failures.Count == 0, "the letter typed next went into the document after: " + string.Join("; ", failures));
    }
}
