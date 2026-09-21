using System.Security.Cryptography;
using System.Text;

namespace Kronikol.History;

/// <summary>One scenario of a roster: its identity and the labels a surface needs to name it.</summary>
/// <param name="StableId">The cross-run identity, as <c>ScenarioStableId.Compute</c> produced it.</param>
/// <param name="Name">The scenario's display name at the time of the run.</param>
/// <param name="Feature">The feature it belonged to.</param>
/// <param name="Source">Where it is written — <c>path:line</c> — when the lane supplied one; a third rename signal (§7.4).</param>
public sealed record HistoryRosterEntry(string StableId, string Name, string Feature, string? Source);

/// <summary>
/// The ordered scenario list a run's results are positional against. Interned: the ledger writes a roster
/// once and every run line references it by hash, which is what keeps fifty runs of two hundred scenarios
/// at 228&#160;KB rather than repeating two hundred sixteen-hex ids on every line.
///
/// <para>The key is a <b>content hash and never a counter</b>. Under <c>merge=union</c> git keeps both
/// branches' lines; two rosters that differ but share a sequential key merge into a file nothing can read
/// correctly, silently. The same scenario set therefore yields the same key, so union de-duplicates the
/// line, and a different set yields a different key (§3.1).</para>
///
/// <para>An id may appear more than once — a <c>[Theory]</c> with repeated data, the same example row in
/// two <c>Examples:</c> blocks, a retried scenario — and each holder gets a slot, so the second is not
/// silently dropped (§5.5).</para>
/// </summary>
public sealed record HistoryRoster
{
    /// <summary>The content hash: the first sixteen hex of SHA-256 over the suite and the ids joined with <c>|</c>.</summary>
    public required string Hash { get; init; }

    /// <summary>The suite the roster belongs to; null when the run's suite did not resolve.</summary>
    public required string? Suite { get; init; }

    /// <summary>The stable ids, in run order, repeated where a scenario ran more than once.</summary>
    public required IReadOnlyList<string> Ids { get; init; }

    /// <summary>For each position, which holder of that id it is: 0 for the first, 1 for the second.</summary>
    public required IReadOnlyList<int> Slots { get; init; }

    /// <summary>The scenario display names, positionally.</summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>The feature names, positionally.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>The source locations (<c>path:line</c>), positionally; null where the lane had none.</summary>
    public required IReadOnlyList<string?> Sources { get; init; }

    /// <summary>How many positions the roster has.</summary>
    public int Count => Ids.Count;

    /// <summary>The roster for a list of scenarios, in the order given, with slots numbered and the hash computed.</summary>
    public static HistoryRoster Create(string? suite, IReadOnlyList<HistoryRosterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ids = new string[entries.Count];
        var slots = new int[entries.Count];
        var names = new string[entries.Count];
        var features = new string[entries.Count];
        var sources = new string?[entries.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ids[i] = entry.StableId;
            seen.TryGetValue(entry.StableId, out var holders);
            slots[i] = holders;
            seen[entry.StableId] = holders + 1;
            names[i] = entry.Name;
            features[i] = entry.Feature;
            sources[i] = entry.Source;
        }

        return new HistoryRoster
        {
            Hash = ComputeHash(suite, ids),
            Suite = suite,
            Ids = ids,
            Slots = slots,
            Names = names,
            Features = features,
            Sources = sources
        };
    }

