# A tapped connection that never reaches its server (#174)

Written on 2026-10-14 at 4.20.2 (`origin/main` at `3240c983`) by kronikol-71, in the worktree `C:/Code/Kronikol-174`
(branch `plan/174-tcptap-failed-connect`). **Green-lit in full by the owner on 2026-10-15** ("implement the plan in
full"), with every question in §9 answered as recommended. §11 records the execution. Line numbers are at `3240c983`,
and Appendix A lists every edit site.

Every RUN fact was measured against the published 4.20.2 packages, on Windows 11 26200 with .NET 10.0.11 and on Ubuntu
24.04.5 in the `mcr.microsoft.com/dotnet/sdk:10.0` container with .NET 10.0.12. The probes and their outputs are in
`TCPTAP_FAILED_CONNECT_PLAN.harness/`. A READ fact comes from reading code or documents and was not measured.

#174 was filed on 2026-10-13 from `PROXY_TAP_HANG_UP_PLAN.md` (§8 and Q7), whose author, kronikol-a5, confirmed its
provenance for this plan. The sibling issue #173 (no time limit on the caller's side) is being planned at the same time
by kronikol-51 in `plans/TAP_CALLER_TIME_LIMITS_PLAN.md`. §4.4 is the split the two sessions agreed.

## 0. Summary

**The issue is right, and on Windows it understates the problem.** On both platforms, a `TcpTap` upstream connect
that runs past `ConnectTimeout` says nothing at all. The timeout's `OperationCanceledException` lands in the catch kept
for the tap stopping (`TcpTap.cs:247-250`). A refused connect is logged and never counted. On Windows, though, a refusal
takes about 2 s per address, because Windows resends the SYN after an RST. Under a `ConnectTimeout` shorter than that, a
refusal arrives as a timeout and is just as silent. In none of the seven failure cases measured does a failure reach a
counter, an `OnCaptureDegraded` event or `Diagnostics()` (§2.2).

**The underlying problem is wider than one catch.** TcpTap's class documentation promises that "capture loss is never
silent": every skip, reset, give-up and drop is "a counter, a Log line, an OnCaptureDegraded event and an entry in
Diagnostics" (`TcpTap.cs:28-30`; the wiki says the same). That promise covers what happens once both sockets are open.
It does not cover the three steps before that:

- **Accepting the caller.** One failed accept ends the accept loop for good (§3.1).
- **Connecting upstream.** A failure here is silent, or at most one log line (§2).
- **Building the decoder.** A `DecoderFactory` that throws closes a connection whose server is healthy (§3.3). That
  breaks the tap's first rule, that forwarding never waits on capture.

**The worst of these is not in the issue.** On Linux, a single accept that fails because the process is out of file
descriptors (EMFILE) stops `TcpTap` from accepting for good. `OtlpTap`'s IPv4 listener has the same loop
(`OtlpTap.cs:268`) and stops the same way. Both still report `IsListening`, neither logs a line, and callers queue
without being served. The runtime itself recovers: a plain loop that keeps going accepts again as soon as descriptors
come back (§3.1), so the `return` in the taps' loops is the defect. `ProxyTap` is not affected, because its
`HttpListener` re-arms its own accept. It takes descriptor exhaustion, or some other failed accept, to trigger this. That
is rare, but when it happens the tap is dead and nothing says so.

**The plan.**

- **R1, a patch:** both accept loops keep accepting after a failed accept, with a pause, a count, a log line and a
  `Diagnostics()` line.
- **R2, a minor, the ask:** a failed upstream connect is sorted by what caused it, using the tap's own tokens. It is then
  counted, logged, raised through `OnCaptureDegraded` and reported by `Diagnostics()`. A throwing `DecoderFactory`
  forwards the connection undecoded. The documents that promise more than the code keeps are corrected. R2 is a minor
  because it adds public members: `TcpTap.UpstreamConnectFailures`, `AcceptFailures` on both taps, and two
  `CaptureDegradationKind` members.
- **R3, a patch, the owner's call:** on Windows, "localhost" costs 2 s per connection on both sides of a `TcpTap`
  (§3.4).

No release adds an option.

## 1. How far each claim was checked

| # | The issue says | Verdict | Evidence |
|---|---|---|---|
| 1 | A connect that times out throws `OperationCanceledException`, which lands in the catch meant for the tap shutting down: no log line and no count | **Confirmed on both platforms** | RUN: `connect/` B1 and B2 log nothing and count nothing. G shows that the timeout throws `System.OperationCanceledException` itself, not a subclass |
| 2 | A refused connect (`SocketException`) is logged and not counted | **Confirmed on Linux. On Windows it holds only when the refusal arrives inside `ConnectTimeout`** | RUN: on Linux the refusal is immediate and logged (A1 to A3). On Windows it takes about 2 s per address. With `ConnectTimeout` 1.5 s it never arrives and nothing is logged (A1, A2). With 5 s and `localhost` it arrives after 4,069 ms and is logged (A3). §2.3 |
| 3 | Neither reaches `TcpTap.Diagnostics()` | **Confirmed** | RUN: every failure case on both platforms: `Diagnostics()` empty, no `OnCaptureDegraded` event, `ConnectionsAccepted` 1 |
| 4 | The caller's connection is closed in both cases | **Confirmed** | RUN: once the connect gives up, the caller reads a reset on Windows and an end of stream on Linux (§2.2) |
| 5 | Tell a connect timeout from the tap stopping by the tap's own token, not by the exception's type | **Right, and the only way** | RUN: G2. The tap's own token, cancelled mid-connect through the linked source, throws the same `System.OperationCanceledException`. Only the tokens tell the two apart. `ProxyTap` already classifies this way (`ForwardFailure.cs:31-44`) |
| 6 | Report them as `CaptureDegraded`, as `ProxyTap` reports forward failures | **Right; one doc comment has to change** | READ: `ProxyTap.cs:113-116`. `DiagnosticKind.CaptureDegraded`'s summary says forwarding "was never affected" (`DiagnosticEntry.cs:41-46`), which is already untrue of the forward-failure lines in `ProxyTap` and `OtlpTap` (§3.5) |
| 7 | The cited lines (212-215, 247-250, 251-254) | **Still correct** | READ: `TcpTap.cs` has changed since #174 was filed only at L645 (4.19.6), as kronikol-a5 noted |

## 2. Where it stands today

### 2.1 The code (READ)

`AcceptLoopAsync` (`TcpTap.cs:181-199`) counts each accepted connection (L196) and hands it to
`HandleConnectionAsync` (L201-262). That method counts the connection as live (L203), creates a dual-mode `TcpClient`
for the upstream (L210) and connects it to `ForwardHost:ForwardPort`. The connect runs under a token linked to the tap's
lifetime and cancelled after `ConnectTimeout` (L212-216; the default is 5 s, `TcpTapOptions.cs:104-105`).
`TcpClient.ConnectAsync(string, int, CancellationToken)` resolves the name and tries each address in turn.

Only after the connect does it call the `DecoderFactory` (L219). Everything sits in one `try`, with two catches:

- `catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)` with the comment
  `// Shutting down.` (L247-250). A connect timeout lands here.
- A catch-all that logs `tcp-tap connection {id} error: {type}: {message}` (L251-254). A refusal, a name that does not
  resolve and a throwing factory land here.

The `finally` disposes both sockets (L255-261). `Diagnostics()` (L529-580) has one line per counter and none about
connecting. The stop line (L715-718) names no connect failures either.

The accept loop itself returns on `OperationCanceledException`, `ObjectDisposedException` **or `SocketException`**
(L191-194). `OtlpTap`'s loop (`OtlpTap.cs:259-275`) returns on those and on `InvalidOperationException` (L268-271). It
runs one loop per listener, and a loopback host gets two listeners (`OtlpTap.cs:219-227`).

### 2.2 The measurements (RUN, `connect/connect-probe.cs`)

Each case starts a Redis tap, connects a client, sends a PING and reads for up to 10 s. `ConnectTimeout` is 1.5 s
unless the case names another value.

| Case | Windows: the tap's log, what the caller heard | Linux: the same |
|---|---|---|
| A1 refused, `127.0.0.1` | nothing logged; a reset after 1,524 ms | `SocketException: Connection refused`; an end of stream after 20 ms |
| A2 refused, `localhost` | nothing logged; a reset after 1,499 ms | the same line; an end of stream after 3 ms |
| A3 refused, `localhost`, 5 s (the default) | `SocketException: No connection could be made because the target machine actively refused it.`; a reset after 4,069 ms | the same as A2, at once |
| B1 SYN unanswered (a listener whose queue is full) | nothing logged; a reset after 1,504 ms | nothing logged; an end of stream after 1,500 ms |
| B2 SYN unanswered (`192.0.2.1`, TEST-NET-1) | nothing logged; a reset after 1,510 ms | nothing logged; an end of stream after 1,501 ms |
| C a host name that does not resolve | `SocketException: No such host is known.`; a reset after 36 ms | `SocketException: Name or service not known`; an end of stream after 16 ms |
| D the `DecoderFactory` throws, upstream healthy | `InvalidOperationException: factory failed`; a reset after 1 ms | the same; an end of stream after 1 ms |
| D0 control: the Redis decoder, upstream healthy | `+PONG` after 13 ms | `+PONG` after 11 ms |
| E the tap disposed 700 ms into a waiting connect | nothing logged (right); a reset after 783 ms | nothing logged; an end of stream after 730 ms |

Every case but the control, on both platforms, leaves `ConnectionsAccepted` at 1 and `LiveConnections` at 0, raises
no `OnCaptureDegraded` event and leaves `Diagnostics()` empty. That is right for E, where the tap was stopping, and is
the defect in the seven failure cases. The caller had already sent its PING, so Windows answers the close with a
reset. Linux shows an end of stream, because `TcpClient.Dispose()` shuts the socket down before
closing it (harness README).

### 2.3 What the runtime throws (RUN, `connect/` G and G2)

This is `TcpClient.ConnectAsync(host, port, token)` alone, with `CancelAfter(1.5 s)`, called as the tap calls it:

- **A SYN that is never answered** throws `System.OperationCanceledException` after 1,500 to 1,513 ms on both
  platforms, with the token cancelled.
- **A refusal** throws `SocketException` `ConnectionRefused` (111) at once on Linux. On Windows the connect is still
  waiting at 1.5 s, so it throws `OperationCanceledException`.
- **A name that does not resolve** throws `SocketException` `HostNotFound` (11001 on Windows after 15 ms; -131073,
  "Name or service not known", on Linux after 12 ms).
- **G2:** cancelling the caller's own token at 700 ms, through a linked source as the tap builds it, throws the same
  `System.OperationCanceledException`.

So the exception's type says that some token fired, never which one. The tap holds both tokens and can ask them.

Windows does not take an RST in answer to a SYN as final. It resends the SYN, as the `TcpMaxConnectRetransmissions`
setting allows, and gives up after about 2 s on loopback. (READ:
[curl's author on slow TCP connects on Windows](https://daniel.haxx.se/blog/2024/08/14/slow-tcp-connect-on-windows/);
measured here at about 2 s per address.)

The consequence: on Windows, "refused" and "timed out" are not two causes the tap can name reliably. With `localhost`
(two addresses) a refusal takes about 4 s, inside the default 5 s, and arrives as a refusal. Under a `ConnectTimeout` of
less than about 2 s per address it arrives as a timeout. R2's wording therefore says "no connection within
`ConnectTimeout`" and never "the server did not answer".

## 3. Findings the issue does not state

### 3.1 One failed accept stops the tap for good (RUN on Linux; `TcpTap` and `OtlpTap`)

`accept/accept-probe.cs` ran the two taps and `ProxyTap` side by side:

- **E0 and E1, resets.** 1,000 clients that connect and reset at once against a bare listener, and 15 rounds of 200
  against each tap. No accept threw, on either platform, and every tap answered in every round. Kestrel's socket
  listener treats a `SocketException` from accept as "the connection got reset while it was in the backlog, so we try
  again" (READ: `dotnet/aspnetcore` `src/Servers/Kestrel/Transport.Sockets/src/SocketConnectionListener.cs`, the
  `catch (SocketException)` in `AcceptAsync`). That was not reproduced here.
- **E2, the process out of descriptors (Linux).** `setrlimit` lowered the soft `RLIMIT_NOFILE` to 152, `/dev/null`
  handles took the rest, and four clients connected while nothing was free. The kernel completed every handshake. A
  bare listener's loop with no pause threw `SocketException` `TooManyOpenSockets` 45,839 times in that second (48,207
  in an earlier run). Once the descriptors were given back, it accepted the queued client within 500 ms, and then a new
  one.
