# S12 D49's tests: the two new classes, copied from this folder, and every pin the titles move, rewritten.
#   tests/Kronikol.Tests/Reports/ControlTitlesTests.cs             (core, new)
#   tests/Kronikol.Tests.EndToEnd/ControlTitlesAuditTests.cs       (Playwright, new)
#   22 core test files whose exact-markup pins carry a control the titles change (listed in PINS below)
# A pin that held a control's whole opening tag now holds it with its title, as the generator writes it; a pin that held
# a section's heading holds its title too. One pin is turned round rather than extended: DiagramToggleReportTests'
# "no Sequence Diagrams heading" checked for the heading's old opening tag, which no heading has once it carries a title,
# so it would have passed whatever the page drew; it now checks for the heading's text before its gear.
# usage: python tests-s12d49.py <worktree root>
import os
import re
import sys

assert len(sys.argv) == 2, 'usage: python tests-s12d49.py <worktree root>'
ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))

copies = {
    'ControlTitlesTests.cs': 'tests/Kronikol.Tests/Reports/ControlTitlesTests.cs',
    'ControlTitlesAuditTests.cs': 'tests/Kronikol.Tests.EndToEnd/ControlTitlesAuditTests.cs',
}
for target in copies.values():
    assert not os.path.exists(ROOT + target), ('already there: S12 D49 tests are in already', target)
    assert os.path.isdir(os.path.dirname(ROOT + target)), ('no such folder: is this a worktree root?', target)

# All or nothing, as scripts-s12d49.py: every edit is made in memory and checked, and the files are written only once
# all of them hold. Read and written with newline='' and utf-8, so a byte order mark stays; edited with LF line ends
# and written back with the ends each file had (CRLF where a checkout holds it so); a file mixing the two is refused.
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


def pin(path, old, new, count=1):
    s = text(path)
    n = s.count(old)
    assert n == count, (path, n, old[:120])
    files[path] = s.replace(old, new)


def pin_re(path, pattern, replacement, count):
    s = text(path)
    new, n = re.subn(pattern, replacement, s)
    assert n == count, (path, n, pattern)
    files[path] = new


T = 'tests/Kronikol.Tests/'
Q = '\\"'  # a quote as the C# source of a test writes it


def title(words):
    """A title attribute as the C# source of a test writes it."""
    return ' title=' + Q + words + Q


REPORT = 'every sequence diagram in the report'
SCENARIO = "this scenario's sequence diagrams"
LABEL = 'How the payloads in {} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit'
EXPANDED = 'The payloads in {} show in full'
COLLAPSED = "The payloads in {} fold to one line each; a box's + button opens it"
TRUNCATED = 'Payloads in {} longer than the line limit beside this are cut at that many lines'
LINES = 'How many lines a cut payload box keeps in {} (with Truncated picked)'
HEADERS = 'Show or hide the headers at the top of each payload in {}'
ASSERTIONS = "Show or hide the boxes with the test's assertions in {}"
STEPS = 'Show or hide the bars marking where each step starts in {}'
DATABASES = 'Show or hide the databases and their calls in {}'
FORMAT = 'How the payloads in {} are written: JSON as captured, or the same data as YAML'
WIDTH = 'How wide the payloads are in {}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.'
SEQ = 'The calls between the test and the services, in order'
ACTIVITY = 'The work inside the services, span by span, as an activity diagram'
FLAME = 'Where the time went inside the services, span by span, as nested bars'
SEQ_SECTION = "This scenario's sequence diagrams"
SECTION = "This scenario's diagrams"
STEPS_SECTION = "This scenario's steps"
BACKGROUND = "The steps of the feature's background, which run before this scenario's own"
HISTORY = 'This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone'
DIAGNOSTICS = 'What Kronikol noticed while building this report, such as calls it could not put under a scenario'
DIAGNOSTICS_DISCLOSURE = 'What Kronikol noticed while building this report: click to show or hide the list'
SHOWING = 'The scenarios and features the filters leave showing'
CLEAR_EMPTY = 'Clear every filter'
CLEAR = 'Clear every filter: the search with its dep:, duration: and is: terms, and the statuses, {}'
CHIP_CLICKS = 'Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once.'
EXPORT_CSV = 'Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds'

