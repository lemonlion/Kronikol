"""The audit's red tree: 4.26.1 (09369515) with the patch's tests and only the seams they need, none of its behaviour.

    python red_stubs_audit.py <red tree> <patch tree>

ProxyTap gains its four test seams as no-ops in 4.26.1's flow: the accept loop waits through GetContext and still ends
on whatever the wait throws (Pause is never called), Received runs before the hand-off while the request is still tracked
only in its handler, and the answer's BeforeLetGo runs where 4.26.1 aborts. TcpTap's facts run on 4.26.1's own helpers
(a write watched by an entry of its own): TcpTapStallTests is 4.26.1's file with the patch's new facts added, since the
patch's helpers read the pair's write marks, which 4.26.1 does not have. The other test files are the patch's.
"""
import pathlib
import re
import sys

red = pathlib.Path(sys.argv[1])
patch = pathlib.Path(sys.argv[2])


def read(tree, rel):
    s = (tree / rel).read_bytes().decode("utf-8")
    assert "\r\n" not in s, rel
    return s


def write(rel, s):
    (red / rel).write_bytes(s.encode("utf-8"))


def replace_once(s, old, new, what):
    assert s.count(old) == 1, (what, s.count(old))
    return s.replace(old, new)


# ---- ProxyTap seams, no behaviour
PT = "src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"
s = read(red, PT)
s = replace_once(s, "    internal StallWatch? Watch => _watch;\n", """    internal StallWatch? Watch => _watch;

    internal Func<HttpListener, Task<HttpListenerContext>> GetContext { get; set; } = static listener => listener.GetContextAsync();

    internal Func<TimeSpan, CancellationToken, Task> Pause { get; set; } = static (pause, ct) => Task.Delay(pause, ct);

    internal Action<HttpListenerContext>? Received { get; set; }

    internal Action? BeforeCallerLetGo { get; set; }
""", "seams")
s = replace_once(s, "                context = await _listener.GetContextAsync().WaitAsync(_listenerClosed.Token).ConfigureAwait(false);",
                 "                context = await GetContext(_listener).WaitAsync(_listenerClosed.Token).ConfigureAwait(false);", "wait")
s = replace_once(s, "            _ = Task.Run(() => GuardedHandleAsync(context), CancellationToken.None);",
                 "            Received?.Invoke(context);\n            _ = Task.Run(() => GuardedHandleAsync(context), CancellationToken.None);", "received")
s = replace_once(s, "        var answer = new CallerAnswer(context.Response);",
                 "        var answer = new CallerAnswer(context.Response) { BeforeLetGo = BeforeCallerLetGo };", "answer")
s = replace_once(s, "        public int StatusIfUnclosed { get; set; } = 502;\n",
                 "        public int StatusIfUnclosed { get; set; } = 502;\n\n        public Action? BeforeLetGo { get; init; }\n", "init")
s = replace_once(s, "                Response.Abort();", "                BeforeLetGo?.Invoke();\n                Response.Abort();", "abort")
write(PT, s)

# ---- the patch's test files, as they are
for rel in [
    "tests/Kronikol.Tests.ProxyTap/StallWatchTests.cs",
    "tests/Kronikol.Tests.ProxyTap/ProxyTapLifecycleTests.cs",
    "tests/Kronikol.Tests.ProxyTap/ProxyTapStoppingTests.cs",
    "tests/Kronikol.Tests.ProxyTap/ProxyTapStallTests.cs",
    "tests/Kronikol.Tests.ProxyTap/ProxyTapTests.cs",
    "tests/Kronikol.Tests.Otlp/HttpRequestReaderTests.cs",
    "tests/Kronikol.Tests.Otlp/OtlpTapRefusalTests.cs",
    "tests/Kronikol.Tests.Otlp/OtlpTapStallTests.cs",
    "tests/Kronikol.Tests.Otlp/OtlpTapLogTests.cs",
]:
    write(rel, read(patch, rel))

# ---- TcpTapStallTests: 4.26.1's file and helpers, with the patch's new facts and its counting clock
TS = "tests/Kronikol.Tests.TcpTap/TcpTapStallTests.cs"
old = read(red, TS)
new = read(patch, TS)
helpers = "    // ------------------------------------------------------------------ helpers\n"
start = new.index("    [Fact]\n    public async Task A_start_that_cannot_bind_leaves_no_heartbeat_running()")
facts = new[start:new.index(helpers)]
clock_start = new.index("    /// <summary>The system's clock, counting the timers made on it that have not been disposed.</summary>")
clock_end = new.index("\n    }\n", new.index("private sealed class Counted", clock_start)) + len("\n    }\n")
clock = new[clock_start:clock_end]
old = replace_once(old, helpers, facts + helpers, "facts")
old = replace_once(old, "using Kronikol.Extensions.TcpTap;\n", "using Kronikol.Extensions.TcpTap;\nusing Kronikol.Extensions.TcpTap.Protocols;\n", "using")
jumping_end = old.index("            inner.CreateTimer(callback, state, dueTime, period);\n    }\n") + len("            inner.CreateTimer(callback, state, dueTime, period);\n    }\n")
old = old[:jumping_end] + "\n" + clock + old[jumping_end:]
write(TS, old)
print("ok")