- **The taps.** `TcpTap` and `OtlpTap` never answered again: three tries, then three more 5 s later. Both still said
  `IsListening`, and neither logged a line. `TcpTap`'s loop task was `RanToCompletion`. `OtlpTap`'s 127.0.0.1 listener
  answered nothing, while its task over both loops was still waiting on the `::1` listener's loop. `ProxyTap` answered
  throughout.

**What a caller sees.** The listening socket stays open, so a caller's connect still completes in the kernel. Its
command then waits for an answer that never comes, until the client's own timeout. (READ, and seen in E2: the probe's
clients connected while nothing could accept them.) The report gets nothing: no counter, no event, no log line, no
diagnostic.

**How likely.** It needs a failed accept. In the `sdk:10.0` container the soft descriptor limit was 1,048,576, so
there it takes a leak. A lower limit, from a shell or a service manager, takes less. kronikol-51 notes that
connections #173 leaves held open hold descriptors, which is one road to EMFILE. The other errors `accept(2)` lists
(ENFILE, ENOBUFS, ENOMEM, EPERM, ECONNABORTED) take the same path (READ, not measured; there is no macOS machine here).

### 3.2 On Windows a refusal is slow, so the two causes blur (RUN)

Covered in §2.3. For the design it means two things. Timeouts and refusals go into one count, with the split only in
the words. And the words must hold on Windows.

