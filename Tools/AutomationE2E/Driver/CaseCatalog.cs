using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Which cases a run takes when it is asked by selector instead of by name: <c>quick</c>, <c>outline</c>, or several at
/// once with names (<c>quick,menus,DG01</c>; <c>@quick</c> works too when the driver is started directly). Every case has a tier - quick (often broken before, or in what
/// the WinUI 3 move keeps breaking: focus and keys, window chrome, outline, themes, menus) or full only - and the areas
/// it touches. A fix runs @quick and the areas of what it changed; a push for CI, a package or a release runs everything.
/// </summary>
internal static class CaseCatalog
{
    private sealed record Entry(bool Quick, string[] Areas);

    private static Entry Q(params string[] areas) => new(true, areas);

    private static Entry F(params string[] areas) => new(false, areas);

    // The areas: api (the automation API), keys (focus and the keyboard), outline, window (full screen, minimize, DPI,
    // the frame), theme, menus, files (tabs, backups, the file tree, links), paste (web pages and pictures in), export,
    // content (what the editor keeps of a document), perf (measurements: only when named), edition (an edition's own).
    private static readonly Dictionary<string, Entry> Cases = new()
    {
        ["S0"] = F("api"),
        ["S1"] = Q("api", "keys"),
        ["R01"] = F("api"),
        ["R02"] = F("api"),
        ["R03"] = Q("api", "keys"),
        ["B01"] = F("api"),
        ["S2"] = F("api"),
        ["S3"] = Q("api", "menus"),
        ["V01"] = F("api", "window"),
        ["N01"] = F("api", "files"),
        ["K01"] = F("api", "keys"),
        ["B02"] = F("api"),
        ["F01"] = Q("api", "content", "keys"),
        ["M01"] = F("api", "content"),
        ["P01"] = F("api", "window"),
        ["W01"] = F("api"),
        ["W02"] = F("api"),
        ["W03"] = F("api"),
        ["MC01"] = F("api"),
        ["EX01"] = F("api"),
        ["EQ01"] = F("api"),
        ["R04"] = Q("files", "api"),
        ["C01"] = Q("api", "files"),
        ["Q02"] = F("window"),
        ["D01"] = F("api"),
        ["K02"] = Q("keys", "menus"),
        ["K04"] = Q("keys"),
        ["K05"] = Q("keys", "files"),
        ["K06"] = Q("keys", "menus", "outline"),
        ["K03"] = F("keys", "menus"),
        ["FS01"] = Q("window"),
        ["FS03"] = Q("window", "menus"),
        ["FS02"] = Q("window"),
        ["PU01"] = F("content", "export"),
        ["RV01"] = F("api"),
        ["IU01"] = F("paste", "menus"),
        ["IU03"] = F("paste"),
        ["IU02"] = F("paste"),
        ["IU04"] = F("paste"),
        ["IN01"] = F("paste", "files"),
        ["VI01"] = F("keys"),
        ["VI02"] = F("keys"),
        ["TC01"] = Q("outline", "files"),
        ["TC02"] = Q("outline", "files"),
        ["SE01"] = F("files"),
        ["WP02"] = F("paste"),
        ["EX02"] = F("export"),
        ["TB01"] = Q("content", "menus"),
        ["TH01"] = Q("theme"),
        ["RD01"] = Q("keys", "menus", "content"),
        ["CP01"] = F("export", "paste"),
        ["TH03"] = Q("theme", "outline"),
        ["ES01"] = Q("keys", "menus"),
        ["ES02"] = Q("keys"),
        ["TI01"] = F("window"),
        ["TE01"] = F("window"),
        ["MN01"] = F("window", "api"),
        ["LK01"] = Q("files"),
        ["FL01"] = F("menus"),
        ["FO01"] = Q("keys", "menus"),
        ["TH04"] = F("theme", "window"),
        ["FT01"] = F("files"),
        ["MN02"] = Q("window", "keys"),
        ["TH05"] = Q("theme"),
        // Fails as a known issue (restored without activation, the editor has no size): in full runs only.
        ["MN03"] = F("window"),
        ["MN04"] = Q("window"),
        ["MN05"] = F("window", "files"),
        ["DP01"] = Q("window"),
        ["DR01"] = Q("files", "paste"),
        ["DM01"] = Q("theme", "window"),
        ["TH02"] = F("theme", "menus"),
        ["WP01"] = Q("paste"),
        ["ER01"] = F("api"),
        ["DG01"] = Q("theme", "menus"),
        ["PM01"] = Q("menus"),
        ["OL02"] = Q("outline"),
        ["OL01"] = Q("outline"),
        ["LD01"] = F("content", "files"),
        ["PF01"] = F("perf"),
        ["PF03"] = F("perf"),
        ["PF02"] = F("perf"),
        ["Q01"] = F("window"),
        // An edition's own cases (Edition.<name>.cs in the edition's repository): a name no case has is simply not run.
        ["TY01"] = F("edition", "menus"),
        ["TY02"] = Q("edition", "menus"),
        ["TY03"] = Q("edition", "theme"),
        ["TY04"] = F("edition"),
        ["TY05"] = Q("edition"),
        ["ST01"] = Q("edition", "theme"),
        ["SP01"] = F("edition"),
        ["PR01"] = Q("edition"),
        ["PR02"] = F("edition"),
        ["PR03"] = F("edition"),
    };

    public static IEnumerable<string> Areas => Cases.Values.SelectMany(e => e.Areas).Distinct().OrderBy(a => a);

    /// <summary>
    /// The names a run takes from <paramref name="selection"/>: case names as they are, @quick, @all (everything,
    /// null), or @&lt;area&gt;. An unknown selector is an error that lists the known ones.
    /// </summary>
    public static string[]? Expand(string[] selection)
    {
        var names = new List<string>();
        foreach (var item in selection.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            // A selector is @word or a word in lower case (case names are capitals and digits): the scripts hand the
            // list on through PowerShell, where a word starting with @ is splatting and the list arrived empty.
            var isSelector = item.StartsWith("@") || item.All(ch => ch >= 'a' && ch <= 'z');
            if (!isSelector) { names.Add(item); continue; }
            var selector = item.TrimStart('@').ToLowerInvariant();
            if (selector == "all") return null;
            var chosen = selector == "quick"
                ? Cases.Where(c => c.Value.Quick).Select(c => c.Key).ToList()
                : Cases.Where(c => c.Value.Areas.Contains(selector)).Select(c => c.Key).ToList();
            if (chosen.Count == 0)
                throw new ArgumentException($"unknown selector {item}: @quick, @all, or an area ({string.Join(", ", Areas.Select(a => "@" + a))})");
            names.AddRange(chosen);
        }
        return names.Distinct().ToArray();
    }

    /// <summary>A case that has no entry here: it runs in full runs, but no selector finds it until it is given one.</summary>
    public static bool Knows(string caseName) => Cases.ContainsKey(caseName.Split(' ')[0]);
}