# ---------------------------------------------------------------------------------------------------------------------
# History: the tab, and the section's heading in it
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'History/HistoryHtmlTests.cs'
pin(P, r'''data-tab=\"history\">History</button>''', r'''data-tab=\"history\"''' + title(HISTORY) + r'''>History</button>''')
pin(P, r'''open><summary class=\"h2\" tabindex=\"-1\">History ", html);''',
    r'''open><summary class=\"h2\" tabindex=\"-1\"''' + title(HISTORY) + r'''>History ", html);''')

P = T + 'History/HistoryOutputsTests.cs'
pin(P, r'''open><summary class=\"h2\" tabindex=\"-1\">History ", byDefault);''',
    r'''open><summary class=\"h2\" tabindex=\"-1\"''' + title(HISTORY) + r'''>History ", byDefault);''')

P = T + 'Reports/Merge/MergeCommandTests.cs'
pin(P, r'''open><summary class=\"h2\" tabindex=\"-1\">", combined);''',
    r'''open><summary class=\"h2\" tabindex=\"-1\"''' + title(HISTORY) + r'''>", combined);''')

P = T + 'Reports/LabsReportTests.cs'
pin(P, r'''href=\"TestRunReport.html#sid-{PayId}\">Checkout &rsaquo; Pay by card</a>''',
    r'''href=\"TestRunReport.html#sid-{PayId}\"''' + title('Open this scenario in the report') + r'''>Checkout &rsaquo; Pay by card</a>''')

# ---------------------------------------------------------------------------------------------------------------------
# The report bar's tabs, the Diagnostics heading in its tab and outside one, the empty state
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Reports/ReportTabsTests.cs'
pin(P, r'''    private static string Tab(string key, string label, bool selected, string count = "") =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(selected ? "true" : "false")}\" tabindex=\"{(selected ? "0" : "-1")}\" data-tab=\"{key}\">{label}{count}</button>";
''', r'''    // each tab titled with what it shows (5.0.0, plans/OPTION_C_PLAN.md D49); none of these runs paid a warm-up
    private static string Tab(string key, string label, bool selected, string count = "") =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(selected ? "true" : "false")}\" tabindex=\"{(selected ? "0" : "-1")}\" data-tab=\"{key}\" title=\"{TabTitle(key)}\">{label}{count}</button>";

    private static string TabTitle(string key) => key switch
    {
        "features" => "The features and their scenarios, with the filters applied",
        "timeline" => "Every scenario as a bar, slowest first",
        "components" => "The services, databases and queues the scenarios called, drawn as one diagram",
        "history" => "''' + HISTORY + r'''",
        _ => "''' + DIAGNOSTICS + r'''"
    };
''')
pin(P, r'''<details class=\"report-diagnostics\" open><summary tabindex=\"-1\">"''',
    r'''<details class=\"report-diagnostics\" open><summary tabindex=\"-1\"''' + title(DIAGNOSTICS) + r'''>"''')
pin(P, r'''class=\"filters-empty-clear\">Clear All</button>''', r'''class=\"filters-empty-clear\"''' + title(CLEAR_EMPTY) + r'''>Clear All</button>''')

P = T + 'Reports/ReportGeneratorDiagnosticsScopeTests.cs'
pin(P, r'''<details class=\"report-diagnostics\" open><summary tabindex=\"-1\">"''',
    r'''<details class=\"report-diagnostics\" open><summary tabindex=\"-1\"''' + title(DIAGNOSTICS) + r'''>"''')

P = T + 'Ingestion/IngestHostDiagnosticsTests.cs'
pin(P, r'''Assert.StartsWith("<details class=\"report-diagnostics\"><summary>", html);''',
    r'''Assert.StartsWith("<details class=\"report-diagnostics\"><summary''' + title(DIAGNOSTICS_DISCLOSURE) + r'''>", html);''')

# ---------------------------------------------------------------------------------------------------------------------
# The bar's filters and the box: readouts, Clear All, the exports, the happy-path segments, the chips
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Reports/FilterBarTests.cs'
pin(P, r'''<span class=\"nav-readout\" aria-hidden=\"true\"><span class=\"nav-readout-long\">''',
    r'''<span class=\"nav-readout\" aria-hidden=\"true\"''' + title(SHOWING) + r'''><span class=\"nav-readout-long\">''')
