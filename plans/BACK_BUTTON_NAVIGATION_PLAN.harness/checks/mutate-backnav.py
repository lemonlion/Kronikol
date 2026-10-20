# The plan's mutations (section 7.4): each undoes one behaviour in the source, rebuilds, runs the Playwright facts that
# should catch it, expects them to FAIL, and puts the source back from the copy it read (never git checkout, which
# would also undo uncommitted work). A mutation counts only when its facts pass unmutated: the baseline runs them first.
# usage: python -I mutate-backnav.py <worktree root> [name ...]   (minutes per mutation: each one rebuilds Kronikol)
import subprocess, sys

ROOT = sys.argv[1].rstrip('/') + '/'
ONLY = set(sys.argv[2:])
R = ROOT + 'src/Kronikol/Reports/'
NAV = R + 'report-navigation-script.js'
HASH = R + 'report-url-hash-function.js'
SEARCH = R + 'report-search-function.js'
E = 'Kronikol.Tests.EndToEnd.'
P, F, L, RS = E + 'BackForwardPlacesTests.', E + 'BackForwardFilterTests.', E + 'BackForwardLayerTests.', E + 'BackForwardRestoreTests.'

MUTATIONS = [
    ('a tab click replaces the entry instead of adding one', NAV,
     "        if (same || (how.key && live && current.key === how.key)) {",
     "        if (true) {",
     [P + 'P2_Back_and_Forward_walk_through_the_tabs_a_reader_opened']),
    ('the position is put back by its pixels only', NAV,
     "        var el = saved.at ? find(saved.at) : null;",
     "        var el = null;",
     [RS + 'R16_Back_lands_on_the_same_scenario_after_the_features_above_it_were_drawn']),
    ('the focus is not put back', NAV,
     "            if (!going && saved && saved.focus) { var f = selectedOf(find(saved.focus)); if (shows(f)) target = f; }",
     "",
     [P + 'P1_Back_from_the_scenario_a_History_link_opened_shows_the_History_tab_where_the_reader_was',
      P + 'P6_Back_from_a_failure_clusters_scenario_returns_to_the_link_where_it_was']),
    ("scroll restoration is left 'auto'", NAV,
     "        try { if (sh.scrollRestoration !== 'manual') sh.scrollRestoration = 'manual'; } catch (e) { }\n        if (typeof window.kronikol_measure_bar",
     "        if (typeof window.kronikol_measure_bar",
     [RS + 'R1_Back_lands_on_the_same_feature_far_down_a_long_report', P + 'P1_Back_from_the_scenario_a_History_link_opened_shows_the_History_tab_where_the_reader_was']),
    ('a run of one control is not one entry', NAV,
     "        if (live && current.key === key) { replaceSoon(hash); return; }",
     "",
     [F + 'F2_A_word_typed_in_the_search_is_one_entry', F + 'F3_A_chip_cycled_through_its_states_is_one_entry_and_another_chip_a_second']),
    ('a change of another control joins the run', NAV,
     "        if (live && current.key === key) { replaceSoon(hash); return; }",
     "        if (live) { replaceSoon(hash); return; }",
     [F + 'F3_A_chip_cycled_through_its_states_is_one_entry_and_another_chip_a_second']),
    ('an address adds its filters to the page\'s', HASH,
     "        step(reset_filters);\n",
     "",
     [F + 'F5_Back_to_an_entry_without_a_filter_takes_it_off_rather_than_adding_to_it']),
    ('a link to a hidden scenario says nothing', HASH,
     "    if (hidden) return keepScroll ? null : filtered_out_note(hidden, anchor);",
     "",
     [F + 'F7_A_link_to_a_scenario_the_filters_hide_says_so_and_its_button_clears_them_and_shows_it']),
    ('a layer closed by its own control leaves its entry', NAV,
     "            try { sh.back(); } catch (e) { return false; }\n            return true;",
     "            return false;",
     [L + 'L2_A_lightbox_closed_by_Esc_or_its_backdrop_leaves_no_entry_behind']),
    ('a click with a modifier key is taken', NAV,
     "        if (e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;",
     "        if (e.button !== 0) return;",
     [P + 'P12_Ctrl_or_Shift_click_on_a_History_link_or_a_cluster_link_opens_the_scenario_in_a_new_page']),
    ('the tab is put on after the scenario is revealed', NAV,
     "        if (to.tab && to.tab !== selectedTab() && typeof window.kronikol_activate_tab === 'function') window.kronikol_activate_tab(to.tab);",
     "        if (o.how !== 'load' && to.tab && to.tab !== selectedTab() && typeof window.kronikol_activate_tab === 'function') window.kronikol_activate_tab(to.tab);",
     [RS + 'R7_With_the_Timeline_as_the_start_tab_a_sid_address_shows_its_scenario_clear_of_the_bar', P + 'P10_An_address_that_names_a_tab_opens_it']),
    ('a relative address is written (S21)', NAV,
     "    function hrefOf(hash) { return location.href.replace(/#.*$/, '') + (hash ? '#' + hash : ''); }",
     "    function hrefOf(hash) { return location.pathname + location.search + (hash ? '#' + hash : ''); }",
     [RS + 'R11_In_an_iframe_srcdoc_the_address_is_written_the_search_opens_its_match_and_Back_undoes_a_filter']),
    ('the write budget is gone', NAV,
     "    var WINDOW = 10000, BUDGET = 80, GAP = 120;",
     "    var WINDOW = 10000, BUDGET = 1000000, GAP = 0;",
     [RS + 'R13_In_WebKit_a_hundred_and_fifty_clicks_on_one_chip_and_a_held_arrow_key_throw_nothing']),
    ("a write's exception is left uncaught", NAV,
     "        try {\n            if (push) sh.pushState(wrapped(entry, false), '', hrefOf(hash));\n            else sh.replaceState(wrapped(entry, true), '', hrefOf(hash));\n        } catch (e) { return false; }",
     "        if (push) sh.pushState(wrapped(entry, false), '', hrefOf(hash));\n        else sh.replaceState(wrapped(entry, true), '', hrefOf(hash));",
     [RS + 'R11_In_an_iframe_srcdoc_the_address_is_written_the_search_opens_its_match_and_Back_undoes_a_filter']),
    # found by the green runs (2026-10-20), each with the fact that caught it
    ('an address with no filters leaves the hidden scenarios hidden', HASH,
     "        step(function() { applyVisibility(fc()); });\n",
     "",
     [F + 'F2_A_word_typed_in_the_search_is_one_entry']),
    ('a bare word in the address is read as a filter', NAV,
     "            else if (s.indexOf('=') > 0) filters.push(s);",
     "            else filters.push(s);",
     [L + 'L6_A_bare_fragment_leaves_the_place_and_an_open_layer_as_they_are']),
    ('the entry takes the position only as the page goes away', NAV,
     "        scrollTimer = setTimeout(function () { scrollTimer = 0; noted = null; note(); keepSoon(); }, 150);",
     "        scrollTimer = setTimeout(function () { scrollTimer = 0; noted = null; note(); }, 150);",
     [P + 'P9_A_reload_opens_the_same_tab_view_scenario_and_filters_scrolled_where_the_reader_was']),
    ('the position is read at one point only', NAV,
     "        var w = window.innerWidth, h = window.innerHeight, xs = [w / 2, w / 4, w * 3 / 4];\n        for (var py = Math.min(top + 2, Math.max(0, h - 1)); py < h; py += 32) {",
     "        var w = window.innerWidth, h = window.innerHeight, xs = [w / 2];\n        for (var py = Math.min(top + 2, Math.max(0, h - 1)); py < h; py = h) {",
     [P + 'P9_A_reload_opens_the_same_tab_view_scenario_and_filters_scrolled_where_the_reader_was']),
    ("the note in a closed feature is out of sight", HASH,
     "    for (var p = note.parentElement; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.setAttribute('open', '');\n",
     "",
     [F + 'F7_A_link_to_a_scenario_the_filters_hide_says_so_and_its_button_clears_them_and_shows_it']),
    ("a search's one match opens in a closed feature", SEARCH,
     "    [el.closest('details.feature'), el.closest('details.rule'), el].forEach(function(d) {",
     "    [el.closest('details.rule'), el].forEach(function(d) {",
     [F + 'F10_A_search_with_one_match_shows_it_in_its_feature_and_emptying_the_search_closes_what_it_opened']),
    ('emptying the search closes every scenario', SEARCH,
     "    _openedBySearch.forEach(function(d) { d.removeAttribute('open'); });",
     "    document.querySelectorAll('details.scenario[open]').forEach(function(d) { d.removeAttribute('open'); });",
     [F + 'F8_Emptying_the_search_leaves_open_a_scenario_the_reader_had_opened']),
    ('emptying the search leaves open what it opened', SEARCH,
     "    _openedBySearch.forEach(function(d) { d.removeAttribute('open'); });",
     "",
     [F + 'F10_A_search_with_one_match_shows_it_in_its_feature_and_emptying_the_search_closes_what_it_opened']),
]

def run(tests):
    flt = '|'.join('FullyQualifiedName~' + t for t in tests)
    r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                        '--filter', flt], capture_output=True, text=True)
    out = r.stdout + r.stderr
    built = 'error CS' not in out and 'Build FAILED' not in out
    return r.returncode == 0, built, out

chosen = [m for m in MUTATIONS if not ONLY or m[0] in ONLY]
targets = sorted({t for m in chosen for t in m[4]})
ok, built, out = run(targets)
print(('BASELINE passes' if ok else 'BASELINE FAILS') + f' ({len(targets)} facts)')
if not ok:
    print(out[-4000:])
    sys.exit(2)

killed = 0
for name, path, old, new, tests in chosen:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        ok, built, out = run(tests)
        dead = not ok and built and 'Failed!' in out
        killed += dead
        print(('KILLED  ' if dead else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(chosen)} killed')
sys.exit(0 if killed == len(chosen) else 1)
