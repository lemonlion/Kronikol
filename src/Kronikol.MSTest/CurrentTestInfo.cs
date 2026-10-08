namespace Kronikol.MSTest;

/// <summary>
/// Provides a standard way to obtain the current test's name and ID for MSTest.
/// </summary>
public static class CurrentTestInfo
{
    /// <summary>
    /// A delegate that returns the current MSTest test's name and fully-qualified ID.
    /// Assign to <c>CurrentTestInfoFetcher</c> on any tracking options class.
    /// </summary>
    public static Func<(string Name, string Id)> Fetcher { get; } =
        () =>
        {
            var ctx = DiagrammedComponentTest.GetCurrentTestContext()
                ?? throw new InvalidOperationException("Test context not available on this thread.");
            return (ctx.TestName!, DiagrammedComponentTest.GetCurrentTestId() ?? $"{ctx.FullyQualifiedTestClassName}.{ctx.TestName}");
        };
}
