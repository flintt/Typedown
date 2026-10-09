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
        var found = root.FindFirst(TreeScope.Descendants, condition);
        return found ?? Walk(root, condition).FirstOrDefault();
    }

    public static List<AutomationElement> All(AutomationElement root, Condition condition)
    {
        var found = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>().ToList();
        return found.Count > 0 ? found : Walk(root, condition).ToList();
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
