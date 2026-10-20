# S12 (D49, D50): a title on every control of the Option C report, and the preview's debug globals settled.
# Every static title, and every where-word that depends on the report bar or a scenario's own bar, is written by the C#
# (ReportGenerator.cs, HistoryHtml.cs), the where-words through the scenarioLevel parameters BuildDetailsRadio and
# BuildFilterToggleButton already take. Scripts title only what they draw or make live: the render script's Raw PlantUML
# summaries, the internal-flow popup's close and view tabs, and, as report-step-status-titles.js does for a step's
# icon since plans/REPORT_FORMAT_PLAN.md R2, a composite step's summary, given its title when it is first pointed at or
# focused, so the page carries no title per step. D50: the product exposes none of the preview's debug globals, and the
# two harness checks that read them use the product's own controls and functions.
# usage: python scripts-s12d49.py <worktree root>
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
assert len(sys.argv) == 2, 'usage: python scripts-s12d49.py <worktree root>'

# All or nothing: every edit is made in memory and checked, and the files are written only once all of them hold, so a
# moved anchor leaves the tree as it was. Files are read and written as text with newline='' and the utf-8 codec, which
# keeps a byte order mark as the first character, so ReportGenerator.cs keeps its BOM. A file is edited with LF line
# ends and written back with the ends it had: a checkout can hold a file with CRLF although git stores it with LF (the
# v5 worktree's nav-check.js does), and the anchors below are written with LF. A file mixing the two is refused.
files = {}
originals = {}
crlf = {}


def text(path):
    if path not in files:
        raw = open(ROOT + path, encoding='utf-8', newline='').read()
        crlf[path] = '\r\n' in raw
        assert raw.count('\r') == (raw.count('\n') if crlf[path] else 0), (path, 'mixed line ends')
        files[path] = originals[path] = raw.replace('\r\n', '\n')
    return files[path]


def edit(path, old, new, count=1):
    s = text(path)
    n = s.count(old)
    assert n == count, (path, n, old[:100])
    files[path] = s.replace(old, new)


RG = 'src/Kronikol/Reports/ReportGenerator.cs'
HH = 'src/Kronikol/History/HistoryHtml.cs'
RENDER = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
POPUP = 'src/Kronikol/Reports/internal-flow-popup-script.js'
STEPS = 'src/Kronikol/Reports/report-step-status-titles.js'
NAV = 'plans/OPTION_C_PLAN.harness/checks/nav-check.js'
QTEST = 'plans/OPTION_C_PLAN.harness/checks/q-test.js'

assert 'PayloadsWhere' not in text(RG), 'S12 D49 is in already'
# the base: S11 complete (S11d's timeline controls, S11e's Components header)
assert 'private static string ComponentHeader(' in text(RG), 'S11e is not in: this applies on v5 after S11'
assert 'private static string TimelineControls(bool warmUp)' in text(RG), 'S11d is not in: this applies on v5 after S11'

# ---------------------------------------------------------------------------------------------------------------------
# 1. The payload controls (rows 11 to 23): the report bar's name every sequence diagram in the report, a scenario's own
#    bar that scenario's, through the scenarioLevel parameter the builders take.
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''    /// agree before the first click. Report and scenario level differ only in their handlers.
    /// </summary>
    private static string BuildDetailsRadio(ResolvedToggleDefaults toggles, bool scenarioLevel)
    {
        string Active(ReportDetailsState state) => toggles.Details == state ? " details-active" : "";
        string Click(string state) => scenarioLevel
            ? $"window._setAllNotes(this,'{state}')"
            : $"window._setReportDetails('{state}')";
        var change = scenarioLevel ? "window._setScenarioTruncateLines(this)" : "window._setTruncateLines(this)";
        var disabled = toggles.Details == ReportDetailsState.Truncated ? "" : " disabled";
        var options = string.Concat(Enum.GetValues<TruncateLineCount>().Select(v =>
            $"<option value=\"{(int)v}\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v} lines</option>"));
        return "<span class=\"details-radio\"><span class=\"details-radio-label\">Payloads</span>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Expanded)}\" data-state=\"expanded\" onclick=\"{Click("expanded")}\">{BuildButtonLabel("Expanded")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\" data-state=\"collapsed\" onclick=\"{Click("collapsed")}\">{BuildButtonLabel("Collapsed")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Truncated)}\" data-state=\"truncated\" onclick=\"{Click("truncated")}\">{BuildButtonLabel("Truncated")}</button>"
            + $"<select class=\"truncate-lines-select\" autocomplete=\"off\"{disabled} onchange=\"{change}\"><optgroup label=\"Payload length\">{options}</optgroup></select></span>";
    }
