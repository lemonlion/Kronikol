"""Settles the three places §5.8 names, for a scratch merge of #222's R1 under this plan's last commit.

git worktree add <w> origin/main; #222's apply_r1.py, apply_r1b.py, apply_r1c.py; commit; git diff origin/main..proto/223-on-5282 -- src | git apply --3way
python -I merge_222_resolve.py <w>

The guard: this plan's frame per level. The split: the first N rows a Gherkin step opened are the Background, and a hook's row stays in Steps. The count:
a nested frame is not one of the scenario's own steps. #222's `Nested` property stays, always null, so its position path compiles: scratch only.
"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])


def resolve(path, pick):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    t = t.replace("\r\n", "\n")
    index = [0]

    def choose(m):
        chosen = pick(index[0], m.group(1), m.group(2))
        index[0] += 1
        return chosen

    t = re.sub(r"<<<<<<< ours\n(.*?)=======\n(.*?)>>>>>>> theirs\n", choose, t, flags=re.S)
    p.write_bytes(t.replace("\n", newline).encode("utf-8"))
    return index[0]


def edit(path, old, new):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    t = t.replace("\r\n", "\n")
    assert t.count(old) == 1, (path, old[:60])
    p.write_bytes(t.replace(old, new).replace("\n", newline).encode("utf-8"))


hooks = "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs"
info = "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs"
extensions = "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs"

print(resolve(info, lambda i, ours, theirs: ours + "\n" + theirs + "\n    /// <summary>The frame opened inside another: not one of the scenario's own steps.</summary>\n    internal bool NestedInFrame { get; init; }\n"))
print(resolve("src/Kronikol/Reports/BackgroundStepsDetector.cs", lambda i, ours, theirs: ours))
print(resolve(hooks, lambda i, ours, theirs: theirs))
print(resolve(extensions, lambda i, ours, theirs: "                                BackgroundSteps = background.Length > 0 ? background : null,\n                                Steps = own.Length > 0 ? own : null,\n"))

edit(hooks, "            inlineParams) { FrameId = frame });", "            inlineParams) { FrameId = frame, NestedInFrame = StepCollector.OpenFrameOf(scenarioId) is not null });")
edit(hooks, """    internal static int CountBackgroundSteps(IReadOnlyList<ReqNRollStepInfo> steps, IReadOnlyList<string> writtenBackground)
    {
        var count = 0;
""", """    internal static int CountBackgroundSteps(IReadOnlyList<ReqNRollStepInfo> allSteps, IReadOnlyList<string> writtenBackground)
    {
        var steps = allSteps.Where(s => !s.NestedInFrame && s.NotRunUnder is null).ToList();
        var count = 0;
""")
t = (root / extensions).read_bytes().decode("utf-8").replace("\r\n", "\n")
m = re.search(r"( *)var ran = [^\n]*\n( *)var backgroundCount = Math\.Min\(x\.BackgroundStepCount, ran\.Length\);\n", t)
assert m, "the split's lines"
pad = m.group(1)
t = t[:m.start()] + (
    f"{pad}var ran = MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)) ?? [];\n"
    f"{pad}// The first rows a Gherkin step opened are the Background as written; a hook's row stays with the scenario's own.\n"
    f"{pad}var background = ran.Where(r => !r.OutsideFrameworkStep).Take(x.BackgroundStepCount).ToArray();\n"
    f"{pad}var own = ran.Where(r => !background.Any(b => ReferenceEquals(b, r))).ToArray();\n"
) + t[m.end():]
(root / extensions).write_bytes(t.encode("utf-8"))
print("ok")
