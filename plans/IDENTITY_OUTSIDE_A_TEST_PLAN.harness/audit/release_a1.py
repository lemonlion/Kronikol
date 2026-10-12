"""Applies the audit release's numbered edits to a checkout: the version everywhere a package or the plugin carries it,
the templates' pins (the release before this one), the history action's VERSION, the CHANGELOG entry, and the
placeholders this audit left in the plan, PLANS_STATUS.md and ROADMAP.md. Fails without writing if an expected anchor
is missing, or if the CHANGELOG opens with an Unreleased section (fold that by hand: CLAUDE.md).

    python release_a1.py <checkout> <version> "<core suite result>"
"""
import pathlib
import re
import sys

ROOT, VERSION, CORE = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
HERE = pathlib.Path(__file__).parent
writes = {}


def read(rel):
    return writes.get(rel) or (ROOT / rel).read_bytes().decode("utf-8")


props = read("Directory.Build.props")
CURRENT = re.search(r"<Version>(\d+\.\d+\.\d+)</Version>", props).group(1)
assert CURRENT != VERSION, "already bumped"


def edit(rel, old, new, times=1):
    text = read(rel)
    found = text.count(old)
    if found != times:
        sys.exit(f"{rel}: expected {times} x {old[:60]!r}, found {found}")
    writes[rel] = text.replace(old, new)


edit("Directory.Build.props", f"<Version>{CURRENT}</Version>", f"<Version>{VERSION}</Version>")
edit(".claude-plugin/plugin.json", f'"version": "{CURRENT}"', f'"version": "{VERSION}"')
edit(".claude-plugin/marketplace.json", f'"version": "{CURRENT}"', f'"version": "{VERSION}"')
history = "templates/github-actions/kronikol-history/VERSION"
if read(history).strip() != CURRENT:
    sys.exit(f"{history} holds {read(history).strip()!r}, not {CURRENT}")
writes[history] = read(history).replace(CURRENT, VERSION)

# The templates pin the release before this one: it is on nuget.org when CI builds the scaffolds.
pin = re.compile(r'(Include="Kronikol[^"]*" Version=")(\d+\.\d+\.\d+)(")')
templates = sorted(ROOT.glob("templates/*/KronikolComponentTests.csproj"))
if not templates:
    sys.exit("no template projects found")
for path in templates:
    rel = path.relative_to(ROOT).as_posix()
    text, n = pin.subn(lambda m: m.group(1) + CURRENT + m.group(3), read(rel))
    if n == 0:
        sys.exit(f"{rel}: no Kronikol pin")
    writes[rel] = text

changelog = read("CHANGELOG.md")
first = changelog.index("\n## [")
if changelog[first:].startswith("\n## [Unreleased]"):
    sys.exit("CHANGELOG.md opens with [Unreleased]: fold it into this release by hand first")
entry = (HERE / "changelog.md").read_text(encoding="utf-8").replace("{VERSION}", VERSION).replace("{PREVIOUS}", CURRENT)
writes["CHANGELOG.md"] = changelog[:first + 1] + entry.rstrip("\n") + "\n\n" + changelog[first + 1:]

plan = "plans/IDENTITY_OUTSIDE_A_TEST_PLAN.md"
for rel in (plan, "plans/PLANS_STATUS.md", "plans/ROADMAP.md"):
    text = read(rel)
    if "{VERSION}" not in text:
        sys.exit(f"{rel}: no {{VERSION}} placeholder")
    writes[rel] = text.replace("{VERSION}", VERSION)
if writes[plan].count("{CORE}") != 1:
    sys.exit(f"{plan}: expected one {{CORE}}")
writes[plan] = writes[plan].replace("{CORE}", CORE)

for rel, text in writes.items():
    (ROOT / rel).write_bytes(text.encode("utf-8"))
print(f"{len(writes)} files written for {VERSION} (was {CURRENT}; the templates pin {CURRENT})")
