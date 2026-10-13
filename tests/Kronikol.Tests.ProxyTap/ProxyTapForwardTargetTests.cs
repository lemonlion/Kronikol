using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Tracking;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// Where a request goes: to <see cref="ProxyTapOptions.ForwardBaseUri"/>'s scheme and authority with the request's own
/// path and query, whatever its target says (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md R0).
/// </summary>
public class ProxyTapForwardTargetTests
{
    private static Func<HttpListenerRequest, byte[], (int, byte[], string?, string?, Dictionary<string, string>?)> Answer(string name) =>
        (_, _) => (200, Encoding.UTF8.GetBytes(name), "text/plain", null, null);

    private static async Task<(Kronikol.Extensions.ProxyTap.ProxyTap Tap, ListSink Sink)> StartAsync(StubUpstream configured)
    {
        var sink = new ListSink();
        var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = configured.BaseUri, CallerName = "web", ServiceName = "api", Sink = sink,
        });
        await tap.StartAsync();
        return (tap, sink);
    }

    /// <summary>
    /// Sends one request with its target exactly as written, which no <see cref="HttpClient"/> would do for a target
    /// holding a backslash, and returns the status and body the caller got.
    /// </summary>
    private static async Task<(int Status, string Body)> RawAsync(int port, string target)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("localhost", port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET {target} HTTP/1.1\r\nHost: localhost:{port}\r\n{TestTrackingHttpHeaders.CurrentTestIdHeader}: r0\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var answer = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var parts = answer.Split("\r\n\r\n", 2);
        return (int.Parse(answer.Split(' ', 3)[1], CultureInfo.InvariantCulture), parts.Length > 1 ? parts[1] : "");
    }

    // The tap answers first and records second, so the records can trail the answer.
    private static async Task<RequestResponseLog[]> RecordsAsync(ListSink sink, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (sink.Logs)
                if (sink.Logs.Count >= expected)
                    return sink.Logs.ToArray();
            await Task.Delay(10);
        }

        lock (sink.Logs) return sink.Logs.ToArray();
    }

    [Theory]
    [InlineData("//localhost:{other}/x")]
    [InlineData("/\\localhost:{other}/x")]
    public async Task A_request_target_naming_another_host_is_forwarded_to_the_forward_base(string template)
    {
        await using var configured = new StubUpstream(Answer("configured"));
        await using var other = new StubUpstream(Answer("other"));
        var (tap, sink) = await StartAsync(configured);
        await using var _ = tap;

        var (status, body) = await RawAsync(tap.Options.ListenPort, template.Replace("{other}", other.Port.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal((200, "configured"), (status, body));
        Assert.Empty(other.Seen);
        // The listener reads a backslash in the path as a slash, so both arrive as the same path.
        var path = $"//localhost:{other.Port}/x";
        Assert.Equal(path, Assert.Single(configured.Seen).Path);
        var records = await RecordsAsync(sink, 2);
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.Equal((configured.BaseUri.Authority, path), (r.Uri.Authority, r.Uri.PathAndQuery)));
    }

    [Theory]
    [InlineData("//api/v1/items")]
    [InlineData("///api/v1/items")]
    public async Task A_path_with_leading_slashes_reaches_the_upstream(string target)
    {
        await using var configured = new StubUpstream(Answer("configured"));
        var (tap, _) = await StartAsync(configured);
        await using var __ = tap;

        var (status, body) = await RawAsync(tap.Options.ListenPort, target);

        Assert.Equal((200, "configured"), (status, body));
        Assert.Equal(target, Assert.Single(configured.Seen).Path);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public async Task An_absolute_form_target_naming_another_host_never_reaches_it()
    {
        await using var configured = new StubUpstream(Answer("configured"));
        await using var other = new StubUpstream(Answer("other"));
        var (tap, _) = await StartAsync(configured);
        await using var __ = tap;

        // Either the listener answers it itself (the managed listener, and http.sys for a host name the tap's prefix
        // does not use), or it hands the tap the request under the tap's own authority (http.sys for the same host
        // name, whatever the port), and the tap forwards its path to the configured upstream.
        var (status, body) = await RawAsync(tap.Options.ListenPort, $"http://localhost:{other.Port}/x");
        Assert.Empty(other.Seen);
        if (status is >= 200 and < 300)
            Assert.Equal(("configured", "/x"), (body, Assert.Single(configured.Seen).Path));
        else
            Assert.Empty(configured.Seen);

        // One naming the tap itself is forwarded as its path.
        var (selfStatus, selfBody) = await RawAsync(tap.Options.ListenPort, $"http://localhost:{tap.Options.ListenPort}/self");
        Assert.Equal((200, "configured"), (selfStatus, selfBody));
        Assert.Equal("/self", configured.Seen[^1].Path);
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:21/")]
    [InlineData("file:///tmp/")]
    public async Task A_forward_base_that_is_not_http_is_rejected_at_start(string forwardBase)
    {
        await using var tap = new Kronikol.Extensions.ProxyTap.ProxyTap(new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = new Uri(forwardBase), CallerName = "web", ServiceName = "api", Sink = new ListSink(),
        });

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => tap.StartAsync());

        Assert.Equal(nameof(ProxyTapOptions.ForwardBaseUri), thrown.ParamName);
        Assert.False(tap.IsListening);
    }
}