''', r'''    /// agree before the first click. Report and scenario level differ in their handlers, and in the diagrams their titles
    /// say they act on (5.0.0, plans/OPTION_C_PLAN.md D49, <see cref="PayloadsWhere"/>). The line-count select is named
    /// "Payload length", its group's heading, as the format and width selects are named by theirs.
    /// </summary>
    private static string BuildDetailsRadio(ResolvedToggleDefaults toggles, bool scenarioLevel)
    {
        string Active(ReportDetailsState state) => toggles.Details == state ? " details-active" : "";
        string Click(string state) => scenarioLevel
            ? $"window._setAllNotes(this,'{state}')"
            : $"window._setReportDetails('{state}')";
        var change = scenarioLevel ? "window._setScenarioTruncateLines(this)" : "window._setTruncateLines(this)";
        var disabled = toggles.Details == ReportDetailsState.Truncated ? "" : " disabled";
        var options = string.Concat(Enum.GetValues<TruncateLineCount>().Select(v =>
            $"<option value=\"{(int)v}\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v} lines</option>"));
        var diagrams = PayloadsWhere(scenarioLevel);
        return $"<span class=\"details-radio\"><span class=\"details-radio-label\" title=\"How the payloads in {diagrams} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit\">Payloads</span>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Expanded)}\" data-state=\"expanded\" onclick=\"{Click("expanded")}\" title=\"The payloads in {diagrams} show in full\">{BuildButtonLabel("Expanded")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\" data-state=\"collapsed\" onclick=\"{Click("collapsed")}\" title=\"The payloads in {diagrams} fold to one line each; a box's + button opens it\">{BuildButtonLabel("Collapsed")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Truncated)}\" data-state=\"truncated\" onclick=\"{Click("truncated")}\" title=\"Payloads in {diagrams} longer than the line limit beside this are cut at that many lines\">{BuildButtonLabel("Truncated")}</button>"
            + $"<select class=\"truncate-lines-select\" autocomplete=\"off\" aria-label=\"Payload length\" title=\"How many lines a cut payload box keeps in {diagrams} (with Truncated picked)\"{disabled} onchange=\"{change}\"><optgroup label=\"Payload length\">{options}</optgroup></select></span>";
    }
''')

edit(RG, r'''    /// One filter toggle (headers / assertions / steps / databases): its bare word, with the configured
    /// start state in its class, <c>data-shown</c> and <c>aria-pressed</c>, which <c>syncToggleBtn</c>
    /// keeps on every later click (5.0.0; the label no longer changes).
    /// </summary>
    private static string BuildFilterToggleButton(string toggleName, bool shown, bool scenarioLevel)
    {
        var label = char.ToUpperInvariant(toggleName[0]) + toggleName[1..];
        var handler = scenarioLevel ? $"window._toggleScenario{label}(this)" : $"window._toggle{label}(this)";
        var on = shown ? "true" : "false";
        return $"<button class=\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\" data-toggle=\"{toggleName}\" data-shown=\"{on}\" aria-pressed=\"{on}\" onclick=\"{handler}\">{BuildButtonLabel(label)}</button>";
    }
''', r'''    /// One filter toggle (headers / assertions / steps / databases): its bare word, with the configured
    /// start state in its class, <c>data-shown</c> and <c>aria-pressed</c>, which <c>syncToggleBtn</c>
    /// keeps on every later click (5.0.0; the label no longer changes), titled with what it shows or hides and in which
    /// diagrams (D49).
    /// </summary>
    private static string BuildFilterToggleButton(string toggleName, bool shown, bool scenarioLevel)
    {
        var label = char.ToUpperInvariant(toggleName[0]) + toggleName[1..];
        var handler = scenarioLevel ? $"window._toggleScenario{label}(this)" : $"window._toggle{label}(this)";
        var on = shown ? "true" : "false";
        return $"<button class=\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\" data-toggle=\"{toggleName}\" data-shown=\"{on}\" aria-pressed=\"{on}\" onclick=\"{handler}\" title=\"{ToggleTitle(toggleName, PayloadsWhere(scenarioLevel))}\">{BuildButtonLabel(label)}</button>";
    }

    /// <summary>
    /// The diagrams a payload control acts on, as its title names them (5.0.0, plans/OPTION_C_PLAN.md D49): the report
    /// bar's act on every sequence diagram in the report, a scenario's own bar on that scenario's only.
    /// </summary>
    private static string PayloadsWhere(bool scenarioLevel) =>
        scenarioLevel ? "this scenario's sequence diagrams" : "every sequence diagram in the report";

    /// <summary>What a filter toggle shows or hides in <paramref name="diagrams"/> (D49).</summary>
    private static string ToggleTitle(string toggleName, string diagrams) => toggleName switch
    {
        "headers" => $"Show or hide the headers at the top of each payload in {diagrams}",
        "assertions" => $"Show or hide the boxes with the test's assertions in {diagrams}",
        "steps" => $"Show or hide the bars marking where each step starts in {diagrams}",
        "databases" => $"Show or hide the databases and their calls in {diagrams}",
        "events" => $"Show or hide the calls to and from message brokers (the participants drawn as queues), with their payloads, in {diagrams}",
        "setup" => $"Show or hide the setup in {diagrams}: everything before the first When step, or the Setup section where the report keeps setup apart",
        _ => throw new ArgumentOutOfRangeException(nameof(toggleName), toggleName, "a toggle with no title")
    };

    /// <summary>The payload format select's title (D49).</summary>
    private static string NoteFormatTitle(bool scenarioLevel) =>
        $"How the payloads in {PayloadsWhere(scenarioLevel)} are written: JSON as captured, or the same data as YAML";

    /// <summary>The payload width select's title, which spells out both states (D49).</summary>
    private static string NoteWidthTitle(bool scenarioLevel) =>
        $"How wide the payloads are in {PayloadsWhere(scenarioLevel)}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";
''')

edit(RG, r'''        var reportNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"Payload format\" onchange=\"window._setNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
        var scenarioNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"Payload format\" onchange=\"window._setScenarioNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
''', r'''        var reportNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"{NoteFormatTitle(scenarioLevel: false)}\" onchange=\"window._setNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
        var scenarioNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"{NoteFormatTitle(scenarioLevel: true)}\" onchange=\"window._setScenarioNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
