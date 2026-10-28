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

    /// <summary>One position of a run as a bridge's rule made it, with how many calls the rule records there.</summary>
    public readonly record struct Cell(string? Set, string? Ordered, IReadOnlyList<string>? Lines, int? Calls = null);

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
            lines,
            bridge.Calls is { } calls ? position >= 0 && position < calls.Count ? calls[position] : null : run.CallsAt(position));
    }

    /// <summary>
    /// The bridge entry a line built position by position carries for one rule: an entry with no arrays when every
    /// position is the line's own, else the positions spelled out, with their calls as a shapes line of the rule's own.
    /// </summary>
    public static HistoryBridge Entry(IReadOnlyList<Cell> cells, IReadOnlyList<string> ownSet, IReadOnlyList<string> ownOrdered,
        IReadOnlyDictionary<string, string>? deps, IReadOnlyList<int>? ownCalls = null)
    {
        var same = true;
        for (var i = 0; i < cells.Count && same; i++)
            same = string.Equals(cells[i].Set ?? "", i < ownSet.Count ? ownSet[i] : "", StringComparison.Ordinal)
                   && string.Equals(cells[i].Ordered ?? "", i < ownOrdered.Count ? ownOrdered[i] : "", StringComparison.Ordinal);
        if (same)
            return new HistoryBridge { Deps = deps is { Count: > 0 } ? deps : null };

        // The counts, where a position's differs from the line's own.
        IReadOnlyList<int>? calls = null;
        if (ownCalls is not null && cells.Select((c, i) => c.Calls is { } n && i < ownCalls.Count && n != ownCalls[i]).Any(differs => differs))
            calls = cells.Select((c, i) => c.Calls ?? (i < ownCalls.Count ? ownCalls[i] : 0)).ToArray();

        var shapes = HistoryShapes.Create(cells.SelectMany(c => c.Lines ?? []));
        var index = shapes.Calls.Select((line, n) => (line, n)).ToDictionary(p => p.line, p => p.n, StringComparer.Ordinal);
        return new HistoryBridge
        {
            ShapeSet = cells.Select(c => c.Set ?? "").ToArray(),
            ShapeOrdered = cells.Select(c => c.Ordered ?? "").ToArray(),
            ShapesHash = shapes.Hash,
            CallSets = cells.Select(c => (IReadOnlyList<int>)(c.Lines ?? []).Select(line => index[line]).OrderBy(n => n).ToArray()).ToArray(),
            Deps = deps is { Count: > 0 } ? deps : null,
            Calls = calls,
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

    /// <summary>
    /// The rule a fold of <paramref name="runs"/> records its fingerprints under: one every run with fingerprints reaches,
    /// by its own rule or through its bridge, the newest such; null when they share none. A run made under an older rule
    /// reaches no newer one, so a fold of a 5.16.1 shard and an R1 shard records the older rule, through the newer
    /// shard's bridge (#192 R1), where until then it recorded no fingerprints.
    /// </summary>
    public static string? FoldRule(IEnumerable<HistoryRun> runs)
    {
        var shaped = runs.Where(r => r.ShapeSet is not null).ToList();
        var common = Reached(shaped);
        if (common.Count == 0)
            return null;
        var own = shaped.Select(HistoryBridge.RuleOf).ToList();
        return common.OrderByDescending(r => VersionOf(r) ?? 0).ThenByDescending(r => own.Count(o => o == r)).ThenBy(r => r, StringComparer.Ordinal).First();
    }

    /// <summary>The rules besides <paramref name="foldRule"/> every run with fingerprints reaches: what the fold bridges.</summary>
    public static IReadOnlyList<string> FoldBridges(IEnumerable<HistoryRun> runs, string foldRule) =>
        Reached(runs.Where(r => r.ShapeSet is not null)).Where(r => !string.Equals(r, foldRule, StringComparison.Ordinal))
            .OrderBy(r => r, StringComparer.Ordinal).ToArray();

    private static HashSet<string> Reached(IEnumerable<HistoryRun> runs)
    {
        HashSet<string>? common = null;
        foreach (var run in runs)
        {
            var reached = new HashSet<string>(run.Bridge?.Keys ?? (IEnumerable<string>)[], StringComparer.Ordinal) { HistoryBridge.RuleOf(run) };
            if (common is null) common = reached;
            else common.IntersectWith(reached);
        }
        return common ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// A position of <paramref name="run"/> under <paramref name="rule"/>: its own where the rule is its own, else its bridge's.
    /// A run with no fingerprints has none under any rule: a fold reaches its rule through the runs that have them, and
    /// holds such a run's positions empty.
    /// </summary>
    public static Cell CellUnder(HistoryRun run, string rule, int position, IReadOnlyList<string>? ownLines) =>
        run.ShapeSet is null
            ? new Cell(run.ShapeSetAt(position), run.ShapeOrderedAt(position), ownLines)
            : string.Equals(HistoryBridge.RuleOf(run), rule, StringComparison.Ordinal)
                ? new Cell(run.ShapeSetAt(position), run.ShapeOrderedAt(position), ownLines, run.CallsAt(position))
                : CellAt(run, run.Bridge![rule], position, ownLines);

    /// <summary>A dependency pair of <paramref name="run"/> as <paramref name="rule"/> named it.</summary>
    public static string PairUnder(HistoryRun run, string rule, string pair) =>
        !string.Equals(HistoryBridge.RuleOf(run), rule, StringComparison.Ordinal) && run.Bridge?.GetValueOrDefault(rule)?.Deps is { } renamed
            ? renamed.GetValueOrDefault(pair, pair)
            : pair;

    /// <summary>
    /// The renamed pairs of a fold's bridge for <paramref name="bridgeRule"/>: each line's pairs as the fold's rule named
    /// them, to the name the bridge's rule gave them.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? FoldDeps(IEnumerable<HistoryRun> runs, string foldRule, string bridgeRule)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var run in runs)
            foreach (var pair in run.Deps ?? [])
            {
                var from = PairUnder(run, foldRule, pair);
                var to = PairUnder(run, bridgeRule, pair);
                if (!string.Equals(from, to, StringComparison.Ordinal))
                    merged.TryAdd(from, to);
            }
        return merged.Count > 0 ? merged : null;
    }

    /// <summary>The built-in version and the consumer's rules hash a rule key names.</summary>
    public static (int Version, string? ShapeRules) Parts(string rule)
    {
        var plus = rule.IndexOf('+', StringComparison.Ordinal);
        return (VersionOf(rule) ?? 1, plus < 0 ? null : rule[(plus + 1)..]);
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

    /// <summary>
    /// The run's fingerprints, calls and dependency pairs under a kept rule, from each position's raw calls; an empty
    /// entry when the rule changes none of the fingerprints. <paramref name="renamed"/> is what the version renames
    /// (<see cref="InteractionShape.RenamedDependencies"/>), which no pattern changes.
    /// </summary>
    public static HistoryBridge Under(int version, HistoryShapeRules? rules, IReadOnlyList<IReadOnlyList<HistoryRawCall>> positions,
        IReadOnlyDictionary<string, string>? renamed, IReadOnlyList<string> ownSet, IReadOnlyList<string> ownOrdered, IReadOnlyList<int>? ownCalls = null)
    {
        var set = new string[positions.Count];
        var ordered = new string[positions.Count];
        var counts = new int[positions.Count];
        var lines = new string[positions.Count][];
        var differs = false;
        for (var i = 0; i < positions.Count; i++)
        {
            var calls = InteractionShape.Calls(positions[i], rules, version);
            (set[i], ordered[i], counts[i]) = InteractionShape.Fingerprint(calls);
            lines[i] = calls.Select(c => c.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            differs |= !string.Equals(set[i], ownSet[i], StringComparison.Ordinal) || !string.Equals(ordered[i], ownOrdered[i], StringComparison.Ordinal);
        }

        var entry = new HistoryBridge { Deps = renamed is { Count: > 0 } ? renamed : null };
        if (!differs)
            return entry;

        var shapes = HistoryShapes.Create(lines.SelectMany(l => l));
        var index = shapes.Calls.Select((line, position) => (line, position)).ToDictionary(p => p.line, p => p.position, StringComparer.Ordinal);
        return entry with
        {
            ShapeSet = set,
            ShapeOrdered = ordered,
            ShapesHash = shapes.Hash,
            CallSets = lines.Select(l => (IReadOnlyList<int>)l.Select(line => index[line]).OrderBy(i => i).ToArray()).ToArray(),
            // A call one rule records and the other does not moves the count with the order, so only a changed position can
            // count differently.
            Calls = ownCalls is not null && !counts.SequenceEqual(ownCalls) ? counts : null,
            Shapes = shapes
        };
    }

    /// <summary>
    /// <paramref name="run"/> with a bridge under each rule of <paramref name="windowRules"/> it lacks, at every kept
    /// version, made from the fragment's raw calls: what <c>history record</c> adds for the consumer's earlier patterns,
    /// which a run that only wrote a fragment could not know (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8, Q18). The run
    /// as it was when it carries no raw calls (a fragment from before #192 R1) or no fingerprints.
    /// </summary>
    public static HistoryRun WithWindowRules(HistoryRun run, HistoryRawCalls? raw, IReadOnlyList<HistoryShapeRules?> windowRules)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(windowRules);
        if (raw is null || run.ShapeSet is not { } ownSet || run.ShapeOrdered is not { } ownOrdered || raw.Order.Count != ownSet.Count)
            return run;

        var own = HistoryBridge.RuleKey(run.ShapeVersion, run.ShapeRules);
        Dictionary<string, HistoryBridge>? bridge = null;
        IReadOnlyList<HistoryRawCall>[]? positions = null;
        foreach (var version in InteractionShape.KeptVersions)
            foreach (var rules in windowRules)
            {
                var key = HistoryBridge.RuleKey(version, rules?.Hash);
                if (key == own || (bridge ?? run.Bridge)?.ContainsKey(key) == true)
                    continue;

                // What a version renames no pattern changes, so the run's own bridge to the version has it; a version the run
                // made no bridge to is not one its fragment can be read under.
                IReadOnlyDictionary<string, string>? renamed = null;
                if (version != run.ShapeVersion)
                {
                    if (run.Bridge?.GetValueOrDefault(HistoryBridge.RuleKey(version, run.ShapeRules)) is not { } sameVersion)
                        continue;
                    renamed = sameVersion.Deps;
                }

                positions ??= Enumerable.Range(0, raw.Order.Count).Select(raw.At).ToArray();
                bridge ??= new Dictionary<string, HistoryBridge>(run.Bridge ?? new Dictionary<string, HistoryBridge>(), StringComparer.Ordinal);
                bridge[key] = Under(version, rules, positions, renamed, ownSet, ownOrdered, run.Calls);
            }
        return bridge is null ? run : run with { Bridge = bridge };
    }

    /// <summary>
    /// The consumer's patterns the runs a line of <paramref name="run"/> is read against were fingerprinted under,
    /// compiled from the lines that recorded them (#192 R1), and null for runs made under none: the last
    /// <paramref name="window"/> runs of the run's own stream and of each of <paramref name="streams"/>, the streams its
    /// verdicts read, as the analysis reads them, which reaches past the suite's last lines when another stream's runs
    /// crowd them (#95). A hash whose patterns no line records (every line before R1) cannot be bridged, and an edit away
    /// from it costs what it cost before.
    /// </summary>
    public static IReadOnlyList<HistoryShapeRules?> WindowRules(HistoryLedger ledger, HistoryRun run, IEnumerable<string?> streams, int window)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(streams);
        var rules = new List<HistoryShapeRules?>();
        var runs = streams.Append(run.Stream).OfType<string>().Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)
            .SelectMany(stream => ledger.PriorRuns(run.Suite, stream, run.Id, window))
            .Where(r => r.ShapeSet is not null).ToList();
        if (runs.Any(r => r.ShapeRules is null))
            rules.Add(null);
        foreach (var hash in runs.Select(r => r.ShapeRules).OfType<string>().Distinct(StringComparer.Ordinal))
            if (runs.FirstOrDefault(r => r.ShapeRules == hash && r.ShapeTemplates is { Count: > 0 })?.ShapeTemplates is { } templates
                && HistoryShapeRules.Create(templates) is { } compiled && string.Equals(compiled.Hash, hash, StringComparison.Ordinal))
                rules.Add(compiled);
        return rules;
    }
}