    /// <summary>
    /// The content hash of a suite's id sequence — the same suite and sequence always yield the same key.
    /// The suite is part of it so that a roster names one suite unambiguously, and two suites that happen
    /// to share ids keep their own rosters.
    /// </summary>
    public static string ComputeHash(string? suite, IEnumerable<string> ids)
    {
        var joined = (suite ?? "\u0001") + "\n" + string.Join("|", ids);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// The position of a scenario in this roster, or -1. A scan of the roster: right for one lookup, and
    /// quadratic when asked once per scenario, which is why the analyzer indexes a roster instead (#91).
    /// </summary>
    public int IndexOf(string stableId, int slot = 0)
    {
        for (var i = 0; i < Ids.Count; i++)
            if (Slots[i] == slot && string.Equals(Ids[i], stableId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    /// <summary>The entries, positionally.</summary>
    public IEnumerable<HistoryRosterEntry> Entries()
    {
        for (var i = 0; i < Ids.Count; i++)
            yield return new HistoryRosterEntry(Ids[i], Names[i], Features[i], Sources[i]);
    }
}

/// <summary>
/// The distinct templated calls a run made, sorted, interned like a roster: the ledger writes the list
/// once and every run line whose scenarios made the same calls references it by hash, each position
/// carrying its calls as indices into the list (<see cref="HistoryRun.CallSets"/>). It is what lets a
/// behaviour verdict name the call that appeared or disappeared rather than only count. Content-hashed
/// for the reason a roster is (§3.1): the same calls yield the same key, so a union merge de-duplicates
/// the line and never glues two lists under one key.
/// </summary>
public sealed record HistoryShapes
{
    /// <summary>The content hash: the first sixteen hex of SHA-256 over the calls joined with a newline.</summary>
    public required string Hash { get; init; }

    /// <summary>The distinct call lines (<c>caller&gt;service method uri status</c>), sorted ordinally.</summary>
    public required IReadOnlyList<string> Calls { get; init; }

    /// <summary>How many distinct calls the list holds.</summary>
    public int Count => Calls.Count;

    /// <summary>The list for a run's calls, de-duplicated and sorted, with the hash computed.</summary>
    public static HistoryShapes Create(IEnumerable<string> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var sorted = calls.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        return new HistoryShapes { Hash = ComputeHash(sorted), Calls = sorted };
    }

    /// <summary>The hash a list of calls keys under.</summary>
    public static string ComputeHash(IEnumerable<string> calls)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", calls)));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>The call line at an index, or null past the end.</summary>
    public string? At(int index) => index >= 0 && index < Calls.Count ? Calls[index] : null;
}

/// <summary>
/// One run of one suite: its identity, and one result per roster position. Everything positional is
/// aligned with the roster the line references by hash. Fields that a milestone had not yet learned to
/// fill are still written from v1 with a defined unknown encoding, so surfacing them later needs no
/// format version (§3.5).
/// </summary>
public sealed record HistoryRun
{
    /// <summary>
    /// <c>gh:&lt;runId&gt;:&lt;attempt&gt;</c> on GitHub Actions, <c>ado:&lt;buildId&gt;:1</c> on Azure
    /// DevOps, <c>local:&lt;stamp&gt;:&lt;hash&gt;</c> otherwise. The provider's run id is what makes eight
    /// shards one run; the attempt is what keeps a re-run from erasing the failure it re-ran (§3.3).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The suite; null when it did not resolve, and recorded as such rather than as a name.</summary>
    public required string? Suite { get; init; }

    /// <summary>
    /// Whether the run is a strict subset of the suite — a filtered run, a crashed half — so that every
    /// scenario it lacks is not <c>absent</c>. Null means undecided: a fragment carries an explicit value
    /// only, and the heuristic is applied against the ledger when the line is appended (§5.12).
    /// </summary>
    public required bool? Partial { get; init; }

    /// <summary>When the run finished. A label for a reader; never the ordering (§5.13).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The branch, or null off CI — such runs form the <c>local</c> stream (§7.5).</summary>
    public required string? Branch { get; init; }

    /// <summary>The commit the run tested, when known.</summary>
    public required string? Commit { get; init; }

    /// <summary>The CI provider's name, or null off CI.</summary>
    public required string? Provider { get; init; }

    /// <summary>The pipeline URL, when known.</summary>
    public required string? Url { get; init; }

    /// <summary>How many fragments were folded into this line; 1 for a direct append.</summary>
    public required int Shards { get; init; }

    /// <summary>The hash of the roster the positions below refer to.</summary>
    public required string RosterHash { get; init; }

    /// <summary>One <see cref="HistoryFormat"/> result character per roster position.</summary>
    public required string Results { get; init; }

    /// <summary>One attempt character per roster position; <see cref="HistoryFormat.AttemptUnknown"/> where the runner said nothing.</summary>
    public required string Attempts { get; init; }

    /// <summary>Whole milliseconds per position, null where untimed; the whole list is absent when durations are not recorded.</summary>
    public IReadOnlyList<int?>? Durations { get; init; }

    /// <summary>Logical calls made per position — the cheap, explainable half of a behaviour change (§4).</summary>
    public IReadOnlyList<int>? Calls { get; init; }

    /// <summary>The order-insensitive interaction fingerprint per position — the primary behaviour signal (§2.4).</summary>
    public IReadOnlyList<string>? ShapeSet { get; init; }

    /// <summary>The ordered interaction fingerprint per position — the secondary, reorder-sensitive signal.</summary>
    public IReadOnlyList<string>? ShapeOrdered { get; init; }

    /// <summary>
    /// The rule the fingerprints were made by (<see cref="InteractionShape.Version"/>): null when none are
    /// recorded, 1 for a line written before the rule was recorded. Fingerprints made by different rules
    /// are not compared.
    /// </summary>
    public int? ShapeVersion { get; init; }

    /// <summary>
    /// The hash of the consumer templating rules the fingerprints were made under
    /// (<see cref="HistoryShapeRules.Hash"/>, 3.25.0); null when there were none. With
    /// <see cref="ShapeVersion"/> it is the rule a fingerprint was made by: fingerprints made under
    /// different rules are not compared, so editing a rule costs one quiet run.
    /// </summary>
    public string? ShapeRules { get; init; }

    /// <summary>The hash of the <see cref="HistoryShapes"/> line <see cref="CallSets"/> index into; null when no call lists are recorded.</summary>
    public string? ShapesHash { get; init; }

    /// <summary>
    /// Per position, the sorted indices into the shapes list of the distinct calls the scenario made: the
    /// set the fingerprint hashes, spelled out. Absent on a line from before 3.17.0, when the fingerprint
    /// alone was recorded and a change could be counted but not named.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>>? CallSets { get; init; }

    /// <summary>Per position, a key into <see cref="ErrorText"/> for a failure, or null.</summary>
    public IReadOnlyList<string?>? Errors { get; init; }

    /// <summary>The error cluster keys this line references — per line and local, so a dropped line loses nothing else.</summary>
    public IReadOnlyDictionary<string, string> ErrorText { get; init; } = new Dictionary<string, string>();

    /// <summary>The distinct <c>caller&gt;service</c> pairs the run captured, sorted — the new-dependency signal (§4).</summary>
    public IReadOnlyList<string>? Deps { get; init; }

    /// <summary>The branch stream this run belongs to: its branch, or <c>local</c> when it has none.</summary>
    public string Stream => Branch is { Length: > 0 } branch ? branch : LocalStream;

    /// <summary>The stream a run with no branch belongs to.</summary>
    public const string LocalStream = "local";

    /// <summary>The result at a roster position, or <see cref="HistoryFormat.Absent"/> past the end.</summary>
    public char ResultAt(int position) => position >= 0 && position < Results.Length ? Results[position] : HistoryFormat.Absent;

    /// <summary>The 1-based attempt at a roster position, or null.</summary>
    public int? AttemptAt(int position) => position >= 0 && position < Attempts.Length ? HistoryFormat.AttemptOf(Attempts[position]) : null;

    /// <summary>The duration at a roster position, or null.</summary>
    public int? DurationAt(int position) => Durations is not null && position >= 0 && position < Durations.Count ? Durations[position] : null;

    /// <summary>The call count at a roster position, or null.</summary>
    public int? CallsAt(int position) => Calls is not null && position >= 0 && position < Calls.Count ? Calls[position] : null;

    /// <summary>The set fingerprint at a roster position, or null.</summary>
    public string? ShapeSetAt(int position) => ShapeSet is not null && position >= 0 && position < ShapeSet.Count ? ShapeSet[position] : null;

    /// <summary>The ordered fingerprint at a roster position, or null.</summary>
    public string? ShapeOrderedAt(int position) => ShapeOrdered is not null && position >= 0 && position < ShapeOrdered.Count ? ShapeOrdered[position] : null;

    /// <summary>The call-set indices at a roster position, or null.</summary>
    public IReadOnlyList<int>? CallSetAt(int position) => CallSets is not null && position >= 0 && position < CallSets.Count ? CallSets[position] : null;

    /// <summary>The error cluster text at a roster position, or null.</summary>
    public string? ErrorAt(int position)
    {
        if (Errors is null || position < 0 || position >= Errors.Count || Errors[position] is not { } key)
            return null;
        return ErrorText.TryGetValue(key, out var text) ? text : null;
    }
}

/// <summary>What kind of line a ledger line is.</summary>
public enum HistoryLineKind
{
    /// <summary>The first line: the format version and the writer.</summary>
    Header,

    /// <summary>A roster declaration.</summary>
    Roster,

    /// <summary>One run.</summary>
    Run,

    /// <summary>A shapes declaration: the distinct calls a run's positions index into.</summary>
    Shapes
}

/// <summary>One parsed ledger line.</summary>
/// <param name="Kind">Which kind it is.</param>
/// <param name="Version">The format version, on a header.</param>
/// <param name="Generator">The writer's version, on a header.</param>
/// <param name="Roster">The roster, on a roster line.</param>
/// <param name="Run">The run, on a run line.</param>
/// <param name="Shapes">The shapes, on a shapes line.</param>
public sealed record HistoryLine(HistoryLineKind Kind, int? Version = null, string? Generator = null, HistoryRoster? Roster = null, HistoryRun? Run = null, HistoryShapes? Shapes = null);

/// <summary>
/// The one-run fragment a run writes into its reports directory as <see cref="HistoryFormat.FragmentFileName"/>:
/// a run line's worth, with the roster it needs, no window and no prior data. Race-free (one process, its
/// own directory), automatically part of the CI artifact, and the unit <c>kronikol history record</c> folds
/// — which is also what makes eight shards one run (§3.6).
/// </summary>
/// <param name="Version">The format version the fragment was written under.</param>
/// <param name="Roster">The run's roster.</param>
/// <param name="Run">The run.</param>
/// <param name="Shapes">The run's distinct calls, when recorded; null on a fragment from before 3.17.0.</param>
public sealed record HistoryFragment(int Version, HistoryRoster Roster, HistoryRun Run, HistoryShapes? Shapes = null)
{
    /// <summary>The fragment as indented JSON — a person opens the artifact, so it is readable.</summary>
    public static string Write(HistoryRoster roster, HistoryRun run, string generator, HistoryShapes? shapes = null) =>
        HistoryJson.Fragment(roster, run, generator, shapes);

    /// <summary>Parses a fragment; throws <see cref="FormatException"/> when it is not one.</summary>
    public static HistoryFragment Parse(string json) => HistoryJson.ParseFragment(json);
}
