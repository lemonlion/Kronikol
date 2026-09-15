namespace Kronikol.History;

/// <summary>
/// The verdict vocabulary (plans/CROSS_RUN_HISTORY_PLAN.md §2.1). A scenario carries a set of these;
/// <see cref="ScenarioHistory.Primary"/> is the one a surface leads with.
/// </summary>
public enum HistoryVerdictKind
{
    /// <summary>Nothing to say: passing, as it was.</summary>
    Stable,

    /// <summary>Not in any earlier run of this stream.</summary>
    New,

    /// <summary>Failing now, passing in the previous run that had a verdict.</summary>
    Broke,

    /// <summary>Failing now and in the previous run; the evidence says since when.</summary>
    Failing,

    /// <summary>Failing in every run of the window; it has never been seen passing.</summary>
    AlwaysFailing,

    /// <summary>Passing now, failing in the previous run that had a verdict.</summary>
    Fixed,

    /// <summary>Flipping between pass and fail at or above the flaky rate, or passed on a retry.</summary>
    Flaky,

    /// <summary>Slower than the window's p95 by the factor, in this run and the previous one.</summary>
    Slower,

    /// <summary>The same status with a different set of calls than the previous run.</summary>
    BehaviourChanged,

    /// <summary>The same set of calls in a different order; reported only when asked for.</summary>
    Reordered,

    /// <summary>The set of calls changes most runs; behaviour verdicts are suppressed.</summary>
    UnstableShape,

    /// <summary>On the quarantine list; additive.</summary>
    Quarantined,

    /// <summary>
    /// No verdict: the current result is not one (it was defaulted), or the scenario failed and the
    /// stream has no earlier pass or fail to compare against. Additive when another verdict applies.
    /// </summary>
    Unknown
}

/// <summary>The kebab-case names the vocabulary is printed and filtered with.</summary>
public static class HistoryVerdictNames
{
    private static readonly IReadOnlyDictionary<HistoryVerdictKind, string> Names = new Dictionary<HistoryVerdictKind, string>
    {
        [HistoryVerdictKind.Stable] = "stable",
        [HistoryVerdictKind.New] = "new",
        [HistoryVerdictKind.Broke] = "broke",
        [HistoryVerdictKind.Failing] = "failing",
        [HistoryVerdictKind.AlwaysFailing] = "always-failing",
        [HistoryVerdictKind.Fixed] = "fixed",
        [HistoryVerdictKind.Flaky] = "flaky",
        [HistoryVerdictKind.Slower] = "slower",
        [HistoryVerdictKind.BehaviourChanged] = "behaviour-changed",
        [HistoryVerdictKind.Reordered] = "reordered",
        [HistoryVerdictKind.UnstableShape] = "unstable-shape",
        [HistoryVerdictKind.Quarantined] = "quarantined",
        [HistoryVerdictKind.Unknown] = "unknown"
    };

    private static readonly IReadOnlyDictionary<string, HistoryVerdictKind> Kinds =
        Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The printed name of a verdict.</summary>
    public static string Name(HistoryVerdictKind kind) => Names[kind];

    /// <summary>The verdict a name stands for, or null.</summary>
    public static HistoryVerdictKind? Parse(string? name) =>
        name is not null && Kinds.TryGetValue(name.Trim(), out var kind) ? kind : null;

    /// <summary>Every name, in enum order.</summary>
    public static IReadOnlyList<string> All { get; } = Enum.GetValues<HistoryVerdictKind>().Select(Name).ToArray();

    /// <summary>The names of a set of verdicts, in precedence order, joined with commas.</summary>
    public static string Join(IEnumerable<HistoryVerdictKind> kinds) =>
        string.Join(",", kinds.OrderBy(HistoryAnalyzer.Precedence).Select(Name));
}

/// <summary>What the analysis reads from the options.</summary>
public sealed record HistoryAnalysisOptions
{
    /// <summary>How many runs back the analysis looks (<see cref="ReportConfigurationOptions.HistoryWindow"/>).</summary>
    public int Window { get; init; } = 50;

    /// <summary>How many verdicts a scenario needs before flakiness and duration are judged (<see cref="ReportConfigurationOptions.HistoryMinRuns"/>).</summary>
    public int MinRuns { get; init; } = 5;

    /// <summary>The flip rate at or above which a scenario is flaky (<see cref="ReportConfigurationOptions.HistoryFlakyRate"/>).</summary>
    public double FlakyRate { get; init; } = 0.1;

    /// <summary>The factor over the p95 that makes a run slower (<see cref="ReportConfigurationOptions.HistorySlowerBy"/>).</summary>
    public double SlowerBy { get; init; } = 1.5;

    /// <summary>The least a scenario must be over the bar, in milliseconds, before it is slower (<see cref="ReportConfigurationOptions.HistorySlowerMinMs"/>).</summary>
    public int SlowerMinMs { get; init; } = 100;

    /// <summary>The share of the previous roster a run may lack before it is partial (<see cref="ReportConfigurationOptions.HistoryPartialThreshold"/>).</summary>
    public double PartialThreshold { get; init; } = 0.10;

