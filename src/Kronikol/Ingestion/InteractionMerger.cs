namespace Kronikol.Ingestion;

/// <summary>
/// Folds the two views of one call into a single arrow. When a stack is captured from both sides — a
/// wire tap that sees the protocol (payloads, hit/miss, status) but has to guess which test a call
/// belongs to, and an OTLP span tap that carries the exact trace id but usually no payload — the same
/// database or HTTP call arrives twice. This merges those duplicates at ingest, keeping the best half of
/// each: the span's identity and the wire's fidelity.
/// </summary>
/// <remarks>
/// <para>Two calls are the same call when they agree on caller, service, the <em>verb</em> of the method
/// label (the first word, so <c>Get (Hit)</c> and <c>GET</c> match, and <c>Find ← Trial</c> matches
/// <c>Find</c>) and the last path segment of the URI (the Redis key or the Mongo collection), and their
/// <c>[start, end]</c> intervals overlap by at least <c>threshold</c> of the <em>shorter</em> interval.
/// Matching is greedy by best overlap and strictly one-to-one, so a burst of N nearly identical calls
/// pairs off N times rather than collapsing.</para>
/// <para>Which side a record came from is read from <see cref="InteractionRecord.CapturedBy"/> when the
/// capturer set it, and inferred otherwise: a record with a span id and no content is span-like, one with
/// content and no span id is wire-like. Records that are neither (or that carry no timestamp) are never
/// merged; nothing is ever dropped without a twin.</para>
/// <para>The merged pair keeps the wire record's position, content, status and label, takes the span
/// record's <c>testId</c>/<c>traceId</c>/<c>activityTraceId</c>/<c>activitySpanId</c>, and records the
/// fact on the request as the pseudo-header <c>x-kronikol-captured-by: wire + span</c> (and in
/// <see cref="InteractionRecord.CapturedBy"/>), so the diagram note says where the arrow came from.</para>
/// </remarks>
public static class InteractionMerger
{
    /// <summary><see cref="InteractionRecord.CapturedBy"/> value for a capturer that decoded the wire protocol (a proxy or TCP tap).</summary>
    public const string WireSource = "wire";

    /// <summary><see cref="InteractionRecord.CapturedBy"/> value for a capturer that read OpenTelemetry spans.</summary>
    public const string SpanSource = "span";

    /// <summary><see cref="InteractionRecord.CapturedBy"/> value of a merged record.</summary>
    public const string MergedSource = "wire + span";

    /// <summary>The pseudo-header added to a merged request so the diagram note states both capture paths.</summary>
    public const string CapturedByHeader = "x-kronikol-captured-by";

    /// <summary>The default fraction of the shorter interval two calls must share to be the same call.</summary>
    public const double DefaultOverlapThreshold = 0.8;

    /// <summary>
    /// Returns <paramref name="records"/> with wire/span duplicates folded together. Input order is
    /// preserved (a merged pair sits where the wire record was); the span records of merged pairs are
    /// removed. Pure — the input list is not modified.
    /// </summary>
    /// <param name="records">The records to merge, in any order.</param>
    /// <param name="overlapThreshold">Fraction (0–1] of the shorter interval the two calls must share. Default 0.8.</param>
    public static List<InteractionRecord> Merge(IReadOnlyList<InteractionRecord> records, double overlapThreshold = DefaultOverlapThreshold)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            return [];

        var threshold = overlapThreshold is > 0 and <= 1 ? overlapThreshold : DefaultOverlapThreshold;
        var calls = CollectCalls(records);
        if (calls.Count < 2)
            return [.. records];

        var pairs = MatchPairs(calls, threshold);
        if (pairs.Count == 0)
            return [.. records];

        var replacements = new Dictionary<int, InteractionRecord>();
        var removed = new HashSet<int>();
        foreach (var (wire, span) in pairs)
        {
            replacements[wire.RequestIndex] = MergeRequest(wire.Request, span.Request);
            removed.Add(span.RequestIndex);

            if (span.ResponseIndex >= 0)
                removed.Add(span.ResponseIndex);
            if (wire.ResponseIndex >= 0)
                replacements[wire.ResponseIndex] = MergeResponse(wire.Response!, span.Response ?? span.Request);
        }

