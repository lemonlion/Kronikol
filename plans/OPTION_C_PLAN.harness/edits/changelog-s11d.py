# S11d's changelog entry: the last of [Unreleased]'s Added list (older sections have their own Changed headings, so
# the anchor is found after [Unreleased]). usage: python changelog-s11d.py <root>
import sys
p = sys.argv[1].rstrip('/') + '/CHANGELOG.md'
s = open(p, encoding='utf-8', newline='').read()
start = s.index('## [Unreleased]')
end = s.index('\n## [', start + 1)
j = s.index('\n### Changed (breaking)\n', start)
assert j < end and 'The Timeline tab has its controls' not in s[start:end]
entry = ("- **The Timeline tab has its controls.** Show Filtered (the start) or Full, and where a scenario paid a first-call\n"
         "  warm-up, Warm-up Shown or Hidden and Order With or Without warm-up. Filtered draws the scenarios the filters leave\n"
         "  showing and says how many it leaves out; the bars are drawn to the longest one showing; with the warm-up hidden a\n"
         "  bar and its time are the scenario's own. A failed scenario's row opens with a cross, the timeline takes the theme's\n"
         "  colours, and a bypassed scenario's bar is blue, as the run's ring draws it (orange before).\n")
s = s[:j] + entry + s[j:]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
