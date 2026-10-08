namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>
/// Kronikol.xUnit2's own ReportingTestFramework, run by <c>dotnet test</c> under both VS adapters (T19), read
/// against the runner's own record of the run (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md section 5).
/// </summary>
[Collection(FixtureLaneCollection.Name)]
public class OwnFrameworkLaneTests
{
    /// <summary>The fixture project per adapter: OwnFramework is 2.8.2, OwnFramework.Vs3 is 3.1.5.</summary>
    public static TheoryData<string> Adapters => new() { "OwnFramework", "OwnFramework.Vs3" };

    public static TheoryData<string, int> AdaptersThreeTimes
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var fixture in new[] { "OwnFramework", "OwnFramework.Vs3" })
                for (var attempt = 1; attempt <= 3; attempt++)
                    data.Add(fixture, attempt);
            return data;
        }
    }

    private static FixtureRun Default(string fixture, int attempt = 1) =>
        FixtureLane.Run(new FixtureVariant(fixture, Attempt: attempt));

    [Theory]
    [MemberData(nameof(Adapters))]
    public void Every_tracked_test_is_one_scenario_with_its_own_result_duration_error_and_calls(string fixture) =>
        Expected.EveryTrackedTestIsItsOwnScenario(Default(fixture));

    // T1
    [Theory]
    [MemberData(nameof(Adapters))]
    public void A_failing_fact_with_a_display_name_is_reported_failed_with_its_error_and_duration_under_its_display_name(string fixture)
    {
        var scenario = Assert.Single(Default(fixture).Report.Named("Facts", "A failing fact with a display name"));

        Assert.Equal("Failed", scenario.Result);
        Assert.StartsWith("Assert.Equal() Failure", scenario.ErrorMessage);
        Assert.True(scenario.DurationSeconds >= 0.1, $"{scenario} waited 0.12 s");
        Assert.Equal(["/facts/displayname-fails"], scenario.Calls);
    }

    // T2
    [Theory]
    [MemberData(nameof(AdaptersThreeTimes))]
    public void Each_theory_row_carries_its_own_name_verdict_duration_and_call(string fixture, int attempt)
    {
        var run = Default(fixture, attempt);
        var report = run.Report;
        var rows = new (int Row, int Ms)[] { (1, 50), (2, 400), (3, 100), (4, 800), (5, 200), (6, 20) };
        var problems = new List<string>();

        foreach (var (row, ms) in rows)
        {
            var named = report.Named("Theories", $"Rows [row: {row}, ms: {ms}]");
            if (named.Length != 1)
            {
                problems.Add($"row {row}: {named.Length} scenarios");
                continue;
            }

            var scenario = named[0];
            if (scenario.Result != (row == 4 ? "Failed" : "Passed"))
                problems.Add($"row {row}: {scenario.Result}");

            // The runner's own duration for the row: a row that slept longer is another row's. Task.Delay can wake
            // a millisecond early, so the row's sleep bounds the runner's figure loosely.
            var ran = run.Trx!.Tests.Single(t => t.DisplayName.EndsWith($"Rows(row: {row}, ms: {ms})", StringComparison.Ordinal));
            if (ran.Duration.TotalSeconds < ms / 1000.0 - 0.02)
                problems.Add($"row {row} slept {ms} ms, the runner says {ran.Duration.TotalSeconds:0.000} s");
            if (Math.Abs(scenario.DurationSeconds - ran.Duration.TotalSeconds) > 0.005)
                problems.Add($"row {row} took {ran.Duration.TotalSeconds:0.000} s, reported {scenario.DurationSeconds:0.000} s");
            if (!scenario.Calls.SequenceEqual([$"/row/{row}/slept/{ms}"]))
                problems.Add($"row {row}: calls [{string.Join(", ", scenario.Calls)}]");
        }

        foreach (var call in report.Background.Where(b => b.Path.StartsWith("/row/", StringComparison.Ordinal)))
            problems.Add($"{call.Path} is in the background section");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // T3
    [Theory]
    [MemberData(nameof(Adapters))]
    public void Each_row_of_a_theory_whose_data_cannot_be_serialized_carries_its_own_name_and_call(string fixture)
    {
        var report = Default(fixture).Report;

        for (var i = 1; i <= 3; i++)
        {
            var scenario = Assert.Single(report.Named("Non Serializable Rows", $"Rows [item: NonSer {i}]"));
            Assert.Equal("Passed", scenario.Result);
            Assert.Equal([$"/nonser/{i}"], scenario.Calls);
        }
    }

    // T4
    [Theory]
    [MemberData(nameof(Adapters))]
    public void A_tracked_skipped_test_is_reported_skipped_and_untracked_tests_are_not_reported(string fixture)
    {
        var report = Default(fixture).Report;

        var skipped = Assert.Single(report.Named("Facts", "Skipped"));
        Assert.Equal("Skipped", skipped.Result);
        Assert.Empty(skipped.Calls);
        Assert.Null(skipped.ErrorMessage);

        Assert.DoesNotContain(report.Scenarios, s => s.Feature == "Untracked");
        Assert.DoesNotContain(report.Scenarios, s => s.Calls.Any(c => c.StartsWith("/untracked/", StringComparison.Ordinal)));
    }

    // T5
    [Theory]
    [MemberData(nameof(Adapters))]
    public void Tests_that_fail_before_their_first_line_are_reported_failed_with_the_error_and_no_calls(string fixture)
    {
        var report = Default(fixture).Report;
        var expected = new (string Feature, string Error)[]
        {
            ("Ctor Throws", "the test class constructor throws on purpose"),
            ("Init Throws", "InitializeAsync throws on purpose"),
            ("Fixture Throws", "the class fixture constructor throws on purpose"),
            ("Unresolvable Argument", "The following constructor parameters did not have matching fixture data"),
        };

        foreach (var (feature, error) in expected)
        {
            var scenario = Assert.Single(report.Named(feature, "Never runs"));
            Assert.Equal("Failed", scenario.Result);
            Assert.Contains(error, scenario.ErrorMessage);
            Assert.Empty(scenario.Calls);
        }
    }

    // T6
    [Theory]
    [MemberData(nameof(Adapters))]
    public void Under_method_display_every_test_keeps_its_own_result(string fixture)
    {
        var run = FixtureLane.Run(new FixtureVariant(fixture, "method-display", RunSettings: "xUnit.MethodDisplay=method"));

        // The setting took: a theory row's display name no longer names its class.
        Assert.Contains(run.Trx!.Tests, t => t.DisplayName == "Rows(row: 4, ms: 800)");
        Expected.EveryTrackedTestIsItsOwnScenario(run);
    }

    // T7
    [Theory]
    [MemberData(nameof(Adapters))]
    public void With_no_options_set_the_run_starts_before_its_first_scenario_and_ends_after_its_last(string fixture)
    {
        var report = Default(fixture).Report;
        var timed = report.Scenarios.Where(s => s.EndedAt is not null).ToArray();
        Assert.NotEmpty(timed);

        // The report writes its start and end to the second.
        static DateTimeOffset ToSecond(DateTimeOffset t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);
        var firstStart = ToSecond(timed.Min(s => s.EndedAt!.Value - TimeSpan.FromSeconds(s.DurationSeconds)));
        var lastEnd = ToSecond(timed.Max(s => s.EndedAt!.Value));

        Assert.True(report.Start <= firstStart, $"The run started {report.Start:O}, its first scenario {firstStart:O}");
        Assert.True(report.End >= lastEnd, $"The run ended {report.End:O}, its last scenario {lastEnd:O}");
    }

    // T10
    [Theory]
    [MemberData(nameof(Adapters))]
    public void The_reports_are_complete_when_dotnet_test_returns_even_when_slow_to_write(string fixture)
    {
        var run = FixtureLane.Run(new FixtureVariant(fixture, "slow-report", Environment: new Dictionary<string, string>
        {
            ["KRONIKOL_FIXTURE_REPORT_DELAY_MS"] = "100",
            ["KRONIKOL_FIXTURE_REPORT_DELAY_LOG"] = "{results}/delays.txt",
        }));

        var delays = Path.Combine(run.ResultsDirectory, "delays.txt");
        var waits = File.Exists(delays) ? File.ReadAllLines(delays).Length : 0;
        Assert.True(waits >= 10, $"The report waited {waits} times; a slow report needs at least 10.{Environment.NewLine}{run.Describe()}");

        foreach (var file in new[] { "TestRunReport.json", "TestRunReport.html", "Failures.md", "Run.json" })
            Assert.True(run.ReadReportFile(file) is { Length: > 0 }, $"{file} was not written.{Environment.NewLine}{run.Describe()}");
        Expected.EveryTrackedTestIsItsOwnScenario(run);
    }

    // T11
    [Theory]
    [MemberData(nameof(Adapters))]
    public void A_report_that_cannot_be_written_leaves_the_run_whole_and_writes_the_error_log(string fixture)
    {
        var run = FixtureLane.Run(new FixtureVariant(fixture, "unwritable-reports",
            Environment: new Dictionary<string, string> { ["KRONIKOL_FIXTURE_REPORTS_FOLDER"] = "{results}/a-file.txt" },
            FilesToCreate: ["{results}/a-file.txt"]));

        Assert.True(run.Trx is { Tests.Count: Expected.TestCount }, $"The run did not record its {Expected.TestCount} tests.{Environment.NewLine}{run.Describe()}");
        Assert.Equal(1, run.ExitCode); // the fixtures' own failures, not a crashed host
        Assert.DoesNotContain("Test host process crashed", run.Output);
        Assert.DoesNotContain("aborted", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(run.ErrorLog is { Length: > 0 }, $"No kronikol-error.log.{Environment.NewLine}{run.Describe()}");
    }

    // T12
    [Theory]
    [MemberData(nameof(Adapters))]
    public void Listing_the_tests_writes_no_report_and_no_error_log(string fixture)
    {
        var run = FixtureLane.Run(new FixtureVariant(fixture, "list-tests", ListTests: true));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Facts.Passes", run.Output);
        Assert.Null(run.ReportsDirectory);
        Assert.Null(run.ErrorLog);
    }

    // T21
    [Theory]
    [MemberData(nameof(Adapters))]
    public void A_call_made_in_the_test_class_constructor_is_not_the_tests(string fixture)
    {
        var report = Default(fixture).Report;

        var scenario = Assert.Single(report.Named("Constructor Call", "Calls from the test"));
        Assert.Equal(["/constructor-call/from-test"], scenario.Calls);
        Assert.DoesNotContain(report.Scenarios, s => s.Calls.Contains("/constructor-call/from-constructor"));
    }
}
