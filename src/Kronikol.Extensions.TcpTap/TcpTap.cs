using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Kronikol.Extensions.TcpTap.Protocols;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// A transparent, byte-for-byte TCP tee. It listens on a port, opens one upstream connection per accepted
/// connection, and copies bytes both ways <em>unmodified and first</em>; a copy of each segment is handed to a
/// protocol <see cref="IProtocolDecoder"/> on a separate task, which turns commands and replies into Kronikol
/// request/response pairs. Point a service at the tap instead of its database and its calls appear in the
/// sequence diagrams with no change inside the service.
/// </summary>
/// <remarks>
/// <para><b>Forwarding never waits on capture (D3).</b> Each pump writes the bytes downstream before it tries
/// to enqueue the copy, the queue is bounded, and a full queue drops the copy and increments
/// <see cref="SegmentsDropped"/>. A decoder that throws is caught and counted; when the error is recoverable
/// (<see cref="TapProtocolException.Recoverable"/>) and <see cref="TcpTapOptions.ResyncAfterOverflow"/> is on
/// the decoder is reset and resumes at the next command boundary (<see cref="DecoderResets"/>), otherwise it is
/// switched off for that one connection (<see cref="DecodingDisabledConnections"/>) — the connection keeps
/// forwarding either way.</para>
/// <para><b>Capture loss is never silent.</b> Every skip, reset, give-up and drop is a counter, a
/// <see cref="TcpTapOptions.Log"/> line, a <see cref="TcpTapOptions.OnCaptureDegraded"/> event and an entry in
/// <see cref="Diagnostics"/> — the list a host hands to the ingest so it lands in the report.</para>
/// <para>The tap never learns a connection string or a password: it sees only the bytes on an already-open
/// socket, and the decoders drop authentication and handshake commands before anything reaches a sink.</para>
/// <para>Use <see cref="RedisTap"/> or <see cref="MongoTap"/> for the two decoders that ship with the package.</para>
/// </remarks>
public class TcpTap : IAsyncDisposable
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name taps emit spans on (see <see cref="TcpTapOptions.EmitActivities"/>).</summary>
    public const string ActivitySourceName = "Kronikol.TcpTap";

    /// <summary>The source the client spans are started on. Attach an OpenTelemetry listener to export them.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly TimeSpan DropReportInterval = TimeSpan.FromMinutes(1);

    private readonly TcpTapOptions _options;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _disposed;
    private int _live;
    private long _connections;
    private long _interactions;
    private long _decodeErrors;
    private long _droppedClientToServer;
    private long _droppedServerToClient;
    private long _bytesClientToServer;
    private long _bytesServerToClient;
    private long _nextConnectionId;

    private long _oversizePayloadsSkipped;
    private long _bytesSkipped;
    private long _largestOversizePayload;
    private long _decoderResets;
    private long _decodingDisabledConnections;
    private long _connectionsClosedMidMessage;
    private long _firstDisabledAtTicks;
    private string? _lastDisabledReason;
    private long _lastInteractionAtTicks;
    private long _bytesSinceLastInteraction;

    /// <summary>Creates a tap for the given options (validated on <see cref="StartAsync"/>).</summary>
    public TcpTap(TcpTapOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The options this tap runs with.</summary>
    public TcpTapOptions Options => _options;

    /// <summary>The port actually bound — the configured <see cref="TcpTapOptions.ListenPort"/>, or the ephemeral port chosen when it was 0. Zero until started.</summary>
    public int BoundPort { get; private set; }

    /// <summary>Whether the listener is accepting connections.</summary>
    public bool IsListening => _listener is not null && _disposed == 0;

    /// <summary>Connections accepted so far.</summary>
    public long ConnectionsAccepted => Interlocked.Read(ref _connections);

    /// <summary>Connections currently open through the tap.</summary>
    public int LiveConnections => Volatile.Read(ref _live);

    /// <summary>Command/reply exchanges recorded so far.</summary>
    public long InteractionsCaptured => Interlocked.Read(ref _interactions);

    /// <summary>
    /// Decode problems of any kind — a decoder reset, a give-up, a pending-queue overflow, a key that could not form a
    /// URI. The specific counters (<see cref="DecoderResets"/>, <see cref="DecodingDisabledConnections"/>, …) say which.
    /// </summary>
    public long DecodeErrors => Interlocked.Read(ref _decodeErrors);

    /// <summary>Byte segments dropped because a connection's decode queue was full. Non-zero means capture (never forwarding) lost data.</summary>
    public long SegmentsDropped => Interlocked.Read(ref _droppedClientToServer) + Interlocked.Read(ref _droppedServerToClient);

    /// <summary>Segments dropped on the service→server direction.</summary>
    public long SegmentsDroppedClientToServer => Interlocked.Read(ref _droppedClientToServer);

    /// <summary>Segments dropped on the server→service direction.</summary>
    public long SegmentsDroppedServerToClient => Interlocked.Read(ref _droppedServerToClient);

    /// <summary>Bytes forwarded from the service to the server.</summary>
    public long BytesClientToServer => Interlocked.Read(ref _bytesClientToServer);

    /// <summary>Bytes forwarded from the server to the service.</summary>
    public long BytesServerToClient => Interlocked.Read(ref _bytesServerToClient);

    /// <summary>Payloads longer than the capture cap that were streamed past instead of buffered. Their interactions were still recorded, as previews.</summary>
    public long OversizePayloadsSkipped => Interlocked.Read(ref _oversizePayloadsSkipped);

    /// <summary>Bytes of oversize payloads that were not kept (the previews are not counted).</summary>
    public long BytesSkipped => Interlocked.Read(ref _bytesSkipped);

    /// <summary>The declared length of the largest payload streamed past so far.</summary>
    public long LargestOversizePayload => Interlocked.Read(ref _largestOversizePayload);

    /// <summary>Times a decoder lost its place and was reset to resume at the next command boundary (see <see cref="TcpTapOptions.ResyncAfterOverflow"/>).</summary>
    public long DecoderResets => Interlocked.Read(ref _decoderResets);

    /// <summary>Connections whose decoding was abandoned for good — the number that must be zero for the diagrams to be complete.</summary>
    public long DecodingDisabledConnections => Interlocked.Read(ref _decodingDisabledConnections);

    /// <summary>Connections that closed with a command unanswered or a message only partly received; their last interaction(s) were not recorded.</summary>
    public long ConnectionsClosedMidMessage => Interlocked.Read(ref _connectionsClosedMidMessage);

    /// <summary>When the last interaction was recorded, or null when none has been.</summary>
    public DateTimeOffset? LastInteractionAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastInteractionAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Bytes forwarded (both directions) since the last recorded interaction — the stall detector's input.</summary>
    public long BytesSinceLastInteraction => Interlocked.Read(ref _bytesSinceLastInteraction);

    /// <summary>Starts listening. Throws if the options are invalid or the port cannot be bound.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        _options.Validate();

        var address = ResolveBindAddress(_options.ListenHost);
        var listener = new TcpListener(address, _options.ListenPort);
        listener.Start(_options.AcceptBacklog);
        _listener = listener;
        BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        _options.Log?.Invoke(
            $"[{_options.DisplayName}] tcp-tap listening on {_options.ListenHost}:{BoundPort} → {_options.ForwardHost}:{_options.ForwardPort}");
        return Task.CompletedTask;
    }

    private static IPAddress ResolveBindAddress(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.Loopback;
        if (host is "*" or "+")
            return IPAddress.Any;
        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? IPAddress.Loopback;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _connections);
            _ = Task.Run(() => HandleConnectionAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        Interlocked.Increment(ref _live);
        var id = Interlocked.Increment(ref _nextConnectionId);
        TcpClient? upstream = null;
        IProtocolDecoder? decoder = null;
        try
        {
            client.NoDelay = true;
            upstream = new TcpClient { NoDelay = true };

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(_options.ConnectTimeout);
                await upstream.ConnectAsync(_options.ForwardHost, _options.ForwardPort, connectCts.Token).ConfigureAwait(false);
            }

            var context = new TcpTapConnectionContext(this, id, client.Client.RemoteEndPoint as IPEndPoint);
            decoder = _options.DecoderFactory!(context);

            var channel = Channel.CreateBounded<TapSegment>(new BoundedChannelOptions(_options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

            var decodeTask = Task.Run(() => DecodeLoopAsync(channel.Reader, decoder, context), CancellationToken.None);

            var clientStream = client.GetStream();
            var upstreamStream = upstream.GetStream();
            var pumpState = new ConnectionPumpState(id);
            var toServer = PumpAsync(clientStream, upstreamStream, upstream.Client, TapDirection.ClientToServer, channel.Writer, pumpState, ct);
            var toClient = PumpAsync(upstreamStream, clientStream, client.Client, TapDirection.ServerToClient, channel.Writer, pumpState, ct);

            await Task.WhenAll(toServer, toClient).ConfigureAwait(false);
            channel.Writer.TryComplete();
            await decodeTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap connection {id} error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { decoder?.Dispose(); } catch (Exception ex) { _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decoder dispose failed: {ex.Message}"); }
            upstream?.Dispose();
            client.Dispose();
            Interlocked.Decrement(ref _live);
        }
    }

    /// <summary>
    /// Copies one direction. Bytes are written downstream <em>before</em> the copy is queued for decoding, and
    /// the queue write is non-blocking — the pump is never slowed by capture.
    /// </summary>
    private async Task PumpAsync(
        Stream source, Stream destination, Socket destinationSocket, TapDirection direction,
        ChannelWriter<TapSegment> writer, ConnectionPumpState state, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ReadBufferBytes);
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, _options.ReadBufferBytes), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
                {
                    break;
                }

                if (read == 0)
                    break;

                try
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
                {
                    break;
                }

                if (direction == TapDirection.ClientToServer)
                    Interlocked.Add(ref _bytesClientToServer, read);
                else
                    Interlocked.Add(ref _bytesServerToClient, read);
                Interlocked.Add(ref _bytesSinceLastInteraction, read);

                var copy = buffer.AsSpan(0, read).ToArray();
                if (!writer.TryWrite(new TapSegment(direction, copy, DateTimeOffset.UtcNow)))
                    CountDrop(direction, state);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            // Half-close: tell the far side this direction is done, but keep the other one pumping.
            try { destinationSocket.Shutdown(SocketShutdown.Send); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { /* already gone */ }
        }
    }

    private void CountDrop(TapDirection direction, ConnectionPumpState state)
    {
        var total = direction == TapDirection.ClientToServer
            ? Interlocked.Increment(ref _droppedClientToServer)
            : Interlocked.Increment(ref _droppedServerToClient);
        if (total == 1)
            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decode queue full ({direction}) — capture is dropping segments; forwarding is unaffected.");

        // The hot path: tell the host at most once per connection per minute.
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref state.LastDropReportTicks);
        if (last != 0 && now - last < DropReportInterval.TotalMilliseconds)
            return;
        if (Interlocked.CompareExchange(ref state.LastDropReportTicks, now, last) == last)
            Degrade(state.ConnectionId, CaptureDegradationKind.SegmentsDropped,
                $"decode queue full ({direction}) — segments dropped; interactions on this connection may be missing or mis-paired");
    }

    private async Task DecodeLoopAsync(ChannelReader<TapSegment> reader, IProtocolDecoder decoder, TcpTapConnectionContext context)
    {
        var decoding = true;
        try
        {
            await foreach (var segment in reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (!decoding)
                    continue; // keep draining so the pumps never see a full queue

                try
                {
                    if (segment.Direction == TapDirection.ClientToServer)
                        decoder.OnClientToServer(segment.Data, segment.Timestamp);
                    else
                        decoder.OnServerToClient(segment.Data, segment.Timestamp);
                }
                catch (Exception ex)
                {
                    decoding = HandleDecodeError(decoder, context, ex);
                }
            }

            if (decoding)
                decoder.OnConnectionClosed();
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _decodeErrors);
            DisableDecoding(context.ConnectionId, ex, "decode loop error");
        }
    }

    /// <summary>
    /// The policy for a decoder exception: a recoverable protocol error with <see cref="TcpTapOptions.ResyncAfterOverflow"/>
    /// on resets the decoder and decoding continues (true); anything else disables decoding for the connection (false).
    /// Every outcome is counted, logged and reported through <see cref="TcpTapOptions.OnCaptureDegraded"/>.
    /// </summary>
    internal bool HandleDecodeError(IProtocolDecoder decoder, TcpTapConnectionContext context, Exception ex)
    {
        Interlocked.Increment(ref _decodeErrors);
        if (ex is TapProtocolException { Recoverable: true } && _options.ResyncAfterOverflow && TryReset(decoder, context))
        {
            Interlocked.Increment(ref _decoderResets);
            var detail = $"{ex.GetType().Name}: {ex.Message}";
            _options.Log?.Invoke(
                $"[{_options.DisplayName}] tcp-tap decoder reset on connection {context.ConnectionId} ({detail}); " +
                "resuming at the next command boundary — the first interaction after it is stamped \"pairing uncertain\". Forwarding continues untouched.");
            Degrade(context.ConnectionId, CaptureDegradationKind.DecoderReset, detail);
            return true;
        }

        DisableDecoding(context.ConnectionId, ex, "decoder gave up");
        return false;
    }

    private bool TryReset(IProtocolDecoder decoder, TcpTapConnectionContext context)
    {
        try
        {
            return decoder.TryReset();
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decoder reset threw on connection {context.ConnectionId} ({ex.GetType().Name}: {ex.Message}).");
            return false;
        }
    }

    private void DisableDecoding(long connectionId, Exception ex, string what)
    {
        Interlocked.Increment(ref _decodingDisabledConnections);
        var detail = $"{ex.GetType().Name}: {ex.Message}";
        var now = DateTimeOffset.UtcNow;
        Interlocked.CompareExchange(ref _firstDisabledAtTicks, now.UtcTicks, 0);
        Volatile.Write(ref _lastDisabledReason, detail);
        _options.Log?.Invoke(
            $"[{_options.DisplayName}] tcp-tap {what} on connection {connectionId} ({detail}); " +
            "nothing on this connection is decoded from now on — forwarding continues untouched.");
        Degrade(connectionId, CaptureDegradationKind.DecodingDisabled, $"{detail} — arrows on this connection after {now:HH:mm:ss}Z are missing");
    }

    internal void Degrade(long connectionId, CaptureDegradationKind kind, string detail)
    {
        var callback = _options.OnCaptureDegraded;
        if (callback is null)
            return;
        try
        {
            callback(new CaptureDegradation(_options.DisplayName, connectionId, kind, detail));
        }
        catch (Exception ex)
        {
            _options.Log?.Invoke($"[{_options.DisplayName}] OnCaptureDegraded threw ({ex.GetType().Name}: {ex.Message}); ignored.");
        }
    }

    internal void CountOversizePayload(long connectionId, long declaredLength, int keptBytes)
    {
        Interlocked.Increment(ref _oversizePayloadsSkipped);
        Interlocked.Add(ref _bytesSkipped, declaredLength - keptBytes);
        long seen;
        while ((seen = Interlocked.Read(ref _largestOversizePayload)) < declaredLength &&
               Interlocked.CompareExchange(ref _largestOversizePayload, declaredLength, seen) != seen)
        {
        }

        Degrade(connectionId, CaptureDegradationKind.OversizePayloadSkipped,
            $"payload of {declaredLength.ToString("N0", CultureInfo.InvariantCulture)} B streamed past (over the capture cap); " +
            $"{keptBytes.ToString("N0", CultureInfo.InvariantCulture)} B kept as a preview");
    }

    internal void CountClosedMidMessage(long connectionId, string detail)
    {
        Interlocked.Increment(ref _connectionsClosedMidMessage);
        _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap connection {connectionId} closed mid-message ({detail}); its last interaction(s) were not recorded.");
        Degrade(connectionId, CaptureDegradationKind.ConnectionClosedMidMessage, detail);
    }

    /// <summary>
    /// Capture health as report diagnostics: one <see cref="DiagnosticKind.CaptureDegraded"/> entry per non-zero
    /// counter — connections whose decoding was disabled (and since when), oversize payloads streamed past, decoder
    /// resets, dropped segments, connections closed mid-message — plus a heuristic "decoding may have stalled" entry
    /// when bytes keep flowing without an interaction being recorded (<see cref="TcpTapOptions.DecodingStallBytes"/>).
    /// Empty when capture was complete. A host hands the list to <c>IngestRequest.HostDiagnostics</c> so it appears
    /// in <c>IngestResult.Diagnostics</c> and the report.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics()
    {
        var name = _options.DisplayName;
        var entries = new List<DiagnosticEntry>();

        var disabled = DecodingDisabledConnections;
        if (disabled > 0)
        {
            var firstTicks = Interlocked.Read(ref _firstDisabledAtTicks);
            var since = firstTicks == 0 ? "" : $" after {new DateTimeOffset(firstTicks, TimeSpan.Zero):HH:mm:ss}Z";
            var reason = Volatile.Read(ref _lastDisabledReason);
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: decoding disabled on {disabled:N0} connection(s) — {_options.ServiceName} arrows on them{since} are missing" +
                (reason is null ? "" : $" ({reason})")));
        }

        var oversize = OversizePayloadsSkipped;
        if (oversize > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {oversize:N0} oversize payload(s) streamed past (largest {LargestOversizePayload:N0} B, {BytesSkipped:N0} B not kept) — values recorded as previews"));

        var resets = DecoderResets;
        if (resets > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: decoder reset {resets:N0} time(s) after a desynchronised stream — interactions after a reset may be mis-paired until the connection goes idle; the first is stamped \"resynchronised — pairing uncertain\""));

        var dropped = SegmentsDropped;
        if (dropped > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {dropped:N0} segment(s) dropped because the decode queue was full ({SegmentsDroppedClientToServer:N0} service→server, {SegmentsDroppedServerToClient:N0} server→service) — interactions may be missing or mis-paired; forwarding was unaffected"));

        var closedMid = ConnectionsClosedMidMessage;
        if (closedMid > 0)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {closedMid:N0} connection(s) closed mid-message — their last command(s) were not recorded"));

        if (_options.DecodingStallBytes is { } stallBytes && BytesSinceLastInteraction >= stallBytes)
        {
            var last = LastInteractionAt;
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded,
                $"{name}: {BytesSinceLastInteraction:N0} B have flowed since the last recorded interaction" +
                (last is null ? " (none recorded yet)" : $" at {last.Value:HH:mm:ss}Z") +
                " — decoding may have stalled, or the traffic is all excluded chatter"));
        }

        return entries;
    }

    // ------------------------------------------------------------------ capture

    /// <summary>
    /// Records one decoded exchange as a Kronikol request/response pair. Called by decoders; safe to call
    /// concurrently.
    /// </summary>
    public void Record(TapInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var identity = ResolveIdentity();
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        string? activityTraceId = null;
        string? activitySpanId = null;
        if (_options.EmitActivities)
        {
            using var activity = ActivitySource.StartActivity(
                $"{_options.ServiceName} {Describe(interaction.Method)}", ActivityKind.Client,
                parentContext: default, tags: null, links: null, startTime: interaction.RequestTimestamp);
            if (activity is not null)
            {
                activity.SetTag("kronikol.caller", _options.CallerName);
                activity.SetTag("kronikol.service", _options.ServiceName);
                activity.SetTag("kronikol.test.id", identity.Id);
                activity.SetTag("db.system", _options.DependencyCategory);
                activity.SetTag("url.full", interaction.Uri.ToString());
                activity.SetEndTime(interaction.ResponseTimestamp.UtcDateTime);
                activityTraceId = activity.TraceId.ToString();
                activitySpanId = activity.SpanId.ToString();
            }
        }

        var requestContent = Cap(interaction.RequestContent);
        var responseContent = _options.CaptureReplies ? Cap(interaction.ResponseContent) : null;
        var headers = interaction.Headers ?? [];

        _options.Sink.Log(new RequestResponseLog(
            identity.Name, identity.Id,
            interaction.RequestMethod ?? interaction.Method,
            requestContent, interaction.Uri, headers,
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: _options.DependencyCategory,
            CallerDependencyCategory: _options.CallerDependencyCategory)
        {
            Timestamp = interaction.RequestTimestamp,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
            Phase = _options.Phase,
        });

        _options.Sink.Log(new RequestResponseLog(
            identity.Name, identity.Id,
            interaction.Method,
            responseContent, interaction.Uri, headers,
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            interaction.StatusCode,
            DependencyCategory: _options.DependencyCategory,
            CallerDependencyCategory: _options.CallerDependencyCategory)
        {
            Timestamp = interaction.ResponseTimestamp,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
            Phase = _options.Phase,
        });

        Interlocked.Increment(ref _interactions);
        Interlocked.Exchange(ref _lastInteractionAtTicks, DateTimeOffset.UtcNow.UtcTicks);
        Interlocked.Exchange(ref _bytesSinceLastInteraction, 0);
    }

    private static string Describe(OneOf<HttpMethod, string> method) =>
        method.Value?.ToString() ?? "call";

    private (string Name, string Id) ResolveIdentity()
    {
        if (_options.IdentityResolver is not null)
        {
            try
            {
                var resolved = _options.IdentityResolver();
                if (resolved is not null)
                    return resolved.Value;
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap identity resolver threw ({ex.Message}); using the fallback identity.");
            }
        }

        return (_options.FallbackTestName, _options.FallbackTestId);
    }

    internal string? Cap(string? text)
    {
        if (text is null)
            return null;
        if (_options.BodyCapBytes is { } cap && text.Length > cap)
            return $"{text[..cap]}\n\n…truncated ({text.Length} chars total)";
        return text;
    }

    internal void CountDecodeError() => Interlocked.Increment(ref _decodeErrors);

    internal void Diagnostic(string message) => _options.Log?.Invoke($"[{_options.DisplayName}] {message}");

    /// <summary>Stops listening, briefly drains live connections so their captures land, and releases the socket.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        try { _listener?.Stop(); }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException) { /* already stopped */ }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        var deadline = (int)Math.Max(0, _options.DrainTimeout.TotalMilliseconds);
        for (var waited = 0; Volatile.Read(ref _live) > 0 && waited < deadline; waited += 25)
            await Task.Delay(25).ConfigureAwait(false);

        _cts?.Dispose();
        _options.Log?.Invoke(
            $"[{_options.DisplayName}] tcp-tap stopped ({ConnectionsAccepted} connections, {InteractionsCaptured} interactions, " +
            $"{SegmentsDropped} segments dropped, {DecodeErrors} decode errors, {OversizePayloadsSkipped} oversize payloads skipped, " +
            $"{DecoderResets} decoder resets, {DecodingDisabledConnections} connections with decoding disabled)");
        GC.SuppressFinalize(this);
    }

    private readonly record struct TapSegment(TapDirection Direction, byte[] Data, DateTimeOffset Timestamp);

    private sealed class ConnectionPumpState(long connectionId)
    {
        public long ConnectionId { get; } = connectionId;

        public long LastDropReportTicks;
    }
}

