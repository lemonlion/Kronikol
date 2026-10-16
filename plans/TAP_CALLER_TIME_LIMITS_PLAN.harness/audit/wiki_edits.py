"""The #173 audit's wiki edits (4.26.3), in a wiki worktree of its own.

    python wiki_edits.py <wiki tree>
"""
import pathlib
import sys

tree = pathlib.Path(sys.argv[1])


def edit(page, pairs):
    p = tree / page
    s = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in s
    if crlf:
        s = s.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (page, old[:80], s.count(old))
        s = s.replace(old, new)
    assert "—" not in "".join(new for _, new in pairs), "em-dash in new text"
    if crlf:
        s = s.replace("\n", "\r\n")
    p.write_bytes(s.encode("utf-8"))


edit("Integration-TcpTap-Extension.md", [
    (
        "`null` or `Timeout.InfiniteTimeSpan` for none. See [Time limits](#time-limits). |",
        "`null` or `Timeout.InfiniteTimeSpan` for none, and zero or less fails at start. See [Time limits](#time-limits). |",
    ),
    (
        "(that side stopped reading); `null` or `Timeout.InfiniteTimeSpan` for none. |",
        "(that side stopped reading); `null` or `Timeout.InfiniteTimeSpan` for none, and zero or less fails at start. |",
    ),
    (
        "  client finished sending and the server moved nothing for 00:01:40`) and reported by `Diagnostics()`. A pair a limit\n"
        "  closes has lost none of its capture, so no `OnCaptureDegraded` event is raised for it.\n",
        "  client finished sending and the server moved nothing for 00:01:40`) and reported by `Diagnostics()`. A limit raises\n"
        "  no `OnCaptureDegraded` event of its own, but a pair it closes with a command unanswered or a reply partly received\n"
        "  has lost that interaction, and the decoder reports it as `ConnectionClosedMidMessage`, as for any connection that\n"
        "  closes then (until 4.26.3 this page said such a close lost nothing).\n"
        "- A pair is closed once. Since **4.26.3**, when two limits come due in the same second, or a limit and the reaper, one\n"
        "  close is counted, and a half-closed pair whose other side has stopped reading is counted as a stalled write; before,\n"
        "  each of them could count it, and about half of such pairs were counted as half-closed.\n",
    ),
    (
        "- No limit fires while a debugger is attached to the tap's process; the reaper still acts, as it always has. `null` or\n"
        "  `Timeout.InfiniteTimeSpan` turns a limit off, and zero or a negative value fails at start.\n",
        "- No limit fires while a debugger is attached to the tap's process; the reaper still acts, as it always has. A client\n"
        "  or server paused in a debugger in a process of its own is closed after the limit, so set the limits to `null` for\n"
        "  such a session. `null` or `Timeout.InfiniteTimeSpan` turns `HalfClosedTimeout` or `StalledWriteTimeout` off, and zero\n"
        "  or a negative value fails at start; `ReapStuckConnectionsAfter` is off while it is `null`, its default, and any value\n"
        "  of zero or less fails at start, `Timeout.InfiniteTimeSpan` among them.\n",
    ),
    (
        "  it unanswered, so a jump of the machine's clock cannot age it, and disposing a tap no longer waits up to a second for a\n"
        "  reaper's tick.\n",
        "  it unanswered, so a jump of the machine's clock cannot age it, and disposing a tap no longer waits up to a second for a\n"
        "  reaper's tick. Since 4.26.3 a decoder whose `OldestUnansweredSince` blocks holds up one thread and no other pair's\n"
        "  limits.\n",
    ),
    (
        "tap-di-redis: 1 stuck connection(s) reaped (a command unanswered for 00:00:30) so a wedged client could reconnect\n",
        "tap-di-redis: 1 stuck connection(s) reaped (a command unanswered for 00:00:30) so a wedged client could reconnect\n"
        "tap-di-redis: 1 connection(s) closed after one side finished sending and the other moved nothing for HalfClosedTimeout (00:01:40)\n"
        "tap-di-redis: 1 connection(s) closed because a write to one side did not complete for StalledWriteTimeout (00:01:40): that side had stopped reading\n",
    ),
])

edit("Integration-ProxyTap-Extension.md", [
    (
        "  forwarded, answered 408 and closed`) and reported by `Diagnostics()`",
        "  forwarded, answered 408 and closed`, which on Windows ends `not forwarded, connection reset`) and reported by\n"
        "  `Diagnostics()`",
    ),
    (
        "a **chunked** request whose caller stops sending before the last chunk",
        "a **chunked** request whose caller hangs up (sends its FIN) before the last chunk",
    ),
    (
        "\n### A caller that hangs up\n",
        "\nA tap starts once. Since **4.26.3** a second `StartAsync` throws `InvalidOperationException`, one after `DisposeAsync`\n"
        "throws `ObjectDisposedException`, and a start and a dispose that race leave nothing listening. The token `StartAsync`\n"
        "takes aborts a start that has not begun and nothing after it; before 4.26.3 it was linked into the tap's lifetime, so\n"
        "cancelling it once the tap had started left the tap listening and answering every request `503`. A wait for a request\n"
        "that fails while the tap runs is logged and tried again, with pauses of up to 1 s; before 4.26.3 it ended the tap's\n"
        "accept loop, and the tap went on listening with nothing to take a request. A request the tap has taken but not yet\n"
        "handed on when a stop begins is settled like any other, never left to the listener's close, which answered it\n"
        "`200 OK` on Linux and macOS.\n"
        "\n### A caller that hangs up\n",
    ),
])

edit("Integration-Otlp-Extension.md", [
    (
        "Spans that are not calls ",
        "A request whose body's length cannot be told is refused before any of it is read, and its connection closed: a\n"
        "`Content-Length` that is not digits alone is answered `400` (4.22.2), and since 4.26.3 a `Transfer-Encoding` that\n"
        "does not end in `chunked` is answered `400` and one with another coding before `chunked` `501` (read before as no\n"
        "body, or as chunked, so its bytes were taken for the next request). An empty line before a request line, which a\n"
        "client may send after a body, is passed over, as HTTP asks.\n"
        "\n"
        "Spans that are not calls ",
    ),
    (
        "`MaxRequestBytes`, `QueueCapacity` and, since 4.25.0, `CallerStallTimeout` and `KeepAliveTimeout` bound the rest.",
        "`MaxRequestBytes` and `QueueCapacity` bound what an export can make the tap hold. Since 4.25.0 `CallerStallTimeout`\n"
        "and `KeepAliveTimeout` close a connection that goes silent, but a sender that moves a byte now and then is not\n"
        "silent, and nothing caps how many connections are open: the secret is what keeps such a caller out, and since 4.26.3\n"
        "a head without it costs the tap no more than the head's own size to read.",
    ),
])
print("ok")
