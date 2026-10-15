# R0's proofs (4.22.2)

R0 was written in a worktree of its own (`C:/Code/Kronikol-173-r0`, branch `release/173-r0`, from `origin/main` at
`85f849f7`, 4.22.1) and released as 4.22.2 (`9b7886d9`). Windows is Windows 11 26200 with .NET 10 (http.sys behind
`HttpListener`); Linux is `mcr.microsoft.com/dotnet/sdk:10.0` under podman's WSL machine (the managed `HttpListener`,
as on macOS), run with `linux-run.sh` (whole projects) and `linux-filtered.sh` (R0's facts only).

| Path | What it is |
|---|---|
| `red-stubs.diff`, `red_stubs.py` | The two seams R0's facts read, stubbed onto 4.22.1 so the facts compile there: `ProxyTap.Stopping` (`_disposed == 1`) and `HttpRequestReader.BodyBytesHeld` (the bytes allocated for the body) |
| `red-4.22.1-proxytap-windows.txt`, `red-4.22.1-otlp-windows.txt`, `red-4.22.1-tcptap-windows.txt` | R0's facts on 4.22.1 with the stubs, Windows: ProxyTap 25 of 28, Otlp 20 of 22, TcpTap 10 of 10 fail |
| `red-4.22.1-linux-summary.txt` | The same on Linux: ProxyTap 26 of 28, Otlp 20 of 22, TcpTap 10 of 10 |
| `red-4.22.1-proxytap-stopping-windows-2.txt`, `red-4.22.1-linux-stopping-2.txt` | `ProxyTapStoppingTests` again on 4.22.1 after the mutation run found the forward begun after the stop (below): 6 of 7 fail on Windows (the stalled body passes, since http.sys resets it), 7 of 7 on Linux. With them, 60 of R0's 65 cases fail on Windows and 57 of the 61 run on Linux |
| `red-exportsink-windows.txt` | `OtlpExportSinkLogTests` (found while implementing) on R0's tree before the export sink's fix: 4 of 4 fail |
| `mutations.py`, `mutations-windows.txt` | 44 mutations, each applied alone to a copy of R0's tree, with the facts of the project the file feeds. The first run (42 mutations) left P10 and P11 alive |
| `mutations-windows-p10-p17.txt`, `mutations-windows-p11b.txt` | After the fix those two found: P16 and P17 (the new check removed, or not answering) are killed, P11 is killed once `A_body_that_ends_as_the_tap_stops_is_neither_forwarded_nor_recorded` checks that the resolver is not asked, and P10 stays alive (below) |
| `final-*-windows.txt`, `final-linux-summary.txt`, `checks-windows.txt` | The three tap suites on the released code on both platforms, the core suite after the version bump, and the Release build of `release.slnf` |

## What the run found

- http.sys resets the connections it keeps alive when the listener closes, so a caller lost an unread `502` written
  during the stop. Answers written while the tap stops now say `Connection: close`.
- Closing the managed listener writes `HTTP/1.1 200 OK` with `Connection: close` and no body on each idle kept-alive
  connection; it answers no request. `Stopping_the_tap_lets_an_answer_in_hand_finish` allows exactly that after the answer.
- `OtlpExportSink` and `OtlpExporter` called `OtlpExportOptions.Log` unguarded, like the taps: a throw from a refused
  batch's line ended the export worker. Fixed in the release.
- P10 and P11 survived the first mutation run because a request the stop reached before its forward began was still
  forwarded, with the tap's cancelled token, and recorded as a call the stop had cut short (or, behind a resolver slower
  than the drain, met the disposed lifetime source). R0 refuses such a request with `503` and records nothing.
  `A_request_the_stop_reaches_before_its_forward_is_refused_and_not_recorded` fails on 4.22.1 and without the check.
- P10 (the forward reads the lifetime from `_cts` again) is equivalent under every stop a fact can make: the check
  before the forward returns first, so the line runs after the disposal only when host code between the two (an
  `InFlightRegistry` or an `ActivityListener`) holds the handler past it, and then both versions leave
  `ForwardFailures` at 0 and write no `502`. `_lifetime` stays for `Stopping`, which a disposed source would throw from.
