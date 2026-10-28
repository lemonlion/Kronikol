using Kronikol.Constants;
using Kronikol.History;

namespace Kronikol.Query;

/// <summary>
/// Which call in another scenario is the same call: the rule <c>diff &lt;old&gt; &lt;new&gt; --body ADDR</c>
/// pairs a call across two runs by, and <c>compare</c> pairs two scenarios' calls by for its first differing
/// body (#115).
/// </summary>
/// <remarks>
/// <para>An interaction address is a position, and a position is not an identity across runs: calls a
/// service makes concurrently are recorded in the order they happened to start and finish, which changes
/// from run to run. Taking the entry at the same ordinal diffed two different calls and exited 0; measured on
/// two runs of one commit, 13 of 1,518 addresses, among them two services' health checks reported
/// byte-identical while one of them had changed.</para>
///
/// <para>A call is its request: a response is ranked through the request it answers, so a request and its
/// response always pair with the two halves of one call, whichever order the responses came back in. Two
/// keys, the second tried only for what the first leaves unpaired. The exact key is the service, the method
/// (ignoring case, as <c>--method</c> compares it) and the path and query - not the host and port, which a
/// container runtime picks per run, while the service name already says which dependency it is. The n-th
/// of the old scenario's calls with a key pairs with the n-th of the new scenario's. The shape key is the
/// same with the call's operation key in place of the path and query (<see cref="CallOperation.Of"/>: ids,
/// timestamps and numbers as placeholders, query values dropped, a statement's head), as the history fingerprint
/// keys it, because a URI that carries an id the test generated never matches across runs; it ranks only
/// the calls the exact key left unpaired on each side, so no call is the partner of two.</para>
///
/// <para>Both keys hold a GraphQL request's operation, where the n-th <c>POST /graphql</c> paired with the n-th
/// whatever it asked, and each report's calls are keyed under the HistoryShapeTemplates its run recorded (#192 R2):
/// <see cref="CallKeys"/>.</para>
/// </remarks>
internal static class CallPairing
{
    /// <summary>Why no call paired.</summary>
    internal enum Refusal
    {
        /// <summary>The other scenario makes no call with the key or the shape.</summary>
        NoCall,

        /// <summary>It makes the call, fewer times than the old scenario did.</summary>
        Fewer,

        /// <summary>The address is a response, and the call it pairs with has none recorded.</summary>
        NoResponse
    }

    /// <summary>What <see cref="Pair"/> found.</summary>
    /// <param name="Partner">The entry of the same half in the other scenario, or null when nothing paired.</param>
    /// <param name="PartnerCall">The other scenario's request the call paired with; under <see cref="Refusal.NoResponse"/>, the call without a response.</param>
    /// <param name="OnShape">Whether the shape key paired it, the exact key having found nothing.</param>
    /// <param name="Target">The old call's path and query, or its URI as written when it is not absolute.</param>
    /// <param name="Shape">The templated target: what the shape key compared.</param>
    /// <param name="Position">Which of the old scenario's calls with the key this is, from 1.</param>
    /// <param name="OldCount">How many of the old scenario's calls share the key.</param>
    /// <param name="NewCount">How many of the other scenario's calls share it.</param>
    /// <param name="Reason">Null when a call paired.</param>
    internal sealed record Result(
        InteractionEntry? Partner,
        InteractionEntry? PartnerCall,
        bool OnShape,
        string Target,
        string Shape,
        int Position,
        int OldCount,
        int NewCount,
        Refusal? Reason);

    /// <summary>
    /// How one report's calls are keyed (#192 R2): for the exact key, a GraphQL request's operation or a statement's head
    /// under the built-in rules alone; for the shape key, the operation key under <paramref name="rules"/>. A body is read
    /// only for a request, a statement-shaped call's or a POST's, once each, and the report opened for it only when one is:
    /// a response's body is what it returned, never what it asked.
    /// </summary>
    internal sealed class CallKeys(ReportIndex index, HistoryShapeRules? rules) : IDisposable
    {
        private readonly Dictionary<InteractionEntry, (string? Operation, string Shape)> _keys = new(ReferenceEqualityComparer.Instance);
        private BodyCache? _cache;

