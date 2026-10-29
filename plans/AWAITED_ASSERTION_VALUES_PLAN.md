# Awaited assertion values plan (#211)

**Status:** written 2026-10-27 at 5.12.0 (`1ee6373a`) by session e132cc3e, at the owner's ask to analyse #211
critically and plan a fix for the underlying problem, which may differ from the one the issue proposes; audited the
same day at 5.14.0 (`8d7d1e1d`; the weaver has not changed since 4.22.3), at the owner's ask to make it complete and
accurate (section 10). **Executed** at the owner's word of 2026-10-29 ("implement the plan in full"), every question
of section 9 at its recommendation, as one patch on 5.27.2 (section 5, and section 10 for what changed on the way).
Line numbers in sections 3 and 4 are 5.12.0's (the same through 5.16.2) unless a section names another commit. #212
shipped first, as 5.26.1 and 5.27.0, and moved the awaited wrap into `WrapSingleAssertion`; section 8 maps this plan
onto it, and the release re-made the prototype there.

**Issue:** [#211](https://github.com/lemonlion/Kronikol/issues/211), "Assertion tracking, Release build: await
Should.ThrowAsync<T>(…) or NotThrowAsync(…) throws NullReferenceException when it's the method's first await and its
lambda captures a local", filed 2026-10-27 from a .NET monorepo's xUnit v2 suites (Kronikol 5.11.0, Shouldly 4.3.0,
net10.0, macOS arm64). Ten tests in six suites failed in Release only; the reporter wrapped each statement in
`// pragma:TrackAssertions:disable` / `enable`.