/// <summary>What a decoder is given about the connection it is decoding, and how it reports what it finds.</summary>
public sealed class TcpTapConnectionContext
{
    private readonly TcpTap _tap;

    internal TcpTapConnectionContext(TcpTap tap, long connectionId, IPEndPoint? remoteEndPoint)
    {
        _tap = tap;
        ConnectionId = connectionId;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>Sequence number of this connection within the tap (1-based).</summary>
    public long ConnectionId { get; }

    /// <summary>The address the tapped service dialled from, when known.</summary>
    public IPEndPoint? RemoteEndPoint { get; }

    /// <summary>The tap's options.</summary>
    public TcpTapOptions Options => _tap.Options;

    /// <summary>Records one decoded exchange.</summary>
    public void Record(TapInteraction interaction) => _tap.Record(interaction);

    /// <summary>Writes a diagnostic line through the tap's <see cref="TcpTapOptions.Log"/>.</summary>
    public void Log(string message) => _tap.Diagnostic($"connection {ConnectionId}: {message}");

    /// <summary>Counts a recoverable decode problem the decoder chose to swallow rather than give up on.</summary>
    public void CountDecodeError() => _tap.CountDecodeError();

    /// <summary>Counts a payload the decoder streamed past because it was longer than the capture cap (the interaction is still recorded, as a preview).</summary>
    public void CountOversizePayload(long declaredLength, int keptBytes) => _tap.CountOversizePayload(ConnectionId, declaredLength, keptBytes);

    /// <summary>Reports that the connection closed with a command unanswered or a message only partly received.</summary>
    public void ReportClosedMidMessage(string detail) => _tap.CountClosedMidMessage(ConnectionId, detail);

    /// <summary>Reports any other capture-loss event on this connection through <see cref="TcpTapOptions.OnCaptureDegraded"/>.</summary>
    public void ReportDegraded(CaptureDegradationKind kind, string detail) => _tap.Degrade(ConnectionId, kind, detail);
}
