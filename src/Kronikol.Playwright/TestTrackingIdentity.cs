using System.Diagnostics;
using System.Text;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Playwright;

/// <summary>
/// The identity a browser-driven test stamps on every request it makes: the four Kronikol
/// <see cref="TestTrackingHttpHeaders"/> (test name, test id, caller name, trace id) and, optionally, a W3C
/// <c>traceparent</c> whose trace id equals <see cref="TraceId"/> — so the test is the correlation root of
/// both the Kronikol diagram and any distributed trace the backend emits.
/// </summary>
/// <remarks>
/// Create one per test with <see cref="Create"/> (or <see cref="FromCurrentScope"/> inside a Kronikol-aware
/// test framework), then apply it with the <see cref="PlaywrightTestTrackingExtensions"/>. Header values are
/// ISO-8859-1-safe and bounded (<see cref="HeaderSafe"/>); the <em>id</em> is the correlation key and must be
/// used byte-for-byte as the report's <c>Scenario.Id</c>.
/// </remarks>
public sealed record TestTrackingIdentity
{
    /// <summary>Default caller participant name for browser traffic.</summary>
    public const string DefaultCallerName = "Browser";

    /// <summary>Display name of the test (cosmetic in diagrams).</summary>
    public required string TestName { get; init; }

    /// <summary>The correlation key — equals the scenario id in the report.</summary>
    public required string TestId { get; init; }

    /// <summary>The calling participant as it appears in diagrams. Default <see cref="DefaultCallerName"/>.</summary>
    public string CallerName { get; init; } = DefaultCallerName;

    /// <summary>
    /// Kronikol's per-chain trace id. Its 32-hex (<c>N</c>) form doubles as the W3C trace id placed in
    /// <c>traceparent</c>, so one value joins Kronikol's correlation and distributed tracing.
    /// </summary>
    public Guid TraceId { get; init; } = Guid.NewGuid();

    /// <summary>Whether <see cref="ToHeaders"/> also emits a <c>traceparent</c> (<c>00-{TraceId:N}-{span}-01</c>). Default true.</summary>
    public bool IncludeTraceparent { get; init; } = true;

    /// <summary>The W3C trace id (32 lowercase hex) derived from <see cref="TraceId"/>.</summary>
    public string W3CTraceId => TraceId.ToString("N");

    /// <summary>
    /// Mints an identity for a test. When <paramref name="testId"/> is omitted the trace id's 32-hex form is
    /// used as the test id — the recommended default for proxy-tap topologies, where downstream hops can
    /// only see the <c>traceparent</c> and attribute by its trace id.
    /// </summary>
    public static TestTrackingIdentity Create(string testName, string? testId = null, string callerName = DefaultCallerName, Guid? traceId = null)
    {
        var trace = traceId ?? Guid.NewGuid();
        return new TestTrackingIdentity
        {
            TestName = testName,
            TestId = testId ?? trace.ToString("N"),
            CallerName = callerName,
            TraceId = trace,
        };
    }

    /// <summary>Builds the identity from the ambient <see cref="TestIdentityScope"/> (set by the Kronikol test-framework adapters). Throws when no scope is active.</summary>
    public static TestTrackingIdentity FromCurrentScope(string callerName = DefaultCallerName)
    {
        var current = TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback
            ?? throw new InvalidOperationException("No Kronikol test identity scope is active. Use TestTrackingIdentity.Create(...) or run inside a Kronikol-enabled test framework.");
        return Create(current.Name, current.Id, callerName);
    }

    /// <summary>
    /// The headers to stamp on every browser request: the four <see cref="TestTrackingHttpHeaders"/> and
    /// (when <see cref="IncludeTraceparent"/>) a <c>traceparent</c>. Merge these into
    /// <c>BrowserNewContextOptions.ExtraHTTPHeaders</c> or pass to <c>SetExtraHTTPHeadersAsync</c>.
    /// </summary>
    public Dictionary<string, string> ToHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TestTrackingHttpHeaders.CurrentTestNameHeader] = HeaderSafe(TestName),
            [TestTrackingHttpHeaders.CurrentTestIdHeader] = HeaderSafe(TestId),
            [TestTrackingHttpHeaders.CallerNameHeader] = HeaderSafe(CallerName),
            [TestTrackingHttpHeaders.TraceIdHeader] = TraceId.ToString(),
        };

        if (IncludeTraceparent)
            headers["traceparent"] = Traceparent();

        return headers;
    }

    /// <summary>A sampled W3C <c>traceparent</c> rooted at <see cref="W3CTraceId"/> with a fresh parent span id.</summary>
    public string Traceparent() => $"00-{W3CTraceId}-{ActivitySpanId.CreateRandom()}-01";

    /// <summary>
    /// Opens the matching in-process <see cref="TestIdentityScope"/> so Kronikol handlers running in the test
    /// process (e.g. an API client used alongside the browser) attribute to the same test. Dispose to close.
    /// </summary>
    public IDisposable BeginScope() => TestIdentityScope.Begin(TestName, TestId);

    /// <summary>Makes a value safe for an HTTP header: printable ASCII only, at most 512 characters.</summary>
    public static string HeaderSafe(string value)
    {
        var sb = new StringBuilder(Math.Min(value.Length, 512));
        foreach (var c in value)
        {
            if (sb.Length >= 512) break;
            sb.Append(c is >= (char)0x20 and <= (char)0x7e ? c : '?');
        }

        return sb.ToString();
    }
}