''')

edit(RG, r'''        (string, string)[] noteWidthOptions = [("default", "Wrap"), ("full", "Wide")];
        const string noteWidthTitle = "Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";
        var widthSelected = toggles.NoteWidth == NoteWidthMode.Full ? "full" : "default";
        var reportNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", noteWidthTitle, "_setNoteWidth", noteWidthOptions, widthSelected)
            : "";
        var scenarioNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", noteWidthTitle, "_setScenarioNoteWidth", noteWidthOptions, widthSelected)
            : "";
''', r'''        (string, string)[] noteWidthOptions = [("default", "Wrap"), ("full", "Wide")];
        var widthSelected = toggles.NoteWidth == NoteWidthMode.Full ? "full" : "default";
        var reportNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", NoteWidthTitle(scenarioLevel: false), "_setNoteWidth", noteWidthOptions, widthSelected)
            : "";
        var scenarioNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", NoteWidthTitle(scenarioLevel: true), "_setScenarioNoteWidth", noteWidthOptions, widthSelected)
            : "";
''')

# ---------------------------------------------------------------------------------------------------------------------
# 2. A scenario's diagrams: the view tabs (row 45) and the section's heading (row 46), which a section with one view
#    titles with what that view shows.
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''    private static string BuildDiagramTab(string dtype, string label, bool active) =>
        $"<button class=\"diagram-toggle-btn{(active ? " diagram-toggle-active" : "")}\" role=\"tab\" aria-selected=\"{(active ? "true" : "false")}\" tabindex=\"{(active ? "0" : "-1")}\" data-dtype=\"{dtype}\">{label}</button>";
''', r'''    private static string BuildDiagramTab(string dtype, string label, bool active) =>
        $"<button class=\"diagram-toggle-btn{(active ? " diagram-toggle-active" : "")}\" role=\"tab\" aria-selected=\"{(active ? "true" : "false")}\" tabindex=\"{(active ? "0" : "-1")}\" data-dtype=\"{dtype}\" title=\"{DiagramViewTitle(dtype)}\">{label}</button>";

    /// <summary>
    /// What a diagram view shows (5.0.0, plans/OPTION_C_PLAN.md D49): its tab's title, and the section's heading's where
    /// the section has that view alone.
    /// </summary>
    private static string DiagramViewTitle(string dtype) => dtype switch
    {
        "seq" => "The calls between the test and the services, in order",
        "activity" => "The work inside the services, span by span, as an activity diagram",
        _ => "Where the time went inside the services, span by span, as nested bars"
    };
''')

edit(RG, r'''        var title = views > 1 ? "Diagrams" : hasSeq ? "Sequence Diagrams" : hasActivity ? "Activity Diagrams" : "Flame Chart";
        body.Append("<summary class=\"h4\">").Append(title);
''', r'''        var title = views > 1 ? "Diagrams" : hasSeq ? "Sequence Diagrams" : hasActivity ? "Activity Diagrams" : "Flame Chart";
        var tooltip = views > 1 ? "This scenario's diagrams" : hasSeq ? "This scenario's sequence diagrams" : DiagramViewTitle(hasActivity ? "activity" : "flame");
        body.Append($"<summary class=\"h4\" title=\"{tooltip}\">").Append(title);
''')

# ---------------------------------------------------------------------------------------------------------------------
# 3. The features table's names (outside the table: the Summary view's way into a feature's card).
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''                featureTable.Append($"<td><button type=\"button\" class=\"feature-open\">{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</button></td>");
''', r'''                featureTable.Append($"<td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</button></td>");
''')

# ---------------------------------------------------------------------------------------------------------------------
# 4. The filter box (rows 24 to 32, 39) and its rows' chips (rows 34 to 36).
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''        var rowsTitle = FilterRowsTitle(features, hasDurations, allDependencies.Count > 0);
''', r'''        var rowsTitle = FilterRowsTitle(features, hasDurations, allDependencies.Count > 0);
        // Clear All's title names what it clears, the same rows (D49)
        var clearTitle = ClearAllTitle(features, hasDurations, allDependencies.Count > 0);
''')

edit(RG, r'''<span class="filter-readout" role="status">{readoutText}</span><button class="export-btn" onclick="clear_all_filters()">Clear All</button><button class="export-btn" onclick="export_html()">Export <span class="wide-only">Filtered </span>HTML</button><button class="export-btn" onclick="export_csv()">Export <span class="wide-only">Filtered </span>CSV</button>{FiltersClose}''',
     r'''<span class="filter-readout" role="status" title="{FiltersLeaveShowing}">{readoutText}</span><button class="export-btn" onclick="clear_all_filters()" title="{clearTitle}">Clear All</button><button class="export-btn" onclick="export_html()" title="Download a copy of this report holding only the features the filters show">Export <span class="wide-only">Filtered </span>HTML</button><button class="export-btn" onclick="export_csv()" title="Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds">Export <span class="wide-only">Filtered </span>CSV</button>{FiltersClose}''')

edit(RG, r'''onkeyup="search_scenarios()" /></span><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button>{MoreFilters(rowsTitle)}</div>
                    <div class="mobile-filter-toggle">Filters</div>''',
     r'''onkeyup="search_scenarios()" title="{SearchTitle}" /></span><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="{SearchHelpTitle}">?</button>{MoreFilters(rowsTitle)}</div>
                    <div class="mobile-filter-toggle" title="Show or hide the filters">Filters</div>''')