# the Bakery has times, a happy path and tags (its categories are tags), and no call
pin(P, r'''class=\"nav-clear\" title=\"Clear every filter\" hidden>''',
    r'''class=\"nav-clear\"''' + title(CLEAR.format('duration, happy paths and tags')) + r''' hidden>''')
pin(P, r'''class=\"filters-empty-clear\">Clear All</button>''', r'''class=\"filters-empty-clear\"''' + title(CLEAR_EMPTY) + r'''>Clear All</button>''')
# the CSV export, the box's last control before its hidden close
pin(P, r'''            "onclick=\"export_csv()\">",''', r'''            "onclick=\"export_csv()\"''' + title(EXPORT_CSV) + r'''>",''')

P = T + 'Reports/FilterRowsTests.cs'
pin(P, r'''data-hp=\"off\" aria-pressed=\"true\">Show</button>''',
    r'''data-hp=\"off\" aria-pressed=\"true\"''' + title('Happy-path scenarios and the rest') + r'''>Show</button>''')
pin(P, r'''data-hp=\"in\" aria-pressed=\"false\">Only</button>''',
    r'''data-hp=\"in\" aria-pressed=\"false\"''' + title('Only the scenarios tagged as happy paths') + r'''>Only</button>''')
pin(P, r'''data-hp=\"out\" aria-pressed=\"false\">Hide</button>''',
    r'''data-hp=\"out\" aria-pressed=\"false\"''' + title('Leave out the scenarios tagged as happy paths') + r'''>Hide</button>''')
# the chips of the Tags and Dependencies rows: their count, then what a click does
pin_re(P, r'title=\\"(\d+ scenarios?)\\">', lambda m: 'title=' + Q + m.group(1) + '. ' + CHIP_CLICKS + Q + '>', count=18)
pin(P, r'''<span class=\"filter-readout\" role=\"status\">''', r'''<span class=\"filter-readout\" role=\"status\"''' + title(SHOWING) + '>', count=2)
# the Bakery has times, a happy path, tags (its categories among them) and calls
pin(P, r'''<button class=\"export-btn\" onclick=\"clear_all_filters()\">Clear All</button>''',
    r'''<button class=\"export-btn\" onclick=\"clear_all_filters()\"''' + title(CLEAR.format('duration, happy paths, tags and dependencies')) + r'''>Clear All</button>''')
pin(P, r'''<button class=\"export-btn\" onclick=\"export_html()\">''',
    r'''<button class=\"export-btn\" onclick=\"export_html()\"''' + title('Download a copy of this report holding only the features the filters show') + '>')
pin(P, r'''<button class=\"export-btn\" onclick=\"export_csv()\">''',
    r'''<button class=\"export-btn\" onclick=\"export_csv()\"''' + title(EXPORT_CSV) + '>')

# ---------------------------------------------------------------------------------------------------------------------
# The payload controls, report level and scenario level
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Reports/ScenarioToolbarTests.cs'
pin(P, r'''data-dtype=\"seq\">Sequence Diagrams</button>''', r'''data-dtype=\"seq\"''' + title(SEQ) + r'''>Sequence Diagrams</button>''', count=2)
pin(P, r'''data-dtype=\"activity\">Activity Diagrams</button>''', r'''data-dtype=\"activity\"''' + title(ACTIVITY) + r'''>Activity Diagrams</button>''', count=2)
pin(P, r'''<summary class=\"h4\">Sequence Diagrams<button''', r'''<summary class=\"h4\"''' + title(SEQ_SECTION) + r'''>Sequence Diagrams<button''')
pin(P, r'''<span class=\"details-radio-label\">Payloads</span>''',
    r'''<span class=\"details-radio-label\"''' + title(LABEL.format(SCENARIO)) + r'''>Payloads</span>''')
