using Kronikol.History;
using System.Text.Json;

namespace Kronikol.Reports;

/// <summary>
/// One test in a CTRF document, already translated out of Kronikol's vocabulary and into the schema's.
/// <para>The properties past <see cref="RawStatus"/> are not CTRF fields: they are what
/// <see cref="CtrfReportGenerator"/> writes into the schema's <c>extra</c> escape hatch, kept here as
/// real properties so both producers — the run, and <c>kronikol ctrf</c> converting a report that has
/// already been written — populate them the same way.</para>
/// </summary>
public sealed record CtrfTest
{
    /// <summary>The scenario's display name. Required by the schema.</summary>
    public required string Name { get; init; }

    /// <summary>One of <c>passed</c>, <c>failed</c>, <c>skipped</c>, <c>pending</c>, <c>other</c>. Required.</summary>
    public required string Status { get; init; }

    /// <summary>Whole milliseconds. Required, so a scenario nobody timed reports <c>0</c> rather than nothing.</summary>
    public required long Duration { get; init; }

    /// <summary>The feature the scenario belongs to.</summary>
    public string? Suite { get; init; }

    /// <summary>The failure message, on a failure.</summary>
    public string? Message { get; init; }

    /// <summary>The failure's stack trace, on a failure.</summary>
    public string? Trace { get; init; }

    /// <summary>
    /// A project-relative source path, or null. Never a bare file name: consumers place CI annotations
    /// with this, and a name where a path is expected annotates the wrong file.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>The line the scenario is declared on, where the lane supplied one.</summary>
    public int? Line { get; init; }

    /// <summary>The scenario's labels, minus the <c>retry N</c> ones Kronikol generates itself.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Attempts before the one that produced this result; <c>0</c> when the runner said nothing.</summary>
    public int Retries { get; init; }

    /// <summary>Passed, but not on the first attempt.</summary>
    public bool Flaky { get; init; }

    /// <summary>Kronikol's own <see cref="ExecutionResult"/> name, so the translation loses nothing.</summary>
    public string? RawStatus { get; init; }

    /// <summary>The scenario's <c>sN</c> address — <c>extra.kronikolAddress</c>.</summary>
    public string? Address { get; init; }

    /// <summary>The scenario's stable id — <c>extra.stableId</c>.</summary>
    public string? StableId { get; init; }

    /// <summary>The scenario's categories — <c>extra.categories</c>, not tags.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The cross-run verdict — <c>extra.kronikolHistory</c> — when a ledger was read.</summary>
    public ScenarioHistory? History { get; init; }
}

/// <summary>
/// The CTRF <c>results.environment</c> block: which build produced the run. Every field is optional and
/// the block is omitted entirely when nothing is known, which is the case off CI.
/// </summary>
public sealed record CtrfEnvironment
{
    /// <summary>The CI provider's name.</summary>
    public string? BuildName { get; init; }

    public string? BuildNumber { get; init; }

    public string? BuildUrl { get; init; }

    public string? RepositoryName { get; init; }

    public string? Commit { get; init; }

    public string? BranchName { get; init; }

    /// <summary>The provider's run id — <c>extra.runId</c>; CTRF has no field of its own for it.</summary>
    public string? RunId { get; init; }

    /// <summary>Which try of <see cref="RunId"/> this is — <c>extra.runAttempt</c>.</summary>
    public string? RunAttempt { get; init; }

    /// <summary>Nothing is known, so the block would be an empty object claiming to describe a build.</summary>
    public bool IsEmpty =>
        BuildName is null && BuildNumber is null && BuildUrl is null && RepositoryName is null
        && Commit is null && BranchName is null && RunId is null && RunAttempt is null;

    /// <summary>The detected CI metadata as an environment block, or null off CI.</summary>
    public static CtrfEnvironment? From(CiMetadata? metadata)
    {
        if (metadata is null) return null;
        var environment = new CtrfEnvironment
        {
            BuildName = metadata.Provider.ToString(),
            BuildNumber = metadata.BuildNumber,
            BuildUrl = metadata.PipelineUrl,
            RepositoryName = metadata.Repository,
            Commit = metadata.CommitSha,
            BranchName = metadata.Branch,
            RunId = metadata.RunId,
            RunAttempt = metadata.RunAttempt
        };
        return environment.IsEmpty ? null : environment;
    }
}

