#:property PublishAot=false
// abort-stall-probe.cs: measures the six INFERRED claims in section 8 of ../source/dotnet-listener-timers.md
// (TAP_CALLER_TIME_LIMITS_PLAN, #173) on a bare System.Net.HttpListener, http.sys on Windows and the managed
// implementation on Linux and macOS, with raw-socket callers and no Kronikol package.
// `dotnet run abort-stall-probe.cs` (add `quick` for a smoke run that is not a measurement); the same file on each OS.
// stdout is the result, stderr is progress. Every case runs 5 times.
//   1   a chunked answer (SendChunked; 16 MB as 64 KiB WriteAsync calls) to a caller that stops reading: does a
//       WriteAsync call block before it returns its Task; does Response.Abort(), 1 s after the writes stall, block;
//       what the caller reads afterwards; 1b the same with 4 MB as writes of 1 to 8 bytes, where the stall can fall on
//       one of the managed listener's synchronous writes
//   2a  a Content-Length answer in one 16 MB WriteAsync, 2b in 64 KiB WriteAsync calls, to a caller that stops
//       reading: does the pending write complete without an exception after Abort(), how soon, is the connection
//       released
//   3a  a pending body ReadAsync (Content-Length: 100000, 1,000 bytes sent, then the caller stalls), then
//       StatusCode = 408 and Abort(): what the read does, what the caller receives; 3b the same with a chunked body
//   3c  (beyond the six claims) when a pending ReadAsync(64 KiB) returns while a body trickles in, for each body kind
//   4   HttpListener.Close() with a caller stalled mid-head and an idle kept-alive connection: what each receives
//   5   callers with no context: 5a connects and sends nothing, 5b idle after one exchange, 5c one byte of a head and
//       then nothing; watched to 210 s, concurrently with everything else
//   6   HttpListenerTimeoutManager: what each getter returns and which setter throws; on Windows, http.sys's
//       EntityBody, MinSendRate, HeaderWait and IdleConnection timers at their defaults and at a set value, on the
//       first request on a connection and on a later one (watched to 210 s, MinSendRate to 540 s)
// The writes, Abort() and HttpListener.Close() run on threads of their own with a 10 s watchdog, so a call that
// blocks is reported as blocked instead of hanging the probe. The source holds no backslash: CR and LF are built
// from char codes.
#pragma warning disable CA1416 // TimeoutManager's setters are Windows-only; trying them on Linux is part of the probe
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
return await Probe.RunAsync(args);

static class Probe
{
    internal static int Repeats = 5;
    internal static int LongWindow = 210;
    internal static int MinSendWindow = 540;
    internal const int OpenMark = 200;
    internal const int Kb64 = 64 * 1024;
    internal const int Mb16 = 16 * 1024 * 1024;
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static double Now => Clock.Elapsed.TotalSeconds;
    internal static readonly string CRLF = new(new[] { (char)13, (char)10 });
    internal static readonly string LF = new(new[] { (char)10 });
    internal static readonly byte[] CrLf = { 13, 10 };
    internal static readonly byte[] HeadEnd = { 13, 10, 13, 10 };
    internal static readonly bool IsWindows = OperatingSystem.IsWindows();
    internal static IPAddress Target = IPAddress.Loopback;
    static readonly object PrintGate = new();

    internal static void Out(string line) { lock (PrintGate) Console.Out.WriteLine(line); }
    internal static void Out(IEnumerable<string> lines) { lock (PrintGate) foreach (var line in lines) Console.Out.WriteLine(line); }
    internal static void Progress(string text) { lock (PrintGate) Console.Error.WriteLine($"[{Now,6:0.0} s] {text}"); }

