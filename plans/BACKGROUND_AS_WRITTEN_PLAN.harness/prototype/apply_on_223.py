#!/usr/bin/env python
"""R1 with #223's prototype under it (apply_on_223.py <worktree at 638f275d> <r1-first-cut.patch>): what landing this plan second would take.

Taken from R1: the document, the section, the query hint. Left out: its guard (OpenStep, Nested, OpenedBy, DescribeNested), as that plan's frame per level, StepPairing and
nested frames do the same job (section 7), and sections 5.4 and 5.5, as that prototype's R1 holds them. What is new here is the one condition
the two meet in: a row no framework step opened is never the Background, and stays in Steps, as the Cucumber lane does.
"""
import subprocess
import sys

root, patch = sys.argv[1:3]

subprocess.run(
    [
        "git", "-c", "core.autocrlf=false", "-c", "core.eol=lf", "apply", "--whitespace=nowarn",
        "--include=src/Kronikol.ReqNRoll.Core/ExamplesBlockResolver.cs",
        "--include=src/Kronikol.ReqNRoll.Core/ReqNRollScenarioInfo.cs",
        "--include=src/Kronikol/Reports/ReportGenerator.cs",
        "--include=src/Kronikol/Reports/ReportToggleDefaults.cs",
        "--include=src/Kronikol/Reports/ReportToggleDefaultsResolver.cs",
        "--include=src/Kronikol/Query/QueryCommand.Narrative.cs",
        "--include=src/Kronikol/Reports/report-select-row-function.js",
        "--include=src/Kronikol/Reports/report-search-function.js",
        patch,
    ],
    cwd=root,
    check=True,
)


def edit(path, *pairs):
    full = root + "/" + path
    text = open(full, encoding="utf-8", newline="").read()
    for old, new in pairs:
        assert text.count(old) == 1, old[:90]
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8", newline="").write(text)


edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """            SourceLine = source.Line,
""",
        """            SourceLine = source.Line,
            WrittenBackground = ExamplesBlockResolver.ResolveBackgroundSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo),
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                            return new Scenario
""",
        """                            // Every step is this scenario's own, as it ran; those a Background: gave it come first (#222).
                            var (background, own) = SplitBackground(MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)) ?? [], x.WrittenBackground ?? []);

                            return new Scenario
""",
    ),
    (
        """                                Steps = MapSteps(x.Steps, StepCollector.GetSteps(x.ScenarioId)),
""",
        """                                BackgroundSteps = background,
                                Steps = own,
""",
    ),
    (
        """                BackgroundStepsDetector.DetectAndExtract(scenarios);
""",
        """                // Scenarios built by hand have no document to read, so what they share is still only guessed at.
                if (featureGroup.All(x => x.WrittenBackground is null))
                    BackgroundStepsDetector.DetectAndExtract(scenarios);
""",
    ),
    (
        """    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep? collected, bool priorFailure) => new()
""",
        """    /// <summary>
    /// The rows the document writes in a Background, from the first framework step: a failed step ends a scenario early, and
    /// a row that disagrees is never called a Background step. A row no framework step opened (a hook's step method) stays in
    /// Steps, as the Cucumber lane has it.
    /// </summary>
    internal static (ScenarioStep[]? Background, ScenarioStep[]? Own) SplitBackground(IReadOnlyList<ScenarioStep> ran, IReadOnlyList<string> writtenBackground)
    {
        var background = new List<ScenarioStep>();
        var own = new List<ScenarioStep>();
        var closed = false;
        foreach (var row in ran)
        {
            if (!row.OutsideFrameworkStep && !closed && background.Count < writtenBackground.Count
                && string.Equals(row.Text, writtenBackground[background.Count], StringComparison.Ordinal))
            {
                background.Add(row);
                continue;
            }

            closed |= !row.OutsideFrameworkStep;
            own.Add(row);
        }

        return (background.Count > 0 ? background.ToArray() : null, own.Count > 0 ? own.ToArray() : null);
    }

    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep? collected, bool priorFailure) => new()
""",
    ),
)

print("ok")
