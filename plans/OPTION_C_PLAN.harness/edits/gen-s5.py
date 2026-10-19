# S5 and S6: the filter box's rows, readout, exports, search help (D6 to D14, D16, D19), the runs a parameterized
# scenario holds, and the marker-only participant no longer a dependency. Run from the v5 worktree root.
G = 'src/Kronikol/Reports/ReportGenerator.cs'
s = open(G, 'rb').read().decode('utf-8')

def sub(old, new, count=1):
    global s
    assert s.count(old) == count, (s.count(old), old[:120])
    s = s.replace(old, new)

# 1. the box's head: the readout, the exports' wide-only word, the qualifiers in the placeholder and the help
sub('''        body.Append($"""
                 <div class="filtering-box" id="filtering-box">
                    <div class="filtering-box-header"><h2>Filtering</h2><div class="filtering-box-export"><button class="export-btn" onclick="clear_all_filters()">Clear All</button><button class="export-btn" onclick="export_html()">Export Filtered HTML</button><button class="export-btn" onclick="export_csv()">Export Filtered CSV</button></div></div>
                    <div class="filter-search"><input id="searchbar" autocomplete="off" placeholder="Search... (@tag, $status, &&, ||, !!, parentheses)" onkeyup="search_scenarios()" /><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button></div>''',
'''        // What the filters leave showing (5.0.0, plans/OPTION_C_PLAN.md S5, D14), beside Clear All: written here for the
        // page as it opens, and kept by the filters (report-scenario-feature-map-helper.js), counting the scenarios as
        // the run counts them. The exports drop "Filtered" where there is no room for it (D13).
        var readoutText = $"All {ScenarioCount(features.Sum(f => f.Scenarios.Length))}, {(features.Length == 1 ? "1 feature" : string.Create(CultureInfo.InvariantCulture, $"{features.Length} features"))}";
        body.Append($"""
                 <div class="filtering-box" id="filtering-box">
                    <div class="filtering-box-header"><h2>Filtering</h2><div class="filtering-box-export"><span class="filter-readout" role="status">{readoutText}</span><button class="export-btn" onclick="clear_all_filters()">Clear All</button><button class="export-btn" onclick="export_html()">Export <span class="wide-only">Filtered </span>HTML</button><button class="export-btn" onclick="export_csv()">Export <span class="wide-only">Filtered </span>CSV</button></div></div>
                    <div class="filter-search"><input id="searchbar" autocomplete="off" placeholder="Search... (@tag, $status, dep:, duration:, &&, ||, !!)" onkeyup="search_scenarios()" /><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button></div>''')
sub('''                    <tr><td><code>$status</code></td><td>Filter by status</td><td><code>$failed</code>, <code>$passed</code>, <code>$skipped</code></td></tr>
''', '''                    <tr><td><code>$status</code></td><td>Filter by status</td><td><code>$failed</code>, <code>$passed</code>, <code>$skipped</code></td></tr>
                    <tr><td><code>dep:name</code></td><td>Scenarios that call a dependency: part of its name is enough; commas for any of several</td><td><code>dep:payment</code>, <code>dep:"order service"</code>, <code>dep:payment,order</code></td></tr>
                    <tr><td><code>duration:&gt;2s</code></td><td>Scenarios by their own time: &gt;, &gt;=, &lt;, &lt;= or a range; ms, s or m; p50, p90, p95, p99 are this report's percentiles</td><td><code>duration:&gt;2s</code>, <code>duration:&lt;500ms</code>, <code>duration:&gt;p90</code>, <code>duration:1s..5s</code></td></tr>
                    <tr><td><code>is:happy</code></td><td>Happy paths</td><td><code>is:happy</code>, <code>!!is:happy</code></td></tr>
''')
sub('''legacy tag expressions (<code>@a and @b or not @c</code>) are also supported.</p>''',
    '''legacy tag expressions (<code>@a and @b or not @c</code>) are also supported. <code>dep:</code>, <code>duration:</code> and <code>is:</code> always narrow the search: they combine with the rest of it by AND, and can be negated with <code>!!</code>.</p>''')

