using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tool.Query;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>flow</c>'s indentation read the way a tree is read: a line belongs to the nearest line above it with less
/// indentation, whatever kind of line that is. A call is indented two spaces per shown call it ran inside, and
/// names the call it ran inside (<c>inside s3/i8</c>) exactly when the line its indentation points at is not
/// that call (plans/FLOW_NESTING_PLAN.md §4.3, F15, F18).
///
/// <para>The fixtures of <see cref="FlowTests"/> hold the shapes the plan named, and a note recorded while a call
/// was open was not one of them: from 3.31.0 the calls after such a note read as the note's, and named nothing.
/// So the reading is checked here on scenarios made from a seed, with every kind of line the verb prints: calls
/// several levels deep, a party's calls answered in any order, deliveries on an open call's trace, requests never
/// answered or answered before they were recorded, requests with no pairing id, user actions, steps that begin
/// while calls are open, and notes anywhere, each view unfiltered, under <c>--service</c>, <c>--step</c> and
/// <c>--errors-only</c>.</para>
/// </summary>
public class FlowTreeTests(FlowTreeTests.GeneratedViews generated) : IClassFixture<FlowTreeTests.GeneratedViews>
{
    [Fact]
    public void Every_line_reads_as_a_tree_of_the_calls_it_ran_inside()
    {
        var misread = generated.Views
            .SelectMany(v => Misreadings(v.Parents, v.Output).Select(m => $"flow {v.Scenario} {(v.View.Length == 0 ? "(unfiltered)" : string.Join(' ', v.View))}: {m}"))
            .ToList();

        Assert.True(misread.Count == 0, $"{misread.Count} lines misread:\n{string.Join("\n", misread.Take(12))}");
    }

    [Fact]
    public void The_generated_scenarios_hold_every_shape_the_reading_must_survive()
    {
        // A generator that stopped making one of these would let the fact above pass without testing it.
        var views = generated.Views;
        var unfiltered = views.Where(v => v.View.Length == 0).ToList();

        Assert.Contains(views, v => v.Parents.Any(p => Depth(v.Parents, p) >= 3));
        Assert.Contains(unfiltered, v => NoteBetweenACallAndOneInsideIt(v.Parents, v.Output));
        Assert.Contains(unfiltered, v => v.Output.Contains("  inside ", StringComparison.Ordinal));
        Assert.Contains(views, v => v.View is ["--service", _] && v.Output.Contains("  inside ", StringComparison.Ordinal));
        Assert.Contains(views, v => v.View is ["--errors-only"] && v.Output.Contains("  inside ", StringComparison.Ordinal));
        Assert.Contains(unfiltered, v => v.Output.Contains("  no response", StringComparison.Ordinal));
    }

    // ─── The reading ───────────────────────────────────────────

    private static readonly Regex CallLine = new(@"^ *s\d+/i(?<ordinal>\d+) ", RegexOptions.Compiled);
    private static readonly Regex Inside = new(@"  inside s\d+/i(?<parent>\d+)$", RegexOptions.Compiled);

    /// <summary>Each call line of one view whose indentation or <c>inside</c> says something rule R4 does not.</summary>
    private static IEnumerable<string> Misreadings(int?[] parents, string output)
    {
        var lines = Body(output);
        var shown = lines.Select(line => Ordinal(line)).OfType<int>().ToHashSet();
        var above = new List<(int Indent, int? Ordinal)>();

        foreach (var line in lines)
        {
            var indent = line.Length - line.TrimStart(' ').Length;
            if (Ordinal(line) is not { } ordinal)
            {
                above.Add((indent, null));
                continue;
            }

            var parent = parents[ordinal];
            var depth = 0;
            for (var ancestor = parent; ancestor is { } a; ancestor = parents[a])
                if (shown.Contains(a))
                    depth++;
            if (indent != 2 + 2 * depth)
                yield return $"indented {indent} with {depth} shown calls it ran inside: {line}";

            // What indentation alone says: the nearest line above with less of it, whatever that line is.
            var reads = above.LastOrDefault(l => l.Indent < indent, (0, null)).Ordinal;
            var named = Inside.Match(line);
            if (named.Success && int.Parse(named.Groups["parent"].Value, CultureInfo.InvariantCulture) != parent)
                yield return $"names i{named.Groups["parent"].Value}, ran inside i{parent}: {line}";
            else if (named.Success && reads == parent)
                yield return $"names the call its indentation already shows: {line}";
            else if (!named.Success && parent is not null && reads != parent)
                yield return $"reads as inside {(reads is { } r ? $"i{r}" : "a line that is not a call")}, ran inside i{parent}: {line}";

            above.Add((indent, ordinal));
        }
    }

