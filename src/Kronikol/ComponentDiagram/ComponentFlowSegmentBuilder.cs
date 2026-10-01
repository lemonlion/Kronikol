using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.ComponentDiagram;

/// <summary>
/// Flow data for a single component relationship, containing the aggregated span segment
/// and per-test summaries.
/// </summary>
public record RelationshipFlowData(
    InternalFlowSegment AggregatedSegment,
    RelationshipTestSummary[] TestSummaries);

/// <summary>
/// Summary of a single test's contribution to a component relationship.
/// </summary>
public record RelationshipTestSummary(
    string TestId,
    string TestName,
    int SpanCount,
    double DurationMs);

/// <summary>
/// Builds aggregated internal-flow segment data per component relationship
/// from the spans collected during test execution.
/// </summary>
public static class ComponentFlowSegmentBuilder
{
    /// <summary>
    /// Builds aggregated flow data per relationship, grouping spans from all tests
    /// that exercised each (Caller, Service) pair.
    /// </summary>
    public static Dictionary<string, RelationshipFlowData> BuildRelationshipSegments(
        ComponentRelationship[] relationships,
        RequestResponseLog[] logs,
        Dictionary<string, InternalFlowSegment> perBoundarySegments)
    {
        var result = new Dictionary<string, RelationshipFlowData>();

        if (relationships.Length == 0 || perBoundarySegments.Count == 0)
            return result;

        foreach (var rel in relationships)
        {
            var relKey = $"iflow-rel-{SanitizeKey(rel.Caller)}-{SanitizeKey(rel.Service)}";

            var matchingLogIds = logs
                .Where(l => l.CallerName == rel.Caller && l.ServiceName == rel.Service
                            && l.Type == RequestResponseType.Request)
                .Select(l => l.RequestResponseId)
                .ToHashSet();

            var allSpans = new List<FlowSpan>();
            var testSpans = new Dictionary<string, List<FlowSpan>>();

            foreach (var (segKey, segment) in perBoundarySegments)
            {
                if (!matchingLogIds.Contains(segment.RequestResponseId))
                    continue;

                allSpans.AddRange(segment.FlowSpans);

                if (!testSpans.TryGetValue(segment.TestId, out var list))
                {
                    list = [];
                    testSpans[segment.TestId] = list;
                }
                list.AddRange(segment.FlowSpans);
            }

            if (allSpans.Count == 0)
                continue;

            var ordered = allSpans.OrderBy(s => s.StartTimeUtc).ToArray();
            var aggregated = InternalFlowSegment.Of(
                Guid.Empty, RequestResponseType.Request, "aggregated",
                new DateTimeOffset(ordered.Min(s => s.StartTimeUtc), TimeSpan.Zero),
                new DateTimeOffset(ordered.Max(s => s.StartTimeUtc + s.Duration), TimeSpan.Zero),
                ordered);

            var testName = logs
                .Where(l => l.CallerName == rel.Caller && l.ServiceName == rel.Service)
                .GroupBy(l => l.TestId)
                .ToDictionary(g => g.Key, g => g.First().TestName);

            var summaries = testSpans
                .Select(kv =>
                {
                    var spans = kv.Value;
                    var totalDuration = spans.Sum(s => s.Duration.TotalMilliseconds);
                    return new RelationshipTestSummary(
                        kv.Key,
                        testName.GetValueOrDefault(kv.Key, kv.Key),
                        spans.Count,
                        totalDuration);
                })
                .OrderByDescending(s => s.DurationMs)
                .ToArray();

            result[relKey] = new RelationshipFlowData(aggregated, summaries);
        }

        return result;
    }

    /// <summary>
    /// Builds a single system-level segment containing all spans across all tests.
    /// </summary>
    [Obsolete("Use the per-test flame chart approach instead. This method merges all spans and can produce diagrams that are too large to render.")]
    public static InternalFlowSegment BuildSystemSegment(
        Dictionary<string, InternalFlowSegment> wholeTestSegments)
    {
        var allSpans = wholeTestSegments.Values
            .SelectMany(s => s.FlowSpans)
            .OrderBy(s => s.StartTimeUtc)
            .ToArray();

        if (allSpans.Length == 0)
            return new InternalFlowSegment(Guid.Empty, RequestResponseType.Request, "system", null, null, []);

        return InternalFlowSegment.Of(
            Guid.Empty, RequestResponseType.Request, "system",
            new DateTimeOffset(allSpans.Min(s => s.StartTimeUtc), TimeSpan.Zero),
            new DateTimeOffset(allSpans.Max(s => s.StartTimeUtc + s.Duration), TimeSpan.Zero),
            allSpans);
    }

