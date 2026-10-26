// SEARCH_PLAN 6.4, S7: each mutation is applied to a shipped script, stylesheet or the generator, the project holding the
// facts it should break is built, those facts are run, and the file is put back byte for byte (its SHA-256 checked). A
// mutation counts only when its facts pass unmutated first.
//   node mutate-s7.js <worktree> [id ...]
// Writes the results as JSON on stdout, one line per mutation as it finishes on stderr; mutations.md records them.
const fs = require('fs'), path = require('path'), crypto = require('crypto'), { execSync } = require('child_process');

const root = path.resolve(process.argv[2] || '.');
const only = process.argv.slice(3);
const R = f => path.join(root, 'src/Kronikol/Reports', f);
const E2E = 'tests/Kronikol.Tests.EndToEnd', UNIT = 'tests/Kronikol.Tests', ENGINE = 'tests/Kronikol.Tests.SearchEngine';
const UI = R('report-search-ui-script.js'), RESULTS = R('report-search-results-script.js'), HIT = R('report-search-hit-script.js');
const NAV = R('report-navigation-script.js'), TABS = R('report-tabs-script.js'), BAR = R('report-filter-bar-script.js');
const KEYS = R('report-keyboard-navigation-function.js'), RSF = R('report-search-function.js'), AS = R('advanced-search.js');
const RG = R('ReportGenerator.cs'), CSS = R('stylesheets.css');
const t = name => 'FullyQualifiedName~' + name;

