using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Automation;

internal static partial class Program
{
    /// <summary>
    /// SA01 (R03): a save whose page does not hand over its text in time writes nothing. The host's copy can be older than
    /// what is on screen, and its hash as old: written, the file was marked saved with the last words typed missing.
    /// With the page's flush answers dropped (test.editor.dropFlush): Ctrl+S says the save failed and the file is as it
    /// was; auto-save holds it back and the status bar says why; answered again, Ctrl+S writes what was typed.
    /// </summary>
    private static async Task SA01(List<string> notes)
    {
        using var c = await Session("e2e SA01");
        var path = Fixture("sa01.md", "# SA01\n\nSome text here.\n");
        var before = File.ReadAllText(path);
        var id = await Open(c, path);
        var windowId = await WindowIdOf(c, id);
        var window = await WindowOf(windowId, c);
        var autoSave = (bool)(await c.Call("test.settings.get", new { windowId, name = "AutoSave" }))["value"]!;
        string SaveState()
        {
            var e = Deep.First(AutomationElement.FromHandle(window), new PropertyCondition(AutomationElement.AutomationIdProperty, "StatusBarSaveState"));
            return e?.Current.HelpText ?? "";
        }
        try
        {
            await c.Call("window.setView", new { windowId, statusBar = true, mode = "visual" });
            await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = false });
            await ClickEditorText(c, windowId, window, "Some text");
            TypeText(" typed");
            await Task.Delay(800);
            await c.Call("test.editor.dropFlush", new { drop = true });

            // Saved by hand: the save failed, said so, nothing written.
            Chord("Ctrl+S");
            var (title, content) = await PressDialogButton(c, windowId, DialogButton.Close, timeoutMs: 20000);
            notes.Add($"Ctrl+S with no answer: dialog '{title}': {content}");
            Check((content ?? "").Contains("did not hand over"), "Ctrl+S, the page not answering: the save says it failed");
            Check(File.ReadAllText(path) == before, "and the file is as it was");

            // Auto-save: held back, the status bar says why, nothing written.
            await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = true });
            // "here": the first click was in "Some text", and what was typed there split it.
            await ClickEditorText(c, windowId, window, "here");
            TypeText("!");
            await Task.Delay(9000);
            var state = SaveState();
            notes.Add($"auto-save with no answer: '{state.Replace("\n", " | ")}'");
            Check(state.Contains("did not hand over"), "auto-save, the page not answering: the status bar says why it did not save");
            Check(File.ReadAllText(path) == before, "and the file is as it was");

            // Answered again: Ctrl+S writes what is on screen.
            await c.Call("test.editor.dropFlush", new { drop = false });
            await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = false });
            await c.Call("test.editor.focus", new { windowId });
            Chord("Ctrl+S");
            await Eventually(() => Task.FromResult(File.ReadAllText(path).Contains("typed")), 5000);
            var after = File.ReadAllText(path);
            notes.Add("saved: " + after.Replace("\n", "\\n"));
            Check(after.Contains(" typed") && after.Contains('!'), "answered again, Ctrl+S writes what was typed");
        }
        finally
        {
            try { await c.Call("test.editor.dropFlush", new { drop = false }); } catch { }
            try { await c.Call("test.settings.set", new { windowId, name = "AutoSave", value = autoSave }); } catch { }
            try { await c.Call("document.close", new { documentId = id }); } catch { }
        }
    }
}
