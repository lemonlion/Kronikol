using System.Text.Json;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What used to reach the diagram and nothing else: why an assertion failed and where it lives in the
/// source, which example row was in flight, and which step a given call belongs to. All of it now has to
/// survive into the data file, because that is the file a debugging agent reads.
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportAttributionTests
{
    // ─── §2.2 Failure detail on steps and assertions ───────────

    [Fact]
    public void A_failing_step_exports_its_message_and_source_location()
    {
        var features = WithSteps(new ScenarioStep
        {
            Keyword = "Then",
            Text = "the total is right",
            Status = ExecutionResult.Failed,
            FailureMessage = "Expected 4173 but found 3902",
            SourceFile = "OverviewTests.cs",
            SourceLine = 142
        });

        var step = FirstStep(Write(features, "Attr_failure.json"));

        Assert.Equal("Expected 4173 but found 3902", step.GetProperty("failureMessage").GetString());
        Assert.Equal("OverviewTests.cs", step.GetProperty("sourceFile").GetString());
        Assert.Equal(142, step.GetProperty("sourceLine").GetInt32());
    }

    [Fact]
    public void Failure_detail_survives_with_full_step_detail_turned_off()
    {
        // The smaller file is for saving payload bytes, not for hiding why a test failed.
        var features = WithSteps(new ScenarioStep
        {
            Keyword = "Then",
            Text = "the total is right",
            Status = ExecutionResult.Failed,
            FailureMessage = "boom"
        });

        var path = ReportGenerator.GenerateTestRunReportData(features, Start, End, "Attr_failure_lean.json",
            DataFormat.Json, fullStepDetail: false);

        Assert.Equal("boom", FirstStep(path).GetProperty("failureMessage").GetString());
    }

    [Fact]
    public void Collected_step_error_message_reaches_the_scenario_step()
    {
        var testId = Guid.NewGuid().ToString();
        StepCollector.StartStep(testId, "Then", "it works", null, null);
        StepCollector.CompleteStep(testId, passed: false, errorMessage: "assertion blew up");

        var steps = StepCollector.GetSteps(testId);

        Assert.Equal("assertion blew up", Assert.Single(steps).FailureMessage);
    }

    [Fact]
    public void A_tracked_assertion_carries_its_message_and_source_location()
    {
        var testId = Guid.NewGuid().ToString();
        StepCollector.StartStep(testId, "Then", "the total is right", null, null);
        StepCollector.AddAssertionSubStep(testId, "total == 4173", passed: false,
            failureMessage: "Expected 4173 but found 3902", sourceFile: "OverviewTests.cs", sourceLine: 142);
        StepCollector.CompleteStep(testId, passed: false);

        var assertion = Assert.Single(Assert.Single(StepCollector.GetSteps(testId)).SubSteps!);

        Assert.Equal("Expected 4173 but found 3902", assertion.FailureMessage);
        Assert.Equal("OverviewTests.cs", assertion.SourceFile);
        Assert.Equal(142, assertion.SourceLine);
    }

    // ─── §2.4 Annotations ──────────────────────────────────────

    [Fact]
    public void Marker_records_declare_what_kind_of_marker_they_are()
    {
        var testId = Guid.NewGuid().ToString();

        StepCollector.StartStep(testId, "Given", "a basket", null, null);
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, "hnote across #lightyellow : Row 3", DiagramMarkerKind.Row);
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, "note over api : anything");

        // Scoped to this test's own id rather than isolated by clearing the store: the store is
        // process-wide, so clearing it is not this test's to do — it wipes whatever another test is
        // mid-way through asserting on.
        var kinds = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.IsOverrideStart && l.PlantUml is not null)
            .Select(l => l.MarkerKind)
            .ToArray();

        Assert.Equal([DiagramMarkerKind.Step, DiagramMarkerKind.Row, DiagramMarkerKind.Custom], kinds);
    }

    [Fact]
    public void Row_and_custom_markers_export_as_scenario_annotations()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"),
            Marker("t1", DiagramMarkerKind.Custom, "note over api : cache warmed"),
        };

        var annotations = Scenario(Write(Features(), "Attr_annotations.json", logs)).GetProperty("annotations");

        Assert.Equal(2, annotations.GetArrayLength());
        Assert.Equal("Row", annotations[0].GetProperty("kind").GetString());
        Assert.Equal("Row 3", annotations[0].GetProperty("text").GetString());
        Assert.Equal("Custom", annotations[1].GetProperty("kind").GetString());
        Assert.Equal("cache warmed", annotations[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Step_and_assertion_markers_are_not_repeated_as_annotations()
    {
        // They are already structured in `steps`; repeating them is duplication, not disclosure.
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>Given a basket"),
            Marker("t1", DiagramMarkerKind.Assertion, "hnote across <<assertionNote>> #green\n✓ ok\nend note"),
        };

        var scenario = Scenario(Write(Features(), "Attr_annotations_none.json", logs));

        Assert.Equal(0, scenario.GetProperty("annotations").GetArrayLength());
    }

    // ─── §2.5 Step ↔ interaction attribution ───────────────────

    [Fact]
    public void Interactions_are_stamped_with_the_step_they_happened_under()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Real("t1", "svc-b"),
            Real("t1", "svc-c"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath.json", logs));

        Assert.Equal("0", interactions[0].GetProperty("stepPath").GetString());
        Assert.Equal("1", interactions[1].GetProperty("stepPath").GetString());
        Assert.Equal("1", interactions[2].GetProperty("stepPath").GetString());
    }

    [Fact]
    public void Interactions_before_the_first_step_have_no_step_path()
    {
        var logs = new[]
        {
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Real("t1", "svc-b"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath_before.json", logs));

        Assert.Equal(JsonValueKind.Null, interactions[0].GetProperty("stepPath").ValueKind);
        Assert.Equal("0", interactions[1].GetProperty("stepPath").GetString());
    }

    [Fact]
    public void More_step_markers_than_steps_leaves_the_path_null_rather_than_guessing()
    {
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a basket"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a third"),
            Real("t1", "svc-a"),
        };

        var interactions = Interactions(Write(TwoStepFeature(), "Attr_steppath_overrun.json", logs));

        Assert.Equal(JsonValueKind.Null, Assert.Single(interactions).GetProperty("stepPath").ValueKind);
    }

    [Fact]
    public void A_mismatched_step_marker_is_reported_once_per_run_not_once_per_output()
    {
        // The data file and Failures.md both read the step attribution. Each used to derive it for itself,
        // and the derivation records the mismatch, so every run with one printed it twice.
        var testId = "attr-once-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.Log(Marker(testId, DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a third") with { Timestamp = Start });
        RequestResponseLogger.Log(Real(testId, "svc-a") with { Timestamp = Start.AddSeconds(1) });
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = testId, DisplayName = "S", Steps = [new ScenarioStep { Keyword = "Given", Text = "a basket" }] }],
            },
        ];
        var directory = Path.Combine(Path.GetTempPath(), "kronikol-attr-once-" + Guid.NewGuid().ToString("N"));
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = directory,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
        };
        var collector = new ReportDiagnosticsCollector();
        try
        {
            DefaultDiagramsFetcher.Reset();
            using (ReportDiagnosticsScope.Begin(collector))
                ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, Start, End, options, RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
            DefaultDiagramsFetcher.Reset();

            Assert.True(File.Exists(Path.Combine(directory, "Failures.md"))); // the digest, the second reader, ran
            Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.StepAttributionMismatch);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Background_steps_are_numbered_before_the_scenario_steps()
    {
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
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "logged in" }],
                        Steps = [new ScenarioStep { Keyword = "When", Text = "checkout" }]
                    }
                ]
            }
        };
        var logs = new[]
        {
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>logged in"),
            Real("t1", "svc-a"),
            Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>checkout"),
            Real("t1", "svc-b"),
        };

        var interactions = Interactions(Write(features, "Attr_steppath_background.json", logs));

        Assert.Equal("b0", interactions[0].GetProperty("stepPath").GetString());
        Assert.Equal("0", interactions[1].GetProperty("stepPath").GetString());
    }

    [Fact]
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_row_rebuilt_by_hand_among_collected_rows_keeps_its_calls(bool intoTheBackground)
    {
        // An adapter of a consumer's own: the collector's rows, with one rebuilt. It has no id, so its stamps name no row, and they
        // say nothing: the bar it drew and its text place its calls, as they did before anything was stamped.
        var testId = "attr-mixed-" + Guid.NewGuid().ToString("N");
        void Step(string keyword, string text, string service)
        {
            StepCollector.StartStep(testId, keyword, text, null, null);
            RequestResponseLogger.Log(Real(testId, service) with { Timestamp = Start.AddSeconds(1) });
            StepCollector.CompleteStep(testId, true);
        }

        Step("Given", "a shop", "shop");
        Step("When", "a basket", "basket");
        Step("Then", "it is empty", "check");
        var collected = StepCollector.GetSteps(testId);
        var scenario = new Scenario { Id = testId, DisplayName = "S" };
        if (intoTheBackground)
        {
            scenario.BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "a shop" }];
            scenario.Steps = [collected[1], collected[2]];
        }
        else
        {
            scenario.Steps = [collected[0], new ScenarioStep { Keyword = "When", Text = "a basket" }, collected[2]];
        }

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, [new Feature { DisplayName = "F", Scenarios = [scenario] }])[testId];

        Assert.Equal(intoTheBackground ? ["b0", "0", "1"] : ["0", "1", "2"], paths);
    }

    [Fact]
    public void A_stamp_that_names_no_row_reads_no_step()
    {
        // A hidden step, a later run of a step or a step still open at report time: the row before it did not make that call.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios = [new Scenario { Id = "t4", DisplayName = "S", Steps = [new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" }] }],
            }
        };
        var logs = new[]
        {
            Real("t4", "svc-a") with { OpenStep = 1 },
            Real("t4", "svc-b") with { OpenStep = 99 },
        };

        Assert.Equal(["0", null], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t4"]);
    }

    [Fact]
    public void A_row_that_did_not_run_leaves_the_others_read_by_their_stamps()
    {
        // A bar drawn for another step, so the walk would refuse it: only the stamp can place this call.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t5",
                        DisplayName = "S",
                        Steps =
                        [
                            new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" },
                            new ScenarioStep { DidNotRun = true, Keyword = "Then", Text = "it is empty" },
                        ],
                    },
                ],
            }
        };
        var logs = new[]
        {
            Marker("t5", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>a hook's step") with { OpenStep = 1 },
            Real("t5", "svc-a") with { OpenStep = 1 },
        };

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t5"]);
    }

    [Fact]
    public void A_stamped_call_reads_the_step_it_names_and_an_unstamped_one_the_step_before_it()
    {
        // The walk stays for what has no stamp: a call between steps, in any lane (P1 to P4, T20, B6).
        var features = Collected("t6", new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" }, new ScenarioStep { CollectedId = 2, Keyword = "When", Text = "checkout" });
        var logs = new[]
        {
            Real("t6", "before"),
            Bar("t6", "Given a basket", 1),
            Real("t6", "inside-given") with { OpenStep = 1 },
            Real("t6", "between"),
            Bar("t6", "When checkout", 2),
            Real("t6", "inside-when") with { OpenStep = 2 },
            Real("t6", "after"),
        };

        Assert.Equal([null, "0", "0", "1", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t6"]);
    }

    [Fact]
    public void A_bar_a_collected_step_drew_is_not_read_against_its_text()
    {
        var features = Collected("t7", new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" });
        var logs = new[]
        {
            Bar("t7", "a label nothing could match", 1),
            Real("t7", "between"),
        };

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t7"]);
    }

    [Fact]
    public void A_stamp_on_a_sub_step_reads_the_top_level_step_it_is_in()
    {
        var features = Collected("t8", new ScenarioStep
        {
            CollectedId = 1,
            Keyword = "Given",
            Text = "a basket",
            SubSteps = [new ScenarioStep { CollectedId = 2, Text = "An item", SubSteps = [new ScenarioStep { CollectedId = 3, Text = "Deeper" }] }],
        });
        var logs = new[]
        {
            Real("t8", "sub") with { OpenStep = 2 },
            Real("t8", "deeper") with { OpenStep = 3 },
        };

        Assert.Equal(["0", "0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t8"]);
    }

    [Fact]
    public void A_later_scenarios_stamp_reads_through_its_own_background_rows()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario { Id = "first", DisplayName = "A", BackgroundSteps = [new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "shared" }], Steps = [new ScenarioStep { CollectedId = 2, Keyword = "Then", Text = "a" }] },
                    new Scenario { Id = "later", DisplayName = "B", BackgroundSteps = [new ScenarioStep { CollectedId = 3, Keyword = "Given", Text = "shared" }], Steps = [new ScenarioStep { CollectedId = 4, Keyword = "Then", Text = "b" }] },
                ],
            }
        };
        var logs = new[]
        {
            Real("later", "in-background") with { OpenStep = 3 },
            Real("later", "in-own") with { OpenStep = 4 },
        };

        Assert.Equal(["b0", "0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["later"]);
    }

    [Fact]
    public void With_bars_hidden_a_stamped_call_keeps_its_step_and_a_call_after_it_the_same()
    {
        // ShowStepDelimiters = false is a display option, and it must not delete data (F10).
        var features = Collected("t9", new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" }, new ScenarioStep { CollectedId = 2, Keyword = "When", Text = "checkout" });
        var logs = new[]
        {
            Real("t9", "inside-given") with { OpenStep = 1 },
            Real("t9", "between"),
            Real("t9", "inside-when") with { OpenStep = 2 },
        };

        Assert.Equal(["0", "0", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t9"]);
    }

    [Fact]
    public void A_hook_steps_row_takes_a_call_by_its_stamp_and_no_step_moves()
    {
        // #223's own shape: a bar too many at the front used to lose every call after it.
        var features = Collected("t10",
            new ScenarioStep { CollectedId = 9, OutsideFrameworkStep = true, Text = "Reference data is seeded" },
            new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "a basket" });
        var logs = new[]
        {
            Bar("t10", "Reference data is seeded", 9),
            Real("t10", "seed") with { OpenStep = 9 },
            Bar("t10", "Given a basket", 1),
            Real("t10", "basket") with { OpenStep = 1 },
        };

        Assert.Equal(["0", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t10"]);
    }

    [Fact]
    public void The_stamps_are_never_read_where_every_row_lacks_an_id_as_an_ingest_or_a_hand_built_report_does()
    {
        var logs = new[]
        {
            Bar("t11", "Given a basket", 41),
            Real("t11", "a") with { OpenStep = 41 },
        };

        var features = WithId("t11", new ScenarioStep { Keyword = "Given", Text = "a basket" }, new ScenarioStep { Keyword = "When", Text = "checkout" });

        Assert.Equal(["0"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t11"]);
    }

    // ─── Helpers ───────────────────────────────────────────────

    // ─── The walk reads the bars Kronikol draws (plans/CALLS_UNDER_THEIR_STEP_PLAN.md R2, #128) ───

    [Fact]
    public void A_hand_built_scenario_with_a_long_label_keeps_each_calls_step()
    {
        // #128's own repro: "Given " and the first text pass 110 characters, so the bar wraps; "And " and the second do not.
        const string first = "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose";
        const string second = "a basket that held two apples and a pear last week but nothing this week, while the shop's prices rose";
        const string third = "the shopper opens the basket page";

        var (paths, mismatches) = Walk(
            [
                new ScenarioStep { Keyword = "Given", Text = first },
                new ScenarioStep { Keyword = "And", Text = second },
                new ScenarioStep { Keyword = "When", Text = third },
            ],
            $"Given {first}", $"And {second}", $"When {third}");

        Assert.Equal(["0", "1", "2"], paths);
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void A_row_with_no_keyword_keeps_its_step_under_a_bar_that_wrapped_it_and_has_one()
    {
        const string first = "a basket that held two apples and a pear last week but nothing at all this week, while the shop's prices rose";
        const string second = "the shopper opens the basket page";

        var (paths, mismatches) = Walk(
            [new ScenarioStep { Text = first }, new ScenarioStep { Text = second }],
            $"Given {first}", $"When {second}");

        Assert.Equal(["0", "1"], paths);
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void A_text_only_another_bars_tail_holds_is_read_on_a_plain_label_as_published_and_refused_once_escaped()
    {
        // A bar with no row comes first, so each row meets the bar after its own. Published's own loose check takes a plain tail
        // and only that: the bar is the row's only where it is what the bar draws for the row.
        var (plain, _) = Walk(
            [new ScenarioStep { Keyword = "When", Text = "we read the file" }, new ScenarioStep { Keyword = "And", Text = "the file" }],
            "Seed the shop", "When we read the file", "And the file");
        Assert.Equal([null, "1", null], plain);

        var path = @"C:\temp";
        var (escaped, mismatches) = Walk(
            [new ScenarioStep { Keyword = "When", Text = "we read " + path }, new ScenarioStep { Keyword = "And", Text = path }],
            "Seed the shop", "When we read " + path, "And " + path);
        Assert.Equal([null, null, null], escaped);
        Assert.Equal(2, mismatches);
    }

    /// <summary>One call under each bar, each drawn as a test's would be: what each call reads, and how many mismatches the walk recorded.</summary>
    private static (string?[] Paths, int Mismatches) Walk(ScenarioStep[] rows, params string[] labels)
    {
        var testId = "attr-walk-" + Guid.NewGuid().ToString("N");
        var logs = labels
            .SelectMany(label => new[] { Marker(testId, DiagramMarkerKind.Step, StepBarPlantUml.Build(label)), Real(testId, "svc") })
            .ToArray();
        Feature[] features = [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = testId, DisplayName = "S", Steps = rows }] }];

        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
        {
            var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, features)[testId].ToArray();
            return (paths, collector.Entries.Count(e => e.Kind == DiagnosticKind.StepAttributionMismatch));
        }
    }

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    private static RequestResponseLog Real(string testId, string service) =>
        new("Test", testId, HttpMethod.Get, null, new Uri("http://" + service + "/x"), [], service, "api",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

    private static Feature[] Features() =>
    [
        new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "t1", DisplayName = "S" }] }
    ];

    private static RequestResponseLog Bar(string testId, string label, int step) =>
        Marker(testId, DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>" + label) with { OpenStep = step };

    private static Feature[] Collected(string testId, params ScenarioStep[] steps) => WithId(testId, steps);

    private static Feature[] WithId(string testId, params ScenarioStep[] steps) =>
    [
        new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = testId, DisplayName = "S", Steps = steps }] }
    ];

    private static Feature[] TwoStepFeature() =>
    [
        new Feature
        {
            DisplayName = "F",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1", DisplayName = "S",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "a basket" },
                        new ScenarioStep { Keyword = "When", Text = "checkout" }
                    ]
                }
            ]
        }
    ];

    private static Feature[] WithSteps(params ScenarioStep[] steps) =>
    [
        new Feature
        {
            DisplayName = "F",
            Scenarios = [new Scenario { Id = "t1", DisplayName = "S", Steps = steps }]
        }
    ];

    private static string Write(Feature[] features, string fileName, RequestResponseLog[]? logs = null) =>
        ReportGenerator.GenerateTestRunReportData(features, Start, End, fileName, DataFormat.Json, trackedLogs: logs);

    private static JsonElement Scenario(string path) =>
        JsonDocument.Parse(ReportHtml.Read(path)).RootElement
            .GetProperty("features")[0].GetProperty("scenarios")[0];

    private static JsonElement FirstStep(string path) => Scenario(path).GetProperty("steps")[0];

    private static JsonElement[] Interactions(string path) =>
        Scenario(path).GetProperty("httpInteractions").EnumerateArray().ToArray();

    // A call held until a later step confirms its document reads the step it was logged under, and moves no step: what follows it reads as it would have.
    [Fact]
    public void A_held_call_keeps_its_step_and_what_follows_it_reads_the_step_before_it()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t5",
                        DisplayName = "S",
                        Steps =
                        [
                            new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "one" },
                            new ScenarioStep { CollectedId = 2, Keyword = "When", Text = "two" },
                        ],
                    }
                ],
            }
        };
        var logs = new[]
        {
            Real("t5", "first") with { OpenStep = 1 },
            Real("t5", "second") with { OpenStep = 2 },
            Real("t5", "held") with { OpenStep = 1 },
            Real("t5", "confirming"),
            Real("t5", "between"),
        };

        Assert.Equal(["0", "1", "0", "1", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t5"]);
    }

    // The bar moved the walk on with no call in its step, and the held call names a sub-step of the row before it.
    [Fact]
    public void A_held_call_moves_nothing_past_a_bar_with_no_call_under_it()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t5b",
                        DisplayName = "S",
                        Steps =
                        [
                            new ScenarioStep { CollectedId = 1, Keyword = "Given", Text = "one", SubSteps = [new ScenarioStep { CollectedId = 2, Text = "under one" }] },
                            new ScenarioStep { CollectedId = 3, Keyword = "When", Text = "two" },
                        ],
                    }
                ],
            }
        };
        var logs = new[]
        {
            Real("t5b", "first") with { OpenStep = 1 },
            Marker("t5b", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>When two") with { OpenStep = 3 },
            Real("t5b", "held") with { OpenStep = 2 },
            Real("t5b", "between"),
        };

        Assert.Equal(["0", "0", "1"], ReportGenerator.AttributeInteractionsToStepPaths(logs, features)["t5b"]);
    }
}
