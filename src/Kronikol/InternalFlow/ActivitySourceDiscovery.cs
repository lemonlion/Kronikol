namespace Kronikol.InternalFlow;

/// <summary>
/// Discovers and counts Activity sources from spans already stored in
/// <see cref="InternalFlowSpanStore"/>. No separate listener needed.
/// </summary>
public static class ActivitySourceDiscovery
{
    /// <summary>
    /// Returns a dictionary of source name → span count from the current span store.
    /// </summary>
    public static Dictionary<string, int> GetDiscoveredSources()
    {
        var spans = InternalFlowSpanStore.GetSpans();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var span in spans)
        {
            // An activity made without a source is counted under the name its lane gets (SpanSource).
            var name = SpanSource.Of(span);
            if (string.IsNullOrEmpty(name)) continue;

            counts.TryGetValue(name, out var count);
            counts[name] = count + 1;
        }

        return counts;
    }
}
