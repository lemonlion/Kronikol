using System.Globalization;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.History;

/// <summary>What a run records about itself, from the options.</summary>
public sealed record HistoryBuildOptions
{
    /// <summary>Whether per-scenario durations are recorded (<see cref="ReportConfigurationOptions.HistoryDurations"/>).</summary>
    public bool Durations { get; init; } = true;

    /// <summary>Whether interaction fingerprints and call counts are recorded (<see cref="ReportConfigurationOptions.HistoryShapes"/>).</summary>
    public bool Shapes { get; init; } = true;

    /// <summary>
    /// Whether the first line of a failure message is kept as the error key. Off, the key is a hash of
    /// that line: the clustering survives and no message text reaches the ledger (§9.1).
    /// </summary>
    public bool ErrorKeys { get; init; } = true;

    /// <summary>The caller's word on whether the run is partial; null leaves it to the heuristic at append time.</summary>
    public bool? Partial { get; init; }

    /// <summary>The consumer's templating rules, applied before the built-in ones (<see cref="ReportConfigurationOptions.HistoryShapeTemplates"/>); null for none.</summary>
    public HistoryShapeRules? ShapeRules { get; init; }
}

/// <summary>
/// Builds the roster and the run line for a finished run: one position per scenario in report order,
/// the result alphabet applied, and the identity that makes eight shards one run (§3.3).
/// </summary>
public static class HistoryRunBuilder
{
    /// <summary>Builds the roster and the run for a finished run.</summary>
    /// <param name="features">The report model, in report order.</param>
    /// <param name="logs">Every tracked interaction of the run; attributed to scenarios by test id.</param>
    /// <param name="suite">The resolved suite, or null.</param>
    /// <param name="ci">The CI metadata, or null off CI.</param>
    /// <param name="at">When the run finished.</param>
    /// <param name="options">What to record.</param>
    /// <param name="runId">An identity to use instead of the one derived from <paramref name="ci"/>.</param>
    public static HistoryBuildResult Build(IReadOnlyList<Feature> features, IReadOnlyList<RequestResponseLog?> logs,
        string? suite, CiMetadata? ci, DateTimeOffset at, HistoryBuildOptions options, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(options);

        // The same ordering as BuildFeaturesJsonModel, FailuresDigestGenerator.Enumerate and the CTRF writer —
        // features by display name under the default culture comparer, scenarios in file order — so a
        // roster position is the scenario's sN address, and `kronikol query history <report> s3` needs no
        // translation.
        var scenarios = features.OrderBy(f => f.DisplayName).SelectMany(f => (f.Scenarios ?? []).Select(s => (Feature: f, Scenario: s))).ToArray();
        var entries = scenarios.Select(pair => new HistoryRosterEntry(
            ScenarioStableId.Compute(suite, pair.Feature.DisplayName, pair.Scenario.DisplayName, pair.Scenario.OutlineId, pair.Scenario.ExampleValues),
            pair.Scenario.DisplayName,
            pair.Feature.DisplayName,
            Source(pair.Scenario))).ToArray();
        var roster = HistoryRoster.Create(suite, entries);

        var results = new char[scenarios.Length];
        var attempts = new char[scenarios.Length];
        var durations = new int?[scenarios.Length];
        var calls = new int[scenarios.Length];
        var shapeSet = new string[scenarios.Length];
        var shapeOrdered = new string[scenarios.Length];
        var callLines = new string[scenarios.Length][];
        var errors = new string?[scenarios.Length];
        var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
        var errorKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        var logsByTest = options.Shapes
            ? logs.Where(l => l is not null).GroupBy(l => l!.TestId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal)
            : null;

        for (var i = 0; i < scenarios.Length; i++)
        {
            var scenario = scenarios[i].Scenario;
            results[i] = scenario.NotATest ? HistoryFormat.NotATest
                : scenario.ResultDefaulted ? HistoryFormat.Unknown
                : HistoryFormat.ResultChar(scenario.Result);
            attempts[i] = HistoryFormat.AttemptChar(scenario.Attempt);
            durations[i] = scenario.Duration is { } duration ? (int)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero) : null;

            if (logsByTest is not null)
            {
                var own = logsByTest.TryGetValue(scenario.Id, out var list) ? list : [];
                var scenarioCalls = InteractionShape.Calls(own, options.ShapeRules);
                var (set, ordered, count) = InteractionShape.Fingerprint(scenarioCalls);
                shapeSet[i] = set;
                shapeOrdered[i] = ordered;
                calls[i] = count;
                callLines[i] = scenarioCalls.Select(c => c.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            }

            if (scenario.Result == ExecutionResult.Failed && !scenario.ResultDefaulted && !scenario.NotATest)
            {
                // Truncate marks the cut with an ellipsis, so the cap leaves room for it: the key is never
                // longer than the limit.
                var line = FailureText.Truncate(FailureText.FirstLine(scenario.ErrorMessage), HistoryFormat.ErrorKeyLimit - 1);
                var text = options.ErrorKeys ? line : "#" + InteractionShape.Hash8(line);
                if (!errorKeys.TryGetValue(text, out var key))
                {
                    key = "e" + (errorKeys.Count + 1).ToString(CultureInfo.InvariantCulture);
                    errorKeys[text] = key;
                    errorText[key] = text;
                }
                errors[i] = key;
            }
        }

        // The run's distinct calls, once, and each position's calls as indices into them: the set the
        // fingerprint hashes, kept in the clear so a change can be named.
        HistoryShapes? shapes = null;
        IReadOnlyList<int>[]? callSets = null;
        if (logsByTest is not null)
        {
            shapes = HistoryShapes.Create(callLines.SelectMany(lines => lines));
            var index = shapes.Calls.Select((line, position) => (line, position)).ToDictionary(p => p.line, p => p.position, StringComparer.Ordinal);
            callSets = callLines.Select(lines => (IReadOnlyList<int>)lines.Select(line => index[line]).OrderBy(i => i).ToArray()).ToArray();
        }

        var run = new HistoryRun
        {
            Id = runId ?? RunId(ci, at),
            Suite = suite,
            Partial = options.Partial,
            At = at,
            Branch = NullIfEmpty(ci?.Branch),
            Commit = NullIfEmpty(ci?.CommitSha),
            Provider = ci is { Provider: not CiEnvironment.None } ? ci.Provider.ToString() : null,
            Url = NullIfEmpty(ci?.PipelineUrl),
            Shards = 1,
            RosterHash = roster.Hash,
            Results = new string(results),
            Attempts = new string(attempts),
            Durations = options.Durations ? durations : null,
            Calls = options.Shapes ? calls : null,
            ShapeSet = options.Shapes ? shapeSet : null,
            ShapeOrdered = options.Shapes ? shapeOrdered : null,
            ShapeVersion = options.Shapes ? InteractionShape.Version : null,
            ShapeRules = options.Shapes ? options.ShapeRules?.Hash : null,
            ShapesHash = shapes?.Hash,
            CallSets = callSets,
            Errors = errors,
            ErrorText = errorText,
            Deps = InteractionShape.Dependencies(logs)
        };
        return new HistoryBuildResult(roster, run, shapes);
    }

