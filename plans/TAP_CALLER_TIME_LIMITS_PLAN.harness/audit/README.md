# The audit of 4.22.2 to 4.26.1, and its patch (4.26.3)

The owner asked, after 4.26.1, that nothing be missed from the plan and that its releases carry no bugs. Five read-only
reviews ran over the released tree (`09369515`): the plan item by item (R0's twelve items, R1 to R3, every test id of
section 5, the docs of section 6, Appendix A and every call to a tap's `Log`), a bug hunt in each tap (`ProxyTap`,
`OtlpTap`, `TcpTap` with the watch), and every changelog, wiki and doc-comment claim against the code. A peer session
auditing #174 (the same `TcpTap` file) reported three more. Each finding was checked by reading the code or by a probe
before anything was changed; what did not hold was dropped. The patch was written in worktree `C:/Code/Kronikol-173-audit`
(branch `audit/173`, from `09369515`), each fix after a fact that failed without it, and rebased onto 4.26.2
(`8dfa1939`, #174's audit) before its release; 4.26.2's reworked start already made the heartbeat after the bind, as the
two audits had agreed, so the rebase kept it. Windows is Windows 11 26200 with .NET 10; Linux is
`mcr.microsoft.com/dotnet/sdk:10.0` under podman's WSL machine.

## What did not hold

- `StallWatch`'s `ExecutionContext.SuppressFlow()` throwing when the flow is already suppressed (two reviews). The
  documented throw is .NET Framework's: on .NET 8.0.27, 9.0.16 and 10.0.11 a nested suppression is allowed and its undo
  leaves the outer one in place (`probe-suppress/`). `StallWatchTests.A_watch_made_while_the_flow_is_suppressed_beats`
  stays as a guard.

## Files

| Path | What it is |
|---|---|
| `reviews/` | The five reviews' reports as written, before anything in them was checked. What held is in the plan's log (section 11) and 4.26.3's changelog; the finding above did not |
| `red_stubs_audit.py`, `red-stubs-audit.diff` | The red tree: 4.26.1 with the patch's tests and only the seams they need (ProxyTap's `GetContext`, `Pause`, `Received` and `BeforeCallerLetGo`, as no-ops in 4.26.1's flow). `TcpTapStallTests` is 4.26.1's file and helpers with the patch's new facts added, since the patch's helpers read the pair's write marks |
| `red-run-windows.sh`, `red-4.26.1-<suite>-windows.txt`, `red-run-windows-summary.txt` | The patch's facts on the red tree, Windows: every fix's fact fails, each on its own assertion. The facts that pass are guards: a watch made while the flow is suppressed, a start after the dispose and one racing it, R1-3's wait for the log line, the count order 4.26.1 already had right (mutant P7 shows its fact fails on 4.26.0's order), the Otlp log lines, the reader's two empty lines, and on Windows the request taken as the tap stops |
| `linux-audit.sh`, `red-4.26.1-ProxyTap-linux.txt`, `red-linux-summary.txt` | The ProxyTap facts on the red tree, Linux: a request taken but not yet handled when the tap stops hears `HTTP/1.1 200 OK` (on Windows http.sys resets it, so the fact is a guard there) |
| `mutations_audit.py`, `mutations-audit-windows.txt`, `mutations-audit-windows-rebased.txt` | 33 mutations with R0's runner: R1's three that its run left out (plan section 5: the body's entry armed at the first completed read, the watch moved into the forward, the `Interlocked` move in `TimeOut` dropped), and one that undoes each fix of the patch. 30 ran on the patch before its rebase onto 4.26.2 (`39642161`): 27 are killed, each by the fact written for it. Three survive there. P5 survives on Windows, where http.sys resets the request either way, and is killed on Linux (next row). T9 removes only the two disposes from a limit's close, which also cancels the pair's token: the pumps end and the connection disposes both sockets, so nothing a fact sees changes; T9b removes the cancel too, as R3's M8 did, and is killed by 9 facts. P9 survives as expected: no fact can order the moment B3's fix closes. T9b, T1r and O1r ran on the rebased tree (`26aad29a`), T1r and O1r repeating T1 and O1 on 4.26.2's reworked start: all three are killed |
| `apply_p5.py`, `linux-p5.txt`, `mutant-P5-ProxyTap-linux.txt` | P5 on Linux: the tree with P5 applied, its stopping facts run there; the caller of a request taken but not yet handled when the tap stops hears `HTTP/1.1 200 OK`, so the fact fails and the mutant is killed |
| `linux-fix-1.txt`, `linux-fix-2.txt`, `linux-fix-3.txt` | The tap suites on Linux: the three on the patch before its rebase (`39642161`) and on the rebased tree (`26aad29a`), and ProxyTap's on the release commit (`46f21de0`) |
| `full-<suite>-windows.txt`, `full-ProxyTap-windows-2.txt` | The three tap suites on Windows on the rebased tree before the bump, and ProxyTap's again after the fix below |
| `checks-core-windows-1.txt`, `checks-core-windows.txt`, `checks-release-windows.txt` | The core suite and the Release build after the bump. The first core run failed `CultureInvariantSourceTests` on the patch's own new log line, a count of failed waits formatted with the machine's culture; the line names the invariant culture now, and the second run passes |
| `probe-suppress/` | The `SuppressFlow` probe and its output on .NET 8, 9 and 10 |
| `wiki_edits.py`, `ledger_audit.py` | The wiki edits and the Kronikol4J ledger line |
