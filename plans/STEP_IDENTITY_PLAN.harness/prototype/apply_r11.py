#!/usr/bin/env python
# R11, the fourth review's changes, on R10 (9b85227f). usage: apply_r11.py <worktree> tests|r1|r2|r3
#   tests: the failing facts, first
#   r1: each scenario keeps its own Background rows, a walk visits a step once, a row is removed by its place, and a call is
#       read by its stamp only where a scenario's top-level rows carry ids; by-position rows carry none
#   r2: a row that did not run has a mark of its own in query
#   r3: #129's order leaves R1 and comes back with the walk's false mismatches on an ingest fixed
import pathlib, sys

root = pathlib.Path(sys.argv[1])
stage = sys.argv[2]


def sub(path, old, new, count=1):
    p = root / path
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    t = raw.replace("\r\n", "\n")
    assert t.count(old) == count, (path, t.count(old), old[:90])
    t = t.replace(old, new)
    p.write_bytes((t.replace("\n", "\r\n") if crlf else t).encode("utf-8"))


if stage == "tests":
    sub("tests/Kronikol.Tests/Reports/BackgroundStepsDetectorTests.cs",
        """    // ── Basic prefix detection ──
""",
        """    // ── Whose rows they are ──

    [Fact]
    public void Each_scenario_keeps_its_own_background_rows()
    {
        var a = MakeScenario("a", ("Given", "a customer"), ("Then", "it works"));
        var b = MakeScenario("b", ("Given", "a customer"), ("Then", "it also works"));
        var ownOfA = a.Steps![0];
        var ownOfB = b.Steps![0];

        BackgroundStepsDetector.DetectAndExtract([a, b]);

        Assert.Same(ownOfA, Assert.Single(a.BackgroundSteps!));
        Assert.Same(ownOfB, Assert.Single(b.BackgroundSteps!));
    }

    [Fact]
    public void A_step_object_listed_twice_is_taken_once()
    {
        var login = new ScenarioStep { Keyword = "Given", Text = "logged in", Status = ExecutionResult.Passed };
        var check = new ScenarioStep { Keyword = "Then", Text = "it works", Status = ExecutionResult.Passed };
        var other = new ScenarioStep { Keyword = "Then", Text = "it also works", Status = ExecutionResult.Passed };
        var a = new Scenario { Id = "a", DisplayName = "a", Result = ExecutionResult.Passed, Steps = [login, check, login, other] };
        var b = new Scenario { Id = "b", DisplayName = "b", Result = ExecutionResult.Passed, Steps = [login, other] };

        BackgroundStepsDetector.DetectAndExtract([a, b]);

        Assert.Equal(["it works", "logged in", "it also works"], a.Steps!.Select(s => s.Text));
        Assert.Equal(["it also works"], b.Steps!.Select(s => s.Text));
    }

    // ── Basic prefix detection ──
""")
    sub("tests/Kronikol.Tests/Reports/ReportAttributionTests.cs",
        """    // ─── Helpers ─""",
        """    [Fact]
    public void Rows_built_by_hand_over_the_collectors_sub_steps_are_read_against_the_bars()
    {
        // An adapter of a consumer's own: its rows, the collector's sub-steps under them. The rows carry no ids, so a stamp
        // has nothing to name, and a map of the sub-steps alone would leave every call that is not under one with no step.
        var testId = "attr-own-rows-" + Guid.NewGuid().ToString("N");
        StepCollector.StartStep(testId, "Given", "a thing", null, null);
        RequestResponseLogger.Log(Real(testId, "before-sub") with { Timestamp = Start.AddSeconds(1) });
        StepCollector.StartStep(testId, null, "a sub step", null, null);
        RequestResponseLogger.Log(Real(testId, "in-sub") with { Timestamp = Start.AddSeconds(2) });
        StepCollector.CompleteStep(testId, true);
        RequestResponseLogger.Log(Real(testId, "after-sub") with { Timestamp = Start.AddSeconds(3) });
        StepCollector.CompleteStep(testId, true);
        StepCollector.StartStep(testId, "When", "another thing", null, null);
        RequestResponseLogger.Log(Real(testId, "second-step") with { Timestamp = Start.AddSeconds(4) });
        StepCollector.CompleteStep(testId, true);
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = testId,
                        DisplayName = "S",
                        Steps = StepCollector.GetSteps(testId)
                            .Select(c => new ScenarioStep { Keyword = c.Keyword, Text = c.Text, Status = c.Status, SubSteps = c.SubSteps })
                            .ToArray(),
                    },
                ],
            },
        ];

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, features)[testId];

        Assert.Equal(["0", "0", "0", "1"], paths);
    }

    // ─── Helpers ─""")
    sub("tests/Kronikol.Tests/Reports/AttachmentCopyTests.cs",
        """public class AttachmentCopyTests : IDisposable
{
    private readonly string _tempDir;
""",
        """public class AttachmentCopyTests : IDisposable
{
    private readonly string _tempDir;

    [Fact]
    public void A_step_two_scenarios_hold_is_visited_once()
    {
        // ScenarioStep and Feature are public, so a caller can list one step twice; a display fault must not lose the report.
        var shared = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a screenshot",
            Attachments = [new FileAttachment("shot.png", "shot.png")],
        };
        var scenarios = new[]
        {
            new Scenario { Id = "a", DisplayName = "a", Steps = [shared] },
            new Scenario { Id = "b", DisplayName = "b", BackgroundSteps = [shared] },
        };
        var visits = 0;

        AttachmentPlacement.ForEachList(scenarios, attachments =>
        {
            visits++;
            return attachments;
        });

        Assert.Equal(1, visits);
    }
""")

