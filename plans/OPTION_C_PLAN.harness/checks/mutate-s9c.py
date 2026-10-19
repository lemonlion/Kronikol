# S9c's mutations: each undoes one behaviour of the note buttons, rebuilds, runs the Playwright test that should catch
# it, expects it to FAIL, and puts the source back. usage: python mutate-s9c.py   (from anywhere; takes a few minutes)
import subprocess, sys

ROOT = 'C:/Code/Kronikol-v5/'
CNS = ROOT + 'src/Kronikol/Reports/collapsible-notes-script.js'
T = 'Kronikol.Tests.EndToEnd.NoteButtonLayoutTests.'
MUTATIONS = [
    ("keepPlace watches the fragment it was given", CNS,
     "        var host = container.classList && container.classList.contains('puml-fragment') && container.parentElement\n            ? container.parentElement : container;",
     "        var host = container;",
     T + 'Making_a_note_shorter_from_its_bottom_brings_its_new_bottom_back_to_the_click'),
    ("keepPlace never scrolls", CNS,
     "                if (Math.abs(rb.bottom - want) > 1) window.scrollBy(0, rb.bottom - want);", "",
     T + 'Making_a_note_shorter_from_its_bottom_brings_its_new_bottom_back_to_the_click'),
    ("a hidden button keeps its place", CNS,
     "                if (b.style.display === 'none') return;\n", "",
     T + 'A_hidden_button_leaves_no_gap_among_the_top_right_buttons'),
    ("a collapsed note that shows all of itself gets + and the bottom triangle", CNS,
     "        var shownWhole = state === 'collapsed' && collapsedIsWhole(contentLines, hdrHidden);",
     "        var shownWhole = false;", T + 'A_collapsed_note_that_shows_all_of_itself_has_nothing_to_open'),
    ("a truncated note's bottom holds one button", CNS,
     "            var pair = state === 'truncated';", "            var pair = false;",
     T + 'A_truncated_notes_bottom_holds_a_collapse_and_an_expand_button_side_by_side'),
    ("the top triangle is 2 px clear", CNS,
     "ga.appendChild(drawnTriangle(ax, ay, topSize, topSize, true, 3));", "ga.appendChild(drawnTriangle(ax, ay, topSize, topSize, true));",
     T + 'An_expanded_long_notes_triangles_are_drawn_and_clear_of_their_buttons_edges'),
    ("the top triangle stays at a tall note's top", CNS,
     "            if (upTop) topRight.push(upTop);\n", "",
     T + 'The_top_right_buttons_follow_the_visible_part_of_a_tall_note_the_triangle_too'),
    ("the + of a long note says it opens in full", CNS,
     "longNote ? 'Open this box to the line limit' : 'Open this box in full'", "'Open this box in full'",
     T + 'Every_button_of_a_long_note_says_what_a_click_does'),
    ("the bottom triangle has no title", CNS,
     "ge.appendChild(giveTitle(bgE, 'Show more of this box'));", "ge.appendChild(bgE);",
     T + 'Every_button_of_a_long_note_says_what_a_click_does'),
    ("the bottom collapse says it cuts back", CNS,
     "gcp.appendChild(giveTitle(bgCP, 'Fold this box to one line'));",
     "gcp.appendChild(giveTitle(bgCP, 'Cut this box back to the line limit'));",
     T + 'Every_button_of_a_long_note_says_what_a_click_does'),
]

killed = 0
for name, path, old, new, test in MUTATIONS:
    s = open(path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj',
                            '--filter', 'FullyQualifiedName~' + test], capture_output=True, text=True)
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
