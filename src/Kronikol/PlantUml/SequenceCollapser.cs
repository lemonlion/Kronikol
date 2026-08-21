using Kronikol.Tracking;

namespace Kronikol.PlantUml;

/// <summary>
/// Pre-pass over one test's ordered log entries that (a) collapses maximal runs of consecutive
/// identical request/response pairs into a single pair annotated with <see cref="RequestResponseLog.CollapsedCount"/>
/// and (b) applies an arrow cap, reporting how many pairs were omitted. Implements
/// <c>ReportConfigurationOptions.CollapseConsecutiveIdenticalCalls</c> / <c>MaxArrowsPerDiagram</c>.
/// </summary>
/// <remarks>
/// Two pairs are "identical" when caller, service, method, path+query, GraphQL operation label
/// (when the body is a GraphQL request), status code and meta-type all match. Only a Request entry
/// immediately followed by its own Response entry (same <see cref="RequestResponseLog.RequestResponseId"/>)
/// forms a collapsible unit; override/action markers and unpaired entries break runs and pass through
/// untouched, so diagram customisations keep their position.
/// </remarks>
public static class SequenceCollapser
{
    /// <summary>Result of <see cref="Apply"/>.</summary>
    /// <param name="Traces">The entries to render, in order.</param>
    /// <param name="OmittedPairs">How many request/response pairs were dropped by the arrow cap (0 when uncapped).</param>
    /// <param name="CollapsedRuns">How many runs were collapsed.</param>
    public sealed record Result(List<RequestResponseLog> Traces, int OmittedPairs, int CollapsedRuns);

    /// <summary>
    /// Applies collapsing (when <paramref name="collapse"/> is true and a run reaches <paramref name="threshold"/>)
    /// and the optional arrow cap (<paramref name="maxPairs"/> request/response pairs, counted after collapsing).
    /// Returns the input list unchanged (same instance) when nothing applies.
    /// </summary>
    public static Result Apply(List<RequestResponseLog> traces, bool collapse, int threshold, int? maxPairs)
    {
        if (traces.Count == 0 || (!collapse && maxPairs is null))
            return new Result(traces, 0, 0);

        if (threshold < 2)
            threshold = 2;

        var units = ToUnits(traces);
        var output = new List<RequestResponseLog>(traces.Count);
        var collapsedRuns = 0;
        var i = 0;
        while (i < units.Count)
        {
            var unit = units[i];
            if (!unit.IsPair || !collapse)
            {
                output.AddRange(unit.Entries);
                i++;
                continue;
            }

            var key = unit.Key!;
            var j = i + 1;
            while (j < units.Count && units[j].IsPair && units[j].Key == key)
                j++;

            var runLength = j - i;
            if (runLength >= threshold)
            {
                collapsedRuns++;
                var run = units.GetRange(i, runLength);
                var first = run[0];
                var request = first.Entries[0] with { };
                request.CollapsedCount = runLength;
                request.CollapsedSummary = Summarise(run);
                output.Add(request);
                output.Add(first.Entries[1]);
            }
            else
            {
                for (var k = i; k < j; k++)
                    output.AddRange(units[k].Entries);
            }

            i = j;
        }

        var omitted = 0;
        if (maxPairs is { } cap && cap >= 0)
        {
            var capped = new List<RequestResponseLog>(output.Count);
            var pairsSeen = 0;
            var pendingResponses = new HashSet<Guid>();
            foreach (var entry in output)
            {
                var isMarker = entry.IsOverrideStart || entry.IsOverrideEnd || entry.IsActionStart;
                if (isMarker)
                {
                    capped.Add(entry);
                    continue;
                }

                if (entry.Type == RequestResponseType.Request)
                {
                    if (pairsSeen >= cap)
                    {
                        omitted++;
                        continue;
                    }

                    pairsSeen++;
                    pendingResponses.Add(entry.RequestResponseId);
                    capped.Add(entry);
                }
                else
                {
                    // Keep a response only when its request survived.
                    if (pendingResponses.Remove(entry.RequestResponseId))
                        capped.Add(entry);
                    else if (pairsSeen < cap)
                        capped.Add(entry); // unpaired response before the cap — pass through
                }
            }

            output = capped;
        }

        if (collapsedRuns == 0 && omitted == 0)
            return new Result(traces, 0, 0);

        return new Result(output, omitted, collapsedRuns);
    }

    /// <summary>The identity used to decide whether two pairs are "the same call".</summary>
    public static string KeyFor(RequestResponseLog request, RequestResponseLog? response)
    {
        var graphQl = GraphQlOperationDetector.TryExtractLabel(request.Content);
        var status = response?.StatusCode?.Value?.ToString();
        return string.Join("",
            request.CallerName,
            request.ServiceName,
            request.Method.Value?.ToString(),
            request.Uri.PathAndQuery,
            graphQl,
            status,
            request.MetaType.ToString());
    }

    private static string? Summarise(List<Unit> run)
    {
        var durations = new List<double>();
        foreach (var unit in run)
        {
            var req = unit.Entries[0].Timestamp;
            var resp = unit.Entries[1].Timestamp;
            if (req.HasValue && resp.HasValue && resp.Value >= req.Value)
                durations.Add((resp.Value - req.Value).TotalMilliseconds);
        }

        if (durations.Count == 0)
            return null;

        var min = durations.Min();
        var max = durations.Max();
        return Math.Abs(min - max) < 0.5 ? $"{min:0} ms" : $"{min:0}–{max:0} ms";
    }

    private sealed record Unit(RequestResponseLog[] Entries, string? Key)
    {
        public bool IsPair => Key is not null;
    }

    private static List<Unit> ToUnits(List<RequestResponseLog> traces)
    {
        var units = new List<Unit>(traces.Count);
        for (var i = 0; i < traces.Count; i++)
        {
            var current = traces[i];
            var isMarker = current.IsOverrideStart || current.IsOverrideEnd || current.IsActionStart;
            if (!isMarker && current.Type == RequestResponseType.Request && i + 1 < traces.Count)
            {
                var next = traces[i + 1];
                var nextIsMarker = next.IsOverrideStart || next.IsOverrideEnd || next.IsActionStart;
                if (!nextIsMarker && next.Type == RequestResponseType.Response && next.RequestResponseId == current.RequestResponseId)
                {
                    units.Add(new Unit([current, next], KeyFor(current, next)));
                    i++;
                    continue;
                }
            }

            units.Add(new Unit([current], null));
        }

        return units;
    }
}
