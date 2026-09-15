namespace Kronikol.Tracking;

/// <summary>
/// Ambient <see cref="AsyncLocal{T}"/>-based scope that carries test identity into
/// background threads, hosted services, change-feed subscribers, and other code paths
/// where neither <c>HttpContext</c> nor the test framework's <c>TestContext</c> is available.
/// <para>
/// Resolution order in <see cref="TestInfoResolver"/>:
/// <list type="number">
///   <item>HTTP request headers (propagated by <see cref="TestTrackingMessageHandler"/>)</item>
///   <item><c>CurrentTestInfoFetcher</c> delegate (test framework AsyncLocal)</item>
///   <item><see cref="TestIdentityScope.Current"/> (this class, AsyncLocal)</item>
///   <item><see cref="TestIdentityScope.GlobalFallback"/> (static, for pre-existing threads)</item>
///   <item><see cref="TestIdentityScope.OwnerWindow"/> (a detached flow working on a scenario's document)</item>
/// </list>
/// </para>
/// <example>
/// <code>
/// // Wrap background processing that is logically part of the test:
/// using (TestIdentityScope.Begin(testName, testId))
/// {
///     await backgroundService.ProcessAsync(); // tracking attributes to this test
/// }
///
/// // For pre-existing threads (Change Feed Processor, Hangfire, hosted services):
/// TestIdentityScope.SetGlobalFallback(testName, testId);
/// // ... run test ...
/// TestIdentityScope.ClearGlobalFallback();
/// </code>
/// </example>
/// </summary>
public static class TestIdentityScope
{
    /// <summary>
    /// The sentinel test name used when no test context is available (e.g. background threads, hosted services).
    /// </summary>
    public const string UnknownTestName = "Unknown";

    /// <summary>
    /// The sentinel test ID used when no test context is available (e.g. background threads, hosted services).
    /// </summary>
    public const string UnknownTestId = "unknown";

    /// <summary>
    /// Convenience tuple combining <see cref="UnknownTestName"/> and <see cref="UnknownTestId"/>.
    /// </summary>
    public static readonly (string Name, string Id) UnknownIdentity = (UnknownTestName, UnknownTestId);

    private static readonly AsyncLocal<(string Name, string Id)?> CurrentIdentity = new();
    // The flow's state is an object the slot refers to, not a value in the slot: a tracker deep inside a
    // database SDK's async call can then leave something (the owner window) for the flow that called it.
    private static readonly AsyncLocal<DetachedFlow?> Flow = new();

    /// <summary>
    /// Whether the current flow is detached from any test identity: inside <see cref="Detach"/>, or work
    /// started there. A detached flow resolves to no scenario unless a <see cref="Begin"/> scope names one.
    /// </summary>
    public static bool IsDetached => Flow.Value is not null;

    /// <summary>
    /// The owner window of the current detached flow: the scenario whose document the flow last wrote while
    /// it had no scenario of its own, kept until the flow's next operation on a document that is not that
    /// scenario's (<see cref="DocumentOwnership"/>). Null outside a detached flow, and while no window is open.
    /// </summary>
    public static (string Name, string Id)? OwnerWindow => Flow.Value?.Window;

    /// <summary>The current detached flow's state, or null when the flow is not detached.</summary>
    internal static DetachedFlow? CurrentFlow => Flow.Value;

    /// <summary>
    /// Detaches the current flow from the test identity it would otherwise inherit, until the returned
    /// scope is disposed. Work started inside — a host, its hosted services, a timer, a consumer loop —
    /// inherits the detachment, so a web host built inside a test no longer carries that test into every
    /// background call it makes for the rest of its life. An explicit <see cref="Begin"/> scope inside
    /// a detached flow still names its scenario: a consumer handling a correlated message is that
    /// scenario's work.
    /// </summary>
    public static IDisposable Detach()
    {
        var previous = Flow.Value;
        var previousIdentity = CurrentIdentity.Value;
        var previousFromMessage = FromMessage.Value;
        Flow.Value = new DetachedFlow();
        // The identity the flow inherited goes with the attachment: only a scope begun inside the detached
        // flow names a scenario there. Restored on dispose, so the caller's own flow keeps it.
        CurrentIdentity.Value = null;
        FromMessage.Value = false;
        return new DetachScope(previous, previousIdentity, previousFromMessage);
    }

