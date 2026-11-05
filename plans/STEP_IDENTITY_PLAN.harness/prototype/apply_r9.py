"""R9, the fifth cut, from the third independent review: BackgroundStepsDetector reads only the rows a framework step ran.

python -I apply_r9.py <checkout>

The rows R2 adds and a hook's row went into its shared prefix, which compares keyword and text and hands every scenario the first scenario's objects, so one
failed or skipped scenario rewrote its passing siblings, in a green run too, and could lose the report (REVIEW.md, third review). What it reads now is what it
always read, on published, so its answers are published's by construction, and the other rows stay in Steps. The ingest lanes' rows carry no mark.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, change):
    p = root / path
    t = p.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    p.write_bytes(change(t.replace("\r\n", "\n")).replace("\n", newline).encode("utf-8"))


def replace(t, old, new):
    assert t.count(old) == 1, old[:70]
    return t.replace(old, new)


def detector(t):
    start = t.index("            var members = group.Where(s => s.Steps is { Length: > 0 }).ToArray();")
    end = t.index("    private static int FindCommonPrefixLength(Scenario[] scenarios)")
    t = t[:start] + """            var members = group.Select(s => (Scenario: s, Ran: RanByTheFramework(s))).Where(m => m.Ran.Length > 0).ToArray();
            if (members.Length < 2)
                continue;

            // If any scenario starts with "And" or "When", the shared prefix is not
            // a Gherkin Background – skip extraction for this group.
            if (members.Any(m => m.Ran[0].Keyword is "And" or "When"))
                continue;

            var commonPrefixLength = FindCommonPrefixLength(members.Select(m => m.Ran).ToArray());
            if (commonPrefixLength == 0)
                continue;

            // Don't extract if any scenario's first remaining step starts with "Given" or "When";
            // background extraction only makes sense when remaining steps start with continuation
            // keywords (Then, And, But) or when all steps are common (no remaining steps).
            if (members.Any(m => m.Ran.Length > commonPrefixLength &&
                                 m.Ran[commonPrefixLength].Keyword is "Given" or "When"))
                continue;

            // Use the first scenario's steps as the background template
            var backgroundSteps = members[0].Ran.Take(commonPrefixLength).ToArray();

            foreach (var (scenario, ran) in members)
            {
                var own = ran.Take(commonPrefixLength).ToArray();
                scenario.OwnBackgroundSteps = own;
                scenario.BackgroundSteps = backgroundSteps;
                scenario.Steps = scenario.Steps!.Where(step => !own.Any(o => ReferenceEquals(o, step))).ToArray();
            }
        }
    }

    // A row no framework step ran (a hook's step method, a step that never ran) says nothing about what scenarios share. It
    // would only let one scenario's outcome and arguments be read as another's, so it stays with the scenario's own steps.
    private static ScenarioStep[] RanByTheFramework(Scenario scenario) =>
        (scenario.Steps ?? []).Where(step => !step.OutsideFrameworkStep && !step.DidNotRun).ToArray();

""" + t[end:]
    t = replace(t, "    private static int FindCommonPrefixLength(Scenario[] scenarios)\n", "    private static int FindCommonPrefixLength(ScenarioStep[][] scenarios)\n")
    t = replace(t, "        var minStepCount = scenarios.Min(s => s.Steps!.Length);\n", "        var minStepCount = scenarios.Min(s => s.Length);\n")
    t = replace(t, "            var reference = scenarios[0].Steps![i];\n", "            var reference = scenarios[0][i];\n")
    t = replace(t, """                s.Steps![i].Keyword == reference.Keyword &&
                s.Steps[i].Text == reference.Text);
""", """                s[i].Keyword == reference.Keyword &&
                s[i].Text == reference.Text);
""")
    return t


edit("src/Kronikol/Reports/BackgroundStepsDetector.cs", detector)
edit("src/Kronikol/Reports/ScenarioStep.cs", lambda t: replace(
    t,
    "    internal bool OutsideFrameworkStep { get; init; }\n",
    "    internal bool OutsideFrameworkStep { get; init; }\n\n"
    "    /// <summary>A framework's step that never ran (skipped after a failure, undefined): listed, and not what ran; never written to a report.</summary>\n"
    "    internal bool DidNotRun { get; init; }\n"))
edit("src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs", lambda t: replace(
    t,
    "    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep? collected, bool priorFailure) => new()\n    {\n        CollectedId = collected?.CollectedId,\n",
    "    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep? collected, bool priorFailure) => new()\n    {\n        CollectedId = collected?.CollectedId,\n        DidNotRun = step.FrameId is null,\n"))
print("ok")
