using System.Collections.Concurrent;

namespace Kronikol.Tracking;

/// <summary>
/// Thread-safe static store that correlates work-item keys (e.g. document IDs, message keys)
/// to test identities, enabling parallel-safe background thread attribution.
/// <para>
/// Database extensions auto-populate this store at write time when <c>AutoCorrelateWrites</c> is enabled.
/// Background processing decorators call <see cref="Resolve"/> to establish
/// <see cref="TestIdentityScope"/> before invoking the real handler.
/// </para>
/// </summary>
public static class TestCorrelationStore
{
    private static readonly ConcurrentDictionary<string, CorrelationEntry> Correlations = new();

    private static TimeSpan _defaultTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Optional diagnostic callback invoked when <see cref="Resolve"/> fails to find a correlation.
    /// Useful for debugging "Unknown" entries in test reports.
    /// </summary>
    public static Action<string>? OnResolveMiss { get; set; }

    /// <summary>
    /// Gets or sets the default time-to-live for correlation entries.
    /// Expired entries are lazily evicted on access.
    /// Default: 30 minutes.
    /// </summary>
    public static TimeSpan DefaultTtl
    {
        get => _defaultTtl;
        set => _defaultTtl = value;
    }

    /// <summary>
    /// Stores a correlation between a work-item key and the test that produced it.
    /// If the key already exists, it is overwritten.
    /// </summary>
    /// <param name="key">The correlation key (e.g. "cosmos:Orders:doc-123").</param>
    /// <param name="testName">The test display name.</param>
    /// <param name="testId">The test unique identifier.</param>
    public static void Correlate(string key, string testName, string testId)
    {
        Correlations[key] = new CorrelationEntry(testName, testId, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Resolves the test identity associated with a work-item key.
    /// Returns <c>null</c> if the key is not found or has expired.
    /// </summary>
    /// <param name="key">The correlation key to look up.</param>
    /// <returns>The test identity tuple, or <c>null</c> if not found/expired.</returns>
    public static (string Name, string Id)? Resolve(string key)
    {
        if (!Correlations.TryGetValue(key, out var entry))
        {
            OnResolveMiss?.Invoke(key);
            return null;
        }

        if (DateTimeOffset.UtcNow - entry.CreatedAt > _defaultTtl)
        {
            Correlations.TryRemove(key, out _);
            OnResolveMiss?.Invoke(key);
            return null;
        }

        return (entry.Name, entry.Id);
    }

    /// <summary>
    /// <see cref="Resolve"/> as a probe: the identity a key maps to, or null, without reporting a miss to
    /// <see cref="OnResolveMiss"/>. For a lookup that expected nothing in particular, such as a document
    /// operation asking whether anyone wrote the document (<see cref="DocumentOwnership"/>).
    /// </summary>
    public static (string Name, string Id)? Lookup(string key)
    {
        if (!Correlations.TryGetValue(key, out var entry))
            return null;
        if (DateTimeOffset.UtcNow - entry.CreatedAt > _defaultTtl)
        {
            Correlations.TryRemove(key, out _);
            return null;
        }
        return (entry.Name, entry.Id);
    }

    /// <summary>
    /// Removes a correlation entry by key.
    /// </summary>
    /// <param name="key">The correlation key to remove.</param>
    /// <returns><c>true</c> if the entry was found and removed; otherwise <c>false</c>.</returns>
    public static bool Remove(string key) => Correlations.TryRemove(key, out _);

    /// <summary>
    /// Removes all correlation entries. Call in fixture teardown.
    /// </summary>
    public static void Clear() => Correlations.Clear();

    /// <summary>
    /// Seeds a correlation entry for pre-existing data (documents/messages that exist
    /// before the test runs). Functionally identical to <see cref="Correlate"/> but
    /// communicates intent — use this in test setup for data that was not written by
    /// the current test's tracked operations.
    /// </summary>
    /// <param name="key">The correlation key for the pre-existing item.</param>
    /// <param name="testName">The test display name that should own processing of this item.</param>
    /// <param name="testId">The test unique identifier.</param>
    public static void Seed(string key, string testName, string testId)
    {
        Correlations[key] = new CorrelationEntry(testName, testId, DateTimeOffset.UtcNow);
    }

    private sealed record CorrelationEntry(string Name, string Id, DateTimeOffset CreatedAt);
}
