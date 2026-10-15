using System.Net;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Reports;
using Kronikol.Tracking;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// A <see cref="ProxyTapOptions.Log"/> that throws changes nothing the tap does (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6
/// item 6). One bound to xUnit's test output throws once its test has ended; unguarded, the throw made <c>StartAsync</c>
/// fail with the listener already open, kept a 502 from its caller, counted a capture fault twice, and failed
/// <c>DisposeAsync</c>.
/// </summary>
public class ProxyTapLogTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Each case, and the words of the one line whose writing throws in it: only that site is under test.
    public static TheoryData<string, string> Lines => new()
    {
        { "the start line", "proxy-tap listening on" },
        { "a sink that throws", "proxy-tap: the sink threw" },
        { "a fault the tap did not expect", "proxy-tap error" },
        { "a forward that failed", "proxy-tap error" },
        { "a test id that is not a trace id", "TraceIdFromIdentity did not apply" },
        { "a forward cut short by stopping", "proxy-tap stopping" },
        { "the stop line", "proxy-tap stopped (" },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public async Task A_throwing_log_callback_changes_nothing(string line, string throwsOn)
    {
        var written = new List<string>();
        using var refusing = LoopbackPort.Closed();
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        using var held = new HeldUpstream();
        var forward = line switch
        {
            "a forward that failed" => new Uri($"http://127.0.0.1:{((IPEndPoint)refusing.LocalEndPoint!).Port}"),
            "a forward cut short by stopping" => held.BaseUri,
            _ => upstream.BaseUri,
        };
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(),
            ForwardBaseUri = forward,
            CallerName = "web",
            ServiceName = "api",
            Sink = line == "a sink that throws" ? new ThrowingSink() : new ListSink(),
            CaptureUnattributedRequests = true,
            FallbackTestId = "throwing-log",
            TraceIdFromIdentity = line == "a test id that is not a trace id",
            Log = text =>
            {
                lock (written) written.Add(text);
                if (text.Contains(throwsOn, StringComparison.Ordinal))
                    throw new InvalidOperationException("There is no currently active test.");
            },
        };
        var tap = new Tap(options);
        if (line == "a fault the tap did not expect")
            tap.BeforeForward = () => throw new InvalidOperationException("a fault the tap did not expect");

        await tap.StartAsync(); // "the start line": it threw here, with the listener already open
        try
        {
            Assert.True(tap.IsListening);
            switch (line)
            {
                case "a sink that throws":
                {
                    Assert.Equal(HttpStatusCode.OK, await GetAsync(tap));
                    Assert.True(await WaitUntilAsync(() => tap.InFlight == 0), "the exchange never ended");
                    Assert.Equal(1, tap.CaptureFaults); // counted once, not again as a fault of the tap's own
                    break;
                }
                case "a fault the tap did not expect":
                case "a forward that failed":
                {
                    using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /x HTTP/1.1\r\nHost: localhost\r\n\r\n");
                    var heard = await caller.HearAsync(Patience);
                    Assert.StartsWith("HTTP/1.1 502 ", heard.FirstLine);
                    Assert.Contains("\"code\":502", Encoding.UTF8.GetString(heard.Body), StringComparison.Ordinal);
                    Assert.Equal(1, tap.ForwardFailures);
                    break;
                }
                case "a test id that is not a trace id":
                {
                    using var client = new HttpClient();
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(tap.ListenUri, "/x"));
                    request.Headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, "not-a-trace-id");
                    using var response = await client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Contains(tap.Diagnostics(), d => d.Kind == DiagnosticKind.OptionNotApplied);
                    break;
                }
                case "a forward cut short by stopping":
                {
                    using var caller = await RawCaller.SendAsync(tap.Options.ListenPort, "GET /hold HTTP/1.1\r\nHost: localhost\r\n\r\n");
                    await held.HasRequest.Task.WaitAsync(Patience);
                    await tap.DisposeAsync();
                    var heard = await caller.HearAsync(Patience);
                    Assert.StartsWith("HTTP/1.1 502 ", heard.FirstLine);
                    Assert.Equal(0, tap.ForwardFailures);
                    break;
                }
            }
        }
        finally
        {
            // "the stop line": DisposeAsync threw.
            await tap.DisposeAsync();
        }

        lock (written)
            Assert.Contains(written, text => text.Contains(throwsOn, StringComparison.Ordinal)); // the site was reached
    }

    private static async Task<HttpStatusCode> GetAsync(Tap tap)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(tap.ListenUri, "/x"));
        return response.StatusCode;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(20);
        }

        return true;
    }

    private sealed class ThrowingSink : IRequestResponseSink
    {
        public void Log(RequestResponseLog log) => throw new IOException("the sink cannot write");
    }
}
