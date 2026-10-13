"""R0 (4.18.3) wiki edits for #170. Run from a checkout of the wiki at origin/master: python r0_wiki.py <wiki dir>.

Each edit replaces one anchor that must occur exactly once, so a page that moved fails loudly instead of being
edited in the wrong place.
"""
import pathlib
import sys

wiki = pathlib.Path(sys.argv[1])


def sub(page, old, new):
    p = wiki / page
    s = p.read_text(encoding='utf-8')
    assert s.count(old) == 1, (page, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8', newline='\n')


# F4: the builder tells one test's calls apart by span tree ("How it works" step 3, and the correlation bullet).
sub('Internal-Flow-Tracking.md',
    "a call takes its own span and the spans descending from it. A span that calls of two scenarios would both take "
    "goes to neither,",
    "a call takes its own span and the spans descending from it. Where the calls of one scenario record one trace under "
    "span ids of their own (a browser test's one trace, which a proxy tap forwards under a span per call), a span that "
    "descends from another of those calls, and not from the call's own, shows in that call's popup only; a call nested "
    "in another still shows inside it, and a span that descends from none of them stays in every call whose time holds "
    "it (from 4.18.3, #170: before, two such calls that overlapped in time each showed both calls' spans). A span that "
    "calls of two scenarios would both take goes to neither,")
sub('Internal-Flow-Tracking.md',
    "where calls of several scenarios record one trace, only the spans under its own span;",
    "where calls of several scenarios record one trace, only the spans under its own span; where calls of one scenario "
    "record one trace under span ids of their own, not the spans under another of those calls (from 4.18.3);")

# F2 and F3: what the tap forwards with a listener ("How it works" step 3).
sub('Integration-ProxyTap-Extension.md',
    "the downstream service then nests under the tap in your distributed trace. Without a listener it is fully "
    "transparent.",
    "the downstream service then nests under the tap in your distributed trace. Without a listener it is fully "
    "transparent. The forwarded `traceparent` keeps the caller's sampled flag when the tap's client span is not "
    "recorded (Kronikol's own internal-flow listener samples with `AllData`, so in a test process it is not), and says "
    "sampled for a trace the tap starts; when a listener drops the server span, the client span is parented on the "
    "caller's `traceparent` itself. Both are from 4.18.3: before, a tap in a test process forwarded `-00`, so a service "
    "with a parent-based sampler recorded nothing, and a sampler that dropped the server span replaced the caller's trace.")
print('r0 wiki edits applied')
