using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kronikol.Constants;
using Kronikol.Extensions.ProxyTap;
using Kronikol.Ingestion;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Tap = Kronikol.Extensions.ProxyTap.ProxyTap;

namespace Kronikol.Tests.ProxyTap;

/// <summary>An upstream that takes connections and never answers.</summary>
internal sealed class SilentUpstream : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<Socket> _held = [];

    public SilentUpstream()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    public int Port { get; }

    public Uri BaseUri => new($"http://127.0.0.1:{Port}");

    public int Accepted
    {
        get { lock (_held) return _held.Count; }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var socket = await _listener.AcceptSocketAsync();
                lock (_held) _held.Add(socket);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Stopped.
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        lock (_held)
            foreach (var socket in _held)
                socket.Dispose();
    }
}

/// <summary>
/// An upstream whose answer promises 100 bytes and sends 10, then ends the connection: with a FIN, or with an RST
/// alone (a zero linger and <see cref="Socket.Dispose()"/>; <see cref="TcpClient.Dispose()"/> would send a FIN first).
/// </summary>
internal sealed class CutShortUpstream : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly bool _reset;

    public CutShortUpstream(bool reset)
    {
        _reset = reset;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = ServeAsync();
    }

    public int Port { get; }

    public Uri BaseUri => new($"http://127.0.0.1:{Port}");

    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var socket = await _listener.AcceptSocketAsync();
                _ = AnswerAsync(socket);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Stopped.
        }
    }

    private async Task AnswerAsync(Socket socket)
    {
        try
        {
            var head = new StringBuilder();
            var buffer = new byte[4096];
            while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None);
                if (read == 0) break;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            await socket.SendAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 100\r\n\r\n0123456789"), SocketFlags.None);
            // Long enough for the head to be read, so the cut falls in the body.
            await Task.Delay(200);
            if (_reset)
            {
                socket.LingerState = new LingerOption(true, 0);
            }
            else
            {
                socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The tap went away first.
        }
        finally
        {
            socket.Dispose();
        }
    }

    public void Dispose() => _listener.Stop();
}

/// <summary>A sink that cannot write.</summary>
internal sealed class ThrowingSink : IRequestResponseSink
{
    public void Log(RequestResponseLog log) => throw new IOException("disk full");
}

/// <summary>
/// A forward that fails is recorded as a failed call, and capture never changes what the caller gets
/// (plans/PROXY_TAP_FAILED_FORWARD_PLAN.md R1 and R2, #171).
/// </summary>
public class ProxyTapFailureTests
{
    internal static async Task<Tap> StartAsync(Uri forward, IRequestResponseSink sink, Action<ProxyTapOptions>? configure = null)
    {
        var options = new ProxyTapOptions
        {
            ListenPort = StubUpstream.FreePort(), ForwardBaseUri = forward, CallerName = "web", ServiceName = "api", Sink = sink,
        };
        configure?.Invoke(options);
        var tap = new Tap(options);
        await tap.StartAsync();
        return tap;
    }

