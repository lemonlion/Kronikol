# Writes one mutated copy of a source file per mutant, from the worktree's current (green) files.
# usage: python make_mutants.py <worktree> <out dir> [id prefix: M for R1, N for R2]
# Each mutant replaces one exact string that must occur once; out/<id>/<path> is the mutated file, out/<id>.txt says
# what it is and which fact should catch it.
import os
import sys

wt, out = sys.argv[1], sys.argv[2]
PT = "src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"
OT = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"
RR = "src/Kronikol.Extensions.Otlp/HttpRequestReader.cs"

BODY_READ_TRY = """            try
            {
                using var buffer = new MemoryStream();
                var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
                await body.CopyToAsync(buffer).ConfigureAwait(false);
                requestBody = buffer.ToArray();
            }
            catch (Exception)
            {
                // The request never arrived whole: the caller hung up or half-closed mid-body, sent a broken chunked
                // body, or the tap is stopping. The exception says which only by platform and by how the caller closed
                // (an IOException for a reset on Linux and macOS, an HttpListenerException for a FIN there and for
                // everything on Windows), so the step decides, not the type. Nothing reached the upstream: no forward
                // failure, and a 400 for a caller still listening.
                answer.StatusIfUnclosed = 400;
                return;
            }
"""

BODY_READ_PLAIN = """            using var buffer = new MemoryStream();
            var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
            await body.CopyToAsync(buffer).ConfigureAwait(false);
            requestBody = buffer.ToArray();
"""

TRAILER_LOOP = """        var trailerBytes = 0;
        while (true)
        {
            var trailer = await ReadLineAsync(ct).ConfigureAwait(false);
            if (trailer is null)
                return null;
            if (trailer.Length == 0)
                break;
            trailerBytes += trailer.Length + 2;
            if (trailerBytes > MaxHeaderBytes)
                return null;
        }
"""