const MUTATIONS = [
    { id: 'B1', what: 'Enter runs the search twice, the second 300 ms later', file: UI, project: E2E,
      from: '            else runSearch(input.value);',
      to: '            else { runSearch(input.value); setTimeout(function () { run_search_scenarios(); }, 300); }',
      facts: t('SearchBuilderTests.Enter_runs_one_search') },
    { id: 'B2', what: 'no input listener: a query put in without a key waits', file: UI, project: E2E,
      from: "        input.addEventListener('input', function () { if (!composing) changed(); });", to: '',
      facts: t('SearchBuilderTests.A_query_put_in_without_a_keyup_is_read_without_leaving_the_field') },
    { id: 'QB16', what: "the builder does not stop its keys", file: UI, project: E2E,
      from: '        if (stop) e.stopPropagation();', to: '',
      facts: t('SearchBuilderTests.Keys_in_the_builder_never_reach_the_page') },
    { id: 'QB20', what: 'the draft is written to #searchbar as it is typed', file: UI, project: E2E,
      from: '    function changed() {\n        paintMirror();',
      to: '    function changed() {\n        field.value = input.value;\n        paintMirror();',
      facts: t('SearchBuilderTests.A_draft_never_reaches_the_address') },
    { id: 'B7', what: '/ prevents its default before it knows the search can open', file: KEYS, project: E2E,
      from: "        if (!window.kronikol_search || !window.kronikol_search.canOpen()) return;\n        e.preventDefault();",
      to: "        e.preventDefault();\n        if (!window.kronikol_search || !window.kronikol_search.canOpen()) return;",
      facts: t('SearchBuilderTests.Slash_is_left_to_the_browser_on_an_exported_page') + '|' + t('SearchBuilderTests.Slash_does_nothing_under_a_lightbox') },
    { id: 'F-a', what: 'the box is written without hidden', file: RG, project: E2E,
      from: 'aria-label="Filters" tabindex="-1" hidden>', to: 'aria-label="Filters" tabindex="-1">',
      facts: t('FilterBoxTests.The_box_is_hidden_when_the_page_opens') },
    { id: 'F-b', what: 'Filters is hidden while the bar is at rest', file: CSS, project: E2E,
      from: '        .nav-filters-toggle {\n            display: inline-flex;',
      to: '        .toolbar-row.report-nav:not(.k-floating) .nav-filters-toggle { display: none; }\n        .nav-filters-toggle {\n            display: inline-flex;',
      facts: t('FilterBoxTests.Filters_shows_at_rest_when_pinned_and_on_a_phone') },
    { id: 'F-c', what: 'the rows past the first folded away, as More filters left them (rows-folded is gone since S2)', file: CSS, project: E2E,
      from: '        .tag-filters .fltr-controls, .dependency-filters .fltr-controls { column-gap: 6px; }',
      to: '        .tag-filters .fltr-controls, .dependency-filters .fltr-controls { column-gap: 6px; }\n        .filtering-box .tag-filters, .filtering-box .happy-filters { display: none; }',
      facts: t('FilterBoxTests.Filters_opens_the_box_at_the_bar_s_top_with_every_row_showing') },
    { id: 'F-e', what: "Filters' count is read from the box's readout, not its controls", file: BAR, project: E2E,
      from: "        if ($('.percentile-btn.percentile-active', box) || (dur && dur.value)) n += 1;\n        return n;",
      to: "        if ($('.percentile-btn.percentile-active', box) || (dur && dur.value)) n += 1;\n        return /Filtered/.test(readout.textContent) ? 1 : 0;",
      facts: t('FilterBoxTests.Filters_counts_the_filters_on_and_says_so_in_its_name') },
    { id: 'F-g', what: 'the bar shuts the box as it comes to rest', file: BAR, project: E2E,
      from: "            nav.classList.toggle('k-floating', f);\n            capBox();",
      to: "            nav.classList.toggle('k-floating', f);\n            if (!f) setOpen(false);\n            capBox();",
      facts: t('FilterBoxTests.The_box_stays_open_when_the_bar_comes_to_rest') },
    { id: 'F-i', what: 'a pinned box is not capped', file: BAR, project: E2E,
      from: "            if (box.getBoundingClientRect().height > cap + 1) box.style.maxHeight = (cap - extra) + 'px';", to: '',
      facts: t('FilterBoxTests.A_pinned_box_takes_at_most_60_percent_of_the_window_and_scrolls_inside') },
    { id: 'F-k', what: "the live region is left in the box's readout", file: RG, project: E2E,
      from: '<span class="filter-readout" aria-hidden="true" title=', to: '<span class="filter-readout" role="status" title=',
      facts: t('FilterBoxTests.The_filters_are_announced_with_the_box_closed_open_and_the_bar_short') },
    { id: 'R1', what: 'the Search tab is left showing after Clear', file: RESULTS, project: E2E,
      from: '        tab.hidden = !q;', to: '        if (q) tab.hidden = false;',
      facts: t('SearchResultsTests.The_Search_tab_shows_while_a_query_is_applied_and_goes_with_it') },
    { id: 'R8', what: "Exclude sets its chip to include", file: RESULTS, project: E2E,
      from: "function () { set_chip_state(x.b, 'out'); filter_row(family); }", to: "function () { set_chip_state(x.b, 'in'); filter_row(family); }",
      facts: t('SearchResultsTests.Exclude_sets_the_chip_to_exclude') },
    { id: 'R13', what: 'Best match ignores names', file: RESULTS, project: E2E,
      from: 'var WEIGHT = { n: 8, t: 4, s: 2, e: 2, f: 1, d: 1, r: 1, p: 1, x: 1, g: 1, w: 1 };',
      to: 'var WEIGHT = { n: 1, t: 1, s: 1, e: 1, f: 1, d: 1, r: 1, p: 1, x: 1, g: 1, w: 1 };',
      facts: t('SearchResultsTests.Best_match_puts_a_scenario_named_after_the_word_first') },
    { id: 'R14', what: 'p= is not written', file: NAV, project: E2E,
      from: "SEARCH_KEYS.forEach(function (k) { if (keys[k]) parts.push(k + '=' + encodeURIComponent(keys[k])); });",
      to: "SEARCH_KEYS.forEach(function (k) { if (keys[k] && k !== 'p') parts.push(k + '=' + encodeURIComponent(keys[k])); });",
      facts: t('SearchResultsTests.Ten_results_a_page_and_each_page_writes_p') },
    { id: 'R22', what: 'the type counts come from the instant pass only (deep results are merged at once)', file: RESULTS, project: E2E,
      from: "            if (d.state === 'working') {\n                merged = false;", to: "            if (d.state === 'working') {",
      facts: t('DeepSearchTests.Deep_results_join') },
    { id: 'R23', what: "the tabs script's arrows do not pass over the Search tab", file: TABS, project: E2E,
      from: "if (!selected() || selected() === 'features' || selected() === 'search') return;", to: "if (!selected() || selected() === 'features') return;",
      facts: t('SearchResultsTests.Down_and_Up_on_the_Search_tab_scroll_and_stay_on_it') },
    { id: 'R26', what: 'the focus is not moved to the count', file: RESULTS, project: E2E,
      from: '    function focusCount() { if (count) count.focus({ preventScroll: true }); }', to: '    function focusCount() { }',
      facts: t('SearchResultsTests.The_focus_goes_to_the_count_after_a_search_a_type_and_a_page_and_stays_on_sort_after_a_sort') },
    { id: 'B5', what: 'the empty path does not unmark the rows the search marked', file: RSF, project: E2E,
      from: "function clear_search_rows() {\n    document.querySelectorAll('tr.row-search-match').forEach(function(r) { r.classList.remove('row-search-match'); });",
      to: 'function clear_search_rows() {',
      facts: t('SearchResultsTests.Emptying_the_search_unmarks_its_outline_rows') },
    { id: 'H2', what: "the way does not open the composite steps on its way (opening one draws its held sub-steps)", file: HIT, project: E2E,
      from: "    function drawn(details) {\n        var holder = details.querySelector(':scope > .sub-steps');",
      to: "    function drawn(details) {\n        return Promise.resolve();\n        var holder = details.querySelector(':scope > .sub-steps');",
      facts: t('SearchHitWayTests.A_sub_step_line_opens_each_composite_step_and_draws_held_sub_steps_first') },
    { id: 'H19-H20', what: 'hit= is not read as the match (it falls among the filters)', file: NAV, project: E2E,
      from: "            else if (k === 'hit') hit = decode(s.substring(4));", to: '',
      facts: t('BackForwardSearchTests') },
    { id: 'W2', what: 'the kinds offsets one off', file: RG, project: UNIT,
      from: 'runs.Append(kind).Append(starts[from].ToString(CultureInfo.InvariantCulture));',
      to: 'runs.Append(kind).Append((starts[from] + 1).ToString(CultureInfo.InvariantCulture));',
      facts: t('SearchKindsTests') },
    { id: 'E3', what: "a scenario skipped after a failure is not skipped for $skipped (5.9.1's rule, which B4 follows)", file: AS, project: ENGINE,
      from: "if (status.toLowerCase() === ast.value || (ast.value === 'skipped' && status === 'SkippedAfterFailure')) return true;",
      to: 'if (status.toLowerCase() === ast.value) return true;',
      facts: t('SearchQueryFormsTests.Status_skipped_finds_a_scenario_skipped_after_a_failure') },
    // S5, the way to a diagram's match and the deep search's lines (2026-10-26, written after the agents' merges)
    { id: 'W7', what: 'no deep lines asked for the results shown', file: RESULTS, project: E2E,
      from: '        if (!features) askDeepLines(onPage);', to: '',
      facts: t('SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words') },
    { id: 'R-cut', what: 'a line with no spaces is widened to its whole length', file: RESULTS, project: E2E,
      from: "        if (typeof kronLineAround === 'function') {", to: '        if (false) {',
      facts: t('SearchHitWayTests.A_line_with_no_spaces_is_cut_to_160_around_the_match') },
    { id: 'H7', what: 'hiddenBy finds nothing a toggle hides', file: R('collapsible-notes-script.js'), project: E2E,
      from: '            if (gone) out.push(f[0]);', to: '',
      facts: t('SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only') + '|' + t('SearchHitTests.Each_toggle_that_hides_a_match_is_turned_on') },
    { id: 'H6', what: 'a note without the words is left as it is', file: HIT, project: E2E,
      from: "            if (!p || what !== 'note' || !api || !api.openNote || holds(p)) return p;", to: '            return p;',
      facts: t('SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words') },
    { id: 'H8h', what: 'a header line never turns Headers on', file: HIT, project: E2E,
      from: "                if (/^\\s*<color:#[0-9a-f]+>\\s*\\[/i.test(sourceLines(host, bits[0])[row] || '')) {", to: '                if (false) {',
      facts: t('SearchHitTests.Each_toggle_that_hides_a_match_is_turned_on') },
    { id: 'H10', what: 'the note is never said to be shown as JSON', file: HIT, project: E2E,
      from: "                    if (ok && holds(j)) told.push('the note shown as JSON');", to: '',
      facts: t('SearchHitTests.A_JSON_phrase_shows_its_note_as_JSON_and_a_word_leaves_it_in_YAML') },
    { id: 'H14', what: 'a picture with no text is not taken to its Raw PlantUML', file: HIT, project: E2E,
      from: "        if ($$('.raw-plantuml pre', scenario).length && (bits[1] === 'raw'", to: "        if (false && (bits[1] === 'raw'",
      facts: t('SearchHitTests.A_server_drawn_diagram_s_line_opens_Raw_PlantUML_and_marks_the_words') },
    { id: 'H13', what: 'the flow does not choose its view', file: HIT, project: E2E,
      from: '            chooseView(flame);', to: '',
      facts: t('SearchHitTests.A_flow_line_opens_the_Flame_view_at_its_bar') },
    { id: 'H33', what: 'a diagram in a hidden view is not shown', file: HIT, project: E2E,
      from: '        chooseView(host);', to: '',
      facts: t('SearchHitTests.An_Activity_view_line_opens_the_view_and_marks_its_text') },
    { id: 'H29', what: "an outline row's diagram is left unchosen", file: HIT, project: E2E,
      from: '        chooseRow(scenario, host);', to: '',
      facts: t('SearchHitTests.A_payload_line_from_an_outline_row_chooses_that_row_and_its_diagram') },
    { id: 'H21', what: 'words the diagram does not draw are not said', file: HIT, project: E2E,
      from: "                if ((kind === 'g' || kind === 'w') && !(marked && marked.count > 0)) told.missing = true;", to: '',
      facts: t('SearchHitTests.A_match_the_diagram_does_not_draw_goes_to_the_nearest_place_and_says_why') },
    { id: 'H22', what: "the reader's keys and scrolls do not stop the way", file: HIT, project: E2E,
      from: '            window.addEventListener(t, function (e) { if (e.isTrusted) stop(); }, { capture: true, passive: true });', to: '',
      facts: t('SearchHitTests.A_scroll_or_a_key_from_the_reader_stops_the_way_there') },
    { id: 'H35', what: 'an arrival (Back) does not stop the way', file: NAV, project: E2E,
      from: '        if (window.kronikol_hit && window.kronikol_hit.stop) window.kronikol_hit.stop();', to: '',
      facts: t('SearchHitTests.Back_while_the_way_there_runs_stops_it') },
    { id: 'H23', what: 'words under 10 px are not zoomed', file: HIT, project: E2E,
      from: '        readable(el, marked, me, 4);', to: '',
      facts: t('SearchHitTests.Words_drawn_under_10_px_are_zoomed_to_be_read') },
    { id: 'H17', what: 'nothing is said under the view tabs', file: HIT, project: E2E,
      from: '            if (told.section && (told.length || told.missing)) underTabs(told.section, told);', to: '',
      facts: t('SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only') + '|' + t('SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words') },
    { id: 'H31', what: 'a declaration is taken for a message not drawn', file: HIT, project: E2E,
      from: '        if (label === null || !api.findMessage) return host;', to: '        if (label === null || !api.findMessage) return null;',
      facts: t('SearchHitTests.A_participant_or_loop_label_line_marks_it_in_the_diagram') },
    { id: 'H-name', what: 'the place is not named after its call', file: HIT, project: E2E,
      from: '        if (row >= 0) told.where = placeName(sourceLines(host, bits[0]), row, what);', to: '',
      facts: t('SearchHitTests.A_body_line_draws_the_diagram_expands_the_note_and_marks_the_words') + '|' + t('SearchHitTests.A_database_call_line_turns_Databases_on_for_that_scenario_only') },
    { id: 'M-bar', what: "a click on the bar's own controls takes the marks off", file: R('report-search-marks-script.js'), project: E2E,
      from: '        if (!rec || !t || t === doc.documentElement || layerOpen() || inBar(t) || (rec.scope && rec.scope.contains(t))) return;',
      to: '        if (!rec || !t || t === doc.documentElement || layerOpen() || (rec.scope && rec.scope.contains(t))) return;',
      facts: t('SearchHitWayTests.Escape_in_the_filter_box_closes_it_and_leaves_the_marks') },
    { id: 'M-hide', what: 'a filter that hides the scenario leaves the marks', file: R('report-search-marks-script.js'), project: E2E,
      from: '        if (active && (appliedQuery() !== active.query || !rendered(active.scope))) clear();',
      to: '        if (active && appliedQuery() !== active.query) clear();',
      facts: t('SearchHitWayTests.A_filter_that_hides_the_marked_scenario_takes_the_marks_off') },
    // found by the wiki pass (2026-10-26): the tips' status row, Clear All's title, and `not` in the lines' marks
    { id: 'TIPS', what: 'the tips count a scenario skipped after a failure as failed', file: RG, project: UNIT,
      from: 'a scenario skipped after a failure counts as skipped', to: 'a scenario skipped after a failure counts as failed',
      facts: t('SearchButtonMarkupTests.The_syntax_tips_count_a_scenario_skipped_after_a_failure_as_skipped') },
    { id: 'CA', what: "Clear All's title leaves out the new qualifiers", file: RG, project: UNIT,
      from: '"Clear every filter: the search with its tag:, status:, feature:, dep:, duration: and is: terms, and the "',
      to: '"Clear every filter: the search with its dep:, duration: and is: terms, and the "',
      facts: t('ControlTitlesTests') + '|' + t('FilterBarTests') + '|' + t('FilterRowsTests') },
    { id: 'NOT', what: '`not` before a word negates it in the lines', file: RESULTS, project: E2E,
      from: "                if (w === '!!') { negate = true; continue; }", to: "                if (w === '!!' || w === 'not') { negate = true; continue; }",
      facts: t('SearchHitWayTests.Not_before_a_word_is_a_word_the_lines_mark') },
    // found by checking the wiki against the code (2026-10-26)
    { id: 'X-line', what: "the export keeps the line saying what was turned on", file: R('report-export-function.js'), project: E2E,
      from: "    copy.querySelectorAll('.search-turned-on').forEach(function (el) { el.parentNode.removeChild(el); });", to: '',
      facts: t('SearchHitTests.An_export_taken_after_a_match_leaves_out_the_line_of_what_was_turned_on') },
    { id: 'DESC', what: "a DESCRIPTION line read from every scenario of the feature", file: RESULTS, project: E2E,
      from: "        else if (kind === 'd') cands = $$('.scenario-description', i.el).concat($$('.feature-description', i.f))",
      to: "        else if (kind === 'd') cands = $$('.scenario-description', i.f).concat($$('.feature-description', i.f))",
      facts: t('SearchHitWayTests.A_description_line_is_the_result_s_own_description') },
    { id: 'ENDP', what: 'an ENDPOINT line taken from the lower-cased search text', file: RESULTS, project: E2E,
      from: "        else if (kind === 'p') cands = $$(':scope > summary .endpoint', i.f)",
      to: "        else if (kind === 'p') cands = $$(':scope > summary .no-endpoint', i.f)",
      facts: t('SearchHitWayTests.An_endpoint_line_is_the_endpoint_as_written') },
    { id: 'G-cut', what: 'an instant diagram line is its whole part of the search text', file: RESULTS, project: E2E,
      from: '                    var text = kronLineAround(part, a, w.length, 160).text.trim();', to: '                    var text = part.trim();',
      facts: t('SearchHitTests.An_instant_diagram_line_is_cut_to_160_around_the_match') },
    { id: 'OWN', what: "a hit= naming another scenario's diagram is followed there", file: HIT, project: E2E,
      from: '        return el && scenario.contains(el) ? el : null;', to: '        return el;',
      facts: t('SearchHitTests.A_hit_link_naming_another_scenario_s_diagram_opens_the_scenario_and_says_so') },
    { id: 'W-quoted', what: "the warning splits the unreadable qualifiers on spaces", file: RESULTS, project: E2E,
      from: '        var items = f.kronQBad && f.kronQBad.length ? f.kronQBad : ', to: '        var items = false ? f.kronQBad : ',
      facts: t('SearchResultsTests.An_unreadable_qualifier_with_a_quoted_space_is_warned_about_whole') },
    // found by H2's mutation in run 2, which froze the page: a held step that never draws was drawn again and again
    { id: 'TRIED', what: 'a held step that never draws is drawn again, for as long as the page is open', file: HIT, project: E2E,
      from: "                return tried.indexOf(d) < 0 && d.querySelector(':scope > .sub-steps[data-steps]');",
      to: "                return d.querySelector(':scope > .sub-steps[data-steps]');",
      facts: t('SearchHitWayTests.A_held_step_that_never_draws_is_tried_once') },
    { id: 'W-group', what: "a negated group's qualifier is given its value's reason", file: RESULTS, project: E2E,
      from: '            var why = b.inGroup ? IN_GROUP : WHY[key.toLowerCase()];', to: '            var why = WHY[key.toLowerCase()];',
      facts: t('SearchResultsTests.A_qualifier_in_a_negated_group_is_warned_about_for_its_place') },
];

