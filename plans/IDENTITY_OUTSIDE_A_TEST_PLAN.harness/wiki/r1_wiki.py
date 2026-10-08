"""The wiki edits for the release that fixes identity outside a test (#133). Exact-string replacements, each of which must
match once, applied to a wiki checkout (CRLF or LF, kept as found).

    python r1_wiki.py <wiki checkout> <version> [--dry-run]
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
VERSION = sys.argv[2]
DRY = "--dry-run" in sys.argv

EDITS = [
    # The fetcher's answer outside a test.
    ("Background-Thread-Correlation.md",
     'and the fetcher will return `("Unknown", "unknown")`.',
     'and the fetcher will throw `InvalidOperationException`, which the resolver takes as "no test here". Every '
     "framework's fetcher does so outside a test; xUnit v2's and NUnit's since {VERSION}."),
    ("Background-Thread-Correlation.md",
     "The resolver catches this and falls through to priorities 3–6. See [[Tracking Custom Dependencies › Test Context "
     "Availability|Tracking-Custom-Dependencies#test-context-availability]] for details.",
     "The resolver catches this and falls through to priorities 3–6. See [[Tracking Custom Dependencies › Test Context "
     "Availability|Tracking-Custom-Dependencies#test-context-availability]] for details. xUnit v2's and NUnit's fetchers "
     "did not throw until {VERSION}: outside a test they answered with an id no scenario owns (xUnit v2 a new random id "
     "on every call, NUnit the fixture's id or, on a thread the test did not reach, an ad hoc one), so priorities 3–6 "
     "were never reached and the call appeared in no report."),
    ("HTTP-Tracking-Setup.md",
     'As of **v2.27.10**, `CurrentTestInfo.Fetcher` is **null-safe** and returns `("Unknown", "unknown")` when the test '
     'context is unavailable. The tracking handler catches this and the startup call is either logged with "Unknown" '
     "identity or silently skipped — **tests still pass** and diagrams for actual tests are unaffected.",
     "`CurrentTestInfo.Fetcher` throws `InvalidOperationException` when no test is running (since v2.28.22; xUnit v2's "
     "and NUnit's since {VERSION}). The tracking handler catches it and sends the startup call untracked, without test "
     "headers, unless a `TestIdentityScope` or `TestIdentityScope.GlobalFallback` names a test (it goes to that test) or "
     "`RequestResponseLogger.CaptureBackground` is on (it is kept as a background call, under the `unknown` identity). "
     "**Tests still pass** and diagrams for actual tests are unaffected."),
    ("Diagnostics-and-Debugging.md",
     '1. All 8 framework `CurrentTestInfo.Fetcher` implementations now return `("Unknown", "unknown")` when the test '
     "context is unavailable",
     "1. All 8 framework `CurrentTestInfo.Fetcher` implementations stopped dereferencing a missing test context. v2.27.10 "
     'made them return `("Unknown", "unknown")`; since v2.28.22 they throw `InvalidOperationException`, which every '
     "tracker catches before it tries the next level (xUnit v2's and NUnit's since {VERSION}: see the next entry)"),
    ("Diagnostics-and-Debugging.md",
     "2. `MessageTracker.GetTestInfo()` wraps the delegate call in a try-catch, matching all other extensions\n",
     "2. `MessageTracker.GetTestInfo()` wraps the delegate call in a try-catch, matching all other extensions\n"
     "\n"
     "### Calls made outside a test are missing, and a scope or fallback does not bring them back (xUnit v2 and NUnit, "
     "fixed in {VERSION})\n"
     "\n"
     "**Symptom:** Calls made outside a test appear in no diagram, and wrapping them in `TestIdentityScope.Begin(...)` or "
     "setting `TestIdentityScope.GlobalFallback` changes nothing. On xUnit v2 that is a test class's constructor, "
     "`InitializeAsync`, `DisposeAsync` and `Dispose`, a fixture, and a thread the test did not start; on NUnit a "
     "`[SetUpFixture]`, a fixture's constructor, `[OneTimeSetUp]` and `[OneTimeTearDown]`, and a thread the test did not "
     "start. At `normal` console verbosity the run prints `Warning: N orphaned test ID(s) in logs do not match any "
     "feature scenario.`; at `dotnet test`'s default verbosity it prints nothing.\n"
     "\n"
     "**Cause (before {VERSION}):** those adapters' fetchers answered outside a test with an id no scenario owns instead "
     "of throwing: xUnit v2 a new random id on every call, NUnit the fixture's id or an ad hoc one. The resolver took it "
     "for the test, so it never reached the scope, the global fallback or the background, and the report dropped the "
     "call. The id also travelled on: in the `test-tracking-current-test-id` header to the service, on Kafka messages, "
     "and as the owner of a document the call wrote.\n"
     "\n"
     "**Fix:** upgrade to {VERSION}. Such calls now go to a `TestIdentityScope` or the global fallback when one names a "
     "test, and are otherwise background calls: dropped, or listed under Background calls with "
     "`RequestResponseLogger.CaptureBackground` on. A `GlobalFallback` left set by an earlier test catches them too, so "
     "clear it in teardown. Calls a `DeferredLogFlushHandler` holds now wait for the first call made inside a test, as on "
     "the other frameworks. Calls made inside a test are unaffected.\n"),
    ("Event-Annotations.md",
     "When the delegate throws (e.g. outside test context), the exception is caught and the call is silently skipped. "
     "All framework `CurrentTestInfo.Fetcher` implementations are null-safe (v2.27.10+).",
     "When the delegate throws (as every framework's `CurrentTestInfo.Fetcher` does outside a test: since v2.28.22, and "
     "on xUnit v2 and NUnit since {VERSION}), the exception is caught and the tracker tries a `TestIdentityScope` and the "
     "global fallback; when neither names a test, the call is silently skipped (or kept as a background call with "
     "`RequestResponseLogger.CaptureBackground` on)."),
    ("Event-Annotations.md",
     'For example, `XUnit2TestTrackingContext.GetCurrentTestInfo()` returns a fallback `("Unknown Test", ...)` when no '
     "test is active, but your own code",
     "For example, the trackers catch the exception every `CurrentTestInfo.Fetcher` throws when no test is active, but "
     "your own code"),
    ("Integration-xUnit3.md",
     "As of **v2.27.10**, `CurrentTestInfo.Fetcher` is null-safe and returns a fallback value in this case.",
     "As of **v2.27.10**, `CurrentTestInfo.Fetcher` handles this (since v2.28.22 it throws `InvalidOperationException`, "
     "which the handler catches; it then tries a `TestIdentityScope` and the global fallback, and sends the call "
     "untracked when neither names a test)."),

    # The custom-tracker recipe and the context availability section.
    ("Tracking-Custom-Dependencies.md",
     "// Use the appropriate import for your test framework:\n"
     "// using Kronikol.xUnit2;  // XUnit2TestTrackingContext.GetCurrentTestInfo()\n"
     "// using TUnit.Core;                   // TestContext.Current\n"
     "// For xUnit 3: TestContext.Current.Test (built-in)\n"
     "// For ReqNRoll.TUnit: ReqNRollTestContext.CurrentTestInfo\n",
     "// Your test framework's package; each has a CurrentTestInfo.Fetcher:\n"
     "using Kronikol.xUnit2;  // or Kronikol.xUnit3, Kronikol.NUnit4, Kronikol.MSTest, Kronikol.TUnit, Kronikol.ReqNRoll, ...\n"),
    ("Tracking-Custom-Dependencies.md",
     "            var testInfo = XUnit2TestTrackingContext.GetCurrentTestInfo();\n"
     "            // For TUnit, use instead:\n"
     "            //   var testInfo = (TestContext.Current!.Metadata.DisplayName, TestContext.Current.Id);\n"
     "            // For ReqNRoll + TUnit:\n"
     "            //   var testInfo = ReqNRollTestContext.CurrentTestInfo\n"
     '            //       ?? throw new InvalidOperationException("No scenario executing.");\n',
     "            // The test running here, else the one a TestIdentityScope or the global fallback names.\n"
     "            // Null outside a test with neither (fixture set-up, a background thread): skip the call.\n"
     "            if (TestInfoResolver.Resolve(null, CurrentTestInfo.Fetcher) is not { } testInfo)\n"
     "                return;\n"),
    ("Tracking-Custom-Dependencies.md",
     "The framework-specific test context (e.g. `XUnit2TestTrackingContext.GetCurrentTestInfo()`) provides the test name "
     "and ID needed by `RequestResponseLog`. Behaviour varies by framework:",
     "Take the test name and ID that `RequestResponseLog` needs from `TestInfoResolver.Resolve(null, "
     "CurrentTestInfo.Fetcher)`, as the example above does: it asks your framework's context first, then a "
     "`TestIdentityScope` and the global fallback, and is `null` when none names a test. The framework contexts "
     "underneath behave differently:"),
    ("Tracking-Custom-Dependencies.md",
     '- **xUnit 2**: `XUnit2TestTrackingContext.GetCurrentTestInfo()` returns a fallback `("Unknown Test", <new GUID>)` '
     "when no test is active (e.g. during fixture setup/teardown).",
     '- **xUnit 2**: `XUnit2TestTrackingContext.GetCurrentTestInfo()` returns a fallback `("Unknown Test", <new GUID>)` '
     "when no test is active: in fixtures, and in a test class's constructor, `InitializeAsync`, `DisposeAsync` and "
     "`Dispose`, which xUnit v2 runs outside the test. The id is new on every call and no scenario owns it, so a call "
     "logged under it appears in no report; `CurrentTestInfo.Fetcher` throws there instead (since {VERSION})."),
    ("Tracking-Custom-Dependencies.md",
     "tracking is silently skipped (or the next fallback is tried), never crashing.",
     "tracking is silently skipped (or the next fallback is tried), never crashing. xUnit v2's and NUnit's only throw "
     "since {VERSION}: before, outside a test, xUnit v2's answered with a new random id and NUnit's with the fixture's id "
     "(or an ad hoc one on a thread the test did not reach), which no scenario owns."),
    ("Tracking-Custom-Dependencies.md",
     "Calling it from `InitializeAsync()` / `DisposeAsync()` on the test class is safe.",
     "Calling it from `InitializeAsync()` / `DisposeAsync()` on the test class is safe on xUnit v3, which runs them "
     "inside the test. xUnit v2 runs them, and the test class's constructor and `Dispose`, outside the test, as NUnit "
     "runs a fixture's constructor and its one-time set-up and tear-down."),
    ("Tracking-Custom-Dependencies.md",
     "This is the same pattern used by `CosmosTrackingMessageHandler`, which checks `CurrentTestInfoFetcher?.Invoke()` "
     "and skips tracking entirely if the result is `null`.",
     "This is the pattern the built-in trackers follow: they resolve through `TestInfoResolver` and skip the call when "
     "nothing names a test."),
    ("Tracking-Custom-Dependencies.md",
     "| `TestName` / `TestId` | Obtained from your framework's test context (e.g. "
     "`XUnit2TestTrackingContext.GetCurrentTestInfo()`, or `TestContext.Current` for TUnit/xUnit 3). |",
     "| `TestName` / `TestId` | From `TestInfoResolver.Resolve(null, CurrentTestInfo.Fetcher)`, with your framework "
     "package's `CurrentTestInfo`. Skip the call when it returns `null`. |"),

    # The API reference.
    ("API-Reference.md",
     "the standard way to set `CurrentTestInfoFetcher` on any tracking options class. One per framework package. |",
     "the standard way to set `CurrentTestInfoFetcher` on any tracking options class. One per framework package. Outside "
     "a test it throws `InvalidOperationException`, which the trackers read as no test. |"),
    ("API-Reference.md",
     "| `TrackingDiagramOverride` | Framework-specific wrapper that auto-resolves the test ID from the current test context. |",
     "| `TrackingDiagramOverride` | Framework-specific wrapper that auto-resolves the test ID from the current test context. "
     "Outside a test it uses the test a `TestIdentityScope` or the global fallback names, and does nothing when neither "
     "names one (ReqNRoll's and LightBDD's throw outside a scenario). |"),

    # Track.TestIdResolver.
    ("Assertion-Tracking.md",
     "1. `Track.TestIdResolver` (static delegate — set automatically by framework integrations: LightBDD, BDDfy, ReqNRoll)",
     "1. `Track.TestIdResolver` (static delegate, set automatically by every framework integration; outside a test it "
     "answers no test)"),
    ("Assertion-Tracking.md",
     "When using one of the framework adapter packages (`Kronikol.LightBDD.*`, `Kronikol.BDDfy.*`, `Kronikol.ReqNRoll.*`), "
     "`Track.TestIdResolver` is set up automatically during configuration — no manual wiring needed.",
     "When using one of the framework adapter packages (`Kronikol.xUnit2`, `Kronikol.xUnit3`, `Kronikol.NUnit4`, "
     "`Kronikol.MSTest`, `Kronikol.TUnit`, `Kronikol.LightBDD.*`, `Kronikol.BDDfy.*`, `Kronikol.ReqNRoll.*`), "
     "`Track.TestIdResolver` is set up automatically, so no manual wiring is needed. Outside a test it answers no test, "
     "and `Track.That()` goes on to the scope and the global fallback; on xUnit v2 and NUnit it answered with an id no "
     "scenario owns until {VERSION}, so such a note was lost."),

    # NUnit.
    ("Integration-NUnit.md",
     "- `NUnitTestTrackingMessageHandlerOptions` uses NUnit's `TestContext.CurrentContext` to resolve the current test's "
     "identity.",
     "- `NUnitTestTrackingMessageHandlerOptions` uses NUnit's `TestContext.CurrentContext` to resolve the current test's "
     "identity. Outside a test (a `[SetUpFixture]`, a fixture's constructor, `[OneTimeSetUp]` or `[OneTimeTearDown]`, or "
     "a thread the test's execution context did not reach) that context names the fixture or an ad hoc test of NUnit's, "
     "not a test, so the fetcher throws there (since {VERSION}) and such calls go to a `TestIdentityScope`, the global "
     "fallback or the background."),

    # xUnit v2.
    ("Integration-xUnit2.md",
     "| Test identity | `TestContext.Current` (built-in) | `AsyncLocal<T>` via `[TestTracking]` attribute |\n",
     "| Test identity | `TestContext.Current` (built-in) | `AsyncLocal<T>` via `[TestTracking]` attribute |\n"
     "| What the test's identity covers | The test class's constructor, `InitializeAsync`, the test method, "
     "`DisposeAsync` and `Dispose` | The test method only, from `[TestTracking]`'s `Before` to its `After` |\n"),
    ("Integration-xUnit2.md",
     "- `AsyncLocal` flows through `await` calls and through `WebApplicationFactory`'s `TestServer`. If you spawn new "
     "threads manually, the value may not propagate — use `async`/`await` instead.\n",
     "- `AsyncLocal` flows through `await` calls and through `WebApplicationFactory`'s `TestServer`. If you spawn new "
     "threads manually, the value may not propagate — use `async`/`await` instead.\n"
     "\n"
     "### Calls made in a test class's constructor or `Dispose` are not in the diagram\n"
     "xUnit v2 runs a test class's constructor and `IAsyncLifetime.InitializeAsync` before `[TestTracking]`'s `Before`, "
     "and `DisposeAsync` and `Dispose` after its `After`, so calls made there belong to no test, as calls from a fixture "
     "do. They go to a `TestIdentityScope` or `TestIdentityScope.GlobalFallback` when one names a test, and are "
     "otherwise not drawn (with `RequestResponseLogger.CaptureBackground` on, they are listed as background calls). To "
     "draw them in the test's diagram, make them from the test method. Before {VERSION} each such call was logged under "
     "a new random id that no scenario owned: it vanished from the report, and a scope or fallback set for it had no "
     "effect.\n"),
]

problems = []
for name, old, new in EDITS:
    path = WIKI / name
    text = path.read_bytes().decode("utf-8")
    crlf = "\r\n" in text
    want = old.replace("\n", "\r\n") if crlf else old
    put = new.replace("{VERSION}", VERSION)
    put = put.replace("\n", "\r\n") if crlf else put
    count = text.count(want)
    if count != 1:
        problems.append(f"{name}: {count} matches for {old[:70]!r}")
        continue
    if not DRY:
        path.write_bytes(text.replace(want, put).encode("utf-8"))

if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"{len(EDITS)} edits {'would apply' if DRY else 'applied'}")
