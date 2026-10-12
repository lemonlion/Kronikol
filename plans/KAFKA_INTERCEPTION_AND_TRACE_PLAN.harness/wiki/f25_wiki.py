"""F25's wiki edit (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 3), applied after its release is published, on a
wiki that already has R0's (r0_wiki.py).

Usage: python f25_wiki.py <wiki-worktree> <version> [--check]
Every `old` must occur exactly once; --check reports without writing. The pages are LF.
"""
import sys

WIKI, VERSION = sys.argv[1], sys.argv[2]
CHECK = '--check' in sys.argv

EDITS = {
    'Integration-Kafka-Extension.md': [
        ("The cost is a slower start of each test process: measured on Kronikol's own Kafka tests (126 facts), 1.66 s became 2.28 to 2.54 s.\n",
         "The cost is a slower start of each test process: measured on Kronikol's own Kafka tests (126 facts), 1.66 s became 2.28 to 2.54 s.\n\n"
         "**Enable before the clients are built.** Call `Enable…()` before the code that builds the clients first runs: in a fixture's constructor, or before the host starts. "
         "A method is compiled at its first call, and with quick JIT off it is compiled with optimizations, which can inline `Build()` into it; a method compiled before the patch keeps the inlined original, and its builds are untracked. "
         "A test method that enables interception and then builds a client in its own body was compiled before its `Enable…()` ran. "
         f"A Debug build of the test project compiles nothing with optimizations, so it can pass where the same tests built in Release fail (the interceptor's remarks say so from {VERSION}).\n"),
    ],
    # Found on the way: the handler starts the listener on its first request (TestTrackingMessageHandler.SendAsync), not
    # in its constructor, and the gRPC interceptor starts it too, as API-Reference says since 4.16.1.
    'Internal-Flow-Tracking.md': [
        ("**Span capture** is auto-started by the `TestTrackingMessageHandler` constructor.",
         "**Span capture** is auto-started on first use: by `TestTrackingMessageHandler` on its first request, and by `GrpcTrackingInterceptor` on its first tracked call."),
    ],
}

problems = 0
for page, edits in EDITS.items():
    path = f'{WIKI}/{page}'
    with open(path, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            problems += 1
            print(f'{page}: expected 1 occurrence, found {count}: {old[:90]!r}')
            continue
        text = text.replace(old, new)
    if not CHECK:
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(text)
print(f'problems: {problems}')
sys.exit(1 if problems else 0)