### 3.3 A `DecoderFactory` that throws closes a healthy connection (RUN)

Case D: with the upstream answering normally, both platforms close the caller within 1 ms. The tap logs `connection 1
error: InvalidOperationException` and counts nothing.

The class documentation says a decoder that throws is caught and counted and that "the connection keeps forwarding
either way" (`TcpTap.cs:21-27`). The factory is the one decoder failure that does not keep it forwarding. It is user
code whenever a host tees its own protocol (`TcpTapOptions.cs:181-182`, `AddTcpTapTestTracking`). The fix needs no new
surface: `DisableDecoding` already counts a connection whose decoding is off, logs it, raises `DecodingDisabled` and
reports it in `Diagnostics()`.

### 3.4 "localhost" costs 2 s per connection on Windows, on both sides of a `TcpTap` (RUN, `connect/` F)

- **The listening side (F1).** `ListenHost` defaults to "localhost", and `ResolveBindAddress` maps it to the IPv4
  loopback only (`TcpTap.cs:171-172`). Windows resolves "localhost" to `::1` first, then `127.0.0.1`. A .NET client
  that dialled the tap at "localhost" took 2,020 to 2,044 ms on every connect, against 0 ms by IP. `OtlpTap`, added in
  the same commit (`f46ebdb6`, 2026-08-22), binds both loopbacks for exactly this reason (its comment at
  `OtlpTap.cs:187-192`).
- **The forwarding side (F2).** `ForwardHost` also defaults to "localhost" (`TcpTapOptions.cs:37-38`). With a server
  listening on 127.0.0.1 only, the first reply through the tap took 2,038 to 2,042 ms on every connection, against 0 to
  1 ms with "127.0.0.1". Each of those connections includes a refused `::1` attempt that the tap never reports.
- **Linux** shows 0 ms on both sides. In the container, "localhost" resolves to 127.0.0.1 only, and Linux answers a
  refused `::1` at once anyway.

This is not the issue's subject, but it is the same path with the same blindness.

### 3.5 The documents promise more than the code keeps (READ)

- **`CaptureDegradation.cs:3`** says "Forwarding is never affected by any of them". `StuckConnectionReaped` (L21-29)
  closes connections, and R2's `UpstreamConnectFailed` is a connection that was never forwarded.
- **`DiagnosticEntry.cs:41-46`** says `CaptureDegraded` means forwarding "was never affected". The forward-failure
  lines of `ProxyTap` and `OtlpTap` already use that kind.
- **`TcpTapOptions.cs:136-143`** says `OnCaptureDegraded` is "Invoked on the decoder task (never on a byte pump ...)".
  The reap event comes from the reaper's task (`TcpTap.cs:237-239`, 366). R2's connect events would come from the
  connection's task.
- **`TcpTapOptions.cs:58`** says the `Log` callback gets "connection errors". A timed-out connect is not logged.
- **The wiki's TcpTap page** (`Integration-TcpTap-Extension.md` at `60daf39`) never mentions the stuck-connection
  reaper: not its option `ReapStuckConnectionsAfter` (public since `398655f4`, 2026-08-26), not its counter
  `StuckConnectionsReaped`, and not its event kind. The kinds list at L304 names five of the six. CLAUDE.md asks that
  the wiki follow the public API.

## 4. The design

### 4.0 One rule for the whole path

A step before both sockets are open ends in one of three ways, and the tap tells them apart by its own tokens, never by
the exception's type:

1. **The tap is stopping.** Its lifetime token, `ct`, is cancelled. Nothing is counted or logged, as today; case E
   already behaves this way.
2. **The step ran out of time.** Its own deadline fired.
3. **The step failed.** Anything else.

The last two are counted, logged, raised through `OnCaptureDegraded` and reported by `Diagnostics()`. A failure ends
only its own connection, and nothing ends the accept loop while the tap runs. This is how `ProxyTap` sorts a failed
forward (`ForwardFailure.Classify`, `ForwardFailure.cs:31-44`), applied to the steps before forwarding.

### 4.1 R1 (patch): both accept loops keep accepting

The edit sites are `TcpTap.cs:181-199` and `OtlpTap.cs:259-275`.

- **When the loop ends.** Only when `ct.IsCancellationRequested`, or when its listener is gone
  (`ObjectDisposedException`; for `OtlpTap`, the `InvalidOperationException` of a listener that has stopped). Nothing
  in Kronikol stops a listener while the tap runs, so a listener that is gone while the tap is not stopping is logged as
  "stopped accepting", never passed over in silence.
- **Any other exception.** It is counted: an internal `_acceptFailures`, the first one's time and message. The first is
  logged at once, then at most one line a minute with the count since, as `CountDrop` reports dropped segments
  (`TcpTap.cs:385-392`).