pin(P, r'''onclick=\"window._setAllNotes(this,'expanded')\"><span''', r'''onclick=\"window._setAllNotes(this,'expanded')\"''' + title(EXPANDED.format(SCENARIO)) + '><span')
pin(P, r'''onclick=\"window._setAllNotes(this,'collapsed')\"><span''', r'''onclick=\"window._setAllNotes(this,'collapsed')\"''' + title(COLLAPSED.format(SCENARIO)) + '><span')
pin(P, r'''onclick=\"window._setAllNotes(this,'truncated')\"><span''', r'''onclick=\"window._setAllNotes(this,'truncated')\"''' + title(TRUNCATED.format(SCENARIO)) + '><span')
pin(P, r'''<select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\">''',
    r'''<select class=\"truncate-lines-select\" autocomplete=\"off\" aria-label=\"Payload length\"''' + title(LINES.format(SCENARIO))
    + r''' onchange=\"window._setScenarioTruncateLines(this)\">''')
pin(P, r'''onclick=\"window._toggleScenarioHeaders(this)\"><span''', r'''onclick=\"window._toggleScenarioHeaders(this)\"''' + title(HEADERS.format(SCENARIO)) + '><span')
pin(P, r'''onclick=\"window._toggleScenarioSteps(this)\"><span''', r'''onclick=\"window._toggleScenarioSteps(this)\"''' + title(STEPS.format(SCENARIO)) + '><span')
pin(P, r'''aria-label=\"Payload format\" title=\"Payload format\" onchange=\"window._setScenarioNoteFormat(this)\"''',
    r'''aria-label=\"Payload format\"''' + title(FORMAT.format(SCENARIO)) + r''' onchange=\"window._setScenarioNoteFormat(this)\"''')
pin(P, r'''title=\"Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.\" onchange=\"window._setScenarioNoteWidth(this)\"''',
    title(WIDTH.format(SCENARIO))[1:] + r''' onchange=\"window._setScenarioNoteWidth(this)\"''')

P = T + 'Reports/ToggleDefaultsMarkupTests.cs'
# the report's Expanded, in its fact and in the merge renderer's
pin(P, r'''onclick=\"window._setReportDetails('expanded')\"><span''', r'''onclick=\"window._setReportDetails('expanded')\"''' + title(EXPANDED.format(REPORT)) + '><span', count=2)
pin(P, r'''onclick=\"window._setReportDetails('truncated')\"><span''', r'''onclick=\"window._setReportDetails('truncated')\"''' + title(TRUNCATED.format(REPORT)) + '><span')
pin(P, r'''<select class=\"truncate-lines-select\" autocomplete=\"off\" disabled onchange=\"window._setTruncateLines(this)\">''',
    r'''<select class=\"truncate-lines-select\" autocomplete=\"off\" aria-label=\"Payload length\"''' + title(LINES.format(REPORT))
    + r''' disabled onchange=\"window._setTruncateLines(this)\">''')
pin(P, r'''onclick=\"window._setAllNotes(this,'expanded')\"><span''', r'''onclick=\"window._setAllNotes(this,'expanded')\"''' + title(EXPANDED.format(SCENARIO)) + '><span')
pin(P, r'''onclick=\"window._toggleHeaders(this)\"><span''', r'''onclick=\"window._toggleHeaders(this)\"''' + title(HEADERS.format(REPORT)) + '><span')
pin(P, r'''onclick=\"window._toggleScenarioHeaders(this)\"><span''', r'''onclick=\"window._toggleScenarioHeaders(this)\"''' + title(HEADERS.format(SCENARIO)) + '><span')
pin(P, r'''onclick=\"window._toggleAssertions(this)\"><span''', r'''onclick=\"window._toggleAssertions(this)\"''' + title(ASSERTIONS.format(REPORT)) + '><span')
pin(P, r'''onclick=\"window._toggleSteps(this)\"><span''', r'''onclick=\"window._toggleSteps(this)\"''' + title(STEPS.format(REPORT)) + '><span')
pin(P, r'''onclick=\"window._toggleDatabases(this)\"><span''', r'''onclick=\"window._toggleDatabases(this)\"''' + title(DATABASES.format(REPORT)) + '><span')
pin(P, r'''title=\"Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.\" onchange=\"window._setNoteWidth(this)\"''',
    title(WIDTH.format(REPORT))[1:] + r''' onchange=\"window._setNoteWidth(this)\"''')
