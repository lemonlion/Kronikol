using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// T19 of plans/WARM_UP_PLAN.md: over two lanes of BreakfastProvider's CI run of 2026-10-05 (the harness's reduced
/// corpus, <c>TestData/WarmUp/*.calls.json.gz</c>), the pass marks exactly the calls <c>warmup.py marks --json</c>
/// printed (<c>expected-marks.json</c>), with the same baselines, and charges each scenario the same warm-up.
/// </summary>
public class WarmUpCorpusTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestData", "WarmUp");

    [Theory]
    [InlineData("xunit")]
    [InlineData("reqnroll")]
    public void The_pass_marks_what_the_spec_marks(string lane)
    {
        var (features, logs) = Load(Path.Combine(Folder, lane + ".calls.json.gz"));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expected-marks.json")));
        var want = expected.RootElement.GetProperty(lane);

        var found = WarmUpCalls.Find(features, logs);

        var wantCalls = want.GetProperty("calls").EnumerateObject().ToDictionary(p => Guid.Parse(p.Name), p => p.Value);
        Assert.Equal(wantCalls.Keys.Order(), found.Calls.Keys.Order());
        foreach (var (id, mark) in found.Calls)
        {
            var spec = wantCalls[id];
            Assert.Equal(spec.GetProperty("kind").GetString(), mark.Kind);
            Assert.Equal(spec.GetProperty("shape").GetString(), mark.Shape);
            Assert.Equal(spec.GetProperty("baselineMs").GetDouble(), mark.BaselineMs, 6);
            Assert.Equal(spec.GetProperty("baselineCalls").GetInt32(), mark.BaselineCalls);
            Assert.Equal(spec.GetProperty("first").ValueKind == JsonValueKind.Null ? null : Guid.Parse(spec.GetProperty("first").GetString()!), mark.First);
        }

        var wantScenarios = want.GetProperty("scenarios").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        Assert.Equal(wantScenarios.Keys.Order(StringComparer.Ordinal), found.ScenarioMs.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, ms) in found.ScenarioMs)
            Assert.InRange(ms, wantScenarios[id] - 1, wantScenarios[id] + 1);
    }

    [Fact]
    public void The_corpus_carries_marks_of_both_kinds()
    {
        // Guards the fact above: two lanes that marked nothing, or no waiter, would pass it while proving little.
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expected-marks.json")));
        var kinds = expected.RootElement.EnumerateObject()
            .SelectMany(lane => lane.Value.GetProperty("calls").EnumerateObject())
            .Select(call => call.Value.GetProperty("kind").GetString())
            .ToHashSet();

        Assert.Equal(["first", "waited"], kinds.Order());
    }

    /// <summary>
    /// The reduced corpus back as a run: each call a request carrying its measured duration (so the pass reads the very
    /// number the script read) and a response carrying its status.
    /// </summary>
    private static (Feature[] Features, List<RequestResponseLog> Logs) Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var corpus = JsonDocument.Parse(gzip);

        var features = new Dictionary<string, List<Scenario>>(StringComparer.Ordinal);
        var logs = new List<RequestResponseLog>();
        foreach (var scenario in corpus.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var id = scenario.GetProperty("id").GetString()!;
            var feature = scenario.GetProperty("feature").GetString() ?? "";
            if (!features.TryGetValue(feature, out var scenarios))
                features[feature] = scenarios = [];
            scenarios.Add(new Scenario
            {
                Id = id,
                DisplayName = scenario.GetProperty("name").GetString() ?? "",
                Result = ExecutionResult.Passed,
                Duration = scenario.GetProperty("durationSeconds").ValueKind == JsonValueKind.Number
                    ? TimeSpan.FromSeconds(scenario.GetProperty("durationSeconds").GetDouble())
                    : null
            });

            foreach (var call in scenario.GetProperty("calls").EnumerateArray())
            {
                var rid = Guid.Parse(call.GetProperty("rid").GetString()!);
                var trace = Guid.NewGuid();
                var uri = new Uri(call.GetProperty("uri").GetString()!, UriKind.RelativeOrAbsolute);
                var method = call.GetProperty("method").GetString()!;
                var meta = call.GetProperty("meta").GetString() == "Event" ? RequestResponseMetaType.Event : RequestResponseMetaType.Default;
                var category = call.GetProperty("category").ValueKind == JsonValueKind.String ? call.GetProperty("category").GetString() : null;
                var (service, caller) = (call.GetProperty("service").GetString()!, call.GetProperty("caller").GetString()!);
                logs.Add(new RequestResponseLog(id, id, method, null, uri, [], service, caller, RequestResponseType.Request, trace, rid, false, null, meta, category)
                {
                    Timestamp = call.GetProperty("start").ValueKind == JsonValueKind.String
                        ? DateTimeOffset.Parse(call.GetProperty("start").GetString()!, CultureInfo.InvariantCulture)
                        : null,
                    DurationMs = call.GetProperty("durationMs").ValueKind == JsonValueKind.Number ? call.GetProperty("durationMs").GetDouble() : null
                });
                OneOf<HttpStatusCode, string>? status = call.GetProperty("status").ValueKind == JsonValueKind.Number
                    ? (HttpStatusCode)call.GetProperty("status").GetInt32()
                    : null;
                logs.Add(new RequestResponseLog(id, id, method, null, uri, [], service, caller, RequestResponseType.Response, trace, rid, false, status, meta, category));
            }
        }

        return (features.Select(f => new Feature { DisplayName = f.Key, Scenarios = f.Value.ToArray() }).ToArray(), logs);
    }
}
