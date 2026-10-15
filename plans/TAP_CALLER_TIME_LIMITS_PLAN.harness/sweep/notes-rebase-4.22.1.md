# The plan against 4.22.1, after #174 shipped (2026-10-15)

#174 shipped on 2026-10-15 as 4.20.3 (its R1), 4.22.0 (R2) and 4.22.1 (R3); `origin/main` is `fefb99e5`. Between
`3240c983` and `fefb99e5` the files the plan edits changed only in `TcpTap.cs`, `OtlpTap.cs`, `TcpTapOptions.cs`,
`CaptureDegradation.cs` and `DiagnosticEntry.cs` (`git diff --stat 3240c983 fefb99e5` over the three tap packages, their
test projects, `DiagnosticEntry.cs` and `ci.yml`). `ProxyTap.cs`, `ProxyTapOptions.cs`, its registration,
`HttpRequestReader.cs`, `OtlpTapOptions.cs` and `ci.yml` did not change. (READ)

## How the lines were mapped

`mapline.py <diff> <line>...` reads `git diff -U0 3240c983 fefb99e5 -- <file>` and moves a line by the hunks above it,
or reports `CHANGED` for a line inside a hunk. Every mapped line in Appendix A was then checked by its text in both
versions (`git show <commit>:<file>`), among them `TcpTap.cs` 229 to 267 (`var pumpState = ...`), 271 to 414
(`PumpAsync`), 529 to 674 (`Diagnostics()`), 693 to 866 (`DisposeAsync`), 731 to 908 (`LastActivityTicks`) and every
unguarded `Log` call (257, 365, 383, 447, 466, 478, 517, 672 and 690 to 296, 508, 526, 591, 610, 622, 661, 829 and
847); `OtlpTap.cs` 277 to 276 (`ServeAsync`) and its unguarded calls (304, 313, 321, 341, 409, 448 and 526 to 303, 312,
320, 340, 408, 447 and 525); and `TcpTapOptions.cs` 96 to 113 (`BodyCapBytes`), 173 to 199
(`ReapStuckConnectionsAfter`), 188 to 214 (`Validate`) and 217 to 243 (`CopyTo`). Lines inside a changed hunk were read
by hand: both stop lines (`TcpTap.cs` 891-895, `OtlpTap.cs` 610-613), `OtlpTap.DisposeAsync`'s end (614; the mapper
pairs the old closing brace with the new `TryLog`'s) and the rewritten doc comments.

## What the design relies on, at 4.22.1

- **`TcpTap.HandleConnectionAsync` (L240-300)** has one catch (L288), which logs whatever reaches it through `TryLog`:
  4.22.0 removed the catch that took any exception for "shutting down" (its comment: it "used to take a connect timeout
  for one"). The `finally` still releases the pair (L294-300).
- **`PumpAsync` (L414-464)** is unchanged: each pump breaks on an `IOException`, `ObjectDisposedException`,
  `SocketException` or `OperationCanceledException` from its read (L428) or its write (L444), so a cancelled pair token
  ends both pumps quietly and never reaches that catch. The reaper (`ReapWhenStuckAsync`, L466-514) is unchanged too,
  started only where a decoder exists, and the connection still awaits it after the pumps, so its one-second tick still
  delays a release (R3-8).
- **`TapAcceptLoop.RunAsync`** calls its `accepted` callback outside any `try`, so a throw there ends the loop:
  `OtlpTap`'s connection registers with the watch inside `ServeAsync`, as §4.4 item 2 says.
- **Stop lines.** `TcpTap`'s names failed accepts and failed connects but not reaped or closed-mid-message connections
  (R3 adds them); `OtlpTap`'s names failed accepts but not `PayloadsRejected` or `PayloadsFailed` (R0 item 10).
- **`TryLog`** exists in `TcpTap` and `OtlpTap` (4.20.3). Still raw: `TcpTap` 296, 508, 526, 591, 610, 622, 661, 829
  and `Diagnostic` (847); `OtlpTap` 303, 312, 320, 340, 408, 447 and 525. These are exactly R0 item 6's sites.
- **Doc comments (4.22.0).** `CaptureDegradationKind`'s summary now names the three kinds that close or refuse a
  connection; `OnCaptureDegraded` (`TcpTapOptions.cs` L160-168) says each event runs on the task where it happens, the
  reaper's own for a reap, which R3 changes to a pool thread; "the one intervention that ever touches forwarding" is
  still in `CaptureDegradation.cs` L27-28 and `TcpTapOptions.cs` L183-184 (R3: one of three). `DiagnosticEntry.cs`
  L41-46 names a forward, a connect or an accept that failed; R1 adds a caller or peer cut by a time limit.
- **Test projects.** `Kronikol.Tests.TcpTap` and `Kronikol.Tests.Otlp` link `LoopbackPort.cs` (4.22.0);
  `TcpTapAcceptTests.cs` has the hand-written `ManualClock` (L363); no `TimeProvider` in `src/` but
  `TapAcceptLoop.Clock`.

## The wiki at `origin/master` `32dc356`

#174's edits (4.20.3, 4.22.0, 4.22.1) added `ReapStuckConnectionsAfter` to the TcpTap page's options table (L152, "the
one intervention that ever touches forwarding"), `StuckConnectionsReaped`, `UpstreamConnectFailures` and
`AcceptFailures` to its counters, rewrote "(never forwarding)" (60daf39 L285) and the sentence on `OnCaptureDegraded`'s
task (60daf39 L304, now L309), and added `AcceptFailures` to the Otlp page's counter line (L143). L37 still ends "A tap
is never the reason a request fails". `Diagnostics-and-Debugging.md` did not change: L110 still says "Forwarding was
never affected".