elif stage == "r1":
    d = "src/Kronikol/Reports/BackgroundStepsDetector.cs"
    sub(d, """/// Detects common step prefixes across scenarios (heuristic for Gherkin Background sections)
/// and extracts them into <see cref="Scenario.BackgroundSteps"/>.
""", """/// Detects common step prefixes across scenarios (heuristic for Gherkin Background sections)
/// and moves each scenario's own rows of that prefix into its <see cref="Scenario.BackgroundSteps"/>.
""")
    sub(d, """            // Use the first scenario's steps as the background template
            var backgroundSteps = members[0].Ran.Take(commonPrefixLength).ToArray();

            foreach (var (scenario, ran) in members)
            {
                var own = ran.Take(commonPrefixLength).ToArray();
                scenario.OwnBackgroundSteps = own;
                scenario.BackgroundSteps = backgroundSteps;
                scenario.Steps = scenario.Steps!.Where(step => !own.Any(o => ReferenceEquals(o, step))).ToArray();
            }
""", """            // Each scenario's own rows: the same text can have run with other arguments, for longer, with another outcome or
            // with a file of its own, and a shared row would show the first scenario's in every other.
            foreach (var (scenario, ran) in members)
            {
                scenario.BackgroundSteps = ran.Take(commonPrefixLength).ToArray();
                scenario.Steps = AfterTheFirstThatRan(scenario.Steps!, commonPrefixLength);
            }
""")
    sub(d, """    private static int FindCommonPrefixLength(""", """    private static ScenarioStep[] AfterTheFirstThatRan(ScenarioStep[] steps, int count)
    {
        var rest = new List<ScenarioStep>(steps.Length);
        foreach (var step in steps)
        {
            if (count > 0 && !step.OutsideFrameworkStep && !step.DidNotRun)
                count--;
            else
                rest.Add(step);
        }
        return rest.ToArray();
    }

    private static int FindCommonPrefixLength(""")

    sub("src/Kronikol/Reports/AttachmentPlacement.cs", """    internal static void ForEachList(IEnumerable<Scenario> scenarios, Func<FileAttachment[], FileAttachment[]> replace)
    {
        foreach (var scenario in scenarios)
        {
            if (scenario.Attachments""", """    internal static void ForEachList(IEnumerable<Scenario> scenarios, Func<FileAttachment[], FileAttachment[]> replace)
    {
        // Each scenario and step once, however many lists hold it: a list replaced twice would hand replace the copies it made.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var scenario in scenarios)
        {
            if (!visited.Add(scenario))
                continue;
            if (scenario.Attachments""")
    sub("src/Kronikol/Reports/AttachmentPlacement.cs", """            foreach (var step in steps ?? [])
            {
                if (step.Attachments is""", """            foreach (var step in steps ?? [])
            {
                if (!visited.Add(step))
                    continue;
                if (step.Attachments is""")

    g = "src/Kronikol/Reports/ReportGenerator.cs"
    sub(g, """    /// The address a call under each collected step gets: the top-level step it is in. Null for a scenario whose steps
    /// carry no ids (built by hand through the public model), whose calls are read against the bars.
    /// </summary>
    private static Dictionary<int, string?>? CollectedStepPaths(Scenario scenario)
    {
        var paths = new Dictionary<int, string?>();
""", """    /// The address a call under each collected step gets: the top-level step it is in. Null for a scenario whose top-level
    /// steps carry no ids (built by hand through the public model, or paired by position, whether or not the collector's
    /// sub-steps are under them), whose calls are read against the bars: a stamp could name a sub-step there and nothing else.
    /// </summary>
    private static Dictionary<int, string?>? CollectedStepPaths(Scenario scenario)
    {
        var background = scenario.BackgroundSteps ?? [];
        var own = scenario.Steps ?? [];
        if (!background.Concat(own).Any(step => step.CollectedId is not null))
            return null;

        var paths = new Dictionary<int, string?>();
""")
    sub(g, """        var background = scenario.OwnBackgroundSteps ?? scenario.BackgroundSteps ?? [];
        for (var i = 0; i < background.Length; i++)
            Add(background[i], $"b{i}");
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            Add(scenario.Steps![i], $"{i}");
        return paths.Count > 0 ? paths : null;
""", """        for (var i = 0; i < background.Length; i++)
            Add(background[i], $"b{i}");
        for (var i = 0; i < own.Length; i++)
            Add(own[i], $"{i}");
        return paths;
""")
    sub(g, """force. A render-time projection only; the step itself is never modified, because background steps
    /// are shared across scenarios and the data writers run concurrently with this one.""",
        """force. A render-time projection only; the step itself is never modified, because the data writers run
    /// concurrently with this one.""")
    sub("src/Kronikol/Reports/StepKeywordCollapser.cs",
        """/// This is a render-time projection and never touches the model: background steps are shared by
/// reference across every scenario of a Rule group (<see cref="BackgroundStepsDetector"/>) and the
/// HTML and data outputs are written concurrently, so rewriting a keyword in place would race the
/// data writers and leak <c>And</c> into the JSON/XML/YAML files.""",
        """/// This is a render-time projection and never touches the model: the HTML and data outputs are written
/// concurrently, so rewriting a keyword in place would race the data writers and leak <c>And</c> into
/// the JSON/XML/YAML files.""")
    s = "src/Kronikol/Reports/Scenario.cs"
    t = (root / s).read_bytes().decode("utf-8").replace("\r\n", "\n")
    i = t.index("    internal ScenarioStep[]? OwnBackgroundSteps { get; set; }")
    a = t.rfind("\n\n", 0, i)
    print("removing from Scenario.cs:\n" + t[a:i + 62])
    sub(s, t[a:i + len("    internal ScenarioStep[]? OwnBackgroundSteps { get; set; }")], "")

    # By-position rows carry no id: their position is a guess a stamp must not make a certainty.
    sub("src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
        """                CollectedId = collectedSteps is not null && i < collectedSteps.Length ? collectedSteps[i].CollectedId : null,
""", "")
    sub("src/Kronikol.BDDfy.xUnit3/ScenarioInfoExtensions.cs",
        """                var mapped = new ScenarioStep
                {
                    CollectedId = collected?.CollectedId,
                    Keyword = step.Keyword,
                    Text = step.Text,
                    Status = step.Result.ToStepResult(priorFailure),
                    Duration = step.Duration,
                    SubSteps = collected?.SubSteps,
                    Attachments = collected?.Attachments,
                };
                if (step.Result == TestStack.BDDfy.Result.Failed)
                    priorFailure = true;
                return mapped;
            }).ToArray();
        }
""", """                var mapped = new ScenarioStep
                {
                    Keyword = step.Keyword,
                    Text = step.Text,
                    Status = step.Result.ToStepResult(priorFailure),
                    Duration = step.Duration,
                    SubSteps = collected?.SubSteps,
                    Attachments = collected?.Attachments,
                };
                if (step.Result == TestStack.BDDfy.Result.Failed)
                    priorFailure = true;
                return mapped;
            }).ToArray();
        }
""")
    sub("src/Kronikol.LightBDD.Core/FeatureResultExtensions.cs",
        """                byPosition[i] = MapStep(stepArray[i], priorFailure, scenarioSkipped, collected);
""", """                byPosition[i] = MapStep(stepArray[i], priorFailure, scenarioSkipped, collected) with { CollectedId = null };
""")