edit(RG, r'''        // Clear (report-filter-chips-script.js). A chip's title counts the scenarios it picks out. A category is a tag
        // (plans/TAGS_PLAN.md §4.8).
''', r'''        // Clear (report-filter-chips-script.js). A chip's title counts the scenarios it picks out and says what a click
        // does (D49). A category is a tag (plans/TAGS_PLAN.md §4.8).
''')

edit(RG, r'''                body.Append($"""<button class="status-toggle" data-status="{statusName}" onclick="toggle_status(this)">{statusName}</button>""");
''', r'''                body.Append($"""<button class="status-toggle" data-status="{statusName}" onclick="toggle_status(this)" title="{StatusToggleTitle(status)}">{statusName}</button>""");
''')

edit(RG, r'''<input id="duration-threshold" autocomplete="off" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" />''',
     r'''<input id="duration-threshold" autocomplete="off" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" title="Seconds: scenarios taking at least this long{notWarm}" />''')

edit(RG, r'''<button type="button" class="happy-seg" data-hp="off" aria-pressed="true">Show</button><button type="button" class="happy-seg" data-hp="in" aria-pressed="false">Only</button><button type="button" class="happy-seg" data-hp="out" aria-pressed="false">Hide</button>''',
     r'''<button type="button" class="happy-seg" data-hp="off" aria-pressed="true" title="Happy-path scenarios and the rest">Show</button><button type="button" class="happy-seg" data-hp="in" aria-pressed="false" title="Only the scenarios tagged as happy paths">Only</button><button type="button" class="happy-seg" data-hp="out" aria-pressed="false" title="Leave out the scenarios tagged as happy paths">Hide</button>''')

# ---------------------------------------------------------------------------------------------------------------------
# 5. The report bar: the tabs (rows 1 to 5), its readout and Clear All (rows 6, 7).
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''            body.Append(ReportTab("features", "Features", startTab, features.Length));
            if (hasDurations) body.Append(ReportTab("timeline", "Timeline", startTab));
            if (hasComponentTab) body.Append(ReportTab("components", "Components", startTab));
            if (hasHistoryTab) body.Append(ReportTab("history", "History", startTab));
            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", startTab, diagnostics!.Count));
            body.Append("</div>");
            body.Append(NavFilters(readoutText, ScenarioCount(features.Sum(f => f.Scenarios.Length)))).Append("</div>");
''', r'''            // each tab titled with what it shows (D49); the timeline's names the warm-up only where a scenario paid one
            body.Append(ReportTab("features", "Features", "The features and their scenarios, with the filters applied", startTab, features.Length));
            if (hasDurations) body.Append(ReportTab("timeline", "Timeline", "Every scenario as a bar, slowest first" + (warmUp.ScenarioMs.Count > 0 ? ", with the first-call warm-up it paid" : ""), startTab));
            if (hasComponentTab) body.Append(ReportTab("components", "Components", "The services, databases and queues the scenarios called, drawn as one diagram", startTab));
            if (hasHistoryTab) body.Append(ReportTab("history", "History", HistoryHtml.Description, startTab));
            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", DiagnosticsDescription, startTab, diagnostics!.Count));
            body.Append("</div>");
            body.Append(NavFilters(readoutText, ScenarioCount(features.Sum(f => f.Scenarios.Length)), clearTitle)).Append("</div>");
''')

# ---------------------------------------------------------------------------------------------------------------------
# 6. A scenario: the failure (row 46), its attachments (row 48), its steps' headings (row 46).
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''                              <details class="failure-result" open>
                                 <summary class="h4">Failure Result</summary>
''', r'''                              <details class="failure-result" open>
                                 <summary class="h4" title="{FailureResultTitle}">Failure Result</summary>
''')

edit(RG, r'''                            body.Append($"<a class=\"step-attachment\" href=\"{AttachmentHref(attachment)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
''', r'''                            body.Append(AttachmentLink(attachment, "scenario"));
''', count=2)

edit(RG, r'''                    body.Append($"<a class=\"step-attachment\" href=\"{AttachmentHref(attachment)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
''', r'''                    body.Append(AttachmentLink(attachment, "step"));
''')

edit(RG, r'''                    body.Append("<details class=\"failure-result\" open><summary class=\"h4\">Failure Result</summary><pre>");
''', r'''                    body.Append($"<details class=\"failure-result\" open><summary class=\"h4\" title=\"{FailureResultTitle}\">Failure Result</summary><pre>");
''')

edit(RG, r'''                body.Append("""<summary class="h4">Background Steps</summary>""");
''', r'''                body.Append("""<summary class="h4" title="The steps of the feature's background, which run before this scenario's own">Background Steps</summary>""");
''')

edit(RG, r'''        body.Append($"""<details class="scenario-steps"{(open ? " open" : "")}>""");
        body.Append("""<summary class="h4">Steps</summary>""");
''', r'''        body.Append($"""<details class="scenario-steps"{(open ? " open" : "")}>""");
        // how long each took only where the steps say (D49)
        body.Append($"""<summary class="h4" title="This scenario's steps{(steps.Any(s => s.Duration.HasValue) ? ", with how long each took" : "")}">Steps</summary>""");
''')

