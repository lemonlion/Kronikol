using System.Collections.Concurrent;

namespace Kronikol.Reports;

/// <summary>
/// Capture-health entries a tracking component records while it is set up, long before any report is generated
/// (Kafka build interception noticing that tiered compilation can drop its patch). Every report this process generates
/// adds them to its diagnostics, once each.
/// </summary>
internal static class ProcessDiagnostics
{
    private static readonly ConcurrentDictionary<string, DiagnosticEntry> ByKey = new(StringComparer.Ordinal);

    /// <summary>Records an entry under <paramref name="key"/> unless that key already holds one.</summary>
    internal static bool RecordOnce(string key, DiagnosticKind kind, string message) =>
        ByKey.TryAdd(key, new DiagnosticEntry(kind, message));

    /// <summary>The entries recorded so far, ordered by key, so every report lists them alike.</summary>
    internal static IReadOnlyList<DiagnosticEntry> Entries =>
        ByKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();

    /// <summary>Removes the entry under <paramref name="key"/>; for tests, which share the process with every report they generate.</summary>
    internal static bool Remove(string key) => ByKey.TryRemove(key, out _);

    /// <summary>Adds to <paramref name="collector"/> every entry it does not hold yet.</summary>
    internal static void AddTo(ReportDiagnosticsCollector collector)
    {
        var held = collector.Entries;
        foreach (var entry in Entries)
            if (!held.Contains(entry))
                collector.Add(entry);
    }
}
