"""4.18.1's wiki edit: the OTLP tap queues an export before it answers (found by CI on 4.18.0 while R2 shipped).

Usage: python otlp_4181_wiki.py <wiki-worktree> [--check]
Every `old` must occur exactly once; --check reports without writing. The pages are LF.
"""
import sys

WIKI = sys.argv[1]
CHECK = '--check' in sys.argv

OLD = """2. **Forwards or acknowledges, first.** With `ForwardBaseUri` set the request goes upstream verbatim — same method, path, headers and body, still gzipped — and the upstream status and body are relayed to the caller. Without it the tap answers `200` locally with an empty `ExportTraceServiceResponse` (which is zero bytes in protobuf, `{}` in JSON).
3. **Queues the payload** on a bounded channel and returns. Decoding and mapping happen on a background worker, so a slow or blocked sink can never slow the exporter down; when the queue is full the newest payload is dropped and counted.
"""
NEW = """2. **Queues the payload** on a bounded channel. Queuing never waits: when the queue is full the newest payload is dropped and counted. From 4.18.1 it queues before it answers, so an export the tap has answered is mapped even when the tap is disposed straight after, since disposing drains the queue (before, a tap disposed between the answer and the queuing lost it, and logged a payload dropped at shutdown as a full queue).
3. **Forwards or acknowledges.** With `ForwardBaseUri` set the request goes upstream verbatim — same method, path, headers and body, still gzipped — and the upstream status and body are relayed to the caller. Without it the tap answers `200` locally with an empty `ExportTraceServiceResponse` (which is zero bytes in protobuf, `{}` in JSON). Decoding and mapping happen on a background worker, so a slow or blocked sink can never slow the exporter down.
"""

path = f'{WIKI}/Integration-Otlp-Extension.md'
with open(path, encoding='utf-8', newline='') as f:
    text = f.read()
count = text.count(OLD)
print(f'Integration-Otlp-Extension.md: {count} occurrence(s)')
if count == 1 and not CHECK:
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(text.replace(OLD, NEW))
print('problems:', 0 if count == 1 else 1)
sys.exit(0 if count == 1 else 1)
