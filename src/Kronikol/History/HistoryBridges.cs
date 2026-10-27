using System.Globalization;

namespace Kronikol.History;

/// <summary>
/// What a run's bridges are kept for (<see cref="HistoryRun.Bridge"/>, #190 R0). A run bridges every rule Kronikol keeps
/// when it is made; a ledger keeps only the bridges its window can use, since a bridge is read only against a run
/// fingerprinted under its rule, and none is written once every such run has left the window. A writer that builds a
/// line from others (a fold of shards or of attempts) builds its bridges the same way, position by position.
/// </summary>
internal static class HistoryBridges
{
    /// <summary>The built-in version a bridge key names (<see cref="HistoryBridge.RuleKey"/>), or null when the key is not one.</summary>
    public static int? VersionOf(string rule)
    {
        var end = rule.IndexOf('+', StringComparison.Ordinal);
        return int.TryParse(end < 0 ? rule : rule[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }

    /// <summary>
    /// The run with only the bridges a run of <paramref name="window"/> was fingerprinted under; with none when no run
    /// there was made by another rule, which is every run once the change has left the window.
    /// </summary>
    public static HistoryRun ForWindow(HistoryRun run, IEnumerable<HistoryRun> window)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(window);
        if (run.Bridge is not { Count: > 0 } bridge)
            return run;

        var rules = window.Where(r => r.ShapeSet is not null).Select(HistoryBridge.RuleOf).ToHashSet(StringComparer.Ordinal);
        var kept = bridge.Where(p => rules.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return kept.Count == bridge.Count ? run : run with { Bridge = kept.Count > 0 ? kept : null };
    }

    /// <summary>
    /// The run with each bridge's calls held in memory again, looked up by hash (a line read from a ledger names them
    /// only), so a writer that builds a line from it can write them.
    /// </summary>
    public static HistoryRun WithShapes(HistoryRun run, Func<string, HistoryShapes?> shapesOf)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Bridge is not { Count: > 0 } bridge || bridge.Values.All(b => b.ShapesHash is null || b.Shapes is not null))
            return run;
        return run with
        {
            Bridge = bridge.ToDictionary(p => p.Key,
                p => p.Value.Shapes is null && p.Value.ShapesHash is { } hash ? p.Value with { Shapes = shapesOf(hash) } : p.Value,
                StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// The run without the call sets of any bridge whose calls are neither in memory nor among
    /// <paramref name="present"/>: a line must not name a shapes line the file lacks. Its fingerprints are kept.
    /// </summary>
    public static HistoryRun WithoutMissingShapes(HistoryRun run, IReadOnlySet<string> present)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Bridge is not { Count: > 0 } bridge
            || bridge.Values.All(b => b.ShapesHash is not { } hash || b.Shapes is not null || present.Contains(hash)))
            return run;
        return run with
        {
            Bridge = bridge.ToDictionary(p => p.Key,
                p => p.Value.ShapesHash is { } hash && p.Value.Shapes is null && !present.Contains(hash) ? p.Value with { ShapesHash = null, CallSets = null } : p.Value,
                StringComparer.Ordinal)
        };
    }

    /// <summary>One position of a run as a bridge's rule made it.</summary>
    public readonly record struct Cell(string? Set, string? Ordered, IReadOnlyList<string>? Lines);

    /// <summary>
    /// A position of <paramref name="run"/> under <paramref name="bridge"/>: the bridge's own value where it has one, the
    /// run's where it holds the run's (<paramref name="ownLines"/> are the run's calls at the position, already spelled out).
    /// </summary>
    public static Cell CellAt(HistoryRun run, HistoryBridge bridge, int position, IReadOnlyList<string>? ownLines)
    {
        static string? At(IReadOnlyList<string> list, int i) => i >= 0 && i < list.Count ? list[i] : null;
        var lines = bridge.CallSets is { } callSets
            ? position >= 0 && position < callSets.Count && bridge.Shapes is { } shapes
                ? callSets[position].Select(shapes.At).OfType<string>().ToArray()
                : null
            : ownLines;
        return new Cell(
            bridge.ShapeSet is { } set ? At(set, position) : run.ShapeSetAt(position),
            bridge.ShapeOrdered is { } ordered ? At(ordered, position) : run.ShapeOrderedAt(position),
            lines);
    }

    /// <summary>
    /// The bridge entry a line built position by position carries for one rule: an entry with no arrays when every
    /// position is the line's own, else the positions spelled out, with their calls as a shapes line of the rule's own.
    /// </summary>
    public static HistoryBridge Entry(IReadOnlyList<Cell> cells, IReadOnlyList<string> ownSet, IReadOnlyList<string> ownOrdered,
        IReadOnlyDictionary<string, string>? deps)
    {
        var same = true;
        for (var i = 0; i < cells.Count && same; i++)
            same = string.Equals(cells[i].Set ?? "", i < ownSet.Count ? ownSet[i] : "", StringComparison.Ordinal)
                   && string.Equals(cells[i].Ordered ?? "", i < ownOrdered.Count ? ownOrdered[i] : "", StringComparison.Ordinal);
        if (same)
            return new HistoryBridge { Deps = deps is { Count: > 0 } ? deps : null };

        var shapes = HistoryShapes.Create(cells.SelectMany(c => c.Lines ?? []));
        var index = shapes.Calls.Select((line, n) => (line, n)).ToDictionary(p => p.line, p => p.n, StringComparer.Ordinal);
        return new HistoryBridge
        {
            ShapeSet = cells.Select(c => c.Set ?? "").ToArray(),
            ShapeOrdered = cells.Select(c => c.Ordered ?? "").ToArray(),
            ShapesHash = shapes.Hash,
            CallSets = cells.Select(c => (IReadOnlyList<int>)(c.Lines ?? []).Select(line => index[line]).OrderBy(n => n).ToArray()).ToArray(),
            Deps = deps is { Count: > 0 } ? deps : null,
            Shapes = shapes
        };
    }

    /// <summary>The renamed pairs of several lines' bridges for one rule, as one map.</summary>
    public static IReadOnlyDictionary<string, string>? MergedDeps(IEnumerable<HistoryBridge> bridges)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var bridge in bridges)
            foreach (var pair in bridge.Deps ?? new Dictionary<string, string>())
                merged.TryAdd(pair.Key, pair.Value);
        return merged.Count > 0 ? merged : null;
    }

    /// <summary>The rules every one of the lines bridges: a fold cannot claim a bridge one of its lines lacks.</summary>
    public static IReadOnlyList<string> CommonRules(IEnumerable<HistoryRun> runs)
    {
        HashSet<string>? common = null;
        foreach (var run in runs)
        {
            var rules = run.Bridge?.Keys ?? (IEnumerable<string>)[];
            if (common is null) common = new HashSet<string>(rules, StringComparer.Ordinal);
            else common.IntersectWith(rules);
        }
        return common is null ? [] : common.OrderBy(r => r, StringComparer.Ordinal).ToArray();
    }
}
