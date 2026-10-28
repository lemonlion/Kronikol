using LightBDD.Core.Results;
using LightBDD.Core.Formatting.NameDecorators;
using LightBDD.Core.Metadata;
using LightBDD.Core.Results.Parameters;
using LightBDD.Core.Results.Parameters.Tabular;
using LightBDD.Framework.Reporting;
using Kronikol.LightBDD;
using Kronikol.Reports;
using Kronikol.Tracking;
using FileAttachment = LightBDD.Core.Results.FileAttachment;

namespace Kronikol.Tests.LightBDD;

[Collection("DiagramsFetcher")]
public class FeatureResultExtensionsTests
{
    [Fact]
    public void ToFeatures_maps_feature_name_and_description()
    {
        var feature = new StubFeatureResult("OrderService", "Handles orders")
            .WithScenario(new StubExecutionResult("s1", "Place order"));

        var features = new[] { feature }.ToFeatures();

        Assert.Single(features);
        Assert.Equal("Order Service", features[0].DisplayName);
        Assert.Equal("Handles orders", features[0].Description);
    }

    [Fact]
    public void ToFeatures_carries_when_the_scenario_ended()
    {
        var result = new StubExecutionResult("s1", "Place order", duration: TimeSpan.FromSeconds(2));
        var feature = new StubFeatureResult("OrderService").WithScenario(result);

        var features = new[] { feature }.ToFeatures();

        Assert.Equal(result.ExecutionTime!.End, features[0].Scenarios[0].EndedAt);
    }

    [Fact]
    public void ToFeatures_leaves_the_end_null_when_the_scenario_never_ran()
    {
        var feature = new StubFeatureResult("OrderService").WithScenario(new StubExecutionResult("s1", "Place order"));

        var features = new[] { feature }.ToFeatures();

        Assert.Null(features[0].Scenarios[0].EndedAt);
    }

    [Fact]
    public void ToFeatures_maps_the_feature_s_labels_to_its_tags()
    {
        var feature = new StubFeatureResult("Svc", labels: ["api", "v2"])
            .WithScenario(new StubExecutionResult("s1", "Test"));

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(["api", "v2"], features[0].Tags);
        Assert.Null(features[0].Scenarios[0].Tags);
    }

    [Fact]
    public void ToFeatures_maps_a_scenario_s_labels_and_categories_to_its_tags()
    {
        var scenario = new StubExecutionResult("s1", "Test", labels: ["smoke"], categories: ["Orders", "SMOKE"]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(["smoke", "Orders"], features[0].Scenarios[0].Tags);
        Assert.Null(features[0].Tags);
    }

    [Fact]
    public void HappyPath_sets_the_flag_and_is_not_a_tag()
    {
        // [HappyPath] is the label Happy Path (HappyPathAttribute), which 4.x also drew as a label chip (F11)
        var scenario = new StubExecutionResult("s1", "Test", labels: ["Happy Path", "smoke"]);

        var mapped = new[] { new StubFeatureResult("F").WithScenario(scenario) }.ToFeatures()[0].Scenarios[0];

        Assert.True(mapped.IsHappyPath);
        Assert.Equal(["smoke"], mapped.Tags);
    }

    [Fact]
    public void A_scenario_label_that_repeats_its_feature_s_in_another_case_is_the_feature_s()
    {
        var scenario = new StubExecutionResult("s1", "Test", labels: ["API", "smoke"]);

        var mapped = new[] { new StubFeatureResult("F", labels: ["api"]).WithScenario(scenario) }.ToFeatures()[0];

        Assert.Equal(["api"], mapped.Tags);
        Assert.Equal(["smoke"], mapped.Scenarios[0].Tags);
    }

    [Fact]
    public void A_category_every_scenario_carries_stays_on_each_scenario()
    {
        // Q8: a class-level [ScenarioCategory] reaches every scenario with no level, and LightBDD does not say where it
        // came from, so it is not lifted to the feature
        var feature = new StubFeatureResult("F")
            .WithScenario(new StubExecutionResult("s1", "One", categories: ["Orders"]))
            .WithScenario(new StubExecutionResult("s2", "Two", categories: ["Orders"]));

        var mapped = new[] { feature }.ToFeatures()[0];

        Assert.Null(mapped.Tags);
        Assert.All(mapped.Scenarios, s => Assert.Equal(["Orders"], s.Tags));
    }

    [Fact]
    public void ToFeatures_maps_scenario_status()
    {
        var scenario = new StubExecutionResult("s1", "Test", status: ExecutionStatus.Failed, statusDetails: "Expected 200");
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Result);
        Assert.Equal("Expected 200", features[0].Scenarios[0].ErrorMessage);
    }

    [Fact]
    public void ToFeatures_maps_scenario_duration()
    {
        var scenario = new StubExecutionResult("s1", "Test", duration: TimeSpan.FromSeconds(2));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(TimeSpan.FromSeconds(2), features[0].Scenarios[0].Duration);
    }

    [Fact]
    public void ToFeatures_maps_steps_with_keyword_and_text()
    {
        var scenario = new StubExecutionResult("s1", "Test")
            .WithStep(new StubStepResult("Given", "a valid request", ExecutionStatus.Passed))
            .WithStep(new StubStepResult("When", "the request is sent", ExecutionStatus.Passed))
            .WithStep(new StubStepResult("Then", "the response is 200", ExecutionStatus.Passed));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var steps = features[0].Scenarios[0].Steps;
        Assert.NotNull(steps);
        Assert.Equal(3, steps!.Length);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("a valid request", steps[0].Text);
    }