const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const built = new Set();
// A run that ends with no summary can leave its test host running, holding the worktree's assemblies, so the next build
// fails: the processes started from this worktree's test projects are stopped, with their children, after each run
// (never a browser or a process of another checkout).
function stopLeftovers() {
    const under = path.join(root, 'tests').replace(/'/g, "''");
    try {
        execSync(`powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith('${under}', 'OrdinalIgnoreCase') } | ForEach-Object { taskkill /T /F /PID $_.ProcessId | Out-Null }"`,
            { stdio: 'pipe' });
    } catch (e) { void e; }
}
function run(project, filter, rebuild) {
    if (rebuild || !built.has(project)) {
        try { execSync(`dotnet build ${project} -nologo -v q`, { cwd: root, stdio: 'pipe' }); }
        catch (e) { return { failed: -1, passed: -1, output: 'the build failed: ' + ((e.stdout || '').toString().match(/[^\n]*error [A-Z]+\d+[^\n]*/g) || []).slice(0, 3).join(' | ') }; }
        built.add(project);
    }
    let out;
    try { out = execSync(`dotnet test ${project} --no-build -nologo -v q --filter "${filter}"`, { cwd: root, stdio: 'pipe', timeout: 1800000 }).toString(); }
    catch (e) { out = (e.stdout || '').toString() + (e.stderr || '').toString(); }
    stopLeftovers();
    const m = /Failed:\s+(\d+),\s+Passed:\s+(\d+)/.exec(out);
    return m ? { failed: +m[1], passed: +m[2] } : { failed: -1, passed: -1, output: out.slice(-800) };
}

if (only[0] === '--dry') {
    // each mutation's text, counted in its file: a mutation runs only where it occurs once
    for (const mu of MUTATIONS) {
        const text = fs.readFileSync(mu.file, 'utf8'), nl = text.includes('\r\n') ? '\r\n' : '\n';
        console.log(mu.id.padEnd(8), text.split(mu.from.split('\n').join(nl)).length - 1);
    }
    process.exit(0);
}
const results = [];
for (const mu of MUTATIONS.filter(m => !only.length || only.includes(m.id))) {
    const original = fs.readFileSync(mu.file);
    const text = original.toString('utf8');
    const nl = text.includes('\r\n') ? '\r\n' : '\n';
    const from = mu.from.split('\n').join(nl), to = mu.to.split('\n').join(nl);
    const count = text.split(from).length - 1;
    if (count !== 1) { results.push({ id: mu.id, error: `the mutation's text occurs ${count} times` }); console.error(mu.id, 'NOT FOUND', count); continue; }
    const unmutated = run(mu.project, mu.facts, true);
    let mutated = null;
    try {
        fs.writeFileSync(mu.file, text.replace(from, to));
        mutated = run(mu.project, mu.facts, true);
    } finally {
        fs.writeFileSync(mu.file, original);
    }
    const restored = sha(fs.readFileSync(mu.file)) === sha(original);
    const r = { id: mu.id, what: mu.what, file: path.basename(mu.file), facts: mu.facts, unmutated, mutated, restored,
        killed: unmutated.failed === 0 && unmutated.passed > 0 && mutated.failed > 0 };
    results.push(r);
    console.error(`${mu.id}: unmutated ${unmutated.passed}/${unmutated.failed} mutated ${mutated.passed}/${mutated.failed} ${r.killed ? 'KILLED' : 'SURVIVED'} restored=${restored}`);
    // each result as it is known, so a run that stops part way keeps what it found
    console.error('RESULT ' + JSON.stringify(r));
}
// the projects built again from the files as they are
for (const p of new Set(MUTATIONS.map(m => m.project))) execSync(`dotnet build ${p} -nologo -v q`, { cwd: root, stdio: 'pipe' });
console.log(JSON.stringify(results, null, 2));