- **The pause.** The loop pauses and tries again. The pause starts at 10 ms, doubles up to 1 s, and resets after an
  accept succeeds. Without it the loop spins: 45,839 to 48,207 failed accepts a second in E2. With the 1 s cap, a
  caller queued during the outage is accepted at most about a second after the cause clears. The pause waits on the
  tap's token, so disposing the tap never waits for it. The reaper's 1 s tick (`TcpTap.cs:340`) cannot be cancelled,
  and kronikol-51 measured it adding 540 ms on Windows and 954 ms on Linux to `DisposeAsync`
  (`TAP_CALLER_TIME_LIMITS_PLAN.harness/probe/README.md`; that tick is #173's).
- **Recovery.** When accepting resumes after failures, one line says so: `accepting again after N failed accept(s)
  since HH:mm:ssZ`.
- **`Diagnostics()`, both taps.** It gains a `CaptureDegraded` line when any accept failed: `{name}: {n:N0} accept(s)
  failed, the first at HH:mm:ssZ ({exception type} {SocketError}: {message}); the tap kept listening and tried again,
  so callers that arrived meanwhile waited in the listen queue, or found it full`.
- **The stop line.** `TcpTap`'s stop line (`TcpTap.cs:715-718`) adds the count.
- **Nothing in the failure path can throw.** Every `Log` call the loop makes goes through one guarded helper,
  `private void TryLog(string message)` in each tap, which swallows whatever the host's callback throws (§4.4 says who
  adds it). Otherwise the new log lines would end the loop
  exactly as the `return` does today. A host's `Log` callback can throw: one that writes to xUnit's `ITestOutputHelper`
  after its test has ended gets an `InvalidOperationException`. (READ; kronikol-51 found every `Log` call in `TcpTap`
  unguarded at 3240c983.) In R2 the same holds for the `AcceptFailed` event. `Degrade` already guards the callback, but
  its own fallback `Log` call at `TcpTap.cs:495` is unguarded.
- **A seam for the facts.** An `internal Func<TcpListener, CancellationToken, ValueTask<TcpClient>> AcceptClient` on
  each tap, defaulting to `AcceptTcpClientAsync`. A fact makes it throw a `SocketException` a set number of times. It
  follows `ProxyTap.WrapRequestBody` and `OtlpTap.BeforeQueued`, and is not public.

R1 adds no public member and no option, so it is a patch. Its new log and `Diagnostics()` lines are new output from
existing members. **Behaviour change:** a tap whose accept fails keeps accepting, where it used to go quiet.

### 4.2 R2 (minor): a connection the tap could not forward is counted (the ask)

The edit sites are `TcpTap.cs:201-254` up to the pumps, `529-580` and `715-718`, plus `CaptureDegradation.cs`, the
doc comments in `TcpTapOptions.cs` and `DiagnosticEntry.cs:41-46`.

**1. The connect is sorted by §4.0.** In outline:

```csharp
using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
connectCts.CancelAfter(_options.ConnectTimeout);
try
{
    await upstream.ConnectAsync(_options.ForwardHost, _options.ForwardPort, connectCts.Token).ConfigureAwait(false);
}
catch (Exception) when (ct.IsCancellationRequested)
{
    return; // the tap is stopping: not the upstream's doing
}
catch (OperationCanceledException) when (connectCts.IsCancellationRequested)
{
    CountConnectFailure(id, caller, timedOut: true, exception: null);
    return;
}
catch (Exception ex)
{
    CountConnectFailure(id, caller, timedOut: false, ex);
    return;
}
```

Each `return` goes through the existing `finally`, which closes both sockets as it does today.

**2. `CountConnectFailure`.** It adds one to `_upstreamConnectFailures` (and to `_upstreamConnectTimeouts` for a
timeout), and keeps the first failure's time and the last one's reason. It logs one of two lines:

- `[{name}] tcp-tap connection {id} from {caller}: could not connect to {ForwardHost}:{ForwardPort} within
  ConnectTimeout ({t}); closed it without forwarding anything`
- the same with `({type} {SocketError}: {message})` in place of the `ConnectTimeout` clause

Then it raises `Degrade(id, CaptureDegradationKind.UpstreamConnectFailed, detail)`.

**3. New public surface,** the reason R2 is a minor (Q1, Q2):

- `TcpTap.UpstreamConnectFailures`: connections the tap closed without forwarding anything because it could not
  connect to `ForwardHost:ForwardPort`, within `ConnectTimeout` or at all. A connect cut short by the tap's own disposal
  is not counted. It is the counterpart of `ProxyTap.ForwardFailures` and `OtlpTap.ForwardFailures`.
- `AcceptFailures` on `TcpTap` and `OtlpTap`: R1's count, made public.
- `CaptureDegradationKind.UpstreamConnectFailed` (raised per connection) and `CaptureDegradationKind.AcceptFailed`
  (raised with `ConnectionId` 0, the tap-wide value the record already documents at `CaptureDegradation.cs:38`).
  Both are appended after `StuckConnectionReaped`.

**4. The `Diagnostics()` line.** It is worded for a report reader, and a part whose count is zero is left out:

```
svc→redis: 12 connection(s) could not be forwarded, because the tap could not connect to localhost:6379 (9 failed, the
last with SocketException ConnectionRefused: Connection refused; 3 got no connection within ConnectTimeout 00:00:05),
the first at 14:03:35Z; the tap closed each one, so svc's commands on them never reached redis
```

It reaches the report as soon as a host hands `tap.Diagnostics()` to `IngestRequest.HostDiagnostics`, which the wiki
already shows (`Diagnostics-and-Debugging.md` L154; unchanged). From there:

- the report's diagnostics section lists `CaptureDegraded` first (`ReportGenerator.cs:4344-4345`);
- every `kronikol query` answer carries it as a `!` note (`QueryCommand.cs:388-418`);
- `Failures.md` is handed the same diagnostics (`FailuresDigestGenerator.cs:508`).

A test that failed because its Redis was out of reach gets the cause beside it.

**5. The stop line.** It adds the failed connects.

**6. The outer catch.** It is decided by the token, not the type: `catch (Exception) when (ct.IsCancellationRequested)`
replaces `TcpTap.cs:247`'s filter. After item 1, no exception from the connect reaches it. Anything else that does is
unexpected, and the catch-all below now logs it instead of taking it for a shutdown. (This line is shared with #173;
see §4.4.)