    [Fact]
    public void ToFeatures_maps_step_status_and_duration()
    {
        var scenario = new StubExecutionResult("s1", "Test")
            .WithStep(new StubStepResult("Given", "x", ExecutionStatus.Failed, duration: TimeSpan.FromMilliseconds(500)));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];
        Assert.Equal(ExecutionResult.Failed, step.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(500), step.Duration);
    }

    [Fact]
    public void ToFeatures_maps_substeps()
    {
        var subStep = new StubStepResult("And", "the body has milk", ExecutionStatus.Passed);
        var parentStep = new StubStepResult("Given", "a valid request body", ExecutionStatus.Passed)
            .WithSubStep(subStep);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(parentStep);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];
        Assert.NotNull(step.SubSteps);
        Assert.Single(step.SubSteps!);
        Assert.Equal("the body has milk", step.SubSteps![0].Text);
    }

    [Fact]
    public void ToFeatures_maps_step_comments()
    {
        var step = new StubStepResult("Then", "validate", ExecutionStatus.Passed, comments: ["Business rule XYZ"]);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(step);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(["Business rule XYZ"], features[0].Scenarios[0].Steps![0].Comments!);
    }

    [Fact]
    public void ToFeatures_maps_file_attachments_to_the_file_LightBDD_wrote()
    {
        // The shape LightBDD's FileAttachmentsManager writes: FilePath absolute, RelativePath the bare file name, relative
        // to the manager's own folder. The bare name was mapped, and the copier looked for it in the binary folder
        // (#148, plans/MISSING_ATTACHMENTS_PLAN.md F3).
        var filePath = Path.Combine(Path.GetTempPath(), "lightbdd", "0f8fad5b-d9cb-469f-a165-70867728950e.png");
        var attachment = new FileAttachment("screenshot", filePath, "0f8fad5b-d9cb-469f-a165-70867728950e.png");
        var step = new StubStepResult("Then", "verify", ExecutionStatus.Passed, attachments: [attachment]);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(step);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var att = features[0].Scenarios[0].Steps![0].Attachments;
        Assert.NotNull(att);
        Assert.Equal("screenshot", att![0].Name);
        Assert.Equal(filePath, att[0].RelativePath);
    }

    [Fact]
    public void ToFeatures_maps_the_relative_path_of_an_attachment_with_no_file_path()
    {
        var attachment = new FileAttachment("screenshot", "", "Reports/shot.png");
        var step = new StubStepResult("Then", "verify", ExecutionStatus.Passed, attachments: [attachment]);
        var feature = new StubFeatureResult("F").WithScenario(new StubExecutionResult("s1", "Test").WithStep(step));

        Assert.Equal("Reports/shot.png", new[] { feature }.ToFeatures()[0].Scenarios[0].Steps![0].Attachments![0].RelativePath);
    }

    // ─── #148: attachments LightBDD's own manager wrote, through a whole run (F3, F21) ───────────

    private static readonly DateTime LightBddRunStart = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>An attachment made the way <c>StepExecution.Current.AttachFile</c> makes one: by LightBDD's own manager.</summary>
    private static FileAttachment LightBddAttachment(string folder, string name = "probe-screenshot") =>
        new FileAttachmentsManager(folder).CreateFromData(name, "png", [137, 80, 78, 71, 13, 10, 26, 10]).GetAwaiter().GetResult();

    private static Kronikol.Reports.Feature[] LightBddFeatures(FileAttachment attachment) =>
        new[]
        {
            new StubFeatureResult("Attachments").WithScenario(new StubExecutionResult("s1", "A step attaches a screenshot")
                .WithStep(new StubStepResult("Then", "it attaches a screenshot", ExecutionStatus.Passed, attachments: [attachment]))),
        }.ToFeatures();

    /// <summary>A run of <paramref name="features"/> into <paramref name="reports"/>, as LightBDD's formatter hands it over.</summary>
    private static IReadOnlyList<DiagnosticEntry> LightBddRun(Kronikol.Reports.Feature[] features, string reports, string runId)
    {
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = reports,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
            HistoryFilePath = Path.Combine(Path.GetDirectoryName(reports)!, "history.ndjson"),
            HistoryRunId = runId,
            HistoryBranch = "",
            KeepRuns = 3,
        };
        var collector = new ReportDiagnosticsCollector();
        DefaultDiagramsFetcher.Reset();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(features, LightBddRunStart, LightBddRunStart.AddMinutes(1), options, RunEnvironment.Unrecorded, _ => null);
        DefaultDiagramsFetcher.Reset();
        return collector.Entries;
    }

    private static Kronikol.Reports.FileAttachment OnlyAttachment(Kronikol.Reports.Feature[] features) =>
        features[0].Scenarios[0].Steps![0].Attachments!.Single();

    private static string NewFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "kronikol-lightbdd-attachments-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void A_LightBDD_attachment_in_the_documented_setup_is_copied_listed_and_kept_with_its_run()
    {
        // The documented setup: LightBDD's default folder, ~\Reports, is the report's own, so the bare name linked by
        // accident while the file was never copied, Run.json listed no attachment, and a kept run's link was dead.
        var dir = NewFolder();
        try
        {
            var reports = Path.Combine(dir, "Reports");
            var first = LightBddAttachment(reports);
            var firstName = Path.GetFileName(first.FilePath);
            var features = LightBddFeatures(first);

            LightBddRun(features, reports, "run-1");

            Assert.Equal("attachments/" + firstName, OnlyAttachment(features).RelativePath);
            Assert.True(File.Exists(Path.Combine(reports, "attachments", firstName)));
            Assert.Contains("attachments/" + firstName, RunManifest.TryRead(Path.Combine(reports, RunManifest.FileName))!.Attachments);

            // The next run, in a process of its own: a process rotates a folder only the first time it writes there.
            RunRotation.ForgetForTests(reports);
            LightBddRun(LightBddFeatures(LightBddAttachment(reports)), reports, "run-2");

            var kept = Path.Combine(reports, "runs", "run-1");
            Assert.Contains($"src=\"attachments/{firstName}\"", ReportHtml.Read(Path.Combine(kept, "TestRunReport.html")), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(kept, "attachments", firstName)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_LightBDD_attachment_written_to_a_folder_of_its_own_is_copied_into_the_report()
    {
        var dir = NewFolder();
        try
        {
            var reports = Path.Combine(dir, "Reports");
            var attachment = LightBddAttachment(Path.Combine(dir, "lightbdd-attachments"));
            var features = LightBddFeatures(attachment);

            LightBddRun(features, reports, "run-1");

            var name = Path.GetFileName(attachment.FilePath);
            Assert.Equal("attachments/" + name, OnlyAttachment(features).RelativePath);
            Assert.True(File.Exists(Path.Combine(reports, "attachments", name)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_LightBDD_image_named_without_an_extension_draws_as_an_image()
    {
        var dir = NewFolder();
        try
        {
            // LightBDD names carry no extension and the mapping passes no media type: the file's own extension decides.
            Assert.True(OnlyAttachment(LightBddFeatures(LightBddAttachment(dir, "probe-screenshot"))).IsInlineImage);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void A_run_with_LightBDD_attachments_records_no_attachment_failure()
    {
        // Guards the order of plans/MISSING_ATTACHMENTS_PLAN.md R1 and R2: had the "not found" entry shipped without the
        // mapping, every LightBDD attachment would have been reported missing while its link worked.
        var dir = NewFolder();
        try
        {
            var reports = Path.Combine(dir, "Reports");

            var entries = LightBddRun(LightBddFeatures(LightBddAttachment(reports)), reports, "run-1");

            Assert.DoesNotContain(entries, e => e.Kind == DiagnosticKind.AttachmentFailure);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ToFeatures_maps_bypassed_status()
    {
        var s1 = new StubExecutionResult("s1", "Bypassed", status: ExecutionStatus.Bypassed);
        var feature = new StubFeatureResult("F").WithScenario(s1);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Bypassed, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_ignored_scenario_to_skipped()
    {
        var s1 = new StubExecutionResult("s1", "Ignored", status: ExecutionStatus.Ignored);
        var feature = new StubFeatureResult("F").WithScenario(s1);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_not_run_to_skipped()
    {
        var s1 = new StubExecutionResult("s1", "NotRun", status: ExecutionStatus.NotRun);
        var feature = new StubFeatureResult("F").WithScenario(s1);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Result);
    }

    [Fact]
    public void ToFeatures_maps_not_run_steps_to_skipped_when_scenario_is_skipped()
    {
        var scenario = new StubExecutionResult("s1", "Test", status: ExecutionStatus.Ignored)
            .WithStep(new StubStepResult("Given", "a precondition", ExecutionStatus.NotRun))
            .WithStep(new StubStepResult("When", "an action", ExecutionStatus.NotRun))
            .WithStep(new StubStepResult("Then", "an assertion", ExecutionStatus.NotRun));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.All(features[0].Scenarios[0].Steps!, step =>
            Assert.Equal(ExecutionResult.Skipped, step.Status));
    }

    [Fact]
    public void ToFeatures_maps_ignored_step_to_skipped_when_no_prior_failure()
    {
        var scenario = new StubExecutionResult("s1", "Test")
            .WithStep(new StubStepResult("Given", "a valid request", ExecutionStatus.Passed))
            .WithStep(new StubStepResult("When", "something is ignored", ExecutionStatus.Ignored));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Skipped, features[0].Scenarios[0].Steps![1].Status);
    }

    [Fact]
    public void ToFeatures_maps_ignored_step_to_skipped_after_failure_when_prior_step_failed()
    {
        var scenario = new StubExecutionResult("s1", "Test")
            .WithStep(new StubStepResult("Given", "a failing step", ExecutionStatus.Failed))
            .WithStep(new StubStepResult("When", "this was ignored", ExecutionStatus.Ignored))
            .WithStep(new StubStepResult("Then", "this was also ignored", ExecutionStatus.Ignored));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(ExecutionResult.Failed, features[0].Scenarios[0].Steps![0].Status);
        Assert.Equal(ExecutionResult.SkippedAfterFailure, features[0].Scenarios[0].Steps![1].Status);
        Assert.Equal(ExecutionResult.SkippedAfterFailure, features[0].Scenarios[0].Steps![2].Status);
    }

    [Fact]
    public void ToFeatures_leaves_steps_null_when_no_steps()
    {
        var scenario = new StubExecutionResult("s1", "Test");
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].Steps);
    }

    [Fact]
    public void ToFeatures_maps_error_stack_trace_from_failed_step_exception()
    {
        Exception capturedException;
        try { throw new InvalidOperationException("Something went wrong"); }
        catch (Exception ex) { capturedException = ex; }

        var step = new StubStepResult("When", "an action fails", ExecutionStatus.Failed,
            executionException: capturedException);
        var scenario = new StubExecutionResult("s1", "Test", status: ExecutionStatus.Failed, statusDetails: "Something went wrong")
            .WithStep(step);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.NotNull(features[0].Scenarios[0].ErrorStackTrace);
        Assert.Contains("Something went wrong", features[0].Scenarios[0].ErrorMessage!);
    }

    [Fact]
    public void ToFeatures_maps_error_stack_trace_from_nested_failed_substep()
    {
        Exception capturedException;
        try { throw new InvalidOperationException("Nested failure"); }
        catch (Exception ex) { capturedException = ex; }

        var subStep = new StubStepResult("And", "a nested failing action", ExecutionStatus.Failed,
            executionException: capturedException);
        var parentStep = new StubStepResult("When", "an action", ExecutionStatus.Failed)
            .WithSubStep(subStep);
        var scenario = new StubExecutionResult("s1", "Test", status: ExecutionStatus.Failed, statusDetails: "Nested failure")
            .WithStep(parentStep);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.NotNull(features[0].Scenarios[0].ErrorStackTrace);
        Assert.Contains("ToFeatures_maps_error_stack_trace_from_nested_failed_substep", features[0].Scenarios[0].ErrorStackTrace!);
    }

    [Fact]
    public void ToFeatures_leaves_error_stack_trace_null_when_scenario_passed()
    {
        var scenario = new StubExecutionResult("s1", "Test", status: ExecutionStatus.Passed);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        Assert.Null(features[0].Scenarios[0].ErrorStackTrace);
    }

    // ─── Feature name titleization ────────────────────────────────

    [Theory]
    [InlineData("OrderService", "Order Service")]
    [InlineData("AlternativeEvidenceScenarios", "Alternative Evidence Scenarios")]
    [InlineData("Alternative Evidence Scenarios", "Alternative Evidence Scenarios")]
    [InlineData("my_feature_name", "My Feature Name")]
    // LightBDD has already given Customer_s_Orders its apostrophe, which is kept (5.9.1, plans/REPORT_UI_FIXES_PLAN.md
    // N2; it was "Customer S Orders")
    [InlineData("Customer's Orders", "Customer's Orders")]
    public void ToFeatures_titleizes_feature_display_name(string featureName, string expected)
    {
        var feature = new StubFeatureResult(featureName)
            .WithScenario(new StubExecutionResult("s1", "Test"));

        var features = new[] { feature }.ToFeatures();
        Assert.Equal(expected, features[0].DisplayName);
    }

    // N3: a feature whose name Titleize now shows differently keeps the ids 5.9.0 gave its scenarios
    [Theory]
    [InlineData("Customer's Orders", "Customer S Orders")]
    [InlineData("OrderService", "Order Service")]
    public void A_feature_shown_as_written_keeps_its_scenarios_ids(string featureName, string shownBy590)
    {
        var feature = new[] { new StubFeatureResult(featureName).WithScenario(new StubExecutionResult("s1", "Test")) }.ToFeatures()[0];
        var scenario = feature.Scenarios[0];
        Assert.Equal(ScenarioStableId.For("suite", shownBy590, scenario), ScenarioStableId.For("suite", feature, scenario));
    }

    // ─── Parameter extraction from NameFormat ─────────────────────

    [Fact]
    public void ToFeatures_extracts_inline_and_bracket_params_from_NameFormat()
    {
        // Simulates LightBDD NameFormat for:
        //   Method: Caller_Uses_Endpoint_With_Invalid_NewPasscode(string newPasscode, string reasonForBeingInvalid)
        //   newPasscode matched inline (case-insensitive), reasonForBeingInvalid appended as bracket
        var scenario = new StubExecutionResult("s1",
            nameFormat: "Caller Uses Endpoint With Invalid NewPasscode \"{0}\" [reasonForBeingInvalid: \"{1}\"]",
            nameParams: [new StubNameParam("111111"), new StubNameParam("TooShort")]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        // OutlineId should be the clean base name without parameter values
        Assert.Equal("Caller Uses Endpoint With Invalid NewPasscode", s.OutlineId);

        // ExampleValues should contain both parameters
        Assert.NotNull(s.ExampleValues);
        Assert.Equal(2, s.ExampleValues!.Count);
        Assert.Equal("111111", s.ExampleValues["NewPasscode"]);
        Assert.Equal("TooShort", s.ExampleValues["reasonForBeingInvalid"]);
    }

    [Fact]
    public void An_outline_with_inline_values_is_headed_as_LightBDD_wrote_it()
    {
        // B4 (plans/TITLES_AS_WRITTEN_PLAN.md): its rows' names hold their values inline, so they share no name, and 5.16.4
        // headed the group by humanising the outline id again: "Caller uses endpoint with invalid new passcode"
        StubExecutionResult Row(string id, string passcode) => new(id,
            nameFormat: "Caller Uses Endpoint With Invalid NewPasscode \"{0}\" [reasonForBeingInvalid: \"{1}\"]",
            nameParams: [new StubNameParam(passcode), new StubNameParam("TooShort")]);
        var feature = new[] { new StubFeatureResult("F").WithScenario(Row("s1", "111111")).WithScenario(Row("s2", "222222")) }.ToFeatures()[0];

        Assert.All(feature.Scenarios, s => Assert.Equal("Caller Uses Endpoint With Invalid NewPasscode", s.OutlineTitle));
        Assert.Equal("Caller Uses Endpoint With Invalid NewPasscode",
            Assert.Single(ParameterGrouper.Analyze(feature.Scenarios).Groups).GroupDisplayName);
    }

    [Fact]
    public void ToFeatures_extracts_all_bracket_params_from_NameFormat()
    {
        // Both params unmatched in method name — both appended as brackets
        var scenario = new StubExecutionResult("s1",
            nameFormat: "Caller Uses Endpoint [version: \"{0}\"] [claimName: \"{1}\"]",
            nameParams: [new StubNameParam("V1"), new StubNameParam("LivePersonSdes")]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        Assert.Equal("Caller Uses Endpoint", s.OutlineId);
        Assert.NotNull(s.ExampleValues);
        Assert.Equal("V1", s.ExampleValues!["version"]);
        Assert.Equal("LivePersonSdes", s.ExampleValues["claimName"]);
    }

    [Fact]
    public void ToFeatures_handles_no_params_in_NameFormat()
    {
        var scenario = new StubExecutionResult("s1", "Simple scenario with no params");
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        Assert.Null(s.OutlineId);
        Assert.Null(s.ExampleValues);
    }

    [Fact]
    public void ToFeatures_groups_parameterized_LightBDD_scenarios_by_OutlineId()
    {
        var s1 = new StubExecutionResult("s1",
            nameFormat: "Caller Uses Endpoint With Invalid NewPasscode \"{0}\" [reason: \"{1}\"]",
            nameParams: [new StubNameParam("111111"), new StubNameParam("TooShort")]);
        var s2 = new StubExecutionResult("s2",
            nameFormat: "Caller Uses Endpoint With Invalid NewPasscode \"{0}\" [reason: \"{1}\"]",
            nameParams: [new StubNameParam("1234"), new StubNameParam("TooLong")]);
        var feature = new StubFeatureResult("F").WithScenario(s1).WithScenario(s2);

        var features = new[] { feature }.ToFeatures();
        // Both scenarios should have the same OutlineId
        Assert.Equal(features[0].Scenarios[0].OutlineId, features[0].Scenarios[1].OutlineId);
        Assert.Equal("Caller Uses Endpoint With Invalid NewPasscode", features[0].Scenarios[0].OutlineId);
    }

    [Fact]
    public void MapStep_populates_TextSegments_from_NameFormat_with_params()
    {
        // NameFormat: "Given customer has \"{0}\" in account" with keyword "Given"
        var stepResult = new StubStepResult("Given", "Given customer has \"{0}\" in account",
            [new StubNameParam("105", ParameterVerificationStatus.Success)],
            ExecutionStatus.Passed)
            .WithParameters(new StubParameterResult("amount", new StubInlineDetails("105", null)));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        Assert.Equal(3, step.TextSegments!.Length);
        Assert.Equal("customer has ", step.TextSegments[0].Text);
        Assert.NotNull(step.TextSegments[1].Parameter);
        Assert.Equal("105", step.TextSegments[1].Parameter!.Value);
        Assert.Equal(VerificationStatus.Success, step.TextSegments[1].Parameter!.Status);
        Assert.Equal("amount", step.TextSegments[1].ParameterName);
        Assert.Equal(" in account", step.TextSegments[2].Text);
    }

    [Fact]
    public void MapStep_TextSegments_null_when_no_name_params()
    {
        var stepResult = new StubStepResult("Given", "customer logs in", ExecutionStatus.Passed);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.Null(step.TextSegments);
    }

    [Fact]
    public void MapStep_TextSegments_maps_expectation_from_inline_parameter_details()
    {
        var stepResult = new StubStepResult("Then", "Then balance is \"{0}\"",
            [new StubNameParam("200", ParameterVerificationStatus.Failure)],
            ExecutionStatus.Failed)
            .WithParameters(new StubParameterResult("balance", new StubInlineDetails("200", "300")));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        var paramSeg = step.TextSegments!.First(s => s.Parameter != null);
        Assert.Equal("200", paramSeg.Parameter!.Value);
        Assert.Equal("300", paramSeg.Parameter!.Expectation);
        Assert.Equal(VerificationStatus.Failure, paramSeg.Parameter!.Status);
    }

    [Fact]
    public void MapStep_TextSegments_emits_TableRef_for_bracket_params()
    {
        // NameFormat: "Then step verifies \"{0}\" [items: \"{1}\"]"
        // Param 0 is inline, param 1 is bracket (tabular)
        var stepResult = new StubStepResult("Then", "Then step verifies \"{0}\" [items: \"{1}\"]",
            [new StubNameParam("OK", ParameterVerificationStatus.Success), new StubNameParam("<$items>")],
            ExecutionStatus.Passed)
            .WithParameters(
                new StubParameterResult("result", new StubInlineDetails("OK", null)),
                new StubParameterResult("items", new StubTabularDetails()));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        // Should have: literal "step verifies " + param "OK" + TableRef "items"
        var tableRef = step.TextSegments!.FirstOrDefault(s => s.TableReference != null);
        Assert.NotNull(tableRef);
        Assert.Equal("items", tableRef!.TableReference);
        Assert.Equal("<$items>", tableRef.TableReferenceFormattedValue);
    }

    [Fact]
    public void MapStep_TextSegments_no_TableRef_when_no_bracket_params()
    {
        var stepResult = new StubStepResult("Given", "Given customer has \"{0}\" in account",
            [new StubNameParam("105", ParameterVerificationStatus.Success)],
            ExecutionStatus.Passed)
            .WithParameters(new StubParameterResult("amount", new StubInlineDetails("105", null)));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        Assert.DoesNotContain(step.TextSegments!, s => s.TableReference != null);
    }

    [Fact]
    public void MapStep_TextSegments_emits_TableRef_for_complex_object_param()
    {
        // FormattedValue is a complex record-style ToString — should emit TableRef, not Param
        var complexValue = "MuffinRecipeTestData { Name = Classic, Flour = Plain Flour, Temperature = 180 }";
        var stepResult = new StubStepResult("Given", "Given a muffin recipe \"{0}\"",
            [new StubNameParam(complexValue, ParameterVerificationStatus.NotApplicable)],
            ExecutionStatus.Passed)
            .WithParameters(new StubParameterResult("recipe", new StubInlineDetails(complexValue, null)));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        // Should have: literal "a muffin recipe " + TableRef "recipe"
        var tableRef = step.TextSegments!.FirstOrDefault(s => s.TableReference != null);
        Assert.NotNull(tableRef);
        Assert.Equal("recipe", tableRef!.TableReference);
        // Should NOT have a Param segment with the full complex value
        Assert.DoesNotContain(step.TextSegments!, s => s.Parameter?.Value == complexValue);
    }

    [Fact]
    public void MapStep_TextSegments_keeps_Param_for_scalar_values()
    {
        // Scalar FormattedValue should still emit Param segment, not TableRef
        var stepResult = new StubStepResult("Given", "Given customer has \"{0}\" in account",
            [new StubNameParam("105", ParameterVerificationStatus.Success)],
            ExecutionStatus.Passed)
            .WithParameters(new StubParameterResult("amount", new StubInlineDetails("105", null)));

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);
        // Should have a Param segment, not a TableRef
        Assert.Contains(step.TextSegments!, s => s.Parameter?.Value == "105");
        Assert.DoesNotContain(step.TextSegments!, s => s.TableReference != null);
    }

    [Fact]
    public void MapStep_TextSegments_bracket_params_carry_FormattedValue()
    {
        // NameFormat: "A client valid for authenticated authorisation requests [grantTypes: \"{0}\"] [scopes: \"{1}\"]"
        // Both params are bracket-appended (no inline placeholders in the base text)
        var stepResult = new StubStepResult("Given",
            "Given A client valid for authenticated authorisation requests [grantTypes: \"{0}\"] [scopes: \"{1}\"]",
            [new StubNameParam("client_credentials"), new StubNameParam("openid, profile")],
            ExecutionStatus.Passed);

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);

        var tableRefs = step.TextSegments!.Where(s => s.TableReference != null).ToArray();
        Assert.Equal(2, tableRefs.Length);

        Assert.Equal("grantTypes", tableRefs[0].TableReference);
        Assert.Equal("client_credentials", tableRefs[0].TableReferenceFormattedValue);

        Assert.Equal("scopes", tableRefs[1].TableReference);
        Assert.Equal("openid, profile", tableRefs[1].TableReferenceFormattedValue);
    }

    [Fact]
    public void MapStep_TextSegments_bracket_params_have_space_before_each_TableRef()
    {
        // NameFormat: "requests [grantTypes: \"{0}\"] [scopes: \"{1}\"]"
        // After stripping bracket params, literal is "requests" — a space must appear before each TableRef
        var stepResult = new StubStepResult("Given",
            "Given requests [grantTypes: \"{0}\"] [scopes: \"{1}\"]",
            [new StubNameParam("client_credentials"), new StubNameParam("openid")],
            ExecutionStatus.Passed);

        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.NotNull(step.TextSegments);

        // Verify the segments flow correctly with spaces
        // Expected: Literal("requests") + Literal(" ") + TableRef("grantTypes") + Literal(" ") + TableRef("scopes")
        // OR: Literal("requests ") + TableRef(...) + Literal(" ") + TableRef(...)
        // Key assertion: the rendered text should NOT concatenate without spaces
        var rendered = string.Join("", step.TextSegments!.Select(s =>
            s.Text ?? s.TableReference ?? ""));
        Assert.DoesNotContain("requestsgrantTypes", rendered);
        Assert.DoesNotContain("grantTypesscopes", rendered);

        // There should be space before each TableRef
        for (var i = 0; i < step.TextSegments!.Length; i++)
        {
            if (step.TextSegments[i].TableReference != null && i > 0)
            {
                var prev = step.TextSegments[i - 1];
                Assert.True(prev.Text != null && prev.Text.EndsWith(" "),
                    $"Segment before TableRef '{step.TextSegments[i].TableReference}' should end with space, but was '{prev.Text}'");
            }
        }
    }

    [Theory]
    [InlineData("user.read")]
    [InlineData("User.Read")]
    [InlineData("api.user.read")]
    [InlineData("Microsoft.Graph.User.Read.All")]
    [InlineData("System.Net.HttpStatusCode")]
    public void MapStep_preserves_quoted_dotted_string_values(string paramValue)
    {
        // Quoted dotted values are string literals — they must NEVER be namespace-stripped
        var stepResult = new StubStepResult("Given",
            $"Given the client has scopes of \"{paramValue}\"",
            ExecutionStatus.Passed);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.Contains(paramValue, step.Text);
    }

    [Theory]
    [InlineData("http://idp.sitint-newdaycards.com")]
    [InlineData("https://api.example.com/v1/users")]
    [InlineData("System.Net.HttpStatusCode")]
    [InlineData("user.read")]
    [InlineData("config.setting.value")]
    public void MapStep_preserves_unquoted_dotted_values_in_text(string paramValue)
    {
        // Unquoted parameter values (e.g. from LightBDD VALUE placeholders) must be preserved.
        // Namespace stripping must NOT be applied to the full step text because it cannot
        // distinguish namespace-qualified names from URLs, scopes, or config values.
        var stepResult = new StubStepResult("Then",
            "issuer should be " + paramValue,
            ExecutionStatus.Passed);
        var scenario = new StubExecutionResult("s1", "Test").WithStep(stepResult);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var step = features[0].Scenarios[0].Steps![0];

        Assert.Equal("issuer should be " + paramValue, step.Text);
    }

    [Fact]
    public void MapScenario_preserves_namespace_qualified_example_values()
    {
        // Example values can't distinguish type names from dotted strings,
        // so all dotted values are preserved (no namespace stripping)
        var scenario = new StubExecutionResult("s1",
            "The status is \"{0}\"",
            [new StubNameParam("System.Net.HttpStatusCode")]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        Assert.NotNull(s.ExampleValues);
        Assert.Contains(s.ExampleValues!, kv => kv.Value == "System.Net.HttpStatusCode");
    }

    [Theory]
    [InlineData("User.Read")]
    [InlineData("user.read")]
    [InlineData("api.user.read")]
    [InlineData("Microsoft.Graph.User.Read.All")]
    public void MapScenario_preserves_dotted_display_name_in_quotes(string paramValue)
    {
        // The scenario display name includes the parameter in quotes.
        // Quoted dotted strings must be preserved.
        var scenario = new StubExecutionResult("s1",
            "The client has scopes \"{0}\"",
            [new StubNameParam(paramValue)]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        Assert.Contains(paramValue, s.DisplayName);
    }

    [Theory]
    [InlineData("User.Read")]
    [InlineData("user.read")]
    [InlineData("api.user.read")]
    [InlineData("Microsoft.Graph.User.Read.All")]
    public void MapScenario_preserves_dotted_example_values(string paramValue)
    {
        // Example values extracted from FormattedValue must preserve dotted strings
        var scenario = new StubExecutionResult("s1",
            "The client has scopes \"{0}\"",
            [new StubNameParam(paramValue)]);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        var features = new[] { feature }.ToFeatures();
        var s = features[0].Scenarios[0];

        Assert.NotNull(s.ExampleValues);
        Assert.Contains(s.ExampleValues!, kv => kv.Value == paramValue);
    }

    // ── Stub implementations ──

    private class StubFeatureResult : IFeatureResult
    {
        private readonly List<IScenarioResult> _scenarios = [];

        public StubFeatureResult(string name, string? description = null, string[]? labels = null)
        {
            Info = new StubFeatureInfo(name, description, labels);
        }

        public IFeatureInfo Info { get; }
        public IEnumerable<IScenarioResult> GetScenarios() => _scenarios;

        public StubFeatureResult WithScenario(StubExecutionResult scenario)
        {
            _scenarios.Add(scenario);
            return this;
        }
    }

    private class StubExecutionResult : IScenarioResult
    {
        private readonly List<IStepResult> _steps = [];

        public StubExecutionResult(string id, string name,
            ExecutionStatus status = ExecutionStatus.Passed,
            string? statusDetails = null,
            TimeSpan? duration = null,
            string[]? labels = null,
            string[]? categories = null)
        {
            Info = new StubScenarioInfo(Guid.NewGuid(), name, labels, categories);
            Status = status;
            StatusDetails = statusDetails;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
        }

        public StubExecutionResult(Guid runtimeId, string name,
            ExecutionStatus status = ExecutionStatus.Passed,
            string? statusDetails = null,
            TimeSpan? duration = null,
            string[]? labels = null,
            string[]? categories = null)
        {
            Info = new StubScenarioInfo(runtimeId, name, labels, categories);
            Status = status;
            StatusDetails = statusDetails;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
        }

        public StubExecutionResult(string id,
            string nameFormat,
            INameParameterInfo[] nameParams,
            ExecutionStatus status = ExecutionStatus.Passed,
            string? statusDetails = null,
            TimeSpan? duration = null,
            string[]? labels = null,
            string[]? categories = null)
        {
            Info = new StubScenarioInfo(Guid.NewGuid(), nameFormat, nameParams, labels, categories);
            Status = status;
            StatusDetails = statusDetails;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
        }

        public IScenarioInfo Info { get; }
        public ExecutionStatus Status { get; }
        public string? StatusDetails { get; }
        public ExecutionTime? ExecutionTime { get; }
        public IEnumerable<IStepResult> GetSteps() => _steps;

        public StubExecutionResult WithStep(StubStepResult step)
        {
            _steps.Add(step);
            return this;
        }
    }

    private class StubStepResult : IStepResult
    {
        private readonly List<IStepResult> _subSteps = [];
        private IReadOnlyList<IParameterResult> _parameters = Array.Empty<IParameterResult>();

        public StubStepResult(string? keyword, string text,
            ExecutionStatus status,
            TimeSpan? duration = null,
            string[]? comments = null,
            FileAttachment[]? attachments = null,
            Exception? executionException = null)
        {
            var fullText = keyword != null ? $"{keyword} {text}" : text;
            Info = new StubStepInfo(keyword, fullText);
            Status = status;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
            Comments = comments ?? [];
            FileAttachments = attachments ?? [];
            ExecutionException = executionException;
        }

        public StubStepResult(string? keyword, string nameFormat, INameParameterInfo[] nameParams,
            ExecutionStatus status,
            TimeSpan? duration = null,
            string[]? comments = null,
            FileAttachment[]? attachments = null,
            Exception? executionException = null)
        {
            Info = new StubStepInfo(keyword, nameFormat, nameParams);
            Status = status;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
            Comments = comments ?? [];
            FileAttachments = attachments ?? [];
            ExecutionException = executionException;
        }

        public IStepInfo Info { get; }
        public ExecutionStatus Status { get; }
        public string? StatusDetails => null;
        public ExecutionTime? ExecutionTime { get; }
        public IEnumerable<string> Comments { get; }
        public Exception? ExecutionException { get; }
        public IReadOnlyList<IParameterResult> Parameters => _parameters;
        public IEnumerable<FileAttachment> FileAttachments { get; }
        public IEnumerable<IStepResult> GetSubSteps() => _subSteps;

        public StubStepResult WithSubStep(StubStepResult subStep)
        {
            _subSteps.Add(subStep);
            return this;
        }

        public StubStepResult WithParameters(params IParameterResult[] parameters)
        {
            _parameters = parameters;
            return this;
        }
    }

    private class StubFeatureInfo : IFeatureInfo
    {
        public StubFeatureInfo(string name, string? description, string[]? labels)
        {
            Name = new StubNameInfo(name);
            Description = description;
            Labels = labels ?? [];
            RuntimeId = Guid.NewGuid();
        }

        public INameInfo Name { get; }
        public Guid RuntimeId { get; }
        public string? Description { get; }
        public IEnumerable<string> Labels { get; }
    }

    private class StubScenarioInfo : IScenarioInfo
    {
        public StubScenarioInfo(Guid id, string name, string[]? labels, string[]? categories)
        {
            RuntimeId = id;
            Name = new StubNameInfo(name);
            Labels = labels ?? [];
            Categories = categories ?? [];
        }

        public StubScenarioInfo(Guid id, string nameFormat, INameParameterInfo[] nameParams, string[]? labels, string[]? categories)
        {
            RuntimeId = id;
            Name = new StubNameInfo(nameFormat, nameParams);
            Labels = labels ?? [];
            Categories = categories ?? [];
        }

        public INameInfo Name { get; }
        public Guid RuntimeId { get; }
        public IFeatureInfo Parent => null!;
        public IEnumerable<string> Labels { get; }
        public IEnumerable<string> Categories { get; }
        public string? Description => null;
    }

    private class StubStepInfo : IStepInfo
    {
        public StubStepInfo(string? keyword, string fullText)
        {
            Name = new StubStepNameInfo(keyword, fullText);
            RuntimeId = Guid.NewGuid();
        }

        public StubStepInfo(string? keyword, string nameFormat, INameParameterInfo[] nameParams)
        {
            Name = new StubStepNameInfo(keyword, nameFormat, nameParams);
            RuntimeId = Guid.NewGuid();
        }

        public IStepNameInfo Name { get; }
        INameInfo IMetadataInfo.Name => Name;
        public Guid RuntimeId { get; }
        public IMetadataInfo Parent => null!;
        public string GroupPrefix => "";
        public int Number => 1;
        public int Total => 1;
    }

    private class StubStepNameInfo : IStepNameInfo
    {
        private readonly string _fullText;
        private readonly INameParameterInfo[] _params;

        public StubStepNameInfo(string? keyword, string fullText) : this(keyword, fullText, []) { }

        public StubStepNameInfo(string? keyword, string nameFormat, INameParameterInfo[] nameParams)
        {
            StepTypeName = keyword != null ? new StubStepTypeNameInfo(keyword) : null;
            _fullText = nameFormat;
            _params = nameParams;
        }

        public IStepTypeNameInfo? StepTypeName { get; }
        public string NameFormat => _fullText;
        public IEnumerable<INameParameterInfo> Parameters => _params;
        public override string ToString()
        {
            if (_params.Length == 0) return _fullText;
            var result = _fullText;
            for (var i = 0; i < _params.Length; i++)
                result = result.Replace($"\"{{{i}}}\"", $"\"{_params[i].FormattedValue}\"");
            return result;
        }
        public string Format(INameDecorator decorator) => ToString();
        public string Format(IStepNameDecorator decorator) => ToString();
    }

    private class StubStepTypeNameInfo : IStepTypeNameInfo
    {
        public StubStepTypeNameInfo(string name) { Name = name; OriginalName = name; }
        public string Name { get; }
        public string OriginalName { get; }
    }

    private class StubNameInfo : INameInfo
    {
        private readonly string _nameFormat;
        private readonly INameParameterInfo[] _params;

        public StubNameInfo(string name) : this(name, []) { }

        public StubNameInfo(string nameFormat, INameParameterInfo[] nameParams)
        {
            _nameFormat = nameFormat;
            _params = nameParams;
        }

        public string NameFormat => _nameFormat;
        public IEnumerable<INameParameterInfo> Parameters => _params;

        public override string ToString()
        {
            if (_params.Length == 0) return _nameFormat;
            return string.Format(_nameFormat, _params.Select(p => (object)(p.FormattedValue ?? "")).ToArray());
        }

        public string Format(INameDecorator decorator)
        {
            if (_params.Length == 0) return decorator.DecorateNameFormat(_nameFormat);
            return string.Format(decorator.DecorateNameFormat(_nameFormat),
                _params.Select(p => (object)decorator.DecorateParameterValue(p)).ToArray());
        }
    }

    private class StubNameParam : INameParameterInfo
    {
        public StubNameParam(string value, ParameterVerificationStatus status = ParameterVerificationStatus.NotApplicable)
        {
            FormattedValue = value;
            VerificationStatus = status;
        }
        public bool IsEvaluated => true;
        public ParameterVerificationStatus VerificationStatus { get; }
        public string FormattedValue { get; }
    }

    private class StubParameterResult : IParameterResult
    {
        public StubParameterResult(string name, IParameterDetails details)
        {
            Name = name;
            Details = details;
        }
        public string Name { get; }
        public IParameterDetails Details { get; }
    }

    private class StubInlineDetails : IInlineParameterDetails
    {
        public StubInlineDetails(string value, string? expectation,
            ParameterVerificationStatus status = ParameterVerificationStatus.NotApplicable)
        {
            Value = value;
            Expectation = expectation ?? "";
            VerificationStatus = status;
        }
        public string Value { get; }
        public string Expectation { get; }
        public string VerificationMessage => "";
        public ParameterVerificationStatus VerificationStatus { get; }
    }

    private class StubTabularDetails : ITabularParameterDetails
    {
        public IReadOnlyList<ITabularParameterColumn> Columns => Array.Empty<ITabularParameterColumn>();
        public IReadOnlyList<ITabularParameterRow> Rows => Array.Empty<ITabularParameterRow>();
        public string VerificationMessage => "";
        public ParameterVerificationStatus VerificationStatus => ParameterVerificationStatus.NotApplicable;
    }

    // ─── Assertion sub-step merging from StepCollector ──────────

    [Fact]
    public void ToFeatures_merges_assertion_substeps_from_StepCollector()
    {
        // Simulate the scenario RuntimeId that StepCollector uses as testId
        var scenarioId = Guid.NewGuid().ToString();
        var scenarioGuid = Guid.Parse(scenarioId);

        // Stub scenario with steps
        var scenario = new StubExecutionResult(scenarioGuid, "Test")
            .WithStep(new StubStepResult("Given", "a valid request", ExecutionStatus.Passed))
            .WithStep(new StubStepResult("When", "the request is sent", ExecutionStatus.Passed))
            .WithStep(new StubStepResult("Then", "the response is valid", ExecutionStatus.Passed));
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        // Simulate what StepTrackingStepDecorator + Track.Assertion do:
        // Steps are started/completed, and assertions are added as sub-steps
        StepCollector.StartStep(scenarioId, "Given", "a valid request", null, null);
        StepCollector.CompleteStep(scenarioId, passed: true);

        StepCollector.StartStep(scenarioId, "When", "the request is sent", null, null);
        StepCollector.CompleteStep(scenarioId, passed: true);

        StepCollector.StartStep(scenarioId, "Then", "the response is valid", null, null);
        StepCollector.AddAssertionSubStep(scenarioId, "response.StatusCode.Should().Be(200)", passed: true);
        StepCollector.AddAssertionSubStep(scenarioId, "response.Body.Should().NotBeNull()", passed: true);
        StepCollector.CompleteStep(scenarioId, passed: true);

        var features = new[] { feature }.ToFeatures();
        var steps = features[0].Scenarios[0].Steps!;

        // Given and When should have no sub-steps
        Assert.Null(steps[0].SubSteps);
        Assert.Null(steps[1].SubSteps);

        // Then should have the 2 assertion sub-steps
        Assert.NotNull(steps[2].SubSteps);
        Assert.Equal(2, steps[2].SubSteps!.Length);
        Assert.Equal("response.StatusCode.Should().Be(200)", steps[2].SubSteps![0].Text);
        Assert.Equal("response.Body.Should().NotBeNull()", steps[2].SubSteps![1].Text);

        StepCollector.ClearSteps(scenarioId);
    }

    [Fact]
    public void ToFeatures_preserves_assertions_inside_native_substeps()
    {
        var scenarioId = Guid.NewGuid().ToString();
        var scenarioGuid = Guid.Parse(scenarioId);

        // LightBDD step "Then validate" has a native sub-step "And check status"
        var thenStep = new StubStepResult("Then", "validate", ExecutionStatus.Passed)
            .WithSubStep(new StubStepResult("And", "check status", ExecutionStatus.Passed));
        var scenario = new StubExecutionResult(scenarioGuid, "Test")
            .WithStep(new StubStepResult("Given", "a request", ExecutionStatus.Passed))
            .WithStep(thenStep);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        // Simulate decorator: parent "Then" has a child sub-step "And check status"
        // Assertions are made inside the sub-step
        StepCollector.StartStep(scenarioId, "Given", "a request", null, null);
        StepCollector.CompleteStep(scenarioId, passed: true);

        StepCollector.StartStep(scenarioId, "Then", "validate", null, null);
        StepCollector.StartStep(scenarioId, "And", "check status", null, null);
        StepCollector.AddAssertionSubStep(scenarioId, "status.Should().Be(200)", passed: true);
        StepCollector.AddAssertionSubStep(scenarioId, "body.Should().NotBeNull()", passed: true);
        StepCollector.CompleteStep(scenarioId, passed: true); // completes "And check status"
        StepCollector.CompleteStep(scenarioId, passed: true); // completes "Then validate"

        var features = new[] { feature }.ToFeatures();
        var steps = features[0].Scenarios[0].Steps!;

        // Given should have no sub-steps
        Assert.Null(steps[0].SubSteps);

        // Then should have the native sub-step "check status"
        Assert.NotNull(steps[1].SubSteps);
        Assert.Single(steps[1].SubSteps!);
        Assert.Equal("check status", steps[1].SubSteps![0].Text);

        // The native sub-step should have the 2 assertion sub-sub-steps
        Assert.NotNull(steps[1].SubSteps![0].SubSteps);
        Assert.Equal(2, steps[1].SubSteps![0].SubSteps!.Length);
        Assert.Equal("status.Should().Be(200)", steps[1].SubSteps![0].SubSteps![0].Text);
        Assert.Equal("body.Should().NotBeNull()", steps[1].SubSteps![0].SubSteps![1].Text);

        StepCollector.ClearSteps(scenarioId);
    }

    [Fact]
    public void ToFeatures_preserves_parent_level_assertions_alongside_native_substeps()
    {
        var scenarioId = Guid.NewGuid().ToString();
        var scenarioGuid = Guid.Parse(scenarioId);

        // LightBDD step "Then validate" has a native sub-step "And check status"
        var thenStep = new StubStepResult("Then", "validate", ExecutionStatus.Passed)
            .WithSubStep(new StubStepResult("And", "check status", ExecutionStatus.Passed));
        var scenario = new StubExecutionResult(scenarioGuid, "Test")
            .WithStep(thenStep);
        var feature = new StubFeatureResult("F").WithScenario(scenario);

        // Simulate decorator: sub-step completes first, then parent-level assertion
        StepCollector.StartStep(scenarioId, "Then", "validate", null, null);
        StepCollector.StartStep(scenarioId, "And", "check status", null, null);
        StepCollector.CompleteStep(scenarioId, passed: true); // completes "And check status"
        // Parent-level assertion made after sub-step completes
        StepCollector.AddAssertionSubStep(scenarioId, "overall.Should().BeTrue()", passed: true);
        StepCollector.CompleteStep(scenarioId, passed: true); // completes "Then validate"

        var features = new[] { feature }.ToFeatures();
        var steps = features[0].Scenarios[0].Steps!;

        // Then should have both: the native sub-step AND the parent-level assertion
        Assert.NotNull(steps[0].SubSteps);
        Assert.Equal(2, steps[0].SubSteps!.Length);
        Assert.Equal("check status", steps[0].SubSteps![0].Text);
        Assert.Equal("overall.Should().BeTrue()", steps[0].SubSteps![1].Text);

        StepCollector.ClearSteps(scenarioId);
    }
}
