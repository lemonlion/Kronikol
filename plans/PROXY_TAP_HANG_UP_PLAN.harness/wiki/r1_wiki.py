# R1's wiki edits (plan section 6.3), applied after 4.19.2 is published.
# usage: python r1_wiki.py <wiki worktree>
# Each edit is an exact-string insertion that must match once; the pages keep the line endings their blobs have.
import os
import sys

wiki = sys.argv[1]


def edit(page, anchor, insert, after=True):
    path = os.path.join(wiki, page)
    data = open(path, "rb").read()
    crlf = b"\r\n" in data
    text = data.decode("utf-8").replace("\r\n", "\n")
    if text.count(anchor) != 1:
        raise SystemExit(f"{page}: the anchor occurs {text.count(anchor)} times: {anchor[:60]!r}")
    text = text.replace(anchor, anchor + insert if after else insert + anchor)
    if crlf:
        text = text.replace("\n", "\r\n")
    open(path, "wb").write(text.encode("utf-8"))
    print(f"{page}: edited")


edit("Integration-ProxyTap-Extension.md",
     "failed the disposal with `HttpListenerException: Address already in use`, in a test that had already passed (#74).\n",
     """
### A caller that hangs up

Since **4.19.2** a caller that goes away is let go at once. On Linux and macOS the `HttpListener` behind the tap holds a
connection until its response is closed or aborted, and before 4.19.2 a caller that hung up while its answer was being
written, in the middle of its body, or before a failed forward was answered kept its connection open until the tap was
disposed (#172). Windows (http.sys) drops such a request itself.

- A request that never arrives whole, because its caller hung up or stopped sending mid-body, is not forwarded, not
  recorded and not counted. A caller still listening is answered `400 Bad Request`, as http.sys answers it on Windows.
- A caller that hangs up while its answer is being written: the exchange is still recorded and counted as handled.
- A forward that fails is answered `502 Bad Gateway` and counted in `ForwardFailures`, whether or not its caller is still
  there, and `ProxyTap.Diagnostics()` reports the count (see [[Diagnostics and Debugging]]).
""")

edit("Integration-ProxyTap-Extension.md",
     "- The tap is a participant in the trace: latencies you see include the tap's own (small) overhead.\n",
     "- On Linux and macOS a **chunked** request whose caller stops sending before the last chunk reaches the service as if\n"
     "  whole: the managed `HttpListener` ends the read without an error, and nothing on the request tells such a body from a\n"
     "  whole one (dotnet/runtime#135680). On Windows http.sys answers it `400` and nothing is forwarded.\n")

edit("Diagnostics-and-Debugging.md",
     "The entries are carried **verbatim** and land in four places:\n",
     "`ProxyTap`'s entry for requests it could not forward leaves out a caller that hangs up before its request has arrived "
     "whole, since nothing was forwarded (4.19.2). ",
     after=False)