/// <summary>
/// Writes <c>ctrf-report.json</c>: the run in the Common Test Report Format, for the CI tooling that
/// already speaks it.
///
/// <para><b>Why it exists.</b> Every other file Kronikol writes is read by a person or by
/// <c>kronikol query</c>. CTRF is read by somebody else's action — a PR comment bot, a GitHub annotation
/// step, a flaky-test dashboard — and those tools will never learn Kronikol's own schema. One small file
/// in a format they already parse is the whole integration.</para>
///
/// <para><b>What it deliberately is not.</b> Not a flag on the OTLP exporter: that pushes live spans to a
/// collector and this writes a file at the end of a run, and folding them together would mean one switch
/// with two unrelated meanings. Not a richer Kronikol report in CTRF clothing either — a field emitted
/// under a name the schema does not declare is silently dropped by every reader, so everything Kronikol
/// knows and CTRF does not goes in the schema's own <c>extra</c> object, including the <c>sN</c> address
/// that leads back into <c>kronikol query</c>.</para>
///
/// <para><b>What the model cannot supply.</b> CTRF's optional per-test <c>start</c> and <c>stop</c> are
/// omitted rather than synthesised: <see cref="Scenario"/> carries a duration and no start time, and a
/// fabricated timeline is worse than an absent one. <c>attempts[]</c> is omitted for the same reason —
/// earlier attempts are folded into one scenario before the report is built, so their results no longer
/// exist to list.</para>
///
/// <para><b>Security.</b> Derived from the same post-redaction model as <c>TestRunReport.json</c>, so it
/// is the same exposure class — but it does carry failure messages and traces verbatim, which is what
/// makes it useful to an annotation action and what makes it a file to think about before publishing.</para>
/// </summary>
public static class CtrfReportGenerator
{
    /// <summary>The file name, fixed by ecosystem convention rather than by an option.</summary>
    public const string FileName = "ctrf-report.json";

    /// <summary>
    /// The CTRF revision this writer was checked against, emitted as <c>specVersion</c>. Pinned, not
    /// tracked: it moves when somebody re-reads the schema and confirms the document still conforms, and
    /// <c>CtrfReportGeneratorTests</c> holds the key list that says what "conforms" meant.
    /// </summary>
    public const string SpecVersion = "0.0.0";

    /// <summary>The tool name every CTRF document must carry.</summary>
    public const string ToolName = "Kronikol";

    /// <summary>Labels of this shape are Kronikol's own retry bookkeeping, not something an author tagged.</summary>
    private static bool IsGeneratedRetryLabel(string label) =>
        label.StartsWith("retry ", StringComparison.Ordinal)
        && label.Length > 6
        && label.AsSpan(6).ToString().All(char.IsAsciiDigit);

    /// <summary>The CTRF document for a completed run.</summary>
    public static string Generate(Feature[] features, DateTime startRunTime, DateTime endRunTime,
        CiMetadata? ciMetadata, string kronikolVersion, string? suite = null, HistoryVerdicts? history = null)
    {
        ArgumentNullException.ThrowIfNull(features);

        var tests = new List<CtrfTest>();
        var ordinal = 0;
        // The same ordering as BuildFeaturesJsonModel and FailuresDigestGenerator.Enumerate — features by
        // display name under the default culture comparer, scenarios in file order. That ordering is what
        // defines sN, so a writer that sorts its own way hands out addresses nobody else agrees with.
        foreach (var feature in features.OrderBy(f => f.DisplayName))
        {
            foreach (var scenario in feature.Scenarios ?? [])
            {
                var retries = scenario.Attempt is { } attempt && attempt > 1 ? attempt - 1 : 0;
                var labels = (scenario.Labels ?? []).Where(l => !IsGeneratedRetryLabel(l)).ToArray();
                var stableId = ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);
                // The ledger's flaky verdict counts as CTRF's flaky flag: a consumer that lists flaky tests
                // (github-test-reporter's flaky table) then sees what the last runs saw, not only what
                // this run's retries saw.
                var scenarioHistory = history?.At(ordinal, stableId);
                tests.Add(new CtrfTest
                {
                    Name = scenario.DisplayName,
                    Status = MapStatus(scenario.Result),
                    Duration = (long)Math.Round(scenario.Duration?.TotalMilliseconds ?? 0),
                    Suite = feature.DisplayName,
                    Message = Trimmed(scenario.ErrorMessage),
                    Trace = Trimmed(scenario.ErrorStackTrace),
                    FilePath = Trimmed(scenario.SourceFile) ?? Trimmed(feature.SourceFile),
                    Line = scenario.SourceFile is { Length: > 0 } ? scenario.SourceLine : null,
                    Tags = labels,
                    Retries = retries,
                    Flaky = retries > 0 && scenario.Result == ExecutionResult.Passed || scenarioHistory?.Has(HistoryVerdictKind.Flaky) == true,
                    RawStatus = scenario.Result.ToString(),
                    Address = "s" + ordinal++,
                    StableId = stableId,
                    Categories = scenario.Categories ?? [],
                    History = scenarioHistory
                });
            }
        }