    internal static async Task<(int Status, string Body)> SendAsync(Tap tap, string? testId, HttpMethod? method = null, string path = "/orders?x=1", string? body = null, params (string, string)[] headers)
    {
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(tap.ListenUri, path));
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (testId is not null)
        {
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestIdHeader, testId);
            request.Headers.TryAddWithoutValidation(TestTrackingHttpHeaders.CurrentTestNameHeader, "failed forward");
        }

        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // The tap answers first and records second, so the records can trail the answer.
    internal static async Task<RequestResponseLog[]> RecordsAsync(ListSink sink, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (sink.Logs)
                if (sink.Logs.Count >= expected)
                    return sink.Logs.ToArray();
            await Task.Delay(10);
        }

        lock (sink.Logs) return sink.Logs.ToArray();
    }

    internal static async Task<long> EventuallyAsync(Func<long> read, long expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (read() != expected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        return read();
    }

    internal static string? StatusOf(RequestResponseLog log) => log.StatusCode?.Value switch { HttpStatusCode code => ((int)code).ToString(), string text => text, _ => null };

    internal static string MessageOf(string badGateway) => JsonDocument.Parse(badGateway).RootElement.GetProperty("error").GetProperty("message").GetString()!;

    private static (RequestResponseLog Request, RequestResponseLog Response) Pair(RequestResponseLog[] records)
    {
        Assert.Equal(2, records.Length);
        return (records.Single(r => r.Type == RequestResponseType.Request), records.Single(r => r.Type == RequestResponseType.Response));
    }

    private static string ForwardFailureLine(Tap tap) =>
        tap.Diagnostics().Select(d => d.Message).Single(m => m.Contains("could not be forwarded", StringComparison.Ordinal));

    // ---- R1 ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_forward_the_upstream_refused_is_recorded_as_a_failed_call()
    {
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink);

        var (status, body) = await SendAsync(tap, "t-refused", HttpMethod.Post, "/orders?x=1", """{"n":1}""");

        Assert.Equal(502, status);
        var (request, response) = Pair(await RecordsAsync(sink, 2));
        Assert.Equal((request.RequestResponseId, request.TraceId), (response.RequestResponseId, response.TraceId));
        Assert.Equal("POST", request.Method.Value is HttpMethod method ? method.Method : request.Method.Value as string);
        Assert.Equal($"http://127.0.0.1:{dead.Port}/orders?x=1", request.Uri.ToString());
        Assert.Equal("""{"n":1}""", request.Content);
        Assert.Equal(("failed forward", "t-refused"), (request.TestName, request.TestId));
        Assert.Equal("!HttpRequestException", StatusOf(response));
        Assert.Contains(" Caused by: ", response.Error);
        Assert.False(response.Error!.StartsWith("proxy-tap:", StringComparison.Ordinal), response.Error);
        Assert.Equal(body, response.Content);
        Assert.Equal("proxy-tap: " + response.Error, MessageOf(body));
        Assert.Contains(response.Headers, h => h.Key == "Content-Type" && h.Value == "application/json");
        Assert.True(response.Timestamp >= request.Timestamp);
        Assert.Equal((request.ActivityTraceId, request.ActivitySpanId), (response.ActivityTraceId, response.ActivitySpanId));
        Assert.Equal(1, tap.ForwardFailures);
        Assert.Equal(0, tap.RequestsHandled);
        Assert.Equal(0, tap.RequestsCaptured);
    }

    [Fact]
    public async Task A_forward_that_timed_out_names_the_deadline()
    {
        using var silent = new SilentUpstream();
        var sink = new ListSink();
        await using var tap = await StartAsync(silent.BaseUri, sink, o => o.ForwardTimeout = TimeSpan.FromSeconds(1));

        var (status, body) = await SendAsync(tap, "t-timeout");

        Assert.Equal(502, status);
        var (_, response) = Pair(await RecordsAsync(sink, 2));
        Assert.Equal("!TaskCanceledException", StatusOf(response));
        Assert.StartsWith($"proxy-tap: no answer from 127.0.0.1:{silent.Port} within ForwardTimeout (00:00:01). Caused by: ", response.Error);
        Assert.Equal(response.Error, MessageOf(body));
        Assert.Equal(1, tap.ForwardFailures);
    }

    [Theory]
    [InlineData(false, "The response ended prematurely")]
    [InlineData(true, "Unable to read data from the transport connection")]
    public async Task A_response_cut_short_is_recorded_with_its_cause(bool reset, string cause)
    {
        using var upstream = new CutShortUpstream(reset);
        var sink = new ListSink();
        await using var tap = await StartAsync(upstream.BaseUri, sink);

        var (status, body) = await SendAsync(tap, "t-cut");

        Assert.Equal(502, status);
        var (_, response) = Pair(await RecordsAsync(sink, 2));
        Assert.Equal("!HttpRequestException", StatusOf(response));
        Assert.Contains(cause, response.Error);
        Assert.Contains(cause, MessageOf(body));
    }

    [Fact]
    public async Task A_forward_cut_by_disposal_is_recorded_and_not_counted_as_a_forward_failure()
    {
        using var silent = new SilentUpstream();
        var sink = new ListSink();
        var tap = await StartAsync(silent.BaseUri, sink);
        var call = Task.Run(async () =>
        {
            try { await SendAsync(tap, "t-dispose"); }
            catch (HttpRequestException) { /* the listener may close first */ }
        });
        Assert.Equal(1, await EventuallyAsync(() => silent.Accepted, 1));

        await tap.DisposeAsync();
        await call.WaitAsync(TimeSpan.FromSeconds(10));

        var (_, response) = Pair(await RecordsAsync(sink, 2));
        Assert.Equal("!TaskCanceledException", StatusOf(response));
        Assert.StartsWith("proxy-tap: the tap stopped before api answered. Caused by: ", response.Error);
        Assert.Equal(0, tap.ForwardFailures);
    }

    [Fact]
    public void Forward_failures_are_classified_by_the_token_that_ended_them()
    {
        using var lifetime = new CancellationTokenSource();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        // The shape a ConnectTimeout throws through the tap's HttpClient (probe-audit.cs): neither token is cancelled.
        var connectTimeout = new TaskCanceledException("The operation was canceled.",
            new TimeoutException("A connection could not be established within the configured ConnectTimeout."));
        var refused = new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        Assert.Equal(ForwardFailureKind.ConnectTimedOut, ForwardFailure.Classify(connectTimeout, deadline.Token, lifetime.Token));
        Assert.Equal(ForwardFailureKind.Failed, ForwardFailure.Classify(refused, deadline.Token, lifetime.Token));
        Assert.Equal(ForwardFailureKind.Failed, ForwardFailure.Classify(new TaskCanceledException("no timeout inside"), deadline.Token, lifetime.Token));

        deadline.Cancel();
        Assert.Equal(ForwardFailureKind.TimedOut, ForwardFailure.Classify(new TaskCanceledException(), deadline.Token, lifetime.Token));
        // A refusal that lands as the deadline passes is still the upstream's refusal.
        Assert.Equal(ForwardFailureKind.Failed, ForwardFailure.Classify(refused, deadline.Token, lifetime.Token));

        lifetime.Cancel();
        Assert.Equal(ForwardFailureKind.Stopping, ForwardFailure.Classify(new TaskCanceledException(), deadline.Token, lifetime.Token));
        Assert.Equal(ForwardFailureKind.Stopping, ForwardFailure.Classify(new ObjectDisposedException("System.Net.Http.HttpClient"), deadline.Token, lifetime.Token));
        Assert.Equal(ForwardFailureKind.Failed, ForwardFailure.Classify(refused, deadline.Token, lifetime.Token));

        var options = new ProxyTapOptions { ForwardBaseUri = new Uri("http://10.255.255.1:81"), ServiceName = "api", ConnectTimeout = TimeSpan.FromSeconds(1) };
        Assert.Equal("could not connect to 10.255.255.1:81 within ConnectTimeout (00:00:01). Caused by: The operation was canceled. Caused by: A connection could not be established within the configured ConnectTimeout.",
            ForwardFailure.Reason(ForwardFailureKind.ConnectTimedOut, connectTimeout, options));
    }

    [Fact]
    public void Error_type_uses_dotnets_words()
    {
        Assert.Equal("connection_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.ConnectionError, "x")));
        Assert.Equal("name_resolution_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.NameResolutionError, "x")));
        Assert.Equal("secure_connection_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.SecureConnectionError, "x")));
        Assert.Equal("http_protocol_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.HttpProtocolError, "x")));
        Assert.Equal("extended_connect_not_supported", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.ExtendedConnectNotSupported, "x")));
        Assert.Equal("version_negotiation_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.VersionNegotiationError, "x")));
        Assert.Equal("user_authentication_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.UserAuthenticationError, "x")));
        Assert.Equal("proxy_tunnel_error", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.ProxyTunnelError, "x")));
        Assert.Equal("invalid_response", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.InvalidResponse, "x")));
        Assert.Equal("response_ended", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.ResponseEnded, "x")));
        Assert.Equal("configuration_limit_exceeded", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, "x")));
        // An RST mid-body is Unknown, for which .NET falls back to the type name, as for any other exception.
        Assert.Equal("System.Net.Http.HttpRequestException", ForwardFailure.ErrorType(new HttpRequestException(HttpRequestError.Unknown, "x")));
        Assert.Equal("System.Threading.Tasks.TaskCanceledException", ForwardFailure.ErrorType(new TaskCanceledException()));
    }

    [Fact]
    public async Task An_unattributed_failed_forward_is_recorded_only_when_opted_in()
    {
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink))
        {
            Assert.Equal(502, (await SendAsync(tap, testId: null)).Status);
            Assert.Equal(1, await EventuallyAsync(() => tap.ForwardFailures, 1));
            Assert.EndsWith("; they carried no test identity and were not recorded", ForwardFailureLine(tap));
        }

        Assert.Empty(sink.Logs);

        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink, o =>
                     {
                         o.CaptureUnattributedRequests = true;
                         o.FallbackTestId = "fallback";
                     }))
        {
            Assert.Equal(502, (await SendAsync(tap, testId: null)).Status);
            var records = await RecordsAsync(sink, 2);
            Assert.Equal(2, records.Length);
            Assert.All(records, r => Assert.Equal("fallback", r.TestId));
        }
    }

    [Fact]
    public async Task An_ignored_failed_forward_is_recorded_as_ignored()
    {
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink);

        Assert.Equal(502, (await SendAsync(tap, "t-ignored", headers: (TestTrackingHttpHeaders.Ignore, "true"))).Status);

        var records = await RecordsAsync(sink, 2);
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.True(r.TrackingIgnore));
    }

    [Fact]
    public void The_bad_gateway_body_is_valid_json_and_keeps_the_message_exactly()
    {
        const string message = "a \"quote\", a back\\slash, a line\nbreak, a\ttab and é";

        var body = Tap.BadGatewayBody(message);

        var json = JsonDocument.Parse(body).RootElement.GetProperty("error");
        Assert.Equal(502, json.GetProperty("code").GetInt32());
        Assert.Equal("proxy-tap: " + message, json.GetProperty("message").GetString());
        var text = Encoding.UTF8.GetString(body);
        Assert.Contains("\\\"quote\\\"", text);
        Assert.Contains("é", text);
    }

    [Fact]
    public async Task Every_resolved_request_ends_in_one_response_record()
    {
        var starts = 0;
        var sink = new ListSink();
        void Resolve(ProxyTapOptions o) => o.IdentityResolver = (_, _) =>
        {
            Interlocked.Increment(ref starts);
            return ("invariant", "t-invariant");
        };

        using var dead = new ClosedPort();
        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink, Resolve))
            await SendAsync(tap, testId: null);

        using var silent = new SilentUpstream();
        await using (var tap = await StartAsync(silent.BaseUri, sink, o => { Resolve(o); o.ForwardTimeout = TimeSpan.FromSeconds(1); }))
            await SendAsync(tap, testId: null);

        using var cut = new CutShortUpstream(reset: false);
        await using (var tap = await StartAsync(cut.BaseUri, sink, Resolve))
            await SendAsync(tap, testId: null);

        await using var healthy = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        await using (var tap = await StartAsync(healthy.BaseUri, sink, Resolve))
            await SendAsync(tap, testId: null);

        using var stopped = new SilentUpstream();
        var disposed = await StartAsync(stopped.BaseUri, sink, Resolve);
        var call = Task.Run(async () =>
        {
            try { await SendAsync(disposed, testId: null); }
            catch (HttpRequestException) { /* the listener may close first */ }
        });
        Assert.Equal(1, await EventuallyAsync(() => stopped.Accepted, 1));
        await disposed.DisposeAsync();
        await call.WaitAsync(TimeSpan.FromSeconds(10));

        var records = await RecordsAsync(sink, 10);
        Assert.Equal(5, Volatile.Read(ref starts));
        Assert.Equal(5, records.Count(r => r.Type == RequestResponseType.Response));
    }

    [Fact]
    public async Task Forward_failure_diagnostics_say_which_calls_are_recorded()
    {
        using var dead = new ClosedPort();
        const string prefix = "web→api: {0} request(s) could not be forwarded and were answered 502 Bad Gateway — the upstream was unreachable, timed out or the exchange threw; ";

        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), new ListSink()))
        {
            await SendAsync(tap, "t-1");
            await SendAsync(tap, "t-2");
            Assert.Equal(2, await EventuallyAsync(() => tap.ForwardFailures, 2));
            Assert.Equal(string.Format(prefix, 2) + "each is recorded as a failed call", ForwardFailureLine(tap));
        }

        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), new ListSink()))
        {
            await SendAsync(tap, testId: null);
            Assert.Equal(1, await EventuallyAsync(() => tap.ForwardFailures, 1));
            Assert.Equal(string.Format(prefix, 1) + "they carried no test identity and were not recorded", ForwardFailureLine(tap));
        }

        await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), new ListSink()))
        {
            await SendAsync(tap, "t-1");
            await SendAsync(tap, "t-2");
            await SendAsync(tap, testId: null);
            Assert.Equal(3, await EventuallyAsync(() => tap.ForwardFailures, 3));
            Assert.Equal(string.Format(prefix, 3) + "2 are recorded as failed calls, 1 carried no test identity and was not recorded", ForwardFailureLine(tap));
        }
    }

    [Fact]
    public async Task A_failed_forward_survives_the_ndjson_round_trip()
    {
        using var dead = new ClosedPort();
        var path = Path.Combine(Path.GetTempPath(), "kronikol-tap-failed-" + Guid.NewGuid().ToString("N") + ".ndjson");
        try
        {
            using (var file = new NdjsonInteractionWriter(path))
            await using (var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), file))
            {
                await SendAsync(tap, "t-ndjson");
                await EventuallyAsync(() => tap.ForwardFailures, 1);
            }

            var records = NdjsonInteractionReader.ReadFile(path);
            Assert.Equal(2, records.Count);
            Assert.Equal("!HttpRequestException", records[1].StatusCode);
            Assert.Contains(" Caused by: ", records[1].Error);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task A_failed_forward_is_drawn_with_its_cause()
    {
        using var dead = new ClosedPort();
        var sink = new ListSink();
        await using var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), sink);
        var (_, body) = await SendAsync(tap, "t-drawn");
        var records = await RecordsAsync(sink, 2);

        var lines = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(records).Single().PlantUmls
            .SelectMany(p => p.PlainText.Split('\n')).Select(l => l.TrimEnd('\r')).ToList();

        // The return arrow is labelled with the failure, and its note holds the 502 body's message.
        Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^api -\[#[0-9A-Fa-f]{6}\]-> web: !HttpRequestException$"));
        var noteStart = lines.FindLastIndex(l => l.StartsWith("note", StringComparison.Ordinal));
        var noteEnd = lines.FindIndex(noteStart, l => l.StartsWith("end note", StringComparison.Ordinal));
        Assert.True(noteStart >= 0 && noteEnd > noteStart, string.Join("\n", lines));
        var message = MessageOf(body);
        Assert.Contains(lines.Skip(noteStart).Take(noteEnd - noteStart), l => l.Contains(message[..30], StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_forward_is_recorded_though_its_caller_hung_up()
    {
        using var silent = new SilentUpstream();
        var sink = new ListSink();
        await using var tap = await StartAsync(silent.BaseUri, sink, o => o.ForwardTimeout = TimeSpan.FromSeconds(1));

        using (var caller = new Socket(SocketType.Stream, ProtocolType.Tcp))
        {
            await caller.ConnectAsync("localhost", tap.Options.ListenPort);
            await caller.SendAsync(Encoding.ASCII.GetBytes(
                $"GET /x HTTP/1.1\r\nHost: localhost\r\n{TestTrackingHttpHeaders.CurrentTestIdHeader}: t-gone\r\nContent-Length: 0\r\n\r\n"), SocketFlags.None);
            Assert.Equal(1, await EventuallyAsync(() => silent.Accepted, 1));
            // An RST alone: a zero linger and Socket.Dispose().
            caller.LingerState = new LingerOption(true, 0);
        }

        var (_, response) = Pair(await RecordsAsync(sink, 2));
        Assert.StartsWith($"proxy-tap: no answer from 127.0.0.1:{silent.Port} within ForwardTimeout (00:00:01)", response.Error);
        Assert.Equal(1, await EventuallyAsync(() => tap.ForwardFailures, 1));
    }

    [Fact]
    public async Task A_fault_the_tap_does_not_expect_before_the_answer_is_answered_502_and_counted()
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var logs = new List<string>();
        var sink = new ListSink();
        await using var tap = await StartAsync(upstream.BaseUri, sink, o => o.Log = line => { lock (logs) logs.Add(line); });
        // An ObjectDisposedException, which 4.19.2's catch for hang-ups took silently.
        tap.BeforeForward = () => throw new ObjectDisposedException("something the tap uses");

        var (status, body) = await SendAsync(tap, "t-bug");

        Assert.Equal(502, status);
        Assert.Contains("something the tap uses", MessageOf(body));
        Assert.Equal(1, await EventuallyAsync(() => tap.ForwardFailures, 1));
        lock (logs) Assert.Contains(logs, l => l.Contains("proxy-tap error: ", StringComparison.Ordinal));
        Assert.Empty(upstream.Seen);
        Assert.EndsWith("; 1 was not recorded", ForwardFailureLine(tap));
    }

    [Fact]
    public async Task A_fault_the_tap_does_not_expect_after_the_answer_is_a_capture_fault()
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var sink = new ListSink();
        await using var tap = await StartAsync(upstream.BaseUri, sink);
        tap.AfterAnswered = () => throw new InvalidOperationException("after the answer");

        var (status, body) = await SendAsync(tap, "t-late");

        Assert.Equal((200, "ok"), (status, body));
        Assert.Equal(1, await EventuallyAsync(() => tap.CaptureFaults, 1));
        Assert.Equal(0, tap.ForwardFailures);
        Assert.Contains(tap.Diagnostics(), d => d.Message.Contains("1 capture fault(s)", StringComparison.Ordinal) && d.Message.Contains("InvalidOperationException, after the answer", StringComparison.Ordinal));
    }

    // ---- R2 ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_throwing_IdentityResolver_does_not_fail_the_call()
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var sink = new ListSink();
        await using var tap = await StartAsync(upstream.BaseUri, sink, o => o.IdentityResolver = (_, _) => throw new KeyNotFoundException("no such device"));

        var (status, _) = await SendAsync(tap, "t-resolver");

        Assert.Equal(200, status);
        var records = await RecordsAsync(sink, 2);
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.Equal("t-resolver", r.TestId));
        Assert.Equal(0, tap.ForwardFailures);
        var line = Assert.Single(tap.Diagnostics(), d => d.Message.Contains("capture fault(s)", StringComparison.Ordinal));
        Assert.Contains("(the first: KeyNotFoundException, no such device)", line.Message);
        Assert.Equal(DiagnosticKind.CaptureDegraded, line.Kind);
    }

    [Fact]
    public async Task A_throwing_sink_is_a_capture_fault_not_a_forward_failure()
    {
        await using var upstream = new StubUpstream((_, _) => (200, "ok"u8.ToArray(), "text/plain", null, null));
        var logs = new List<string>();
        await using var tap = await StartAsync(upstream.BaseUri, new ThrowingSink(), o => o.Log = line => { lock (logs) logs.Add(line); });

        var (status, _) = await SendAsync(tap, "t-sink");

        Assert.Equal(200, status);
        Assert.Equal(1, await EventuallyAsync(() => tap.CaptureFaults, 1));
        Assert.Equal(0, tap.ForwardFailures);
        lock (logs) Assert.Contains("[web→api] proxy-tap: the sink threw IOException: disk full", logs);
        var messages = tap.Diagnostics().Select(d => d.Message).ToList();
        Assert.Equal("web→api: 1 capture fault(s): the IdentityResolver, the sink, the body decoder or an ActivityListener threw, so a call may be missing, unattributed or untraced (the first: IOException, disk full); what the callers got was not changed", Assert.Single(messages));
    }

    [Fact]
    public async Task A_throwing_sink_does_not_change_a_failed_forwards_answer()
    {
        using var dead = new ClosedPort();
        await using var tap = await StartAsync(new Uri($"http://127.0.0.1:{dead.Port}"), new ThrowingSink());

        var (status, body) = await SendAsync(tap, "t-sink-failed");

        // The 502 still names the forward's cause, not the sink's, and the failure is counted once.
        Assert.Equal(502, status);
        Assert.Contains(" Caused by: ", MessageOf(body));
        Assert.DoesNotContain("disk full", MessageOf(body));
        Assert.Equal(1, tap.ForwardFailures);
        Assert.Equal(1, tap.CaptureFaults);
        Assert.EndsWith("; 1 was not recorded", ForwardFailureLine(tap));
    }

    [Fact]
    public async Task TryDecodeBody_returns_false_for_a_br_body_that_is_not_brotli()
    {
        var notBrotli = Encoding.UTF8.GetBytes("this is not brotli, it is plain text");

        Assert.False(Tap.TryDecodeBody(notBrotli, "br", out _));

        await using var upstream = new StubUpstream((_, _) => (200, notBrotli, "text/plain", "br", null));
        var sink = new ListSink();
        await using var tap = await StartAsync(upstream.BaseUri, sink);
        Assert.Equal(200, (await SendAsync(tap, "t-br")).Status);
        var records = await RecordsAsync(sink, 2);
        Assert.Equal($"<{notBrotli.Length} bytes, undecodable content-encoding 'br'>", records.Single(r => r.Type == RequestResponseType.Response).Content);
        Assert.Equal(0, tap.ForwardFailures);
    }
}
