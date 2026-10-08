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
