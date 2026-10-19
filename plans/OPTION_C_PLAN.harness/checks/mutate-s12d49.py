# S12 D49's mutations: each undoes one title, or one where-word a title holds, rebuilds, runs the test that should catch
# it, expects it to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s12d49.py [part of a test name, to run only its mutations]   (takes a while: each run rebuilds)
#        python mutate-s12d49.py ControlTitlesAuditTests   the Playwright ones only
#        python mutate-s12d49.py --check [root]   checks that every mutation's text is in its file exactly once, under root
#                                                 (default: this checkout), and runs nothing
# A file is matched and mutated with LF line ends and written back with the ends it had, as the scripts edit it.
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
HH = 'src/Kronikol/History/HistoryHtml.cs'
PVR = 'src/Kronikol/Reports/ParameterValueRenderer.cs'
IFLOW = 'src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs'
RENDER = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
POPUP = 'src/Kronikol/Reports/internal-flow-popup-script.js'
STEPS = 'src/Kronikol/Reports/report-step-status-titles.js'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
C = 'Kronikol.Tests.Reports.ControlTitlesTests.'
P = 'Kronikol.Tests.EndToEnd.ControlTitlesAuditTests.'
PARAMS = 'Kronikol.Tests.Reports.ParameterValueRendererTests.'
EMPTY = 'Kronikol.Tests.InternalFlow.PopupNotesByClassTests.An_empty_popups_diagnostics_are_a_classed_block'
L = '\n'.join

PAYLOADS = C + 'The_report_bars_payload_controls_say_they_act_on_every_sequence_diagram_in_the_report'
OWN = C + 'A_scenarios_payload_controls_say_they_act_on_its_own_sequence_diagrams'
TABS = C + 'Each_tab_of_the_report_bar_says_what_it_shows'
WARM = C + 'The_timeline_tab_and_the_seconds_field_name_the_warm_up_only_where_a_scenario_paid_one'
BOX = C + 'The_filter_box_says_what_each_of_its_controls_does'
CLEAR = C + 'Clear_All_names_the_search_and_every_row_the_box_has_in_the_box_and_in_the_bar'
PAGE = C + 'The_pages_own_buttons_and_links_say_where_they_go_or_what_they_do'
SECTIONS = C + 'A_scenarios_sections_say_what_they_hold'
ONE_VIEW = C + 'A_section_with_one_view_is_titled_with_what_that_view_shows'
ATTACHED = C + 'An_attachment_says_what_it_opens_and_whose_it_is'
OUTLINE = C + 'An_outlines_failed_example_and_its_attachments_have_the_titles_a_scenarios_have'
IN_TAB = C + 'In_its_tab_a_sections_heading_says_what_the_section_is_and_not_that_a_click_shows_or_hides_it'
OUTSIDE = C + 'Outside_a_tab_a_sections_heading_says_a_click_shows_or_hides_it'
SCRIPTS = C + 'The_scripts_title_what_they_draw_or_make_live'
POPUP_FACT = P + 'The_internal_flow_popup_titles_its_close_its_view_tabs_and_its_diagnostic_info'
COMPOSITE = P + 'A_composite_steps_summary_says_how_many_steps_it_holds_once_it_is_pointed_at_or_focused'
AUDIT = P + 'Every_control_that_shows_has_a_title_and_a_name'

# the tails of a scenario's attachments, in a scenario's card and in an outline example's panel
CARD_TAIL = L(['', '                        }', '                    }', '                    body.Append("</div>");', '                }', '',
               '                var diagramsForTest'])
PANEL_TAIL = L(['', '                        }', '                    }', '                    body.Append("</div>");', '                }', '',
                '                if (s.Result == ExecutionResult.Failed)'])
IMAGE_THEN_FILE = L(['body.Append(AttachmentImageLink(attachment, "scenario"));', '                        }', '                        else',
                     '                        {', '                            body.Append(AttachmentLink(attachment, "scenario"));'])
RAW = '<summary title="The PlantUML source this diagram was drawn from">Raw PlantUML</summary>'