# ---------------------------------------------------------------------------------------------------------------------
# 7. The page's own buttons: Next Failure (row 51) and back to the top (row 52, which had no name either).
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''            body.Append($"""<button class="jump-to-failure" onclick="jump_to_next_failure()">Next Failure <span class="failure-counter" id="failure-counter">(0/{failureCount})</span></button>""");
''', r'''            body.Append($"""<button class="jump-to-failure" onclick="jump_to_next_failure()" title="Go to the next failed scenario; the count says which one of how many">Next Failure <span class="failure-counter" id="failure-counter">(0/{failureCount})</span></button>""");
''')

edit(RG, r'''        body.Append("""<button class="back-to-top" id="back-to-top" onclick="window.scrollTo({top:0,behavior:'smooth'})">↑</button>""");
''', r'''        body.Append("""<button class="back-to-top" id="back-to-top" onclick="window.scrollTo({top:0,behavior:'smooth'})" aria-label="Back to top" title="Back to the top of the report">↑</button>""");
''')

# ---------------------------------------------------------------------------------------------------------------------
# 8. The helpers: the tab's title, the chip's, Clear All's, the readouts', the empty state's, the search's.
# ---------------------------------------------------------------------------------------------------------------------
edit(RG, r'''    /// <summary>A tab of the report bar (5.0.0, S3): the selected one is in the tab order, the others reached by the arrow
    /// keys (report-tabs-script.js).</summary>
    private static string ReportTab(string key, string label, string selected, int? count = null) =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(key == selected ? "true" : "false")}\" tabindex=\"{(key == selected ? "0" : "-1")}\" data-tab=\"{key}\">{label}"
''', r'''    /// <summary>A tab of the report bar (5.0.0, S3): the selected one is in the tab order, the others reached by the arrow
    /// keys (report-tabs-script.js); its title says what the tab shows (D49).</summary>
    private static string ReportTab(string key, string label, string title, string selected, int? count = null) =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(key == selected ? "true" : "false")}\" tabindex=\"{(key == selected ? "0" : "-1")}\" data-tab=\"{key}\" title=\"{title}\">{label}"
''')

edit(RG, r'''    /// none carries <c>data-none</c> and a reserved value, which the link writes. Its title counts the scenarios it picks out.
    /// </summary>
    private static string FilterChip(string family, string value, string label, int scenarios, bool none = false, bool more = false) =>
        $"<button type=\"button\" class=\"{family}-toggle{(none ? $" {family}-none" : "")}\" data-{family}=\"{System.Net.WebUtility.HtmlEncode(value)}\"{(none ? " data-none=\"1\"" : "")}{(more ? " data-more=\"1\" hidden" : "")} aria-pressed=\"false\" title=\"{ScenarioCount(scenarios)}\">{System.Net.WebUtility.HtmlEncode(label)}</button>";
''', r'''    /// none carries <c>data-none</c> and a reserved value, which the link writes. Its title counts the scenarios it picks out,
    /// then says what a click does (D49): the count was the whole title, and the preview's instructions never showed.
    /// </summary>
    private static string FilterChip(string family, string value, string label, int scenarios, bool none = false, bool more = false) =>
        $"<button type=\"button\" class=\"{family}-toggle{(none ? $" {family}-none" : "")}\" data-{family}=\"{System.Net.WebUtility.HtmlEncode(value)}\"{(none ? " data-none=\"1\"" : "")}{(more ? " data-more=\"1\" hidden" : "")} aria-pressed=\"false\" title=\"{ScenarioCount(scenarios)}. {ChipClicks}\">{System.Net.WebUtility.HtmlEncode(label)}</button>";

    /// <summary>What a click on a filter chip does, after its count (D49; report-filter-chips-script.js cycles it).</summary>
    private const string ChipClicks = "Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once.";
''')

edit(RG, r'''    private static string FilterRowsTitle(Feature[] features, bool hasDurations, bool hasDependencies)
    {
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        var rows = new List<string> { "Status" };
        if (hasDurations) rows.Add("duration");
        if (runs.Any(s => s.IsHappyPath)) rows.Add("happy paths");
        if (features.Any(f => f.Scenarios.Any(s => s.IsHappyPath || ScenarioTags.Effective(f, s).Length > 0))) rows.Add("tags");
        if (hasDependencies) rows.Add("dependencies");
        return rows.Count == 1 ? rows[0] : string.Join(", ", rows.Take(rows.Count - 1)) + " and " + rows[^1];
    }