    /// <summary>Whether a reorder of the same calls is a verdict (<see cref="ReportConfigurationOptions.HistoryReordered"/>).</summary>
    public bool ReportReordered { get; init; }

    /// <summary>The stream to analyse against instead of the current run's own.</summary>
    public string? Branch { get; init; }

    /// <summary>A second stream to read the same run against, reported as <see cref="HistoryVerdicts.Compare"/>.</summary>
    public string? CompareBranch { get; init; }
}

/// <summary>One prior run's reading of one scenario.</summary>
/// <param name="RunId">The run.</param>
/// <param name="At">When it finished.</param>
/// <param name="Commit">What it tested.</param>
/// <param name="Result">The result character.</param>
/// <param name="DurationMs">The duration, when recorded.</param>
/// <param name="ShapeSet">The set fingerprint, when recorded.</param>
/// <param name="ShapeOrdered">The ordered fingerprint, when recorded.</param>
/// <param name="Calls">The call count, when recorded.</param>
/// <param name="Error">The error cluster text, on a failure.</param>
/// <param name="Attempt">The attempt the result came from, when the runner said.</param>
/// <param name="ShapeVersion">The templating rule the fingerprints were made by.</param>
/// <param name="CallSet">The distinct calls, spelled out, when the line recorded them (3.17.0).</param>
public sealed record HistoryPoint(string RunId, DateTimeOffset At, string? Commit, char Result, int? DurationMs, string? ShapeSet, string? ShapeOrdered, int? Calls, string? Error, int? Attempt, int? ShapeVersion = null,
    IReadOnlyList<string>? CallSet = null);

/// <summary>Where a failing streak began.</summary>
/// <param name="RunId">The first failing run of the streak.</param>
/// <param name="At">When it finished.</param>
/// <param name="Commit">What it tested.</param>
/// <param name="Runs">How many consecutive runs have failed, the current one included.</param>
public sealed record FailingSince(string RunId, DateTimeOffset At, string? Commit, int Runs);

/// <summary>One scenario's history and verdicts.</summary>
public sealed record ScenarioHistory
{
    /// <summary>The scenario's stable id.</summary>
    public required string StableId { get; init; }

    /// <summary>Which holder of the id this is, when a run has it more than once.</summary>
    public required int Slot { get; init; }

    /// <summary>The display name in the current run.</summary>
    public required string Name { get; init; }

    /// <summary>The feature in the current run.</summary>
    public required string Feature { get; init; }

    /// <summary>The result in the current run.</summary>
    public required char Current { get; init; }

    /// <summary>The verdict a surface leads with.</summary>
    public required HistoryVerdictKind Primary { get; init; }

    /// <summary>Every verdict that applies.</summary>
    public required IReadOnlySet<HistoryVerdictKind> Verdicts { get; init; }

    /// <summary>The one-line justification of the primary verdict — the numbers, so the reader can disagree.</summary>
    public required string Evidence { get; init; }

    /// <summary>Prior runs of the stream in which the scenario appeared.</summary>
    public required int RunsSeen { get; init; }

    /// <summary>Pass-or-fail results in the window, the current run included.</summary>
    public required int RealVerdicts { get; init; }

    /// <summary>Failures among <see cref="RealVerdicts"/>.</summary>
    public required int Failures { get; init; }

    /// <summary>Failures over verdicts.</summary>
    public required double FailRate { get; init; }

    /// <summary>Changes between consecutive verdicts.</summary>
    public required int Flips { get; init; }

    /// <summary>Flips over the transitions between verdicts.</summary>
    public required double FlipRate { get; init; }

    /// <summary>Runs since the last flip; the length of the current streak minus one.</summary>
    public required int RunsSinceLastFlip { get; init; }

    /// <summary>How many runs ago the scenario last failed before this run, or null if it never did in the window.</summary>
    public required int? LastFailedRunsAgo { get; init; }

    /// <summary>Where the current failing streak began, when the scenario is failing.</summary>
    public required FailingSince? FailingSince { get; init; }

    /// <summary>The last results, oldest first, the current run last — for a sparkline.</summary>
    public required string Series { get; init; }

    /// <summary>Every prior reading in the window, oldest first, and the current run last.</summary>
    public required IReadOnlyList<HistoryPoint> Points { get; init; }

    /// <summary>The current duration, when recorded.</summary>
    public required int? DurationMs { get; init; }

    /// <summary>The p95 of the prior durations, when enough were recorded, in this run's milliseconds: each prior duration is read against its run's speed and the p95 scaled to this run's.</summary>
    public required int? DurationP95 { get; init; }

    /// <summary>The set fingerprint in the previous run that recorded one.</summary>
    public required string? PreviousShapeSet { get; init; }

    /// <summary>The set fingerprint now.</summary>
    public required string? ShapeSet { get; init; }

    /// <summary>The call count in the previous run that recorded one.</summary>
    public required int? PreviousCalls { get; init; }

    /// <summary>The call count now.</summary>
    public required int? Calls { get; init; }

