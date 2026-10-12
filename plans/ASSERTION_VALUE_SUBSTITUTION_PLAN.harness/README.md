# Harness for `ASSERTION_VALUE_SUBSTITUTION_PLAN.md` (#163)

Measured on 2026-10-11 at Kronikol 4.14.6 (`d59843fa`), with the published packages `Kronikol` and
`Kronikol.AssertionTracking` 4.14.6, Shouldly 4.3.0 and FluentAssertions 6.12.1, on net8.0, built by SDK 10.0.300 on
Windows 11, in Debug and in Release.

| Path | What it is |
|---|---|
| `probe/Probe.csproj`, `probe/Program.cs` | The probe: a console app with no test framework. `Track.TestIdResolver` names a test per case, and the assertion notes each case recorded are read back from `RequestResponseLogger` and printed as the label (and, for a failure, the first line of the library's message). `-p:KronikolVersion=<v>` picks the packages (default 4.14.6) |
| `probe/WovenShouldly.cs` | 31 Shouldly statements, woven at build time: the issue's shapes and their neighbours (plan §2.3) |
| `probe/WovenFluent.cs` | 9 FluentAssertions 6.12.1 statements, woven |
| `probe/ThatCases.cs` | 10 `Track.That` cases (`ClosureValueResolver`). The type carries `[SuppressAssertionTracking]`, which does not stop the weave reaching the lambdas (F8), so each case draws two notes: the woven one, then `Track.That`'s |
| `probe/DirectCases.cs` | 10 calls to `Track.AssertionPassedWithValues` or `AssertionFailedWithValues` with a statement's text and the values the weave would pass: a getter that throws, a null root, a null link, TUnit's text, a value holding "to string()" |
| `probe/SuppressCases.cs` | 5 cases for `[SuppressAssertionTracking]` on a method and on a class (F8, plan §2.6) |
| `s0/build-4.14.6-debug.txt`, `s0/build-4.14.6-release.txt` | The weave's summary line: 55 statements instrumented |
| `s0/run-4.14.6-debug.txt`, `s0/run-4.14.6-release.txt` | Every case, its outcome and its notes. The two files are identical |
| `classify.py` | Counts `kronikol query assertions` lines (or the probe's output) by what their quoted values are (plan §4.4). A member's name in quotes (F5) and a whole record in quotes (F4) cannot be told from a real value by the text alone; the script lists quoted single words for a look and does not count them |

## Re-running

Build in a copy of `probe/` outside the repository, so no `bin/` or `obj/` lands under `plans/`, and so the
repository's `Directory.Build.props` does not apply:

```sh
cp -r probe "$TMP/probe" && cd "$TMP/probe"
dotnet build -v:n | grep Instrumented          # the weave's line
dotnet run --no-build > run-debug.txt           # every case, its outcome and its notes
dotnet build -c Release -v:n | grep Instrumented
dotnet run -c Release --no-build > run-release.txt
diff run-debug.txt run-release.txt              # no output: the labels do not depend on the configuration
python classify.py run-debug.txt                # PYTHONUTF8=1 on Windows
```

On local packages of a release candidate, add `-p:KronikolVersion=<label>` with an isolated NuGet cache, as the
earlier plans' acceptance runs did, and diff the run against `s0/`: every difference should be a §3.9 row.