''', r'''    private static string FilterRowsTitle(Feature[] features, bool hasDurations, bool hasDependencies) =>
        Listed(FilterRows(features, hasDurations, hasDependencies, "Status"));

    /// <summary>
    /// What Clear All clears (5.0.0, plans/OPTION_C_PLAN.md D49): the search, with the dep:, duration: and is: terms in it,
    /// and every row the box has, named as More filters names them.
    /// </summary>
    private static string ClearAllTitle(Feature[] features, bool hasDurations, bool hasDependencies) =>
        "Clear every filter: the search with its dep:, duration: and is: terms, and the "
        + Listed(FilterRows(features, hasDurations, hasDependencies, "statuses"));

    /// <summary>The rows the filter box has, in their order, the first named <paramref name="status"/>.</summary>
    private static List<string> FilterRows(Feature[] features, bool hasDurations, bool hasDependencies, string status)
    {
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        var rows = new List<string> { status };
        if (hasDurations) rows.Add("duration");
        if (runs.Any(s => s.IsHappyPath)) rows.Add("happy paths");
        if (features.Any(f => f.Scenarios.Any(s => s.IsHappyPath || ScenarioTags.Effective(f, s).Length > 0))) rows.Add("tags");
        if (hasDependencies) rows.Add("dependencies");
        return rows;
    }

    /// <summary>Items in words: "a", "a and b", "a, b and c".</summary>
    private static string Listed(List<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>A status filter's title (D49): the scenarios it shows, and that statuses picked together add up.</summary>
    private static string StatusToggleTitle(ExecutionResult status)
    {
        var what = status switch
        {
            ExecutionResult.Passed => "passed",
            ExecutionResult.Failed => "failed",
            ExecutionResult.Skipped => "were skipped",
            _ => "were bypassed"
        };
        return $"Show only the scenarios that {what}; pick more statuses to show them together";
    }

    /// <summary>The title of both readouts, the box's and the bar's (D49).</summary>
    private const string FiltersLeaveShowing = "The scenarios and features the filters leave showing";

    /// <summary>The search field's title (D49), written HTML-encoded as the attribute holds it.</summary>
    private const string SearchTitle = "Search names, steps, tags and diagram text. Combine words with &amp;&amp; (and), || (or) and !! (not); @tag and $status pick by tag or status, dep:, duration: and is:happy by dependency, time and happy path; ? shows the syntax";

    /// <summary>The search help button's title (D49), written HTML-encoded as the attribute holds it.</summary>
    private const string SearchHelpTitle = "How to write a search: words, &quot;phrases&quot;, @tags, $statuses, dep:, duration:, is:happy, &amp;&amp;, ||, !! and parentheses";
''')

edit(RG, r'''    /// it; Clear All while anything is filtered; and Filters, which takes the box into the bar while the bar floats.
    /// </summary>
    private static string NavFilters(string readout, string scenarios) =>
        $"""<div class="nav-filters" role="group" aria-label="Filters"><span class="nav-readout" aria-hidden="true"><span class="nav-readout-long">{readout}</span><span class="nav-readout-short">All {scenarios}</span></span><button type="button" class="nav-clear" title="Clear every filter" hidden>Clear All</button>''',
     r'''    /// it; Clear All while anything is filtered, titled with what it clears (<paramref name="clearTitle"/>, D49); and
    /// Filters, which takes the box into the bar while the bar floats.
    /// </summary>
    private static string NavFilters(string readout, string scenarios, string clearTitle) =>
        $"""<div class="nav-filters" role="group" aria-label="Filters"><span class="nav-readout" aria-hidden="true" title="{FiltersLeaveShowing}"><span class="nav-readout-long">{readout}</span><span class="nav-readout-short">All {scenarios}</span></span><button type="button" class="nav-clear" title="{clearTitle}" hidden>Clear All</button>''')

edit(RG, r'''        """<div class="filters-empty" hidden><span>No scenarios match the filters.</span><button type="button" class="filters-empty-clear">Clear All</button></div>""";
''', r'''        """<div class="filters-empty" hidden><span>No scenarios match the filters.</span><button type="button" class="filters-empty-clear" title="Clear every filter">Clear All</button></div>""";
''')

# the CI run's link (row 53)
edit(RG, r'''rel=\"noopener noreferrer\">View run</a>"));''', r'''rel=\"noopener noreferrer\" title=\"Open the CI run this report came from\">View run</a>"));''')

# the failure's heading, an attachment's link, the Diagnostics tab's words (rows 46, 48, 5 and 49)
edit(RG, r'''    /// <summary>
    /// An attachment the report does not hold (#148), drawn as its name and why, with no image or link that would be dead.
''', r'''    /// <summary>
    /// An attachment the report holds, or one that lives elsewhere, as a link to it titled with what it opens and whose it
    /// is (5.0.0, plans/OPTION_C_PLAN.md D49): <paramref name="owner"/> is "scenario" or "step".
    /// </summary>
    private static string AttachmentLink(FileAttachment attachment, string owner)
    {
        var name = System.Net.WebUtility.HtmlEncode(attachment.Name);
        return $"<a class=\"step-attachment\" href=\"{AttachmentHref(attachment)}\" title=\"Open {name}, {(attachment.IsUrl ? "a link" : "a file")} this {owner} attached\">{name}</a>";
    }

    /// <summary>The title of a scenario's Failure Result (D49).</summary>
    private const string FailureResultTitle = "Why the scenario failed: the error, with the expected and actual values where it gives them";

    /// <summary>
    /// An attachment the report does not hold (#148), drawn as its name and why, with no image or link that would be dead.
''')

edit(RG, r'''        var html = new StringBuilder();
        html.Append($"<details class=\"report-diagnostics\"{(open || inTab ? " open" : "")}>");
        // In its tab the summary is the section's heading and nothing to click: the tabs script keeps it open.
        html.Append($"<summary{(inTab ? " tabindex=\"-1\"" : "")}>{summary}</summary>"); // enum names and counts only — nothing to encode, and the × must stay a glyph
''', r'''        var html = new StringBuilder();
        html.Append($"<details class=\"report-diagnostics\"{(open || inTab ? " open" : "")}>");
        // In its tab the summary is the section's heading and nothing to click: the tabs script keeps it open. Its title
        // says what the section is, and only where it is a disclosure that a click shows or hides it (D49).
        var title = inTab ? DiagnosticsDescription : "What Kronikol noticed while building this report: click to show or hide the list";
        html.Append($"<summary{(inTab ? " tabindex=\"-1\"" : "")} title=\"{title}\">{summary}</summary>"); // enum names and counts only: nothing to encode, and the × must stay a glyph
''')

edit(RG, r'''    /// <summary>
    /// The "Report diagnostics" block: a <c>&lt;details&gt;</c> listing every <see cref="DiagnosticEntry"/> the
