using System;
using System.Collections.Generic;
using System.Linq;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// A suite's runs recorded across a release that moved history's rule, for a tracker's project to replay its own change
/// (plans/EMULATOR_URL_SHAPES_PLAN.md 6.3): the runs before the move written as the release before it wrote them, from
/// the calls it recorded, and the runs after by this build. One scenario per id, each passing. Linked into the trackers'
/// test projects, as <see cref="HistoryGuard"/> is, and throws its own exception for the same reason.
/// </summary>
internal sealed class HistoryReplay(string directory, IReadOnlyList<string> ids)
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Replay",
            Scenarios = ids.Select(id => new Scenario { Id = id, DisplayName = id, Result = ExecutionResult.Passed }).ToArray()
        }
    ];

    private static ReportConfigurationOptions Options(string ledger) => new() { HistoryFilePath = ledger, HistoryBranch = "", HistoryMinRuns = 2 };

    /// <summary>
    /// Run <paramref name="n"/> as <paramref name="generator"/>, a release of rule <paramref name="version"/>, wrote it from
    /// <paramref name="logs"/>, the calls it recorded: fingerprinted and counted under that rule, with no bridge, as a
    /// window of its own rule's runs needs none.
    /// </summary>
    public void RecordAsReleased(string ledger, RequestResponseLog[] logs, int n, int version, string generator)
    {
        var built = HistoryRunBuilder.Build(Features(), logs, "Suite", null, At.AddHours(n),
            new HistoryBuildOptions { Durations = true, Shapes = true, ErrorKeys = true, Bridges = true });
        var lines = new string[ids.Count][];
        var sets = new string[ids.Count];
        var ordered = new string[ids.Count];
        var counts = new int[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var calls = InteractionShape.Calls(logs.Where(l => l.TestId == ids[i]), null, version);
            (sets[i], ordered[i], counts[i]) = InteractionShape.Fingerprint(calls);
            lines[i] = calls.Select(c => c.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        }
        var shapes = HistoryShapes.Create(lines.SelectMany(l => l));
        var index = shapes.Calls.Select((line, position) => (line, position)).ToDictionary(p => p.line, p => p.position, StringComparer.Ordinal);
        var run = built.Run with
        {
            ShapeVersion = version,
            ShapeSet = sets,
            ShapeOrdered = ordered,
            Calls = counts,
            ShapesHash = shapes.Hash,
            CallSets = lines.Select(l => (IReadOnlyList<int>)l.Select(line => index[line]).OrderBy(i => i).ToArray()).ToArray(),
            Generator = generator,
            Bridge = null
        };
        var outcome = HistoryLedgerWriter.Append(ledger, built.Roster, run, generator, shapes: shapes).Outcome;
        if (outcome != HistoryAppendOutcome.Appended)
            throw new HistoryReplayException($"run {n} as {generator} wrote it was not appended: {outcome}");
    }

    /// <summary>Run <paramref name="n"/> recorded by this build (<paramref name="generator"/>) from <paramref name="logs"/>, with its verdicts.</summary>
    public HistoryVerdicts Record(string ledger, RequestResponseLog[] logs, int n, string generator)
    {
        var context = HistoryRunContext.Create(Features(), logs, "Suite", null, At.AddHours(n), Options(ledger), directory, generator, _ => null, directory)
            ?? throw new HistoryReplayException($"run {n} made no history context");
        var outcome = context.Append()?.Outcome;
        if (outcome != HistoryAppendOutcome.Appended)
            throw new HistoryReplayException($"run {n} by this build was not appended: {outcome}");
        return context.Verdicts ?? throw new HistoryReplayException($"run {n} read no verdicts");
    }
}

/// <summary>A replay that could not write or read a run.</summary>
internal sealed class HistoryReplayException(string message) : Exception(message);
