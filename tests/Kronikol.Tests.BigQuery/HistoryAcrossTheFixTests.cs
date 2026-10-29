using System.Net;
using Kronikol.Extensions.BigQuery;
using Kronikol.History;
using Kronikol.Tests.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// History across #125's fix, replayed (plans/EMULATOR_URL_SHAPES_PLAN.md 6.3). A suite that makes each request of
/// <see cref="RecordedBefore9Record"/>, one scenario per request, is recorded twice: once as an upgrade records it, the
/// first four runs by the published 5.27.2 package, from what the record says it recorded, and the rest by this build;
/// and once by this build throughout. After the upgrade the two ledgers give the same verdicts, scenario by scenario:
/// the renamed calls read as no change, with no quiet run, and a real change at the upgrade still reads as changed.
/// </summary>
public class HistoryAcrossTheFixTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-bigquery-replay").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly string ThisBuild = typeof(BigQueryTrackingMessageHandler).Assembly.GetName().Version!.ToString(3);

    public static TheoryData<BigQueryTrackingVerbosity> Verbosities => new() { BigQueryTrackingVerbosity.Detailed, BigQueryTrackingVerbosity.Summarised };

    [Theory]
    [MemberData(nameof(Verbosities))]
    public async Task An_upgraded_suite_reads_the_verdicts_this_build_reads_throughout(BigQueryTrackingVerbosity verbosity)
    {
        var rows = Rows(verbosity);
        // A real change at the upgrade: a dataset read on Google's endpoint, which 5.27.2 drew as Read /sales/sales,
        // answers 404 from the fifth run on.
        var changed = rows.Where(r => r.Method == "GET" && r.Url.StartsWith("https://bigquery.googleapis.com/bigquery/v2/projects/shop/datasets/sales?", StringComparison.Ordinal))
            .Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(changed);
        var replay = new HistoryReplay(_dir, rows.Select(r => r.Id).ToArray());
        var upgraded = Path.Combine(_dir, "upgraded", "history.jsonl");
        var throughout = Path.Combine(_dir, "throughout", "history.jsonl");
        for (var n = 1; n <= 4; n++)
        {
            replay.RecordAsReleased(upgraded, await PublishedPackageRecorded(rows), n, 8, "5.27.2");
            replay.Record(throughout, await ThisBuildRecords(rows), n, ThisBuild);
        }

        for (var n = 5; n <= 8; n++)
        {
            var logs = await ThisBuildRecords(rows, changed);
            var afterUpgrade = replay.Record(upgraded, logs, n, ThisBuild);
            var byThisBuild = replay.Record(throughout, logs, n, ThisBuild);
            for (var i = 0; i < rows.Length; i++)
            {
                var a = afterUpgrade.Scenarios[i];
                var b = byThisBuild.Scenarios[i];
                Assert.True(a.Verdicts.Order().SequenceEqual(b.Verdicts.Order()),
                    $"run {n}, {rows[i].Method} {rows[i].Url}: [{string.Join(", ", a.Verdicts)}] after the upgrade, " +
                    $"[{string.Join(", ", b.Verdicts)}] by this build throughout. {a.Evidence} | {b.Evidence}");
                Assert.Equal(n == 5 && changed.Contains(rows[i].Id), a.Verdicts.Contains(HistoryVerdictKind.BehaviourChanged));
            }
            Assert.Equal(byThisBuild.NewDependencies, afterUpgrade.NewDependencies);
        }
    }

    [Fact]
    public async Task A_real_change_at_the_upgrade_reads_as_changed_and_names_the_call()
    {
        var rows = Rows(BigQueryTrackingVerbosity.Detailed);
        var changed = rows.First(r => r.Method == "GET" && r.Url.StartsWith("https://bigquery.googleapis.com/bigquery/v2/projects/shop/datasets/sales?", StringComparison.Ordinal));
        var replay = new HistoryReplay(_dir, rows.Select(r => r.Id).ToArray());
        var ledger = Path.Combine(_dir, "named", "history.jsonl");
        for (var n = 1; n <= 4; n++)
            replay.RecordAsReleased(ledger, await PublishedPackageRecorded(rows), n, 8, "5.27.2");

        var scenario = replay.Record(ledger, await ThisBuildRecords(rows, new HashSet<string>(StringComparer.Ordinal) { changed.Id }), 5, ThisBuild)
            .Scenarios[Array.IndexOf(rows, changed)];

        // The run is compared with the runs before it under their rule, through its bridge, so the call is named as
        // 5.27.2 drew it.
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Contains("new: TestCaller>BigQuery Read /sales/sales 404; gone: TestCaller>BigQuery Read /sales/sales 200",
            scenario.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_service_every_call_to_which_Summarised_dropped_is_a_new_dependency_once()
    {
        // The one note the upgrade makes, and a true one: on an emulator's root, in a suite that uploads nothing, 5.27.2
        // recorded no call to the service at all.
        var rows = Rows(BigQueryTrackingVerbosity.Summarised).Where(r => r.Label == "-").ToArray();
        Assert.NotEmpty(rows);
        var replay = new HistoryReplay(_dir, rows.Select(r => r.Id).ToArray());
        var ledger = Path.Combine(_dir, "dropped", "history.jsonl");
        for (var n = 1; n <= 4; n++)
            replay.RecordAsReleased(ledger, await PublishedPackageRecorded(rows), n, 8, "5.27.2");

        for (var n = 5; n <= 8; n++)
        {
            var verdicts = replay.Record(ledger, await ThisBuildRecords(rows), n, ThisBuild);

            Assert.Equal(n == 5 ? ["TestCaller>BigQuery"] : [], verdicts.NewDependencies);
            Assert.All(verdicts.Scenarios, s => Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, s.Verdicts));
        }
    }

    private sealed record Row(string Id, BigQueryTrackingVerbosity Verbosity, string Method, string Url, string Label, string Uri);

    private static Row[] Rows(BigQueryTrackingVerbosity verbosity) => RecordedBefore9Record.Lines
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r').Split('\t'))
        .Where(f => f[0] == verbosity.ToString())
        .Select((f, i) => new Row($"r{i}", verbosity, f[1], f[2], f[3], f[4]))
        .ToArray();

    // What 5.27.2 recorded for each row's request: its label and clean URI, answered as the stub answers, or no call where
    // it recorded none.
    private static async Task<RequestResponseLog[]> PublishedPackageRecorded(Row[] rows)
    {
        var logs = new List<RequestResponseLog>();
        foreach (var row in rows.Where(r => r.Label != "-"))
            logs.AddRange((await Send(row, HttpStatusCode.OK)).Select(l => l with { TestId = row.Id, Method = row.Label, Uri = new Uri(row.Uri), RecordedBefore9 = null }));
        return logs.ToArray();
    }

    // What this build records for each row's request, the server answering 404 to the rows in changed and 200 to the rest.
    private static async Task<RequestResponseLog[]> ThisBuildRecords(Row[] rows, IReadOnlySet<string>? changed = null)
    {
        var logs = new List<RequestResponseLog>();
        foreach (var row in rows)
            logs.AddRange((await Send(row, changed?.Contains(row.Id) == true ? HttpStatusCode.NotFound : HttpStatusCode.OK)).Select(l => l with { TestId = row.Id }));
        return logs.ToArray();
    }

    private static async Task<RequestResponseLog[]> Send(Row row, HttpStatusCode status)
    {
        var testId = Guid.NewGuid().ToString();
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            ServiceName = "BigQuery",
            CallerName = "TestCaller",
            Verbosity = row.Verbosity,
            CurrentTestInfoFetcher = () => ("history across the fix", testId),
        };
        using var invoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(options, new Answering(status)));
        (await invoker.SendAsync(new HttpRequestMessage(new HttpMethod(row.Method), row.Url), CancellationToken.None)).Dispose();
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
    }

    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
}