    public static async Task<int> RunAsync(string[] args)
    {
        var quick = args.Contains("quick");
        if (quick) { Repeats = 2; LongWindow = 25; MinSendWindow = 30; }
        Target = (await Dns.GetHostAddressesAsync("localhost"))[0];
        Out($".NET {Environment.Version} ({RuntimeInformation.FrameworkDescription}), {RuntimeInformation.OSDescription}, {RuntimeInformation.RuntimeIdentifier}, {Environment.ProcessorCount} CPUs");
        Out($"HttpListener: {(IsWindows ? "http.sys" : "managed")}. abort-stall-probe.cs started {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z{(quick ? " in QUICK mode (a smoke run, not a measurement)" : "")}.");
        Out($"Callers are raw sockets to {Target} (localhost's first address). {Repeats} runs per case. Times are in seconds.");
        Out("FIN: the caller's pending receive returned 0. RST: it threw SocketException ConnectionReset.");
        _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(15)); Out("WATCHDOG: the probe ran past 15 min and was stopped"); Environment.Exit(2); });

        var settings = SettingsCheck();
        var longCases = StartLongCases();
        await Guard("1", () => Claim1(tiny: false));
        await Guard("1b", () => Claim1(tiny: true));
        await Guard("2a", () => Claim2(oneWrite: true));
        await Guard("2b", () => Claim2(oneWrite: false));
        await Guard("3a", () => Claim3(chunked: false));
        await Guard("3b", () => Claim3(chunked: true));
        await Guard("3c-length", () => Claim3c(chunked: false));
        await Guard("3c-chunked", () => Claim3c(chunked: true));
        await Guard("4", Claim4);
        Progress("cases 1 to 4 done; waiting for the long cases");

        Out("");
        Out("== 5. Callers that never complete a head, so the application has no context for them (the managed listener's first-read");
        Out($"   timer; on Windows, http.sys's own timers). Each case's {Repeats} callers start together and keep a receive pending; watched to");
        Out($"   {LongWindow} s after the case starts, concurrently with cases 1 to 4 and 6. Its own listener and port per case.");
        foreach (var c in longCases.Where(c => c.Claim == 5)) { await c.Task; Out(c.Lines); }

        Out("");
        Out("== 6. HttpListenerTimeoutManager. On a new listener that is never started: each getter, then each setter in turn.");
        Out(settings);
        if (IsWindows)
        {
            Out($"   http.sys's timers firing: each case has its own listener and port, its {Repeats} callers start together, watched to {LongWindow} s");
            Out($"   (6b and 6b10k to {MinSendWindow} s). 'netsh' is the URL group's Timeouts section as http.sys reports it after the set.");
            foreach (var c in longCases.Where(c => c.Claim == 6)) { await c.Task; Out(c.Lines); }
        }
        else Out("   (the cases where http.sys's timers fire run on Windows only)");
        Out("");
        Out($"Done at {Now:0} s.");
        return 0;
    }

    static async Task Guard(string id, Func<Task> run)
    {
        try { await run(); }
        catch (Exception ex) { Out($"  CASE {id} FAILED: {Describe(ex)}"); }
    }

    // ------------------------------------------------------------------------------------------------ 6: settings
    static List<string> SettingsCheck()
    {
        var lines = new List<string>();
        try
        {
            using var listener = new HttpListener();
            var tm = listener.TimeoutManager;
            lines.Add($"  getters: EntityBody={tm.EntityBody} DrainEntityBody={tm.DrainEntityBody} RequestQueue={tm.RequestQueue} IdleConnection={tm.IdleConnection} HeaderWait={tm.HeaderWait} MinSendBytesPerSecond={tm.MinSendBytesPerSecond}");
            var five = TimeSpan.FromSeconds(5);
            var setters = new (string Name, Action Set, Func<object> Get)[]
            {
                ("EntityBody = 5 s", () => tm.EntityBody = five, () => tm.EntityBody),
                ("DrainEntityBody = 5 s", () => tm.DrainEntityBody = five, () => tm.DrainEntityBody),
                ("RequestQueue = 5 s", () => tm.RequestQueue = five, () => tm.RequestQueue),
                ("IdleConnection = 5 s", () => tm.IdleConnection = five, () => tm.IdleConnection),
                ("HeaderWait = 5 s", () => tm.HeaderWait = five, () => tm.HeaderWait),
                ("MinSendBytesPerSecond = 10000", () => tm.MinSendBytesPerSecond = 10_000, () => tm.MinSendBytesPerSecond),
            };
            foreach (var (name, set, get) in setters)
            {
                string outcome;
                try { set(); outcome = $"set; reads back {get()}"; }
                catch (Exception ex) { outcome = $"THREW {Describe(ex)}; reads back {get()}"; }
                lines.Add($"  {name}: {outcome}");
            }
        }
        catch (Exception ex) { lines.Add($"  settings check FAILED: {Describe(ex)}"); }
        return lines;
    }

    // ------------------------------------------------------------------------------------------------ 1
    static async Task Claim1(bool tiny)
    {
        Out("");
        if (!tiny)
        {
            Out("== 1. A chunked answer (SendChunked = true; 16 MB as 256 awaited WriteAsync calls of 64 KiB, on a thread of their own) to a caller");
            Out("   that stops reading (receive buffer 16 KiB). A stall: no write completed for 1 s. 1 s after the stall Response.Abort() runs on a");
            Out("   thread of its own with a 10 s watchdog. Then the caller reads what it is sent, until FIN, RST or 3 s without a byte.");
        }
        else
        {
            Out("== 1b. As 1, but 4 MB as awaited WriteAsync calls of 1 to 8 bytes (sizes drawn at random, seeded by the run). With 64 KiB and with");
            Out("   1 KiB writes the stall fell on the asynchronous data write every time; tiny writes give the managed listener's synchronous writes");
            Out("   (a chunk's size line, inside the WriteAsync call; its closing CRLF, in the completion callback) a real share of the bytes. The");
            Out("   caller's chunked parse tells them apart: data in flight leaves a chunk without its CRLF (MALFORMED); a CRLF that was already");
            Out("   queued leaves whole chunks (looks COMPLETE); a size line with no data after it is cut or MALFORMED at the size.");
        }
        var port = FreePort();
        using var listener = StartListener(port);
        int inCall = 0, blocked = 0, malformed = 0, complete = 0;
        for (var run = 1; run <= Repeats; run++)
        {
            Progress($"1{(tiny ? "b" : "")} run {run}");
            var lines = new List<string>();
            using var caller = await Connect(port, 16 * 1024);
            await Send(caller, $"GET /c1/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}");
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            ctx.Response.SendChunked = true;
            var writer = tiny ? new Writer(ctx.Response.OutputStream, TinySizes(run, 4 * 1024 * 1024)) : new Writer(ctx.Response.OutputStream, Kb64, 256);
            var stall = await writer.WaitForStallAsync(1, 20);
            if (stall.InCall) inCall++;
            lines.Add($"at the stall ({stall.At - writer.StartedAt:0.00} s after the first call): {stall.Text}");
            await Task.Delay(1000);
            var abortAt = Now;
            writer.MarkAbort();
            var abort = Call.Start(ctx.Response.Abort);
            lines.Add($"Abort(): {await abort.WatchAsync(10)}");
            if (!abort.Task.IsCompleted) blocked++;
            await Task.WhenAny(writer.Done, Task.Delay(500));
            lines.Add($"the writes then: {writer.AfterAbort(abortAt)}");
            var readAt = Now;
            var watcher = new Watcher(caller);
            await watcher.DrainAsync(3, 30);
            var fate = watcher.Fate(readAt, "it started reading");
            if (fate.Contains("MALFORMED")) malformed++;
            if (fate.Contains("looks COMPLETE")) complete++;
            lines.Add($"the caller then read {fate}");
            await Task.WhenAny(abort.Task, Task.Delay(10_000));
            lines.Add($"Abort() in the end: {abort.Later(readAt, "the caller started reading")}");
            await Task.WhenAny(writer.Done, Task.Delay(10_000));
            lines.Add($"the writes in the end: {writer.AfterAbort(abortAt)}");
            lines.Add($"the longest any WriteAsync call took to return its Task: {writer.LongestCallText}");
            await ResetIfStillBlocked(caller, abort, lines, readAt);
            Out(RunBlock(run, lines));
        }
        Out($"  tally: stalled inside a WriteAsync call (no Task returned) {inCall}/{Repeats}; Abort() blocked >10 s {blocked}/{Repeats}; the caller's chunked body MALFORMED {malformed}/{Repeats}, looks complete {complete}/{Repeats}");
    }

    // ------------------------------------------------------------------------------------------------ 2
    static async Task Claim2(bool oneWrite)
    {
        Out("");
        Out(oneWrite
            ? "== 2a. A Content-Length answer (ContentLength64 = 16 MB) in ONE WriteAsync of 16 MB, to a caller that stops reading (receive buffer 16 KiB)."
            : "== 2b. A Content-Length answer (ContentLength64 = 16 MB) in 256 awaited WriteAsync calls of 64 KiB, to a caller that stops reading (16 KiB).");
        Out("   As in 1: a stall, Response.Abort() 1 s later on a thread of its own (10 s watchdog), then the caller reads. The listener's tables");
        Out("   (reflection; on Windows, http.sys's active requests from netsh) and the server side's TCP sockets (Linux: /proc, 'no fd' = the");
        Out("   process no longer holds it) are read before the Abort, 0.2 s after it, and after the caller's read.");
        var port = FreePort();
        using var listener = StartListener(port);
        int normal = 0, released = 0, blocked = 0;
        for (var run = 1; run <= Repeats; run++)
        {
            Progress($"2{(oneWrite ? "a" : "b")} run {run}");
            var lines = new List<string>();
            using var caller = await Connect(port, 16 * 1024);
            await Send(caller, $"GET /c2/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}");
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            ctx.Response.ContentLength64 = Mb16;
            var writer = oneWrite ? new Writer(ctx.Response.OutputStream, Mb16, 1) : new Writer(ctx.Response.OutputStream, Kb64, 256);
            var stall = await writer.WaitForStallAsync(1, 20);
            lines.Add($"at the stall ({stall.At - writer.StartedAt:0.00} s after the first call): {stall.Text}");
            lines.Add($"before the Abort: {Tables(listener, port)}");
            await Task.Delay(1000);
            var abortAt = Now;
            writer.MarkAbort();
            var abort = Call.Start(ctx.Response.Abort);
            lines.Add($"Abort(): {await abort.WatchAsync(10)}");
            if (!abort.Task.IsCompleted) blocked++;
            await Task.WhenAny(writer.Done, Task.Delay(2000));
            lines.Add($"the writes: {writer.AfterAbort(abortAt)}");
            if (writer.PendingWriteCompletedNormally) normal++;
            await Task.Delay(200);
            if (Held(listener, port) == 0) released++;
            lines.Add($"0.2 s after the Abort: {Tables(listener, port)}");
            var readAt = Now;
            var watcher = new Watcher(caller);
            await watcher.DrainAsync(3, 30);
            lines.Add($"the caller then read {watcher.Fate(readAt, "it started reading")}");
            lines.Add($"after the caller's read: {Tables(listener, port)}");
            if (!abort.Task.IsCompleted) lines.Add($"Abort() in the end: {abort.Later(readAt, "the caller started reading")}");
            await ResetIfStillBlocked(caller, abort, lines, readAt);
            Out(RunBlock(run, lines));
        }
        Out($"  tally: the write pending at the Abort completed without an exception {normal}/{Repeats}; Abort() blocked >10 s {blocked}/{Repeats}; {(IsWindows ? "no active request" : "_connections and _listenerContexts both 0")} 0.2 s after the Abort {released}/{Repeats}");
    }

    // ------------------------------------------------------------------------------------------------ 3
    static async Task Claim3(bool chunked)
    {
        Out("");
        Out(chunked
            ? "== 3b. A pending body ReadAsync on a CHUNKED request body: a head with Transfer-Encoding: chunked, one 1,000-byte chunk (3E8), then the"
            : "== 3a. A pending body ReadAsync: a head with Content-Length: 100000 and 1,000 body bytes, then the");
        Out("   caller stalls with the socket open and keeps a receive pending. The server reads until a ReadAsync stays pending for 0.5 s; 1 s");
        Out("   later StatusCode = 408 and Response.Abort() (a thread of its own, 10 s watchdog). Reported: what the pending read did and when,");
        Out("   and what the caller received (its receive waits up to 3 s for FIN or RST).");
        var port = FreePort();
        using var listener = StartListener(port);
        var tally = new SortedDictionary<string, int>();
        for (var run = 1; run <= Repeats; run++)
        {
            Progress($"3{(chunked ? "b" : "a")} run {run}");
            var lines = new List<string>();
            using var caller = await Connect(port);
            var watcher = new Watcher(caller);
            var body = new string('a', 1000);
            await Send(caller, chunked
                ? $"POST /c3b/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}Transfer-Encoding: chunked{CRLF}{CRLF}3E8{CRLF}{body}{CRLF}"
                : $"POST /c3a/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}Content-Length: 100000{CRLF}{CRLF}{body}");
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var input = ctx.Request.InputStream;
            var buffer = new byte[Kb64];
            long got = 0;
            Task<int>? pending = null;
            var early = "";
            while (true)
            {
                var read = input.ReadAsync(buffer, 0, buffer.Length);
                if (await Task.WhenAny(read, Task.Delay(500)) != read) { pending = read; break; }
                try
                {
                    var n = await read;
                    if (n == 0) { early = "a ReadAsync returned 0 before the Abort"; break; }
                    got += n;
                }
                catch (Exception ex) { early = $"a ReadAsync threw before the Abort: {Describe(ex)}"; break; }
            }
            lines.Add($"the server read {got} body bytes; {(pending is null ? early : "the next ReadAsync is pending")}");
            await Task.Delay(1000);
            var observed = pending is null ? null : Observe(pending);
            var status = Try(() => ctx.Response.StatusCode = 408);
            var abortAt = Now;
            var abort = Call.Start(ctx.Response.Abort);
            lines.Add($"StatusCode = 408: {status}; Abort(): {await abort.WatchAsync(10)}");
            if (observed is not null)
            {
                string outcome, label;
                if (await Task.WhenAny(observed, Task.Delay(5000)) == observed)
                {
                    var (at, text, kind) = await observed;
                    outcome = $"{text}, {at - abortAt:0.000} s after the Abort() call";
                    label = kind;
                }
                else { outcome = "still pending 5 s after the Abort() call"; label = "still pending"; }
                tally[label] = tally.GetValueOrDefault(label) + 1;
                lines.Add($"the pending ReadAsync {outcome}");
            }
            await watcher.WaitEndAsync(3);
            lines.Add($"the caller received {watcher.Fate(abortAt, "the Abort() call")}");
            await ResetIfStillBlocked(caller, abort, lines, abortAt);
            Out(RunBlock(run, lines));
        }
        Out($"  tally of the pending read: {string.Join("; ", tally.Select(kv => $"{kv.Key} {kv.Value}/{Repeats}"))}");
    }

    // Beyond the six claims: when a pending ReadAsync(64 KiB) returns while a body trickles in. A tap that resets an idle
    // timer on each completed read needs a read to complete when bytes arrive.
    static async Task Claim3c(bool chunked)
    {
        Out("");
        Out($"== 3c-{(chunked ? "chunked" : "length")}. Beyond the six claims: when does a pending ReadAsync(64 KiB) return while the body trickles in? The caller sends");
        Out(chunked
            ? "   a head with Transfer-Encoding: chunked and one 1,000-byte chunk, a 100-byte chunk at 1 s and at 2 s, a 70,000-byte chunk at 3 s, then stalls."
            : "   a head with Content-Length: 100000 and 1,000 body bytes, 100 more bytes at 1 s and at 2 s, 70,000 more at 3 s, then stalls.");
        Out("   The server keeps one ReadAsync(64 KiB) pending at a time from the moment it has the context. Listed: each read's result, timed from");
        Out("   when the head was sent; at 6 s Response.Abort() ends the run (what it did to the pending read is listed too).");
        var port = FreePort();
        using var listener = StartListener(port);
        var trickle = chunked ? $"64{CRLF}{new string('b', 100)}{CRLF}" : new string('b', 100);
        var big = chunked ? $"11170{CRLF}{new string('c', 70_000)}{CRLF}" : new string('c', 70_000);
        for (var run = 1; run <= Repeats; run++)
        {
            Progress($"3c-{(chunked ? "chunked" : "length")} run {run}");
            using var caller = await Connect(port);
            _ = new Watcher(caller);
            var sentAt = Now;
            await Send(caller, chunked
                ? $"POST /c3c/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}Transfer-Encoding: chunked{CRLF}{CRLF}3E8{CRLF}{new string('a', 1000)}{CRLF}"
                : $"POST /c3c/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}Content-Length: 100000{CRLF}{CRLF}{new string('a', 1000)}");
            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var events = new ConcurrentQueue<string>();
            var input = ctx.Request.InputStream;
            var reader = Task.Run(async () =>
            {
                var buffer = new byte[Kb64];
                while (true)
                {
                    int n;
                    try { n = await input.ReadAsync(buffer, 0, buffer.Length); }
                    catch (Exception ex) { events.Enqueue($"threw at {Now - sentAt:0.00} s: {Describe(ex)}"); return; }
                    events.Enqueue($"{n} bytes at {Now - sentAt:0.00} s");
                    if (n == 0) return;
                }
            });
            await DelayUntil(sentAt + 1);
            await Send(caller, trickle);
            events.Enqueue($"(100 more sent at {Now - sentAt:0.00} s)");
            await DelayUntil(sentAt + 2);
            await Send(caller, trickle);
            events.Enqueue($"(100 more sent at {Now - sentAt:0.00} s)");
            await DelayUntil(sentAt + 3);
            await Send(caller, big);
            events.Enqueue($"(70,000 more sent at {Now - sentAt:0.00} s)");
            await DelayUntil(sentAt + 6);
            events.Enqueue($"(Abort() at {Now - sentAt:0.00} s)");
            var abort = Call.Start(ctx.Response.Abort);
            await abort.WatchAsync(10);
            await Task.WhenAny(reader, Task.Delay(2000));
            if (!reader.IsCompleted) events.Enqueue("the read was still pending 2 s after the Abort()");
            Out($"  run {run}: reads: {string.Join("; ", events)}");
        }
    }

    // Write sizes of 1 to 8 bytes, drawn at random with the run as the seed, adding up to at least `total` bytes.
    static int[] TinySizes(int seed, int total)
    {
        var random = new Random(seed);
        var sizes = new List<int>();
        for (long sum = 0; sum < total;)
        {
            var size = random.Next(1, 9);
            sizes.Add(size);
            sum += size;
        }
        return sizes.ToArray();
    }

    static async Task DelayUntil(double at)
    {
        var wait = at - Now;
        if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait));
    }

    static async Task<(double At, string Text, string Kind)> Observe(Task<int> read)
    {
        try
        {
            var n = await read.ConfigureAwait(false);
            return (Now, $"returned {n}", n == 0 ? "returned 0 (reads as the end of the body)" : $"returned {n}");
        }
        catch (Exception ex)
        {
            var at = Now;
            var kind = ex is HttpListenerException h ? $"HttpListenerException {h.ErrorCode}" : ex.GetType().Name + (ex.InnerException is { } inner ? $" (inner {inner.GetType().Name})" : "");
            return (at, $"threw {Describe(ex)}", kind);
        }
    }

    // ------------------------------------------------------------------------------------------------ 4
    static async Task Claim4()
    {
        Out("");
        Out("== 4. HttpListener.Close() (a thread of its own, 10 s watchdog) with (a) a caller stalled mid-head (request line and Host, no blank");
        Out("   line) and (b) an idle kept-alive connection after one complete exchange (GET; 200 with Content-Length: 2, read whole). Both keep");
        Out("   a receive pending; reported is what each receives in the 6 s after the Close() call. A new listener per run.");
        int a200 = 0, b200 = 0;
        for (var run = 1; run <= Repeats; run++)
        {
            Progress($"4 run {run}");
            var lines = new List<string>();
            var port = FreePort();
            var listener = StartListener(port);
            Dispatch(listener, AnswerOk);
            using var callerA = await Connect(port);
            var watcherA = new Watcher(callerA);
            await Send(callerA, $"GET /c4a/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}");
            using var callerB = await Connect(port);
            var watcherB = new Watcher(callerB);
            await Send(callerB, $"GET /c4b/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}");
            var first = await watcherB.WaitForAnswerAsync(0, 10);
            lines.Add(first < 0 ? "(b)'s first exchange did not complete within 10 s" : $"(b)'s first exchange read whole ({first} bytes)");
            await Task.Delay(1000);
            var closeAt = Now;
            var close = Call.Start(listener.Close);
            lines.Add($"HttpListener.Close(): {await close.WatchAsync(10)}");
            await Task.WhenAll(watcherA.WaitEndAsync(6), watcherB.WaitEndAsync(6));
            var fa = watcherA.Fate(closeAt, "the Close() call");
            var fb = watcherB.Fate(closeAt, "the Close() call", Math.Max(first, 0));
            if (fa.Contains("HTTP/1.1 200")) a200++;
            if (fb.Contains("HTTP/1.1 200")) b200++;
            lines.Add($"(a) the mid-head caller received {fa}");
            lines.Add($"(b) the idle kept-alive caller received {fb}");
            Out(RunBlock(run, lines));
        }
        Out($"  tally: a 200 head reached (a) {a200}/{Repeats} and (b) {b200}/{Repeats}");
    }

    // ------------------------------------------------------------------------------------------------ 5 and 6: long cases
    static List<LongCase> StartLongCases()
    {
        var cases = new List<LongCase>
        {
            Long(5, "5a", l => IdleCase(l, "5a", "connects and sends nothing", null, false, null, "connecting")),
            Long(5, "5b", l => IdleCase(l, "5b", "one complete exchange (GET; 200 with Content-Length: 2, read whole), then idle on keep-alive", null, true, null, "the first answer was read")),
            Long(5, "5c", l => IdleCase(l, "5c", "sends one byte of a head (G), then nothing", null, false, (_, _) => "G", "the byte was sent")),
        };
        if (IsWindows)
        {
            var five = TimeSpan.FromSeconds(5);
            cases.Add(Long(6, "6a", l => EntityBodyCase(l, "6a", null)));
            cases.Add(Long(6, "6a5", l => EntityBodyCase(l, "6a5", 5)));
            cases.Add(Long(6, "6b", l => MinSendCase(l, "6b", null)));
            cases.Add(Long(6, "6b10k", l => MinSendCase(l, "6b10k", 10_000)));
            cases.Add(Long(6, "6c", l => IdleCase(l, "6c", "HeaderWait at its default, a LATER request: one complete exchange, then a partial second head (request line and Host, no blank line)", null, true, PartialHead, "the partial head was sent")));
            cases.Add(Long(6, "6c5", l => IdleCase(l, "6c5", "HeaderWait = 5 s, a LATER request: one complete exchange, then a partial second head", tm => tm.HeaderWait = five, true, PartialHead, "the partial head was sent")));
            cases.Add(Long(6, "6c5first", l => IdleCase(l, "6c5first", "HeaderWait = 5 s, the FIRST request on a connection: a partial head (request line and Host, no blank line)", tm => tm.HeaderWait = five, false, PartialHead, "the partial head was sent")));
            cases.Add(Long(6, "6d5", l => IdleCase(l, "6d5", "IdleConnection = 5 s: one complete exchange, then idle on keep-alive", tm => tm.IdleConnection = five, true, null, "the first answer was read")));
            cases.Add(Long(6, "6d5first", l => IdleCase(l, "6d5first", "IdleConnection = 5 s: connects and sends nothing (no first request)", tm => tm.IdleConnection = five, false, null, "connecting")));
        }
        return cases;
    }

    static string PartialHead(int port, int run) => $"GET /p/{run}/2 HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}";

    static LongCase Long(int claim, string id, Func<List<string>, Task> body)
    {
        var c = new LongCase { Claim = claim };
        c.Task = Task.Run(async () =>
        {
            try { await body(c.Lines); }
            catch (Exception ex) { c.Lines.Add($"  CASE {id} FAILED: {Describe(ex)}"); }
            Progress($"long case {id} done");
        });
        return c;
    }

    // Callers that connect, maybe complete one exchange, maybe send part of a head, and then only wait.
    static async Task IdleCase(List<string> lines, string id, string title, Action<HttpListenerTimeoutManager>? configure, bool exchangeFirst, Func<int, int, string>? partial, string originName)
    {
        lines.Add($"  {id}: {title}");
        var port = FreePort();
        var listener = StartListener(port, configure);
        if (configure is not null) lines.Add($"    netsh: {HttpSysTimeouts(port)}");
        Dispatch(listener, AnswerOk);
        var start = Now;
        var callers = new List<(Socket Socket, Watcher Watcher, double Origin, long Skip)>();
        for (var run = 1; run <= Repeats; run++)
        {
            var socket = await Connect(port);
            var watcher = new Watcher(socket);
            var origin = Now;
            long skip = 0;
            if (exchangeFirst)
            {
                await Send(socket, $"GET /{id}/{run}/1 HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}");
                skip = await watcher.WaitForAnswerAsync(0, 10);
                origin = Now;
                if (skip < 0) { lines.Add($"    run {run}: the first exchange did not complete within 10 s"); skip = 0; }
            }
            if (partial is not null) { await Send(socket, partial(port, run)); origin = Now; }
            callers.Add((socket, watcher, origin, skip));
        }
        await WaitAll(callers.Select(c => c.Watcher.Loop), start + LongWindow);
        for (var i = 0; i < callers.Count; i++)
            lines.Add($"    run {i + 1}: {callers[i].Watcher.Fate(callers[i].Origin, originName, callers[i].Skip, OpenMark)}");
        lines.Add($"    summary: {Summarize(callers.Select(c => (c.Watcher, c.Origin, c.Skip)))}");
        foreach (var c in callers) c.Socket.Dispose();
        listener.Close();
    }

    // Windows: the first request's body stalls after 1,000 of 100,000 bytes; the server keeps a ReadAsync pending.
    static async Task EntityBodyCase(List<string> lines, string id, int? seconds)
    {
        lines.Add($"  {id}: EntityBody {(seconds is int s ? $"= {s} s" : "at its default")}, the FIRST request on a connection: a head with Content-Length: 100000 and 1,000 body bytes, then the caller stalls with the socket open and keeps a receive pending; the server reads and keeps a ReadAsync pending");
        var port = FreePort();
        var listener = StartListener(port, seconds is int v ? tm => tm.EntityBody = TimeSpan.FromSeconds(v) : null);
        if (seconds is not null) lines.Add($"    netsh: {HttpSysTimeouts(port)}");
        var server = new ConcurrentDictionary<int, string>();
        var origins = new ConcurrentDictionary<int, double>();
        Dispatch(listener, async ctx =>
        {
            var run = int.Parse(ctx.Request.Url!.Segments[^1], CultureInfo.InvariantCulture);
            var input = ctx.Request.InputStream;
            var buffer = new byte[Kb64];
            long got = 0;
            while (true)
            {
                int n;
                try { n = await input.ReadAsync(buffer, 0, buffer.Length); }
                catch (Exception ex) { server[run] = $"server: read {got} body bytes, then the pending ReadAsync threw {Now - origins.GetValueOrDefault(run):0.00} s after the body stalled: {Describe(ex)}"; break; }
                if (n == 0) { server[run] = $"server: read {got} body bytes, then a ReadAsync returned 0, {Now - origins.GetValueOrDefault(run):0.00} s after the body stalled"; break; }
                got += n;
            }
        });
        var start = Now;
        var callers = new List<(Socket Socket, Watcher Watcher, double Origin)>();
        for (var run = 1; run <= Repeats; run++)
        {
            var socket = await Connect(port);
            var watcher = new Watcher(socket);
            await Send(socket, $"POST /{id}/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}Content-Length: 100000{CRLF}{CRLF}{new string('a', 1000)}");
            origins[run] = Now;
            callers.Add((socket, watcher, Now));
        }
        await WaitAll(callers.Select(c => c.Watcher.Loop), start + LongWindow);
        await Task.Delay(500);
        for (var i = 0; i < callers.Count; i++)
            lines.Add($"    run {i + 1}: the caller received {callers[i].Watcher.Fate(callers[i].Origin, "the body stalled", 0, OpenMark)}; {server.GetValueOrDefault(i + 1, "server: the ReadAsync is still pending")}");
        lines.Add($"    summary: {Summarize(callers.Select(c => (c.Watcher, c.Origin, 0L)))}");
        foreach (var c in callers) c.Socket.Dispose();
        listener.Close();
    }

    // Windows: a 64 MB Content-Length answer to a first request whose caller never reads.
    static async Task MinSendCase(List<string> lines, string id, long? rate)
    {
        lines.Add($"  {id}: MinSendBytesPerSecond {(rate is long r ? $"= {r}" : "at its default (150 bytes/s)")}, the FIRST request on a connection: GET for a 64 MB Content-Length answer, written as 1,024 awaited WriteAsync calls of 64 KiB; the caller (receive buffer 16 KiB) does not read until the server's write fails or {MinSendWindow} s pass, then reads");
        var port = FreePort();
        var listener = StartListener(port, rate is long v ? tm => tm.MinSendBytesPerSecond = v : null);
        if (rate is not null) lines.Add($"    netsh: {HttpSysTimeouts(port)}");
        var writers = new ConcurrentDictionary<int, Writer>();
        Dispatch(listener, ctx =>
        {
            var run = int.Parse(ctx.Request.Url!.Segments[^1], CultureInfo.InvariantCulture);
            ctx.Response.ContentLength64 = 64L * 1024 * 1024;
            writers[run] = new Writer(ctx.Response.OutputStream, Kb64, 1024);
            return Task.CompletedTask;
        });
        var start = Now;
        var callers = new List<(Socket Socket, double Origin)>();
        for (var run = 1; run <= Repeats; run++)
        {
            var socket = await Connect(port, 16 * 1024);
            await Send(socket, $"GET /{id}/{run} HTTP/1.1{CRLF}Host: localhost:{port}{CRLF}{CRLF}");
            callers.Add((socket, Now));
        }
        var until = Now + 10;
        while (writers.Count < Repeats && Now < until) await Task.Delay(50);
        await WaitAll(writers.Values.Select(w => w.Done), start + MinSendWindow);
        // The server's side is read before the caller reads: once it reads, a stalled write moves again.
        var snapAt = Now;
        var before = Enumerable.Range(1, Repeats).Select(run => writers.GetValueOrDefault(run)).Select((w, i) => w is null ? "no context arrived" : w.Outcome(callers[i].Origin)).ToList();
        var faulted = writers.Values.Count(w => w.Faulted);
        var readers = callers.Select(c => (Watcher: new Watcher(c.Socket), At: Now)).ToList();
        await Task.WhenAll(readers.Select(r => r.Watcher.DrainAsync(3, 30)));
        for (var i = 0; i < Repeats; i++)
        {
            lines.Add($"    run {i + 1}: server, {snapAt - start:0} s into the case, before the caller reads: {before[i]}");
            lines.Add($"      then the caller read {readers[i].Watcher.Fate(readers[i].At, "it started reading")}");
            if (writers.GetValueOrDefault(i + 1) is { Faulted: false } w) lines.Add($"      the server after the caller's read: {w.Outcome(callers[i].Origin)}");
        }
        lines.Add($"    summary: the server's write faulted in {faulted}/{Repeats} before the caller read");
        foreach (var c in callers) c.Socket.Dispose();
        listener.Close();
    }

    static string Summarize(IEnumerable<(Watcher Watcher, double Origin, long Skip)> callers)
    {
        var rows = callers.Select(c => (Kind: c.Watcher.EndKind ?? "still open", T: c.Watcher.EndKind is null ? double.NaN : c.Watcher.EndAt - c.Origin, Bytes: Math.Max(0, c.Watcher.Count - c.Skip))).ToList();
        return string.Join("; ", rows.GroupBy(r => r.Kind).Select(g =>
        {
            var times = g.Where(r => !double.IsNaN(r.T)).Select(r => r.T).ToList();
            var when = times.Count == 0 ? $" when the watch ended ({LongWindow} s)" : $" at {times.Min():0.0} to {times.Max():0.0} s";
            return $"{g.Key} {g.Count()}/{rows.Count}{when}, bytes before it {string.Join("/", g.Select(r => r.Bytes))}";
        }));
    }

    static async Task WaitAll(IEnumerable<Task> tasks, double deadline)
    {
        var all = Task.WhenAll(tasks);
        var left = deadline - Now;
        if (left > 0) await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(left)));
    }

    // ------------------------------------------------------------------------------------------------ helpers
    static async Task ResetIfStillBlocked(Socket caller, Call call, List<string> lines, double origin)
    {
        if (call.Task.IsCompleted) return;
        caller.LingerState = new LingerOption(true, 0);
        caller.Close();
        await Task.WhenAny(call.Task, Task.Delay(5000));
        lines.Add($"the caller then reset its socket (zero linger); the call: {call.Later(origin, "the origin above")}");
    }

    static IEnumerable<string> RunBlock(int run, List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
            yield return (i == 0 ? $"  run {run}: " : "    ") + lines[i];
    }

    internal static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    internal static HttpListener StartListener(int port, Action<HttpListenerTimeoutManager>? configure = null)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        configure?.Invoke(listener.TimeoutManager);
        listener.Start();
        return listener;
    }

    internal static void Dispatch(HttpListener listener, Func<HttpListenerContext, Task> handle) => _ = Task.Run(async () =>
    {
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(async () => { try { await handle(ctx); } catch { } });
        }
    });

    internal static Task AnswerOk(HttpListenerContext ctx)
    {
        ctx.Response.ContentLength64 = 2;
        ctx.Response.OutputStream.Write("ok"u8);
        ctx.Response.Close();
        return Task.CompletedTask;
    }

    internal static async Task<Socket> Connect(int port, int receiveBuffer = 0)
    {
        var socket = new Socket(Target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        if (receiveBuffer > 0) socket.ReceiveBufferSize = receiveBuffer;
        await socket.ConnectAsync(new IPEndPoint(Target, port));
        return socket;
    }

    internal static async Task Send(Socket socket, string text) =>
        await socket.SendAsync(new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(text)), SocketFlags.None);

    static string Try(Action action)
    {
        try { action(); return "no exception"; }
        catch (Exception ex) { return $"THREW {Describe(ex)}"; }
    }

    internal static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var extra = e switch
            {
                HttpListenerException h => $" ErrorCode={h.ErrorCode} NativeErrorCode={h.NativeErrorCode}",
                SocketException s => $" SocketErrorCode={s.SocketErrorCode} ErrorCode={s.ErrorCode}",
                _ => "",
            };
            parts.Add($"{e.GetType().FullName} HResult=0x{e.HResult:X8}{extra} '{e.Message}'");
        }
        return string.Join(" <- inner ", parts);
    }

    // The listener's collections (by reflection) and the server side's TCP sockets on the port.
    static string Tables(HttpListener listener, int port)
    {
        var parts = new List<string>();
        foreach (var f in typeof(HttpListener).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            if (!f.Name.Contains("prefix", StringComparison.OrdinalIgnoreCase) && f.GetValue(listener) is ICollection c)
                parts.Add($"{f.Name}={c.Count}");
        if (IsWindows) parts.Add($"http.sys active requests={HttpSysActiveRequests(port)?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        parts.Add($"server-side TCP: {ServerSockets(port)}");
        return string.Join(" ", parts);
    }

    // Linux: _connections + _listenerContexts; Windows: http.sys's active requests on the port's queue.
    static int Held(HttpListener listener, int port)
    {
        if (IsWindows) return HttpSysActiveRequests(port) ?? -1;
        var total = 0;
        foreach (var name in new[] { "_connections", "_listenerContexts" })
        {
            if (typeof(HttpListener).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(listener) is ICollection c) total += c.Count;
            else return -1;
        }
        return total;
    }

    static string ServerSockets(int port)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var found = new List<string>();
                foreach (var table in new[] { "/proc/self/net/tcp", "/proc/self/net/tcp6" })
                {
                    if (!File.Exists(table)) continue;
                    foreach (var line in File.ReadLines(table).Skip(1))
                    {
                        var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (cols.Length < 10) continue;
                        var local = cols[1];
                        if (int.Parse(local[(local.LastIndexOf(':') + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) != port) continue;
                        var state = cols[3] switch
                        {
                            "01" => "ESTABLISHED", "04" => "FIN_WAIT1", "05" => "FIN_WAIT2", "06" => "TIME_WAIT", "07" => "CLOSE",
                            "08" => "CLOSE_WAIT", "09" => "LAST_ACK", "0A" => "LISTEN", "0B" => "CLOSING", var s => "state " + s,
                        };
                        if (state == "LISTEN") continue;
                        var unsent = long.Parse(cols[4].Split(':')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        found.Add($"{state} ({(cols[9] == "0" ? "no fd" : "fd held")}{(unsent > 0 ? $", {unsent} bytes unsent" : "")})");
                    }
                }
                return found.Count == 0 ? "none" : string.Join(", ", found);
            }
            var states = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                .Where(c => c.LocalEndPoint.Port == port).Select(c => c.State.ToString()).ToList();
            return states.Count == 0 ? "none" : string.Join(", ", states);
        }
        catch (Exception ex) { return $"? ({ex.GetType().Name})"; }
    }

    static string Netsh()
    {
        var psi = new ProcessStartInfo("netsh", "http show servicestate view=requestq") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return text.Replace(CRLF, LF);
    }

    // The block of netsh's request-queue view for the queue that serves the port (split on the unindented line only:
    // each queue's URL-group section repeats "Request queue name:" indented).
    static string? QueueBlock(int port) =>
        Netsh().Split(LF + "Request queue name:").FirstOrDefault(b => b.Contains($":{port}/", StringComparison.OrdinalIgnoreCase));

    static int? HttpSysActiveRequests(int port)
    {
        if (!IsWindows) return null;
        try
        {
            var block = QueueBlock(port);
            if (block is null) return -1;
            var line = block.Split(LF).FirstOrDefault(l => l.Trim().StartsWith("Active requests:", StringComparison.Ordinal));
            return line is not null && int.TryParse(line.Split(':')[1].Trim(), out var active) ? active : -1;
        }
        catch { return null; }
    }

    // The URL group's Timeouts section (the first one in the queue's block; the server session's follows it).
    static string HttpSysTimeouts(int port)
    {
        if (!IsWindows) return "";
        try
        {
            var block = QueueBlock(port);
            if (block is null) return "(queue not found)";
            var lines = block.Split(LF).Select(l => l.Trim()).ToList();
            var at = lines.IndexOf("Timeouts:");
            if (at < 0) return "(no Timeouts section)";
            return string.Join("; ", lines.Skip(at + 1).TakeWhile(l =>
                l.Contains("timeout", StringComparison.OrdinalIgnoreCase) || l.Contains("rate", StringComparison.OrdinalIgnoreCase) || l.Contains("inherited", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) { return $"(netsh failed: {ex.GetType().Name})"; }
    }

    // ------------------------------------------------------------------------------------------------ wire text
    internal static string Escape(ReadOnlySpan<byte> bytes, int max)
    {
        const char bs = (char)92;
        var sb = new StringBuilder();
        foreach (var b in bytes.Length > max ? bytes[..max] : bytes)
        {
            if (b == 13) sb.Append(bs).Append('r');
            else if (b == 10) sb.Append(bs).Append('n');
            else if (b == 92) sb.Append(bs).Append(bs);
            else if (b < 32 || b > 126) sb.Append(bs).Append('x').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            else sb.Append((char)b);
        }
        if (bytes.Length > max) sb.Append($"...(+{bytes.Length - max} bytes)");
        return sb.ToString();
    }

    internal static int? CompleteAnswerLength(ReadOnlySpan<byte> data)
    {
        var headEnd = data.IndexOf(HeadEnd);
        if (headEnd < 0) return null;
        foreach (var line in Encoding.ASCII.GetString(data[..headEnd]).Split(CRLF))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line[15..].Trim(), CultureInfo.InvariantCulture, out var length))
                return data.Length >= headEnd + 4 + length ? headEnd + 4 + length : null;
        return null;
    }

    internal static string DescribeAnswer(ReadOnlySpan<byte> data)
    {
        var headEnd = data.IndexOf(HeadEnd);
        if (headEnd < 0) return $"{data.Length} bytes, no complete head: '{Escape(data, 200)}'";
        var lines = Encoding.ASCII.GetString(data[..headEnd]).Split(CRLF);
        string? Header(string name)
        {
            foreach (var line in lines.Skip(1))
                if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim();
            return null;
        }
        var te = Header("Transfer-Encoding");
        var cl = Header("Content-Length");
        var conn = Header("Connection");
        var body = data[(headEnd + 4)..];
        string bodyText;
        if (te is not null && te.Contains("chunked", StringComparison.OrdinalIgnoreCase)) bodyText = $"chunked body: {ParseChunked(body)}";
        else if (cl is not null && long.TryParse(cl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            bodyText = body.Length == length ? $"body complete ({length} bytes)" : body.Length < length ? $"body SHORT: {body.Length} of {length} bytes" : $"body {body.Length} bytes, more than {length}";
        else bodyText = $"body {body.Length} bytes, no length";
        var headers = $"{(conn is null ? "no Connection header" : "Connection: " + conn)}, {(cl is null ? "no Content-Length" : "Content-Length: " + cl)}{(te is null ? "" : ", Transfer-Encoding: " + te)}";
        var whole = data.Length <= 320 ? $"; all of it: '{Escape(data, 320)}'" : "";
        return $"{data.Length} bytes: '{lines[0]}' ({headers}), {bodyText}{whole}";
    }

    static string ParseChunked(ReadOnlySpan<byte> body)
    {
        int pos = 0, chunks = 0;
        long data = 0;
        while (true)
        {
            var rest = body[pos..];
            var eol = rest.IndexOf(CrLf);
            if (eol < 0) return $"{chunks} chunks, {data} data bytes, then cut inside a chunk-size line ('{Escape(rest, 20)}')";
            var sizeText = Encoding.ASCII.GetString(rest[..eol]);
            var semi = sizeText.IndexOf(';');
            if (semi >= 0) sizeText = sizeText[..semi];
            if (!long.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size))
                return $"{chunks} chunks, {data} data bytes, then MALFORMED: '{Escape(rest, 12)}' where a chunk-size line belongs";
            pos += eol + 2;
            if (size == 0)
            {
                var tail = body[pos..];
                return tail.StartsWith(CrLf)
                    ? $"{chunks} chunks, {data} data bytes, then a well-formed last chunk (0, CRLF, CRLF){(tail.Length > 2 ? $" and {tail.Length - 2} more bytes" : "")}: looks COMPLETE"
                    : $"{chunks} chunks, {data} data bytes, then the last chunk without its final CRLF ('{Escape(tail, 12)}')";
            }
            var available = body.Length - pos;
            if (available < size) return $"{chunks} chunks, {data} data bytes, then cut inside chunk {chunks + 1} ({available} of its {size} bytes)";
            pos += (int)size;
            data += size;
            chunks++;
            var after = body[pos..];
            if (after.Length < 2) return $"{chunks} chunks, {data} data bytes, then cut before chunk {chunks}'s closing CRLF";
            if (!after.StartsWith(CrLf)) return $"{chunks} chunks, {data} data bytes, then MALFORMED: '{Escape(after, 12)}' where chunk {chunks}'s closing CRLF belongs";
            pos += 2;
        }
    }
}

sealed class LongCase
{
    public required int Claim { get; init; }
    public List<string> Lines { get; } = new();
    public Task Task { get; set; } = Task.CompletedTask;
}

// A call that may block (Abort(), Close()), on a thread of its own.
sealed class Call
{
    public Task Task { get; private set; } = Task.CompletedTask;
    public double StartedAt { get; private set; }
    public double EndedAt { get; private set; } = -1;
    public Exception? Error { get; private set; }

    public static Call Start(Action action)
    {
        var call = new Call { StartedAt = Probe.Now };
        call.Task = Task.Factory.StartNew(() =>
        {
            try { action(); }
            catch (Exception ex) { call.Error = ex; }
            finally { call.EndedAt = Probe.Now; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return call;
    }

    public async Task<string> WatchAsync(double seconds)
    {
        if (await Task.WhenAny(Task, Task.Delay(TimeSpan.FromSeconds(seconds))) == Task) return Ended();
        return $"BLOCKED >{seconds:0} s (had not returned {seconds:0} s after it was called)";
    }

    string Ended() => Error is null
        ? $"returned after {(EndedAt - StartedAt) * 1000:0.0} ms"
        : $"threw after {(EndedAt - StartedAt) * 1000:0.0} ms: {Probe.Describe(Error)}";

    public string Later(double origin, string originName) => !Task.IsCompleted
        ? $"STILL BLOCKED {Probe.Now - StartedAt:0.0} s after it was called"
        : EndedAt <= origin
            ? $"{Ended()}, before {originName}"
            : $"{Ended()} in all, {EndedAt - origin:0.000} s after {originName}";
}

// Awaited WriteAsync calls in a loop on a thread of its own, so a call that blocks before returning its Task shows.
sealed class Writer
{
    readonly object _gate = new();
    readonly double[] _doneAt;
    readonly int[] _sizes;
    readonly int _count;
    int _calls, _completed, _errorIndex = -1, _atAbort = -1, _longestIndex;
    long _bytesDone;
    bool _inCall, _inCallAtAbort;
    double _callStartedAt, _callReturnedAt, _longest, _finishedAt = -1;
    Exception? _error;
    string _errorWhere = "";

    public readonly record struct Stall(bool Stalled, bool InCall, double At, string Text);

    public double StartedAt { get; }
    public Task Done { get; }
    public bool Faulted { get { lock (_gate) return _error is not null; } }

    public Writer(Stream stream, int size, int count) : this(stream, Enumerable.Repeat(size, count).ToArray()) { }

    // One WriteAsync per entry of `sizes`, each awaited before the next.
    public Writer(Stream stream, int[] sizes)
    {
        _sizes = sizes;
        _count = sizes.Length;
        _doneAt = new double[_count];
        Array.Fill(_doneAt, -1);
        var buffer = new byte[sizes.Max()];
        buffer.AsSpan().Fill((byte)'x');
        StartedAt = Probe.Now;
        Done = Task.Factory.StartNew(() => Loop(stream, buffer), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    void Loop(Stream stream, byte[] buffer)
    {
        for (var i = 0; i < _count; i++)
        {
            lock (_gate) { _inCall = true; _callStartedAt = Probe.Now; _calls = i + 1; }
            Task task;
            try { task = stream.WriteAsync(buffer, 0, _sizes[i], CancellationToken.None); }
            catch (Exception ex)
            {
                lock (_gate) { _inCall = false; _error = ex; _errorIndex = i; _errorWhere = "the WriteAsync call itself threw"; _doneAt[i] = Probe.Now; }
                break;
            }
            lock (_gate)
            {
                _inCall = false;
                _callReturnedAt = Probe.Now;
                if (_callReturnedAt - _callStartedAt > _longest) { _longest = _callReturnedAt - _callStartedAt; _longestIndex = i; }
            }
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                lock (_gate) { _error = ex; _errorIndex = i; _errorWhere = "its Task faulted"; _doneAt[i] = Probe.Now; }
                break;
            }
            lock (_gate) { _completed = i + 1; _bytesDone += _sizes[i]; _doneAt[i] = Probe.Now; }
        }
        lock (_gate) _finishedAt = Probe.Now;
    }

    string Head() => $"{_completed} of {_count} writes completed ({_bytesDone} bytes)";

    public async Task<Stall> WaitForStallAsync(double quiet, double max)
    {
        var start = Probe.Now;
        var seen = -1;
        var changedAt = start;
        while (true)
        {
            await Task.Delay(50);
            int completed;
            bool finished, inCall;
            lock (_gate) { completed = _completed; finished = _finishedAt >= 0; inCall = _inCall; }
            var now = Probe.Now;
            if (finished) return new(false, false, now, $"no stall: {State()}");
            if (completed != seen) { seen = completed; changedAt = now; }
            else if (now - changedAt >= quiet) return new(true, inCall, now, State());
            if (now - start >= max) return new(false, inCall, now, $"no stall within {max:0} s: {State()}");
        }
    }

    public string State()
    {
        lock (_gate)
        {
            var head = Head();
            if (_finishedAt >= 0)
                return _error is null ? $"{head}; the loop ended" : $"{head}; write {_errorIndex + 1}: {_errorWhere}: {Probe.Describe(_error)}";
            return _inCall
                ? $"{head}; WriteAsync call {_calls} has NOT returned a Task: blocked inside the call for {Probe.Now - _callStartedAt:0.000} s"
                : $"{head}; WriteAsync call {_calls} returned its Task after {(_callReturnedAt - _callStartedAt) * 1000:0.000} ms and the Task has been pending for {Probe.Now - _callReturnedAt:0.000} s";
        }
    }

    public void MarkAbort() { lock (_gate) { _atAbort = _completed; _inCallAtAbort = _inCall; } }

    public bool PendingWriteCompletedNormally
    {
        get { lock (_gate) return _atAbort >= 0 && _atAbort < _count && _doneAt[_atAbort] >= 0 && _errorIndex != _atAbort; }
    }

    public string AfterAbort(double abortAt)
    {
        lock (_gate)
        {
            var p = _atAbort;
            if (p < 0) return "(no Abort marked)";
            if (p >= _count) return "every write had completed before the Abort";
            var where = _inCallAtAbort ? "blocked inside its WriteAsync call" : "its Task pending";
            var sb = new StringBuilder();
            if (_errorIndex == p) sb.Append($"write {p + 1} ({where} at the Abort) FAULTED {_doneAt[p] - abortAt:0.000} s after the Abort ({_errorWhere}): {Probe.Describe(_error!)}");
            else if (_doneAt[p] >= 0) sb.Append($"write {p + 1} ({where} at the Abort) completed WITHOUT an exception {_doneAt[p] - abortAt:0.000} s after the Abort");
            else sb.Append($"write {p + 1} ({where} at the Abort) still pending {Probe.Now - abortAt:0.000} s after the Abort");
            var later = 0;
            var lastAt = -1.0;
            for (var i = p + 1; i < _count; i++)
                if (_doneAt[i] >= 0 && i != _errorIndex) { later++; lastAt = _doneAt[i]; }
            if (later > 0) sb.Append($"; then {later} more WriteAsync calls completed without an exception, the last {lastAt - abortAt:0.000} s after the Abort");
            if (_errorIndex > p) sb.Append($"; then write {_errorIndex + 1} failed ({_errorWhere}): {Probe.Describe(_error!)}");
            sb.Append(_finishedAt >= 0 ? "; the write loop has ended" : "; the write loop is still running");
            return sb.ToString();
        }
    }

    public string LongestCallText { get { lock (_gate) return $"{_longest * 1000:0.000} ms (call {_longestIndex + 1})"; } }

    // For the MinSendRate cases: when the write failed, relative to the request and to the last completed write.
    public string Outcome(double origin)
    {
        lock (_gate)
        {
            var head = Head();
            var lastDone = _completed > 0 ? _doneAt[_completed - 1] : StartedAt;
            var last = _completed > 0 ? $", the last {lastDone - origin:0.00} s after the request" : "";
            if (_error is not null)
                return $"{head}{last}; write {_errorIndex + 1} FAULTED {_doneAt[_errorIndex] - origin:0.00} s after the request, {_doneAt[_errorIndex] - lastDone:0.00} s after {(_completed > 0 ? "the last completed write" : "the first call")} ({_errorWhere}): {Probe.Describe(_error)}";
            if (_finishedAt >= 0) return $"{head}: every write completed";
            return $"{State()}{last}";
        }
    }
}

// A caller's receive loop: every byte with its arrival time, then FIN or RST.
sealed class Watcher
{
    readonly Socket _socket;
    readonly object _gate = new();
    readonly MemoryStream _bytes = new();
    readonly List<(double At, long EndOffset)> _arrivals = new();
    double _lastByteAt = -1, _endAt = -1;
    string? _end;

    public Task Loop { get; }

    public Watcher(Socket socket)
    {
        _socket = socket;
        Loop = Task.Run(RunAsync);
    }

    async Task RunAsync()
    {
        var buffer = new byte[Probe.Kb64];
        try
        {
            while (true)
            {
                var n = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
                var now = Probe.Now;
                lock (_gate)
                {
                    if (n == 0) { _end = "FIN"; _endAt = now; return; }
                    _bytes.Write(buffer, 0, n);
                    _arrivals.Add((now, _bytes.Length));
                    _lastByteAt = now;
                }
            }
        }
        catch (SocketException ex)
        {
            var now = Probe.Now;
            lock (_gate)
            {
                _end = ex.SocketErrorCode switch
                {
                    SocketError.ConnectionReset => "RST",
                    SocketError.OperationAborted => "(closed by the probe)",
                    var e => $"SocketException {e}",
                };
                _endAt = now;
            }
        }
        catch (ObjectDisposedException) { lock (_gate) { _end = "(closed by the probe)"; _endAt = Probe.Now; } }
        catch (Exception ex) { lock (_gate) { _end = $"{ex.GetType().Name} '{ex.Message}'"; _endAt = Probe.Now; } }
    }

    public long Count { get { lock (_gate) return _bytes.Length; } }
    public string? EndKind { get { lock (_gate) return _end; } }
    public double EndAt { get { lock (_gate) return _endAt; } }

    public async Task<bool> WaitEndAsync(double seconds) => await Task.WhenAny(Loop, Task.Delay(TimeSpan.FromSeconds(seconds))) == Loop;

    // Until FIN or RST, or `idle` seconds without a byte, or `max` seconds in all.
    public async Task DrainAsync(double idle, double max)
    {
        var start = Probe.Now;
        while (!Loop.IsCompleted)
        {
            await Task.Delay(50);
            double last;
            lock (_gate) last = Math.Max(_lastByteAt, start);
            var now = Probe.Now;
            if (now - last >= idle || now - start >= max) return;
        }
    }

    // Waits for a whole Content-Length answer that starts at `from`; returns the offset just after it, or -1.
    public async Task<long> WaitForAnswerAsync(long from, double max)
    {
        var until = Probe.Now + max;
        while (Probe.Now < until && !Loop.IsCompleted)
        {
            byte[] data;
            lock (_gate) data = _bytes.ToArray();
            if (data.Length > from && Probe.CompleteAnswerLength(data.AsSpan((int)from)) is int length) return from + length;
            await Task.Delay(20);
        }
        return -1;
    }

    public string Fate(double origin, string originName, long skip = 0, double openMark = 0)
    {
        byte[] data;
        string? end;
        double endAt, firstAt = -1;
        lock (_gate)
        {
            data = _bytes.ToArray();
            end = _end;
            endAt = _endAt;
            foreach (var (at, endOffset) in _arrivals)
                if (endOffset > skip) { firstAt = at; break; }
        }
        var got = data.Length <= skip
            ? "no bytes"
            : $"{Probe.DescribeAnswer(data.AsSpan((int)skip))} (first byte {firstAt - origin:0.000} s after {originName})";
        string tail;
        if (end is null) tail = $"no FIN or RST: still open {Probe.Now - origin:0.0} s after {originName}";
        else
        {
            var t = endAt - origin;
            tail = $"{end} {t:0.000} s after {originName}{(openMark > 0 && t > openMark ? $" (so still open at {openMark:0} s)" : "")}";
        }
        return $"{got}; then {tail}";
    }
}
