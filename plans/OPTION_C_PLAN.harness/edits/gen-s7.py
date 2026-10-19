# S7: More filters (D15), the search field that holds the deep search's chip (D17), the report bar carrying the filters
# (D24) below the mark where it pins (D25), the box's close for the bar, and the empty state. Run from the v5 worktree root.
G = 'src/Kronikol/Reports/ReportGenerator.cs'
s = open(G, encoding='utf-8', newline='').read()

def sub(a, b, n=1):
    global s
    assert s.count(a) == n, (s.count(a), a[:90])
    s = s.replace(a, b)

# the rows' names for More filters, before the box
sub('''        var readoutText = $"All {ScenarioCount(''', '''        // More filters (S7, D15) names the rows the box has, in their order
        var rowsTitle = FilterRowsTitle(features, hasDurations, allDependencies.Count > 0);
        var readoutText = $"All {ScenarioCount(''')

# the box starts folded, carries its close for the bar, and its search field holds the deep search's chip (D17)
sub('<div class="filtering-box" id="filtering-box">', '<div class="filtering-box rows-folded" id="filtering-box">')
sub('''export_csv()">Export <span class="wide-only">Filtered </span>CSV</button></div></div>''',
    '''export_csv()">Export <span class="wide-only">Filtered </span>CSV</button>{FiltersClose}</div></div>''')
sub('''<div class="filter-search"><input id="searchbar" autocomplete="off" placeholder="Search... (@tag, $status, dep:, duration:, &&, ||, !!)" onkeyup="search_scenarios()" /><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button></div>''',
    '''<div class="filter-search"><span class="search-field"><input id="searchbar" autocomplete="off" placeholder="Search... (@tag, $status, dep:, duration:, &&, ||, !!)" onkeyup="search_scenarios()" /></span><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button>{MoreFilters(rowsTitle)}</div>''')

# the bar: the mark where it pins, then the tabs and the filters on one row
sub('''        body.Append(useTabs ? $"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}{tableMode}">""" : $"""<div class="toolbar-row{tableMode}">""");''',
    '''        // The bar pins below a mark that says where (D25), and carries the filters on its top row, right of the tabs (D24)
        if (useTabs) body.Append("""<div class="nav-mark" aria-hidden="true"></div>""");
        body.Append(useTabs ? $"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}{tableMode}">""" : $"""<div class="toolbar-row{tableMode}">""");''')
sub('''            body.Append("""<div class="report-tabs" role="tablist" aria-label="Report sections">""");''',
    '''            body.Append("""<div class="nav-row"><div class="report-tabs" role="tablist" aria-label="Report sections">""");''')
sub('''            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", startTab, diagnostics!.Count));
            body.Append("</div>");''',
    '''            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", startTab, diagnostics!.Count));
            body.Append("</div>");
            body.Append(NavFilters(readoutText, ScenarioCount(features.Sum(f => f.Scenarios.Length)))).Append("</div>");''')

# the empty state, first in the Features panel
sub('''        body.Append(useTabs ? ReportPanel("features", startTab) : """<div class="report-panel" id="panel-features" data-panel="features">""");''',
    '''        body.Append(useTabs ? ReportPanel("features", startTab) : """<div class="report-panel" id="panel-features" data-panel="features">""");
        body.Append(FiltersEmpty);''')

anchor = '''    /// <summary>The Features tab's views, Full (the cards) and Summary (the features table), as tabs (S4, D21).</summary>'''
helpers = '''    /// <summary>
    /// The rows More filters folds away (S7, D15), named in their order for its title: Status always, then Duration,
    /// Happy paths, Tags, Dependencies and Categories where the box has them.
    /// </summary>
    private static string FilterRowsTitle(Feature[] features, bool hasDurations, bool hasDependencies)
    {
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        var rows = new List<string> { "Status" };
        if (hasDurations) rows.Add("duration");
        if (runs.Any(s => s.IsHappyPath)) rows.Add("happy paths");
        if (runs.Any(s => s.IsHappyPath || s.Labels is { Length: > 0 })) rows.Add("tags");
        if (hasDependencies) rows.Add("dependencies");
        if (runs.Any(s => s.Categories is { Length: > 0 })) rows.Add("categories");
        return rows.Count == 1 ? rows[0] : string.Join(", ", rows.Take(rows.Count - 1)) + " and " + rows[^1];
    }

    /// <summary>
    /// More filters, at the end of the search line (S7, D15): it opens the rows, and counts the filters on in them while
    /// they are folded (report-filter-bar-script.js).
    /// </summary>
    private static string MoreFilters(string title) =>
        $"""<button type="button" class="more-filters" aria-controls="filter-rows" aria-expanded="false" title="{title}"><span class="more-filters-label">More filters</span><span class="more-filters-count" hidden></span><svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true" focusable="false"><path d="M4 6l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg></button>""";

    /// <summary>
    /// The report bar's filters (S7, D24): a copy of the readout, long and short, which the box's readout announces for
    /// it; Clear All while anything is filtered; and Filters, which takes the box into the bar while the bar floats.
    /// </summary>
    private static string NavFilters(string readout, string scenarios) =>
        $"""<div class="nav-filters" role="group" aria-label="Filters"><span class="nav-readout" aria-hidden="true"><span class="nav-readout-long">{readout}</span><span class="nav-readout-short">All {scenarios}</span></span><button type="button" class="nav-clear" title="Clear every filter" hidden>Clear All</button><button type="button" class="nav-filters-toggle" aria-expanded="false" aria-controls="filtering-box" title="Show the filter box in this bar, so it stays in view as you scroll"><svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M2.5 3h11l-4.25 5v4.25l-2.5 1.25V8z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg><span>Filters</span></button></div>""";

    /// <summary>The box's close while it rides in the bar (S7, D24), hidden until then.</summary>
    private const string FiltersClose =
        """<button type="button" class="filters-close" aria-label="Take the filters out of the bar" title="Put the filter box back above the bar (Esc)" hidden><svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M4 4l8 8M12 4l-8 8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg></button>""";

    /// <summary>What the Features panel says when the filters leave no scenario, with the way back (S7, D24).</summary>
    private const string FiltersEmpty =
        """<div class="filters-empty" hidden><span>No scenarios match the filters.</span><button type="button" class="filters-empty-clear">Clear All</button></div>""";

'''
sub(anchor, helpers + anchor)
open(G, 'w', encoding='utf-8', newline='').write(s)
print('ok')