pin(P, r'''data-dtype=\"activity\">Activity Diagrams</button>''', r'''data-dtype=\"activity\"''' + title(ACTIVITY) + r'''>Activity Diagrams</button>''')
pin(P, r'''data-dtype=\"seq\">Sequence Diagrams</button>''', r'''data-dtype=\"seq\"''' + title(SEQ) + r'''>Sequence Diagrams</button>''')
pin(P, r'''data-dtype=\"flame\">Flame Chart</button>''', r'''data-dtype=\"flame\"''' + title(FLAME) + r'''>Flame Chart</button>''')

# ---------------------------------------------------------------------------------------------------------------------
# A scenario's sections: its diagrams, its steps and background, the feature table's names, the CI run's link
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Reports/DiagramToggleReportTests.cs'
pin(P, r'''<summary class=\"h4\">Sequence Diagrams<button''', r'''<summary class=\"h4\"''' + title(SEQ_SECTION) + r'''>Sequence Diagrams<button''', count=2)
pin(P, r'''<summary class=\"h4\">Diagrams<button''', r'''<summary class=\"h4\"''' + title(SECTION) + r'''>Diagrams<button''')
# turned round: no heading has the old opening tag once headings carry a title, so the check could not fail
pin(P, r'''Assert.DoesNotContain("<summary class=\"h4\">Sequence Diagrams", content);''',
    r'''Assert.DoesNotContain(">Sequence Diagrams<button type=\"button\" class=\"scenario-diagram-controls-toggle", content);''')
pin(P, r'''<summary class=\"h4\">Activity Diagrams<span class=\"span-count-warning\">''',
    r'''<summary class=\"h4\"''' + title(ACTIVITY) + r'''>Activity Diagrams<span class=\"span-count-warning\">''')
pin(P, r'''data-dtype=\"flame\">Flame Chart</button><span class=\"span-count-warning\">''',
    r'''data-dtype=\"flame\"''' + title(FLAME) + r'''>Flame Chart</button><span class=\"span-count-warning\">''')

for P in (T + 'Reports/StepRenderingReportTests.cs', T + 'Reports/ParameterizedGroupRenderTests.cs'):
    # none of these steps has a time
    pin(P, r'''<summary class=\"h4\">Steps</summary>''', r'''<summary class=\"h4\"''' + title(STEPS_SECTION) + r'''>Steps</summary>''')
    pin(P, r'''Assert.Contains("<summary class=\"h4\">Background Steps</summary>", content);''',
        r'''Assert.Contains("<summary class=\"h4\"''' + title(BACKGROUND) + r'''>Background Steps</summary>", content);''')

# an outline group's toolbar is the scenario toolbar, built once, so its toggles carry the scenario-level titles
P = T + 'Reports/ParameterizedGroupRenderTests.cs'
pin(P, r'''aria-pressed=\"true\" onclick=\"window._toggleScenarioAssertions(this)\"><span''',
    r'''aria-pressed=\"true\" onclick=\"window._toggleScenarioAssertions(this)\"''' + title(ASSERTIONS.format(SCENARIO)) + '><span')
pin(P, r'''aria-pressed=\"true\" onclick=\"window._toggleScenarioSteps(this)\"><span''',
    r'''aria-pressed=\"true\" onclick=\"window._toggleScenarioSteps(this)\"''' + title(STEPS.format(SCENARIO)) + '><span')

P = T + 'Reports/FeaturesToolsTests.cs'
pin(P, r'''class=\"feature-open\">''', r'''class=\"feature-open\"''' + title("Open this feature's card in the Full view") + '>', count=2)

P = T + 'Reports/RunBandTests.cs'
pin(P, r'''rel=\"noopener noreferrer\">View run</a>''', r'''rel=\"noopener noreferrer\"''' + title('Open the CI run this report came from') + '>View run</a>')