**7. The decoder factory** (`TcpTap.cs:219`). A factory that throws no longer closes the connection. The tap calls
`DisableDecoding(id, ex, "the decoder factory threw")` (`TcpTap.cs:471-482`), which counts it, logs it, raises
`DecodingDisabled` and feeds the existing `Diagnostics()` line. It starts the pumps with no decoder, drains the queue
undecoded (the decode loop's existing `decoding == false` path, `TcpTap.cs:402-403`), and starts no reaper, which has
no decoder to read.

**8. Logging cannot undo the count.** The catch-all's `Log` call (`TcpTap.cs:253`) and every line R2 adds go through
R1's guarded helper. A failed connect is counted before anything is logged or raised, so a callback that throws can
neither lose the count nor stop the `finally` from releasing the pair.

**9. The documents of §3.5 are corrected.** That covers `CaptureDegradationKind`'s summary, `CaptureDegraded`'s, the
"invoked on" sentence of `OnCaptureDegraded`, `ConnectTimeout`'s doc (what a failure does, and Windows' 2 s per
address), the `Log` option's doc, and the class remarks, whose promise now covers accepting and connecting.

**Behaviour changes:** new log lines and `Diagnostics()` lines; a throwing `DecoderFactory` forwards undecoded instead
of closing the connection; `CaptureDegradationKind` gains two values. A host that switches over it exhaustively will
see them.

### 4.3 R3 (patch, the owner's call, Q6): "localhost" on Windows

- **Listening.** When `ListenHost` resolves to the IPv4 loopback, the tap also binds the IPv6 loopback on the same
  port, as `OtlpTap` does (`OtlpTap.cs:193-227`). A failure to bind the second listener is logged and skipped. Each
  listener gets one R1 loop, and `BoundPort` is unchanged. This removes F1's 2 s per connection for a client that dials
  "localhost".
- **Forwarding.** There are three ways to handle F2:
  - **(a)** Try `ForwardHost`'s IPv4 addresses first: resolve, put IPv4 first, then
    `TcpClient.ConnectAsync(IPAddress[], int, CancellationToken)`. This is how the tap already picks a bind address
    (`TcpTap.cs:175-178`). The 2 s moves to a server that listens on IPv6 only.
  - **(b)** The runtime's Happy-Eyeballs-like connect, `ConnectAlgorithm.Parallel`, through the static
    `Socket.ConnectAsync(SocketType, ProtocolType, SocketAsyncEventArgs, ConnectAlgorithm)`. READ only
    (`System.Net.Sockets/ConnectAlgorithm.cs`). It would need its own probe before it was chosen.
  - **(c)** Leave it and document it in the options table and the Limitations.

  The recommendation is the listening change plus (a).

### 4.4 How it meets #173

kronikol-51 and kronikol-71 agreed the split on 2026-10-14:

- **#174 owns** the accept loops of both taps; `HandleConnectionAsync` up to the pumps (the connect, the factory and the
  outer catches); and the accept and connect lines in `Diagnostics()`.
- **#173 owns** everything after both sockets are open: the pumps (`TcpTap.cs:271-321`), half-close, the pair's dispose
  and the reaper, plus `OtlpTap`'s `ServeAsync` and `HttpRequestReader`.
- **Shared:** `TcpTap.cs:247-254` (R2 items 6 and 8), the `Diagnostics()` list and the stop line. Each plan adds its
  own lines, and whichever lands second rebases.

#173 cites `accept/accept-probe.cs` for connections held open holding descriptors.

Three `TcpTap` findings from kronikol-51's read (`TAP_CALLER_TIME_LIMITS_PLAN.harness/read/tcptap.md` §9, READ at
`3240c983`) were divided the same day:

- **F1, a negative `BodyCapBytes` passes `Validate`** (`TcpTapOptions.cs:96`, 188-214). For Redis, the parser's
  constructor then throws inside the decoder factory, and every pair closes right after its upstream connect. **#173
  takes it** (validation at start). Until it lands, R2 item 7 changes the symptom: each Redis connection would be
  forwarded undecoded and counted as decoding disabled, instead of closed. Either order works.
- **F2, a throwing `DecoderFactory` closes the pair.** This is §3.3; **#174 takes it** (R2 item 7).
- **F3, unguarded `Log` calls.** **#173 takes** `TcpTap.cs:257`, 365, 383, 447, 466, 478, 495, 517 and 672 (the
  pumps, the reaper, the decode loop, the `finally`, `TryReset` and `ResolveIdentity`), `OtlpTap`'s outside its accept
  loop (among them `MapPayload`'s catch at L526, whose throw ends the mapping loop for good), and `ProxyTap`'s, which
  this plan does not touch. **#174 takes** L253 and every line it adds (R1, R2 item 8).

Both plans need the same guarded helper, named with kronikol-51: `private void TryLog(string message)`, the one line
`try { _options.Log?.Invoke(message); } catch { }`. The callback is user code, and nothing it throws should change what
the tap does. Whichever plan lands first adds it to `TcpTap`, and the other uses it rather than adding a second.
`OtlpTap` gets the same helper by the same rule: its `Log` calls are unguarded too (L182, 210, 304, 313, 321 and on).
#173 takes the per-connection ones, and R1's accept-loop lines use `TryLog`. `TcpTap.Diagnostic` (L690), through which the decoders log, is unguarded
too. A decoder that calls it under a throwing callback throws itself, and the decode loop takes that for a decoder
error. That is on #173's side of the split (decoding).

### 4.5 Not in this plan

- **Drawing a failed connect in the sequence diagram** as a failed call, as `ProxyTap` does for a failed forward since
  4.19.5 (Q5). A failed connect has no decoded command to draw, only a connection.
- **Telling the caller "refused".** The kernel completes a caller's handshake before the tap's accept returns, so a TCP
  tap cannot refuse after the fact. Whether it then closes with a FIN or an RST stays as it is.
- **Retrying the upstream connect inside the tap.** The client retries on its own. A tap that retried would hide a
  server that is down and stretch the caller's wait past `ConnectTimeout`.
- **Owned by #173:** caller-side time limits, and a pair held after a half-close. A cap on connections at the accept
  loop was never #173's question, as this plan said; kronikol-51 made it that plan's Q13 (recommended: not now; if ever,
  in `TapAcceptLoop`).
- **`ProxyTap`'s request loop** (§8).

## 5. Tests, red first

