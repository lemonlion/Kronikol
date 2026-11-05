using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// R2 of plans/STEP_PHASE_PLAN.md (5.6): what the boundary a step draws does to a diagram, over logs from real
/// <see cref="StepCollector"/> steps and a real <see cref="MessageTracker"/>, as a hand-built log passes with the bug.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepBoundaryDiagramTests
{
    public StepBoundaryDiagramTests() => TestPhaseContext.Reset();

    private static string NewTestId() => $"step-boundary-diagram-{Guid.NewGuid():N}";

    private static MessageTracker Tracker(string id, bool trackDuringSetup = true) => new(new MessageTrackerOptions
    {
        CallerName = "Test",
        ServiceName = "Bus",
        CurrentTestInfoFetcher = () => ("A test", id),
        TrackDuringSetup = trackDuringSetup,
    });

    private static void Send(MessageTracker tracker, string label) =>
        tracker.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));

    private static void Step(string id, string keyword, Action body)
    {
        StepCollector.StartStep(id, keyword, $"{keyword} step", null, null);
        body();
        StepCollector.CompleteStep(id, passed: true);
    }

    private static List<RequestResponseLog> Logs(string id) => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToList();

    private static string Source(string id, bool separateSetup = true, bool collapse = false) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Logs(id), separateSetup: separateSetup, collapseConsecutiveIdenticalCalls: collapse)
            .Single().PlantUmls.First().PlainText;

    // The Setup box's own line: the word also opens a block of the diagram's style.
    private static readonly Regex Partition = new(@"(?m)^partition\b.*\bSetup\s*$");

    private static string SetupPartition(string source)
    {
        var open = Partition.Match(source);
        Assert.True(open.Success, $"no Setup partition in:\n{source}");
        var end = Regex.Match(source[open.Index..], @"(?m)^end\s*$");
        Assert.True(end.Success, $"the partition does not close in:\n{source}");
        return source.Substring(open.Index, end.Index);
    }

    private static RequestResponseLog Marker(string id, bool replaces = false) =>
        new(id, id, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsActionStart = true,
            MarkerKind = DiagramMarkerKind.Phase,
            PlantUml = replaces ? DefaultTrackingDiagramOverride.ReplacesBoundary : null
        };

    /// <summary>A call that takes its phase variant from its place, as a tracker with a per-phase option logs one.</summary>
    private static RequestResponseLog[] VariantCall(string id, string label)
    {
        var trace = Guid.NewGuid();
        var request = Guid.NewGuid();
        var setup = new PhaseVariant(HttpMethod.Get, new Uri("http://probe.local/at-setup"), null, [], false);
        var action = new PhaseVariant(HttpMethod.Get, new Uri("http://probe.local/at-action"), null, [], false);
        var uri = new Uri($"http://probe.local/{label}");
        return
        [
            new("A test", id, "GET", "", uri, [], "Shop", "Test", RequestResponseType.Request, trace, request, false) { SetupVariant = setup, ActionVariant = action },
            new("A test", id, "GET", "", uri, [], "Shop", "Test", RequestResponseType.Response, trace, request, false, System.Net.HttpStatusCode.OK) { SetupVariant = setup, ActionVariant = action },
        ];
    }

    // F5: a Given that only sends a message had no setup call to the handler, so no boundary and no partition.
    [Fact]
    public void A_Given_whose_only_call_is_a_message_is_inside_the_partition_and_a_Whens_message_is_outside_it()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        Step(id, "Given", () => Send(tracker, "seeded"));
        Step(id, "When", () => Send(tracker, "worked"));

        var source = Source(id);
        var setup = SetupPartition(source);
        Assert.Contains("seeded", setup);
        Assert.DoesNotContain("worked", setup);
        Assert.Contains("worked", source);
    }

    // review/E01, Z10: hasSetupTraces finds nothing inside it.
    [Fact]
    public void A_Given_with_no_tracked_calls_draws_no_partition()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        Step(id, "Given", () => { });
        Step(id, "When", () => Send(tracker, "worked"));

        Assert.False(Partition.IsMatch(Source(id)));
    }

    // review/E90, through the report's own path, as a direct call of the public method draws the dropped arrow whatever the fix does.
    [Fact]
    public void A_Given_whose_calls_were_all_dropped_draws_no_partition_through_the_reports_path()
    {
        var id = NewTestId();
        var tracker = Tracker(id, trackDuringSetup: false);

        Step(id, "Given", () => Send(tracker, "seeded"));
        Step(id, "When", () => Send(tracker, "worked"));

        var kept = Logs(id).Where(l => !l.TrackingIgnore).ToList();
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(kept, separateSetup: true).Single().PlantUmls.First().PlainText;

        Assert.DoesNotContain("seeded", source);
        Assert.False(Partition.IsMatch(source));
    }

    // Q5: the last marker wins, for the partition.
    [Fact]
    public void An_explicit_StartAction_in_the_step_that_drew_the_boundary_moves_the_partition_past_what_came_before_it()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        var seeding = StepCollector.StartFrame(id, "Given", "seeding", phaseKeyword: "Given");
        Send(tracker, "seeded");
        StepCollector.CompleteFrame(id, seeding, passed: true);
        var acting = StepCollector.StartFrame(id, "When", "acting", phaseKeyword: "When");
        Send(tracker, "before-explicit");
        DefaultTrackingDiagramOverride.StartAction(id);
        Send(tracker, "after-explicit");
        StepCollector.CompleteFrame(id, acting, passed: true);

        var setup = SetupPartition(Source(id));
        Assert.Contains("seeded", setup);
        Assert.Contains("before-explicit", setup);
        Assert.DoesNotContain("after-explicit", setup);
    }

    // And for the verbosity variant each call is drawn at.
    [Fact]
    public void The_last_marker_decides_the_variant_each_call_is_drawn_at()
    {
        var id = NewTestId();
        List<RequestResponseLog> logs = [Marker(id), .. VariantCall(id, "a"), Marker(id, replaces: true), .. VariantCall(id, "b")];
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).Single().PlantUmls.First().PlainText;

        Assert.True(source.IndexOf("at-setup", StringComparison.Ordinal) < source.IndexOf("at-action", StringComparison.Ordinal),
            $"the call before the last marker reads its Setup variant:\n{source}");
    }

    // review2/F2: a capture written before markers said so has one for each client and each explicit call, and its first is its boundary, as it always was.
    [Fact]
    public void A_capture_with_markers_that_do_not_replace_keeps_its_first()
    {
        var id = NewTestId();
        var tracker = Tracker(id);
        Send(tracker, "in-setup");
        Send(tracker, "first-act");
        Send(tracker, "second-act");
        var logs = Logs(id);
        logs.Insert(logs.FindIndex(l => l.Uri.ToString().Contains("second-act")), Marker(id));
        logs.Insert(logs.FindIndex(l => l.Uri.ToString().Contains("first-act")), Marker(id));

        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true).Single().PlantUmls.First().PlainText;

        var setup = SetupPartition(source);
        Assert.Contains("in-setup", setup);
        Assert.DoesNotContain("first-act", setup);
    }

    // review2/F2: that the marker replaces the earlier one lives through a store.
    [Fact]
    public void A_marker_that_replaces_another_stays_one_through_a_store()
    {
        var id = NewTestId();

        var replacing = Kronikol.Ingestion.InteractionRecord.FromLog(Marker(id, replaces: true)).ToLogs(id).Single();
        var first = Kronikol.Ingestion.InteractionRecord.FromLog(Marker(id)).ToLogs(id).Single();

        Assert.True(replacing.IsActionStart);
        Assert.Equal(DefaultTrackingDiagramOverride.ReplacesBoundary, replacing.PlantUml);
        Assert.True(first.IsActionStart);
        Assert.Null(first.PlantUml);
    }

    // review2/E08: a retry loop that clears its steps has two boundaries for one test, and neither knows of the other. The first stands, as it did.
    [Fact]
    public void A_retry_that_clears_its_steps_keeps_the_first_attempts_boundary()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        Send(tracker, "attempt-one-setup");
        DefaultTrackingDiagramOverride.StartAction(id);
        Send(tracker, "attempt-one-act");
        StepCollector.ClearSteps(id);
        Send(tracker, "attempt-two-setup");
        DefaultTrackingDiagramOverride.StartAction(id);
        Send(tracker, "attempt-two-act");

        var setup = SetupPartition(Source(id));
        Assert.Contains("attempt-one-setup", setup);
        Assert.DoesNotContain("attempt-one-act", setup);
    }

    // review2/F1, painted: an act between a woven Given and a woven Then stays out of a partition, so the page's Setup filter leaves it.
    [Fact]
    public void An_act_between_a_woven_Given_and_a_woven_Then_has_no_partition_over_it()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        Step(id, "Given", () => Send(tracker, "arranged"));
        Send(tracker, "the-act");
        Step(id, "Then", () => Send(tracker, "checked"));

        Assert.False(Partition.IsMatch(Source(id)), Source(id));
    }

    // review/E08.
    [Fact]
    public void A_test_of_a_marker_alone_draws_no_diagram()
    {
        var id = NewTestId();

        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Empty(PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Logs(id), separateSetup: true).SelectMany(r => r.PlantUmls));
    }

    // review/E11: a marker carries the test's id as its name.
    [Fact]
    public void A_marker_first_does_not_take_the_tests_name()
    {
        var id = NewTestId();
        var tracker = Tracker(id);

        DefaultTrackingDiagramOverride.StartAction(id);
        Send(tracker, "worked");

        Assert.Equal("A test", PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Logs(id), separateSetup: true).Single().TestName);
    }

    // review/K01, K02: with nothing drawn at the boundary, it must not end a run the collapser would fold.
    [Fact]
    public void With_SeparateSetup_off_a_run_of_identical_calls_across_the_boundary_folds_as_one()
    {
        var id = NewTestId();
        var tracker = Tracker(id);
        var options = StepCollector.Options;
        try
        {
            StepCollector.Options = new StepTrackingOptions { ShowStepDelimiters = false };
            Step(id, "Given", () => { Send(tracker, "same"); Send(tracker, "same"); });
            Step(id, "When", () => { Send(tracker, "same"); Send(tracker, "same"); });
        }
        finally
        {
            StepCollector.Options = options;
        }

        Assert.Contains(Logs(id), l => l.IsActionStart);
        var source = Source(id, separateSetup: false, collapse: true);
        Assert.Equal(1, source.Split('\n').Count(l => l.StartsWith("loop ", StringComparison.Ordinal)));
    }

    // review4/D1, C02: a call that takes its phase variant from its place needs the marker, so it stays.
    [Fact]
    public void With_SeparateSetup_off_a_call_with_a_phase_variant_keeps_its_marker()
    {
        var id = NewTestId();
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([.. VariantCall(id, "a"), Marker(id), .. VariantCall(id, "a")], separateSetup: false, collapseConsecutiveIdenticalCalls: true)
            .Single().PlantUmls.First().PlainText;

        Assert.Contains("at-setup", source);
        Assert.Contains("at-action", source);
    }

    // review4/C04: the boundary draws nothing, so a split at the last call before it leaves no empty diagram after it. A response
    // past the limit by itself splits there, and no bar sits between it and the marker, as with step delimiters off.
    [Fact]
    public void A_diagram_split_just_before_the_marker_leaves_no_empty_diagram()
    {
        var id = NewTestId();
        var trace = Guid.NewGuid();
        var request = Guid.NewGuid();
        var uri = new Uri("http://probe.local/big");
        var big = string.Concat(Enumerable.Range(0, 100).Select(_ => Guid.NewGuid().ToString("N")));
        RequestResponseLog[] logs =
        [
            new("A test", id, "GET", "", uri, [], "Shop", "Test", RequestResponseType.Request, trace, request, false),
            new("A test", id, "GET", big, uri, [], "Shop", "Test", RequestResponseType.Response, trace, request, false, System.Net.HttpStatusCode.OK),
            Marker(id),
        ];

        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: true, maxEncodedDiagramLength: 2000).Single().PlantUmls;

        Assert.Single(diagrams);
    }
}
