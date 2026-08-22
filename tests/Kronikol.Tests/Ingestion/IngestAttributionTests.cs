using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// Attribution for captures that arrive without a test identity — a database tee, a shared sidecar, an
/// OTLP exporter — and the phase a step window lends the traffic that happened inside it.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestAttributionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-attribution-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    public IngestAttributionTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static InteractionRecord Anonymous(string service, double atSecond, string testId = "", string? pairId = null, string type = "Request") => new()
    {
        Type = type,
        Method = "GET",
        Uri = $"http://{service}/op",
        ServiceName = service,
        CallerName = "data-insights",
        TestId = testId,
        RequestResponseId = pairId,
        Timestamp = T0.AddSeconds(atSecond),
    };

    private static readonly TestRunRecord[] TwoTests =
    [
        new() { Event = "start", TestId = "first", TestName = "first", Timestamp = T0.AddSeconds(1) },
        new() { Event = "end", TestId = "first", Status = "passed", Timestamp = T0.AddSeconds(5) },
        new() { Event = "start", TestId = "second", TestName = "second", Timestamp = T0.AddSeconds(10) },
        new() { Event = "end", TestId = "second", Status = "passed", Timestamp = T0.AddSeconds(15) },
    ];

    [Fact]
    public void A_record_inside_a_test_window_is_attributed_to_it_and_one_outside_is_left_alone()
    {
        var windows = IngestAttribution.BuildWindows(TwoTests);

        var (records, attributed) = IngestAttribution.AttributeByWindow(
            [Anonymous("redis", 2), Anonymous("redis", 12), Anonymous("redis", 8)], windows);

        Assert.Equal(2, attributed);
        Assert.Equal("first", records[0].TestId);
        Assert.Equal("second", records[1].TestId);
        Assert.Equal("", records[2].TestId); // between the two tests: nothing claims it
    }

    [Fact]
    public void The_window_boundaries_are_inclusive()
    {
        var windows = IngestAttribution.BuildWindows(TwoTests);

        var (records, _) = IngestAttribution.AttributeByWindow([Anonymous("redis", 1), Anonymous("redis", 5)], windows);

        Assert.Equal("first", records[0].TestId);
        Assert.Equal("first", records[1].TestId);
    }

    [Fact]
    public void When_windows_overlap_the_test_that_started_latest_wins()
    {
        // Nested or overlapping tests: the innermost one is the one that caused the traffic.
        var windows = IngestAttribution.BuildWindows(
        [
            new TestRunRecord { Event = "start", TestId = "outer", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = "outer", Timestamp = T0.AddSeconds(20) },
            new TestRunRecord { Event = "start", TestId = "inner", Timestamp = T0.AddSeconds(5) },
            new TestRunRecord { Event = "end", TestId = "inner", Timestamp = T0.AddSeconds(10) },
        ]);

        var (records, _) = IngestAttribution.AttributeByWindow(
            [Anonymous("redis", 7), Anonymous("redis", 15)], windows);

        Assert.Equal("inner", records[0].TestId);
        Assert.Equal("outer", records[1].TestId);
    }

    [Fact]
    public void Two_windows_that_start_at_the_same_instant_resolve_to_the_first_one_seen()
    {
        // A tie has to break somewhere; it breaks deterministically, in file order.
        var windows = IngestAttribution.BuildWindows(
        [
            new TestRunRecord { Event = "start", TestId = "a", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = "a", Timestamp = T0.AddSeconds(10) },
            new TestRunRecord { Event = "start", TestId = "b", Timestamp = T0 },
            new TestRunRecord { Event = "end", TestId = "b", Timestamp = T0.AddSeconds(10) },
        ]);

        var (records, _) = IngestAttribution.AttributeByWindow([Anonymous("redis", 5)], windows);

        Assert.Equal("a", records[0].TestId);
    }

    [Fact]
    public void A_response_follows_its_request_even_when_it_lands_after_the_test_ended()
    {
        // A slow query answered after the test's end record must not be orphaned or, worse, given to
        // whatever test happened to start next.
        var windows = IngestAttribution.BuildWindows(TwoTests);

        var (records, _) = IngestAttribution.AttributeByWindow(
        [
            Anonymous("mongo", 4, pairId: "pair-1"),
            Anonymous("mongo", 11, pairId: "pair-1", type: "Response"),
        ], windows);

        Assert.Equal("first", records[0].TestId);
        Assert.Equal("first", records[1].TestId);
    }

    [Fact]
    public void The_capturer_s_fallback_marker_counts_as_no_identity_at_all()
    {
        var windows = IngestAttribution.BuildWindows(TwoTests);

        var (records, attributed) = IngestAttribution.AttributeByWindow(
            [Anonymous("redis", 2, testId: "session")], windows, fallbackTestId: "session");

        Assert.Equal(1, attributed);
        Assert.Equal("first", records[0].TestId);

        // Without naming the marker, "session" is just another test id and is left alone.
        var (untouched, none) = IngestAttribution.AttributeByWindow([Anonymous("redis", 2, testId: "session")], windows);
        Assert.Equal(0, none);
        Assert.Equal("session", untouched[0].TestId);
    }

    [Fact]
    public void A_record_that_already_names_its_test_is_never_reassigned()
    {
        var windows = IngestAttribution.BuildWindows(TwoTests);

        var (records, attributed) = IngestAttribution.AttributeByWindow([Anonymous("redis", 12, testId: "first")], windows);

        Assert.Equal(0, attributed);
        Assert.Equal("first", records[0].TestId);
    }

    [Fact]
    public void A_test_killed_before_its_end_record_is_bounded_by_the_last_thing_it_did()
    {
        var windows = IngestAttribution.BuildWindows(
        [
            new TestRunRecord { Event = "start", TestId = "killed", Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "killed", Text = "the last thing", Timestamp = T0.AddSeconds(7) },
        ]);

        var window = Assert.Single(windows);
        Assert.Equal(T0, window.Start);
        Assert.Equal(T0.AddSeconds(7), window.End);
    }

    [Fact]
    public void A_test_with_no_start_record_bounds_nothing()
    {
        Assert.Empty(IngestAttribution.BuildWindows(
            [new TestRunRecord { Event = "end", TestId = "orphan", Timestamp = T0 }]));
    }

    [Fact]
    public void Phase_from_steps_tags_interactions_with_the_step_they_happened_during()
    {
        var stepWindows = IngestAttribution.BuildStepWindows(
        [
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "Given", Text = "the seed exists", DurationMs = 2000, Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "And", Text = "the cache is warm", DurationMs = 1000, Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "When", Text = "the overview opens", DurationMs = 3000, Timestamp = T0.AddSeconds(4) },
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "Then", Text = "the figures show", DurationMs = 1000, Timestamp = T0.AddSeconds(8) },
        ]);

        var (records, tagged) = IngestAttribution.ApplyPhaseFromSteps(
        [
            Anonymous("redis", 1, testId: "t"),      // inside the Given
            Anonymous("redis", 2.5, testId: "t"),    // inside the And, which inherits Setup
            Anonymous("mongo", 5, testId: "t"),      // inside the When
            Anonymous("mongo", 8.5, testId: "t"),    // inside the Then
            Anonymous("mongo", 20, testId: "t"),     // after every step
        ], stepWindows);

        Assert.Equal(4, tagged);
        Assert.Equal(nameof(TestPhase.Setup), records[0].Phase);
        Assert.Equal(nameof(TestPhase.Setup), records[1].Phase);
        Assert.Equal(nameof(TestPhase.Action), records[2].Phase);
        Assert.Equal(nameof(TestPhase.Action), records[3].Phase);
        Assert.Null(records[4].Phase);
    }

    [Fact]
    public void A_capturer_that_already_knows_the_phase_keeps_it()
    {
        var stepWindows = IngestAttribution.BuildStepWindows(
            [new TestRunRecord { Event = "step", TestId = "t", Keyword = "Given", Text = "setup", DurationMs = 5000, Timestamp = T0 }]);

        var known = Anonymous("redis", 1, testId: "t") with { Phase = "Action" };
        var unknown = Anonymous("redis", 2, testId: "t") with { Phase = "Unknown" };

        var (records, tagged) = IngestAttribution.ApplyPhaseFromSteps([known, unknown], stepWindows);

        Assert.Equal(1, tagged);
        Assert.Equal("Action", records[0].Phase);
        Assert.Equal(nameof(TestPhase.Setup), records[1].Phase);
    }

    [Fact]
    public void Background_and_nested_steps_open_no_phase_window()
    {
        var stepWindows = IngestAttribution.BuildStepWindows(
        [
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "Given", Text = "background", Background = true, DurationMs = 1000, Timestamp = T0 },
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "When", Text = "nested", Level = 1, DurationMs = 1000, Timestamp = T0.AddSeconds(2) },
            new TestRunRecord { Event = "step", TestId = "t", Keyword = "When", Text = "top level", DurationMs = 1000, Timestamp = T0.AddSeconds(4) },
        ]);

        var window = Assert.Single(stepWindows);
        Assert.Equal(T0.AddSeconds(4), window.Start);
        Assert.Equal(TestPhase.Action, window.Phase);
    }

    [Fact]
    public void The_pipeline_wires_window_attribution_and_phases_end_to_end()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Reports");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            // A database tee stamps every line with its session marker; nothing else identifies it.
            Interactions =
            [
                Anonymous("redis", 2, testId: "session"),
                Anonymous("redis", 30, testId: "session"),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "first", TestName = "first", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "step", TestId = "first", Keyword = "Given", Text = "the seed exists", DurationMs = 3000, Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "first", Status = "passed", Timestamp = T0.AddSeconds(5) },
            ],
            Options = options,
            AttributeByTestWindow = true,
            WindowAttributionFallbackId = "session",
            PhaseFromSteps = true,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        });

        var scenarios = result.Features.SelectMany(f => f.Scenarios).ToArray();
        var attributed = Assert.Single(scenarios, s => s.Id == "first");
        Assert.Equal("first", attributed.DisplayName);
        // The one inside the test window joined the test; the one at +30s fell into the fold bucket.
        Assert.Contains(scenarios, s => s.DisplayName == "Traffic outside any test");

        // The step also injects its delimiter bar as an override pair, which is not an interaction.
        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == "first" && !l.IsOverrideStart && !l.IsOverrideEnd).ToArray();
        Assert.Equal(TestPhase.Setup, Assert.Single(logs).Phase);

        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.UnattributedInteractions && d.Message.Contains("attributed to a test by time window"));
    }

    [Fact]
    public void Drop_unattributed_discards_only_what_the_predicate_claims_and_folds_the_rest()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Dropped");
        options.GenerateComponentDiagram = false;

        // Two session-wide capturers: the redis tee sees the seeder's traffic that no test asked for,
        // the mongo tee sees traffic worth showing in the fold bucket.
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                Anonymous("redis", 30, testId: "session"),
                Anonymous("mongo", 31, testId: "session"),
                Anonymous("redis", 2, testId: "session"), // inside the test window: attributed, so never offered
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "first", TestName = "first", Timestamp = T0.AddSeconds(1) },
                new TestRunRecord { Event = "end", TestId = "first", Status = "passed", Timestamp = T0.AddSeconds(5) },
            ],
            Options = options,
            AttributeByTestWindow = true,
            WindowAttributionFallbackId = "session",
            DropUnattributed = record => record.ServiceName == "redis",
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        });

        // Scoped to this run's own ids: the store is process-wide, and a test elsewhere in the assembly
        // may log into it while this one asserts (it did — the suite flaked on exactly this line).
        var mine = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId is "first" or "outside-any-test" && l.ServiceName is "redis" or "mongo")
            .ToArray();
        Assert.Equal(2, mine.Length);
        // The redis line at +30s is gone; the one attributed to the test survived.
        Assert.Single(mine, l => l.ServiceName == "redis" && l.TestId == "first");
        Assert.Single(mine, l => l.ServiceName == "mongo" && l.TestId == "outside-any-test");

        var dropped = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.DroppedUnattributed);
        Assert.Contains("1 unattributed interaction record(s) dropped", dropped.Message);
    }

    [Fact]
    public void Dropping_a_request_takes_its_response_with_it()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Pairs");
        options.GenerateComponentDiagram = false;

        IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                Anonymous("redis", 30, testId: "session", pairId: "pair-1"),
                Anonymous("redis", 31, testId: "session", pairId: "pair-1", type: "Response"),
            ],
            TestRecords = [new TestRunRecord { Event = "start", TestId = "first", TestName = "first", Timestamp = T0 }],
            Options = options,
            WindowAttributionFallbackId = "session",
            // The predicate only ever sees requests here; the response has to follow anyway, or the
            // diagram is left with a reply to a call that was never made.
            DropUnattributed = record => record.Type == "Request",
            AllowEmpty = true,
        });

        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.ServiceName == "redis");
    }

    [Fact]
    public void A_predicate_that_throws_keeps_the_record_and_says_so()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Throwing");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [Anonymous("redis", 30, testId: "session")],
            TestRecords = [new TestRunRecord { Event = "start", TestId = "first", TestName = "first", Timestamp = T0 }],
            Options = options,
            WindowAttributionFallbackId = "session",
            DropUnattributed = _ => throw new InvalidOperationException("the host's predicate is broken"),
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
        });

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.ServiceName == "redis");
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("the host's predicate is broken"));
    }
}