MUTANTS = [
    # ---- R1 (ProxyTap)
    ("M01", PT, "drop EndUnclosed() from the finally", "R1-1, R1-2, R1-3, R1-4 (Linux)",
     "            answer.EndUnclosed();\n            Interlocked.Decrement(ref _inflight);",
     "            Interlocked.Decrement(ref _inflight);"),
    ("M02", PT, "drop the status set before Abort()", "R1-3 (Linux: the caller hears 200), R1-5 (Linux)",
     "                Response.StatusCode = StatusIfUnclosed;\n",
     "                _ = StatusIfUnclosed;\n"),
    ("M03", PT, "the body read's catch takes a type filter (IOException or HttpListenerException)", "R1-5",
     "            catch (Exception)\n            {\n                // The request never arrived whole",
     "            catch (Exception ex) when (ex is IOException or HttpListenerException)\n            {\n                // The request never arrived whole"),
    ("M04", PT, "Abort() unguarded", "R1-6 after the listener closed (Windows)",
     "            try\n            {\n                Response.Abort();\n            }\n            catch (Exception)\n            {\n                // On Windows the listener is closed (the tap is stopping), and its request queue with it.\n            }",
     "            Response.Abort();"),
    ("M05", PT, "the status setter unguarded", "R1-6 after Close(), after Abort()",
     "            try\n            {\n                Response.StatusCode = StatusIfUnclosed;\n            }\n            catch (Exception)\n            {\n                // The response is closed (the setter does not throw once the head has gone).\n            }",
     "            Response.StatusCode = StatusIfUnclosed;"),
    ("M06", PT, "no catch of its own on the body read (the finally still aborts)", "R1-2 RST alone (Linux: counted), R1-5",
     BODY_READ_TRY, BODY_READ_PLAIN),
    ("M07", PT, "the body read's catch leaves the status at 502", "R1-3 (Linux), R1-5 (Linux)",
     "                answer.StatusIfUnclosed = 400;\n                return;",
     "                return;"),
    ("M08", PT, "the body read's catch forwards what arrived", "R1-2, R1-3, R1-5 (the upstream sees a request)",
     "                answer.StatusIfUnclosed = 400;\n                return;",
     "                answer.StatusIfUnclosed = 400;"),
    ("M09", PT, "Abort() whether or not the tap closed the response", "none expected: Abort() after Close() does nothing on .NET 10 (plan 4.1)",
     "            if (Closed)\n                return;\n\n",
     ""),
    ("M10", PT, "the 502 closes the response without marking it closed", "none expected: the same as M09 for the 502",
     "            response.OutputStream.Write(payload);\n            answer.Close();",
     "            response.OutputStream.Write(payload);\n            response.Close();"),
    ("M11", PT, "an IOException anywhere in the exception's chain is a hang-up", "R1-7",
     "        catch (Exception ex)\n        {\n            Interlocked.Increment(ref _forwardFailures);",
     "        catch (Exception ex) when (ex is IOException || ex.InnerException is IOException)\n        {\n        }\n        catch (Exception ex)\n        {\n            Interlocked.Increment(ref _forwardFailures);"),
    ("M12", PT, "every failure is a hang-up (the catch-all stops counting)", "R1-4 (Linux), R1-7",
     "            Interlocked.Increment(ref _forwardFailures);\n            _options.Log?.Invoke($\"[{_options.DisplayName}] proxy-tap error: {ex.Message}\");\n            TryRespondBadGateway(answer, ex.Message);",
     "            TryRespondBadGateway(answer, ex.Message);"),
    # ---- R2 (OtlpTap, HttpRequestReader)
    ("N01", OT, "the answer write counted as a forward failure again", "R2-1",
     "        await WriteResponseAsync(stream, status, reason, contentType, body, request.KeepAlive, ct, contentEncoding).ConfigureAwait(false);\n    }",
     "        try\n        {\n            await WriteResponseAsync(stream, status, reason, contentType, body, request.KeepAlive, ct, contentEncoding).ConfigureAwait(false);\n        }\n        catch (Exception ex) when (ex is IOException or SocketException)\n        {\n            Interlocked.Increment(ref _forwardFailures);\n            _options.Log?.Invoke($\"[{_options.DisplayName}] otlp-tap forward failed: {ex.Message}\");\n        }\n    }"),
    ("N02", OT, "the tap's own cancellation counted as a forward failure", "R2-2",
     "catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or IOException or SocketException) && !ct.IsCancellationRequested)",
     "catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)"),
    ("N03", RR, "an end of stream before the last chunk returns the body read so far", "R2-3 no zero-size chunk, size line over 1,024 bytes",
     "            var sizeLine = await ReadLineAsync(ct).ConfigureAwait(false);\n            if (sizeLine is null)\n                return null;",
     "            var sizeLine = await ReadLineAsync(ct).ConfigureAwait(false);\n            if (sizeLine is null)\n                return (body.ToArray(), tooLarge, true);"),
    ("N04", RR, "a size line that is not hex returns the body read so far", "R2-3 not hex",
     "out var size))\n                return null;",
     "out var size))\n                return (body.ToArray(), tooLarge, true);"),
    ("N05", RR, "one line read as the whole trailer section", "R2-5, R2-3 trailers past 64 KiB",
     TRAILER_LOOP,
     "        await ReadLineAsync(ct).ConfigureAwait(false);\n"),
    ("N06", RR, "no check for a size past int", "R2-6",
     "            if (size > int.MaxValue)\n                return ([], true, false);\n",
     ""),
    ("N07", RR, "the size parse wraps past long (no saturation)", "R2-6 100000000000000000",
     "            if (size <= int.MaxValue)\n                size = size * 16 + digit;",
     "            size = size * 16 + digit;"),
    ("N08", RR, "a chunk's CRLF not checked", "R2-3 chunk data longer than its size",
     "            if (await ReadLineAsync(ct).ConfigureAwait(false) is not \"\")\n                return null;",
     "            await ReadLineAsync(ct).ConfigureAwait(false);"),
    ("N09", RR, "no cap on the trailer section", "R2-3 trailers past 64 KiB",
     "            if (trailerBytes > MaxHeaderBytes)\n                return null;\n",
     ""),
    ("N10", RR, "a size past int keeps the connection", "R2-6 (the exporter is not let go)",
     "                return ([], true, false);",
     "                return ([], true, true);"),
    ("N11", RR, "a size past int is not a request at all (no 413)", "R2-6",
     "                return ([], true, false);",
     "                return null;"),
]

os.makedirs(out, exist_ok=True)
only = sys.argv[3] if len(sys.argv) > 3 else ""
for mid, path, what, killers, old, new in MUTANTS:
    if not mid.startswith(only):
        continue
    with open(os.path.join(wt, path), encoding="utf-8", newline="") as f:
        text = f.read()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{mid}: the text to replace occurs {count} times in {path}")
    target = os.path.join(out, mid, path)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8", newline="") as f:
        f.write(text.replace(old, new))
    with open(os.path.join(out, mid + ".txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write(f"{mid} {path}: {what}. Expected to be caught by: {killers}\n")
print(f"{len(MUTANTS)} mutants written to {out}")
