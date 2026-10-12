"""The wiki edits for the audit release of #133: exact-string replacements, each of which must match the number of times
given, applied to a wiki checkout (CRLF or LF, kept as found).

    python a1_wiki.py <wiki checkout> <version> [--dry-run]
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
VERSION = sys.argv[2]
DRY = "--dry-run" in sys.argv

EDITS = [
    # (page, old, new, times)
    ("API-Reference.md",
     "Outside a test it uses the test a `TestIdentityScope` or the global fallback names, and does nothing when neither "
     "names one (ReqNRoll's and LightBDD's throw outside a scenario). |",
     "Outside a test its markers go to the test a `TestIdentityScope` or the global fallback names, and are dropped when "
     "neither names one; `StartSetup()` and `StartAction()` set the phase there either way, since the phase belongs to the "
     "flow (since {VERSION}; xUnit v3's sets none outside a test, and ReqNRoll's and LightBDD's throw outside a scenario). |",
     1),
    ("Event-Annotations.md",
     "> **Null-safety:** `TestContext.Current.Test` can be `null` during app startup or DI container warm-up. Always use a "
     "null-safe delegate.",
     "> **Outside a test:** `TestContext.Current.Test` can be `null` during app startup or DI container warm-up. A delegate "
     "may throw there, as every framework's `CurrentTestInfo.Fetcher` does: the tracker catches it, tries a "
     "`TestIdentityScope` and the global fallback, and skips the call when neither names a test (or keeps it as a background "
     "call with `RequestResponseLogger.CaptureBackground` on).",
     1),
    # gRPC recipes that called a helper the page never defines; a reader on xUnit v2 would write it over
    # XUnit2TestTrackingContext.GetCurrentTestInfo(), whose id outside a test no scenario owns (#133).
    ("Diagnostics-and-Debugging.md", "() => GetCurrentTestInfo()", "CurrentTestInfo.Fetcher", 2),
    ("Integration-Grpc-Extension.md", "() => GetCurrentTestInfo()", "CurrentTestInfo.Fetcher", 3),
]

pages = {}
for page, old, new, times in EDITS:
    path = WIKI / page
    if page not in pages:
        raw = path.read_bytes().decode("utf-8")
        pages[page] = ("\r\n" if "\r\n" in raw else "\n", raw.replace("\r\n", "\n"))
    nl, text = pages[page]
    found = text.count(old)
    if found != times:
        sys.exit(f"{page}: expected {times} match(es), found {found}: {old[:70]!r}")
    pages[page] = (nl, text.replace(old, new.replace("{VERSION}", VERSION)))

if DRY:
    print(f"{len(EDITS)} edits would apply to {len(pages)} pages")
else:
    for page, (nl, text) in pages.items():
        (WIKI / page).write_bytes(text.replace("\n", nl).encode("utf-8"))
    print(f"{len(EDITS)} edits applied to {len(pages)} pages")
