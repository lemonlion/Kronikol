using Microsoft.AspNetCore.Http;
using Kronikol.Constants;

namespace Kronikol.Tracking;

/// <summary>A resolved test identity and how it was resolved.</summary>
/// <param name="Name">The test display name.</param>
/// <param name="Id">The test unique id.</param>
/// <param name="Source">Which level of the resolution answered.</param>
public readonly record struct TestIdentity(string Name, string Id, AttributionSource Source)
{
    /// <summary>
    /// False for the background identity: nothing named a scenario (<see cref="AttributionSource.None"/>),
    /// or the flow is detached (<see cref="AttributionSource.Detached"/>).
    /// </summary>
    public bool IsAttributed => Source is not (AttributionSource.None or AttributionSource.Detached);
}

/// <summary>
/// Resolves test identity using a four-level resolution strategy:
/// <list type="number">
///   <item>HTTP context request headers (for code running inside the SUT's request pipeline)</item>
///   <item>Delegate fallback (for code running on the test thread)</item>
///   <item><see cref="TestIdentityScope.Current"/> (for background threads with an explicit scope)</item>
///   <item><see cref="TestIdentityScope.GlobalFallback"/> (for pre-existing threads that can't inherit AsyncLocal)</item>
/// </list>
/// A detached flow (<see cref="TestIdentityScope.Detach"/>) skips the delegate and the global fallback:
/// a host started inside a test would otherwise carry that test into every background call it makes.
/// <see cref="ResolveWithSource(IHttpContextAccessor?, Func{ValueTuple{string, string}}?)"/> says which
/// level answered, and that mark travels with the call so the report can tell a scenario's own work from
/// work that merely inherited its context.
/// </summary>
public static class TestInfoResolver
{
    /// <summary>
    /// Attempts to resolve the current test name and ID.
    /// First checks HTTP request headers propagated by <see cref="TestTrackingMessageHandler"/>,
    /// then falls back to the delegate (e.g. from a test framework's execution context),
    /// then falls back to <see cref="TestIdentityScope.Current"/>,
    /// then falls back to <see cref="TestIdentityScope.GlobalFallback"/>.
    /// Null when nothing resolved — unless <see cref="RequestResponseLogger.CaptureBackground"/> is on,
    /// when the background identity (<see cref="TestIdentityScope.UnknownIdentity"/>) comes back instead.
    /// </summary>
    public static (string Name, string Id)? Resolve(
        IHttpContextAccessor? httpContextAccessor,
        Func<(string Name, string Id)>? currentTestInfoFetcher)
    {
        var who = ResolveWithSource(httpContextAccessor, currentTestInfoFetcher);
        return who is { } identity ? (identity.Name, identity.Id) : null;
    }

    /// <summary>
    /// Overload for delegates that return a nullable tuple (e.g. Dapper extension).
    /// </summary>
    public static (string Name, string Id)? Resolve(
        IHttpContextAccessor? httpContextAccessor,
        Func<(string Name, string Id)?>? currentTestInfoFetcher)
    {
        var who = ResolveWithSource(httpContextAccessor, currentTestInfoFetcher);
        return who is { } identity ? (identity.Name, identity.Id) : null;
    }

    /// <summary>
    /// <see cref="Resolve(IHttpContextAccessor?, Func{ValueTuple{string, string}}?)"/>, and which level answered.
    /// </summary>
    public static TestIdentity? ResolveWithSource(
        IHttpContextAccessor? httpContextAccessor,
        Func<(string Name, string Id)>? currentTestInfoFetcher)
    {
        Func<(string Name, string Id)?>? nullable = currentTestInfoFetcher is null ? null : () => currentTestInfoFetcher();
        return ResolveWithSource(httpContextAccessor, nullable);
    }

