namespace Kronikol.xUnit2;

/// <summary>
/// Provides a standard way to obtain the current test's name and ID for xUnit v2.
/// </summary>
public static class CurrentTestInfo
{
    /// <summary>
    /// A delegate that returns the current xUnit v2 test's display name and unique ID.
    /// Throws <see cref="InvalidOperationException"/> when no test is running on this flow: in a test class's constructor,
    /// <c>IAsyncLifetime.InitializeAsync</c>, <c>DisposeAsync</c> or <c>Dispose</c>, in a fixture, or on a thread the test's
    /// execution context did not reach. The resolver then goes on to a <c>TestIdentityScope</c>, the global fallback or
    /// the background.
    /// Assign to <c>CurrentTestInfoFetcher</c> on any tracking options class.
    /// </summary>
    public static Func<(string Name, string Id)> Fetcher { get; } =
        () => XUnit2TestTrackingContext.Current
              ?? throw new InvalidOperationException("Test context not available on this thread.");
}
