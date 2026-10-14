using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Automation;

/// <summary>
/// Finding elements anywhere under an element. UI Automation's own search of the descendants (FindFirst/FindAll with
/// TreeScope.Descendants) misses what a WinUI 3 window shows - its XAML content sits in a content island the search does
/// not always go into - while walking the children level by level finds it (a dialog's PrimaryButton, listed by a walk,
/// was not found by the search). So the search is tried first and the walk is the fallback; the walk skips a web view's
/// insides, the whole page, which no case looks for this way.
/// </summary>
internal static class Deep
{
    public static AutomationElement? First(AutomationElement root, Condition condition)
    {
        var found = Retry(() => Searched(root).Select(part => part.FindFirst(TreeScope.Descendants, condition)).FirstOrDefault(e => e != null));
        found ??= Walk(root, condition).FirstOrDefault();
        ReleaseNow();
        return found;
    }

    public static List<AutomationElement> All(AutomationElement root, Condition condition)
    {
        // Both, not the walk only when the search finds nothing: the search finds the window's own (Win32) parts, the
        // title bar's buttons say, and still misses the XAML ones.
        var found = Retry(() => Searched(root).SelectMany(part => part.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()).ToList());
        var ids = new HashSet<string>(found.Select(Id));
        foreach (var e in Walk(root, condition))
            if (ids.Add(Id(e))) found.Add(e);
        ReleaseNow();
        return found;
    }

    // The elements a search went through and the caller does not keep, let go of now, while what they stand for is still
    // on screen. Left to a later collection, UI Automation released them into the test host after a case had closed tabs
    // or windows, and a release reaching an automation peer already gone crashed the host (uiautomationcore's
    // ~ProviderInfo on a freed provider, fail-fast 0xc0000409: every case after it timed out).
    private static void ReleaseNow()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // Where UI Automation's own search goes: the root's children one by one, without a web view's window. Searched as a
    // whole, a window hiding its web view (the settings page shown) took the search into that window's legacy (MSAA)
    // accessible objects, one of which listed itself among its own descendants: the search never came back (FB01). The
    // root itself, when it is what is looked for, is matched by the walk.
    private static IEnumerable<AutomationElement> Searched(AutomationElement root)
    {
        AutomationElementCollection children;
        try { children = root.FindAll(TreeScope.Children, Condition.TrueCondition); } catch { yield break; }
        foreach (AutomationElement child in children)
        {
            string cls;
            try { cls = child.Current.ClassName ?? ""; } catch { continue; }
            if (cls.Contains("WebView2") || cls.StartsWith("Chrome_")) continue;
            yield return child;
        }
    }

    // A search over a tree that changes under it (a virtualized list dropping rows) can fail with the element gone:
    // asked again, a few times.
    private static T Retry<T>(Func<T> search)
    {
        for (var i = 0; ; i++)
        {
            try { return search(); }
            catch (ElementNotAvailableException) when (i < 4) { System.Threading.Thread.Sleep(100); }
        }
    }

    /// <summary>An element's name, empty when it is gone (a virtualized list drops rows while a whole tree is read).</summary>
    public static string Name(AutomationElement e)
    {
        try { return e.Current.Name ?? ""; } catch (ElementNotAvailableException) { return ""; }
    }

    private static string Id(AutomationElement e)
    {
        try { return string.Join(".", e.GetRuntimeId()); } catch { return System.Guid.NewGuid().ToString(); }
    }

    private static IEnumerable<AutomationElement> Walk(AutomationElement root, Condition condition)
    {
        var pending = new Stack<AutomationElement>();
        Push(pending, root);
        while (pending.Count > 0)
        {
            var e = pending.Pop();
            bool matches;
            try { matches = Matches(e, condition); } catch { continue; }
            if (matches) yield return e;
            string cls;
            try { cls = e.Current.ClassName ?? ""; } catch { continue; }
            if (cls.Contains("WebView2") || cls.StartsWith("Chrome_")) continue;
            Push(pending, e);
        }
    }

    private static void Push(Stack<AutomationElement> pending, AutomationElement parent)
    {
        AutomationElementCollection children;
        try { children = parent.FindAll(TreeScope.Children, Condition.TrueCondition); } catch { return; }
        for (var i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
    }

    private static bool Matches(AutomationElement e, Condition condition) => condition switch
    {
        PropertyCondition p => Equal(e.GetCurrentPropertyValue(p.Property), p.Value, (p.Flags & PropertyConditionFlags.IgnoreCase) != 0),
        AndCondition a => a.GetConditions().All(c => Matches(e, c)),
        OrCondition o => o.GetConditions().Any(c => Matches(e, c)),
        NotCondition n => !Matches(e, n.Condition),
        _ => condition == Condition.TrueCondition,
    };

    private static bool Equal(object? actual, object? wanted, bool ignoreCase)
    {
        if (actual is ControlType at && wanted is int id) return at.Id == id;
        if (wanted is ControlType wt) return actual is ControlType a && a.Id == wt.Id || actual is int ai && ai == wt.Id;
        if (actual is string s && wanted is string w) return ignoreCase ? string.Equals(s, w, System.StringComparison.OrdinalIgnoreCase) : s == w;
        return Equals(actual, wanted);
    }
}
