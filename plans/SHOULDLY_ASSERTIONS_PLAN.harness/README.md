# Harness for `SHOULDLY_ASSERTIONS_PLAN.md` (#141)

Measured on 2026-10-07 at Kronikol 4.9.0 (`417c8e58`), with the published packages `Kronikol` and
`Kronikol.AssertionTracking` 4.9.0, Shouldly 4.3.0 and AwesomeAssertions 9.6.0, on net8.0, built by SDK 10.0.300 on
Windows 11.

| Path | What it is |
|---|---|
| `probe/Probe.csproj`, `probe/Program.cs` | The probe: a console app with no test framework. `Track.TestIdResolver` names a test per case, and the assertion notes each case recorded are read back from `RequestResponseLogger`. `-p:KronikolVersion=<v>` picks the packages (default 4.9.0); `-p:Control=true` adds AwesomeAssertions and the `CONTROL` cases |
| `s0/plain-default.log`, `s0/plain-detailed.log`, `s0/plain-run.txt` | The repro (plan §2.1): the build at the default verbosity and at `-v:d`, and the run |
| `s0/control-build.log`, `s0/control-run.txt` | The control (§2.2): `-p:Control=true` at `-v:n`, and the run |
| `s0/embedded-build.log` | `-p:Control=true -p:DebugType=embedded` at `-v:d` (§2.3) |
| `proto/weaver-prototype.diff` | The prototype (§2.7): a change to `src/Kronikol.AssertionTracking/AssertionWeaver.cs` at `417c8e58`, with two switches read from the environment, `PROTO_CAPTURE_BEFORE=1` and `PROTO_SPILL_POP0=1` |
| `proto/Runner.csproj`, `proto/Runner.cs` | Runs the prototype weaver over a built assembly. Its `ProjectReference` names the worktree the diff was applied in |
| `proto/IlDump.csproj`, `proto/IlDump.cs` | Prints a method's IL with the source line where each sequence point starts |
| `proto/p1-debug-run.txt` to `proto/p7-release-spill-default-capture-run.txt` | The runs in §2.7's table |
| `proto/p5-release-il-before.txt`, `proto/p5-release-il-after.txt` | `Cases.ShouldBe_passes` in Release, before and after the prototype's weave |
| `shouldly/inventory-4.3.0-net8.0.md`, `shouldly/inventory-5.0.0-preview.2-net8.0.md` | Shouldly's public API by reflection over each package's `lib/net8.0/Shouldly.dll`: types, `[ShouldlyMethods]`, methods and overloads, return and parameter shapes (§2.6). The messages are in `s0/plain-run.txt` |
| `s0b/nowarn-results.txt`, `s0b/NoWarnProbe.csproj` | How a task's coded warning behaves under `<NoWarn>`, `<MSBuildWarningsAsMessages>`, `TreatWarningsAsErrors`, `-warnaserror` and `MSBuildTreatWarningsAsErrors`, on SDKs 8.0.421 and 10.0.300 (§3.9) |
| `s0b/cluster/tests.ndjson`, `s0b/cluster/Failures.md` | Six failing tests (four Shouldly messages, two FluentAssertions ones) fed to the published 4.9.0 `kronikol ingest` with `KRONIKOL_HISTORY=off`, and the digest it wrote (F14, F15) |
| `r1/red-4.10.0-*.txt`, `r1/red-4.11.0-*.txt` | R1's facts without R1's fixes: the new and changed facts copied into a worktree at each release, built and run there (`base` names the commit) |
| `r1/mutations.txt` | R1's mutations: each undoes one fix in a snapshot worktree, with the facts that went red |
| `r1/acceptance/` | R1's acceptance: the probe with its controls on local packages in Debug, in Release and with an embedded PDB, the published 4.11.0 in Release (F21), and the scripts that ran them |
| `r1/scope/` | The scope fix (F12) on FluentAssertions 6.12.2, 7.2.0 and 8.9.0 and AwesomeAssertions 8.2.0 and 9.6.0, on local packages (`run.sh`, `Program.cs`) |
| `r2/red-4.12.3-*.txt`, `r2/red-stubs.py`, `r2/red.sh` | R2's facts on R1's release, with the compile stubs `red-stubs.py` adds (members declared and never read): the weaver's facts with the IL net on and off, the core facts and the Playwright fact |
| `r2/mutations.txt`, `r2/mutate.py` | R2's mutations: plan §4.3's M1 to M9 and M15 to M18, then M19 on for what execution added |
| `r2/acceptance/` | R2's acceptance: the probe on local packages, Shouldly only and with its controls, in Debug and Release, with an embedded PDB, and with `<DebugType>none</DebugType>` for `KRONIKOL001` |

## Re-running

Build in a copy of `probe/` outside the repository, so no `bin/` or `obj/` lands under `plans/`.

The repro and the control (plan §2.1 to §2.3):

```sh
dotnet build -v:d | grep AssertionTracking     # the weaver's line
dotnet run --no-build                           # every case, its outcome and its notes
dotnet build -p:Control=true -v:n && dotnet run --no-build -p:Control=true
dotnet build -p:Control=true -p:DebugType=embedded -v:d | grep AssertionTracking
```

The prototype (§2.7):

1. `git worktree add <dir> 417c8e58` and `git -C <dir> apply <this folder>/proto/weaver-prototype.diff`.
2. Point `proto/Runner.csproj`'s `ProjectReference` at `<dir>/src/Kronikol.AssertionTracking/`, then build the runner.
3. Build the probe without `Control` (4.9.0's weaver skips it, so it is unwoven), in Debug or Release.
4. `dotnet <runner>/bin/Debug/net10.0/Runner.dll <probe>/bin/<Configuration>/net8.0/Probe.dll`, with the switches
   set as the run needs, then `dotnet run --no-build -c <Configuration>` in the probe.

Write each case one statement per line: Shouldly reads the source line of the failing frame, and a case written on
one line makes it quote `public static void` as the subject (plan §10, trap 4).