    private static string? Source(Scenario scenario) =>
        string.IsNullOrEmpty(scenario.SourceFile) ? null
            : scenario.SourceLine is { } line ? $"{scenario.SourceFile}:{line.ToString(CultureInfo.InvariantCulture)}"
            : scenario.SourceFile;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The run's identity. <c>gh:&lt;runId&gt;:&lt;attempt&gt;</c> on GitHub Actions — the run id is
    /// shared by every shard of one workflow run, the attempt is what keeps a re-run from erasing the
    /// failure it re-ran. <c>ado:&lt;buildId&gt;:1</c> on Azure DevOps, which has no attempt number.
    /// <c>local:&lt;stamp&gt;:&lt;hash&gt;</c> otherwise, the hash salted by machine and output directory so
    /// two developers' runs in the same second stay two runs.
    /// </summary>
    public static string RunId(CiMetadata? ci, DateTimeOffset at) => RunId(ci, at, LocalSalt());

    /// <summary>The run's identity with the local salt injected, for tests.</summary>
    public static string RunId(CiMetadata? ci, DateTimeOffset at, string salt)
    {
        switch (ci?.Provider)
        {
            case CiEnvironment.GitHubActions when !string.IsNullOrWhiteSpace(ci.RunId):
                var attempt = int.TryParse(ci.RunAttempt, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
                return $"gh:{ci.RunId.Trim()}:{attempt.ToString(CultureInfo.InvariantCulture)}";
            case CiEnvironment.AzureDevOps when !string.IsNullOrWhiteSpace(ci.RunId ?? ci.BuildNumber):
                return $"ado:{(ci.RunId ?? ci.BuildNumber)!.Trim()}:1";
            default:
                var stamp = at.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                return $"local:{stamp}:{InteractionShape.Hash8(salt)}";
        }
    }

    private static string LocalSalt()
    {
        string machine;
        try { machine = Environment.MachineName; }
        catch (InvalidOperationException) { machine = ""; }
        return machine + "|" + AppContext.BaseDirectory;
    }
}

/// <summary>What a run builds: its roster, its line, and the shapes list its call sets index into (null when shapes are switched off).</summary>
public sealed record HistoryBuildResult(HistoryRoster Roster, HistoryRun Run, HistoryShapes? Shapes)
{
    /// <summary>The pair a caller that has no use for the shapes takes.</summary>
    public void Deconstruct(out HistoryRoster roster, out HistoryRun run)
    {
        roster = Roster;
        run = Run;
    }
}
