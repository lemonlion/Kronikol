# S11e's mutations: each undoes one part of the Components tab, rebuilds, runs the test that should catch it, expects it
# to FAIL, and puts the file back. Test runs start below normal priority.
# usage: python mutate-s11e.py [part of a test name, to run only its mutations]   (takes a while: each run rebuilds)
#        python mutate-s11e.py --check [root]   checks that every mutation's text is in its file exactly once, under root
#                                               (default: this checkout), and runs nothing
import os, subprocess, sys

# the checkout this file is in, whichever worktree that is (plans/OPTION_C_PLAN.harness/checks/)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
CDG = 'src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
MERGE = 'src/Kronikol/Reports/Merge/MergeableReportRenderer.cs'
CSS = 'src/Kronikol/Reports/stylesheets.css'
JS = 'src/Kronikol/Reports/report-components-script.js'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
C = 'Kronikol.Tests.Reports.ComponentsTabHtmlTests.'
P = 'Kronikol.Tests.EndToEnd.ComponentsTabTests.'
MUTATIONS = [
    # the generator: what the tab draws, and the model of it
    ("the embedded drawing keeps its title", CDG,
     '        if (roles is null)\n        {\n            sb.AppendLine($"title {WrapLabel(options.Title)}");',
     '        if (true)\n        {\n            sb.AppendLine($"title {WrapLabel(options.Title)}");',
     CORE, C + 'The_tab_is_headed_by_the_diagrams_title_which_its_drawing_leaves_out'),
    ("the heading ignores the configured title", CDG,
     'string.IsNullOrWhiteSpace(options.Title) ? EmbeddedComponentDiagram.DefaultHeading : options.Title,',
     'EmbeddedComponentDiagram.DefaultHeading,', CORE, C + 'A_configured_title_is_the_tabs_heading'),
    ("the direction's line is not counted", CDG, '            roles.Direction = NextLine();', '            roles.Direction = 0;',
     CORE, C + 'The_model_says_where_each_part_of_the_source_is_written'),
    ("a component's line is not counted", CDG, 'roles?.Components.Add((NextLine(), participant));',
     'roles?.Components.Add((0, participant));', CORE, C + 'The_model_says_where_each_part_of_the_source_is_written'),
    ("a connection's line is the one after it", CDG, 'roles?.Connections.Add((NextLine(), rel, bare));',
     'roles?.Connections.Add((NextLine() + 1, rel, bare));', CORE, C + 'The_model_says_where_each_part_of_the_source_is_written'),
    ("C4's relation keeps its label without its summary", CDG, '                bare = $"Rel({callerAlias}, {serviceAlias}, \\"\\"{tags})";',
     '                bare = edge;', CORE, C + 'The_embedded_form_is_the_diagram_without_its_title_and_GeneratePlantUml_is_unchanged'),
    ("TestsOf reads every log", CDG, '        DrawnCalls(logs, participantFilter)\n            .GroupBy(log => (Caller:',
     '        logs\n            .GroupBy(log => (Caller:', CORE, C + 'TestsOf_reads_the_calls_ExtractRelationships_counts'),
    # the report: where the tab is, what it holds, and when
    ("a run whose calls draw no connection keeps its tab", RG,
     '            if (componentRelationships.Length > 0)\n                componentDiagram =', '            if (true)\n                componentDiagram =',
     CORE, C + 'A_run_whose_calls_give_the_diagram_no_connection_has_no_components_tab'),
    ("no controls anywhere", RG, 'var componentControls = componentDiagram is not null && componentDiagramDrawn is null;',
     'var componentControls = false;', CORE, C + 'Where_the_page_draws_the_diagram_the_tab_has_its_three_controls_and_the_note'),
    ("controls under every renderer", RG, 'var componentControls = componentDiagram is not null && componentDiagramDrawn is null;',
     'var componentControls = componentDiagram is not null;', CORE,
     C + 'Under_a_renderer_that_draws_when_the_report_is_written_the_tab_has_no_controls'),
    ("the script is left out", RG, '        var componentsScript = componentControls\n', '        var componentsScript = false\n',
     CORE, C + 'Where_the_page_draws_the_diagram_the_tab_has_its_three_controls_and_the_note'),
    ("the model is left out", RG, '            if (componentControls)\n                componentHtml +=', '            if (false)\n                componentHtml +=',
     CORE, C + 'Where_the_page_draws_the_diagram_the_tab_has_its_three_controls_and_the_note'),
    ("Show without knowing who made which call", RG, '        if (usage)\n            html.Append(TimelineSegments("Show", "view",',
     '        if (true)\n            html.Append(TimelineSegments("Show", "view",', CORE,
     C + 'A_merged_report_has_Show_only_where_its_shards_carried_their_calls'),
    ("each connection names every scenario", RG,
     '.Select(c => c.TestIds!.Where(placeOf.ContainsKey).Select(t => placeOf[t]).Distinct().OrderBy(i => i).ToArray())',
     '.Select(c => placeOf.Values.OrderBy(i => i).ToArray())', CORE, C + 'Each_connection_names_the_scenarios_whose_calls_it_draws'),
    ("the model's line without the label is empty", RG, '["bare"] = c.Bare', '["bare"] = ""', CORE,
     C + 'The_model_says_where_each_part_of_the_source_is_written'),
    ("a merge never hands in its shards' calls", MERGE, 'logs: report.Interactions.Length > 0 ? report.Interactions : null);',
     'logs: null);', CORE, C + 'A_merged_report_has_Show_only_where_its_shards_carried_their_calls'),
    ("a merge hands in no model", MERGE, '            componentDiagram: componentDiagram,\n', '', CORE,
     C + 'A_merged_report_has_Show_only_where_its_shards_carried_their_calls'),
    ("the tab's card is not the timeline's", CSS, '.scenario-timeline, .component-diagram-section {', '.scenario-timeline {',
     CORE, C + 'The_tabs_card_is_the_timelines'),
    # the page
    ("Filtered keeps every connection", JS, 'if (filtering && !used(c)) { drop[c.line] = true; return; }',
     'if (false) { drop[c.line] = true; return; }', E2E, P + 'Filtered_follows_the_filters_and_says_what_it_leaves_out'),
    ("a component no shown scenario used stays", JS,
     '            if (filtering) model.components.forEach(function (line, i) { if (!ends[i]) drop[line] = true; });\n', '',
     E2E, P + 'Filtered_follows_the_filters_and_says_what_it_leaves_out'),
    ("only a scenario's own element is mapped", JS, "document.querySelectorAll('[data-stable-id]')",
     "document.querySelectorAll('details.scenario[data-stable-id]')", E2E, P + 'An_outlines_example_rows_answer_for_their_scenario'),
    ("Top to bottom keeps the direction", JS, "            if (state.dir === 'tb') drop[model.direction] = true;\n", '',
     E2E, P + 'Top_to_bottom_turns_the_diagram_and_a_form_drawn_before_comes_back_from_the_cache'),
    ("Hidden keeps the labels", JS, "                    line = bare[i] + (line.charAt(line.length - 1) === '\\r' ? '\\r' : '');\n", '',
     E2E, P + 'Hidden_call_summaries_draw_the_connections_without_their_labels'),
    ("a change of the filters is ignored", JS, '            settle = setTimeout(sync, 150);\n', '',
     E2E, P + 'Filtered_follows_the_filters_and_says_what_it_leaves_out'),
    ("a change made on another tab is never drawn", JS, '        if (window.IntersectionObserver) new IntersectionObserver(function (entries) {',
     '        if (false) new IntersectionObserver(function (entries) {', E2E,
     P + 'A_filter_changed_on_another_tab_reaches_the_diagram_when_its_tab_opens'),
    ("the first form is not handed to the renderer", JS,
     "            if (!el.dataset.queued) { el.setAttribute('data-plantuml', wanted); return; }",
     '            if (!el.dataset.queued) return;', E2E, P + 'A_links_filters_reach_the_first_drawing'),
    ("the drawing is never put away", JS, '            el.hidden = f.empty;', '            el.hidden = false;', E2E,
     P + 'When_no_scenario_the_filters_show_made_a_call_the_drawing_is_put_away_and_the_note_says_so'),
    ("the note never shows", JS, '            note.hidden = !f.filtering;', '            note.hidden = true;', E2E,
     P + 'Filtered_follows_the_filters_and_says_what_it_leaves_out'),
    ("one scenario is counted as scenarios", JS,
     "var who = f.shown === 1 ? 'the 1 scenario that matches the filters' : 'the ' + f.shown + ' scenarios that match the filters';",
     "var who = 'the ' + f.shown + ' scenarios that match the filters';", E2E,
     P + 'Filtered_follows_the_filters_and_says_what_it_leaves_out'),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/\\') + '/') if len(sys.argv) > 2 else ROOT
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        n = open(base + path, encoding='utf-8', newline='').read().count(old)
        if n != 1:
            bad += 1
            print(f'NOT ONCE ({n}): {name}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once')
    sys.exit(1 if bad else 0)

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]
killed = 0
for name, path, old, new, project, test in MUTATIONS:
    s = open(ROOT + path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
