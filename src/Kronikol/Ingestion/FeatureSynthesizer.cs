using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>
/// Builds the <see cref="Feature"/>[] model the report generator needs from tests-file records
/// (<see cref="TestRunRecord"/>) and/or the ingested interaction logs. A scenario is created for every
/// distinct test id seen in either source; outcome/duration/steps come from the tests file when present.
/// </summary>
public static class FeatureSynthesizer
{
    /// <summary>Result of <see cref="Build"/>.</summary>
    /// <param name="Features">Features grouped by <see cref="TestRunRecord.Feature"/> (or the default feature name).</param>
    /// <param name="Start">Earliest timestamp observed (UTC) — the run start.</param>
    /// <param name="End">Latest timestamp observed (UTC) — the run end.</param>
    /// <param name="TestNames">Display name per test id, for normalising log <c>TestName</c>s.</param>
    public sealed record Result(Feature[] Features, DateTime Start, DateTime End, IReadOnlyDictionary<string, string> TestNames);

    /// <summary>
    /// Synthesises features. <paramref name="defaultFeatureName"/> groups scenarios that carry no feature.
    /// <paramref name="resultWhenUnknown"/> is the verdict for tests that have interactions but no <c>end</c> record.
    /// </summary>
    public static Result Build(
        IEnumerable<TestRunRecord>? testRecords,
        IEnumerable<RequestResponseLog>? logs,
        string defaultFeatureName = "Ingested",
        ExecutionResult resultWhenUnknown = ExecutionResult.Passed)
    {
        var records = (testRecords ?? []).ToList();
        var logList = (logs ?? []).ToList();

        var byTest = new Dictionary<string, TestAccumulator>(StringComparer.Ordinal);
        var order = new List<string>();

        TestAccumulator Get(string testId)
        {
            if (!byTest.TryGetValue(testId, out var acc))
            {
                acc = new TestAccumulator(testId);
                byTest[testId] = acc;
                order.Add(testId);
            }

            return acc;
        }

        foreach (var record in records.OrderBy(r => r.Timestamp ?? DateTimeOffset.MinValue))
        {
            if (string.IsNullOrWhiteSpace(record.TestId))
                continue;
            var acc = Get(record.TestId);
            if (!string.IsNullOrWhiteSpace(record.TestName)) acc.Name = record.TestName;
            if (!string.IsNullOrWhiteSpace(record.Feature)) acc.Feature = record.Feature;

            switch (record.Event?.ToLowerInvariant())
            {
                case "start":
                    acc.Start ??= record.Timestamp;
                    break;
                case "step":
                    acc.Steps.Add(record);
                    break;
                case "end":
                    acc.End = record.Timestamp ?? acc.End;
                    acc.Status = record.Status ?? acc.Status;
                    acc.DurationMs = record.DurationMs ?? acc.DurationMs;
                    acc.Error = record.Error ?? acc.Error;
                    acc.HasEnd = true;
                    break;
            }
        }

        foreach (var log in logList)
        {
            if (string.IsNullOrWhiteSpace(log.TestId))
                continue;
            var acc = Get(log.TestId);
            acc.Name ??= string.IsNullOrWhiteSpace(log.TestName) || log.TestName == TestIdentityScope.UnknownTestName ? null : log.TestName;
            acc.HasLogs = true;
            if (log.Timestamp is { } ts)
            {
                acc.FirstLog = acc.FirstLog is null || ts < acc.FirstLog ? ts : acc.FirstLog;
                acc.LastLog = acc.LastLog is null || ts > acc.LastLog ? ts : acc.LastLog;
            }
        }

        var featureGroups = new Dictionary<string, List<Scenario>>(StringComparer.Ordinal);
        var featureOrder = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        DateTimeOffset? runStart = null, runEnd = null;

        foreach (var testId in order)
        {
            var acc = byTest[testId];
            var name = acc.Name ?? testId;
            names[testId] = name;

            var scenario = new Scenario
            {
                Id = testId,
                DisplayName = name,
                Result = acc.HasEnd ? MapStatus(acc.Status) : resultWhenUnknown,
                ErrorMessage = acc.Error,
                Duration = acc.DurationMs is { } ms ? TimeSpan.FromMilliseconds(ms)
                    : acc.Start is { } s && acc.End is { } e && e >= s ? e - s
                    : acc.FirstLog is { } f && acc.LastLog is { } l && l >= f ? l - f
                    : null,
                Steps = acc.Steps.Count == 0 ? null : acc.Steps.Select(st => new ScenarioStep
                {
                    Text = st.Text ?? "(step)",
                    Keyword = st.Keyword,
                    Status = st.Status is null ? null : MapStatus(st.Status),
                    Duration = st.DurationMs is { } d ? TimeSpan.FromMilliseconds(d) : null,
                }).ToArray(),
            };

            var feature = acc.Feature ?? defaultFeatureName;
            if (!featureGroups.TryGetValue(feature, out var list))
            {
                list = [];
                featureGroups[feature] = list;
                featureOrder.Add(feature);
            }

            list.Add(scenario);

            foreach (var candidate in new[] { acc.Start, acc.End, acc.FirstLog, acc.LastLog })
            {
                if (candidate is not { } c) continue;
                runStart = runStart is null || c < runStart ? c : runStart;
                runEnd = runEnd is null || c > runEnd ? c : runEnd;
            }
        }

        var features = featureOrder
            .Select(f => new Feature { DisplayName = f, Scenarios = featureGroups[f].ToArray() })
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        return new Result(
            features,
            (runStart ?? now).UtcDateTime,
            (runEnd ?? runStart ?? now).UtcDateTime,
            names);
    }

    /// <summary>Maps a test-runner status word to <see cref="ExecutionResult"/> (Playwright, Jest, JUnit and xUnit vocabularies).</summary>
    public static ExecutionResult MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "passed" or "pass" or "ok" or "success" or "succeeded" => ExecutionResult.Passed,
        "failed" or "fail" or "error" or "timedout" or "timed-out" or "timeout" or "interrupted" or "broken" => ExecutionResult.Failed,
        "skipped" or "skip" or "pending" or "todo" or "ignored" => ExecutionResult.Skipped,
        "bypassed" => ExecutionResult.Bypassed,
        "skippedafterfailure" => ExecutionResult.SkippedAfterFailure,
        _ => ExecutionResult.Failed,
    };

    private sealed class TestAccumulator(string testId)
    {
        public string TestId { get; } = testId;
        public string? Name { get; set; }
        public string? Feature { get; set; }
        public DateTimeOffset? Start { get; set; }
        public DateTimeOffset? End { get; set; }
        public string? Status { get; set; }
        public double? DurationMs { get; set; }
        public string? Error { get; set; }
        public bool HasEnd { get; set; }
        public bool HasLogs { get; set; }
        public DateTimeOffset? FirstLog { get; set; }
        public DateTimeOffset? LastLog { get; set; }
        public List<TestRunRecord> Steps { get; } = [];
    }
}
