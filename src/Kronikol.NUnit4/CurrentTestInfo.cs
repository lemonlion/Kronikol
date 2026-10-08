namespace Kronikol.NUnit4;

/// <summary>
/// Provides a standard way to obtain the current test's name and ID for NUnit 4.
/// </summary>
public static class CurrentTestInfo
{
    /// <summary>
    /// A delegate that returns the current NUnit test's display name and ID.
    /// Throws <see cref="InvalidOperationException"/> when no test is running on this flow: in a set-up fixture, in a
    /// fixture's constructor or one-time set-up or tear-down, or on a thread the test's execution context did not reach.
    /// The resolver then goes on to a <c>TestIdentityScope</c>, the global fallback or the background.
    /// Assign to <c>CurrentTestInfoFetcher</c> on any tracking options class.
    /// </summary>
    public static Func<(string Name, string Id)> Fetcher { get; } =
        () =>
        {
            var test = RunningTest.Current
                ?? throw new InvalidOperationException("Test context not available on this thread.");
            return (test.DisplayName!, test.ID);
        };
}
