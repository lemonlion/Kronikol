# Mutations of TOGGLE_JUMP_PLAN.md section 6.3 (and a few more), each applied to the fix in C:/Code/Kronikol-227, built,
# and run against the facts that should catch it. Usage: python -u mutate227.py [name ...]
# Every file is backed up before it is changed and written back from the backup after, never with git checkout.
import os, shutil, subprocess, sys, re, time

WT = 'C:/Code/Kronikol-227'
CM = WT + '/src/Kronikol/Reports/context-menu-script.js'
CN = WT + '/src/Kronikol/Reports/collapsible-notes-script.js'
COMP = WT + '/src/Kronikol/Reports/report-components-script.js'
OUT = 'C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/6a05fcc2-08b1-4c23-8f2f-1be8814e2bd4/scratchpad/mutations'
CLS = 'Kronikol.Tests.EndToEnd.ReadersPlaceOnRedrawTests'
WHEEL = ("    window.addEventListener('wheel', function(e) {\n"
         "        if (readingPlace && (e.ctrlKey || e.metaKey || wheelScrollsWindow(e))) letGoOfReadingPlace();\n"
         "    }, { capture: true, passive: true });\n")
CHECK = ("        var dy = readingOffset(readingPlace);\n"
         "        if (dy === null || Math.abs(dy) > 2) letGoOfReadingPlace();\n"
         "        else readingKeptY = window.scrollY;\n")
TAKE = "        checkReadingPlace();\n        if (readingPlace && readingOffset(readingPlace) !== null) return;\n"
UNIQUE = 'unique: above.slice(0, 3).concat(below.slice(0, 3)).sort(nearer) };'
AGREE = 'if (near >= 0 && Math.abs(sameY[near] - want) <= Math.max(3, 0.02 * asked[k].far))'
ANCHOR = '        if (asked.length) return { el: asked[0].el, off: asked[0].off };\n'
OTHER = '(k === 0 || asked[k].far <= 2 * asked[0].far + 20)'
BOUND = 'Math.abs(s.height - place.h0) + 30'

