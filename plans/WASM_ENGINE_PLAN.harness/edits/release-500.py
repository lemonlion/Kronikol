# Release 5.0.0 (the owner's ask of 2026-10-20): every package and manifest to 5.0.0, the templates to the last
# published version (4.32.1), the changelog's [Unreleased] folded into [5.0.0] with why the major part moves, and the
# 5.0.0 plans' rows and logs saying they were released. usage: python -I release-500.py <repo root>
import glob, re, sys

root = sys.argv[1]
DATE = '2026-10-20'


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    for old, new in pairs:
        assert t.count(old) == 1, (rel, t.count(old), old[:120])
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


edit('Directory.Build.props', [('<Version>4.32.1</Version>', '<Version>5.0.0</Version>')])
edit('.claude-plugin/plugin.json', [('"version": "4.32.1"', '"version": "5.0.0"')])
edit('.claude-plugin/marketplace.json', [('"version": "4.32.1"', '"version": "5.0.0"')])
edit('templates/github-actions/kronikol-history/VERSION', [('4.32.1', '5.0.0')])
pins = 0
for path in glob.glob(root + '/templates/*/KronikolComponentTests.csproj'):
    t = open(path, 'rb').read().decode('utf-8')
    n = len(re.findall(r'(Include="Kronikol[^"]*" Version=")4\.32\.0(")', t))
    t2 = re.sub(r'(Include="Kronikol[^"]*" Version=")4\.32\.0(")', r'\g<1>4.32.1\g<2>', t)
    assert n >= 1 and '4.32.0' not in t2, path
    pins += n
    open(path, 'wb').write(t2.encode('utf-8'))
assert pins == 12, pins

edit('CHANGELOG.md', [
    ("""## [Unreleased]

For 5.0.0 only: this section is on the branch `v5`, which carries the 5.0.0 work and is released as 5.0.0, not before.
""",
     """## [5.0.0] - """ + DATE + """

The major part moves (4.32.1 to 5.0.0): this release changes defaults and public surface that existing code and
existing reports read. The data file is `TestRunReport.krkl` by default and its schema is written only when asked;
labels and categories are one list of tags (`Feature.Tags`, `Scenario.Tags`, `data-tags`, the data files' `tags`);
every package targets .NET 10 alone; the report is the Option C redesign, with its diagram toggles' start states, its
tabs and its colours, so a test or a script that reads the page's markup or the diagrams' source meets new shapes; and
the browser engine is PlantUML's Wasm GC build of a newer PlantUML (`TrackingDefaults.PlantUmlJsCdnBase` names
another address). Each change is under Changed (breaking) below, with the setting that keeps the 4.x behaviour where
one does; the wiki's Migrating to v5 page gathers them. Everything here was merged on the branch `v5` and is released
for the first time in this version (`plans/V5_PLAN.md`).
"""),
    ("""- **The report's look (Option C, in progress on the branch).**""",
     """- **The report's look (Option C).**"""),
])

edit('plans/PLANS_STATUS.md', [
    ("""| ✅ **Executed** on the local branch `v5` (not pushed, not released): the Option C report""",
     """| ✅ **Executed and released as 5.0.0 (""" + DATE + """):** the Option C report"""),
    ("""Executes the Option C half of `TOOLBAR_REDESIGN_PLAN.md`, where the preview's later decisions win. | 5.0.0 (unreleased) |""",
     """Executes the Option C half of `TOOLBAR_REDESIGN_PLAN.md`, where the preview's later decisions win. | 5.0.0 |"""),
    ("""| 📝 **The index of 5.0.0** (`ROADMAP.md` D39, stage 1e): the report data file (`REPORT_FORMAT_PLAN.md`), Option C (`OPTION_C_PLAN.md`, on the local `v5` branch) and the dependency filter's OR default (`9452ddb5`, on `v5`) | not started |""",
     """| ✅ **The index of 5.0.0, released """ + DATE + """** (`ROADMAP.md` D39, stage 1e): the report data file (`REPORT_FORMAT_PLAN.md`), Option C (`OPTION_C_PLAN.md`), the dependency filter's OR default (`9452ddb5`), the tags (`TAGS_PLAN.md`) and the engine (`WASM_ENGINE_PLAN.md`) | 5.0.0 |"""),
    ("""| 🚧 **Executing** (the owner's ask of 2026-10-19: in full, with the v5 part as unreleased commits on a branch made from `v5`).""",
     """| ✅ **Executed; its v5 part released in 5.0.0 (""" + DATE + """), R6 is Kronikol4J's (on its `main` for 0.1.25) and R7 waits for .NET 11** (the owner's ask of 2026-10-19: in full, with the v5 part as unreleased commits on a branch made from `v5`)."""),
    ("""| R1 4.27.0, R2 4.27.1, R3 4.30.0, R4 4.32.0; R6, the v5 commits and R5 to come; R7 waits for .NET 11 |""",
     """| R1 4.27.0, R2 4.27.1, R3 4.30.0, R4 4.32.0 (its fix 4.32.1), R5 and the v5 commits 5.0.0; R6 in Kronikol4J; R7 waits for .NET 11 |"""),
    ("""| ✅ **Executed** on `v5`, unreleased (the owner's ask of 2026-10-19:""",
     """| ✅ **Executed and released in 5.0.0 (""" + DATE + """)** (the owner's ask of 2026-10-19:"""),
    ("""| ✅ **Executed** for 5.0.0 (the owner's ask of 2026-10-20: release v5""",
     """| ✅ **Executed and released in 5.0.0 (""" + DATE + """)** (the owner's ask of 2026-10-20: release v5"""),
])


def log_line(rel, line):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    open(p, 'wb').write((t.rstrip('\n') + '\n' + line + '\n').encode('utf-8'))


# The report format plan's log is section 11, which Appendix A follows, and its entries open with the date in bold.
edit('plans/REPORT_FORMAT_PLAN.md', [
    ('''

## Appendix A. Edit sites at `a8563d21`''',
     '''
- **''' + DATE + ''', release:** the v5 part released in 5.0.0, with Option C, the tags and the Wasm engine
  (`V5_PLAN.md`). R6 is on Kronikol4J's `main` for 0.1.25, and R7 waits for .NET 11.

## Appendix A. Edit sites at `a8563d21`'''),
])
log_line('plans/OPTION_C_PLAN.md', '- ' + DATE + ': released as 5.0.0, with the report data file, the tags and the Wasm engine; the wiki '
         'series (ten patches) and the Kronikol4J ledger entries publish with it.')
log_line('plans/WASM_ENGINE_PLAN.md', '- ' + DATE + ': released in 5.0.0.')
print('ok', pins, 'template pins')

# Added after the first run: two phrases of the report format plan's row still placed its v5 part on the local branch.
# Applied by hand on 2026-10-20 (the script had already run), kept here as the record of the edit:
#   "On the local `v5` branch (5.0.0, unreleased): the `.krkl` ..." -> "In 5.0.0 (released 2026-10-20): the `.krkl` ..."
#   "On the local `v5` branch it is one of 5.0.0's three pieces" -> "In 5.0.0 it is one of the release's pieces"
