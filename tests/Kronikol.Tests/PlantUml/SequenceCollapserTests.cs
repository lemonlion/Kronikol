using System.Net;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

public class SequenceCollapserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static List<RequestResponseLog> Pairs(params (string Path, int Status, int DurMs)[] calls)
    {
        var logs = new List<RequestResponseLog>();
        var t = T0;
        foreach (var (path, status, dur) in calls)
        {
            var rrId = Guid.NewGuid();
            var traceId = Guid.NewGuid();
            logs.Add(new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://bq" + path), [], "BigQuery", "DataInsights",
                RequestResponseType.Request, traceId, rrId, false) { Timestamp = t });
            logs.Add(new RequestResponseLog("Test", "t1", HttpMethod.Get, null, new Uri("http://bq" + path), [], "BigQuery", "DataInsights",
                RequestResponseType.Response, traceId, rrId, false, (HttpStatusCode)status) { Timestamp = t.AddMilliseconds(dur) });
            t = t.AddSeconds(1);
        }

        return logs;
    }

    [Fact]
    public void Collapses_a_run_of_identical_pairs_into_one_annotated_pair()
    {
        var logs = Pairs(("/q/1", 200, 10), ("/q/1", 200, 40), ("/q/1", 200, 25), ("/q/2", 200, 5));

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 2, maxPairs: null);

        Assert.Equal(1, result.CollapsedRuns);
        Assert.Equal(4, result.Traces.Count);
        var first = result.Traces[0];
        Assert.Equal(3, first.CollapsedCount);
        Assert.Equal("10–40 ms", first.CollapsedSummary);
        Assert.Equal(0, result.Traces[2].CollapsedCount);
        Assert.Equal("/q/2", result.Traces[2].Uri.AbsolutePath);
    }

    [Fact]
    public void Runs_shorter_than_the_threshold_are_left_alone()
    {
        var logs = Pairs(("/q/1", 200, 10), ("/q/1", 200, 10), ("/q/2", 200, 5));

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 3, maxPairs: null);

        Assert.Same(logs, result.Traces);
        Assert.Equal(0, result.CollapsedRuns);
    }

    [Fact]
    public void Different_status_codes_break_a_run()
    {
        var logs = Pairs(("/q/1", 200, 10), ("/q/1", 500, 10), ("/q/1", 200, 10));

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 2, maxPairs: null);

        Assert.Equal(0, result.CollapsedRuns);
    }

    [Fact]
    public void GraphQl_operation_name_is_part_of_the_identity()
    {
        var logs = new List<RequestResponseLog>();
        foreach (var op in new[] { "query A { a }", "query B { b }", "query B { b }" })
        {
            var rrId = Guid.NewGuid();
            logs.Add(new RequestResponseLog("T", "t1", HttpMethod.Post, $$"""{"query":"{{op}}"}""", new Uri("http://gql/graphql"), [], "Gql", "Web",
                RequestResponseType.Request, rrId, rrId, false));
            logs.Add(new RequestResponseLog("T", "t1", HttpMethod.Post, "{}", new Uri("http://gql/graphql"), [], "Gql", "Web",
                RequestResponseType.Response, rrId, rrId, false, HttpStatusCode.OK));
        }

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 2, maxPairs: null);

        Assert.Equal(1, result.CollapsedRuns);
        Assert.Equal(4, result.Traces.Count);
        Assert.Equal(2, result.Traces[2].CollapsedCount);
    }

    [Fact]
    public void Arrow_cap_drops_the_tail_and_reports_how_many_pairs_were_omitted()
    {
        var logs = Pairs(("/a", 200, 1), ("/b", 200, 1), ("/c", 200, 1), ("/d", 200, 1), ("/e", 200, 1));

        var result = SequenceCollapser.Apply(logs, collapse: false, threshold: 2, maxPairs: 2);

        Assert.Equal(4, result.Traces.Count);
        Assert.Equal(3, result.OmittedPairs);
        Assert.Equal("/b", result.Traces[2].Uri.AbsolutePath);
    }

    [Fact]
    public void Cap_counts_pairs_after_collapsing()
    {
        var logs = Pairs(("/poll", 200, 1), ("/poll", 200, 1), ("/poll", 200, 1), ("/x", 200, 1), ("/y", 200, 1));

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 2, maxPairs: 2);

        // poll×3 collapsed = 1 pair, /x = 2nd pair, /y omitted.
        Assert.Equal(1, result.OmittedPairs);
        Assert.Equal(3, result.Traces[0].CollapsedCount);
        Assert.Equal("/x", result.Traces[2].Uri.AbsolutePath);
    }

    [Fact]
    public void Override_markers_break_runs_and_pass_through()
    {
        var logs = Pairs(("/q", 200, 1), ("/q", 200, 1));
        var marker = new RequestResponseLog("T", "t1", HttpMethod.Get, null, new Uri("http://x/"), [], "S", "C", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = "note over S: hi\n" };
        logs.Insert(4, marker);
        logs.AddRange(Pairs(("/q", 200, 1), ("/q", 200, 1)));

        var result = SequenceCollapser.Apply(logs, collapse: true, threshold: 2, maxPairs: null);

        // Two separate runs of 2 either side of the marker; marker kept in place.
        Assert.Equal(2, result.CollapsedRuns);
        Assert.Equal(5, result.Traces.Count);
        Assert.True(result.Traces[2].IsOverrideStart);
        Assert.Equal(2, result.Traces[0].CollapsedCount);
        Assert.Equal(2, result.Traces[3].CollapsedCount);
    }

    [Fact]
    public void Nothing_configured_returns_the_same_list_instance()
    {
        var logs = Pairs(("/q", 200, 1), ("/q", 200, 1));
        var result = SequenceCollapser.Apply(logs, collapse: false, threshold: 2, maxPairs: null);
        Assert.Same(logs, result.Traces);
    }

    [Fact]
    public void Renderer_emits_a_loop_fragment_and_an_omission_line()
    {
        var logs = Pairs(("/poll", 200, 12), ("/poll", 200, 48), ("/poll", 200, 30), ("/done", 200, 3), ("/extra", 200, 3));

        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs,
            collapseConsecutiveIdenticalCalls: true, collapseThreshold: 2, maxArrowsPerDiagram: 2).ToArray();

        var plantUml = diagrams.Single().PlantUmls.Single().PlainText;
        Assert.Contains("loop ×3 · 12–48 ms", plantUml);
        Assert.Contains("...+1 more call omitted (MaxArrowsPerDiagram)...", plantUml);
        // One loop opened, one closed, and the loop wraps exactly the /poll pair.
        var lines = plantUml.Split('\n').Select(l => l.Trim()).ToArray();
        var loopIndex = Array.FindIndex(lines, l => l.StartsWith("loop "));
        var endIndex = Array.FindIndex(lines, loopIndex, l => l == "end");
        Assert.True(loopIndex >= 0 && endIndex > loopIndex);
        Assert.Contains(lines.Skip(loopIndex).Take(endIndex - loopIndex), l => l.Contains("/poll"));
        Assert.DoesNotContain(lines.Skip(loopIndex).Take(endIndex - loopIndex), l => l.Contains("/done"));
        Assert.DoesNotContain("/extra", plantUml);
        // Balanced: every loop has its end.
        Assert.Equal(lines.Count(l => l.StartsWith("loop ")), lines.Count(l => l == "end"));
    }

    [Fact]
    public void Renderer_is_unchanged_when_collapsing_is_off()
    {
        var logs = Pairs(("/poll", 200, 12), ("/poll", 200, 48));
        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.Single().PlainText;
        Assert.DoesNotContain("loop ", plantUml);
        Assert.Equal(2, plantUml.Split('\n').Count(l => l.Contains("GET: /poll")));
    }
}