        var result = new List<InteractionRecord>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            if (removed.Contains(i))
                continue;
            result.Add(replacements.TryGetValue(i, out var merged) ? merged : records[i]);
        }

        return result;
    }

    // ------------------------------------------------------------------ candidates

    private static List<Call> CollectCalls(IReadOnlyList<InteractionRecord> records)
    {
        var byPairId = new Dictionary<string, (int RequestIndex, int ResponseIndex)>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            if (record.IsMarker || record.IsUserAction || string.IsNullOrEmpty(record.RequestResponseId))
                continue;

            var key = record.RequestResponseId!;
            if (!byPairId.TryGetValue(key, out var slot))
                slot = (-1, -1);

            if (string.Equals(record.Type, "Response", StringComparison.OrdinalIgnoreCase))
            {
                if (slot.ResponseIndex < 0) slot.ResponseIndex = i;
            }
            else if (slot.RequestIndex < 0)
            {
                slot.RequestIndex = i;
            }

            byPairId[key] = slot;
        }

        var calls = new List<Call>();
        foreach (var (_, slot) in byPairId)
        {
            if (slot.RequestIndex < 0)
                continue;
            var request = records[slot.RequestIndex];
            var response = slot.ResponseIndex >= 0 ? records[slot.ResponseIndex] : null;
            if (request.Timestamp is not { } start)
                continue;

            var source = Classify(request, response);
            if (source == Source.Unknown)
                continue;

            var end = response?.Timestamp ?? start;
            if (end < start)
                end = start;
            if (response?.Timestamp is null && request.DurationMs is { } duration and > 0)
                end = start.AddMilliseconds(duration);

            calls.Add(new Call(source, slot.RequestIndex, slot.ResponseIndex, request, response, start, end, KeyOf(request)));
        }

        return calls;
    }

    private enum Source
    {
        Unknown,
        Wire,
        Span,
    }

    private static Source Classify(InteractionRecord request, InteractionRecord? response)
    {
        var declared = request.CapturedBy ?? response?.CapturedBy;
        if (!string.IsNullOrWhiteSpace(declared))
        {
            if (declared.Equals(SpanSource, StringComparison.OrdinalIgnoreCase))
                return Source.Span;
            if (declared.Equals(WireSource, StringComparison.OrdinalIgnoreCase))
                return Source.Wire;
            return Source.Unknown; // already merged, or a capturer we do not know how to reconcile
        }

        var hasSpanId = !string.IsNullOrWhiteSpace(request.ActivitySpanId) || !string.IsNullOrWhiteSpace(response?.ActivitySpanId);
        var hasContent = !string.IsNullOrWhiteSpace(request.Content) || !string.IsNullOrWhiteSpace(response?.Content);

        if (hasSpanId && !hasContent)
            return Source.Span;
        if (hasContent && !hasSpanId)
            return Source.Wire;
        return Source.Unknown;
    }

    private static string KeyOf(InteractionRecord request) =>
        string.Join('|',
            request.CallerName.ToLowerInvariant(),
            request.ServiceName.ToLowerInvariant(),
            Verb(request.Method),
            LastSegment(request.Uri));

    /// <summary>The first word of a method label, lowercased: <c>Get (Hit)</c> → <c>get</c>, <c>Find ← Trial</c> → <c>find</c>.</summary>
    internal static string Verb(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return "";
        var trimmed = method.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '(', ':']);
        return (end <= 0 ? trimmed : trimmed[..end]).ToLowerInvariant();
    }

    /// <summary>The last non-empty path segment of a URI (the Redis key, the Mongo collection, the last route segment), lowercased.</summary>
    internal static string LastSegment(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return "";
        var path = uri;
        var query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];
        var scheme = path.IndexOf("//", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var slash = path.IndexOf('/', scheme + 2);
            path = slash < 0 ? "" : path[slash..];
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "" : segments[^1].ToLowerInvariant();
    }

    // ------------------------------------------------------------------ matching

    private static List<(Call Wire, Call Span)> MatchPairs(List<Call> calls, double threshold)
    {
        var candidates = new List<(double Overlap, double Distance, Call Wire, Call Span)>();
        var wires = calls.Where(c => c.Source == Source.Wire).ToList();
        var spans = calls.Where(c => c.Source == Source.Span).ToList();
        if (wires.Count == 0 || spans.Count == 0)
            return [];

        foreach (var span in spans)
        {
            foreach (var wire in wires)
            {
                if (!string.Equals(span.Key, wire.Key, StringComparison.Ordinal))
                    continue;
                var overlap = OverlapRatio(wire.Start, wire.End, span.Start, span.End);
                if (overlap < threshold)
                    continue;
                candidates.Add((overlap, Math.Abs((wire.Start - span.Start).TotalMilliseconds), wire, span));
            }
        }

        var ordered = candidates
            .OrderByDescending(c => c.Overlap)
            .ThenBy(c => c.Distance)
            .ThenBy(c => c.Wire.RequestIndex)
            .ThenBy(c => c.Span.RequestIndex);

        var usedWires = new HashSet<int>();
        var usedSpans = new HashSet<int>();
        var pairs = new List<(Call Wire, Call Span)>();
        foreach (var candidate in ordered)
        {
            if (!usedWires.Add(candidate.Wire.RequestIndex))
                continue;
            if (!usedSpans.Add(candidate.Span.RequestIndex))
            {
                usedWires.Remove(candidate.Wire.RequestIndex);
                continue;
            }

            pairs.Add((candidate.Wire, candidate.Span));
        }

        return pairs;
    }

    /// <summary>How much of the shorter of two intervals the two share, 0–1. Two instants at the same point count as a full overlap.</summary>
    internal static double OverlapRatio(DateTimeOffset startA, DateTimeOffset endA, DateTimeOffset startB, DateTimeOffset endB)
    {
        var start = startA > startB ? startA : startB;
        var end = endA < endB ? endA : endB;
        if (end < start)
            return 0;

        var overlap = (end - start).TotalMilliseconds;
        var shorter = Math.Min((endA - startA).TotalMilliseconds, (endB - startB).TotalMilliseconds);
        if (shorter <= 0)
            return 1; // a zero-length interval that falls inside the other one
        return Math.Min(1, overlap / shorter);
    }

    // ------------------------------------------------------------------ merging

    private static InteractionRecord MergeRequest(InteractionRecord wire, InteractionRecord span) =>
        Adopt(wire, span) with
        {
            Headers = WithCapturedByHeader(wire.Headers),
        };

    private static InteractionRecord MergeResponse(InteractionRecord wire, InteractionRecord span) => Adopt(wire, span);

    /// <summary>The wire record with the span's identity: exact attribution, wire fidelity.</summary>
    private static InteractionRecord Adopt(InteractionRecord wire, InteractionRecord span) => wire with
    {
        TestId = span.TestId,
        TestName = span.TestName ?? wire.TestName,
        TraceId = span.TraceId ?? wire.TraceId,
        ActivityTraceId = span.ActivityTraceId ?? wire.ActivityTraceId,
        ActivitySpanId = span.ActivitySpanId ?? wire.ActivitySpanId,
        CapturedBy = MergedSource,
    };

    private static InteractionHeader[] WithCapturedByHeader(InteractionHeader[]? headers)
    {
        var existing = headers ?? [];
        var kept = existing.Where(h => !string.Equals(h.Key, CapturedByHeader, StringComparison.OrdinalIgnoreCase));
        return [.. kept, new InteractionHeader(CapturedByHeader, MergedSource)];
    }

    private sealed record Call(
        Source Source,
        int RequestIndex,
        int ResponseIndex,
        InteractionRecord Request,
        InteractionRecord? Response,
        DateTimeOffset Start,
        DateTimeOffset End,
        string Key);
}
