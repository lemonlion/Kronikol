using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// Where a forwarded request goes: to <see cref="OtlpTapOptions.ForwardBaseUri"/>'s scheme and authority with the
/// target's origin form, whatever host the target names (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md R0).
/// </summary>
public class OtlpTapForwardTargetTests
{
    private static async Task<OtlpTap> StartAsync(StubCollector configured)
    {
        var tap = new OtlpTap(new OtlpTapOptions { ListenPort = 0, Sink = new ListSink(), ForwardBaseUri = configured.BaseUri });
        await tap.StartAsync();
        return tap;
    }

    /// <summary>Sends one request line as written and returns the status the exporter got.</summary>
    private static async Task<int> RawAsync(int port, string requestLine)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("localhost", port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: localhost:{port}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var answer = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(answer.Length == 0, $"no answer to {requestLine}");
        return int.Parse(answer.Split(' ', 3)[1], CultureInfo.InvariantCulture);
    }

    [Theory]
    // An ordinary path first: on Linux it also parses as an absolute URI (file:///v1/traces).
    [InlineData("POST /v1/traces?x=1", "/v1/traces?x=1")]
    [InlineData("POST //localhost:{other}/v1/traces", "//localhost:{other}/v1/traces")]
    [InlineData("POST http://localhost:{other}/v1/traces?x=1", "/v1/traces?x=1")]
    [InlineData("GET http://localhost:{other}/anything", "/anything")]
    [InlineData("POST ///localhost:{other}/v1/traces", "///localhost:{other}/v1/traces")]
    public async Task A_target_naming_another_host_is_forwarded_to_the_forward_base(string requestLine, string forwarded)
    {
        await using var configured = new StubCollector();
        await using var other = new StubCollector();
        await using var tap = await StartAsync(configured);
        var port = other.Port.ToString(CultureInfo.InvariantCulture);

        var status = await RawAsync(tap.BoundPort, requestLine.Replace("{other}", port));

        Assert.Equal(200, status);
        Assert.Empty(other.Seen);
        Assert.Equal(forwarded.Replace("{other}", port), Assert.Single(configured.Seen).Path);
    }

    [Theory]
    [InlineData("OPTIONS * HTTP/1.1")]
    [InlineData("CONNECT localhost:{other} HTTP/1.1")]
    [InlineData("GET localhost:{other}/x HTTP/1.1")]
    public async Task A_target_with_no_origin_form_gets_400(string requestLine)
    {
        await using var configured = new StubCollector();
        await using var other = new StubCollector();
        await using var tap = await StartAsync(configured);

        var status = await RawAsync(tap.BoundPort, requestLine.Replace("{other}", other.Port.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal(400, status);
        Assert.Empty(other.Seen);
        Assert.Empty(configured.Seen);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Theory]
    [InlineData("ftp://127.0.0.1:21/")]
    [InlineData("file:///tmp/")]
    public async Task A_forward_base_that_is_not_http_is_rejected_at_start(string forwardBase)
    {
        await using var tap = new OtlpTap(new OtlpTapOptions { ListenPort = 0, Sink = new ListSink(), ForwardBaseUri = new Uri(forwardBase) });

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => tap.StartAsync());

        Assert.Equal(nameof(OtlpTapOptions.ForwardBaseUri), thrown.ParamName);
    }
}
