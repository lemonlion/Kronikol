# S11f's changelog entries, after [Unreleased]'s "History and diagnostics are back" entry. usage: python changelog-s11f.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
start = s.index('## [Unreleased]')
end = s.index('\n## [', start + 1)
a = s.index('- **History and diagnostics are back in the report, as its History and Diagnostics tabs.**', start)
j = s.index('\n- **', a) + 1
assert a < end and j < end and "History's pass rate is a ring per run" not in s[start:end]
entry = ("- **History's pass rate is a ring per run,** drawn like the run band's ring with the rate inside, never 100% while\n"
         "  any scenario of the run did not pass; a run that failed has a fail arc. Each ring keeps its run's tooltip, and the\n"
         "  labs page draws them too. The `svg.history-chart-svg` chart and its `history-bar-pass`, `history-bar-fail`,\n"
         "  `history-bar-duration` and `history-bar-partial` classes are gone.\n"
         "- **History's duration bars carry their time** (\"850ms\", \"44s\", \"2m 26s\", \"1h 5m\"), in the bar or just above a\n"
         "  short one; a window too long for its card scrolls inside it and opens on the newest run.\n"
         "- **The History section and its verdict pills take the theme,** each kind in its own colour.\n"
         "- **A duration of an hour or more reads in hours** in the run band and the features table (\"1h 5m\", where it read\n"
         "  \"65m 0s\").\n")
s = s[:j] + entry + s[j:]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
