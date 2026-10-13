# R2's wiki edit (plan section 6.3), applied after 4.19.3 is published.
# usage: python r2_wiki.py <wiki worktree>
# Each edit replaces one exact string that must match once; the page keeps the line endings its blob has.
import os
import sys

wiki = sys.argv[1]


def replace(page, old, new):
    path = os.path.join(wiki, page)
    data = open(path, "rb").read()
    crlf = b"\r\n" in data
    text = data.decode("utf-8").replace("\r\n", "\n")
    if text.count(old) != 1:
        raise SystemExit(f"{page}: the text occurs {text.count(old)} times: {old[:60]!r}")
    text = text.replace(old, new)
    if crlf:
        text = text.replace("\n", "\r\n")
    open(path, "wb").write(text.encode("utf-8"))
    print(f"{page}: edited")


replace("Integration-Otlp-Extension.md",
        "| `MaxRequestBytes` | 32 MiB | Bigger exports are answered `413` (and drained, not buffered). |\n",
        "| `MaxRequestBytes` | 32 MiB | Bigger exports are answered `413` (and drained, not buffered). A chunk size past `int` is "
        "answered `413` too, and the connection closed, since a chunk that large is not drained (4.19.3). |\n")

replace("Integration-Otlp-Extension.md",
        "Counters on the tap: `RequestsReceived`, `SpansReceived`, `SpansMapped`, `SpansIgnored`, `PayloadsDropped`, "
        "`UnauthenticatedRequests`, `ForwardFailures`.\n",
        "Counters on the tap: `RequestsReceived`, `SpansReceived`, `SpansMapped`, `SpansIgnored`, `PayloadsDropped`, "
        "`PayloadsRejected` (answered `413`), `PayloadsFailed` (accepted, then lost decoding, mapping or writing to the sink), "
        "`UnauthenticatedRequests`, `ForwardFailures`. `ForwardFailures` counts exports the real collector could not take, "
        "answered `502`; since 4.19.3 an exporter that hangs up before its answer, and a tap disposed in the middle of a "
        "forward, are not counted. A chunked export cut short or malformed is not received: like a `Content-Length` export "
        "cut short, it gets no answer and is neither counted nor mapped (4.19.3).\n")