    private sealed class DetachScope(DetachedFlow? previous, (string Name, string Id)? previousIdentity, bool previousFromMessage) : IDisposable
    {
        public void Dispose()
        {
            Flow.Value = previous;
            CurrentIdentity.Value = previousIdentity;
            FromMessage.Value = previousFromMessage;
        }
    }

    private static readonly object GlobalFallbackLock = new();
    private static (string Name, string Id)? _globalFallback;

    /// <summary>
    /// Gets the current test identity from the ambient scope, or <c>null</c> if no scope is active.
    /// </summary>
    public static (string Name, string Id)? Current => CurrentIdentity.Value;

    /// <summary>
    /// Gets the global fallback test identity for pre-existing background threads
    /// that cannot inherit <see cref="AsyncLocal{T}"/> values.
    /// <para>
    /// This is checked as the last resort in the resolution chain, after
    /// HTTP headers, delegate, and <see cref="Current"/>.
    /// </para>
    /// <para>
    /// <b>Warning:</b> This is a process-wide static field. It is designed for
    /// scenarios where tests run serially within a shared fixture (e.g. xUnit
    /// collection fixtures). It does not support parallel test execution where
    /// multiple tests set different fallback values simultaneously.
    /// </para>
    /// </summary>
    public static (string Name, string Id)? GlobalFallback
    {
        get { lock (GlobalFallbackLock) { return _globalFallback; } }
    }

    /// <summary>
    /// Sets the global fallback test identity for pre-existing background threads.
    /// Use this in test setup when background infrastructure (Change Feed Processor,
    /// Hangfire workers, hosted service loops) was started before
    /// <see cref="Begin"/> could propagate via <see cref="AsyncLocal{T}"/>.
    /// </summary>
    /// <param name="testName">The test display name.</param>
    /// <param name="testId">The test unique identifier.</param>
    public static void SetGlobalFallback(string testName, string testId)
    {
        lock (GlobalFallbackLock) { _globalFallback = (testName, testId); }
    }

    /// <summary>
    /// Clears the global fallback test identity. Call in test teardown.
    /// </summary>
    public static void ClearGlobalFallback()
    {
        lock (GlobalFallbackLock) { _globalFallback = null; }
    }

    /// <summary>
    /// Sets the ambient test identity for the current async context.
    /// Returns an <see cref="IDisposable"/> that restores the previous value on dispose.
    /// </summary>
    /// <param name="testName">The test display name.</param>
    /// <param name="testId">The test unique identifier.</param>
    public static IDisposable Begin(string testName, string testId)
    {
        var previous = CurrentIdentity.Value;
        var previousFromMessage = FromMessage.Value;
        CurrentIdentity.Value = (testName, testId);
        FromMessage.Value = false;
        return new IdentityScope(previous, previousFromMessage);
    }

    /// <summary>
    /// Sets the ambient test identity from an incoming message header.
    /// Unlike <see cref="Begin"/>, this does not return a disposable — the identity
    /// remains set until overwritten by the next message or explicitly cleared.
    /// Use this in message consumers where processing continues after <c>Consume()</c> returns.
    /// </summary>
    /// <param name="testName">The test display name extracted from message headers.</param>
    /// <param name="testId">The test unique identifier extracted from message headers.</param>
    public static void SetFromMessage(string testName, string testId)
    {
        CurrentIdentity.Value = (testName, testId);
        FromMessage.Value = true;
    }

    /// <summary>
    /// Clears an identity that <see cref="SetFromMessage"/> established and leaves one that
    /// <see cref="Begin"/> established alone. A consumer calls it before it looks at the next message,
    /// so a message that carries no identity is not processed under the previous message's, while a
    /// test that scoped itself around the consumer keeps its own.
    /// </summary>
    public static void ClearMessageIdentity()
    {
        if (!FromMessage.Value)
            return;
        CurrentIdentity.Value = null;
        FromMessage.Value = false;
    }

    /// <summary>
    /// Clears the ambient test identity for the current async context.
    /// </summary>
    public static void Reset()
    {
        CurrentIdentity.Value = null;
        FromMessage.Value = false;
    }

    /// <summary>True while <see cref="Current"/> was set by <see cref="SetFromMessage"/>.</summary>
    private static readonly AsyncLocal<bool> FromMessage = new();

    private sealed class IdentityScope((string Name, string Id)? previous, bool previousFromMessage) : IDisposable
    {
        public void Dispose()
        {
            CurrentIdentity.Value = previous;
            FromMessage.Value = previousFromMessage;
        }
    }
}