        return Build(tests, startRunTime, endRunTime, CtrfEnvironment.From(ciMetadata), kronikolVersion);
    }

    /// <summary>
    /// The document for a list of already-translated tests. Both producers come through here, so the
    /// envelope, the summary and the key names exist in exactly one place and cannot drift apart.
    /// </summary>
    public static string Build(IReadOnlyList<CtrfTest> tests, DateTime startRunTime, DateTime endRunTime,
        CtrfEnvironment? environment, string kronikolVersion)
    {
        ArgumentNullException.ThrowIfNull(tests);

        var results = new Dictionary<string, object?>
        {
            ["tool"] = new Dictionary<string, object?> { ["name"] = ToolName, ["version"] = kronikolVersion },
            ["summary"] = new Dictionary<string, object?>
            {
                ["tests"] = tests.Count,
                ["passed"] = tests.Count(t => t.Status == "passed"),
                ["failed"] = tests.Count(t => t.Status == "failed"),
                ["pending"] = tests.Count(t => t.Status == "pending"),
                ["skipped"] = tests.Count(t => t.Status == "skipped"),
                ["other"] = tests.Count(t => t.Status == "other"),
                ["start"] = EpochMilliseconds(startRunTime),
                ["stop"] = EpochMilliseconds(endRunTime)
            },
            ["tests"] = tests.Select(MapTest).ToArray()
        };

        if (environment is { IsEmpty: false })
            results["environment"] = MapEnvironment(environment);

        var document = new Dictionary<string, object?>
        {
            ["reportFormat"] = "CTRF",
            ["specVersion"] = SpecVersion,
            ["generatedBy"] = ToolName,
            ["results"] = results
        };

        return JsonSerializer.Serialize(document,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
    }

    /// <summary>
    /// Kronikol's five results onto CTRF's five statuses. Two collapse onto <c>skipped</c> and
    /// <c>Bypassed</c> has nowhere but <c>other</c> to go — <c>pending</c> means "not implemented yet",
    /// which is not what any of them says. Nothing is lost: the original word is emitted as
    /// <c>rawStatus</c>.
    /// </summary>
    public static string MapStatus(ExecutionResult result) => result switch
    {
        ExecutionResult.Passed => "passed",
        ExecutionResult.Failed => "failed",
        ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "skipped",
        _ => "other"
    };

    private static Dictionary<string, object?> MapTest(CtrfTest test)
    {
        var mapped = new Dictionary<string, object?>
        {
            ["name"] = test.Name,
            ["status"] = test.Status,
            ["duration"] = test.Duration
        };

        Set(mapped, "suite", test.Suite);
        Set(mapped, "message", test.Message);
        Set(mapped, "trace", test.Trace);
        Set(mapped, "filePath", test.FilePath);
        if (test.Line is { } line) mapped["line"] = line;
        if (test.Tags.Count > 0) mapped["tags"] = test.Tags;
        mapped["retries"] = test.Retries;
        mapped["flaky"] = test.Flaky;
        Set(mapped, "rawStatus", test.RawStatus);

        var extra = new Dictionary<string, object?>();
        Set(extra, "kronikolAddress", test.Address);
        Set(extra, "stableId", test.StableId);
        if (test.Categories.Count > 0) extra["categories"] = test.Categories;
        if (test.History is { } history)
            extra["kronikolHistory"] = new Dictionary<string, object?>
            {
                ["primary"] = HistoryVerdictNames.Name(history.Primary),
                ["verdicts"] = history.Verdicts.OrderBy(HistoryAnalyzer.Precedence).Select(HistoryVerdictNames.Name).ToArray(),
                ["evidence"] = history.Evidence,
                ["series"] = history.Series
            };
        if (extra.Count > 0) mapped["extra"] = extra;

        return mapped;
    }

    private static Dictionary<string, object?> MapEnvironment(CtrfEnvironment environment)
    {
        var mapped = new Dictionary<string, object?>();
        Set(mapped, "buildName", environment.BuildName);
        Set(mapped, "buildNumber", environment.BuildNumber);
        Set(mapped, "buildUrl", environment.BuildUrl);
        Set(mapped, "repositoryName", environment.RepositoryName);
        Set(mapped, "commit", environment.Commit);
        Set(mapped, "branchName", environment.BranchName);

        var extra = new Dictionary<string, object?>();
        Set(extra, "runId", environment.RunId);
        Set(extra, "runAttempt", environment.RunAttempt);
        if (extra.Count > 0) mapped["extra"] = extra;

        return mapped;
    }

    /// <summary>An absent optional field is left out rather than emitted as null — the schema's own idiom.</summary>
    private static void Set(Dictionary<string, object?> target, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) target[key] = value;
    }

    private static string? Trimmed(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// CTRF's run window as epoch milliseconds — the unit <c>duration</c> already uses. Truncated to whole
    /// seconds because that is all <c>TestRunReport.json</c>'s <c>startTime</c> carries, and a document
    /// converted from that file has to equal the one the run itself would have written.
    /// </summary>
    private static long EpochMilliseconds(DateTime time) =>
        new DateTimeOffset(DateTime.SpecifyKind(time.ToUniversalTime(), DateTimeKind.Utc)).ToUnixTimeSeconds() * 1000;
}