# ---------------------------------------------------------------------------------------------------------------------
# Attachments: a file or a link, and an image, the scenario's or a step's
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Ingestion/IngestAttachmentTests.cs'
pin(P, r'''href=\"attachments/trace.zip\">trace.zip</a>''', r'''href=\"attachments/trace.zip\"''' + title('Open trace.zip, a file this scenario attached') + '>trace.zip</a>')
pin(P, r'''href=\"attachments/report.png\">report.png</a>''', r'''href=\"attachments/report.png\"''' + title('Open report.png, a file this scenario attached') + '>report.png</a>')

P = T + 'Reports/MissingAttachmentTests.cs'
pin(P, r'''href=\"attachments/shot%231.png\" onclick=\"openLightbox(event, this)\"><img''',
    r'''href=\"attachments/shot%231.png\" onclick=\"openLightbox(event, this)\"''' + title('Show shot#1.png, an image this scenario attached, at full size') + '><img')
pin(P, r'''href=\"attachments/what%3F.txt\">what?.txt</a>''', r'''href=\"attachments/what%3F.txt\"''' + title('Open what?.txt, a file this step attached') + '>what?.txt</a>')
pin(P, r'''href=\"attachments/a%25%23.log\">a%#.log</a>''', r'''href=\"attachments/a%25%23.log\"''' + title('Open a%#.log, a file this step attached') + '>a%#.log</a>')
pin(P, r'''href=\"https://example.com/trace?id=1#top\">Trace</a>''', r'''href=\"https://example.com/trace?id=1#top\"''' + title('Open Trace, a link this scenario attached') + '>Trace</a>')

P = T + 'Reports/ScenarioImageLightboxTests.cs'
pin(P, r'''Assert.Contains($"<a class=\"attachment-image-link\" href=\"attachments/{name}\" onclick=\"openLightbox(event, this)\">", links);''',
    r'''Assert.Contains($"<a class=\"attachment-image-link\" href=\"attachments/{name}\" onclick=\"openLightbox(event, this)\" title=\"Show {name}, an image this {(name == "step.png" ? "step" : "scenario")} attached, at full size\">", links);''')

# ---------------------------------------------------------------------------------------------------------------------
# What a script draws, a table cell's whole value, a popup's diagnostic info
# ---------------------------------------------------------------------------------------------------------------------
P = T + 'Reports/DiagramContextMenuTests.cs'
pin(P, r'''Assert.Contains("<summary>Raw PlantUML</summary>", _plantUmlScript);''',
    r'''Assert.Contains("<summary''' + title('The PlantUML source this diagram was drawn from') + r'''>Raw PlantUML</summary>", _plantUmlScript);''')

P = T + 'Reports/ParameterValueRendererTests.cs'
pin(P, r'''Assert.Contains("<summary>", ''', r'''Assert.Contains("<summary''' + title('Show or hide the whole value') + r'''>", ''', count=3)

P = T + 'InternalFlow/PopupNotesByClassTests.cs'
pin(P, r'''<details class=\"iflow-diagnostics\"><summary>Diagnostic info</summary>''',
    r'''<details class=\"iflow-diagnostics\"><summary''' + title('Why this call may have no spans: click to show or hide the details') + r'''>Diagnostic info</summary>''')

# the new files take the line ends the checkout gives the files beside them (this folder holds them with LF; with no
# file beside them, as in the package's dry run, they stay LF)
sources = {}
for name, target in copies.items():
    sources[name] = open(os.path.join(HERE, name), encoding='utf-8', newline='').read()
    assert '\r' not in sources[name], (name, 'held with CRLF in the package')
    folder = os.path.dirname(ROOT + target)
    beside = sorted(f for f in os.listdir(folder) if f.endswith('.cs'))
    if beside and '\r\n' in open(os.path.join(folder, beside[0]), encoding='utf-8', newline='').read():
        sources[name] = sources[name].replace('\n', '\r\n')

changed = sorted(p for p in files if files[p] != originals[p])
assert len(changed) == 22, (len(changed), changed)

# written only once every check above has held
for path in changed:
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(files[path].replace('\n', '\r\n') if crlf[path] else files[path])
for name, target in copies.items():
    open(ROOT + target, 'w', encoding='utf-8', newline='').write(sources[name])
print('ok:', len(changed), 'pinned files rewritten,', ', '.join(sorted(copies.values())))