''', r'''    /// <summary>What the Diagnostics tab shows: its title, and its heading's in the tab (5.0.0, plans/OPTION_C_PLAN.md D49).</summary>
    private const string DiagnosticsDescription = "What Kronikol noticed while building this report, such as calls it could not put under a scenario";

    /// <summary>
    /// The "Report diagnostics" block: a <c>&lt;details&gt;</c> listing every <see cref="DiagnosticEntry"/> the
''')

# ---------------------------------------------------------------------------------------------------------------------
# 9. HistoryHtml: the tab's words, the section's heading (row 49), its scenario links (row 50).
# ---------------------------------------------------------------------------------------------------------------------
edit(HH, r'''    /// <summary>
    /// The report's History section: the run's summary line, the trend of the last runs, and the lists of what
    /// changed, each scenario linked in the page by its stable id. Open when there is something to say.
    /// </summary>
''', r'''    /// <summary>
    /// What the History section shows (5.0.0, plans/OPTION_C_PLAN.md D49): the History tab's title, and its heading's in
    /// the tab, where the heading is nothing to click.
    /// </summary>
    internal const string Description = "This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone";

    /// <summary>The heading's title where the section is a disclosure, on the labs page and in <see cref="Section(HistoryVerdicts)"/>.</summary>
    private const string DisclosureTitle = "This run against the earlier runs on the same branch: click to show or hide the details";

    /// <summary>
    /// The report's History section: the run's summary line, the trend of the last runs, and the lists of what
    /// changed, each scenario linked in the page by its stable id. Open when there is something to say.
    /// </summary>
''')

edit(HH, r'''            : $"<a class=\"history-link\" href=\"{HtmlEncode(Uri.EscapeDataString(reportHref))}#sid-{HtmlEncode(stableId)}\">{html}</a>";
''', r'''            : $"<a class=\"history-link\" href=\"{HtmlEncode(Uri.EscapeDataString(reportHref))}#sid-{HtmlEncode(stableId)}\" title=\"Open this scenario in the report\">{html}</a>";
''')

edit(HH, r'''history.replaceState(null,'',location.pathname+location.search+'#sid-{id}');\">{HtmlEncode(entry.Feature)} &rsaquo; {HtmlEncode(entry.Name)}</a>";''',
     r'''history.replaceState(null,'',location.pathname+location.search+'#sid-{id}');\" title=\"Go to this scenario on the Features tab\">{HtmlEncode(entry.Feature)} &rsaquo; {HtmlEncode(entry.Name)}</a>";''')

edit(HH, r'''        sb.Append($"<summary class=\"h2\"{(inTab ? " tabindex=\"-1\"" : "")}>History <span class=\"history-summary-line\">''',
     r'''        sb.Append($"<summary class=\"h2\"{(inTab ? " tabindex=\"-1\"" : "")} title=\"{(inTab ? Description : DisclosureTitle)}\">History <span class=\"history-summary-line\">''')

# ---------------------------------------------------------------------------------------------------------------------
# 10. The scripts: what they draw or make live.
# ---------------------------------------------------------------------------------------------------------------------
# the render script's three failure blocks (row 49)
edit(RENDER, '<summary>Raw PlantUML</summary>', '<summary title="The PlantUML source this diagram was drawn from">Raw PlantUML</summary>', count=3)

# the internal-flow popup: its cross and its view tabs (outside the table)
edit(POPUP, r'''        var closeBtn = document.createElement('button');
        closeBtn.className = 'iflow-popup-close';
        closeBtn.innerHTML = '&times;';
''', r'''        var closeBtn = document.createElement('button');
        closeBtn.className = 'iflow-popup-close';
        closeBtn.innerHTML = '&times;';
        // the cross's name and title (5.0.0, plans/OPTION_C_PLAN.md D49)
        closeBtn.setAttribute('aria-label', 'Close');
        closeBtn.title = 'Close (Esc)';
''')

edit(POPUP, r'''        var toggleBtns = popup.querySelectorAll('.iflow-toggle-btn');
        if (toggleBtns.length) {
            toggleBtns.forEach(function(btn) {
                btn.addEventListener('click', function() {
''', r'''        var toggleBtns = popup.querySelectorAll('.iflow-toggle-btn');
        if (toggleBtns.length) {
            toggleBtns.forEach(function(btn) {
                // what each view shows (D49), said here, where the tabs are made live, and not in every segment's markup
                btn.title = btn.getAttribute('data-view') === 'flame'
                    ? 'Where the time went inside the services for this call, span by span, as nested bars'
                    : 'The work inside the services for this call, span by span, as an activity diagram';
                btn.addEventListener('click', function() {
''')

# a composite step's summary (row 47), as its icon is given its title (plans/REPORT_FORMAT_PLAN.md R2)
edit(STEPS, r'''// is focused, before the browser looks for a tooltip to show.
(function () {
    var titles = __STEP_STATUS_TITLES__;
''', r'''// is focused, before the browser looks for a tooltip to show. A composite step's summary is given its own title the same
// way, saying how many steps it holds (5.0.0, plans/OPTION_C_PLAN.md D49), so the page carries no title per step there
// either.
(function () {
    var titles = __STEP_STATUS_TITLES__;

    function giveSummaryTitle(summary) {
        if (summary.hasAttribute('title')) return;
        var n = summary.parentNode.querySelectorAll(':scope > .sub-steps > .step').length;
        if (n === 0) return;
        summary.setAttribute('title', n === 1
            ? 'This step has one step inside it: click to show or hide it'
            : 'This step has ' + n + ' steps inside it: click to show or hide them');
    }
''')

