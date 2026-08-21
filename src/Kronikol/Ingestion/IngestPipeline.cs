using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>What to ingest and how to render it. See <see cref="IngestPipeline.Run"/>.</summary>
public sealed class IngestRequest
{
    /// <summary>NDJSON files of <see cref="InteractionRecord"/> lines (in addition to <see cref="Interactions"/>).</summary>
    public IReadOnlyList<string> InteractionFiles { get; init; } = [];

    /// <summary>In-memory interaction records (in addition to <see cref="InteractionFiles"/>).</summary>
    public IEnumerable<InteractionRecord>? Interactions { get; init; }

    /// <summary>Optional tests NDJSON file (<see cref="TestRunRecord"/> lines) supplying outcomes, durations and steps.</summary>
    public string? TestsFile { get; init; }

    /// <summary>In-memory test-run records (in addition to <see cref="TestsFile"/>).</summary>
    public IEnumerable<TestRunRecord>? TestRecords { get; init; }

    /// <summary>Report options. <c>ReportsFolderPath</c> decides the output directory. Defaults to <see cref="IngestPipeline.DefaultOptions"/>.</summary>
    public ReportConfigurationOptions? Options { get; init; }

    /// <summary>Clear the in-process store (<see cref="RequestResponseLogger.Clear"/>) before replaying. Default <c>true</c> — ingest is a whole-run replay.</summary>
    public bool ClearExistingLogs { get; init; } = true;

    /// <summary>Feature name for scenarios that carry none.</summary>
    public string DefaultFeatureName { get; init; } = "Ingested";

    /// <summary>Verdict for tests that have interactions but no <c>end</c> record in the tests file.</summary>
    public ExecutionResult ResultWhenUnknown { get; init; } = ExecutionResult.Passed;

    /// <summary>When true, the report is generated even if no scenario could be synthesised (it will be empty). Default <c>false</c>.</summary>
    public bool AllowEmpty { get; init; }
}

/// <summary>Outcome of <see cref="IngestPipeline.Run"/>.</summary>
/// <param name="InteractionCount">Interaction lines replayed into the store.</param>
/// <param name="ScenarioCount">Scenarios in the synthesised model.</param>
/// <param name="Features">The synthesised model.</param>
/// <param name="ReportsDirectory">Where the report files were written.</param>
/// <param name="Start">Run start (UTC).</param>
/// <param name="End">Run end (UTC).</param>
/// <param name="Generated">False when nothing was ingested and the report was skipped.</param>
public sealed record IngestResult(
    int InteractionCount,
    int ScenarioCount,
    Feature[] Features,
    string ReportsDirectory,
    DateTime Start,
    DateTime End,
    bool Generated)
{
    /// <summary>Path of the main HTML report (may not exist when <see cref="Generated"/> is false).</summary>
    public string TestRunReportHtml => Path.Combine(ReportsDirectory, "TestRunReport.html");
}

/// <summary>
/// Replays NDJSON captures into the in-process store and generates the standard Kronikol reports from
/// them — the programmatic form of <c>kronikol ingest</c>. Use it when a capturer (an out-of-process
/// proxy tap, a Java/Node service, any tool that can write <see cref="InteractionRecord"/> lines) runs
/// outside the test process, or when a host wants to regenerate a report from files after the fact.
/// </summary>
/// <remarks>
/// Order of operations: read everything → (optionally) clear the store → replay records in timestamp
/// order (the sequence diagram follows enqueue order, not timestamps) → normalise each log's
/// <c>TestName</c> from the tests file → reset the diagram cache → synthesise <see cref="Feature"/>s →
/// <see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/>. Capture-time redaction
/// (<see cref="RequestResponseLogger.Redaction"/>) applies during replay, so secrets present in a raw
/// capture file can still be kept out of the report data files.
/// </remarks>
public static class IngestPipeline
{
    /// <summary>
    /// Defaults suited to externally captured traffic: browser-side PlantUML rendering, no internal-flow
    /// tracking (there are no in-process spans to show), component diagram on, consecutive identical
    /// calls collapsed.
    /// </summary>
    public static ReportConfigurationOptions DefaultOptions() => new()
    {
        PlantUmlRendering = PlantUmlRendering.BrowserJs,
        InternalFlowTracking = false,
        GenerateComponentDiagram = true,
        CollapseConsecutiveIdenticalCalls = true,
        ShowNoInteractionsMarker = true,
    };

    /// <summary>Runs the pipeline. Throws <see cref="FormatException"/> for malformed input lines and <see cref="FileNotFoundException"/> for missing files.</summary>
    public static IngestResult Run(IngestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Options ?? DefaultOptions();

        var records = new List<InteractionRecord>();
        foreach (var file in request.InteractionFiles)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("Interaction file not found.", file);
            records.AddRange(NdjsonInteractionReader.ReadFile(file));
        }

        if (request.Interactions is not null)
            records.AddRange(request.Interactions);

        var testRecords = new List<TestRunRecord>();
        if (!string.IsNullOrWhiteSpace(request.TestsFile))
        {
            if (!File.Exists(request.TestsFile))
                throw new FileNotFoundException("Tests file not found.", request.TestsFile);
            testRecords.AddRange(NdjsonTestRunReader.ReadFile(request.TestsFile));
        }

        if (request.TestRecords is not null)
            testRecords.AddRange(request.TestRecords);

        var reportsDirectory = ReportGenerator.ResolveReportsDirectory(options);

        if (request.ClearExistingLogs)
            RequestResponseLogger.Clear();

        // Stable sort by timestamp: entries without a timestamp keep their file order relative to
        // each other and sort first.
        var ordered = records
            .Select((record, index) => (record, index))
            .OrderBy(x => x.record.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Select(x => x.record)
            .ToList();

        // Names from the tests file win over whatever each hop knew.
        var namesFromTests = testRecords
            .Where(t => !string.IsNullOrWhiteSpace(t.TestId) && !string.IsNullOrWhiteSpace(t.TestName))
            .GroupBy(t => t.TestId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().TestName!, StringComparer.Ordinal);

        // First non-empty name per test id among the records themselves, so every hop of one test
        // carries the same label even without a tests file.
        var namesFromRecords = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in ordered)
        {
            if (!string.IsNullOrWhiteSpace(record.TestName) && record.TestName != TestIdentityScope.UnknownTestName
                && !namesFromRecords.ContainsKey(record.TestId))
                namesFromRecords[record.TestId] = record.TestName;
        }

        var logs = new List<RequestResponseLog>(ordered.Count);
        foreach (var record in ordered)
        {
            var name = namesFromTests.TryGetValue(record.TestId, out var fromTests) ? fromTests
                : namesFromRecords.TryGetValue(record.TestId, out var fromRecords) ? fromRecords
                : null;
            var log = record.ToLog(name);
            RequestResponseLogger.Log(log);
            logs.Add(log);
        }

        var synthesised = FeatureSynthesizer.Build(testRecords, logs, request.DefaultFeatureName, request.ResultWhenUnknown);
        var scenarioCount = synthesised.Features.Sum(f => f.Scenarios.Length);

        if (scenarioCount == 0 && !request.AllowEmpty)
            return new IngestResult(logs.Count, 0, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: false);

        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagrams(synthesised.Features, synthesised.Start, synthesised.End, options);
        DefaultDiagramsFetcher.Reset();

        return new IngestResult(logs.Count, scenarioCount, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: true);
    }
}