# 2. the rows
start = s.index('        // Status filter toggles\n')
end = s.index('        body.Append("</div>"); // close filters\n')
region = s[start:end]
# keep the duration block's percentile computation as it is
d0 = region.index('            // Each scenario\'s own time: a scenario slow only because it paid')
d1 = region.index('            body.Append(CultureInfo.InvariantCulture, $"""<div class="duration-filters"')
percentiles = region[d0:d1]
c0 = region.index('        // Category filter (only shown when scenarios have category data)\n')
c1 = region.index('        if (allCategories.Length > 0)\n')
categories = region[c0:c1]
new_region = '''        // The filter rows (5.0.0, plans/OPTION_C_PLAN.md S6, D6 to D12, D19): labels without colons and each row's controls
        // in a span.fltr-controls; Status, Duration and Happy paths share the first row; then Tags, Dependencies and
        // Categories, each a row of tri-state chips (off, has, has not) with an ANY / ALL switch, a chip for the scenarios
        // with none, and a Clear (report-filter-chips-script.js). A chip's title counts the scenarios it picks out.
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        // Status filter toggles
        {
            body.Append("""<div class="status-filters"><span class="status-filters-label">Status</span><span class="fltr-controls">""");
            foreach (var status in Enum.GetValues<ExecutionResult>().OrderBy(s => s))
            {
                if (status == ExecutionResult.SkippedAfterFailure) continue;
                var statusName = status.ToString();
                body.Append($"""<button class="status-toggle" data-status="{statusName}" onclick="toggle_status(this)">{statusName}</button>""");
            }
            body.Append("</span></div>");
        }

        // Duration filter (only shown when scenarios have duration data), on the Status line (D7). Each button says what
        // it shows, and that the first-call warm-up is left out when the run had one (D19).
        if (hasDurations)
        {
''' + percentiles + '''            var warm = warmUp.ScenarioMs.Count > 0;
            var notWarm = warm ? ", not counting first-call warm-up" : "";
            string Threshold(double ms, string share) => $"Scenarios taking {FormatDurationBadge(TimeSpan.FromMilliseconds(ms))} or more{notWarm} ({share})";
            body.Append(CultureInfo.InvariantCulture, $"""<div class="duration-filters" data-p50="{p50Ms:F0}" data-p90="{p90Ms:F0}" data-p95="{p95Ms:F0}" data-p99="{p99Ms:F0}"><span class="duration-filters-label"{(warm ? " title=\\"Each scenario's own time, without the first-call warm-up it paid\\"" : "")}>Duration ≥</span><span class="fltr-controls"><button class="percentile-btn" data-threshold-ms="{p50Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p50Ms, "the slowest half")}">P50 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p50Ms))})</button><button class="percentile-btn" data-threshold-ms="{p90Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p90Ms, "the slowest 10%")}">P90 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p90Ms))})</button><button class="percentile-btn" data-threshold-ms="{p95Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p95Ms, "the slowest 5%")}">P95 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p95Ms))})</button><button class="percentile-btn" data-threshold-ms="{p99Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p99Ms, "the slowest 1%")}">P99 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p99Ms))})</button><button class="percentile-btn" data-custom="1" onclick="set_percentile(this)" title="Scenarios taking at least the seconds you enter{notWarm}">Custom</button><span id="custom-duration-wrap" style="display:none;align-items:center;gap:0.3em"><input id="duration-threshold" autocomplete="off" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" /><span class="duration-filters-unit">seconds</span></span></span></div>""");
        }

        // Happy paths: Show, Only or Hide, a face of the Tags row's Happy Path chip, so one filter underneath (D8)
        if (runs.Any(s => s.IsHappyPath))
            body.Append("""<div class="happy-filters"><span class="happy-filters-label">Happy paths</span><span class="fltr-controls" role="group" aria-label="Happy paths"><button type="button" class="happy-seg" data-hp="off" aria-pressed="true">Show</button><button type="button" class="happy-seg" data-hp="in" aria-pressed="false">Only</button><button type="button" class="happy-seg" data-hp="out" aria-pressed="false">Hide</button></span></div>""");

        body.Append("</div>"); // close filter-row

        // Tags: a scenario's labels, with Happy Path for a happy path, pinned first (D9)
        string[] TagsOf(Scenario sc) => sc.IsHappyPath ? [HappyPathTag, .. (sc.Labels ?? []).Where(l => l != HappyPathTag)] : sc.Labels ?? [];
        var tagNames = runs.SelectMany(TagsOf).Distinct().OrderBy(t => t == HappyPathTag ? 0 : 1).ThenBy(t => t).ToArray();
        if (tagNames.Length > 0)
        {
            body.Append("""<div class="tag-filters"><span class="tag-filters-label">Tags</span><span class="fltr-controls">""");
            body.Append(FilterModeToggle("tag-mode-toggle", "OR", "ANY: scenarios with any of the chosen tags. ALL: scenarios with every one of them. Click to switch."));
            foreach (var tag in tagNames)
                body.Append(FilterChip("tag", tag, tag, runs.Count(sc => TagsOf(sc).Contains(tag))));
            var untagged = runs.Count(sc => TagsOf(sc).Length == 0);
            if (untagged > 0) body.Append(FilterChip("tag", "__untagged__", "Untagged", untagged, none: true));
            body.Append(GroupClear("tag"));
            body.Append("</span></div>");
        }

        // Dependencies: the participants a scenario's diagrams name; "No dependencies" for those that name none (D10)
        if (allDependencies.Count > 0)
        {
            body.Append("""<div class="dependency-filters"><span class="dependency-filters-label">Dependencies</span><span class="fltr-controls">""");
            body.Append(FilterModeToggle("dep-mode-toggle", depModeText, "ANY: scenarios that call any of the chosen dependencies. ALL: scenarios that call every one of them. Click to switch."));
            foreach (var dep in allDependencies.OrderBy(d => d))
                body.Append(FilterChip("dependency", dep, dep, runs.Count(sc => scenarioDependencies.TryGetValue(sc.Id, out var ds) && ds.Contains(dep))));
            var noDependencies = runs.Count(sc => !scenarioDependencies.TryGetValue(sc.Id, out var ds) || ds.Count == 0);
            if (noDependencies > 0) body.Append(FilterChip("dependency", "__none__", "No dependencies", noDependencies, none: true));
            body.Append(GroupClear("dependency"));
            body.Append("</span></div>");
        }

''' + categories + '''        if (allCategories.Length > 0)
        {
            body.Append("""<div class="category-filters"><span class="category-filters-label">Categories</span><span class="fltr-controls">""");
            body.Append(FilterModeToggle("cat-mode-toggle", catModeText, "ANY: scenarios in any of the chosen categories. ALL: scenarios in every one of them. Click to switch."));
            foreach (var cat in allCategories)
                body.Append(FilterChip("category", cat, cat, runs.Count(sc => sc.Categories is { } cs && cs.Contains(cat))));
            var uncategorized = runs.Count(sc => sc.Categories is not { Length: > 0 });
            if (uncategorized > 0) body.Append(FilterChip("category", "__uncategorized__", "Uncategorized", uncategorized, none: true));
            body.Append(GroupClear("category"));
            body.Append("</span></div>");
        }

'''
s = s[:start] + new_region + s[end:]