edit(STEPS, r'''        var target = event.target;
        if (!target || target.nodeType !== 1) return;
        if (target.classList.contains('step-status')) {
''', r'''        var target = event.target;
        if (!target || target.nodeType !== 1) return;
        var summary = target.closest('details.step-collapsible > summary');
        if (summary) giveSummaryTitle(summary);
        if (target.classList.contains('step-status')) {
''')

# ---------------------------------------------------------------------------------------------------------------------
# 11. D50: the harness checks that read the preview's debug globals use the product's own controls and functions, and
#     keep working against the preview, so the product needs no debug global.
# ---------------------------------------------------------------------------------------------------------------------
edit(NAV, r'''      await p.evaluate(() => window.__kronikolNavFilters.open()); await p.waitForTimeout(500);
''', r'''      // the Filters button's own click (the preview's window.__kronikolNavFilters.open() is not in the product, D50)
      await p.evaluate(() => document.querySelector('.nav-filters-toggle').click()); await p.waitForTimeout(500);
''')

edit(QTEST, r'''  const phrase = await p.evaluate(() => window.__kronikolQuery.split('"status: 400" milk:'));
  check('a phrase and an unknown key stay text', phrase.quals.length === 0 && phrase.rest === '"status: 400" milk:', JSON.stringify(phrase.rest));
''', r'''  // the product's split_search_qualifiers (a page function), else the preview's window.__kronikolQuery.split (D50)
  const phrase = await p.evaluate(() => {
    const split = typeof window.split_search_qualifiers === 'function' ? window.split_search_qualifiers : window.__kronikolQuery.split;
    const r = split('"status: 400" milk:');
    return { quals: r.quals.length, rest: r.rest };
  });
  check('a phrase and an unknown key stay text', phrase.quals === 0 && phrase.rest === '"status: 400" milk:', JSON.stringify(phrase.rest));
''')

# ---------------------------------------------------------------------------------------------------------------------
# 12. The controls outside the table that the report also draws: a failure cluster's scenario links, an image an
#     attachment shows, a step's parameter buttons, a table cell's whole value, and a popup's diagnostic info.
# ---------------------------------------------------------------------------------------------------------------------
PARAMS = 'src/Kronikol/Reports/ParameterValueRenderer.cs'
IFLOW = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'

edit(RG, r'''+'#{anchorId}');}}\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>";''',
     r'''+'#{anchorId}');}}\" title=\"Go to this scenario\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>";''')

# an image's link, at the scenario's two sites and the step's (the step's has the shorter indent, so it is matched
# with the line end before it, after the two others are replaced)
edit(RG, r'''                            body.Append($"<a class=\"attachment-image-link\" href=\"{AttachmentHref(attachment)}\" onclick=\"openLightbox(event, this)\"><img class=\"attachment-image\" src=\"{AttachmentHref(attachment)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
''', r'''                            body.Append(AttachmentImageLink(attachment, "scenario"));
''', count=2)

edit(RG, r'''
                    body.Append($"<a class=\"attachment-image-link\" href=\"{AttachmentHref(attachment)}\" onclick=\"openLightbox(event, this)\"><img class=\"attachment-image\" src=\"{AttachmentHref(attachment)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
''', r'''
                    body.Append(AttachmentImageLink(attachment, "step"));
''')

edit(RG, r'''    /// <summary>The title of a scenario's Failure Result (D49).</summary>
''', r'''    /// <summary>
    /// An image the report holds, as a link that opens it full size (report-lightbox-function.js), titled as
    /// <see cref="AttachmentLink"/> is (D49).
    /// </summary>
    private static string AttachmentImageLink(FileAttachment attachment, string owner)
    {
        var name = System.Net.WebUtility.HtmlEncode(attachment.Name);
        return $"<a class=\"attachment-image-link\" href=\"{AttachmentHref(attachment)}\" onclick=\"openLightbox(event, this)\" title=\"Show {name}, an image this {owner} attached, at full size\"><img class=\"attachment-image\" src=\"{AttachmentHref(attachment)}\" alt=\"{name}\" /></a>";
    }

    /// <summary>The title of a scenario's Failure Result (D49).</summary>
''')

# a step's parameter buttons: a long value shown under the step, or the parameter's cells in its table
edit(RG, r'''data-value=\"{System.Net.WebUtility.HtmlEncode(json)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");''',
     r'''data-value=\"{System.Net.WebUtility.HtmlEncode(json)}\" title=\"Show or hide the value of {System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");''')

edit(RG, r'''data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");''',
     r'''data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\" title=\"Show {System.Net.WebUtility.HtmlEncode(seg.TableReference)} in its table\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");''')

# a table cell's value too complex to show whole
edit(PARAMS, r'''        body.Append($"<summary>{System.Net.WebUtility.HtmlEncode(preview)}</summary>");
''', r'''        body.Append($"<summary title=\"Show or hide the whole value\">{System.Net.WebUtility.HtmlEncode(preview)}</summary>");
''', count=2)

# a popup with no spans: what Kronikol knows, and the usual causes
edit(IFLOW, r'''<details class=\"iflow-diagnostics\"><summary>Diagnostic info</summary><ul>''',
     r'''<details class=\"iflow-diagnostics\"><summary title=\"Why this call may have no spans: click to show or hide the details\">Diagnostic info</summary><ul>''', count=2)

changed = sorted(p for p in files if files[p] != originals[p])
assert len(changed) == 9, changed
for path in changed:
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(files[path].replace('\n', '\r\n') if crlf[path] else files[path])
print('ok:', ', '.join(changed))