MUTATIONS = {
    'keep-skipped': ([(CM, 'function keepReadingPlace() {\n        var place = readingPlace;',
                       'function keepReadingPlace() {\n        return;\n        var place = readingPlace;')],
                     ['Headers_with_the_window_edge_on_the_picture_above_keeps_the_text_read_in_place',
                      'Headers_with_the_window_edge_on_the_picture_above_keeps_a_zoomed_diagrams_text_in_place',
                      'Headers_with_zoomed_diagrams_far_above_keeps_the_diagram_read_on_screen']),
    'take-after-first-swap': ([(CN, "        // the reader's place, kept across what the control draws again (context-menu-script.js, #227)\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();\n", ''),
                               (CN, 'function keepReadingPlace() {\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.keep();',
                                'function keepReadingPlace() {\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.keep();')],
                              ['Headers_with_the_window_edge_on_the_picture_above_keeps_the_text_read_in_place']),
    'fraction-only': ([(CM, '        if (!place.mark) return dy;\n', '        return dy;\n')],
                      ['Steps_keeps_the_place_read_in_a_diagram_zoomed_out']),
    'text-without-bound': ([(CM, 'if (mk !== null && Math.abs(mk - place.y - mark.off - dy) <= ' + BOUND + ') return',
                             'if (mk !== null) return')],
                           []),
    'unique-bounded': ([(CM, AGREE, AGREE[:-1] + ' && Math.abs(sameY[near] - place.y - mark.off - dy) <= ' + BOUND + ')'),
                        (CM, ANCHOR, '        if (asked.length && Math.abs(asked[0].y - place.y - asked[0].off - dy) <= ' + BOUND + ') return { el: asked[0].el, off: asked[0].off };\n')],
                       ['Direction_keeps_the_component_read_in_place']),
    'other-side-unbounded': ([(CM, OTHER, 'true')],
                             ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'wheel-dropped': ([(CM, WHEEL, '')],
                      ['The_readers_wheel_while_a_toggle_draws_lets_the_place_go']),
    'wheel-and-scroll-dropped': ([(CM, WHEEL, ''), (CM, CHECK, '')],
                                 ['The_readers_wheel_while_a_toggle_draws_lets_the_place_go']),
    'check-dropped': ([(CM, '        if (!readingPlace || Math.abs(window.scrollY - readingKeptY) <= 2) return;\n', '        return;\n')],
                      ['Kronikol.Tests.EndToEnd.ReportToggleResponsivenessTests.A_toggle_draws_the_diagram_on_screen_first_and_one_scrolled_to_next_and_a_far_one_gets_its_buttons_as_it_comes_near']),
    'p5-skipped': ([(CM, '            if (state.selected) selectDiagram(box);\n            sizeGivenPicture(box, state);', '            if (state.selected) selectDiagram(box);')],
                   ['A_notes_own_fold_on_a_zoomed_diagram_paints_no_frame_off_its_place',
                    'Headers_keeps_the_place_read_in_a_zoomed_diagram',
                    'Headers_with_zoomed_diagrams_far_above_keeps_the_diagram_read_on_screen']),
    'p5-width-after-swap': ([(CM, 'var f = factsAt([svg], [w.value], false, state.width);', 'var f = factsAt([svg], [w.value], false, contentWidthOf(box));')],
                            ['A_notes_own_fold_on_a_zoomed_diagram_paints_no_frame_off_its_place',
                             'Headers_on_a_short_page_leaves_a_zoomed_diagram_where_it_was']),
    'end-not-waiting': ([(CM, '            if (redrawsRunning > 0) redrawsRunning--;', '            redrawsRunning = 0;')],
                        ['A_second_toggle_while_the_first_draws_keeps_the_place_read_before_the_first']),
    'parts-keep-dropped': ([(CN, "                            container.style.minHeight = '';\n                            keepReadingPlace();\n", "                            container.style.minHeight = '';\n")],
                           ['Headers_keeps_the_place_read_in_a_zoomed_diagram_in_parts',
                            'Headers_with_the_window_edge_on_a_diagram_in_parts_above_keeps_the_text_read_in_place']),
    'scenario-take-dropped': ([(CN, '    function showFromControl(scenario, states) {\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();',
                                '    function showFromControl(scenario, states) {')],
                              ['A_scenarios_own_headers_keeps_the_place_read_in_its_zoomed_diagram']),
    'take-in-show': ([(CN, '    function show(scenario, states) {\n        states = states || {};',
                       '    function show(scenario, states) {\n        if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();\n        states = states || {};')],
                     ['The_diagram_apis_show_takes_no_place_for_the_search_to_keep']),
    'unique-ignored': ([(CM, UNIQUE, 'unique: [] };')],
                       ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'unique-nearest-only': ([(CM, UNIQUE, 'unique: above.concat(below).sort(nearer).slice(0, 1) };')],
                            ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'unique-either-side': ([(CM, UNIQUE, 'unique: above.concat(below).sort(nearer).slice(0, 3) };')],
                           ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'first-anchor-only': ([(CM, OTHER, 'k === 0')],
                          ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'agreement-snaps-40': ([(CM, AGREE, 'if (near >= 0 && Math.abs(sameY[near] - want) <= 40)')],
                           ['A_filter_that_takes_rows_away_keeps_the_place_read_among_notes_of_the_same_words']),
    'components-direction-take-dropped': ([(COMP, '            if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();\n', '')],
                                          ['Direction_keeps_the_component_read_in_place']),
    'components-take-dropped': ([(COMP, '            if (window.kronikol_readingPlace) window.kronikol_readingPlace.take();\n', '')],
                                ['Call_summaries_keeps_the_place_read_in_a_zoomed_component_diagram']),
    # the review's faults, each put back
    'r1-plain-object': ([(CM, 'drawn = Object.create(null);', 'drawn = {};')],
                        ['Headers_keeps_the_place_read_in_a_diagram_that_draws_the_words_every_object_has']),
    'r2-hidden-read': ([(CM, 'place.el.isConnected && place.el.getClientRects().length ?', 'place.el.isConnected ?')],
                       ['A_place_taken_out_of_the_layout_while_a_toggle_draws_scrolls_the_page_no_more']),
    'r3-take-trusts-held': ([(CM, TAKE, '        if (readingPlace) return;\n')],
                            ['A_second_toggle_after_the_page_jumped_keeps_the_place_read_at_the_second']),
    'r4-out-width-remembered-only': ([(CM, 'width: box._kContentWidth || contentWidthOf(box)', 'width: box._kContentWidth || 0')],
                                     ['Steps_keeps_the_place_read_in_a_diagram_zoomed_out_after_the_window_changed_width']),
    'r6-wheel-any': ([(CM, WHEEL, "    window.addEventListener('wheel', letGoOfReadingPlace, { capture: true, passive: true });\n")],
                     ['A_wheel_across_a_zoomed_diagram_while_a_toggle_draws_keeps_the_place_read']),
    'r6-touchstart': ([(CM, "window.addEventListener('touchmove', letGoOfReadingPlace", "window.addEventListener('touchstart', letGoOfReadingPlace")],
                      ['A_tap_while_a_toggle_draws_keeps_the_place_read']),
    'r7-smooth': ([(CM, "            window.scrollBy({ top: dy, behavior: 'instant' });\n            // what the window could not scroll",
                    "            window.scrollBy(0, dy);\n            // what the window could not scroll")],
                  ['Headers_keeps_the_text_read_in_place_on_a_page_that_scrolls_smoothly']),
    'r9-check-scroll-only': ([(CM, CHECK, '        letGoOfReadingPlace();\n')],
                             ['A_scroll_that_leaves_the_place_on_its_line_lets_nothing_go']),
    'r10-components-no-check': ([(COMP, '                    if (place) place.check();\n                    if (window._addZoomButton)',
                                  '                    if (window._addZoomButton)')],
                                ['A_scroll_between_the_component_diagrams_picture_and_its_slider_stands']),
}


def run(cmd, log):
    with open(log, 'a', encoding='utf-8') as f:
        f.write('\n$ ' + ' '.join(cmd) + '\n')
        f.flush()
        return subprocess.call(cmd, stdout=f, stderr=subprocess.STDOUT, cwd=WT)


def main(names):
    os.makedirs(OUT, exist_ok=True)
    files = [CM, CN, COMP]
    backup = {p: open(p, 'rb').read() for p in files}
    try:
        # every pattern is checked before anything is built, so a stale one stops the run at once, not half way
        for name in names:
            for path, old, new in MUTATIONS[name][0]:
                assert backup[path].count(old.encode()) == 1, (name, path, old[:60])
        for name in names:
            edits, facts = MUTATIONS[name]
            for p in files: open(p, 'wb').write(backup[p])
            for path, old, new in edits:
                b = open(path, 'rb').read()
                open(path, 'wb').write(b.replace(old.encode(), new.encode()))
            log = os.path.join(OUT, name + '.log')
            if os.path.exists(log): os.remove(log)
            t0 = time.time()
            if run(['dotnet', 'build', 'tests/Kronikol.Tests.EndToEnd', '-c', 'Debug', '-nologo', '-v:q'], log) != 0:
                print(name, 'BUILD FAILED', flush=True)
                continue
            if not facts:
                print(name, 'no fact named: run by hand', flush=True)
                continue
            flt = '|'.join('FullyQualifiedName~' + (f if '.' in f else CLS + '.' + f) for f in facts)
            rc = run(['dotnet', 'test', 'tests/Kronikol.Tests.EndToEnd', '--no-build', '-c', 'Debug', '--filter', flt,
                      '--logger', 'console;verbosity=normal'], log)
            text = open(log, encoding='utf-8', errors='replace').read()
            failed = re.findall(r'^\s*Failed (\S+)', text, re.M)
            passed = re.findall(r'^\s*Passed (\S+)', text, re.M)
            print(f'{name}: {"CAUGHT" if failed else "SURVIVED"} rc={rc} failed={len(failed)} passed={len(passed)} '
                  f'{int(time.time() - t0)}s {[f.split(".")[-1][:60] for f in failed]}', flush=True)
    finally:
        for p in files: open(p, 'wb').write(backup[p])
        print('restored', flush=True)


if __name__ == '__main__':
    main(sys.argv[1:] or list(MUTATIONS))