**Sibling:** [#212](https://github.com/lemonlion/Kronikol/issues/212) is the same statement shape in the same function
(its label is raw source in Release, and in Debug the statement has no note). kronikol-c6 plans it in
`plans/AWAITED_ASSERTION_LABELS_PLAN.md` (worktree `C:/Code/Kronikol-212`). The split, the lines each plan touches and
the release order are agreed (section 8).

**Harness:** `plans/AWAITED_ASSERTION_VALUES_PLAN.harness/`, described in its `README.md`. `repro/` is the issue's
project as filed, `shapes/` the probes P2 to P13 that vary it, both built by the SDK as a consumer builds them (a
`KronikolVersion` property picks the packages, default 5.12.0). `results/` holds every run, `il/` the woven and unwoven
`MoveNext` of each case the plan cites, `probe/` the fact that compared the suite's compile with csc's, and
`prototype.patch` the design of section 4 with its facts, as measured.

## 1. Summary

**What happens.** An awaited assertion (`await Should.ThrowAsync<T>(…)`, FluentAssertions' `await act.Should()
.ThrowAsync<T>().WithMessage(…)`, every TUnit assertion) is woven at the await's merge point: the weave wraps
`GetResult()` in a try and builds the note's values there, after the await. A variable the statement named is read
back through the same local of `MoveNext` the statement used. A Release build keeps a local in `MoveNext` only while it
needs it, and when the await suspends, the code after it runs in a later call of `MoveNext`, whose locals start
zeroed. So after a real suspension the weave reads a local that no longer holds anything:

- a closure (the display class of a lambda that captures a local) reads as null, and the weave's own `ldfld` on it
  throws `NullReferenceException`. The weave's catch records the user's assertion as failed with that message and
  rethrows it, before `GetResult()` has run: the test fails on the weave's error, the assertion's own verdict is never
  read, and the report draws a red assertion that is not the user's;
- a plain local reads as null or zero, and the note shows that value without any error: FluentAssertions'
  `WithMessage(message)` reads `'null'` in Release where Debug reads `'expected a1'`, in passing notes and in failing
  ones beside FluentAssertions' own message about the expected text.

Debug is not affected: it keeps every local in a field of the state machine, which survives the await.

**Which Release.** What a Release build keeps in locals depends on the compiler's debug-plus mode, which csc turns on
with `/debug+`, which MSBuild passes when `DebugSymbols` is true. The .NET SDK's Release default is false
(`/debug- /debug:portable`: a PDB, no debug-plus), and of the test frameworks only xUnit v2 sets it true, in
`xunit.core.props` (F7). So the issue's statements throw in xUnit v2's Release, as reported. The Release builds of
xUnit v3, TUnit, MSTest and NUnit keep most closures on the stack instead, and fail in other ways: an awaited
statement inside a `using` or `try` still throws the weave's exception, a plain local still reads null, and a
synchronous `Should.Throw<T>(() => …)` whose lambda captures a local reads the closure's field off the test class
(D-A), which returns the wrong object or null, or crashes the test process with an access violation when a value-type
field lies at that offset (measured, F8).

**What the issue gets wrong.** "The method's first await" is not the condition: a ThrowAsync in a loop after an
earlier await fails too (P2), and a first await whose variable is read again after it passes (P3). The third test of
the issue's table passes because its closure was created before its `await Task.Yield()` and so lives in a field. It
is not Shouldly's alone: FluentAssertions fails the same way (P10) and reads wrong values without failing (P9), and
so does TUnit (F6). Nor is it xUnit v2's alone: every framework's Release reads wrong values, and those other than
xUnit v2 reach the same exception in a `using` block.
"It hands back null, so the line that reads `e` throws" is a misreading: the woven IL throws in the weave's own read,
before `GetResult()`, and `exception` is never assigned. And Shouldly's `ThrowAsync` returns
`actual().ContinueWith(…).ContinueWith(…)`, two hops through the thread pool, so it suspends in practice every time:
the failure is not timing.

**Why nothing caught it.** The weaver suite compiles its Release fixtures with Roslyn's API at
`OptimizationLevel.Release` only, which is the Release of xUnit v3, TUnit, MSTest and NUnit, not xUnit v2's; its
SDK-built fixture project is a plain library, so `BuildWithSdk` builds the same. Debug-plus mode is what keeps a
closure in a local of `MoveNext`; without it, the closure stays on the stack. So the issue's own two tests pass there
on 5.12.0. No fact had an awaited assertion that suspends while it names a variable either: the #52 fact completes
synchronously, and the Shouldly fact that suspends names nothing. And no fact had a synchronous lambda capturing a
local in Release, which ILVerify would have rejected (D-A).

**The fix, one patch.** What the weave reads after an await, it carries across the await itself: each local the
statement's values are read through is stored in a field of the state machine where the statement stores its awaiter,
read from that field at the merge point, and cleared. The values are built in a protected region of their own, so a
fault in the weave's own code leaves the note without values and never becomes the assertion's verdict. The weaver
suite compiles Release both ways a project builds it. Two weave defects found on the way go in the same release: a
closure's field read off `this` (D-A, a memory-unsafe read in the Release of every framework but xUnit v2) and fields
named on the open generic state machine (D-B, unverifiable IL in every async test of a generic base class).

**Measured.** With the prototype, the issue's project passes 3 of 3 in Release (2 of 3 fail on 5.11.0 and 5.12.0); the
probes built by the SDK give the unwoven build's outcome in every case; Release notes equal Debug's, values included;
the 24 new facts pass, where 7 fail on 5.12.0 (8 on a run where `CompleteWithinAsync` suspends); and every existing
fact of the weaver suite still passes (430 passed, 28 skipped for SDKs not installed here). On the 31 shapes of the
audit, each compiled as Debug, as plain Release and as debug-plus Release, 5.14.0 throws the weave's exception in 4
shapes of plain Release and 13 of debug-plus Release, draws `'null'` in 2 more of each and adds ILVerify errors in 6
methods, and a shape built to show D-A crashes the test process; the prototype runs every shape as its unwoven build
does, draws Debug's notes in all three compiles (but for #212's Release label), adds no ILVerify error and does not
crash.

## 2. What the issue says, checked

| # | The issue says | Measured | Verdict |
|---|---|---|---|
| 1 | Release only | Debug passes 3 of 3 on 5.11.0 and 5.12.0 (`results/repro-*-Debug.txt`); Debug keeps the closure in `<>8__1`, a field (`il/repro-first-await.debug.woven.il`); the exception needs xUnit v2's Release (debug-plus) or, in any Release, a closure the code around it stores (F7) | true |
| 2 | Only when ThrowAsync is the method's first await | P2 (a loop after an earlier await) throws; P3 (the first await, the variable read again after it) passes; the issue's third test passes because its closure was created before `await Task.Yield()` and is a field (`il/repro-another-await-first.release.woven.il`) | a stand-in, wrong both ways (F2) |
| 3 | Only when the lambda captures a local | needed for the exception; a plain local named by the statement gives a wrong value instead, silently (P9) | half (F5) |
| 4 | Assigned, it hands back null, so the line that reads `e` throws | the woven IL throws at `ldloc.1; ldfld request` (`IL_00ae`), inside the weave's try, before `GetResult()`; the stack's line is the ThrowAsync statement's own | misreading (F1, F4) |
| 5 | NotThrowAsync in the same position throws too | the same awaited path | true |
| 6 | The pragma, or `TrackAssertionsEnabled=false`, avoids it | both leave the statement unwoven | true; costs the note |
| 7 | Suggestion: a test of the weave over this shape in a Release build | the suite's Release fixtures would not produce the shape at all (F7), and the shape is one of several | needed, not enough |
| 8 | Title: Shouldly's ThrowAsync and NotThrowAsync | every awaited assertion: Shouldly's, FluentAssertions' and AwesomeAssertions', TUnit's; in every framework's Release; and in the Release of every framework but xUnit v2, a synchronous one too (D-A) | too narrow (F6, F8) |

## 3. Findings

### 3.1 F1: the values of an awaited assertion are read after the await, from locals that end at the suspension

`WrapAwaitedAssertion` (`src/Kronikol.AssertionTracking/AssertionWeaver.cs:3124`; since 5.26.1 `WrapSingleAssertion`
from the merge point, the same read, section 8) puts its `try` at the merge point and builds the names and values
arrays at its start (3159-3223), after the await. Each value is read the way
`DetectCapturedVariables` (1431) found the statement reading it: through a local of `MoveNext` for a closure
(`ldloc` of the display class, then `ldfld`, 3192-3196) or a plain local (3212-3215), or through a field of the state
machine (3197-3207).

A local of `MoveNext` is valid for one call. The method is declared `.locals init`, so each call starts with every
local zeroed, and an await that suspends returns from `MoveNext`; the code after the merge point runs in the call the
continuation makes. Roslyn keeps a variable in a field only when its lifetime crosses an await. The closure of the
issue's statement dies when the delegate is made, before the await, so Release keeps it in local 1:

```
IL_000a: newobj <>c__DisplayClass1_0::.ctor()
IL_000f: stloc.1                                   // the closure, local 1
...
IL_0049: stloc.s 4                                 // the awaiter
IL_0052: brtrue.s IL_0095                          // completed at once: go to the merge point
...                                                // suspend: return from MoveNext
IL_0078: ldarg.0                                   // resume, in a later call: local 1 is null
...
.try {                                             // the weave's try, at the merge point
  ...
  IL_00ae: ldloc.1
  IL_00af: ldfld <>c__DisplayClass1_0::request     // NullReferenceException
  ...
  IL_00b9: call TaskAwaiter`1::GetResult()         // never reached
```

(`il/repro-first-await.release.woven.il`; unwoven: `il/repro-first-await.release.unwoven.il`.) The completed-at-once
path enters the same try in the same call, where local 1 still holds the closure, which is why a synchronous
completion passes.

### 3.2 F2: the conditions

All four must hold:

1. An optimized build (Release) that keeps the variable in a local of `MoveNext`. Debug-plus Release (xUnit v2's)
   keeps a closure there wherever it stands; plain Release (xUnit v3's, TUnit's, MSTest's, NUnit's) keeps a plain
   local there, and a closure only where the code around it needs the closure stored, as a `using` block or a
   `try`/`catch` does (measured; a `foreach` after an await does not, `results/dive-5.14.0.txt`). Debug keeps every
   local in a field (Debug passes everywhere it was run).
2. An awaited assertion: the weave's awaited path (`FindAssertionStatements`, 691-722).
3. The statement names a variable the weave reads through a local of `MoveNext`, a local whose lifetime in the
   compiled code does not cross an earlier await. Whether it does is the compiler's choice, not the source's: the
   issue's "first await" is a stand-in for it. P2 (`foreach` after `await Task.Yield()`) throws, because a loop body's
   closure is made in the loop; P2b (an `if` block after the same await) passes, because the compiler merged the
   block's closure into the method's, made before the await (`il/shapes-P2b_*.il`); P3 (the first await, the variable
   read again after it) passes, because that read makes the closure a field.
4. The await suspends: the awaited task is not complete when `IsCompleted` is read.

### 3.3 F3: Shouldly's ThrowAsync suspends, in practice every time

Shouldly 4.3.0's `Should.ThrowAsync<TException>(Func<Task> actual, string? customMessage)` returns
`actual().ContinueWith(…).ContinueWith(…)` (decompiled), two continuations with the default options and scheduler, so
the task it returns completes two thread-pool hops later and is not complete when the await reads `IsCompleted`, even
when `actual()` has already faulted. P4 (the delegate returns `Task.FromException`) throws as the issue's tests do. For
Shouldly, condition 4 holds in practice every time, and every closure in a local fails, every run. For
FluentAssertions and TUnit it holds when the code under test yields.

### 3.4 F4: the weave's error becomes the assertion's verdict, and the verdict is never read

The arrays are built inside the try that wraps `GetResult()`, so the weave's own exception is caught by the weave's
own catch: `AssertionFailedWithValues` records the user's assertion as failed with "Object reference not set to an
instance of an object." (values array still null), and `rethrow` fails the test with it. `GetResult()` never runs.
A `ThrowAsync` that would have failed (nothing thrown) and one that would have passed read the same; for
`NotThrowAsync`, the user's own exception is lost. The note in the report says the user's assertion failed.

### 3.5 F5: a plain local reads as null or zero, without an error

A plain local named after FluentAssertions' `Should()` is read with `ldloc` at the merge point, the same way, and
after a suspension it is zero. `await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message)` with
`message` a local (P9; `il/shapes-P9_*.il`: `IL_00b3: ldloc.2` after the resume) draws, on 5.12.0
(`results/notes-5.12.0.txt`):

| Build | Note |
|---|---|
| Debug | `✓ Act should throw async <InvalidOperationException>, 'expected a1'` |
| Release | `✓ Act should throw async <InvalidOperationException>, 'null'` |
| Release, assertion failing | `✗ … 'null'`, under FluentAssertions' "Expected the exception message to match the equivalent of "not the message", but "expected a1" does not." |

A value type reads as its default: FluentAssertions' `CompleteWithinAsync(timeout)` drew `'00:00:30'` in Debug and,
on the run where the delegate had not finished in time, `'00:00:00'` in Release (it passed on another run, when the
delegate completed first).

### 3.6 F6: which assertions

Every assertion the weave treats as awaited (`IsGetAwaiterOnAssertionResult`, 753):

- **Shouldly**: `Should.ThrowAsync`, `Should.NotThrowAsync`, `ShouldThrowAsync`, `ShouldNotThrowAsync`. Its values are
  its arguments, so a capturing lambda is read through its closure (the issue).
- **FluentAssertions and AwesomeAssertions**: values are what comes after `Should()` (the subject is not read, so P5
  and P6 pass), so `WithMessage(…)`, `Where(…)` and `CompleteWithinAsync(…)` arguments: a closure read throws (P10), a
  plain local reads wrong (P9).
- **TUnit**: every TUnit assertion is awaited. TUnit.Assertions 1.73.5's assertion types return a `TaskAwaiter<T>`
  from their own `GetAwaiter()`, so they take the same path. Measured with the suite's real TUnit.Assertions
  (`results/dive-*.txt`, the `DiveTUnit` rows): `IsEqualTo(expected)` with `expected` a local reads `'a1'` when the
  assertion completes at once, and `'null'` in both Release compiles after a suspension
  (`Assert.That(async () => await DelayedAsync("a1")).IsEqualTo(expected)`); `IsEqualTo(request.Id)` with `request`
  captured by the delegate throws the weave's exception in both. A TUnit delegate's own captures are not read at all
  (`Assert.That(() => _client.CallAsync(request)).Throws<T>()` captures nothing), so that statement is unaffected.

xUnit's, NUnit's and MSTest's own asserts are not woven (`ReferencesAssertionLibrary`, 332-344, and
`IsAssertionLibraryType`, 856-862, name the four libraries above), so `await Assert.ThrowsAsync<T>(…)` is not
affected.

### 3.7 F7: why the suite did not see it

- **There are two Release builds, and the suite compiles the other one.** csc's `/debug+` sets the compiler's
  debug-plus mode, which keeps a closure in a local of `MoveNext`; without it the closure stays on the stack
  (`newobj; dup; stfld; ldftn`) and there is no local to read after the await. Measured on the issue's code
  (`results/debugplus-probe.txt`): `<Issue_Unassigned>d__1`'s locals are `[Int32, Task, TaskAwaiter, Exception]`
  without debug-plus and `[Int32, <>c__DisplayClass1_0, Task, TaskAwaiter, Exception]` with it. MSBuild passes
  `/debug+` when `DebugSymbols` is true, and the SDK's Release default is false: a Release build passes
  `/debug- /debug:portable /optimize+` (a PDB, no debug-plus) unless something sets it. Of the test frameworks, only
  xUnit v2 does: `xunit.core` 2.9.3's `build/xunit.core.props` sets `<DebugSymbols>true</DebugSymbols>`
  unconditionally. Measured with `dotnet msbuild -p:Configuration=Release -getProperty:DebugSymbols` on a project per
  framework (`frameworks/`, the versions this repository uses): xUnit v2 true; xUnit v3 3.2.2 false (its targets set it
  only when empty, after the SDK has set false), TUnit 1.53.0, MSTest 4.2.2, NUnit 4.6.0 and a plain library false.
  So the issue's xUnit v2 project builds `/debug+ /debug:portable /optimize+` (measured, `-v:d`) and the others build
  without debug-plus. A `-p:DebugSymbols=false` Release passes `/debug-` alone, writes no PDB and is left unwoven
  (KRONIKOL001).
  `TestAssemblyBuilder.Build` compiles with `new CSharpCompilationOptions(…).WithOptimizationLevel(optimization)`
  (`TestAssemblyBuilder.cs:93`), the plain Release; and `BuildWithSdk` builds `Fixtures/WeaverFixture.csproj`, a plain
  library, so its Release is plain too (`/debug- /debug:portable /optimize+`, rebuilt by hand from its own steps). That
  is why kronikol-c6's pass of the verbatim repro through `BuildWithSdk` (SDK 10.0.300,
  `C:/Code/Kronikol-212/plans/AWAITED_ASSERTION_LABELS_PLAN.harness/probe/summary.txt`, "Pass 2", rows R211) did not
  throw: its `<ThrowAsync_is_the_first_await>d__1` has the locals `[Int32, TaskAwaiter, Exception]` and captures
  nothing. So the issue's two tests, copied verbatim into a fixture, pass on 5.12.0 either way, and the suite has
  never compiled xUnit v2's Release.
- **No fact suspended while naming a variable.** The #52 fact
  (`AssertionWeaverTests.cs:2112`, `Weave_AwaitedAssertion_WithCapturedVariables_FailingAssertion_DoesNotThrowNRE`)
  awaits `Task.FromException` and `Task.CompletedTask` through a stub awaitable, so it never suspends.
  `ShouldlyWeaveTests.cs:87` suspends (`async () => await Task.Yield()`) and names nothing. `CrossSdkWeaverTests`
  builds with real SDKs and suspends, but its awaited fixtures name no variables.
- **The facts checked "no exception", not "as unwoven" or "as Debug".** A comparison of a woven Release run with the
  unwoven one, and of its notes with Debug's, finds F1 and F5 in any shape; a check for one exception type does not.

### 3.8 F8 (D-A): a closure's field read off `this`, in the Release of every framework but xUnit v2

When a lambda reads a field that is not a hoisted local, `DetectCapturedVariables` takes it for a field of the test
class. Its "Non-async" branches (1745 for a lambda, 1807 for an expression tree's `ldtoken`) record the field to be read
as `ldarg.0; ldfld field` with no check of the field's type. For a closure kept on the stack, in an async method that
never uses `this` (so its state machine has no `<>4__this`), the field is the closure's, and the weave emits
`ldarg.0; ldfld <>c__DisplayClass3_0::id` in `MoveNext`, a read of one type's field off another (ILVerify:
"StackUnexpected … Found address of `<…>d__3`, Expected ref `<>c__DisplayClass3_0`"). The runtime does not check it:
the read takes whatever lies at that offset of the state machine.

It is reached by every compile that keeps a closure on the stack, which is the Release of every framework but xUnit
v2 (F7). The first pass of this plan called it latent because its probes P11 and P12 were built by the harness's
xUnit v2 project, whose debug-plus keeps the closure in a local (P12 throws F1's exception instead,
`il/shapes-P12_*.il`; P11 reads the closure correctly, `il/shapes-P11_*.il`). Measured in plain Release
(`results/dive-5.14.0.txt`, its ILVerify rows), five of the audit's shapes read a closure's field off the wrong
object, four of them synchronous: the issue's statement made synchronous,
`Should.Throw<InvalidOperationException>(() => _client.Get(request))` (kronikol-c6's S9,
`il/S9.release.woven.il.txt` in the #212 harness's `probe/`), the same in a generic base class, a lambda that
captures only the local (`() => new Client().Get(request)`), a synchronous `Should.ThrowAsync(…).GetAwaiter()
.GetResult()`, and an async method that never uses `this`. kronikol-c6's pass with `dotnet build -c Release
-p:DebugSymbols=true` against plain `-c Release` saw the same in its shapes S9, S9b, S15a, S15b and R212's `Throw`:
plain Release carries the closure on the stack into the statement and the woven IL fails ILVerify, debug-plus keeps
it in a local and verifies (`summary-212c.txt` in the #212 harness's `probe/`). The read takes whatever lies at that
offset of the other object: another field of the test class, the next object's header (null), or a value-type field
read as a reference.
The last one crashes. A test class whose first field is a `long`, running the synchronous statement above, ends the
test process with an access violation (exit code -1073741819, 0xC0000005,
`results/dive-crash-5.14.0-Release.txt`); the same fixture runs unwoven, with the field set to 0, built with
debug-plus, or built as Debug (`results/dive-crash-5.14.0-*.txt`), and it runs under the prototype's guard
(`results/dive-crash-proto211-Release.txt`). Any test class whose layout puts a value-type field there, a count or a
flag, can crash a Release run of xUnit v3, TUnit, MSTest or NUnit this way.

The guard leaves such a value out. That costs no text a reader sees in the shapes measured: a delegate lambda's
captured values are never written into its label, not even in Debug (`Client invoking(c => c get(request)) should
throw <InvalidOperationException>`; the `Invoking`, `Awaiting` and `Throw` rows), and the expression-tree branch
(1807), whose values labels do show (`Items should contain [ x => x == 'a1' ]`), keeps its closure in a local in every
compile (the `Fa_Contain_predicate` rows), so it gets the guard as a safety net only. The weave could read such a
closure correctly instead: to enter its try with an empty stack, the synchronous wrap already stores the closure in a
local of its own (`IL_0022: stloc.2` before the try, then `IL_004d: ldarg.0; ldfld …::request` for the value,
`il/dive-sync-release-woven.il`), so the read could go through that local (option G, 4.3).

### 3.9 F9 (D-B): fields named on the open generic state machine

In a generic async method (`Generic<T>`) of the test class, the weave reads `<>4__this` through the `FieldDefinition` it
found on the state machine (1680, used at 1737, 1799 and 1873), so `ldfld` names the field on the open type
`<Generic>d__7`1` where the method runs on `<Generic>d__7`1<T>`. ILVerify rejects it in Debug and Release ("Expected ref
`<Generic>d__7`1`"); the runtime accepts it today (P13 passes in Debug on the SDK, and in Release it fails with F1's
exception, not this one). The compiler names such a field through the type instantiated over its own parameters, and
the weave should too. It is not only generic methods: every async test of a generic base class has a generic state
machine (`Base<T>.In_generic_base` runs on `<In_generic_base>d__1<T>`), and the weave names `<>4__this` on the open
type and the test class's field on `Base<!0>` (ILVerify rows of all three compiles, `results/dive-5.14.0.txt`). The
prototype's helper clears both (`results/dive-proto211.txt`).

### 3.10 Since when

The awaited path came with `2c716801` (2026-05-08, "TUnit assertion tracking + async/awaited assertion support"), and
its values have been read at the merge point since; #52 (`3a991288`'s neighbourhood) moved the try before the arrays
for the completed-at-once path, not the read itself. Shouldly's async methods are woven since 4.14.0 (2026-10-08). The
weaver has not changed since 4.22.3 (`609ba8f3`), through 5.14.0 (`8d7d1e1d`); #210's 5.16.4 changed it since, at none
of this plan's lines (section 8).

### 3.11 Found on the way, not in this plan

- `GetLocalVariableNames` (2092) keeps the first name it meets for each local's slot across every scope, which would
  mislabel a slot two named locals share. Probed in the audit and not reproduced: two sibling blocks, each declaring a
  local used once or twice, keep separate slots in all three compiles (the `Sibling_blocks_*` rows draw
  `"one" should be 'one'` and `"two" should be 'two'` in each). Not a finding.
- kronikol-c6 found that a statement whose first `GetAwaiter` is not the assertion's own (`(await x).ShouldBe(y)`,
  `(await x).Should().Be(y)`, TUnit's `await Assert.That(await x).IsEqualTo(y)`) is never tracked, with no message,
  in Debug or Release (`FindAssertionStatements` 717-720; `IsGetAwaiterOnAssertionResult` 753-791 reads the first
  one only). That is c6's to plan; a wrap for it from the statement's last merge point would read its values after
  that merge point, which is this plan's mechanism (section 8).
- #212's two halves (the Release label, the Debug refusal) are kronikol-c6's (section 8), with what c6 measured on the
  way: Debug drops every awaited assertion whose result is used (assigned, returned, discarded, `ConfigureAwait`,
  compound) in Shouldly, FluentAssertions and TUnit alike; an un-awaited returned task is recorded as passed when it is
  made (S15); a compound `(await …).X.Should…` records the pass before the trailing assertion runs (S16); and
  `WrapSingleAssertion` (3103-3113) retargets a Debug `return x;` past `AssertionPassed`, so that pass is never recorded
  (S9b). None of it is in this plan's lines.

## 4. Design

### 4.1 The rule

**The weave reads a value through what the statement itself read, where that still holds it; what it needs after an
await, it carries across the await itself.** Concretely, in `WrapAwaitedAssertion` (since 5.26.1, in
`WrapSingleAssertion` for a statement with a merge point, section 8):

a. **Carry.** For each captured variable read through a local of `MoveNext` (a closure's display class or a plain
   local), the weave adds a private field to the state machine (`<>krk__carried{slot}`, one per local, shared by the
   statements that carry it) and stores the local in it right after the statement stores its awaiter
   (`GetAwaiter(); stloc awaiter`, then `ldarg.0; ldloc x; stfld field`). The stack is empty there, the instruction
   runs on the completed-at-once path and on the suspending one, and the statement has read every local it names.
   For a generic state machine, the field is named through the type instantiated over its own parameters. A
   byref-like local (a `Span<T>`) cannot be a field, and is never carried: the weave captures no byref-like, by-ref or
   pointer type in the first place (`IsUsableIn` and `CanHoldAsLocal`, 1390-1403).
b. **Read.** The arrays read `ldarg.0; ldfld field` (then the closure's own field) instead of `ldloc x`.
c. **Clear.** After the arrays are built, each carried field is cleared (`ldflda; initobj`), so the state machine does
   not keep the closure alive for the rest of the method, as the compiler's own temporaries are cleared.
d. **Protect.** The arrays are built in a `try`/`catch` of their own inside the assertion's try: if a read throws, the
   values array is null and the note is drawn without values (`Track.ResolveVariableValues` already takes a null array
   as none, `Track.cs:270`), and the assertion runs as compiled. A fault in the weave's bookkeeping never stands in for
   the user's verdict again.
e. **Fall back.** When the await is not the compiler's shape (`GetAwaiter()` followed by a store of the awaiter), a
   variable read through a local is left out of the values, never read at the merge point. An awaited statement is
   only found in a state machine's `MoveNext`: a synchronous `….GetAwaiter().GetResult()` is woven as a synchronous
   statement, read where it stands (`awaited=False` in the `*_GetResult` rows), so the prototype's check for
   `MoveNext` is a guard, not a path.
f. **D-A.** The two "Non-async" branches capture a field only when it belongs to the method's own type or a base of it
   (`IsFieldAccessibleOnType(field, method.DeclaringType)`, the check the async branches already make against
   `<>4__this`'s type, which walks base types and generic instances by name). A closure's field is then left out
   (3.8 measures that no label loses text for it), and the crash of 3.8 cannot happen.
g. **D-B.** Every field the weave names on a state machine (`<>4__this` at 1737, 1799 and 1873, and the carried fields)
   goes through one helper that instantiates a generic declaring type over its own parameters.
h. **The suite compiles Release both ways.** Today's Release fixtures are the plain Release of xUnit v3, TUnit, MSTest
   and NUnit (F7) and stay; each Release fact also runs on a debug-plus build, xUnit v2's Release, whose options come
   from csc's own command-line parser (`CSharpCommandLineParser.Default.Parse(["/debug+", "/debug:portable",
   "/optimize+", "/target:library"], …).CompilationOptions`), the public way to get debug-plus mode. Measured: the
   parser's options give the debug-plus shape (`ReleaseDebugPlus` rows: `Throw_capturing_a_local`'s locals
   `[<>c__DisplayClass2_0, InvalidOperationException]` against `[]` in plain Release). `BuildWithSdk` gains a
   `-p:DebugSymbols=true` build beside its plain one, since its fixture project is a plain library, each in a project
   directory of its own: kronikol-c6 measured that two builds of one fixture project differing only by `DebugSymbols`
   share `obj/`, and the second skips `CoreCompile` and keeps the first build's DLL, so a debug-plus fact would test
   plain Release without a word (`summary-212c.txt` in the #212 harness's `probe/`). Debug needs no
   second build: debug-plus changes nothing in a Debug compile (`results/debugplus-scope.txt`: all 155 methods of the
   audit's shapes alike, against 20 of 134 in Release). #212 shipped the debug-plus mode as
   `BuildConfiguration.ReleaseDebugPlus` in `TestAssemblyBuilder` (5.26.1, section 8); this plan uses it, and adds
   `BuildWithSdk`'s debug-plus build if 5.26.1 has none.
i. **The statements #212 weaves first.** 5.26.1 weaves statements whose first `GetAwaiter` is not the assertion's own
   (`(await x).ShouldBe(y)`, TUnit's `await Assert.That(await x).IsEqualTo(y)`) and leaves out every value it would
   read through a local of `MoveNext` (`IsKeptAcrossTheAwait`, section 8). Once this plan carries such values, that
   filter goes, and those statements draw in Release the values they draw in Debug. A variable the source reads after
   an inner await already crosses it and is a field; a local read only before it is carried by the rule of a, with
   one change for a statement with two awaits: the store goes at the first awaiter store after the local's last load,
   not at the last one (the prototype's last `GetAwaiter` before the merge point, the same when the statement has one
   await, and for TUnit's shape above, whose `y` is loaded between the two). A fact per shape of 5.26.1's catalogue.

Debug is unchanged except for D-B: its values are already read through fields, and the carry applies only to locals.
The values are still read after the await, as Debug reads them; for a plain local that is the same value as at the
store, since nothing but the statement itself can assign it in between.

### 4.2 What changes in the report

- A Release run's awaited assertions draw the values a Debug run draws (P9's `'expected a1'`, not `'null'`).
- A Release run that failed with the weave's `NullReferenceException` runs the assertion and draws its own verdict.
- If reading a value ever fails, the note is drawn without values, where it was drawn failed.
- A Release run of xUnit v3, TUnit, MSTest or NUnit no longer reads a closure's field off another object (D-A): the
  value is left out, which no label measured shows (3.8), and a run that crashed the test process runs.
- Debug notes are unchanged (measured: every Debug note of the audit's shapes is the same on 5.14.0 and on the
  prototype). No label text changes (labels are #212's).

### 4.3 Options considered

| Option | What it does | Verdict |
|---|---|---|
| **A. Carry in fields (4.1)** | stores what the statement read before the await, reads it after | **recommended**: one patch, every awaitable kind (Task, ValueTask, TUnit's types), Debug's read time kept, the failure's stack trace unchanged |
| B. Build the values before the await, into one field | snapshots every value at the call | changes when Debug reads a closure's fields (before the await, not after), so Debug's notes would change for code that assigns a captured variable while it runs |
| C. Restore the locals on the resume path | saves each local before the suspension and writes it back after | the same values as A, but two insertion points per await (the suspend path and the resume label) inside code the compiler owns, and it writes the compiler's own locals |
| D. Leave such variables out of the values | never reads a local at the merge point | the smallest change, but a Release note loses values Debug has (the issue's `request`); kept as A's fallback only |
| E. Pass the awaited task through a Kronikol helper before `GetAwaiter()` (kronikol-c6's idea, weighed for #212) | builds the values before any suspension; the helper records pass or fail when the task completes | needs a new public Track member (a minor, and a version gate against an older Kronikol beside a newer weave); TUnit's awaitables and any ValueTask-shaped assertion stay on the merge-point path, which A must fix anyway; the failure's stack trace gains Kronikol's frames; c6 and this plan agreed not to take it (section 8) |
| F. The issue's suggestion: a fact of the shape in Release | | necessary (section 6), not a fix; and with the suite's builder as it is, the fact would pass on 5.12.0 |
| G. Read a closure kept on the stack through the weave's own entry spill (D-A) | the synchronous wrap already stores the stack's closure in a local to enter its try (`il/dive-sync-release-woven.il`); read the closure's fields through it, and for an awaited statement copy it at the statement's start and carry it as in A | values D-A's guard leaves out would be read; not taken now: no label measured shows them (3.8), and it adds an insertion point in code the compiler owns; the release's guard keeps the door open |

### 4.4 What does not change

The merge-point wrap, its exit spill and the completed-at-once retarget; the Track API; the labels; the weave of any
statement that is not awaited (it reads where the statement reads, in the same call of `MoveNext`).

### 4.5 Cost

Per carried local: one field of the state machine (a struct in Release, so the boxed state machine grows by a pointer),
three instructions at the store, three at the clear, and one more load at the read; per statement with values, a
protected region of eight instructions. Only awaited statements that name a variable through a local carry anything.

## 5. Releases

One patch: fixes only, nothing new for a consumer to call. It goes on top of #212's 5.26.1 (section 8).
Its number is the next free patch at push, claimed then. The changelog's Changed section names the behaviour changes
of 4.2. Shipped as 5.27.4 on 2026-10-29, on top of 5.27.3 (`38e5eaf4`, kronikol-2d's #205 R2b).

## 6. Tests (TDD: each written red against 5.12.0, then made green)

### 6.1 Fixtures

- `AwaitedAssertionResumeTests` (new; prototype in `prototype.patch`): `Cases`, eleven awaited assertions that all
  suspend: the issue's two tests verbatim; Shouldly's ThrowAsync assigned and unassigned at the first await,
  NotThrowAsync, a ThrowAsync in a loop after an await; FluentAssertions' `WithMessage` reading a closure, a plain local
  (passing and failing) and `CompleteWithinAsync` with a value-type local. `GenericCases`: an instance generic method
  capturing `this` and a local. `StackClosureCases`: a closure kept on the stack in an async method that never uses
  `this`, built with the old options (D-A), beside the expression-tree case of 3.8 as a guard of the path that works.
- From the audit (each measured on 5.14.0 and on the prototype, `probe/Dive211Tests.cs`): `Cases` gains an awaited
  statement in a `using` block, in a `try`/`catch` and in a `try`/`finally`, a `foreach` after an await,
  `ConfigureAwait(false)`, two statements on one closure, a `ValueTask` test method, an async lambda and an async
  local function holding the assertion, FluentAssertions' `Awaiting(…)`, and TUnit's `IsEqualTo` after a suspension,
  with a local and with a closure (the suite's real TUnit.Assertions). `GenericCases` gains an async test of a generic
  base class (D-B). `StackClosureCases` gains the four synchronous shapes of 3.8, the static callee in an async method
  that never uses `this` (D-A in plain Release, F1 in debug-plus) and the crash shape (a `long` field where the
  closure's field would be); on 5.12.0 they fail at ILVerify, which the facts run before the fixture, so the red proof
  never runs the read that crashes the test process.
- Every fact of the fixture runs in Debug, in plain Release and in debug-plus Release (4.1 h); the notes facts
  compare each Release with Debug.
- On 5.26.1 (4.1 i): the new cases join #212's catalogue (`Fixtures/AwaitedShapes/`, `AwaitedAssertionShapeTests`),
  with a case per shape it weaves first, each naming a value through a local, among them TUnit's
  `await Assert.That(await x).IsEqualTo(expected)` and a local loaded only before the inner await. The catalogue
  builds five of its shapes in Debug and debug-plus Release only, because of D-A (S9, S9b, S15a, S15b and R212: their
  `// Builds: Debug, ReleaseDebugPlus` header says plain Release is left out for it); the guard (4.1 f) adds `Release`
  to each header, so the catalogue checks the guard in plain Release as it checks every other shape there.
- As built: the cases above are catalogue shapes, each built in Debug, plain Release and debug-plus Release. R211 is the
  issue's project as filed; S27 to S35 are the audit's Shouldly shapes (a `using` block, a `try` with a catch, a `try`
  with a finally, a `foreach` after an await, `ConfigureAwait(false)`, two statements on one closure, a `ValueTask`
  method, an async lambda, an async local function); S36 to S39 FluentAssertions' (`Awaiting`, `WithMessage` with a
  plain local and with a closure, `CompleteWithinAsync` with a value-type local); S40 and S41 TUnit's after a
  suspension; S42 and S43 D-A's (an async method that never uses `this`, a lambda that captures only the local); S44 a
  generic base class and S45 a generic method (D-B); S46 the crash shape; S47 and S48 4.1 i's (a value loaded before
  the statement's only await, and before both of its two); S49 `NotThrowAsync`; S50 AwesomeAssertions' `WithMessage`;
  S51 and S52 a static helper's lambda and expression tree reading the class's field through a parameter (section 10,
  found in the release's review).
  5.27.0 added three more shapes that left out plain Release for D-A (S15c, S15d, S26), so the guard adds `Release` to
  eight. S17g, a TUnit statement #212 weaves first, now draws its local's value in every build. S47 and S48 are
  FluentAssertions' because a Shouldly statement cannot have the shape: the compiler spills a Shouldly subject loaded
  before an await into a field of its own, and the weave reads Shouldly's values from where that spill is loaded
  again, after the await. `AwaitedAssertionResumeTests` holds what a shape cannot: a read made to fail (4.1 d), an
  await of another shape (4.1 e) and the cleared field (4.1 c). R211, S37 and S48 join the SDK rows.
- Still to add: AwesomeAssertions' `WithMessage` (same fixture, second library), and each pre-existing defect's cases
  in a fixture of their own, since ILVerify reads a whole assembly and one unverifiable method fails every case built
  with it. A case that reaches D-A's expression-tree branch was looked for again and not found (3.8).
- `CrossSdkWeaverTests` gains the issue's project shape, built plain and with `-p:DebugSymbols=true`, each in a project
  directory of its own (4.1 h), so SDKs 8, 9 and 10 each build both Release builds a consumer's project makes.

### 6.2 Facts

| Fact | 5.12.0 (and 5.14.0, the same weaver) | Prototype |
|---|---|---|
| `A_woven_optimized_build_runs_as_the_unwoven_one` × 10 methods: same exception type and message as the unwoven Release build | 1 fails (FluentAssertions' closure: NRE); the issue's own 2 pass only because the fixture builder keeps the closure on the stack (F7) | all pass |
| `An_optimized_build_draws_the_notes_a_debug_build_draws` × 10: outcome, label, message and line equal | 3 fail (`'null'`, and the NRE note) and 1 more on the runs where `CompleteWithinAsync` suspends | all pass; the assigned ThrowAsync rows are compared once #212 is in |
| `An_awaited_assertion_in_a_generic_method_verifies_and_runs` (Debug, Release) | both fail ILVerify (D-B) | pass |
| `A_closure_kept_on_the_stack_is_not_read_off_the_state_machine` (Debug, Release) | Release fails ILVerify (D-A) | pass |
| Every fact above, on debug-plus fixtures too (4.1 h) | the issue's own two rows fail with F1's NRE, as in `results/repro-5.12.0-Release.txt` | pass |
| The audit's shapes (6.1), each in Debug and both Release compiles (`results/dive-5.14.0.txt`, `results/dive-proto211.txt`) | plain Release: 4 throw the weave's exception, 2 draw `'null'`, 6 methods fail ILVerify (D-A in 5, D-B in 1), the crash shape ends the test process; debug-plus Release: 13 throw, 2 draw `'null'`, 1 method fails ILVerify (D-B); Debug: 1 (D-B) | every shape runs as unwoven and draws Debug's notes in all three compiles (but for #212's Release label), no ILVerify error, no crash |

Every existing Release fact of the suite was also run on debug-plus fixtures in the audit (`TestAssemblyBuilder`
switched by an environment variable, `probe/dive-TestAssemblyBuilder.patch`, the switch checked inside the test host):
on 5.14.0 and on the prototype alike, 407 pass and 28 skip for SDKs not installed here, and the one failure in each run
is the suite's own check that every weave goes through ILVerify, flagging the audit's probe file
(`results/weaver-suite-debugplus.txt`). So no existing fact
covers a shape debug-plus changes, and the new facts are what 4.1 h is for. In Debug, debug-plus changes nothing (the
155 methods of the audit's shapes have the same locals and instruction counts either way); in Release it changes 20 of
134 (`results/debugplus-scope.txt`).

### 6.3 Mutations

| # | Mutation | Caught by |
|---|---|---|
| M1 | read the local at the merge point again (5.12.0's read) | the issue's rows and P9's (notes, outcome) |
| M2 | store into the field after the merge point instead of after the awaiter's store | the same rows: the suspending path never stores |
| M3 | drop the protected region | with M1 also applied, the outcome facts fail on the NRE rather than only the notes facts |
| M4 | carry closures only, not plain locals | P9's and `CompleteWithinAsync`'s note facts |
| M5 | name the carried field on the open generic type | the generic fact (ILVerify) |
| M6 | drop the D-A guard | the stack-closure fact (ILVerify), and the plain Release builds of #212's five D-A shapes (6.1) |
| M7 | skip the clear | a fact that the closure is collectable after the statement while the method is still running (a `WeakReference` read after `GC.Collect`), to write |
| M8 | builder back to one Release, plain only | the issue's rows pass on 5.12.0 again (the red proof shows it); with debug-plus only instead, the stack-closure rows stop reaching D-A |

As run on the release (`results/mutations-release.txt`), each mutation of the weaver against the facts named, every one
caught:

| # | Caught by |
|---|---|
| M1 | the notes of S37, S38, S40, S41 and S48 in both Release builds. The protected region now keeps the outcome, so the issue's rows, whose labels show no value, pass under M1 alone |
| M2 | the same rows as M1 |
| M3 | `A_value_whose_read_throws_leaves_the_note_without_values`, both Release builds |
| M4 | the notes of S37, S39, S40, S47, S48 and S50, both Release builds |
| M5 | S44 and S45 in debug-plus Release, every fact (ILVerify), where the generic method's closure is in a local |
| M5b, `<>4__this` on the open type again (D-B) | S44 and S45 in all three builds, every fact |
| M6 | R212, S9, S9b, S15a, S26, S42, S43, S44 and S46 in plain Release, every fact (ILVerify; S46 never reaches its run) |
| M7 | `A_carried_closure_is_let_go_after_its_statement`, both Release builds |
| M8 | the red proof: on 5.27.2, R211 fails in debug-plus Release only and the D-A shapes in plain Release only |
| M8i, the store at the statement's last await (4.1 i) | the notes of S48 in both Release builds |
| M9, the shape checked per value, not per statement (4.1 e) | `An_await_the_weave_cannot_carry_a_value_across_draws_no_value`, all three builds |
| M10, the guard without its static-method test | S51 and S52 in all three builds, every fact (ILVerify: an `Int32` where `Probe` was expected) |

### 6.4 Checks

The weaver suite on net8.0, net9.0 and net10.0, with ILVerify over every weave as now; `TargetsBuildTests` (real
`dotnet build`); the core suite; the issue's project and the shapes on packages packed from the release, in Debug and
Release; the weaver suite in the Linux container; release.slnf in Release.

## 7. Documentation and ledger

- `CHANGELOG.md`: the patch's entry, with 4.2 under Changed.
- The wiki's `Assertion-Tracking.md` (lines at wiki `183dbb0`): "Variable Value Resolution" lists "Async method
  closures (variables captured across `await` boundaries)" as working (276), and "Async and Awaited Assertions" says
  the weaver "fully supports" assertions in async methods, awaited ones included (306-312). Both are false of a Release
  build today and true after the release; the release adds one sentence under "Async and Awaited Assertions": a
  Release build's notes carry the values a Debug build's carry, in either Release a project builds (the version
  named), and a delegate lambda's captured values are not drawn in either.
- Kronikol4J: nothing to port. The weaver is .NET's own, and the report's format does not change.
- #211: a comment once the release is out, with the conditions of F2 and F6, at the owner's word (section 9).

## 8. Coordination

- **#212 (kronikol-c6, `AWAITED_ASSERTION_LABELS_PLAN.md`).** Agreed on 2026-10-27: this plan owns where and when the
  values are read (4.1 a to e), c6's owns the label text and the merge-point refusal. Both keep the merge-point wrap
  (option E not taken). c6's fix touches `FindGetResultInstruction` (798-854), `AwaitedRange` (2606-2621), PlanStack's
  awaited branch (2538) and only the `AwaitedRange` call in `WrapAwaitedAssertion` (3147); this plan touches the arrays
  block (3159-3223), adds the store after the awaiter's store and the protected region, and the capture branches of
  4.1 f and g. c6's `tryStart` moves up to the real merge point; the carried reads and the protected region still go
  between `tryStart` and the awaiter's load. c6's plan also changes 2521 and 2527 (PlanStack's part-way checks, for an
  awaited statement), 3255-3274 (the awaited pass moved to the statement's end) and 3093-3113 with 683-689 (a
  trailing branch trimmed like a leave); c6 checked this plan's `prototype.patch` (1734-1870, 3135-3149, 3159-3226,
  3374-3383): the two meet at 3147 only.
- **The order, reversed 2026-10-28.** The owner green-lit #212 in full while this plan waited, so #212 shipped first
  and this plan rebases onto it. #212's R1 is 5.26.1 (`9b2331d5`; line numbers here are that commit's):
  `WrapAwaitedAssertion` and `AwaitedRange` are gone. An awaited statement is wrapped by `WrapSingleAssertion` from its
  merge point (`AssertionStatement.MergePoint`; `StartOf`, 2739), and its values are built there, before the merge
  point's first instruction (3016-3109: `ldloc` of a closure at 3066, of a plain local at 3087). A statement read as
  before (`ReadAsBefore`, 902: one await, on a library's assertion, which is every shape of this plan) still reads
  MoveNext's locals there, so #211 is unchanged on 5.26.1, as its own comment says (715-719) and as the issue's project
  shows on the published packages: 2 of 3 fail in Release, 3 of 3 pass in Debug (`results/repro-5.26.1-*`, 2026-10-29).
  A statement R1 weaves for the first time (an assertion on an awaited value, two awaits, a suite's own async
  `[ShouldlyMethods]`) keeps only the values `IsKeptAcrossTheAwait` (920) finds in fields of the state machine, so it
  never reads a dead local and draws no value in Release that Debug lacks. #212 also shipped
  `BuildConfiguration.ReleaseDebugPlus` in `TestAssemblyBuilder` (4.1 h) and a shape catalogue
  (`Fixtures/AwaitedShapes/`, `AwaitedAssertionShapeTests`) that this plan's shapes join, and its facts keep #211's
  shapes out of their comparisons (an `await Task.Yield()` first). At this plan's release, `prototype.patch` is re-made
  on 5.26.1: its `DetectCapturedVariables` hunks (D-A, D-B) apply there as they are (182 lines lower, checked); its
  `WrapAwaitedAssertion` hunks move into `WrapSingleAssertion` (the carry for an awaited statement in a `MoveNext`, the
  reads at 3066 and 3087 from the field, the protected region around the arrays); then its facts and the dive run again.
  #212's R2 (5.27.0, under way on 2026-10-29) changes `WrapSingleAssertion` and `WrapAssertions` again (it observes an
  assertion's task that the statement does not await), so the re-make goes on whichever release is on main then; R2's
  new shape S26 (a synchronous wait) leaves plain Release out for D-A too.
- **#210 (kronikol-be, `ASSERTION_LABELS_PLAN.md`).** Shipped: 5.16.3 (`186d0ebc`, the labels) and 5.16.4
  (`f33ed3e9`, the weave: `ReadSourceText`, `IsPragmaDisabled`, `EvaluatesSomething` and `IsConstant`, so a constant
  argument reads as its value). No line in common with this plan; it moves every line of this plan's from 1390 on
  down by 97, and `prototype.patch` applies cleanly to main after it (`3d1817b2`, 5.22.0, checked 2026-10-28).
- No other worktree changed `src/Kronikol.AssertionTracking` at the audit (2026-10-27: the worktrees with weaver
  changes were #210's two and this plan's own); #212's is the one to rebase onto now.
- The version number is claimed by SendMessage at push, as usual.

## 9. Questions for the owner

1. **Design A** (carry the locals in fields, 4.1), or the smaller D (leave such values out of Release notes)?
   Recommended: A.
2. **The suite compiles Release both ways** (4.1 h): today's plain Release kept, and each Release fact also run on a
   debug-plus build, xUnit v2's Release, in this release? Measured on the prototype: every existing fact passes on
   debug-plus fixtures (6.2). Recommended: yes.
3. **D-A and D-B in this release**, as CLAUDE.md asks for bugs found on the way? Recommended: yes.
4. **Clear the carried fields** after use (4.1 c)? Recommended: yes.
5. **A comment on #211** with the corrected conditions and an interim workaround (read the variable again after the
   await, which makes the compiler keep it in a field, or the pragma it already uses): now, at the release, or not at
   all? Recommended: at the release.
6. **One patch, or D-A's guard first?** D-A can crash a Release test run of xUnit v3, TUnit, MSTest or NUnit (3.8),
   which may argue for shipping its two-line guard and its fact ahead of the rest. The rest is prototyped too and
   shares the same red-green proof and the same suite change (4.1 h). Recommended: one patch, unless you want D-A out
   sooner.

Answered 2026-10-29: the owner asked for the plan to be implemented in full, so each question went at its
recommendation: design A, both Release builds in the suite, D-A and D-B in the release, the fields cleared, a comment
on #211 at the release, and one patch.

## 10. Log

- 2026-10-27, e132cc3e: issue reproduced on 5.11.0 and 5.12.0 from nuget.org (Windows, SDK 10.0.300). Probes P2 to P13
  built by the SDK, the IL of each case dumped, the in-memory compile compared with csc's (debug-plus, `probe/`).
  Prototype of 4.1 a to g in `prototype.patch` (applies cleanly to `1ee6373a`): the issue's project passes 3 of 3 in
  Release, the shapes match the unwoven build, 24 of 24 new facts pass (7 or 8 fail on 5.12.0,
  `results/facts-5.12.0.txt`), and the weaver suite passes 430 with 28 skipped (`results/weaver-suite-proto211.txt`;
  its 2 failures are the debug-plus probe, run there without its output path and since moved to `probe/`). 4.1 h is
  designed, not prototyped. Split with #212 agreed with kronikol-c6.
- 2026-10-27, e132cc3e: 5.12.1 went to a Search tab patch (`79e8d644`), so section 5 names no number. It leaves the
  weaver, its tests and `Track` alone, and `prototype.patch` applies cleanly to it. The probe projects now make NU1603
  an error: with a `feed/` that lacked the version asked for, a restore of `5.12.0-proto211` exited 0 on the published
  5.12.0, as `5.12.1-proto211` would on 5.12.1 once it is on nuget.org; it now fails.
- 2026-10-27, e132cc3e: the audit, at the owner's ask to make the plan complete and accurate, on 5.14.0 (`8d7d1e1d`,
  whose weaver is 5.12.0's; `prototype.patch` applies cleanly to 5.13.0 and 5.14.0 too).
  - The issue re-read (`gh issue view 211`): section 2 holds.
  - Which Release (F7): only xUnit v2 builds Release with debug-plus, since `xunit.core.props` sets `DebugSymbols`;
    xUnit v3, TUnit, MSTest, NUnit and plain libraries build `/debug- /debug:portable` (`frameworks/`,
    `results/frameworks-release.txt`). That explains kronikol-c6's `BuildWithSdk` result, which the first pass left
    open, and shows the suite's in-memory Release is the other frameworks' Release.
  - A dive of 31 shapes in three compiles, on 5.14.0 and on the prototype (`probe/Dive211Tests.cs`,
    `results/dive-*.txt`): the weave's exception in plain Release too (`using`, `try`/`catch`, FluentAssertions'
    closure, TUnit's closure), `'null'` in both Releases (FluentAssertions, TUnit), D-A live in plain Release in five
    shapes and crashing the test process in a sixth built for it (`results/dive-crash-*`), D-B in generic base
    classes. The prototype fixes every shape and changes no Debug note; it needed no change. 4.1 h's debug-plus compile
    is prototyped in the probe's builder patch (`probe/dive-TestAssemblyBuilder.patch`).
  - Measured, not findings: a synchronous `.GetAwaiter().GetResult()` is woven as a synchronous statement (4.1 e);
    sibling-block locals keep separate slots (3.11); byref-like locals are never captured (4.1 a); Shouldly's
    `ThrowAsync` chains two continuations (F3); TUnit's delegate captures are not read, and xUnit's, NUnit's and
    MSTest's asserts are not woven (F6); `-p:DebugSymbols=false` leaves a project unwoven (F7); debug-plus changes no
    Debug code (6.2).
  - The whole weaver suite on debug-plus fixtures: no existing fact turns red, on 5.14.0 or on the prototype (6.2).
  - Changed in the plan: section 1, F2's condition 1, F3, F6, F7, F8 (D-A is live, not latent), F9, 3.11, 4.1 a, e, f
    and h (both Release compiles, not one), 4.2, option G, 6.1 to 6.3, section 7's wiki lines, section 8 (#210 and
    c6's further lines), questions 2 and 6.
- 2026-10-28, e132cc3e: the order is reversed. The owner green-lit #212 in full, so it ships first and this plan
  rebases onto it (kronikol-c6). #212 adds 4.1 h's debug-plus mode, and its F8 starts to weave statements with an
  await before the assertion, whose values this plan's mechanism must cover (4.1 i, new). #210 shipped meanwhile
  (5.16.3, 5.16.4), moving this plan's lines from 1390 on down by 97; `prototype.patch` still applies cleanly to main
  (`3d1817b2`, 5.22.0). Changed: the status, 3.10, 4.1 h and i, section 5, 6.1, section 8.
- 2026-10-28, e132cc3e: #212's R1 shipped as 5.26.1 (`9b2331d5`, kronikol-c6). The awaited wrap is
  `WrapSingleAssertion` from the merge point now; #211's shapes are read as before (`ReadAsBefore`), so the defect is
  unchanged, and the statements R1 weaves first leave out every value a dead local would give (`IsKeptAcrossTheAwait`).
  The prototype's D-A and D-B hunks apply to 5.26.1 as they are; its carry moves into `WrapSingleAssertion` at the
  release. Changed: the status, 3.1, 4.1 (h, i and the wrap's name), section 5, 6.1, section 8.
- 2026-10-29, e132cc3e: the issue's project on the published 5.26.1 packages (`-p:KronikolVersion=5.26.1`): Release
  fails 2 of 3 with the NullReferenceException, Debug passes 3 of 3, as on 5.11.0 and 5.12.0
  (`results/repro-5.26.1-*.txt`). #212's catalogue at 5.26.1 builds five shapes without plain Release because of D-A
  (S9, S9b, S15a, S15b, R212), so the guard puts it back (6.1, M6). Changed: 6.1, 6.3, section 8.
- 2026-10-29, e132cc3e: executed, at the owner's word, on 5.27.2 (`e4a1c82a`; #212's 5.27.0 had changed
  `WrapSingleAssertion` and `WrapAssertions` again). The prototype was re-made, not applied:
  - `FindAssertionStatements` finds, for each value an awaited statement reads through a local of `MoveNext`, the
    awaiter store to carry it from (`CarryPoints`, 4.1 a and i), which replaces #212's `ReadAsBefore`. The
    `IsKeptAcrossTheAwait` filter stays only for a statement that cannot be carried (4.1 e).
  - `WrapSingleAssertion` stores, reads and clears each carried field (`CarryAcrossTheAwait`, `OwnField`) and builds an
    awaited statement's values in a protected region of their own (4.1 b to d). D-A's guard and D-B's helper went in
    as the prototype had them (4.1 f, g).
  - Three things changed from the prototype on the way:
    - The fallback is decided per statement. If any of its awaits is not `GetAwaiter()` and a store of the awaiter,
      none of its values is carried. The first version checked each value alone, so Debug, whose values are fields,
      kept a value that Release dropped (M9).
    - The protected region now also keeps the outcome of the issue's rows if a carry is lost: M1 and M2 are caught by
      the rows whose labels show a value, not by the outcome rows.
    - 4.1 i's shapes are FluentAssertions' (S47, S48). A Shouldly statement cannot have the shape, because the
      compiler spills its subject before an await (6.1).
  - Measured: on 5.27.2, 124 of the catalogue's 868 rows fail (`results/catalogue-5.27.2.txt`), all seven new facts
    fail, and the crash shape stops at ILVerify. On the release all pass (`results/catalogue-release.txt`), and every
    mutation of 6.3 is caught (`results/mutations-release.txt`).
  - kronikol-13 plans #213 in `DetectCapturedVariables`, a capture read off the state machine, the same class as D-A.
    It agreed that #211 lands first; its rule then replaces both D-A guards and keeps `OwnField`.
  - The release's review found one more defect of D-A's kind: the guard's two arms read `ldarg.0; ldfld` in a static
    method too, where `ldarg.0` is the first parameter. A static helper whose lambda or expression tree reads the
    class's field through a parameter (`probe._client`) was woven to read that field off an `int`, which ILVerify
    rejects. Both arms now read a field off `this` only in an instance method (S51, S52, M10). S52's label still shows
    the value: it reads `probe._name` through the closure's `probe`.
  - The weaver suite ran in a Linux container (`results/weaver-suite-release.txt`). That run found an older SDK 8
    fact with no check for SDK 8, which now skips where the SDK is missing, and passes on the final tree.
  - Changed: the status, 6.1, 6.3 (results), section 9 (answered), this log.