    /// <summary>
    /// The calls this run made that the previous shaped run did not, by their templated line: what a
    /// <c>behaviour-changed</c> verdict names. Empty when the calls are the same, or when either run
    /// predates the call lists (3.17.0) and the change could only be counted.
    /// </summary>
    public IReadOnlyList<string> NewCalls { get; init; } = [];

    /// <summary>The calls the previous shaped run made that this run did not; see <see cref="NewCalls"/>.</summary>
    public IReadOnlyList<string> GoneCalls { get; init; } = [];

    /// <summary>The quarantine entry, when there is one.</summary>
    public required HistoryQuarantineEntry? Quarantine { get; init; }

    /// <summary>Whether the verdict set has this kind.</summary>
    public bool Has(HistoryVerdictKind kind) => Verdicts.Contains(kind);

    /// <summary>The printed verdicts, comma-separated, primary first.</summary>
    public string VerdictNames => HistoryVerdictNames.Join(Verdicts);
}

/// <summary>A scenario the previous run had and this one does not.</summary>
/// <param name="StableId">Its id.</param>
/// <param name="Name">Its name, as the previous run knew it.</param>
/// <param name="Feature">Its feature, as the previous run knew it.</param>
/// <param name="LastRunId">The run that last had it.</param>
public sealed record AbsentScenario(string StableId, string Name, string Feature, string LastRunId);

/// <summary>One run's totals, for the run-level trend.</summary>
/// <param name="RunId">The run.</param>
/// <param name="At">When it finished.</param>
/// <param name="Commit">What it tested.</param>
/// <param name="Passed">Scenarios that passed.</param>
/// <param name="Failed">Scenarios that failed.</param>
/// <param name="Total">Scenarios in the run.</param>
/// <param name="DurationMs">The sum of recorded durations, or null when none were.</param>
/// <param name="Partial">Whether the run was partial.</param>
public sealed record RunPoint(string RunId, DateTimeOffset At, string? Commit, int Passed, int Failed, int Total, long? DurationMs, bool Partial);

/// <summary>The analysis of one run against its stream.</summary>
public sealed record HistoryVerdicts
{
    /// <summary>The suite analysed.</summary>
    public required string? Suite { get; init; }

    /// <summary>The stream analysed against.</summary>
    public required string Stream { get; init; }

    /// <summary>The current run.</summary>
    public required string RunId { get; init; }

    /// <summary>Prior runs of the stream in the window, the current run excluded.</summary>
    public required int RunsRecorded { get; init; }

    /// <summary>The minimum the flakiness and duration verdicts need.</summary>
    public required int MinRuns { get; init; }

    /// <summary>Whether the stream has fewer runs than the verdicts need.</summary>
    public required bool ColdStart { get; init; }

    /// <summary>What to tell a reader about the cold start, when there is one.</summary>
    public required string? ColdStartMessage { get; init; }

    /// <summary>Whether the current run was judged partial.</summary>
    public required bool Partial { get; init; }

    /// <summary>One entry per roster position of the current run.</summary>
    public required IReadOnlyList<ScenarioHistory> Scenarios { get; init; }

    /// <summary>Scenarios the previous run had and this one lacks; empty when the run is partial.</summary>
    public required IReadOnlyList<AbsentScenario> Absent { get; init; }

    /// <summary>How many scenarios carry each verdict.</summary>
    public required IReadOnlyDictionary<HistoryVerdictKind, int> Counts { get; init; }

    /// <summary>The run-level trend, oldest first, the current run last.</summary>
    public required IReadOnlyList<RunPoint> Runs { get; init; }

    /// <summary><c>caller&gt;service</c> pairs the current run has and no prior run in the window had.</summary>
    public required IReadOnlyList<string> NewDependencies { get; init; }

    /// <summary>Scenarios seen for the first time in this stream.</summary>
    public required int FirstSeen { get; init; }

    /// <summary>The same run read against <see cref="HistoryAnalysisOptions.CompareBranch"/>, when asked for.</summary>
    public required HistoryVerdicts? Compare { get; init; }

    /// <summary>The entry for a scenario, or null.</summary>
    public ScenarioHistory? Find(string stableId, int slot = 0) =>
        Scenarios.FirstOrDefault(s => s.Slot == slot && string.Equals(s.StableId, stableId, StringComparison.Ordinal));

    /// <summary>
    /// The entry at a roster position when it is the scenario expected there, otherwise the first entry
    /// with that id. Positions are <c>sN</c> ordinals, so a surface that numbers scenarios the same way
    /// finds its entry in constant time and is never misled when it does not.
    /// </summary>
    public ScenarioHistory? At(int position, string stableId) =>
        position >= 0 && position < Scenarios.Count && string.Equals(Scenarios[position].StableId, stableId, StringComparison.Ordinal)
            ? Scenarios[position]
            : Find(stableId);

    /// <summary>How many scenarios carry a verdict.</summary>
    public int Count(HistoryVerdictKind kind) => Counts.TryGetValue(kind, out var n) ? n : 0;

    /// <summary>Whether any scenario carries a verdict other than stable.</summary>
    public bool HasAnything => Scenarios.Any(s => s.Primary != HistoryVerdictKind.Stable) || Absent.Count > 0;
}