        /// <summary>The keys of the calls of one report, under the rules it recorded (<c>compare</c>'s two scenarios).</summary>
        internal static CallKeys Of(ReportIndex index) => new(index, HistoryShapeRules.ForReading(index.ShapeTemplates));

        /// <summary>
        /// The keys of two reports' calls (<c>diff --body</c>): under the rules both recorded, or under the built-in rules
        /// alone where they recorded different ones, since a call's shape under one run's rules and under another's are
        /// not comparable; before 5.19.0 no report recorded any and the shape was always read so.
        /// </summary>
        internal static (CallKeys Old, CallKeys New) Of(ReportIndex old, ReportIndex @new)
        {
            var rules = old.ShapeTemplates.SequenceEqual(@new.ShapeTemplates) ? HistoryShapeRules.ForReading(old.ShapeTemplates) : null;
            return (new CallKeys(old, rules), new CallKeys(@new, rules));
        }

        /// <summary>What went wrong with a rule the keys applied, each said once, as the run says it.</summary>
        internal IReadOnlyList<string> Problems => rules?.Problems ?? [];

        internal (string? Operation, string Shape) Of(InteractionEntry entry)
        {
            if (_keys.TryGetValue(entry, out var keys))
                return keys;
            var needsBody = entry.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)
                            && (DependencyCategories.IsStatementShaped(entry.DependencyCategory)
                                || string.Equals(entry.Method, "POST", StringComparison.OrdinalIgnoreCase));
            var content = needsBody && entry.BodyHash is { } hash ? (_cache ??= new BodyCache(index)).Raw(hash) : null;
            var call = CallOperation.Untemplated(entry.Method, entry.Uri, entry.DependencyCategory, content, queryKeys: true);
            var exact = call.Operation ?? (call.Head is { } head ? InteractionShape.StatementHead(head, null) : null);
            return _keys[entry] = (exact, CallOperation.Key(call, rules));
        }

