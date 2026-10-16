"""Adds 4.26.3's line to Kronikol4J's ledger, after 4.26.2's.

    python ledger_audit.py <Kronikol4J tree>
"""
import pathlib
import sys

p = pathlib.Path(sys.argv[1]) / "docs" / "REMAINING_PARITY.md"
s = p.read_bytes().decode("utf-8")
assert "\r\n" not in s
anchors = [line for line in s.split("\n") if line.startswith("- **") and "(.NET 4.26.2" in line]
assert len(anchors) == 1, anchors
assert "(.NET 4.26.3" not in s
start = s.index(anchors[0])
end = s.index("\n", start) + 1
line = (
    "- **The taps' time limits, audited (.NET 4.26.3, 2026-10-16, `TAP_CALLER_TIME_LIMITS_PLAN.md`, #173).** Nothing to "
    "port: the port has no network tap. For whoever builds one: make a tap's timer only once its port is bound; mark a "
    "pending write with a timestamp, not an object per write; close a connection once, whichever limit (or the reaper) "
    "comes first, and when a half-closed pair's other side has stopped reading call it a stalled write; skip a condition "
    "check still running rather than wait on it; for an HTTP receiver, end a phase the limit cut as cut even when the "
    "request is already buffered, count a refusal only once its head is the receiver's, pass over one empty line before a "
    "request line, refuse a Transfer-Encoding other than chunked alone (400, or 501 for a coding before chunked) and join "
    "repeated headers once; for an HTTP proxy, start once with the start token governing the start alone, keep taking "
    "requests after a failed wait, track a request before handing it on, and keep a HEAD answer's Content-Length. Report "
    "output is unchanged\n"
)
assert "—" not in line
s = s[:end] + line + s[end:]
p.write_bytes(s.encode("utf-8"))
print("ok")
