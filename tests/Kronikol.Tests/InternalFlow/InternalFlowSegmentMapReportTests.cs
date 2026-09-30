using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// The segment map of a generated report holds only segments some arrow can open (#100). Since 3.15.1 every
/// marker record carries a time, and the builder made a segment of each step bar and assertion note, which no
/// diagram links: 55% of a real report's map. These facts run the real writers, the real generator and both
/// outputs that carry the map: the HTML page and the mergeable data file.
/// </summary>
[Collection("DiagramsFetcher")]
public class InternalFlowSegmentMapReportTests : IDisposable
{
    private readonly string _sourceName = $"Kronikol.Tests.SegmentMapReport.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kronikol-iflow-map-" + Guid.NewGuid().ToString("N"));

    public InternalFlowSegmentMapReportTests()
    {
        _source = new ActivitySource(_sourceName);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var span in _spans) span.Dispose();
        _listener.Dispose();
        _source.Dispose();
        try { Directory.Delete(_directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(InternalFlowNoDataBehavior.HideLink)]
    [InlineData(InternalFlowNoDataBehavior.ShowMessage)]
    public void Every_segment_of_a_scenario_is_opened_by_an_arrow_in_its_diagram(InternalFlowNoDataBehavior noData)
    {
        var run = Generate(noData);

        var linked = LinkedIds(DiagramSourcesInPage(run.Html));
        var inPage = SegmentKeysInPage(run.Html).Where(run.Own.Contains).ToArray();
        // Not vacuous: both calls have a segment (each has spans of its own trace).
        Assert.Contains($"iflow-{run.First}", inPage);
        Assert.Contains($"iflow-{run.Second}", inPage);
        Assert.All(inPage, key => Assert.Contains(key, linked));

        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        var root = data.RootElement;
        var linkedInData = LinkedIds(root.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .SelectMany(s => s.TryGetProperty("diagrams", out var d) ? d.EnumerateArray().Select(x => ReportPayloadText.Of(x) ?? "") : []));
        var inData = root.GetProperty("internalFlowSegments").EnumerateObject().Select(p => p.Name).Where(run.Own.Contains).ToArray();
        Assert.Contains($"iflow-{run.First}", inData);
        Assert.Contains($"iflow-{run.Second}", inData);
        Assert.All(inData, key => Assert.Contains(key, linkedInData));
    }

    [Theory]
    [InlineData(InternalFlowNoDataBehavior.HideLink)]
    [InlineData(InternalFlowNoDataBehavior.ShowMessage)]
    public void The_element_list_answers_for_every_link_the_page_shows_what_the_map_says(InternalFlowNoDataBehavior noData)
    {
        // The render script binds links from the list alone, without decoding (INTERNAL_FLOW_BLOB_PLAN §3.3): for every
        // id a diagram of the page links, "has a segment" by the list must be "is a key" by the map.
        var run = Generate(noData);

        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var start = run.Html.IndexOf(head, StringComparison.Ordinal) + head.Length;
        using var element = JsonDocument.Parse(run.Html[start..run.Html.IndexOf("</script>", start, StringComparison.Ordinal)]);
        var keys = SegmentKeysInPage(run.Html).ToHashSet();
        var listed = element.RootElement.EnumerateObject().First().Value.EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var hasList = element.RootElement.EnumerateObject().First().Name == "has";

        var linked = LinkedIds(DiagramSourcesInPage(run.Html));
        Assert.Contains($"iflow-{run.First}", linked);
        Assert.All(linked, id => Assert.Equal(keys.Contains(id), hasList ? listed.Contains(id) : !listed.Contains(id)));
    }

    [Fact]
    public void The_element_list_is_chosen_over_the_links_of_the_scenarios_the_page_shows()
    {
        // The page shows a diagram only for a scenario it lists, but the list was counted over every test the
        // process had logged: another test's calls, links without a segment here, outnumbered the page's own and
        // tipped the choice to "has" (red in CI from 3.31.9 on, in the E2E ArrowLinkOpensPopupTests, whose fixtures
        // share a process). Four such calls under a test this run does not name.
        var elsewhere = "iflow-map-elsewhere-" + Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow.AddSeconds(-40);
        for (var i = 0; i < 4; i++)
        {
            var id = Guid.NewGuid();
            var trace = ActivityTraceId.CreateRandom();
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Request, at.AddMilliseconds(i * 20)));
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Response, at.AddMilliseconds(i * 20 + 5)));
        }

        var run = Generate(InternalFlowNoDataBehavior.HideLink);

        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var start = run.Html.IndexOf(head, StringComparison.Ordinal) + head.Length;
        using var element = JsonDocument.Parse(run.Html[start..run.Html.IndexOf("</script>", start, StringComparison.Ordinal)]);
        var list = element.RootElement.EnumerateObject().First();
        var linked = LinkedIds(DiagramSourcesInPage(run.Html));
        var keys = SegmentKeysInPage(run.Html).ToHashSet();
        var withSegment = linked.Count(keys.Contains);

        // Not vacuous: the page's two calls both have a segment, so over its own links "hidden" is the shorter list.
        Assert.Equal(2, withSegment);
        Assert.Equal(linked.Count - withSegment < withSegment ? "hidden" : "has", list.Name);
        Assert.All(list.Value.EnumerateArray(), id => Assert.Contains(id.GetString()!, linked));
    }

    [Fact]
    public void The_run_report_and_a_merge_of_its_data_file_carry_the_same_element()
    {
        // INTERNAL_FLOW_BLOB_PLAN §7.4: the two emit sites, ReportGenerator for a run and MergeableReportRenderer for a
        // merge, write the same element for the same run, and do so when the process also logged a test the run does not
        // name (§11.3): the merge renders the data file's diagrams, which are the run's scenarios' only.
        var elsewhere = "iflow-map-elsewhere-" + Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow.AddSeconds(-40);
        for (var i = 0; i < 4; i++)
        {
            var id = Guid.NewGuid();
            var trace = ActivityTraceId.CreateRandom();
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Request, at.AddMilliseconds(i * 20)));
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Response, at.AddMilliseconds(i * 20 + 5)));
        }
        var run = Generate(InternalFlowNoDataBehavior.HideLink);

        var merged = Kronikol.Reports.Merge.MergeableReportRenderer.Render(
            Kronikol.Reports.Merge.MergeableReportReader.ReadFile(Path.Combine(_directory, "TestRunReport.json")),
            Path.Combine(_directory, "merged", "TestRunReport.html"));

        var element = Element(run.Html);
        Assert.Contains($"iflow-{run.First}", SegmentKeysInPage(run.Html));
        Assert.Equal(element, Element(File.ReadAllText(merged)));
    }

    /// <summary>The page's segment element, whole.</summary>
    private static string Element(string html)
    {
        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var start = html.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, "the page carries a segment element");
        return html[start..(html.IndexOf("</script>", start, StringComparison.Ordinal) + "</script>".Length)];
    }

    [Fact]
    public void A_flow_two_calls_show_is_stored_once_in_the_page_and_in_the_data_file()
    {
        // #86 (V4_PLAN.md R5): the first call and the call nested in it show the same span, so both maps the run writes
        // hold that flow once, in the first call's segment, and the nested call's segment names it.
        var run = Generate(InternalFlowNoDataBehavior.HideLink, nestedCall: true);

        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        foreach (var map in new[] { PageMap(run.Html), data.RootElement.GetProperty("internalFlowSegments") })
        {
            Assert.Contains("first.root", map.GetProperty($"iflow-{run.First}").GetProperty("content").GetString(), StringComparison.Ordinal);
            Assert.Equal($"iflow-{run.First}", map.GetProperty($"iflow-{run.Nested}").GetProperty("sameAs").GetString());
        }
    }

    [Fact]
    public void Every_segment_the_page_and_the_data_file_carry_is_one_their_diagrams_link()
    {
        // V4_PLAN.md R6 (Q4: drop): a segment no drawn arrow links can never be opened. A test this run does not name
        // made calls with spans of their own, so the builder made segments of them, which neither output shows.
        var elsewhere = "iflow-map-elsewhere-" + Guid.NewGuid().ToString("N");
        var at = DateTimeOffset.UtcNow.AddSeconds(-40);
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            var trace = ActivityTraceId.CreateRandom();
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Request, at.AddMilliseconds(i * 20)));
            Log(Call(elsewhere, HttpMethod.Get, id, trace, RequestResponseType.Response, at.AddMilliseconds(i * 20 + 5)));
            Span("elsewhere.root", trace, at.AddMilliseconds(i * 20 + 1), 2);
        }

        var run = Generate(InternalFlowNoDataBehavior.HideLink);

        var linkedInPage = LinkedIds(DiagramSourcesInPage(run.Html));
        Assert.Contains($"iflow-{run.First}", linkedInPage);
        Assert.All(SegmentKeysInPage(run.Html), key => Assert.Contains(key, linkedInPage));

        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        var root = data.RootElement;
        var linkedInData = LinkedIds(root.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .SelectMany(s => s.TryGetProperty("diagrams", out var d) ? d.EnumerateArray().Select(x => ReportPayloadText.Of(x) ?? "") : []));
        Assert.All(root.GetProperty("internalFlowSegments").EnumerateObject(), p => Assert.Contains(p.Name, linkedInData));
    }

    [Fact]
    public void A_call_the_arrow_cap_leaves_undrawn_has_no_segment()
    {
        // With one arrow a diagram, the second call is not drawn, so no arrow links its segment.
        var run = Generate(InternalFlowNoDataBehavior.HideLink, configure: o => o.MaxArrowsPerDiagram = 1);

        Assert.DoesNotContain($"iflow-{run.Second}", LinkedIds(DiagramSourcesInPage(run.Html)));
        Assert.Contains($"iflow-{run.First}", SegmentKeysInPage(run.Html));
        Assert.DoesNotContain($"iflow-{run.Second}", SegmentKeysInPage(run.Html));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        Assert.False(data.RootElement.GetProperty("internalFlowSegments").TryGetProperty($"iflow-{run.Second}", out _),
            "the data file carries no segment for the undrawn call");
    }

    [Fact]
    public void A_scenario_that_made_no_call_has_no_whole_test_flow()
    {
        var run = Generate(InternalFlowNoDataBehavior.HideLink);

        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        var flows = data.RootElement.GetProperty("wholeTestFlow");

        // The scenario with only a step bar and an assertion note has no trace id of its own, and was given every
        // span of the run.
        Assert.False(flows.TryGetProperty(run.QuietScenarioId, out _), "a scenario that made no call has no whole-test flow");
        Assert.True(flows.TryGetProperty(run.CallsScenarioId, out _), "the scenario that made calls keeps its whole-test flow");
    }

    [Fact]
    public void A_marker_draws_no_boundary_line_in_the_whole_test_flame_chart()
    {
        var run = Generate(InternalFlowNoDataBehavior.HideLink);

        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "TestRunReport.json")));
        var flows = data.RootElement.GetProperty("wholeTestFlow");

        // A dashed line per call, and none for the step bar and the assertion note, which drew one each labelled ": /".
        var flame = Property(flows.GetProperty(run.CallsScenarioId), "FlameHtml").GetString()!;
        Assert.Equal([$"GET: /iflow-map/{run.First}", $"POST: /iflow-map/{run.Second}"], FlameBoundaryLabels(flame));

        // The page draws the same lines: its whole-test flame chart is built by the same code for the live report.
        var pageFlames = Regex.Matches(run.Html, "<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"[^\"]*\"></div>")
            .Select(m => FlameBoundaryLabels(m.Value))
            .Where(labels => labels.Contains($"GET: /iflow-map/{run.First}"))
            .ToArray();
        Assert.Equal([$"GET: /iflow-map/{run.First}", $"POST: /iflow-map/{run.Second}"], Assert.Single(pageFlames));
    }

    private sealed record Run(string Html, HashSet<string> Own, Guid First, Guid Second, string CallsScenarioId, string QuietScenarioId, Guid Nested);

    private Run Generate(InternalFlowNoDataBehavior noData, bool nestedCall = false, Action<ReportConfigurationOptions>? configure = null)
    {
        var callsId = "iflow-map-" + Guid.NewGuid().ToString("N");
        var quietId = "iflow-map-quiet-" + Guid.NewGuid().ToString("N");

        // The markers come from the real writers, stamped by the logger as in a run; the calls are placed around them.
        DefaultTrackingDiagramOverride.InsertPlantUml(callsId, StepBarPlantUml.Build("Given a basket", null, null), DiagramMarkerKind.Step);
        DefaultTrackingDiagramOverride.InsertPlantUml(callsId, "hnote across <<assertionNote>> #DFF0D8\n✓ the basket holds 2 items\nend note", DiagramMarkerKind.Assertion);
        DefaultTrackingDiagramOverride.InsertPlantUml(quietId, StepBarPlantUml.Build("Given nothing to call", null, null), DiagramMarkerKind.Step);
        DefaultTrackingDiagramOverride.InsertPlantUml(quietId, "hnote across <<assertionNote>> #DFF0D8\n✓ nothing was called\nend note", DiagramMarkerKind.Assertion);

        var markers = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == callsId).ToArray();
        Assert.All(markers, m => Assert.True(m.IsDiagramMarker && m.Timestamp is not null && m.ActivityTraceId is null));
        var at = markers.Min(m => m.Timestamp!.Value);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstTrace = ActivityTraceId.CreateRandom();
        var secondTrace = ActivityTraceId.CreateRandom();

        Log(Call(callsId, HttpMethod.Get, first, firstTrace, RequestResponseType.Request, at.AddMilliseconds(-400)));
        Log(Call(callsId, HttpMethod.Get, first, firstTrace, RequestResponseType.Response, at.AddMilliseconds(-300)));
        Log(Call(callsId, HttpMethod.Post, second, secondTrace, RequestResponseType.Request, at.AddMilliseconds(300)));
        Log(Call(callsId, HttpMethod.Post, second, secondTrace, RequestResponseType.Response, at.AddMilliseconds(400)));
        // A call the first one's service made inside it, on the first call's trace: its popup shows first.root too.
        var nested = Guid.NewGuid();
        if (nestedCall)
        {
            Log(Call(callsId, HttpMethod.Get, nested, firstTrace, RequestResponseType.Request, at.AddMilliseconds(-390)));
            Log(Call(callsId, HttpMethod.Get, nested, firstTrace, RequestResponseType.Response, at.AddMilliseconds(-340)));
        }

        Span("first.root", firstTrace, at.AddMilliseconds(-401), 98);
        // Work the first call's service went on with after it answered: it falls in the step bar's window.
        Span("first.tail", firstTrace, at.AddMilliseconds(-20), 2);
        Span("second.root", secondTrace, at.AddMilliseconds(301), 98);

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Segment map",
                Scenarios =
                [
                    new Scenario { Id = callsId, DisplayName = "Two calls around a step bar and an assertion", Result = ExecutionResult.Passed },
                    new Scenario { Id = quietId, DisplayName = "No call at all", Result = ExecutionResult.Passed },
                ],
            },
        ];
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _directory,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            InternalFlowTracking = true,
            InternalFlowSpanGranularity = InternalFlowSpanGranularity.Full,
            InternalFlowNoDataBehavior = noData,
            GenerateMergeableData = true,
            GenerateComponentDiagram = false,
            WriteRunSummaryToConsole = false,
        };
        configure?.Invoke(options);

        DefaultDiagramsFetcher.Reset();
        try
        {
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(
                features, at.UtcDateTime.AddSeconds(-1), at.UtcDateTime.AddSeconds(1), options,
                RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
        }
        finally
        {
            DefaultDiagramsFetcher.Reset();
        }

        var own = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == callsId || l.TestId == quietId)
            .Select(l => $"iflow-{l.RequestResponseId}")
            .ToHashSet();
        return new Run(File.ReadAllText(Path.Combine(_directory, "TestRunReport.html")), own, first, second, callsId, quietId, nested);
    }

    private static void Log(RequestResponseLog log) => RequestResponseLogger.Log(log);

    private static RequestResponseLog Call(
        string testId, HttpMethod method, Guid id, ActivityTraceId trace, RequestResponseType type, DateTimeOffset at) =>
        new("Two calls", testId, method, null, new Uri($"http://orders/iflow-map/{id}"), [], "Orders", "Caller",
            type, Guid.NewGuid(), id, false, type == RequestResponseType.Response ? HttpStatusCode.OK : null)
        {
            Timestamp = at,
            ActivityTraceId = trace.ToString()
        };

    private void Span(string name, ActivityTraceId trace, DateTimeOffset start, int milliseconds)
    {
        Activity.Current = null;
        var span = _source.StartActivity(name, ActivityKind.Internal,
            new ActivityContext(trace, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
        span.SetStartTime(start.UtcDateTime);
        span.SetEndTime(start.UtcDateTime.AddMilliseconds(milliseconds));
        _spans.Add(span);
        InternalFlowSpanStore.Add(span);
    }

    /// <summary>The segment map's keys, read from the page's segment element (its <c>z</c>, decoded).</summary>
    internal static string[] SegmentKeysInPage(string html)
    {
        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var at = html.IndexOf(head, StringComparison.Ordinal);
        Assert.True(at >= 0, "the page carries a segment element");
        var start = at + head.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        using var element = JsonDocument.Parse(html[start..end]);
        using var map = JsonDocument.Parse(Gunzip(element.RootElement.GetProperty("z").GetString()!));
        return map.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    /// <summary>The page's segment map, decoded from its element's <c>z</c>.</summary>
    private static JsonElement PageMap(string html)
    {
        const string head = "<script id=\"iflow-segments\" type=\"application/json\">";
        var at = html.IndexOf(head, StringComparison.Ordinal);
        Assert.True(at >= 0, "the page carries a segment element");
        var start = at + head.Length;
        using var element = JsonDocument.Parse(html[start..html.IndexOf("</script>", start, StringComparison.Ordinal)]);
        using var map = JsonDocument.Parse(Gunzip(element.RootElement.GetProperty("z").GetString()!));
        return map.RootElement.Clone();
    }

    /// <summary>Every diagram source the page embeds in its <c>puml-data</c> block, decoded.</summary>
    internal static string[] DiagramSourcesInPage(string html)
    {
        // The tag whose text is the JSON map: the head's own scripts quote the tag in string literals.
        const string tag = "<script id=\"puml-data\" type=\"application/json\">";
        for (var at = html.IndexOf(tag, StringComparison.Ordinal); at >= 0; at = html.IndexOf(tag, at + tag.Length, StringComparison.Ordinal))
        {
            var start = at + tag.Length;
            if (start >= html.Length || html[start] != '{') continue;
            var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(html[start..end])!;
            return map.Values.Select(Gunzip).ToArray();
        }
        Assert.Fail("the page carries no puml-data block");
        return [];
    }

    internal static HashSet<string> LinkedIds(IEnumerable<string> sources) =>
        sources.SelectMany(s => Regex.Matches(s, @"\[\[#(iflow-[0-9a-f-]+)").Select(m => m.Groups[1].Value)).ToHashSet();

    private static string[] FlameBoundaryLabels(string flameHtml)
    {
        var z = Regex.Match(flameHtml, "data-flame-z=\"([^\"]*)\"").Groups[1].Value;
        using var flame = JsonDocument.Parse(Gunzip(z));
        return flame.RootElement.TryGetProperty("m", out var markers)
            ? markers.EnumerateArray().Select(m => m[1].GetString()!).ToArray()
            : [];
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Gunzip(string base64)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
