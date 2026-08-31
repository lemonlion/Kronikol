using System.Collections.Concurrent;

namespace Kronikol.Reports.SearchIndex;

/// <summary>
/// Cross-report memoization for the deep-search index build (SEARCH_INDEX_PLAN §5.1): the
/// Specifications and TestRunReport HTML reports render the same features and diagrams, and the
/// expensive half of the index — normalizing + trigram-hashing each distinct text piece — is
/// identical for both. One instance is created per report run and passed to both
/// <see cref="ReportGenerator.GenerateHtmlReport"/> calls; each report then only assembles its
/// own doc table and serializes. The same body also appears many times inside one report
/// (repeated payloads), so memoization pays within a single report too.
/// </summary>
public sealed class SearchIndexBuildCache
{
    private readonly ConcurrentDictionary<string, int[]> _bucketsByText = new();
    private readonly object _prewarmLock = new();
    private Task? _prewarm;

    /// <summary>
    /// Starts hashing the given texts on the thread pool, once; later calls are no-ops. Kicked
    /// off at the start of report generation so the heavy hashing overlaps HTML body building.
    /// </summary>
    internal void StartPrewarm(IEnumerable<string> texts)
    {
        if (_prewarm is not null) return;
        lock (_prewarmLock)
        {
            if (_prewarm is not null) return;
            var distinct = texts.Distinct().ToArray();
            _prewarm = Task.Run(() => Parallel.ForEach(distinct, t => GetOrAddBuckets(t)));
        }
    }

    /// <summary>Waits for the prewarm (if any) so assembly-time lookups are hits, not duplicate work.</summary>
    internal void WaitForPrewarm() => _prewarm?.Wait();

    /// <summary>Trigram bucket set for one raw corpus piece, normalized then hashed once per distinct string.</summary>
    internal int[] GetOrAddBuckets(string rawPiece) =>
        _bucketsByText.GetOrAdd(rawPiece, static piece =>
        {
            var buckets = new HashSet<int>();
            SearchIndexBuilder.AddTrigramBuckets(SearchNormalizer.Normalize(piece), buckets);
            var arr = buckets.ToArray();
            Array.Sort(arr);
            return arr;
        });

    /// <summary>Number of distinct text pieces hashed — the deterministic perf observable (§5.1: pin cost with observables, never wall-clock).</summary>
    internal int DistinctTextCount => _bucketsByText.Count;
}