elif stage == "r2":
    sub("src/Kronikol/Query/QueryCommand.Narrative.cs",
        """var mark = step.Failed ? "✗" : step.Status is "Bypassed" ? "~" : step.IsAssertion ? "·" : " ";""",
        """var mark = step.Failed ? "✗" : step.Status is "Bypassed" ? "~" : step.Status is "Skipped" or "SkippedAfterFailure" ? "-" : step.IsAssertion ? "·" : " ";""")

elif stage == "r3":
    g = "src/Kronikol/Reports/ReportGenerator.cs"
    sub(g, """                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount < ordered.Count && StepMarkerMatches(""", """                    case DiagramMarkerKind.Step:
                        // A tests file's background step draws no bar (TestRunRecord.IsDiagramMarker), so a bar that is not
                        // its own passes over it.
                        while (stepMarkerCount < ordered.Count && ordered[stepMarkerCount].Path.StartsWith('b')
                               && !StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Text))
                            stepMarkerCount++;

                        current = stepMarkerCount < ordered.Count && StepMarkerMatches(""")
    sub(g, """        var marker = plantUml.Replace('\\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
""", """        var marker = plantUml.Replace('\\n', ' ').Trim();
        var text = stepText.Replace('\\n', ' ');
        return marker.Contains(text, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(text) ?? text, StringComparison.OrdinalIgnoreCase);
""")
elif stage == "r3b":
    sub("tests/Kronikol.Tests/Reports/ReportAttributionTests.cs",
        """    // ─── Helpers ─""",
        """    [Fact]
    public void A_background_step_that_drew_no_bar_is_passed_over()
    {
        // A tests file's background step draws none (TestRunRecord.IsDiagramMarker), and the steps after it have theirs.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "S",
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a shop" }, new ScenarioStep { Keyword = "And", Text = "a customer" }],
                        Steps = [new ScenarioStep { Keyword = "When", Text = "a basket" }, new ScenarioStep { Keyword = "Then", Text = "it is empty" }],
                    }
                ]
            }
        };
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("When", "a basket")),
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("Then", "it is empty")),
            Real("t1", "svc-b"),
        };

        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
            Assert.Equal(["0", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t1"]);

        Assert.DoesNotContain(collector.Entries, e => e.Kind == DiagnosticKind.StepAttributionMismatch);
    }

    [Theory]
    [InlineData("line one\\nline two")]
    [InlineData("a long step whose words go on and on until the bar has to wrap them, and it wraps them with an escape of its own between two words, so that the text as written is nowhere in the bar at all, not that a reader could tell")]
    public void A_bar_that_breaks_its_text_still_names_its_step(string text)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = "t2", DisplayName = "S", Steps = [new ScenarioStep { Keyword = "When", Text = text }] }],
            }
        };
        var logs = new[]
        {
            Marker("t2", DiagramMarkerKind.Step, Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("When", text)),
            Real("t2", "svc-a"),
        };

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t2"]);
    }

    [Fact]
    public void A_bar_that_cuts_its_text_still_names_its_step()
    {
        var text = new string('x', 900);
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = "t3", DisplayName = "S", Steps = [new ScenarioStep { Keyword = "When", Text = text }] }],
            }
        };
        var logs = new[]
        {
            Marker("t3", DiagramMarkerKind.Step, Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml("When", text)),
            Real("t3", "svc-a"),
        };

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t3"]);
    }

    // ─── Helpers ─""")

elif stage == "r3c":
    g = "src/Kronikol/Reports/ReportGenerator.cs"
    sub(g, """        var marker = plantUml.Replace('\\n', ' ').Trim();
        var text = stepText.Replace('\\n', ' ');
        return marker.Contains(text, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(text) ?? text, StringComparison.OrdinalIgnoreCase);
    }
""", """        // A bar wraps a long text, writes a break as the two characters \\n, can mark a character up and cuts what is left, so
        // what both are sure to hold is the text's opening with the breaks taken out.
        var marker = Comparable(plantUml);
        var text = Comparable(stepText);
        if (text.Length > StepMarkerOpeningChars)
            text = text[..StepMarkerOpeningChars];
        return marker.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private const int StepMarkerOpeningChars = 60;

    private static string Comparable(string text) =>
        string.Concat(text.Replace("\\\\n", "", StringComparison.Ordinal).Replace("<U+200B>", "", StringComparison.Ordinal)
            .Where(c => !char.IsWhiteSpace(c) && c != '~'));
""")
else:
    raise SystemExit("stage?")
print("applied", stage)