# 3. the helpers, after ReportPanel's
sub('''    /// <summary>The Features tab's views, Full (the cards) and Summary (the features table), as tabs (S4, D21).</summary>''',
'''    private const string HappyPathTag = "Happy Path";

    /// <summary>
    /// A tri-state filter chip (5.0.0, plans/OPTION_C_PLAN.md S6): off, then has, which takes the row's active class as in
    /// 4.x, then has not, its exclude class (report-filter-chips-script.js cycles them). The chip for the scenarios with
    /// none carries <c>data-none</c> and a reserved value, which the link writes. Its title counts the scenarios it picks out.
    /// </summary>
    private static string FilterChip(string family, string value, string label, int scenarios, bool none = false) =>
        $"<button type=\\"button\\" class=\\"{family}-toggle{(none ? $" {family}-none" : "")}\\" data-{family}=\\"{System.Net.WebUtility.HtmlEncode(value)}\\"{(none ? " data-none=\\"1\\"" : "")} aria-pressed=\\"false\\" title=\\"{ScenarioCount(scenarios)}\\">{System.Net.WebUtility.HtmlEncode(label)}</button>";

    private static string ScenarioCount(int n) => n == 1 ? "1 scenario" : string.Create(CultureInfo.InvariantCulture, $"{n} scenarios");

    /// <summary>A filter row's ANY / ALL switch; the mode underneath, OR or AND, is what the link writes (D11).</summary>
    private static string FilterModeToggle(string cssClass, string mode, string title) =>
        $"<button type=\\"button\\" class=\\"{cssClass}\\" data-mode=\\"{mode}\\" title=\\"{title}\\">{(mode == "AND" ? "ALL" : "ANY")}</button>";

    /// <summary>A filter row's Clear, shown while one of its chips is set (D12).</summary>
    private static string GroupClear(string noun) =>
        $"<button type=\\"button\\" class=\\"group-clear\\" title=\\"Clear the {noun} filters\\" hidden>Clear</button>";

    /// <summary>The Features tab's views, Full (the cards) and Summary (the features table), as tabs (S4, D21).</summary>''')

# 4. the runs a parameterized scenario holds, for the readout (D14)
sub('''        body.Append($"<details class=\\"scenario scenario-parameterized{happyPathClass}\\"{(toggles.ScenariosExpanded ? " open" : "")} data-status=\\"{overallStatus}\\"''',
    '''        body.Append($"<details class=\\"scenario scenario-parameterized{happyPathClass}\\"{(toggles.ScenariosExpanded ? " open" : "")} data-status=\\"{overallStatus}\\"{(scenarios.Length > 1 ? string.Create(CultureInfo.InvariantCulture, $" data-runs=\\"{scenarios.Length}\\"") : "")}''')

# 5. the participant of a diagram with no calls is no dependency
sub('''            if (match.Success)
                deps.Add(match.Groups[1].Value);''', '''            // the participant a diagram with no call is drawn with holds its step markers: no dependency (5.0.0)
            if (match.Success && match.Groups[1].Value != Kronikol.PlantUml.PlantUmlCreator.MarkerOnlyParticipantName)
                deps.Add(match.Groups[1].Value);''')
open(G, 'wb').write(s.encode('utf-8'))

P = 'src/Kronikol/PlantUml/PlantUmlCreator.cs'
t = open(P, 'rb').read().decode('utf-8')
old = '''    internal const string MarkerOnlyParticipant = "participant \\"(no interactions)\\" as noInteractions";'''
assert t.count(old) == 1
t = t.replace(old, '''    internal const string MarkerOnlyParticipantName = "(no interactions)";
    internal const string MarkerOnlyParticipant = "participant \\"" + MarkerOnlyParticipantName + "\\" as noInteractions";''')
open(P, 'wb').write(t.encode('utf-8'))
print('ok')