    /// <summary>
    /// Computes aggregated performance statistics per relationship from request/response log pairs.
    /// </summary>
    public static Dictionary<string, RelationshipStats> ComputeRelationshipStats(
        ComponentRelationship[] relationships,
        RequestResponseLog[] logs,
        int lowCoverageThreshold = 3)
    {
        var result = new Dictionary<string, RelationshipStats>();
        if (relationships.Length == 0 || logs.Length == 0)
            return result;

        // Build response lookup: RequestResponseId → response log
        var responseLookup = new Dictionary<Guid, RequestResponseLog>();
        foreach (var log in logs)
        {
            if (log.Type == RequestResponseType.Response && log.Timestamp.HasValue)
                responseLookup.TryAdd(log.RequestResponseId, log);
        }

        // Group request logs by (Caller, Service)
        var requestLogs = logs
            .Where(l => l.Type == RequestResponseType.Request
                        && !l.TrackingIgnore && !l.IsOverrideStart && !l.IsOverrideEnd && !l.IsActionStart
                        && l.Timestamp.HasValue)
            .ToArray();

        // Track per-test, per-relationship duration sums for latency contribution (two-pass)
        var testRelDurations = new Dictionary<string, Dictionary<string, double>>(); // testId → relKey → sumMs

        foreach (var rel in relationships)
        {
            var relKey = $"iflow-rel-{SanitizeKey(rel.Caller)}-{SanitizeKey(rel.Service)}";

            var relRequests = requestLogs
                .Where(l => l.CallerName == rel.Caller && l.ServiceName == rel.Service)
                .ToArray();

            // Pair with responses to get durations
            var callDurations = new List<(double DurationMs, RequestResponseLog Request, RequestResponseLog Response)>();
            foreach (var req in relRequests)
            {
                if (responseLookup.TryGetValue(req.RequestResponseId, out var res))
                {
                    var duration = (res.Timestamp!.Value - req.Timestamp!.Value).TotalMilliseconds;
                    if (duration >= 0)
                        callDurations.Add((duration, req, res));
                }
            }

            if (callDurations.Count == 0)
                continue;

            // Accumulate per-test duration sums for latency contribution
            foreach (var call in callDurations)
            {
                var testId = call.Request.TestId;
                if (!testRelDurations.TryGetValue(testId, out var relDurs))
                {
                    relDurs = new Dictionary<string, double>();
                    testRelDurations[testId] = relDurs;
                }
                relDurs.TryAdd(relKey, 0);
                relDurs[relKey] += call.DurationMs;
            }

            var durations = callDurations.Select(c => c.DurationMs).OrderBy(d => d).ToArray();
            var testCount = callDurations.Select(c => c.Request.TestId).Distinct().Count();

            // Error rate + status code distribution
            var statusDist = new Dictionary<HttpStatusCode, int>();
            var errorCount = 0;
            foreach (var call in callDurations)
            {
                if (call.Response.StatusCode?.Value is HttpStatusCode sc)
                {
                    statusDist.TryAdd(sc, 0);
                    statusDist[sc]++;
                    if ((int)sc >= 400)
                        errorCount++;
                }
            }

            // Payload sizes
            PayloadSizeStats? payloadSizes = null;
            var requestSizes = callDurations
                .Where(c => c.Request.Content != null)
                .Select(c => (double)c.Request.Content!.Length)
                .ToArray();
            var responseSizes = callDurations
                .Where(c => c.Response.Content != null)
                .Select(c => (double)c.Response.Content!.Length)
                .ToArray();

            if (requestSizes.Length > 0 || responseSizes.Length > 0)
            {
                payloadSizes = new PayloadSizeStats(
                    RequestMeanBytes: requestSizes.Length > 0 ? requestSizes.Average() : 0,
                    RequestP95Bytes: requestSizes.Length > 0 ? Percentile(requestSizes.OrderBy(x => x).ToArray(), 95) : 0,
                    ResponseMeanBytes: responseSizes.Length > 0 ? responseSizes.Average() : 0,
                    ResponseP95Bytes: responseSizes.Length > 0 ? Percentile(responseSizes.OrderBy(x => x).ToArray(), 95) : 0);
            }

            // Endpoint breakdown
            var endpointGroups = callDurations
                .GroupBy(c => (Method: c.Request.Method.Value?.ToString() ?? "Unknown", Path: NormalizePathGuids(c.Request.Uri.AbsolutePath)))
                .Select(g =>
                {
                    var epDurations = g.Select(c => c.DurationMs).OrderBy(d => d).ToArray();
                    var epErrors = g.Count(c => c.Response.StatusCode?.Value is HttpStatusCode sc && (int)sc >= 400);
                    return new EndpointStats(
                        Method: g.Key.Method,
                        Path: g.Key.Path,
                        CallCount: g.Count(),
                        MeanMs: epDurations.Average(),
                        MedianMs: Percentile(epDurations, 50),
                        P95Ms: Percentile(epDurations, 95),
                        P99Ms: Percentile(epDurations, 99),
                        MinMs: epDurations[0],
                        MaxMs: epDurations[^1],
                        ErrorRate: (double)epErrors / g.Count());
                })
                .ToArray();

            // Concurrency detection — find overlapping calls from the same caller within the same test
            ConcurrencyInfo? concurrency = null;
            var callsByTest = callDurations
                .GroupBy(c => c.Request.TestId)
                .ToArray();

            var concurrentTestCount = 0;
            var concurrentPairs = new HashSet<string>();
            foreach (var testGroup in callsByTest)
            {
                var testCalls = testGroup
                    .OrderBy(c => c.Request.Timestamp!.Value)
                    .ToArray();

                var foundConcurrent = false;
                for (int i = 0; i < testCalls.Length; i++)
                {
                    var reqStart = testCalls[i].Request.Timestamp!.Value;
                    var reqEnd = testCalls[i].Response.Timestamp!.Value;

                    // Check against all other in-flight calls in the SAME test
                    // Look at all requests from the same caller in this test
                    var sameCallerCalls = logs
                        .Where(l => l.Type == RequestResponseType.Request
                                    && l.TestId == testGroup.Key
                                    && l.CallerName == rel.Caller
                                    && l.Timestamp.HasValue
                                    && l.RequestResponseId != testCalls[i].Request.RequestResponseId
                                    && responseLookup.ContainsKey(l.RequestResponseId))
                        .Select(l => (Start: l.Timestamp!.Value, End: responseLookup[l.RequestResponseId].Timestamp!.Value, Service: l.ServiceName))
                        .ToArray();

                    foreach (var other in sameCallerCalls)
                    {
                        if (reqStart < other.End && reqEnd > other.Start)
                        {
                            foundConcurrent = true;
                            concurrentPairs.Add($"{rel.Service}+{other.Service}");
                        }
                    }
                }
                if (foundConcurrent) concurrentTestCount++;
            }

            if (concurrentTestCount > 0)
            {
                concurrency = new ConcurrencyInfo(
                    ConcurrentCallCount: concurrentTestCount,
                    ConcurrencyPercentage: (double)concurrentTestCount / callsByTest.Length * 100,
                    ConcurrentPairs: concurrentPairs.ToArray());
            }

            // Coefficient of Variation (stdDev / mean)
            var mean = durations.Average();
            var variance = durations.Select(d => (d - mean) * (d - mean)).Sum() / durations.Length;
            var stdDev = Math.Sqrt(variance);
            var cv = mean > 0 ? stdDev / mean : 0;

            // Method distribution
            var methodDist = callDurations
                .GroupBy(c => c.Request.Method.Value?.ToString() ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            // Outlier detection (mean + 2σ), only when ≥5 calls
            OutlierInfo? outliers = null;
            if (callDurations.Count >= 5)
            {
                var threshold = mean + 2 * stdDev;
                var outlierCalls = callDurations
                    .Where(c => c.DurationMs > threshold)
                    .GroupBy(c => c.Request.TestId)
                    .Select(g =>
                    {
                        var worst = g.OrderByDescending(c => c.DurationMs).First();
                        var deviations = stdDev > 0 ? (worst.DurationMs - mean) / stdDev : 0;
                        return new OutlierDetail(worst.Request.TestName, worst.DurationMs, deviations);
                    })
                    .OrderByDescending(o => o.DeviationsFromMean)
                    .Take(5)
                    .ToArray();

                if (outlierCalls.Length > 0)
                {
                    outliers = new OutlierInfo(outlierCalls.Length, threshold, outlierCalls);
                }
            }

            result[relKey] = new RelationshipStats(
                CallCount: callDurations.Count,
                TestCount: testCount,
                MeanMs: durations.Average(),
                MedianMs: Percentile(durations, 50),
                P95Ms: Percentile(durations, 95),
                P99Ms: Percentile(durations, 99),
                MinMs: durations[0],
                MaxMs: durations[^1],
                ErrorRate: (double)errorCount / callDurations.Count,
                StatusCodeDistribution: statusDist,
                EndpointBreakdown: endpointGroups,
                PayloadSizes: payloadSizes,
                Concurrency: concurrency,
                IsLowCoverage: testCount < lowCoverageThreshold,
                CoefficientOfVariation: cv,
                MethodDistribution: methodDist,
                Outliers: outliers,
                LatencyContributionPct: 0);
        }

        // Second pass: compute latency contribution percentages
        if (testRelDurations.Count > 0)
        {
            // For each test, compute total duration across all relationships
            var testTotals = testRelDurations.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Values.Sum());

            // For each relationship, compute average contribution % across tests that include it
            foreach (var relKey in result.Keys.ToArray())
            {
                var contributions = new List<double>();
                foreach (var (testId, relDurs) in testRelDurations)
                {
                    if (relDurs.TryGetValue(relKey, out var relDur) && testTotals[testId] > 0)
                    {
                        contributions.Add(relDur / testTotals[testId] * 100);
                    }
                }

                if (contributions.Count > 0)
                {
                    result[relKey] = result[relKey] with { LatencyContributionPct = contributions.Average() };
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Computes the call ordering within each test, showing the sequence of dependency calls.
    /// </summary>
    public static TestCallOrdering[] BuildCallOrdering(RequestResponseLog[] logs)
    {
        if (logs.Length == 0) return [];

        return logs
            .Where(l => l.Type == RequestResponseType.Request
                        && !l.TrackingIgnore && !l.IsOverrideStart && !l.IsOverrideEnd && !l.IsActionStart
                        && l.Timestamp.HasValue)
            .GroupBy(l => l.TestId)
            .Select(g =>
            {
                var ordered = g.OrderBy(l => l.Timestamp!.Value).ToArray();
                var entries = ordered.Select((l, idx) => new CallOrderEntry(
                    Position: idx + 1,
                    Caller: l.CallerName,
                    Service: l.ServiceName,
                    Method: l.Method.Value?.ToString() ?? "Unknown",
                    Path: l.Uri.AbsolutePath)).ToArray();

                return new TestCallOrdering(g.Key, ordered[0].TestName, entries);
            })
            .ToArray();
    }

    /// <summary>
    /// Aggregates call ordering data into patterns showing how often service A is called before service B.
    /// </summary>
    public static CallOrderingPattern[] ComputeCallOrderingPatterns(TestCallOrdering[] orderings)
    {
        if (orderings.Length == 0) return [];

        // For each test, extract the order of first appearance of each service
        var pairCounts = new Dictionary<(string, string), (int AFirst, int BFirst)>();

        foreach (var test in orderings)
        {
            var serviceOrder = new List<string>();
            foreach (var entry in test.Entries)
            {
                if (!serviceOrder.Contains(entry.Service))
                    serviceOrder.Add(entry.Service);
            }

            // For all pairs of services in this test
            for (int i = 0; i < serviceOrder.Count; i++)
            {
                for (int j = i + 1; j < serviceOrder.Count; j++)
                {
                    var a = serviceOrder[i];
                    var b = serviceOrder[j];
                    // Normalize: always use alphabetical order as the key
                    var key = string.Compare(a, b, StringComparison.Ordinal) <= 0 ? (a, b) : (b, a);
                    pairCounts.TryAdd(key, (0, 0));
                    var (af, bf) = pairCounts[key];
                    if (key.Item1 == a)
                        pairCounts[key] = (af + 1, bf);
                    else
                        pairCounts[key] = (af, bf + 1);
                }
            }
        }

        return pairCounts
            .Where(kv => kv.Value.AFirst + kv.Value.BFirst >= 3)
            .Select(kv =>
            {
                var total = kv.Value.AFirst + kv.Value.BFirst;
                var (first, second, pct) = kv.Value.AFirst >= kv.Value.BFirst
                    ? (kv.Key.Item1, kv.Key.Item2, (double)kv.Value.AFirst / total * 100)
                    : (kv.Key.Item2, kv.Key.Item1, (double)kv.Value.BFirst / total * 100);
                return new CallOrderingPattern(first, second, pct, total);
            })
            .Where(p => p.PctFirstBeforeSecond >= 60)
            .OrderByDescending(p => p.SampleCount)
            .ToArray();
    }

    /// <summary>
    /// Computes pairwise error co-occurrence between relationships across tests.
    /// </summary>
    public static ErrorCorrelation[] ComputeErrorCorrelations(
        ComponentRelationship[] relationships,
        RequestResponseLog[] logs)
    {
        if (relationships.Length == 0 || logs.Length == 0) return [];

        // Build response lookup
        var responseLookup = new Dictionary<Guid, RequestResponseLog>();
        foreach (var log in logs)
        {
            if (log.Type == RequestResponseType.Response && log.Timestamp.HasValue)
                responseLookup.TryAdd(log.RequestResponseId, log);
        }

        // For each test, collect the set of relKeys that had errors
        var testErrorSets = new Dictionary<string, HashSet<string>>();
        foreach (var req in logs.Where(l => l.Type == RequestResponseType.Request && !l.TrackingIgnore && !l.IsOverrideStart && !l.IsOverrideEnd && !l.IsActionStart))
        {
            if (responseLookup.TryGetValue(req.RequestResponseId, out var res)
                && res.StatusCode?.Value is System.Net.HttpStatusCode sc && (int)sc >= 400)
            {
                var relKey = $"{req.CallerName} \u2192 {req.ServiceName}";
                if (!testErrorSets.TryGetValue(req.TestId, out var set))
                {
                    set = [];
                    testErrorSets[req.TestId] = set;
                }
                set.Add(relKey);
            }
        }

        // Pairwise co-occurrence
        var results = new List<ErrorCorrelation>();
        var allErrorRels = testErrorSets.Values.SelectMany(s => s).Distinct().ToArray();

        for (int i = 0; i < allErrorRels.Length; i++)
        {
            var relA = allErrorRels[i];
            var testsWithAError = testErrorSets.Count(kv => kv.Value.Contains(relA));
            if (testsWithAError < 2) continue;

            for (int j = i + 1; j < allErrorRels.Length; j++)
            {
                var relB = allErrorRels[j];
                var coOccurrences = testErrorSets.Count(kv => kv.Value.Contains(relA) && kv.Value.Contains(relB));
                if (coOccurrences == 0) continue;

                var pctFromA = (double)coOccurrences / testsWithAError * 100;
                var testsWithBError = testErrorSets.Count(kv => kv.Value.Contains(relB));
                var pctFromB = (double)coOccurrences / testsWithBError * 100;

                if (pctFromA >= 50)
                    results.Add(new ErrorCorrelation(relA, relB, pctFromA, coOccurrences, testsWithAError));
                if (pctFromB >= 50 && testsWithBError >= 2)
                    results.Add(new ErrorCorrelation(relB, relA, pctFromB, coOccurrences, testsWithBError));
            }
        }

        return results.OrderByDescending(r => r.CoOccurrenceCount).ToArray();
    }

    /// <summary>
    /// Computes dependency graph metrics: fan-in/fan-out, circular dependencies, longest chain.
    /// </summary>
    public static DependencyGraphMetrics ComputeGraphMetrics(ComponentRelationship[] relationships)
    {
        if (relationships.Length == 0)
            return new DependencyGraphMetrics([], [], 0, []);

        // Build adjacency
        var outgoing = new Dictionary<string, HashSet<string>>();
        var incoming = new Dictionary<string, HashSet<string>>();
        var allNodes = new HashSet<string>();

        foreach (var rel in relationships)
        {
            allNodes.Add(rel.Caller);
            allNodes.Add(rel.Service);

            if (!outgoing.TryGetValue(rel.Caller, out var outs))
            {
                outs = [];
                outgoing[rel.Caller] = outs;
            }
            outs.Add(rel.Service);

            if (!incoming.TryGetValue(rel.Service, out var ins))
            {
                ins = [];
                incoming[rel.Service] = ins;
            }
            ins.Add(rel.Caller);
        }

        // Service metrics
        var services = allNodes.Select(n => new ServiceMetrics(
            Name: n,
            FanIn: incoming.GetValueOrDefault(n)?.Count ?? 0,
            FanOut: outgoing.GetValueOrDefault(n)?.Count ?? 0,
            InboundFrom: (incoming.GetValueOrDefault(n) ?? []).OrderBy(x => x).ToArray(),
            OutboundTo: (outgoing.GetValueOrDefault(n) ?? []).OrderBy(x => x).ToArray()
        )).OrderByDescending(s => s.FanIn + s.FanOut).ToArray();

        // Cycle detection (Johnson's algorithm simplified — find all SCCs via Tarjan's)
        var cycles = FindCycles(allNodes, outgoing);

        // Longest path (DFS with memoization, only meaningful for DAGs or longest simple path)
        var (longestLength, longestChain) = FindLongestPath(allNodes, outgoing);

        return new DependencyGraphMetrics(
            Services: services,
            CircularDependencies: cycles,
            LongestChainLength: longestLength,
            LongestChain: longestChain);
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        if (sortedValues.Length == 1) return sortedValues[0];

        var index = (percentile / 100.0) * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sortedValues[lower];

        var fraction = index - lower;
        return sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]);
    }

    private static string[][] FindCycles(HashSet<string> nodes, Dictionary<string, HashSet<string>> adj)
    {
        // Tarjan's SCC algorithm
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>();
        var indices = new Dictionary<string, int>();
        var lowLinks = new Dictionary<string, int>();
        var sccs = new List<string[]>();

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowLinks[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            if (adj.TryGetValue(v, out var neighbors))
            {
                foreach (var w in neighbors)
                {
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
                    }
                }
            }

            if (lowLinks[v] == indices[v])
            {
                var scc = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    scc.Add(w);
                } while (w != v);

                if (scc.Count > 1)
                    sccs.Add(scc.ToArray());
            }
        }

        foreach (var node in nodes)
        {
            if (!indices.ContainsKey(node))
                StrongConnect(node);
        }

        return sccs.ToArray();
    }