The new facts go in `tests/Kronikol.Tests.TcpTap` (new classes `TcpTapAcceptTests` and `TcpTapConnectFailureTests`)
and `tests/Kronikol.Tests.Otlp` (`OtlpTapAcceptTests`). `LoopbackPort.cs` is linked into
`Kronikol.Tests.TcpTap.csproj`, as the ProxyTap and Otlp test projects already link it
(`Kronikol.Tests.ProxyTap.csproj:36-37`). That gives `LoopbackPort.Closed()` (a port that refuses) and `Listening()`.

CI runs both projects only on Linux, in "Remaining Unit Tests (auto-discovered)" (`.github/workflows/ci.yml:79-80`;
only the template actions run on Windows and macOS, L139-146). Every fact is therefore also run on Windows before each
release.

No Playwright facts are planned: nothing on the page changes, and the new lines reach it through the existing
diagnostics section.

**R1**

- **A1 `AFailedAcceptDoesNotStopTheTap`** (`TcpTap`). The seam throws `SocketException(TooManyOpenSockets)` three
  times, then accepts. A client's PING gets `+PONG` through the tap, three accepts are counted, there is one log line for
  the first failure and one for "accepting again", and `Diagnostics()` has the accept line. On 4.20.2 the client never
  gets an answer.
- **A2.** The same for `OtlpTap`: a GET is answered.
- **A3 `AnAcceptThatKeepsFailingIsTriedAtAPace`.** The seam fails for 500 ms, and at most 12 tries are allowed (the
  doubling pauses give six or seven). A loop with no pause tries thousands of times.
- **A4 `AFailingAcceptIsLoggedOnceAMinute`.** With the pause shortened through an internal setting (the real one would
  take about 45 s), fifty failures give one line, plus the "accepting again" line. A3 keeps the real pauses.
- **A5 `StoppingTheTapEndsTheAcceptLoopQuietly`.** The seam fails until the loop's pause has reached 1 s, then the tap
  is disposed, and the seam throws `ObjectDisposedException` from then on. `DisposeAsync` returns within 250 ms, the loop
  ends, and nothing more is counted or logged as a failure.
- **A6 `ALogCallbackThatThrowsDoesNotStopTheAcceptLoop`.** A1 with a `Log` callback that throws on every call: the
  client still gets `+PONG`, and the failures are still counted. Both taps.
- **Real-world proof.** The harness's E2, run against the R1 build on Linux, must show both taps answering once the
  descriptors are back.

**R2**

- **C1 refused.** `LoopbackPort.Closed()`, `ForwardHost` "127.0.0.1" (one address) and `ConnectTimeout` 10 s, so that
  Windows' 2 s refusal arrives in time. Expect `UpstreamConnectFailures` 1, one `UpstreamConnectFailed` event with
  `ConnectionId` 1, a log line naming the address and `ConnectionRefused`, the `Diagnostics()` line, and the caller's
  read ending.