    private static bool NoteBetweenACallAndOneInsideIt(int?[] parents, string output)
    {
        var lines = Body(output);
        for (var k = 0; k < lines.Count; k++)
        {
            if (Ordinal(lines[k]) is not { } ordinal || parents[ordinal] is not { } parent)
                continue;
            var parentLine = lines.FindIndex(line => Ordinal(line) == parent);
            if (parentLine >= 0 && lines.Skip(parentLine + 1).Take(k - parentLine - 1).Any(line => line.StartsWith("  ── ", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    /// <summary>The lines under the title, the footer included.</summary>
    private static List<string> Body(string output) => [.. output.Split('\n').Skip(2).Where(line => line.Length > 0)];

    private static int? Ordinal(string line) =>
        CallLine.Match(line) is { Success: true } call ? int.Parse(call.Groups["ordinal"].Value, CultureInfo.InvariantCulture) : null;

    private static int Depth(int?[] parents, int? parent)
    {
        var depth = 0;
        for (var ancestor = parent; ancestor is { } a; ancestor = parents[a])
            depth++;
        return depth;
    }

    // ─── Fixture ───────────────────────────────────────────────

    /// <summary>
    /// A hundred and twenty scenarios of two or three steps, made from one seed, and every view of each: written
    /// and run once for the class.
    /// </summary>
    public sealed class GeneratedViews : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-flow-tree").FullName;

        public GeneratedViews()
        {
            var report = Report(_directory);
            string[][] views = [[], ["--service", "db"], ["--step", "1"], ["--errors-only"]];
            foreach (var scenario in ReportScanner.Scan(report).Scenarios)
            {
                var parents = CallNesting.Parents(scenario.Interactions);
                foreach (var view in views)
                    Views.Add((scenario.Address, parents, view, Run(report, [scenario.Address, .. view])));
            }
        }

        public List<(string Scenario, int?[] Parents, string[] View, string Output)> Views { get; } = [];

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    private static string Run(string report, string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["flow", report, .. args, "--max-bytes", "0"], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private static string Report(string directory)
    {
        var random = new Random(20260927);
        var scenarios = new List<Scenario>();
        var logs = new List<RequestResponseLog>();
        for (var n = 0; n < 120; n++)
        {
            var testId = $"g{n}";
            var steps = random.Next(2, 4);
            scenarios.Add(new Scenario
            {
                Id = testId, DisplayName = $"Generated {n}", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                Steps = [.. Enumerable.Range(0, steps).Select(k => new ScenarioStep { Keyword = "When", Text = $"step {k}", Status = ExecutionResult.Passed })]
            });
            logs.AddRange(Generate(random, testId, steps));
        }

        var written = ReportGenerator.GenerateTestRunReportData(
            [new Feature { DisplayName = "Generated", Scenarios = [.. scenarios] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Tree_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs.ToArray());

        var path = Path.Combine(directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private sealed class Call
    {
        public required string Caller { get; init; }
        public required string Service { get; init; }
        public required string Path { get; init; }
        public required Guid Trace { get; init; }
        public required Guid Id { get; init; }
        public bool Never { get; init; }
        public bool UserAction { get; init; }
    }

    /// <summary>
    /// One scenario's records: the test's calls, the calls a service makes while it handles an open one (on that
    /// call's trace or a new one), deliveries on an open call's trace, answers in any order, and the rest.
    /// </summary>
    private static List<RequestResponseLog> Generate(Random random, string testId, int steps)
    {
        string[] services = ["api", "svc1", "svc2", "db", "cache"];
        HttpStatusCode?[] statuses =
            [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, null];
        var logs = new List<RequestResponseLog>();
        var open = new List<Call>();
        var step = 0;
        var serial = 0;

        Guid NewId()
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes);
        }

        Call NewCall(string caller, string service, Guid trace, Guid id, bool never = false, bool userAction = false) => new()
        {
            Caller = caller, Service = service, Path = $"r{serial++}", Trace = trace, Id = id, Never = never, UserAction = userAction
        };

        void Send(Call call) => logs.Add(Log(testId, call, RequestResponseType.Request, null));
        void Answer(Call call) => logs.Add(Log(testId, call, RequestResponseType.Response, statuses[random.Next(statuses.Length)]));

        // Most scenarios start in a step; the rest record calls before the first one.
        if (random.NextDouble() >= 0.2)
            logs.Add(StepMarker(testId, $"step {step++}"));

        for (var events = random.Next(2, 15); events > 0; events--)
        {
            var roll = random.NextDouble();
            if (roll < 0.40 || open.Count == 0)
            {
                var handling = open.Where(c => !c.UserAction).ToList();
                var by = handling.Count > 0 && random.NextDouble() < 0.75 ? handling[random.Next(handling.Count)] : null;
                var caller = by?.Service ?? "test";
                var called = services.Where(s => s != caller).ToArray();
                var call = NewCall(caller, called[random.Next(called.Length)],
                    by is not null && random.NextDouble() < 0.3 ? by.Trace : NewId(),
                    random.NextDouble() < 0.05 ? Guid.Empty : NewId(),
                    never: random.NextDouble() < 0.06);
                Send(call);
                open.Add(call);
            }
            else if (roll < 0.48)
            {
                var call = NewCall("broker", services[random.Next(services.Length)], open[random.Next(open.Count)].Trace, NewId());
                Send(call);
                open.Add(call);
            }
            else if (roll < 0.78)
            {
                var call = open[random.Next(open.Count)];
                open.Remove(call);
                if (!call.Never && !call.UserAction)
                    Answer(call);
            }
            else if (roll < 0.84)
            {
                if (step < steps)
                    logs.Add(StepMarker(testId, $"step {step++}"));
            }
            else if (roll < 0.90)
            {
                logs.Add(Marker(testId, $"note across : note {serial++}"));
            }
            else if (roll < 0.94)
            {
                Send(NewCall("User", "web", NewId(), NewId(), userAction: true));
            }
            else
            {
                // Answered before it was recorded.
                var call = NewCall("test", services[random.Next(services.Length)], NewId(), NewId());
                Answer(call);
                Send(call);
            }
        }

        foreach (var call in open.OrderBy(_ => random.Next()).ToList())
            if (!call.Never && !call.UserAction)
                Answer(call);

        return logs;
    }

    private static RequestResponseLog Log(string testId, Call call, RequestResponseType type, HttpStatusCode? status) =>
        new(testId, testId, call.UserAction ? "Click" : call.Caller == "broker" ? "CONSUME" : "GET", null,
            new Uri($"http://{call.Service}/{call.Path}"), [], call.Service, call.Caller, type, call.Trace, call.Id, false,
            status is { } code ? code : null)
        { IsUserAction = call.UserAction };

    private static RequestResponseLog StepMarker(string testId, string stepText) =>
        Marker(testId, "hnote across <<stepDelimiter>> #black:<color:white>" + stepText, DiagramMarkerKind.Step);

    private static RequestResponseLog Marker(string testId, string plantUml, DiagramMarkerKind kind = DiagramMarkerKind.Custom) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };
}
