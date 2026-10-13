# The Kronikol4J divergence ledger lines for this plan's releases (plan section 6.4), each added after its publication.
# usage: python ledger.py <Kronikol4J worktree> R1|R2
# Appends the release's line after the ledger's newest entry (the last "- **... (.NET 4.x.y, date, ..." line).
import os
import re
import sys

LINES = {
    "R1": "- **A proxy tap lets go of a caller that hangs up, and does not count one as a failed forward (.NET 4.19.2, "
          "2026-10-13, `PROXY_TAP_HANG_UP_PLAN.md` R1, #172).** Nothing to port: the port has no proxy tap. For whoever "
          "builds one: end every response the tap does not answer in one place that every exit passes through, since a "
          "server that keeps a connection until its response is closed or aborted (as .NET's managed `HttpListener` does) "
          "otherwise holds a hung-up caller's socket; set a failure status before aborting where an abort writes an unsent "
          "head; and decide whose failure it was by the step that failed (reading the request, forwarding, writing the "
          "answer), not by the exception's type, which differs by platform and by how the caller closed.",
    "R2": "- **The OTLP tap reads an exporter that hangs up as a hang-up, and a chunked export cut short as no export (.NET "
          "4.19.3, 2026-10-13, `PROXY_TAP_HANG_UP_PLAN.md` R2, #172's plan).** Nothing to port: the port has no OTLP tap. "
          "For whoever builds one: write the collector's answer outside the forward's failure handling, and treat the tap's "
          "own cancellation as stopping, not a failed forward; read a chunked body through its last chunk and its "
          "trailers' blank line, so that a stream ending first, a size line that is not hex or a chunk without its CRLF is "
          "no request; and parse a chunk size so that a value past the integer range is too large, not negative.",
}

root, release = sys.argv[1], sys.argv[2]
path = os.path.join(root, "docs", "REMAINING_PARITY.md")
data = open(path, "rb").read()
crlf = b"\r\n" in data
lines = data.decode("utf-8").replace("\r\n", "\n").split("\n")
entry = re.compile(r"^- \*\*.*\(\.NET 4\.\d+\.\d+, \d{4}-\d{2}-\d{2}")
newest = max(i for i, line in enumerate(lines) if entry.match(line))
if any(LINES[release][:60] in line for line in lines):
    raise SystemExit(f"{release}'s line is already in the ledger")
lines.insert(newest + 1, LINES[release])
text = "\n".join(lines)
if crlf:
    text = text.replace("\n", "\r\n")
open(path, "wb").write(text.encode("utf-8"))
print(f"{release}: added after line {newest + 1}")