- **C2 timed out.** A listener whose queue is full (B1's shape, which leaves a SYN unanswered on both platforms) with
  `ConnectTimeout` 1 s. The failure is counted as a timeout, and the line says "within ConnectTimeout". On 4.20.2
  nothing is logged.
- **C3 host not found.** `no-such-host.invalid` (an RFC 2606 name). Counted as failed, with `HostNotFound`.
- **C4 stopping.** A connect that waits (C2's listener, `ConnectTimeout` 30 s), with the tap disposed after 300 ms.
  Nothing is counted, logged or raised, and `Diagnostics()` stays empty. This is the fact that kills a classification by
  type.
- **C5 the wording.** The line's formatter, called directly with only failures, only timeouts and both: no zero part
  appears.
- **C6 the decoder factory throws.** A stub answers PONG. The client gets `+PONG` and the connection stays open,
  `DecodingDisabledConnections` is 1, a `DecodingDisabled` event is raised, and the existing `Diagnostics()` line
  appears. On 4.20.2 the connection is closed within 1 ms.
- **C7 the stop line.** It names failed connects and failed accepts.
- **C8 a `Log` callback that throws.** C1 with a `Log` callback that throws on every call, and an `OnCaptureDegraded`
  that throws: `UpstreamConnectFailures` is still 1, and `LiveConnections` returns to 0.

**R3**

- **L1 `ATapOnLocalhostAlsoListensOnTheIPv6Loopback`.** A client connects to `[::1]:BoundPort` and gets `+PONG`. It is
  skipped only where the OS has no IPv6 (`Socket.OSSupportsIPv6`), the one case where it cannot apply. On 4.20.2 it is
  red on both platforms.
- **L2 `ATapForwardingToLocalhostReachesAnIPv4OnlyServerAtOnce`.** A stub on 127.0.0.1 only, with `ForwardHost`
  "localhost": the first reply arrives within 500 ms. It is red on 4.20.2 on Windows only (2,038 ms). It passes on Linux
  either way, so the red run is the Windows one.

**Proofs, as the last plans did** (`PROXY_TAP_HANG_UP_PLAN.harness/r1`, `r2`). Each release's facts are copied onto
4.20.2 with only what they need to compile (new members stubbed) and run on both platforms. Every fact must fail on its
own assertion (`rN/red-4.20.2.txt`). Mutations are exact-string replacements, and every one must be killed:

- **R1:**
  - M1 the `return` back in the loop
  - M2 no pause
  - M3 the pause never resets
  - M4 a failure while stopping counted
  - M5 a log line for every failure
  - M6 `OtlpTap`'s loop left as it is
  - M7 the loop's `Log` call unguarded
  - M8 the pause not waiting on the tap's token
- **R2:**
  - N1 the stop decided by the exception's type
  - N2 a timeout counted as failed
  - N3 no event
  - N4 the factory fault still closes the connection
  - N5 the line dropped when there are only timeouts
  - N6 the outer catch keeping its type filter
  - N7 the count taken after the log line

  N6 may prove equivalent, as M09 and M10 did for #172. If it does, the filter is deleted rather than kept untested.

**Checks before each release:**

- Both test projects on Windows and in the `sdk:10.0` container.
- The core suite.
- `release.slnf` in Release.
- The release steps the last releases followed: the templates pin the previous published version only once nuget.org
  lists all 62 ids, and the history action's `VERSION`.

## 6. Releases and records

### 6.1 Releases

| Release | What | Bump | Issue |
|---|---|---|---|
| R1 | `TcpTap` and `OtlpTap` keep accepting after a failed accept | patch | found while planning #174 |
| R2 | Failed connects counted and reported; the decoder factory fault; the documents | minor | #174 |
| R3 | "localhost" on Windows, if the owner wants it | patch | found while planning #174 |

Version numbers are claimed with "taking X now" at push time, as the sibling plans do. None is named here.

### 6.2 Changelog drafts

R1:

```
**Patch - a tap whose accept fails keeps accepting (`plans/TCPTAP_FAILED_CONNECT_PLAN.md` R1, found while planning
#174).** A bug fix, and nothing new for a consumer to call, so the patch part moves.

### Fixed

- **One failed accept stopped `TcpTap` and `OtlpTap` from accepting for good.** Their accept loops returned on any
  `SocketException`, and nothing started them again. The listening socket stayed open and `IsListening` stayed true, so
  later callers connected and were never served, and nothing was logged. Measured on Linux with the process out of file
  descriptors: neither tap answered again once they were back. Each loop now logs the failure, pauses (10 ms, doubling
  to 1 s) and tries again until the tap is disposed, and `Diagnostics()` says how many accepts failed. `ProxyTap` was
  not affected.
```

R2:

```
**Minor - a `TcpTap` connection that could not be forwarded is counted and reported (#174;
`plans/TCPTAP_FAILED_CONNECT_PLAN.md` R2).** New public members, so the minor part moves.

### Added

- `TcpTap.UpstreamConnectFailures`, `TcpTap.AcceptFailures`, `OtlpTap.AcceptFailures`, and
  `CaptureDegradationKind.UpstreamConnectFailed` and `AcceptFailed`.

### Fixed

- **A connect to the real server that failed was not counted, and one that timed out was not even logged.** A timeout
  was taken for the tap shutting down, and a refusal was one log line. On Windows a refusal takes about 2 s per address,
  so under a short `ConnectTimeout` it was silent too. Each failure is now counted, logged, raised through
  `OnCaptureDegraded` and reported by `Diagnostics()`, which names the address and the last error. The tap stopping is
  told apart by its own token, not by the exception's type.
- **A `DecoderFactory` that threw closed a connection whose server was healthy.** The connection now forwards
  undecoded, and the failure is counted as decoding disabled.
- Documentation: `CaptureDegradationKind`, `DiagnosticKind.CaptureDegraded` and `OnCaptureDegraded` no longer claim
  that forwarding is never affected, or that events come only from the decoder task.
```

R3 follows the same shape: **Patch**, with "a tap on localhost no longer costs 2 s per connection on Windows".

### 6.3 Wiki (`../Kronikol.wiki` at `origin/master` `60daf39`; edited after publication in a worktree of its own, as before)

`Integration-TcpTap-Extension.md`:

- **L151, the `ConnectTimeout` row:** what a failure does, and Windows' 2 s per address.
- **The options table:** a `ReapStuckConnectionsAfter` row, missing since `398655f4`.
- **L287-300, the counters:** `UpstreamConnectFailures`, `AcceptFailures` and the missing `StuckConnectionsReaped`.
- **L304, the event kinds:** `StuckConnectionReaped` (missing), `UpstreamConnectFailed` and `AcceptFailed`.
- **L306-318, the `Diagnostics()` examples:** the two new lines and the reaped line.
- **L285:** the promise now covers accepting and connecting.
- **Limitations:** the "localhost" note, unless R3 ships.
- **For R3, L131, the `ListenHost` row:** "localhost" binds both loopbacks.

`Integration-Otlp-Extension.md` gets R1's accept line, and R2's `AcceptFailures`.

### 6.4 Plans, roadmap, Kronikol4J, the issue

- `PLANS_STATUS.md`: this plan's row, added with the plan and updated per release.
- `ROADMAP.md`: no row, as #171 and #172 took none (Q7).
- Kronikol4J's ledger (`docs/REMAINING_PARITY.md`): one line per release, saying "Nothing to port: the port has no
  network tap". It never mentions `TcpTap`, and says the same of `ProxyTap` at L1922.
- #174 closes with R2's publication, with a comment naming each release.

## 7. Order against the sibling plans

- **#173** (kronikol-51) shares the lines in §4.4. Either can land first, and the second rebases.
- **The other plans written today** (#175 by kronikol-ec, #165's weaver plan, and #78 with #124 by kronikol-22) share
  no file with this one.
- **Within this plan:** R1 first, as it is the most severe and the smallest and is independent of R2. Then R2, then R3.

## 8. Found on the way, not in this plan

- **`ProxyTap`'s request loop** also returns on any `HttpListenerException` (`ProxyTap.cs:223-228`). Under the same
  EMFILE it kept answering, because its listener re-arms its own accept, and no burst made it throw. That is not a
  defect anyone has seen, so it is not planned here. It is worth one fact if #173 touches that loop.
- **The runtime's wording.** It reports EMFILE as "Too many open files in system" (`SocketError.TooManyOpenSockets`,
  printed with native code 23, which is ENFILE's). A limit on one process reads as a limit on the whole system. It is
  cosmetic, and no report is planned.
- **A false reading in the harness.** The first Windows run of `accept-probe.cs` read one slow check as a dead
  `OtlpTap`. The corrected probe checks again every round (harness README), and only the corrected runs are cited.

## 9. Questions, with recommendations

1. **Q1. One counter or two?** Should R2 add one counter, `UpstreamConnectFailures` (timeouts included, with the split
   only in the `Diagnostics()` words), or a second one, `UpstreamConnectTimeouts`? **Recommended: one**, as
   `ProxyTap.ForwardFailures` and `OtlpTap.ForwardFailures` do.
2. **Q2. Make the accept count public?** That means `AcceptFailures` on both taps, plus `AcceptFailed` raised through
   `OnCaptureDegraded`, in R2. **Recommended: yes.** TcpTap's promise names a counter and an event for every loss, and
   `OtlpTap` has a counter for each of its `Diagnostics()` lines.
3. **Q3. How much logging?** Log every failed connect, as a refusal is logged today, or the first and then one line a
   minute? **Recommended: every one,** the same in kind as today's refusal line, with the `Diagnostics()` line as the
   summary. A client's retry rate was not measured.
4. **Q4. How many events?** One `OnCaptureDegraded` event per failed connect, or a throttled one like
   `SegmentsDropped`? **Recommended: one per connection,** as `ConnectionClosedMidMessage` is.
5. **Q5. Draw a failed connect in the sequence diagram** as a failed call, as `ProxyTap` does for a failed forward?
   **Recommended: not now.** A failed connect has no command to draw, and its test could only be found from the test
   window. The `Diagnostics()` line already reaches every query answer and `Failures.md`.
6. **Q6. R3:** take it, and which forwarding approach from §4.3? **Recommended: take it, with (a).**
7. **Q7. A `ROADMAP.md` row?** **Recommended: none,** as for #171 and #172.
8. **Q8. Ship R1 on its own first, or fold it into R2?** **Recommended: on its own, first.**
9. **Q9. R1's pause** (10 ms, doubling to 1 s, reset after a success)? **Recommended: as stated.**

## 10. Assumption ledger

| Assumption | Status | Where |
|---|---|---|
| A refusal on Windows takes about 2 s per address | RUN, and READ (the post cited in §2.3) | `connect/` A1 to A3, G |
| A connect timeout and the tap stopping throw the same type | RUN on both platforms | `connect/` G, G2 |
| Resets do not make an accept throw | RUN, Windows 11 26200 and Ubuntu 24.04.5 | `accept/` E0, E1 |
| EMFILE ends both taps' loops for good, while the runtime recovers | RUN on Linux | `accept/` E2 |
| `ProxyTap` survives EMFILE | RUN on Linux | `accept/` E2 |
| A caller's handshake completes while nothing accepts | RUN, as seen in E2; READ for the rest | `accept/` E2 |
| macOS behaves like Linux, and the other `accept(2)` errors take the same path | READ, not measured (no macOS here) | `accept(2)` |
| The two test projects run only on Linux in CI | READ | `ci.yml:60`, `79-80` |
| How often a client retries against a server that is down | Not measured | Q3 |

## 11. Log

- **2026-10-14:** written at 4.20.2 (`3240c983`) by kronikol-71. Measured on both platforms. Split agreed with
  kronikol-51 (#173); kronikol-a5 confirmed the issue's provenance. Not green-lit.
- **2026-10-14, later:** kronikol-51's read found every `Log` call in `TcpTap` unguarded (§4.4, F1 to F3). Added: R1's
  and R2's own logging goes through one guarded helper (§4.1, R2 item 8), facts A6 and C8, mutations M7 and N7, and the
  division of F1 to F3 between the two plans.
- **2026-10-15:** the owner green-lit the plan in full ("implement the plan in full"), with every §9 question answered as
  recommended: one counter (Q1), the accept count public (Q2), every failed connect logged (Q3), one event per
  connection (Q4), no failed call drawn (Q5), R3 with approach (a) (Q6), no roadmap row (Q7), R1 first and on its own
  (Q8), the pause as stated (Q9). kronikol-51 confirmed that #173 is not being implemented, so this plan adds `TryLog`
  to both taps. Neither plan's F3 list had the start and stop lines (`TcpTap.cs:162`, 715; `OtlpTap.cs:182`, 210, 611),
  so this plan takes them, and kronikol-51 was told.
- **2026-10-15, R1 released as 4.20.3.** Both taps run one accept loop, `TapAcceptLoop`, a file `Kronikol.Extensions.Otlp`
  links from `Kronikol.Extensions.TcpTap` and compiles into its own namespace (`KRONIKOL_OTLP_SHARED`), as the TcpTap
  package links the Redis and MongoDB classifiers. Its seams are `Accept`, `Pause` and a `TimeProvider` clock, so the
  pauses and the once-a-minute rule are pinned without waiting 45 s; §4.1's `AcceptClient` seam on each tap became
  `Accepting.Accept`. Each log line names the listener's address, since R3 gives a tap two. Found while building it:
  `OtlpTap` binds both loopbacks for "127.0.0.1" too, not only for "localhost", so its facts make the IPv4 listener's
  accepts fail, the one E2 saw stop. Two facts beyond §5 pin branches the plan named and listed no fact for: a listening
  socket closed under a running tap is logged and ends its loop, and stopping a tap that waits for a caller counts
  nothing (the latter is M4's fact: A5 stops the loop in a pause, where no accept is under way). The start and stop
  lines and `OtlpTap`'s "could not also bind" line go through `TryLog` too, each with a fact. Red on 4.20.2
  (`r1/red-4.20.2-{windows,linux}.txt`, with `r1/red-seam.diff` and `r1/TapAcceptLoop.red-stub.cs` wrapping the seam
  round 4.20.2's own loop): 12 facts red on their own assertions on both platforms, and the two "stopping counts
  nothing" guards green, as they are on 4.20.2. 25 of 25 mutations are killed (`r1/mutations-windows.txt`): M1 to M8
  as §5 lists them, and seventeen more for the lines and branches R1 added. On Linux, E2 built from the release's tree
  (`r1/accept-probe-r1.cs`) shows both taps logging seven failed accepts under EMFILE and answering again once the
  descriptors are back (`r1/accept-r1-linux.txt`), the real-world proof §5 asked for. Version order agreed with
  kronikol-2a (#165, 4.20.4) and kronikol-77 (#168's audit, 4.20.5).

## Appendix A. Edit sites at `3240c983`

| File | Lines | Release | What changes |
|---|---|---|---|
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 13-34 | R2 | the class remarks: the promise covers accepting and connecting |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 49-70 | R1, R2 | the new counters |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 149-199 | R1, R3 | the accept loop and its seam; for R3, the second listener |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 201-254 | R2 | the connect sorted by its tokens, the factory, the outer catch |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 529-580 | R1, R2 | the accept and connect lines |
| `src/Kronikol.Extensions.TcpTap/TcpTap.cs` | 693-720 | R1, R2, R3 | the stop line; for R3, stopping both listeners |
| `src/Kronikol.Extensions.Otlp/OtlpTap.cs` | 85-154, 259-275 | R1, R2 | the accept loop, its line and (R2) its public count |
| `src/Kronikol.Extensions.TcpTap/CaptureDegradation.cs` | 3-30 | R2 | the summary and the two new members |
| `src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs` | 58, 104-105, 136-143 | R2 | the `Log`, `ConnectTimeout` and `OnCaptureDegraded` docs |
| `src/Kronikol/Reports/DiagnosticEntry.cs` | 41-46 | R2 | `CaptureDegraded`'s summary |
| `tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj` | (new item) | R1 | link `LoopbackPort.cs` |
| `tests/Kronikol.Tests.TcpTap`, `tests/Kronikol.Tests.Otlp` | (new classes) | R1, R2, R3 | the facts in §5 |