MUTATIONS = [
    # the report bar's tabs
    ("the Features tab has no title", RG, 'ReportTab("features", "Features", "The features and their scenarios, with the filters applied", startTab',
     'ReportTab("features", "Features", "", startTab', CORE, TABS),
    ("the Timeline tab has no title", RG, 'ReportTab("timeline", "Timeline", "Every scenario as a bar, slowest first" + (',
     'ReportTab("timeline", "Timeline", "" + (', CORE, TABS),
    ("the Timeline tab never names the warm-up", RG, '(warmUp.ScenarioMs.Count > 0 ? ", with the first-call warm-up it paid" : "")',
     '(warmUp.ScenarioMs.Count > 0 ? "" : "")', CORE, WARM),
    ("the Timeline tab names a warm-up no scenario paid", RG, '(warmUp.ScenarioMs.Count > 0 ? ", with the first-call warm-up it paid"',
     '(true ? ", with the first-call warm-up it paid"', CORE, WARM),
    ("the Components tab has no title", RG,
     'ReportTab("components", "Components", "The services, databases and queues the scenarios called, drawn as one diagram", startTab',
     'ReportTab("components", "Components", "", startTab', CORE, TABS),
    ("the History tab has no title", RG, 'ReportTab("history", "History", HistoryHtml.Description, startTab',
     'ReportTab("history", "History", "", startTab', CORE, TABS),
    ("the Diagnostics tab has no title", RG, 'ReportTab("diagnostics", "Diagnostics", DiagnosticsDescription, startTab',
     'ReportTab("diagnostics", "Diagnostics", "", startTab', CORE, TABS),

    # where the payload controls act: the report bar on every sequence diagram, a scenario's bar on its own
    ("the report bar says it acts on the scenario's diagrams", RG,
     '''scenarioLevel ? "this scenario's sequence diagrams" : "every sequence diagram in the report";''',
     '''scenarioLevel ? "this scenario's sequence diagrams" : "this scenario's sequence diagrams";''', CORE, PAYLOADS),
    ("a scenario's bar says it acts on every diagram in the report", RG,
     '''scenarioLevel ? "this scenario's sequence diagrams" : "every sequence diagram in the report";''',
     '''scenarioLevel ? "every sequence diagram in the report" : "every sequence diagram in the report";''', CORE, OWN),

    # the payload controls
    ("Payloads has no title", RG,
     r''' title=\"How the payloads in {diagrams} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit\">Payloads</span>''',
     r'''>Payloads</span>''', CORE, PAYLOADS),
    ("Expanded has no title", RG, r''' title=\"The payloads in {diagrams} show in full\"''', '', CORE, PAYLOADS),
    ("Collapsed has no title", RG, r''' title=\"The payloads in {diagrams} fold to one line each; a box's + button opens it\"''', '', CORE, PAYLOADS),
    ("Truncated has no title", RG, r''' title=\"Payloads in {diagrams} longer than the line limit beside this are cut at that many lines\"''', '',
     CORE, PAYLOADS),
    ("the line count has no title", RG, r''' title=\"How many lines a cut payload box keeps in {diagrams} (with Truncated picked)\"''', '',
     CORE, PAYLOADS),
    ("the line count has no name", RG, r'''autocomplete=\"off\" aria-label=\"Payload length\" title=\"How many lines''',
     r'''autocomplete=\"off\" title=\"How many lines''', CORE, PAYLOADS),
    ("a toggle has no title", RG, r''' onclick=\"{handler}\" title=\"{ToggleTitle(toggleName, PayloadsWhere(scenarioLevel))}\">''',
     r''' onclick=\"{handler}\">''', CORE, PAYLOADS),
    ("a scenario's toggles say they act on the report", RG, 'ToggleTitle(toggleName, PayloadsWhere(scenarioLevel))',
     'ToggleTitle(toggleName, PayloadsWhere(false))', CORE, OWN),
    ("Headers keeps only its name", RG, '"headers" => $"Show or hide the headers at the top of each payload in {diagrams}",',
     '"headers" => "Headers",', CORE, PAYLOADS),
    ("Assertions keeps only its name", RG, '''"assertions" => $"Show or hide the boxes with the test's assertions in {diagrams}",''',
     '"assertions" => "Assertions",', CORE, PAYLOADS),
    ("Steps keeps only its name", RG, '"steps" => $"Show or hide the bars marking where each step starts in {diagrams}",',
     '"steps" => "Steps",', CORE, PAYLOADS),
    ("Databases keeps only its name", RG, '"databases" => $"Show or hide the databases and their calls in {diagrams}",',
     '"databases" => "Databases",', CORE, PAYLOADS),
    ("Events keeps only its name", RG,
     '"events" => $"Show or hide the calls to and from message brokers (the participants drawn as queues), with their payloads, in {diagrams}",',
     '"events" => "Events",', CORE, PAYLOADS),
    ("Setup keeps only its name", RG,
     '"setup" => $"Show or hide the setup in {diagrams}: everything before the first When step, or the Setup section where the report keeps setup apart",',
     '"setup" => "Setup",', CORE, PAYLOADS),
    ("the format select keeps its old title", RG,
     '$"How the payloads in {PayloadsWhere(scenarioLevel)} are written: JSON as captured, or the same data as YAML";',
     '"Payload format";', CORE, PAYLOADS),
    ("a scenario's format select says it acts on the report", RG, r'''title=\"{NoteFormatTitle(scenarioLevel: true)}\"''',
     r'''title=\"{NoteFormatTitle(scenarioLevel: false)}\"''', CORE, OWN),
    ("the width select keeps its old title", RG,
     '$"How wide the payloads are in {PayloadsWhere(scenarioLevel)}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";',
     '"Payload width. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";', CORE, PAYLOADS),
    ("a scenario's width select says it acts on the report", RG, 'NoteWidthTitle(scenarioLevel: true)', 'NoteWidthTitle(scenarioLevel: false)',
     CORE, OWN),

    # the diagram views and the section's heading
    ("a view's tab has no title", RG, r''' data-dtype=\"{dtype}\" title=\"{DiagramViewTitle(dtype)}\">{label}</button>''',
     r''' data-dtype=\"{dtype}\">{label}</button>''', CORE, SECTIONS),
    ("the sequence view keeps only its name", RG, '"seq" => "The calls between the test and the services, in order",',
     '"seq" => "Sequence Diagrams",', CORE, SECTIONS),
    ("the activity view keeps only its name", RG, '"activity" => "The work inside the services, span by span, as an activity diagram",',
     '"activity" => "Activity Diagrams",', CORE, ONE_VIEW),
    ("the flame view keeps only its name", RG, '_ => "Where the time went inside the services, span by span, as nested bars"',
     '_ => "Flame Chart"', CORE, ONE_VIEW),
    ("a section of several views is titled as sequence diagrams", RG, '''views > 1 ? "This scenario's diagrams" :''',
     '''views > 1 ? "This scenario's sequence diagrams" :''', CORE, SECTIONS),
    ("a section of sequence diagrams is titled as several views", RG, '''hasSeq ? "This scenario's sequence diagrams" :''',
     '''hasSeq ? "This scenario's diagrams" :''', CORE, SECTIONS),
    ("a section of one view takes the activity view's title", RG, 'DiagramViewTitle(hasActivity ? "activity" : "flame")',
     'DiagramViewTitle("activity")', CORE, ONE_VIEW),
    ("the diagrams' heading has no title", RG, r'''body.Append($"<summary class=\"h4\" title=\"{tooltip}\">").Append(title);''',
     r'''body.Append("<summary class=\"h4\">").Append(title);''', CORE, SECTIONS),

    # the Summary view's feature names
    ("a feature's name has no title", RG, r''' title=\"Open this feature's card in the Full view\"''', '', CORE, PAGE),

    # the filter box, and the bar's copies
    ("Clear All does not name the search", RG, '"Clear every filter: the search with its dep:, duration: and is: terms, and the "',
     '"Clear every filter: the "', CORE, CLEAR),
    ("Clear All names only the statuses", RG, '+ Listed(FilterRows(features, hasDurations, hasDependencies, "statuses"));', '+ "statuses";',
     CORE, CLEAR),
    ("Clear All forgets the dependencies", RG, 'ClearAllTitle(features, hasDurations, allDependencies.Count > 0)',
     'ClearAllTitle(features, hasDurations, false)', CORE, CLEAR),
    ("the box's Clear All has no title", RG, 'onclick="clear_all_filters()" title="{clearTitle}">', 'onclick="clear_all_filters()">', CORE, CLEAR),
    ("the bar's Clear All keeps its old title", RG, 'class="nav-clear" title="{clearTitle}" hidden>',
     'class="nav-clear" title="Clear every filter" hidden>', CORE, CLEAR),
    ("the box's readout has no title", RG, '<span class="filter-readout" role="status" title="{FiltersLeaveShowing}">',
     '<span class="filter-readout" role="status">', CORE, C + 'Both_readouts_say_what_they_count'),
    ("the bar's readout has no title", RG, '<span class="nav-readout" aria-hidden="true" title="{FiltersLeaveShowing}">',
     '<span class="nav-readout" aria-hidden="true">', CORE, C + 'Both_readouts_say_what_they_count'),
    ("Export HTML has no title", RG, ' title="Download a copy of this report holding only the features the filters show"', '', CORE, BOX),
    ("Export CSV has no title", RG,
     ' title="Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds"', '', CORE, BOX),
    ("the search field has no title", RG, 'onkeyup="search_scenarios()" title="{SearchTitle}" />', 'onkeyup="search_scenarios()" />', CORE, BOX),
    ("the search help keeps its old title", RG, 'onclick="toggle_search_help()" title="{SearchHelpTitle}">',
     'onclick="toggle_search_help()" title="Search syntax help">', CORE, BOX),
    ("the phone's Filters has no title", RG, '<div class="mobile-filter-toggle" title="Show or hide the filters">',
     '<div class="mobile-filter-toggle">', CORE, BOX),
    ("a status has no title", RG, 'onclick="toggle_status(this)" title="{StatusToggleTitle(status)}">', 'onclick="toggle_status(this)">', CORE, BOX),
    ("Skipped is said as a verb", RG, 'ExecutionResult.Skipped => "were skipped",', 'ExecutionResult.Skipped => "skipped",', CORE, BOX),
    ("the seconds field has no title", RG, ' title="Seconds: scenarios taking at least this long{notWarm}" />', ' />', CORE, BOX),
    ("the seconds field never names the warm-up", RG, 'title="Seconds: scenarios taking at least this long{notWarm}" />',
     'title="Seconds: scenarios taking at least this long" />', CORE, WARM),
    ("Show has no title", RG, 'aria-pressed="true" title="Happy-path scenarios and the rest">Show</button>', 'aria-pressed="true">Show</button>',
     CORE, BOX),
    ("Only has no title", RG, 'aria-pressed="false" title="Only the scenarios tagged as happy paths">Only</button>',
     'aria-pressed="false">Only</button>', CORE, BOX),
    ("Hide has no title", RG, 'aria-pressed="false" title="Leave out the scenarios tagged as happy paths">Hide</button>',
     'aria-pressed="false">Hide</button>', CORE, BOX),
    ("a chip says only its count", RG, r'''title=\"{ScenarioCount(scenarios)}. {ChipClicks}\">''', r'''title=\"{ScenarioCount(scenarios)}\">''',
     CORE, C + 'A_chip_gives_the_count_of_scenarios_it_picks_out_then_what_a_click_does'),

    # the page's own buttons and links
    ("the empty state's Clear All has no title", RG, 'class="filters-empty-clear" title="Clear every filter">', 'class="filters-empty-clear">',
     CORE, PAGE),
    ("Next Failure has no title", RG, ' title="Go to the next failed scenario; the count says which one of how many">Next Failure', '>Next Failure',
     CORE, PAGE),
    ("Next Failure has no title, by the generator's audit", RG,
     ' title="Go to the next failed scenario; the count says which one of how many">Next Failure', '>Next Failure',
     CORE, C + 'No_control_the_generator_writes_is_left_without_a_title'),
    ("back to top has no name", RG, ' aria-label="Back to top" title="Back to the top of the report">', ' title="Back to the top of the report">',
     CORE, PAGE),
    ("back to top has no title", RG, ' aria-label="Back to top" title="Back to the top of the report">', ' aria-label="Back to top">', CORE, PAGE),
    ("the CI run's link has no title", RG, r''' rel=\"noopener noreferrer\" title=\"Open the CI run this report came from\">View run</a>''',
     r''' rel=\"noopener noreferrer\">View run</a>''', CORE, PAGE),
    ("a cluster's scenario link has no title", RG, r'''\" title=\"Go to this scenario\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>''',
     r'''\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>''', CORE, PAGE),

    # a scenario's sections
    ("a scenario's Failure Result has no title", RG, '<summary class="h4" title="{FailureResultTitle}">Failure Result</summary>',
     '<summary class="h4">Failure Result</summary>', CORE, SECTIONS),
    ("an outline example's Failure Result has no title", RG, r'''<summary class=\"h4\" title=\"{FailureResultTitle}\">Failure Result</summary>''',
     r'''<summary class=\"h4\">Failure Result</summary>''', CORE, OUTLINE),
    ("Background Steps has no title", RG, ''' title="The steps of the feature's background, which run before this scenario's own">Background Steps''',
     '>Background Steps', CORE, SECTIONS),
    ("Steps has no title", RG,
     '''<summary class="h4" title="This scenario's steps{(steps.Any(s => s.Duration.HasValue) ? ", with how long each took" : "")}">Steps</summary>''',
     '<summary class="h4">Steps</summary>', CORE, SECTIONS),
    ("Steps never says how long each took", RG, '(steps.Any(s => s.Duration.HasValue) ? ", with how long each took" : "")', '("")',
     CORE, SECTIONS),
    ("Steps says how long each took where no step has a time", RG, '(steps.Any(s => s.Duration.HasValue) ? ', '(true ? ', CORE, SECTIONS),

    # attachments: what they open, and whose they are
    ("a file's link has no title", RG,
     r''' title=\"Open {name}, {(attachment.IsUrl ? "a link" : "a file")} this {owner} attached\">{name}</a>''', r'''>{name}</a>''',
     CORE, ATTACHED),
    ("a link is said to be a file", RG, '{(attachment.IsUrl ? "a link" : "a file")}', '{("a file")}', CORE, ATTACHED),
    ("an image's link has no title", RG,
     r''' onclick=\"openLightbox(event, this)\" title=\"Show {name}, an image this {owner} attached, at full size\"><img''',
     r''' onclick=\"openLightbox(event, this)\"><img''', CORE, ATTACHED),
    ("a step's file is said to be the scenario's", RG, 'body.Append(AttachmentLink(attachment, "step"));',
     'body.Append(AttachmentLink(attachment, "scenario"));', CORE, ATTACHED),
    ("a step's image is said to be the scenario's", RG, 'body.Append(AttachmentImageLink(attachment, "step"));',
     'body.Append(AttachmentImageLink(attachment, "scenario"));', CORE, ATTACHED),
    ("a scenario's file is said to be a step's", RG, 'body.Append(AttachmentLink(attachment, "scenario"));' + CARD_TAIL,
     'body.Append(AttachmentLink(attachment, "step"));' + CARD_TAIL, CORE, ATTACHED),
    ("a scenario's image is said to be a step's", RG, IMAGE_THEN_FILE + CARD_TAIL,
     IMAGE_THEN_FILE.replace('AttachmentImageLink(attachment, "scenario")', 'AttachmentImageLink(attachment, "step")') + CARD_TAIL, CORE, ATTACHED),
    ("an outline example's file is said to be a step's", RG, 'body.Append(AttachmentLink(attachment, "scenario"));' + PANEL_TAIL,
     'body.Append(AttachmentLink(attachment, "step"));' + PANEL_TAIL, CORE, OUTLINE),
    ("an outline example's image is said to be a step's", RG, IMAGE_THEN_FILE + PANEL_TAIL,
     IMAGE_THEN_FILE.replace('AttachmentImageLink(attachment, "scenario")', 'AttachmentImageLink(attachment, "step")') + PANEL_TAIL, CORE, OUTLINE),

    # a step's parameters, and a table's whole values
    ("an inline value's button has no title", RG,
     r''' title=\"Show or hide the value of {System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">''', '>', CORE,
     C + 'A_steps_parameter_buttons_and_a_tables_whole_values_say_what_they_show'),
    ("a table's button has no title", RG, r''' title=\"Show {System.Net.WebUtility.HtmlEncode(seg.TableReference)} in its table\">''', '>', CORE,
     C + 'A_steps_parameter_buttons_and_a_tables_whole_values_say_what_they_show'),
    ("a value's whole has no title", PVR,
     L([r'''        var jsonBody = GenerateHighlightedJson(value, 0);''', r'''        if (jsonBody.Length > 10000)''',
        r'''            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";''', '',
        r'''        body.Append("<details class=\"param-expand\">");''',
        r'''        body.Append($"<summary title=\"Show or hide the whole value\">{System.Net.WebUtility.HtmlEncode(preview)}</summary>");''']),
     L([r'''        var jsonBody = GenerateHighlightedJson(value, 0);''', r'''        if (jsonBody.Length > 10000)''',
        r'''            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";''', '',
        r'''        body.Append("<details class=\"param-expand\">");''',
        r'''        body.Append($"<summary>{System.Net.WebUtility.HtmlEncode(preview)}</summary>");''']),
     CORE, PARAMS + 'RenderExpandable_renders_details_summary'),
    ("a parsed value's whole has no title", PVR,
     L([r'''        var jsonBody = GenerateHighlightedJsonFromParsed(parsed);''', r'''        if (jsonBody.Length > 10000)''',
        r'''            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";''', '',
        r'''        body.Append("<details class=\"param-expand\">");''',
        r'''        body.Append($"<summary title=\"Show or hide the whole value\">{System.Net.WebUtility.HtmlEncode(preview)}</summary>");''']),
     L([r'''        var jsonBody = GenerateHighlightedJsonFromParsed(parsed);''', r'''        if (jsonBody.Length > 10000)''',
        r'''            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";''', '',
        r'''        body.Append("<details class=\"param-expand\">");''',
        r'''        body.Append($"<summary>{System.Net.WebUtility.HtmlEncode(preview)}</summary>");''']),
     CORE, PARAMS + 'RenderExpandableFromParsed_shows_preview_with_type_name'),

    # History and Diagnostics: a heading in its tab, a disclosure outside one, and the history's links
    ("History's heading in its tab says a click shows or hides it", HH, r'''title=\"{(inTab ? Description : DisclosureTitle)}\"''',
     r'''title=\"{DisclosureTitle}\"''', CORE, IN_TAB),
    ("History's disclosure does not say a click shows or hides it", HH, r'''title=\"{(inTab ? Description : DisclosureTitle)}\"''',
     r'''title=\"{Description}\"''', CORE, OUTSIDE),
    ("a history link to the report has no title", HH, r''' title=\"Open this scenario in the report\">''', '>', CORE,
     C + 'A_history_link_says_where_it_goes'),
    ("a history link in the page has no title", HH, r''' title=\"Go to this scenario on the Features tab\">''', '>', CORE,
     C + 'A_history_link_says_where_it_goes'),
    ("Diagnostics' heading in its tab says a click shows or hides it", RG,
     'var title = inTab ? DiagnosticsDescription : "What Kronikol noticed while building this report: click to show or hide the list";',
     'var title = "What Kronikol noticed while building this report: click to show or hide the list";', CORE, IN_TAB),
    ("Diagnostics' disclosure does not say a click shows or hides it", RG,
     'var title = inTab ? DiagnosticsDescription : "What Kronikol noticed while building this report: click to show or hide the list";',
     'var title = DiagnosticsDescription;', CORE, OUTSIDE),

    # what the scripts draw or make live
    ("a failed drawing's source has no title", RENDER, "var rawDetails = '<details style=\"margin-top:0.5em\">" + RAW,
     "var rawDetails = '<details style=\"margin-top:0.5em\"><summary>Raw PlantUML</summary>", CORE, SCRIPTS),
    ("a drawing that failed to load has its source untitled", RENDER, "\n                + '<details style=\"margin-top:0.5em\">" + RAW,
     "\n                + '<details style=\"margin-top:0.5em\"><summary>Raw PlantUML</summary>", CORE, SCRIPTS),
    ("a drawing that failed in its worker has its source untitled", RENDER,
     "\n                        + '<details style=\"margin-top:0.5em\">" + RAW,
     "\n                        + '<details style=\"margin-top:0.5em\"><summary>Raw PlantUML</summary>", CORE, SCRIPTS),
    ("an empty call's diagnostic info has no title", IFLOW,
     L([r'''        sb.Append("<br/><br/><details class=\"iflow-diagnostics\"><summary title=\"Why this call may have no spans: click to show or hide the details\">Diagnostic info</summary><ul>");''',
        r'''        sb.Append($"<li>InternalFlowSpanStore: {totalSpansInStore} total span(s) globally</li>");''']),
     L([r'''        sb.Append("<br/><br/><details class=\"iflow-diagnostics\"><summary>Diagnostic info</summary><ul>");''',
        r'''        sb.Append($"<li>InternalFlowSpanStore: {totalSpansInStore} total span(s) globally</li>");''']), CORE, EMPTY),
    ("an empty ingested call's diagnostic info has no title", IFLOW,
     L([r'''        sb.Append("<br/><br/><details class=\"iflow-diagnostics\"><summary title=\"Why this call may have no spans: click to show or hide the details\">Diagnostic info</summary><ul>");''',
        r'''        sb.Append($"<li>Spans supplied to the ingest: {suppliedSpans}</li>");''']),
     L([r'''        sb.Append("<br/><br/><details class=\"iflow-diagnostics\"><summary>Diagnostic info</summary><ul>");''',
        r'''        sb.Append($"<li>Spans supplied to the ingest: {suppliedSpans}</li>");''']), CORE, EMPTY),
    ("the popup's cross has no name", POPUP, "        closeBtn.setAttribute('aria-label', 'Close');\n", '', E2E, POPUP_FACT),
    ("the popup's cross has no title", POPUP, "        closeBtn.title = 'Close (Esc)';\n", '', E2E, POPUP_FACT),
    ("the popup's view tabs have no title", POPUP,
     L(["                btn.title = btn.getAttribute('data-view') === 'flame'",
        "                    ? 'Where the time went inside the services for this call, span by span, as nested bars'",
        "                    : 'The work inside the services for this call, span by span, as an activity diagram';", '']), '', E2E, POPUP_FACT),
    ("the popup's view tabs swap their titles", POPUP, "btn.title = btn.getAttribute('data-view') === 'flame'",
     "btn.title = btn.getAttribute('data-view') === 'activity'", E2E, POPUP_FACT),
    ("a composite step's summary is never titled", STEPS, '        if (summary) giveSummaryTitle(summary);\n', '', E2E, COMPOSITE),
    ("one step inside is counted as many", STEPS, "summary.setAttribute('title', n === 1\n", "summary.setAttribute('title', n === 0\n", E2E, COMPOSITE),

    # the browser audit: a control that shows only in one state with no title, and one with no name either
    ("a feature's name has no title, in the Summary view", RG, r''' title=\"Open this feature's card in the Full view\"''', '', E2E, AUDIT),
    ("back to top has neither name nor title", RG, ' aria-label="Back to top" title="Back to the top of the report">', '>', E2E, AUDIT),
]


def read(path):
    raw = open(path, encoding='utf-8', newline='').read()
    return raw, '\r\n' in raw


def write(path, text, crlf):
    open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', '\r\n') if crlf else text)


if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/\\') + '/') if len(sys.argv) > 2 else ROOT
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        raw, _ = read(base + path)
        n = raw.replace('\r\n', '\n').count(old)
        if n != 1 or old == new:
            bad += 1
            print(f'NOT ONCE ({n}): {name}' if n != 1 else f'NO CHANGE: {name}')
    names = [m[0] for m in MUTATIONS]
    for dup in sorted({n for n in names if names.count(n) > 1}):
        bad += 1
        print(f'NAMED TWICE: {dup}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once')
    sys.exit(1 if bad else 0)

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]
killed = 0
for name, path, old, new, project, test in MUTATIONS:
    raw, crlf = read(ROOT + path)
    s = raw.replace('\r\n', '\n')
    assert s.count(old) == 1, (name, s.count(old))
    write(ROOT + path, s.replace(old, new), crlf)
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        # the file exactly as it was, line ends and all
        open(ROOT + path, 'w', encoding='utf-8', newline='').write(raw)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
