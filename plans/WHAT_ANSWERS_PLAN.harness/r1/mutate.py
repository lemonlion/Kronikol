# R1's mutations (plans/WHAT_ANSWERS_PLAN.md 6.1): each is applied alone, the four classes nearest the change run, and the
# file restored. A mutation is caught when at least one fact fails. usage: python -I mutate-r1.py <worktree> > out.txt
import os, re, subprocess, sys

root = sys.argv[1]
chain = "src/Kronikol/Tracking/HandlerChain.cs"
handler = "src/Kronikol/Tracking/TestTrackingMessageHandler.cs"
classes = "FullyQualifiedName~HandlerTestDoubleTests|FullyQualifiedName~HandlerChainTests|FullyQualifiedName~TestTrackingMessageHandlerTests|FullyQualifiedName~CallOwnSpanThroughTheHandlerTests"

mutations = [
    ("the walk stops at the first non-delegating handler", chain,
     "foreach (var field in FieldsOf(handler.GetType()))", "foreach (var field in Array.Empty<FieldInfo>())"),
    ("an invoker is not looked through", chain,
     "case HttpMessageInvoker invoker:", "case HttpMessageInvoker invoker when invoker is null:"),
    ("only the first handler held is followed", chain,
     "for (var i = held.Count - 1; i >= 0; i--)\n                pending.Push(held[i]);", "if (held.Count > 0)\n                pending.Push(held[0]);"),
    ("any end answers in place of every end", chain,
     "return ends.TrueForAll(e => e.Kind == End.Answerer) ? new HandlerChain(false, ends[0].Handler!.GetType().Name) : OtherTransport;",
     "return ends.Exists(e => e.Kind == End.Answerer) ? new HandlerChain(false, ends.Find(e => e.Kind == End.Answerer).Handler!.GetType().Name) : OtherTransport;"),
    ("any end a socket in place of every end", chain,
     "if (ends.TrueForAll(e => e.Kind == End.Socket))", "if (ends.Exists(e => e.Kind == End.Socket))"),
    ("response.RequestMessage not read", handler,
     "TransportSpan(response.RequestMessage ?? request,", "TransportSpan(request,"),
    ("the passed-handler check dropped", chain,
     "case HttpMessageHandler next when !seen.Contains(next):", "case HttpMessageHandler next:"),
    ("the 64 cap dropped", chain,
     "if (seen.Count > MostHandlers)", "if (seen.Count < 0)"),
    ("the fast path answering socket for any end", chain,
     "        switch (Kind(handler))\n        {\n            case End.Socket: return Sockets;",
     "        if (handler is not null) return Sockets;\n        switch (Kind(handler))\n        {\n            case End.Socket: return Sockets;"),
    ("an unreadable invoker read as a stub", chain,
     "            if (unreadable)\n                ends.Add((End.Unknown, handler));\n            else if (held.Count == 0)",
     "            if (held.Count == 0)"),
    ("Q4: a delegating handler with no inner handler read as unknown", chain,
     "        null => End.Unknown,\n", "        null => End.Unknown,\n        DelegatingHandler { InnerHandler: null } => End.Unknown,\n"),
]

for name, rel, old, new in mutations:
    path = os.path.join(root, rel)
    original = open(path, encoding="utf-8").read()
    assert original.count(old) == 1, (name, old)
    open(path, "w", encoding="utf-8", newline="").write(original.replace(old, new))
    try:
        run = subprocess.run(["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--filter", classes],
                             cwd=root, capture_output=True, text=True, encoding="utf-8", errors="replace")
        out = run.stdout + run.stderr
        summary = re.findall(r"(Failed!|Passed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", out)
        failed = sorted({m.split(".")[-1].split("(")[0] for m in re.findall(r"^\s+Failed (\S+)", out, re.M)})
        errors = re.findall(r"error CS\d+[^\[]*", out)
        verdict = "BUILD ERROR " + errors[0][:150] if errors else (f"caught: {summary[-1][1]} failed" if summary and summary[-1][1] != "0" else "NOT CAUGHT")
        print(f"- {name}: {verdict}")
        for f in failed[:6]:
            print(f"    {f}")
        sys.stdout.flush()
    finally:
        open(path, "w", encoding="utf-8", newline="").write(original)