    private static (int Length, string[] Chain) FindLongestPath(
        HashSet<string> nodes, Dictionary<string, HashSet<string>> adj)
    {
        var memo = new Dictionary<string, (int Length, List<string> Path)>();
        var visiting = new HashSet<string>(); // cycle guard

        (int Length, List<string> Path) Dfs(string node)
        {
            if (memo.TryGetValue(node, out var cached)) return cached;
            if (visiting.Contains(node)) return (0, [node]); // cycle — stop

            visiting.Add(node);
            var best = (Length: 0, Path: new List<string> { node });

            if (adj.TryGetValue(node, out var neighbors))
            {
                foreach (var next in neighbors)
                {
                    var (subLen, subPath) = Dfs(next);
                    if (subLen + 1 > best.Length)
                    {
                        best = (subLen + 1, new List<string> { node });
                        best.Path.AddRange(subPath);
                    }
                }
            }

            visiting.Remove(node);
            memo[node] = best;
            return best;
        }

        var longest = (Length: 0, Path: new List<string>());
        foreach (var node in nodes)
        {
            var (len, path) = Dfs(node);
            if (len > longest.Length)
                longest = (len, path);
        }

        return (longest.Length, longest.Path.ToArray());
    }

    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    internal static string NormalizePathGuids(string path) =>
        GuidPattern.Replace(path, "{guid}");

    internal static string SanitizeKey(string name) =>
        name.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
}
