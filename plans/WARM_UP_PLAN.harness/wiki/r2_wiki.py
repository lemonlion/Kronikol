"""R2's wiki edit (plans/WARM_UP_PLAN.md 6.4): the duration badge, the Scenario Timeline and the duration filter, none
documented before, with the first-call warm-up's marker.

    python r2_wiki.py <wiki checkout> <version>
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
V = sys.argv[2]
PAGE = 'Why-Is-My-First-Scenario-Slow'

path = WIKI / 'Generated-Reports.md'
text = path.read_text(encoding='utf-8')
anchor = '- **Duration columns** — Feature summary table includes Duration, Avg, and Longest columns when scenario durations are available.\n'
assert text.count(anchor) == 1
new = f"""- **Duration badge**: a scenario with a recorded duration shows it in its header, green under 2 s, amber under 5 s and red
  above; a parameterized group's badge is the sum of its rows. From {V}, a scenario that paid the run's
  [first-call warm-up]({PAGE}) keeps its wall time as the badge's text and adds the warm-up as muted text, as in
  `1.3s · 1.2s warm-up`. Hovering the badge names the calls (`1.27 s, 1.19 s of it first-call warm-up: POST /orders
  661 ms (later calls 7.6 ms), GET /milk 82 ms (2.3 ms)`), and its colour is the one the time left earns, so a scenario
  slow only because it went first is not coloured slow. A group sums its rows' warm-up as it sums their durations.
- **Scenario Timeline**: a toolbar toggle draws every scenario's duration as a bar, longest first, coloured by result
  (green passed, red failed, grey skipped, orange bypassed). From {V} the warm-up's share of a marked scenario's bar is
  shaded from its start, with the badge's tooltip; the bars keep their order, which is elapsed time.
- **Duration filter**: `Duration ≥` buttons for the run's P50, P90, P95 and P99, and a custom number of seconds, hide
  every scenario below the threshold, and one with no recorded duration. From {V} the percentiles and the comparison
  read each scenario's own time, its first-call warm-up left out, and a percentile is the nearest rank: the P50 of two
  durations is the smaller, where it was the larger. Export Filtered CSV still writes wall time.
"""
path.write_bytes(text.replace(anchor, anchor + new).encode('utf-8'))
print('edited Generated-Reports.md')
