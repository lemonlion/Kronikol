using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.Otlp;
using Kronikol.Reports;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// A failed accept never stops the tap (plans/TCPTAP_FAILED_CONNECT_PLAN.md R1). <c>OtlpTap</c> ran the same accept loop as
/// <c>TcpTap</c>, which returned on any <see cref="SocketException"/>: on Linux, with the process out of file descriptors,
/// its 127.0.0.1 listener never answered again (harness <c>accept/</c> E2). The loop is shared with <c>TcpTap</c>, whose
/// facts pin the pauses and the log's pace; these pin that <c>OtlpTap</c> runs it. A loopback tap listens on 127.0.0.1 and
/// on ::1, one loop each, and the seam fails the IPv4 listener's accepts, the one E2 saw stop.
/// </summary>
public class OtlpTapAcceptTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static SocketException OutOfDescriptors() => new((int)SocketError.TooManyOpenSockets);

    private static bool IsIPv4(TcpListener listener) => listener.LocalEndpoint.AddressFamily == AddressFamily.InterNetwork;

    private static OtlpTap Tap(List<string> log, Action<string>? logged = null) => new(new OtlpTapOptions
    {
        ListenPort = 0,
        ListenHost = "127.0.0.1",
        Sink = new ListSink(),
        Log = line =>
        {
            lock (log) log.Add(line);
            logged?.Invoke(line);
        },
    });

    /// <summary>The status line a request to the tap is answered with within <see cref="Patience"/>; empty when none.</summary>
    private static async Task<string> AskAsync(OtlpTap tap)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, tap.BoundPort);
        var stream = client.GetStream();
        await stream.WriteAsync("GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"u8.ToArray());
        var heard = new StringBuilder();
        var buffer = new byte[1024];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            while (!heard.ToString().Contains("\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, patience.Token);
                if (read == 0)
                    break;
                heard.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Nothing more came.
        }

        var text = heard.ToString();
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    private static string[] Lines(List<string> log, string containing)
    {
        lock (log)
            return log.Where(line => line.Contains(containing, StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public async Task A_failed_accept_does_not_stop_the_tap()
    {
        var log = new List<string>();
        await using var tap = Tap(log);
        var tries = 0;
        tap.Accepting.Accept = (listener, ct) => IsIPv4(listener) && Interlocked.Increment(ref tries) <= 3
            ? ValueTask.FromException<TcpClient>(OutOfDescriptors())
            : listener.AcceptTcpClientAsync(ct);
        await tap.StartAsync();

        Assert.Equal("HTTP/1.1 404 Not Found", await AskAsync(tap));
        Assert.Equal(3, tap.Accepting.Failures);
        Assert.Equal(3, tap.AcceptFailures);

        var failed = Assert.Single(Lines(log, "failed ("));
        Assert.Contains($"otlp-tap accept on 127.0.0.1:{tap.BoundPort} failed (SocketException TooManyOpenSockets: ", failed);
        Assert.Single(Lines(log, "accepting again"));

        var entry = Assert.Single(tap.Diagnostics());
        Assert.Equal(DiagnosticKind.CaptureDegraded, entry.Kind);
        Assert.StartsWith("otlp: 3 accept(s) failed, the first at ", entry.Message);
        Assert.Contains("the tap kept listening and tried again", entry.Message);

        await tap.DisposeAsync();
        Assert.Contains(", 3 failed accepts", Assert.Single(Lines(log, "otlp-tap stopped (")));
    }

    [Fact]
    public async Task A_log_callback_that_throws_does_not_fail_a_start_whose_second_bind_fails()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv6 && CanListen(IPAddress.IPv6Loopback), "this machine cannot listen on the IPv6 loopback");
        for (var attempt = 1; await StartsBesideATakenIPv6PortAsync(attempt) is false; attempt++)
        {
        }
    }

    // One try of the fact above: false when another test holds 127.0.0.1 on the port taken on ::1, so try another.
    private static async Task<bool> StartsBesideATakenIPv6PortAsync(int attempt)
    {
        // Something else holds [::1] on the port, so the tap's second listener cannot bind and logs that it could not.
        using var holder = LoopbackPort.Hold(
            () =>
            {
                var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
                socket.Listen();
                return socket;
            },
            socket => ((IPEndPoint)socket.LocalEndPoint!).Port,
            socket => socket.Dispose());
        var port = ((IPEndPoint)holder.LocalEndPoint!).Port;
        var log = new List<string>();
        var tap = new OtlpTap(new OtlpTapOptions
        {
            ListenPort = port,
            ListenHost = "localhost",
            Sink = new ListSink(),
            Log = line =>
            {
                lock (log) log.Add(line);
                throw new InvalidOperationException("There is no currently active test.");
            },
        });

        var starting = await Record.ExceptionAsync(() => tap.StartAsync());
        try
        {
            if (starting is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } && attempt < 5)
                return false;
            Assert.Null(starting);
            Assert.Single(Lines(log, $"otlp-tap could not also bind {IPAddress.IPv6Loopback}"));
            Assert.Equal("HTTP/1.1 404 Not Found", await AskAsync(tap));
            return true;
        }
        finally
        {
            await Record.ExceptionAsync(async () => await tap.DisposeAsync());
        }
    }

    /// <summary>
    /// A ::1 listener that cannot be made, as in a process out of file descriptors, is logged and skipped like one that
    /// cannot bind. It used to be made outside the guarded step, so the failure escaped <c>StartAsync</c> with the IPv4
    /// listener already open and no accept loop on it.
    /// </summary>
    [Fact]
    public async Task A_second_listener_that_cannot_be_made_is_logged_and_skipped()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv6, "a loopback tap listens on 127.0.0.1 alone where there is no IPv6");
        var log = new List<string>();
        await using var tap = Tap(log);
        tap.CreateListener = (address, port) => address.Equals(IPAddress.IPv6Loopback)
            ? throw new SocketException((int)SocketError.TooManyOpenSockets)
            : new TcpListener(address, port);

        await tap.StartAsync();

        Assert.True(tap.IsListening);
        Assert.Equal("HTTP/1.1 404 Not Found", await AskAsync(tap));
        Assert.Single(Lines(log, $"otlp-tap could not also bind {IPAddress.IPv6Loopback}: "));
        Assert.Empty(tap.Diagnostics());
    }

    /// <summary>
    /// When something else holds the port on ::1, the tap says so in its log and in <c>Diagnostics()</c>: an exporter that
    /// dials localhost and tries ::1 first may reach that socket instead of the tap, and its spans never arrive.
    /// </summary>
    [Fact]
    public async Task A_taken_IPv6_port_is_logged_and_reported()
    {
        Assert.SkipUnless(Socket.OSSupportsIPv6 && CanListen(IPAddress.IPv6Loopback), "this machine cannot listen on the IPv6 loopback");
        for (var attempt = 1; ; attempt++)
        {
            if (await ReportsATakenIPv6PortAsync(attempt))
                return;
        }
    }

    // One try of the fact above: false when another test holds 127.0.0.1 on the port taken on ::1, so try another.
    private static async Task<bool> ReportsATakenIPv6PortAsync(int attempt)
    {
        using var holder = LoopbackPort.Hold(
            () =>
            {
                var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
                socket.Listen();
                return socket;
            },
            socket => ((IPEndPoint)socket.LocalEndPoint!).Port,
            socket => socket.Dispose());
        var port = ((IPEndPoint)holder.LocalEndPoint!).Port;
        var log = new List<string>();
        await using var tap = new OtlpTap(new OtlpTapOptions
        {
            ListenPort = port,
            ListenHost = "localhost",
            Sink = new ListSink(),
            Log = line => { lock (log) log.Add(line); },
        });

        try
        {
            await tap.StartAsync();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 5)
        {
            return false;
        }

        var line = Assert.Single(Lines(log, $"otlp-tap could not also bind {IPAddress.IPv6Loopback}"));
        Assert.Contains($"whose port {port} something else holds", line, StringComparison.Ordinal);
        var entry = Assert.Single(tap.Diagnostics());
        Assert.Contains($"could not also listen on [::1]:{port}, which something else holds", entry.Message, StringComparison.Ordinal);
        return true;
    }

    private static bool CanListen(IPAddress address)
    {
        try
        {
            using var probe = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(address, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// A tap on an IPv6 address starts and names an endpoint an exporter can dial. Since 4.20.3 the start line read
    /// <see cref="OtlpTap.TracesEndpoint"/> whether or not a <c>Log</c> callback was set, and an unbracketed IPv6 host made it
    /// throw, so <c>StartAsync</c> failed after the tap had bound its socket and started serving.
    /// </summary>
    [Theory]
    [InlineData("::1", "[::1]")]
    [InlineData("[::1]", "[::1]")]
    [InlineData("::", "localhost")]
    [InlineData("::0", "localhost")]
    public async Task A_tap_on_an_IPv6_address_starts_and_names_an_endpoint_an_exporter_can_dial(string listenHost, string shownHost)
    {
        Assert.SkipUnless(CanListen(IPAddress.IPv6Loopback), "this machine cannot listen on the IPv6 loopback");
        var log = new List<string>();
        await using var tap = new OtlpTap(new OtlpTapOptions
        {
            ListenPort = 0,
            ListenHost = listenHost,
            Sink = new ListSink(),
            Log = line => { lock (log) log.Add(line); },
        });

        await tap.StartAsync();

        Assert.True(tap.IsListening);
        Assert.Equal($"http://{shownHost}:{tap.BoundPort}/v1/traces", tap.TracesEndpoint.ToString());
        Assert.Single(Lines(log, $"otlp-tap listening on http://{shownHost}:{tap.BoundPort}/v1/traces"));
    }

    [Fact]
    public async Task Stopping_the_tap_while_it_waits_for_a_caller_counts_nothing()
    {
        var log = new List<string>();
        await using var tap = Tap(log);
        await tap.StartAsync();
        Assert.Equal("HTTP/1.1 404 Not Found", await AskAsync(tap));

        // Both loops wait in a real accept, which the stop ends with an exception of the platform's choosing.
        await tap.DisposeAsync();
        await Task.Delay(100);

        Assert.Equal(0, tap.Accepting.Failures);
        Assert.Empty(Lines(log, "failed ("));
        Assert.Empty(Lines(log, "stopped accepting"));
        Assert.Empty(tap.Diagnostics());
    }

    [Fact]
    public async Task A_log_callback_that_throws_does_not_stop_the_accept_loop()
    {
        var log = new List<string>();
        var throwing = 0;
        await using var tap = Tap(log, _ =>
        {
            if (Volatile.Read(ref throwing) == 1)
                throw new InvalidOperationException("There is no currently active test.");
        });
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tries = 0;
        tap.Accepting.Accept = async (listener, ct) =>
        {
            await go.Task.WaitAsync(ct);
            if (IsIPv4(listener) && Interlocked.Increment(ref tries) <= 3)
                throw OutOfDescriptors();
            return await listener.AcceptTcpClientAsync(ct);
        };
        await tap.StartAsync();
        Volatile.Write(ref throwing, 1);
        go.SetResult();

        try
        {
            Assert.Equal("HTTP/1.1 404 Not Found", await AskAsync(tap));
            Assert.Equal(3, tap.Accepting.Failures);
            Assert.Single(Lines(log, "failed ("));
            Assert.Single(Lines(log, "accepting again"));
        }
        finally
        {
            // Stopping has a fact of its own, so a red run here fails on what this fact is about.
            Volatile.Write(ref throwing, 0);
        }
    }

    [Fact]
    public async Task A_log_callback_that_throws_does_not_fail_starting_or_stopping()
    {
        var log = new List<string>();
        var tap = Tap(log, _ => throw new InvalidOperationException("There is no currently active test."));

        var starting = await Record.ExceptionAsync(() => tap.StartAsync());
        string answer;
        Exception? stopping;
        try
        {
            answer = await AskAsync(tap);
        }
        finally
        {
            stopping = await Record.ExceptionAsync(async () => await tap.DisposeAsync());
        }

        Assert.Null(starting);
        Assert.Equal("HTTP/1.1 404 Not Found", answer);
        Assert.Null(stopping);
        Assert.Single(Lines(log, "otlp-tap listening on"));
        Assert.Single(Lines(log, "otlp-tap stopped ("));
    }
}