        public void Dispose() => _cache?.Dispose();
    }

    /// <summary>
    /// The same call as <paramref name="entry"/>, the same half of it, in <paramref name="other"/>: the calls of
    /// <paramref name="scenario"/> keyed by <paramref name="mine"/>, its report's, and those of <paramref name="other"/> by
    /// <paramref name="theirs"/>.
    /// </summary>
    internal static Result Pair(ScenarioEntry scenario, InteractionEntry entry, ScenarioEntry other, CallKeys mine, CallKeys theirs)
    {
        var isResponse = Is(entry, "Response");

        // The call is ranked through its request. A response nothing pairs with is ranked among the
        // responses instead: the best that is left, and only for entries a writer recorded without an id.
        // An entry of any other type than the two this writer records is ranked among its own type.
        var request = isResponse ? QueryCommand.FindRequest(scenario, entry) : entry;
        var anchor = request ?? entry;
        var half = anchor.Type;

        var mineCalls = scenario.Interactions.Where(i => Is(i, half)).ToList();
        var theirCalls = other.Interactions.Where(i => Is(i, half)).ToList();
        var target = Target(anchor);
        var shape = mine.Of(anchor).Shape;

        var key = Key(anchor, mine);
        var mineExact = mineCalls.Where(i => Key(i, mine) == key).ToList();
        var theirsExact = theirCalls.Where(i => Key(i, theirs) == key).ToList();
        var rank = mineExact.IndexOf(anchor);
        if (rank < theirsExact.Count)
            return Found(theirsExact[rank], onShape: false, rank, mineExact.Count, theirsExact.Count);

        // Only what the exact key left unpaired, on both sides: a call the exact key paired already has its
        // partner, and ranking it here as well could hand that partner to a second call.
        var shapeKey = ShapeKey(anchor, mine);
        var mineShaped = Unpaired(mineCalls, mine, theirCalls, theirs).Where(i => ShapeKey(i, mine) == shapeKey).ToList();
        var theirsShaped = Unpaired(theirCalls, theirs, mineCalls, mine).Where(i => ShapeKey(i, theirs) == shapeKey).ToList();
        var shapedRank = mineShaped.IndexOf(anchor);
        if (shapedRank < theirsShaped.Count)
            return Found(theirsShaped[shapedRank], onShape: true, shapedRank, mineShaped.Count, theirsShaped.Count);

        // Said in the terms of the key that came closest: the URI when the other scenario makes that call at
        // all, the shape when it makes calls shaped like it, else neither.
        return theirsExact.Count > 0
            ? new Result(null, null, false, target, shape, rank + 1, mineExact.Count, theirsExact.Count, Refusal.Fewer)
            : theirsShaped.Count > 0
                ? new Result(null, null, true, target, shape, shapedRank + 1, mineShaped.Count, theirsShaped.Count, Refusal.Fewer)
                : new Result(null, null, false, target, shape, rank + 1, mineExact.Count, 0, Refusal.NoCall);

        Result Found(InteractionEntry call, bool onShape, int at, int oldCount, int newCount)
        {
            // The same half: the partner's response for a response, found the way every verb pairs halves.
            if (!isResponse || request is null)
                return new Result(call, call, onShape, target, shape, at + 1, oldCount, newCount, null);

            return QueryCommand.FindResponse(other, call) is { } response
                ? new Result(response, call, onShape, target, shape, at + 1, oldCount, newCount, null)
                : new Result(null, call, onShape, target, shape, at + 1, oldCount, newCount, Refusal.NoResponse);
        }
    }

    /// <summary>
    /// The path and query a call is identified by, or the URI as written when it is not absolute. A scheme
    /// is required, because on Unix a bare <c>/orders?x=1</c> parses as an absolute file URI whose query is
    /// escaped into its path, and the same report would key differently on two operating systems.
    /// </summary>
    internal static string Target(InteractionEntry entry) =>
        entry.Uri.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(entry.Uri, UriKind.Absolute, out var uri)
            ? uri.PathAndQuery
            : entry.Uri;

    /// <summary>The call in a few words: service, method and target, leaving out whichever the call has none of.</summary>
    internal static string Describe(InteractionEntry entry, string target) =>
        string.Join(" ", new[] { entry.ServiceName, entry.Method ?? "", target }.Where(part => part.Length > 0));

    /// <summary>
    /// The calls of <paramref name="side"/> the exact key leaves without a partner in <paramref name="opposite"/>:
    /// those past the number of calls the opposite side makes with the same key.
    /// </summary>
    private static List<InteractionEntry> Unpaired(List<InteractionEntry> side, CallKeys sideKeys, List<InteractionEntry> opposite, CallKeys oppositeKeys)
    {
        var available = opposite.GroupBy(i => Key(i, oppositeKeys)).ToDictionary(group => group.Key, group => group.Count());
        var seen = new Dictionary<CallKey, int>();
        var unpaired = new List<InteractionEntry>();
        foreach (var call in side)
        {
            var key = Key(call, sideKeys);
            var rank = seen.GetValueOrDefault(key);
            seen[key] = rank + 1;
            if (rank >= available.GetValueOrDefault(key))
                unpaired.Add(call);
        }
        return unpaired;
    }

    private readonly record struct CallKey(string Service, string Method, string Target, string? Operation);

    private static CallKey Key(InteractionEntry entry, CallKeys keys) =>
        new(entry.ServiceName, (entry.Method ?? "").ToUpperInvariant(), Target(entry), keys.Of(entry).Operation);

    // The operation key holds the operation, so the shape key needs no second copy of it.
    private static CallKey ShapeKey(InteractionEntry entry, CallKeys keys) =>
        new(entry.ServiceName, (entry.Method ?? "").ToUpperInvariant(), keys.Of(entry).Shape, null);

    private static bool Is(InteractionEntry entry, string type) =>
        entry.Type.Equals(type, StringComparison.OrdinalIgnoreCase);
}