    /// <summary>
    /// <see cref="Resolve(IHttpContextAccessor?, Func{ValueTuple{string, string}?}?)"/>, and which level answered.
    /// Null when nothing resolved and <see cref="RequestResponseLogger.CaptureBackground"/> is off; the
    /// background identity with <see cref="AttributionSource.None"/> or <see cref="AttributionSource.Detached"/>
    /// when it is on.
    /// </summary>
    public static TestIdentity? ResolveWithSource(
        IHttpContextAccessor? httpContextAccessor,
        Func<(string Name, string Id)?>? currentTestInfoFetcher)
    {
        if (TryResolveFromHttpContext(httpContextAccessor, out var fromHeaders))
            return new TestIdentity(fromHeaders.Name, fromHeaders.Id, AttributionSource.RequestHeader);

        // A detached flow is nobody's scenario unless a scope says otherwise: the framework context it
        // inherited is exactly what it must not use.
        if (TestIdentityScope.IsDetached)
        {
            return TestIdentityScope.Current is { } scoped
                ? new TestIdentity(scoped.Name, scoped.Id, AttributionSource.Scope)
                : Unattributed(AttributionSource.Detached);
        }

        try
        {
            var delegateResult = currentTestInfoFetcher?.Invoke();
            if (delegateResult is not null && !IsUnknownIdentity(delegateResult.Value))
                return new TestIdentity(delegateResult.Value.Name, delegateResult.Value.Id, AttributionSource.TestContext);
        }
        catch
        {
            // Delegate threw — fall through to scope
        }

        if (TestIdentityScope.Current is { } current)
            return new TestIdentity(current.Name, current.Id, AttributionSource.Scope);

        if (TestIdentityScope.GlobalFallback is { } fallback)
            return new TestIdentity(fallback.Name, fallback.Id, AttributionSource.GlobalFallback);

        return Unattributed(AttributionSource.None);
    }

    private static TestIdentity? Unattributed(AttributionSource source) =>
        RequestResponseLogger.CaptureBackground
            ? new TestIdentity(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, source)
            : null;

    /// <summary>
    /// Creates a <c>Func&lt;(string Name, string Id)&gt;</c> that tries to resolve test identity
    /// from HTTP request headers first, falling back to the provided delegate.
    /// <para>
    /// Use this to eliminate the repetitive httpContext+fallback boilerplate when setting
    /// <c>CurrentTestInfoFetcher</c> on tracking options classes.
    /// </para>
    /// </summary>
    /// <param name="httpContextAccessor">
    /// Optional accessor for the current HTTP context. When available, the returned delegate
    /// reads <see cref="TestTrackingHttpHeaders.CurrentTestNameHeader"/> and
    /// <see cref="TestTrackingHttpHeaders.CurrentTestIdHeader"/> from request headers.
    /// </param>
    /// <param name="fallback">
    /// Delegate invoked when the HTTP context is unavailable or headers are missing
    /// (e.g. test framework context like <c>TestContext.Current</c>).
    /// </param>
    public static Func<(string Name, string Id)> CreateHttpFallbackFetcher(
        IHttpContextAccessor? httpContextAccessor,
        Func<(string Name, string Id)> fallback)
    {
        return () =>
        {
            if (TryResolveFromHttpContext(httpContextAccessor, out var result))
                return result;

            return fallback();
        };
    }

    private static bool TryResolveFromHttpContext(
        IHttpContextAccessor? httpContextAccessor,
        out (string Name, string Id) result)
    {
        result = default;

        try
        {
            var httpContext = httpContextAccessor?.HttpContext;
            if (httpContext is not null &&
                httpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestNameHeader, out var testName) &&
                httpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestIdHeader, out var testId) &&
                testName.Count > 0 && testId.Count > 0)
            {
                result = (testName[0]!, testId[0]!);
                return true;
            }
        }
        catch
        {
            // HttpContext access can fail in edge cases — fall through to delegate
        }

        return false;
    }

    private static bool IsUnknownIdentity((string Name, string Id) identity) =>
        string.Equals(identity.Id, TestIdentityScope.UnknownTestId, StringComparison.OrdinalIgnoreCase);
}
