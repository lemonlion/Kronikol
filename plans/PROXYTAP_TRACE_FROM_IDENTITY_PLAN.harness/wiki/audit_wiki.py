"""The #170 audit's wiki edits (plan section 12). Run in a worktree of the wiki at origin/master, with the release's
version: python audit_wiki.py 4.19.4. Each edit adds to one paragraph in place, whatever the checkout's line endings."""
import pathlib
import sys

version = sys.argv[1]


def edit(name, anchor, addition):
    p = pathlib.Path(name)
    s = p.read_bytes().decode('utf-8')
    assert s.count(anchor) == 1, (name, anchor, s.count(anchor))
    assert addition not in s, (name, 'already applied')
    p.write_bytes(s.replace(anchor, anchor + addition).encode('utf-8'))


edit('Internal-Flow-Tracking.md',
     "(from 4.18.3, #170: before, two such calls that overlapped in time each showed both calls' spans).",
     ' Which call another ran inside is read from the span tree first, so the queries a service makes for two calls an'
     ' app made at once stay each with its own call, though both were made while both calls were open (from '
     f'{version}: before, both went inside the call opened last, whose popup showed the other\'s query as well).')

edit('Integration-ProxyTap-Extension.md',
     "and a sampler that dropped the server span replaced the caller's trace.",
     f' From {version} the forward runs none of the HttpClient instrumentation in the tap\'s process: OpenTelemetry\'s'
     ' instrumentation put a span of its own in place of the forwarded `traceparent`, which started a new trace with'
     ' nothing listening to the tap and went out `-00` beside Kronikol\'s listener; listen to `Kronikol.ProxyTap` for'
     ' the forward\'s span.'
     ' A tap started under an activity no longer forwards that activity\'s trace and baggage either: each request'
     ' starts with no current activity.')
print('wiki edited')
