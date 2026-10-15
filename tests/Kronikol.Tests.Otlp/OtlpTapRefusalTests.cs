using System.Net;
using System.Net.Http.Headers;
using Kronikol.Extensions.Otlp;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// Requests the tap refuses, and when (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.6 items 3, 4, 9 and 11). The shared secret
/// was checked only once the whole body had been read into an array sized up front to its declared length, so a caller
/// without it could make the tap allocate and hold <see cref="OtlpTapOptions.MaxRequestBytes"/> by declaring a body and
/// stalling. A <c>Content-Length</c> that was not a plain number in <c>int</c> range was read as no body, and the body then
/// as the next request's head. A refusal before the body is read is answered, then the connection closes in stages: the
/// answer, a FIN, and the body read and thrown away, so a caller still sending does not lose the answer to a reset.
/// </summary>
public class OtlpTapRefusalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly byte[] Export = OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv);

    private static string Head(string contentLength, string extra = "") =>
        $"POST /v1/traces HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\n{extra}Content-Length: {contentLength}\r\n\r\n";

    [Fact]
    public async Task An_unauthenticated_export_is_refused_before_its_body_is_read()
    {
        var sink = new ListSink();
        await using var tap = await StartAsync(sink, o => o.ExpectedHeaders["Authorization"] = "Bearer secret");

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(Head((32 * 1024 * 1024).ToString()) + "0123456789"); // the rest of the body never comes

        var heard = await exporter.HearAsync(Patience);

        Assert.StartsWith("HTTP/1.1 401 ", heard.FirstLine);
        Assert.Contains("Connection: close", heard.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(heard.Closed, $"the tap answered and did not close its side: {heard}");
        Assert.Equal(1, tap.UnauthenticatedRequests);
        Assert.Equal(0, tap.RequestsReceived);
        Assert.Equal(0, tap.PayloadsRejected);
        Assert.Empty(sink.Logs);
    }

    [Fact]
    public async Task An_unauthenticated_export_still_sending_its_body_hears_its_401()
    {
        await using var tap = await StartAsync(new ListSink(), o => o.ExpectedHeaders["Authorization"] = "Bearer secret");

        // HttpClient sends its whole body before it reads: closed at once, the 401 was lost to the reset that the unread
        // body turned the close into (plans/TAP_CALLER_TIME_LIMITS_PLAN.harness/sweep/early-answer-probe-*).
        using var client = new HttpClient { Timeout = Patience };
        using var content = new ByteArrayContent(new byte[8 * 1024 * 1024]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        using var response = await client.PostAsync(tap.TracesEndpoint, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, tap.UnauthenticatedRequests);
    }

    [Fact]
    public async Task An_unauthenticated_export_over_the_cap_is_401_not_413()
    {
        await using var tap = await StartAsync(new ListSink(), o =>
        {
            o.ExpectedHeaders["Authorization"] = "Bearer secret";
            o.MaxRequestBytes = 1024;
        });

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(Head("4096") + new string('x', 4096));
        var heard = await exporter.HearAsync(Patience);

        Assert.StartsWith("HTTP/1.1 401 ", heard.FirstLine);
        Assert.Equal(1, tap.UnauthenticatedRequests);
        Assert.Equal(0, tap.PayloadsRejected);
    }

    [Theory]
    [InlineData("abc", 400)]
    [InlineData("-5", 400)]
    [InlineData("+5", 400)]
    [InlineData("10, 10", 400)]
    [InlineData("", 400)]
    [InlineData("3000000000", 413)]
    [InlineData("2147483648", 413)]
    public async Task A_content_length_that_is_not_digits_in_range_is_refused(string contentLength, int status)
    {
        var sink = new ListSink();
        await using var tap = await StartAsync(sink);

        using var exporter = await RawExporter.ConnectAsync(tap);
        // A second, well-formed export follows on the same connection: read out of step, the tap would answer it too.
        await exporter.SendAsync([.. System.Text.Encoding.ASCII.GetBytes(Head(contentLength) + "0123456789"),
            .. System.Text.Encoding.ASCII.GetBytes(Head(Export.Length.ToString())), .. Export]);

        var heard = await exporter.HearAsync(Patience);

        Assert.Equal([$"HTTP/1.1 {status} {(status == 400 ? "Bad Request" : "Payload Too Large")}"], heard.StatusLines);
        Assert.True(heard.Closed, $"the tap did not close the connection after refusing its framing: {heard}");
        Assert.Equal(0, tap.RequestsReceived);
        Assert.Equal(status == 413 ? 1 : 0, tap.PayloadsRejected);
        Assert.Empty(sink.Logs);
    }

    [Fact]
    public async Task A_content_length_of_zero_is_still_an_empty_body()
    {
        await using var tap = await StartAsync(new ListSink());

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync([.. System.Text.Encoding.ASCII.GetBytes(Head("0") + Head(Export.Length.ToString())), .. Export]);
        await WaitForAsync(() => tap.RequestsReceived == 2, "both exports to be received");
        exporter.Client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);
        var heard = await exporter.HearAsync(Patience);

        Assert.Equal(["HTTP/1.1 200 OK", "HTTP/1.1 200 OK"], heard.StatusLines);
    }

    [Fact]
    public async Task An_over_cap_body_cut_short_is_not_counted()
    {
        await using var tap = await StartAsync(new ListSink(), o => o.MaxRequestBytes = 1024 * 1024);

        using var exporter = await RawExporter.ConnectAsync(tap);
        await exporter.SendAsync(Head((64 * 1024 * 1024).ToString()));
        await exporter.SendAsync(new byte[1024 * 1024]);
        exporter.Client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send); // gone, mid-drain

        var heard = await exporter.HearAsync(Patience);

        Assert.True(heard.Closed, $"the tap did not close the connection: {heard}");
        Assert.Empty(heard.Bytes);
        Assert.Equal(0, tap.PayloadsRejected);
    }

    [Fact]
    public async Task A_request_with_both_framings_closes_its_connection()
    {
        await using var tap = await StartAsync(new ListSink());

        using var exporter = await RawExporter.ConnectAsync(tap);
        var chunked = $"{Export.Length:x}\r\n{System.Text.Encoding.ASCII.GetString(Export)}\r\n0\r\n\r\n";
        await exporter.SendAsync(Head("5", "Transfer-Encoding: chunked\r\n") + chunked + Head(Export.Length.ToString()) + System.Text.Encoding.ASCII.GetString(Export));

        var heard = await exporter.HearAsync(Patience);

        Assert.Equal(["HTTP/1.1 200 OK"], heard.StatusLines);
        Assert.Contains("Connection: close", heard.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(heard.Closed, $"the tap kept the connection: {heard}");
        Assert.Equal(1, tap.RequestsReceived);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<OtlpTap> StartAsync(ListSink sink, Action<OtlpTapOptions>? configure = null)
    {
        var options = new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = "127.0.0.1",
            Sink = sink,
        };
        configure?.Invoke(options);
        var tap = new OtlpTap(options);
        await tap.StartAsync();
        return tap;
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {what}.");
            await Task.Delay(20);
        }
    }
}
