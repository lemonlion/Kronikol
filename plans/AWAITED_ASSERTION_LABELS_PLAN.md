# Awaited assertion notes plan (#212)

**Status:** written 2026-10-27 at 5.12.0 (`1ee6373a`) by kronikol-c6, at the owner's ask to analyse #212 critically
and plan a fix for the underlying problem, which may differ from the one the issue proposes. **Green-lit
2026-10-28** ("can you implement the plan in full"), every question in section 9 at its recommendation: Q1 a, Q2 a,
Q3 R1 then R2. Executing from 5.22.0 (`3d1817b2`). #211 is not green-lit, so this plan now goes first (section 8).

**Issue:** [#212](https://github.com/lemonlion/Kronikol/issues/212), "Assertion tracking: an assigned or returned
`await Should.ThrowAsync<T>(…)` is labelled with its source in Release, and not drawn at all in Debug", filed
2026-10-27 from a .NET monorepo's xUnit v2 suites (Kronikol 5.12.0, Shouldly 4.3.0, net10.0, macOS arm64). Eighteen
notes in six Release reports carried a statement's source in place of a sentence.

**Siblings:** [#211](https://github.com/lemonlion/Kronikol/issues/211), the same statement shape in the same
function: in Release the weave's own read of a captured variable throws `NullReferenceException`. Session e132cc3e
(kronikol-12) plans it in `plans/AWAITED_ASSERTION_VALUES_PLAN.md` (worktree `C:/Code/Kronikol-211`).
[#210](https://github.com/lemonlion/Kronikol/issues/210), assertion labels that misread a subject or an argument,
is planned by kronikol-be in `plans/ASSERTION_LABELS_PLAN.md` (worktree `C:/Code/Kronikol-210`). The split between
the three plans is agreed (section 8).

**Harness:** `plans/AWAITED_ASSERTION_LABELS_PLAN.harness/`, described in its `README.md`. `probe/` holds three probe
facts, each moved out of the test project after it ran. The first (`Probe212Tests.cs.txt`) builds about forty
statement shapes, weaves each, runs it in Debug and in Release and records what the weave did and which notes were
drawn. It ran two ways: through the suite's `TestAssemblyBuilder` (Roslyn 5.3.0) and through `dotnet build` with SDK
10.0.300 (Roslyn 5.6.0) on a plain library. The two agree on every shape both ran. The second
(`Probe212bTests.cs.txt`) adds fifteen shapes and runs five on SDKs 8, 9 and 10. The third (`Probe212cTests.cs.txt`)
builds Release with debug-plus, as the issue's xUnit v2 suites do (3.7). The `summary*.txt` files hold the tables,
`results-*.txt` every run, and `il/` the woven and unwoven IL of the shapes cited. `tl-probe/` and `unwrap-probe/`
hold two smaller measurements (3.2, 4.1 f).

Windows 11, .NET SDK 10.0.300. Line numbers are to `1ee6373a` (5.12.0). The weaver, the formatter, `Track` and their
tests are unchanged through 5.16.0 (`15a95d4c`). Three files 4.2 cites have moved since: at 5.16.0,
`HistoryRunBuilder.cs` L103 is L114 and `InteractionShape.cs` L217 and L284 are L237 and L310, and `ReportIndex.cs`,
which #190's releases keep changing, is cited by its method as well (`AllSteps`). #210's releases, 5.16.3
(`186d0ebc`) and 5.16.4 (`f33ed3e9`), then changed the formatter's head and moved the weaver's lines from L1737 on by
97; `harness/line-map-5.16.4.txt` maps every code citation to `f33ed3e9` and names the ranges changed inside (only
`Format`'s head, where F3 still holds). Wiki lines are on `origin/master` `ae3393f`, and `Assertion-Tracking.md` is
unchanged at `e10a398`.

---

## 1. Summary

The issue is right about both symptoms, and the probes reproduce its five rows exactly in all three builds, the
suite's, the SDK's and xUnit v2's debug-plus Release (`summary.txt`, `summary-212c.txt`, rows R212). It treats
them as one labelling problem of one statement shape. They are two problems, and neither is specific to Shouldly or
to `ThrowAsync`:

- **The Debug note is missing because the weave refuses the statement.** In a Debug build the compiler keeps the
  result of every `await` it goes on to use in a spill field, and pushes the field's owner at the point where the
  await resumes, before it reads the result. The weave recognises an awaited assertion only when the stack is empty
  where the result is read, so it leaves every such statement as written, and says so only at normal build
  verbosity, where `dotnet test` never shows it. This covers every awaited assertion whose result is used: assigned
  in any form, returned, discarded, given `ConfigureAwait`, or fed to another assertion. FluentAssertions' and TUnit's
  fail the same way.
- **The Release label is the statement's text because the formatter reads the statement's head by a fixed list.**
  The weave hands the runtime the whole statement. `AssertionExpressionFormatter.Format` strips `() => `, then
  `await `, then `var x = `, in that order, so `var e = await X` keeps its `await`. It never strips `return`, a
  declaration with a type, an assignment, a discard or a trailing `ConfigureAwait`. Any statement with such a head
  falls through to its own source.

The probes found more defects on the same path, none of them in the issue. The widest is not a label at all:

- **An assertion on an awaited value is never drawn, in Debug or in Release, and the build says nothing (F8).**
  `(await x).ShouldBe(y)`, `(await x).Should().Be(y)` and TUnit's `await Assert.That(await x).IsEqualTo(y)` put the
  assertion's call after the await's resumption, under a hidden sequence point that belongs to no statement the
  weave reads (S17).

Three more draw a note that disagrees with what the assertion did:

- **An assertion task that leaves its statement un-awaited is drawn ✓ when it is created.** Returned by a helper,
  stored for a later `await`, or returned by an expression-bodied test, of Shouldly, FluentAssertions or TUnit, it
  is drawn ✓ even when the assertion then fails (S15, S19).
- **An awaited assertion that feeds another assertion is drawn ✓ before the second one runs.** So
  `(await act.Should().ThrowAsync<T>()).Which.Message.Should().Be("x")` is ✓ while the test fails on the message
  (S16).
- **A Debug `return` of an assertion never records its pass.** The weave sends the statement's own jump to the
  method's return past the pass (S9b).

The underlying problem: the weave recognises an assertion by the shape of the IL one compiler setting writes for
one statement form, and nothing checks the result against the alternatives. Nothing compares a Debug weave with a
Release weave of the same source. Nothing checks that a note's outcome is the assertion's. Nothing covers an
assertion whose result is used, and nothing checks that every assertion in the code is either woven or listed.

| What Kronikol does | Measured | Effect |
|---|---|---|
| Starts an awaited assertion's wrap at the awaiter's load before `GetResult` (`AwaitedRange`, AssertionWeaver.cs L2606-2621), and refuses when the stack is not empty there (`PlanStack` L2538-2539) | Debug writes `ldarg.0` at the resume point before the awaiter's load, for every result it uses (`il/S2.Debug.unwoven.il.txt`) | No note in Debug for an assigned, returned, discarded, `ConfigureAwait`ed or compound awaited assertion, of any library (F1) |
| Lists a statement it leaves unwoven at normal importance only (`WeaveAssertionsTask.cs` L110-117); KRONIKOL001 warns only when nothing at all was woven (L122-128, L179) | `dotnet test` builds at minimal verbosity, and the terminal logger hides even a high-importance message (3.2) | The missing note comes with no word from the build (F2) |
| Reads the statement's head by three fixed prefixes in a fixed order (`AssertionExpressionFormatter.cs` L30-45) | 11 statement heads measured, 2 read | The label is the statement's source, capitalised (F3) |
| Wraps an assertion whose task the statement does not await as a synchronous statement | The pass is recorded when the task is made | A failing assertion is drawn ✓ (F4) |
| Records an awaited assertion's pass right after `GetResult` (`WrapAwaitedAssertion` L3255-3274) | The rest of the statement runs after the `try` | A failing trailing assertion is drawn ✓ (F5) |
| Retargets the statement's own branch to its natural end onto the `leave` after the pass (`WrapSingleAssertion` L3093-3113) | Debug's `return x;` ends in `br.s` to the method's single return | The pass of a Debug `return` of an assertion is never recorded (F6) |
| Reads a statement as the instructions under one visible sequence point (`FindAssertionStatements` L561-744), and skips one whose first `GetAwaiter` is not an assertion's (L717-721) | An await's resumption, and the rest of its statement, sit under a hidden sequence point (`il/S17a.Release.unwoven.il.txt`) | An assertion on an awaited value draws no note in either build, and is in no list (F8) |
| Has no fact for an assertion whose result is used, and no Debug and Release parity check | Nearest facts are all bare `await X;` | None of F1 to F6, or F8, was ever exercised (F7) |

The fix keeps the weave's design: an awaited assertion stays wrapped where its result is read, as #211's plan keeps it
too. The wrap starts at the await's real merge point, which is always stack-empty (226 of 226 measured, through the
suite's builder and SDKs 8 to 10), and ends at the statement's end. A statement is read through its awaits'
resumptions, so an assertion on an awaited value is wrapped the same way, from the last merge point. The formatter reads a statement head by one rule.
A trailing branch to the statement's end counts as completing it. A statement the weave still leaves unwoven is
listed in the build's output, which a CI log shows; a warning, which a terminal shows too, is the owner's call (Q2).
One patch release does that (section 5). The un-awaited assertion task of F4 needs a small helper in Kronikol's
core, which is new public surface, so it is a minor release of its own, and the owner's call (Q1).

---

## 2. What the issue says, checked

| The issue says | Checked | Verdict |
|---|---|---|
| Unassigned `await Should.ThrowAsync<T>(…)` reads "Should throw InvalidOperationException" in both configurations | R212 `Awaited_ThrowAsync`, both builds, both configurations | Right |
| Assigned, the Release note is the statement's source, capitalised | R212 `Assigned_ThrowAsync` Release: `✓ Await Should.ThrowAsync<InvalidOperationException>(async () => await _client.GetOrderAsync(request))` | Right. The `var exception = ` is gone because `Format` strips it; the `await` stays because `Format` strips `await` before it gets to the `var` (F3) |
| Returned by a helper, it reads "Return await Should.ThrowAsync…" | R212 `ThrowAsync_returned_by_a_helper` Release | Right: `return` is never stripped (F3) |
| In Debug the assertion isn't drawn at all, though the next one is | R212 Debug: unwoven, "line 27: the stack is not empty where the awaited result is read" (and line 56, the helper) | Right, but it is not a label problem: the weave refuses the statement (F1), and the build says so only at normal verbosity (F2) |
| The note ends in "…" | `kronikol query note` lists each note through `QueryWriter.OneLine(text, 90)` (`QueryCommand.Payloads.cs` L603) | The listing is cut at 90 characters, not the note: the note keeps its whole label (the issue's "104 B" is "✓ " and 100 characters). Not a defect |
| `NotThrowAsync` and synchronous `Throw` are right | R212 `NotThrowAsync`, `Throw` | Right for those statements. A synchronous `Throw` that is assigned reads right too (S9), but one that is returned does not (S9b, F3, F6) |
| "A test that goes on to check the exception has to assign it, so in our suites this is most `ThrowAsync` calls" | F1 and F3 cover every awaited assertion whose result is used, of Shouldly, FluentAssertions and TUnit | Understated: TUnit's `var v = await Assert.That(x).IsNotNull()` and FluentAssertions' `var a = await act.Should().ThrowAsync<T>()` fail the same way (S12a, S13). F8 reaches further: an assertion on an awaited value draws nothing in either build (S17) |
| Expected: every row reads "Should throw InvalidOperationException" in both configurations | — | The right expectation. Section 4 meets it, and holds every shape in the catalogue to it: the same notes in Debug and Release, each with the assertion's outcome |

The issue's `await Task.Yield()` before each `ThrowAsync`, to stay clear of #211, does not change anything measured
here: S1, S2 and S3 ran without it too (S14b, S14a, S14c), with the same notes and the same refusals, one line
earlier. S14d to S14o are #211's variants.

---

## 3. Findings

### 3.1 F1: in Debug, an awaited assertion whose result is used is never woven

An awaited assertion compiles to the call chain, `GetAwaiter()`, an `IsCompleted` test whose `brtrue` jumps to the
merge point, the suspension, and the resumption, which falls through to the same merge point. There the result is
read with `GetResult()`. The weave finds `GetResult` by scanning from the `brtrue`'s target (`FindGetResultInstruction`
L798-854). It then wraps the range `AwaitedRange` gives (L2606-2621), from the awaiter's load just before `GetResult`
to the `pop` or `nop` just after it.

A Debug build stores the result of every await it uses in a spill field, `<>s__N`, before copying it on. The store
needs its object first, so the merge point is the `ldarg.0`, not the awaiter's load. S2 Debug, `var exception = await
Should.ThrowAsync<InvalidOperationException>(async () => await _client.GetOrderAsync(request));`
(`il/S2.Debug.unwoven.il.txt`):

```
IL_00d9: brtrue.s IL_0120            // the merge point is the ldarg.0
IL_0120: ldarg.0                     // stack: [this]
IL_0121: ldloca.s V_4                // AwaitedRange's start
IL_0123: call TaskAwaiter`1::GetResult
IL_0128: stfld <>s__3                // then copied to <exception>5__2, and <>s__3 cleared
```

`PlanStack` reads the stack before the awaiter's load, finds `[this]`, and refuses: "the stack is not empty where the
awaited result is read" (L2538-2539). The statement runs as written, with no note. A `return await X` spills the
same way (S4: `stfld <>s__2`, then `stloc.1; leave`), and so do a discard (S7), an explicit type (S5), an existing
local (S6), a field (S6b), a `ConfigureAwait` (S8) and a member access on the result (S16). Only a bare `await X;`
writes `ldloca; GetResult; pop`, which is the shape every existing fact uses (F7).

A Release build reads the result with nothing under it on the stack. It stores it in an IL local (`ldloca;
GetResult; stloc`), even when the variable is hoisted because it is used after a later await (S11), or, in plain
Release, leaves it on the stack for the next statement when that statement reads it once (S2, S8, S18a, S18c, S21a:
`PlanStack`'s exit for S2 is `[InvalidOperationException]`, `results-release.txt` L134). Debug-plus, xUnit v2's
Release, stores it in a local there (3.10). So Release weaves every shape of this kind, and its
labels are F3's.

The same holds for every library whose assertions are awaited: FluentAssertions' `var a = await
act.Should().ThrowAsync<T>()` (S12a) and TUnit's `var v = await Assert.That(x).IsNotNull()` (S13) are unwoven in
Debug and woven in Release. Measured through the suite's builder (`summary.txt`, Pass 1); the SDK pass (Pass 2)
ran FluentAssertions as S16a only, and agreed.

The merge point itself is always stack-empty. The compiler empties the stack before a suspension, and both paths into
the merge point (the `brtrue` and the resumption) arrive with nothing on it. The weave's `brtrue` scan already finds
it (L816-835) and then discards it.

Measured by the second probe (`summary-212b.txt`, section E): 226 awaits in both configurations, 142 through the
suite's builder (Roslyn 5.3.0, every shape) and 84 through SDKs 8.0.421, 9.0.314 and 10.0.300 (Roslyn 4.11, 4.14 and
5.6; S2, S4, S7, S8 and S16a), and none has a value on the stack at its merge point.
In Debug, 46 of the 113 merge points are an `ldarg.0`: exactly the awaits whose result is used, assertion or not.
Every Release merge point is the awaiter's load.

### 3.2 F2: a statement left unwoven is said only at normal verbosity

`WeaveAssertionsTask` logs each statement the weave leaves as written at `MessageImportance.Normal` (L110-117), and
warns KRONIKOL001 only when the assembly has nothing woven at all (L122-128, `NothingInstrumented` L179). `dotnet test`
and `dotnet build` default to minimal verbosity, which shows neither. The reporter saw a note missing and nothing
else.

Raising the list's importance is not enough on its own. Measured on SDKs 8.0.421, 9.0.314 and 10.0.300, with the
`Message` and `Warning` tasks of a throwaway project (`harness/tl-probe/results.txt`):

| Logger | High importance | Critical message | Warning |
|---|---|---|---|
| Console logger at minimal verbosity (a build with no terminal: CI) | shown | shown | shown |
| Terminal logger at minimal or normal verbosity (the default in an interactive terminal since .NET 9) | hidden | hidden | shown |
| Terminal logger at detailed verbosity | shown | shown | shown |

So a high-importance list reaches a CI log, but not a developer running `dotnet test` in a terminal, as the issue's
transcript does. Only a warning reaches both (Q2). A task's warning fails a build only under
`MSBuildTreatWarningsAsErrors` or a `WarningsAsErrors` that names its code; `TreatWarningsAsErrors` alone leaves it a
warning, and `NoWarn` silences it (same three SDKs).

The wiki says the weave "detects the GetAwaiter()/GetResult() pattern and wraps the entire await expression
including the merge point where the failure manifests" (`Assertion-Tracking.md` L306-312), which F1 makes false for
most awaited statements in Debug.

### 3.3 F3: the label of a statement with a head is its source

The weave passes the statement's text, cut at its sequence point's columns, to `Track.AssertionPassed` (`ldstr`, then
`AssertionWeaver.cs` L3255-3274 for an awaited one). `Format` (`AssertionExpressionFormatter.cs` L25-45) strips a
leading `() => `, then a leading `await `, then a leading `var name = `, and hands the rest to the FluentAssertions,
Shouldly and TUnit readers. Each reader needs the assertion at the start of its text (`Should.`, `Assert.That(`), or
a subject with nothing in front of it. Measured in Release:

| Statement | Label |
|---|---|
| `await Should.ThrowAsync<T>(…)` | Should throw InvalidOperationException |
| `var e = Should.Throw<T>(…)` (S9) | Should throw InvalidOperationException |
| `var e = await Should.ThrowAsync<T>(…)` (S2, S3, S11) | Await Should.ThrowAsync<InvalidOperationException>(…) |
| `return await Should.ThrowAsync<T>(…)` (S4) | Return await Should.ThrowAsync<…>(…) |
| `InvalidOperationException e = await …` (S5) | InvalidOperationException e = await … |
| `e = await …` (S6), `_e = await …` (S6b) | E = await …, _e = await … |
| `_ = await …` (S7) | _ = await … |
| `var e = await Should.ThrowAsync<T>(…).ConfigureAwait(false)` (S8) | Await Should.ThrowAsync<…>(…).ConfigureAwait(false) |
| `return Should.Throw<T>(…)` (S9b), `return Should.ThrowAsync<T>(…)` (S15b) | Return Should.Throw<…>(…), Return Should.ThrowAsync<…>(…) |
| `var a = await act.Should().ThrowAsync<T>()` (S12a) | Await act should throw async <InvalidOperationException> |
| `var v = await Assert.That(x).IsNotNull()` (S13) | Await Assert.That(x).IsNotNull() |

With the head cut by hand, every Shouldly shape reads "Should throw InvalidOperationException". The one exception is
a trailing `ConfigureAwait`: at 5.12.0 `ParseMethodAndArgs` takes the last top-level bracket pair as the arguments,
so `Should.ThrowAsync<T>(…).ConfigureAwait(false)` would read "Should throw InvalidOperationException false". #210's
5.16.3 reads a chain link by link instead; either way the statement's tail needs the same reading as its head, and
4.1 d removes it before any reader sees it.

`Format` is the right place for the rule. It is the only reader of the text, a report written with a new core reads
the text any weave wrote, and `Track.That`'s caller-argument texts have no head, so they are untouched. The
statement's text as the weave cuts it stays as it is, which #210's argument values read (`DetectCapturedArguments`
matches arguments on the statement as written).

### 3.4 F4: an assertion that leaves its statement un-awaited is drawn ✓ when it is made

An assertion whose task is returned by a helper that is not `async` has no `GetAwaiter` in its statement. The weave
wraps it as a synchronous statement, so the `try` covers making the task, and the pass is recorded before the task
completes. S15 ran each helper twice, once where the assertion holds and once where it fails:

| Helper | Debug | Release |
|---|---|---|
| `Task<InvalidOperationException> Fails(OrderRequest r) => Should.ThrowAsync<…>(…)` (S15a) | ✓ in both runs; the failing run fails the test | same |
| `Task Ok(OrderRequest r) => Should.NotThrowAsync(…)` (S15c) | ✓ in both runs | same |
| The same two with a block body, `{ return Should.…(…); }` (S15b, S15d) | no note in either run (F6) | ✓ "Return Should.…" in both runs |
| FluentAssertions, `Task<ExceptionAssertions<T>> Fails(Func<Task> act) => act.Should().ThrowAsync<T>()` (S19a) | ✓ in both runs | same |
| TUnit, a helper returning `Assert.That(x).IsEqualTo(y)` without awaiting it (S19b) | ✓ in both runs | same |
| Shouldly, stored for a later await: `var t = Should.ThrowAsync<T>(…); await t;` (S19c) | ✓ in both runs | same |
| An expression-bodied test, `public Task Test() => Should.ThrowAsync<T>(…)` (S19d) | ✓ in both runs | same |

TUnit's case is drawn earlier still: its assertion is not a task, and TUnit runs it only when it is awaited, so the ✓
is drawn before the assertion has run at all (`results-212b-release.txt` L892).

A helper of this kind is the usual way to share an expected failure between tests. Two ways to draw it right:

- **Observe the task.** Pass the task through a Kronikol helper that records the assertion's outcome when the task
  completes, and hand back a task with the same outcome. This needs a new public member on `Track`. It cannot take
  TUnit's assertion, which is not a task (4.1 f).
- **Leave it unwoven, and say so** (F2's message). The passing case then loses the ✓ it has today.

Either is a decision for the owner (Q1). Neither #211's mechanism nor the merge-point wrap reaches F4, since nothing
is awaited in the statement.

### 3.5 F5: an awaited assertion that feeds another is drawn ✓ before the other runs

`(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("not found")` (S16a,
FluentAssertions) and `(await Should.ThrowAsync<InvalidOperationException>(…)).Message.ShouldBe("not found")` (S16b,
Shouldly) are one statement. The weave takes it as one awaited assertion, and `WrapAwaitedAssertion` records its pass
right after `GetResult` (L3255-3274). The trailing assertion runs after the `try`. Release, measured: one ✓ in both
runs, also when the message differs and the test fails on it. Debug: unwoven (F1). BreakfastProvider uses this shape
(`(await act.Should().ThrowAsync<ClickHouseServerException>()).Which.Message.Should().StartWith("Code: 60.")`, which
kronikol-be found for #210).

The fix is the synchronous wrap's own rule: the `try` covers the whole statement, and the pass is recorded at its
end. A failure of either part is caught and drawn ✗ with its message, one note for the statement. The label is
whatever #210's subject rule makes of it (today "(await act should throw async <InvalidOperationException>", and for
S16b a garble of the bracketed part, which #210 fixes).

One compound shape cannot be covered whole. With a second await after the assertion's, as in
`(await Should.ThrowAsync<T>(…)).Message.ShouldBe(await _client.GetExpectedMessageAsync())` (S20), the state machine's
dispatch jumps into the statement after the first merge point, to the second await's resumption (Debug `IL_0022 br`
to `IL_0194`, Release `IL_000f switch` to `IL_016e`, `il/S20.*.unwoven.il.txt`), and a `try` can be entered only at
its start. Release draws S20 ✓ today while its trailing assertion fails (`results-212b-release.txt` L1668-1672). It
is refused, with its reason (4.1 c).

### 3.6 F6: a Debug `return` of an assertion never records its pass

In Debug, `return Should.Throw<T>(…);` compiles to `call …; stloc.1; br.s IL_ret`, the jump to the method's single
return, which is the next instruction. The statement's range keeps the `br.s` (only a trailing `leave` or `ret` is
trimmed, L683-689). The branch counts as outbound (L663-667), and both retargets send it to the exit, which comes
after the pass: `branch.Operand = nullPathExitStart ?? leaveInstr` (L3093-3096) and `internalRetarget =
truePathExitStart ?? leaveInstr` (L3103-3113). The woven S9b, `il/S9b.Debug.woven.il.txt`:

```
IL_0058: stloc.1
IL_0059: br.s IL_006e                // past the pass
IL_005b..IL_0069: …AssertionPassedWithValues   // never reached
IL_006e: leave.s IL_008e
```

A pass is never drawn, and a failure still is (the catch is reached). Release returns without a branch, so it draws
the pass. The same happens to a block-bodied helper returning an assertion task (S15b, S15d Debug).

A `br` to the instruction right after the statement is how the statement completes, not a short cut. Trimmed like a
trailing `leave`, it stays after the `catch`, and the `try` ends where the statement's work ends.

### 3.7 F7: nothing covers an assertion whose result is used

Every fact with an awaited assertion awaits it bare: `ShouldlyWeaveTests.cs` L87-89 (and its rows L108, L124),
L498-501 (L529), `AssertionWeaverTests.cs` L1401, L1467, L2112, `Shouldly5PreviewTests.cs` L42,
`AssertionScopeTests.cs` L171-172. The formatter's facts strip `await` (`AssertionExpressionFormatterTests.cs`
L345-346) and `var` (L354-357) separately, never together, and no fact has a `return`, a typed declaration or an
assignment. No fact compares a Debug weave with a Release weave of the same source. No fact checks that each note's
outcome is the assertion's outcome in a run where the assertion fails.

#211's plan found a second gap that these facts need closed too: the suite builds "Release" with optimisation alone,
where an xUnit v2 project's `dotnet build -c Release` also passes `/debug+` ("debug-plus"), which changes where a
closure is kept (`AWAITED_ASSERTION_VALUES_PLAN.md` 3.7, its harness `results/debugplus-probe.txt`). Only xUnit v2
does: xunit.core 2.9.3's props set `DebugSymbols` unconditionally, while xUnit v3, TUnit, MSTest, NUnit and a plain
library build Release with `/debug-`, as the suite does (#211's harness, `frameworks/`). The issue's suites are
xUnit v2. Neither of this plan's first two probes built debug-plus (the SDK pass builds a plain library); a third
pass did (`summary-212c.txt`, 3.10). The facts of section 6 are built with each framework's flags, through #211's
builder mode (its 4.1 h).

### 3.8 F8: an assertion on an awaited value is never woven, and nothing says so

The weave reads a statement as the instructions under one visible sequence point (`FindAssertionStatements`,
L561-744). The compiler puts an await's resumption, and everything its statement does after it, under a hidden
sequence point. So in `(await _client.GetNameAsync()).ShouldBe("order")` (S17a) the statement the weave reads ends at
the awaiter's store, and the `ShouldBe` call after `GetResult` belongs to no statement. In
`il/S17a.Release.unwoven.il.txt` (its failing method, line 50, `ShouldBe("other")`), the visible point ends at the
awaiter's store (`IL_0085 stloc.s V_4`), and everything after it sits under the hidden one: the `IsCompleted` test,
the `brtrue` (IL_008e), the suspension, `GetResult` (IL_00d0) and `ShouldBe` (IL_00db). A statement the weave never
finds is in no unwoven list either. Measured in Debug and Release through the suite's builder, and in SDK 10's plain
and debug-plus Release: no note in the passing run or the failing run (`results-212b-*.txt`, `summary-212c.txt`).

The same holds for `(await x).Should().Be(y)` (S17b) and TUnit's `await Assert.That(await x).IsEqualTo(y)` (S17c).
When the assertion's first call comes before the await, as in `x.Should().Be(await y)`, the visible statement holds
both, and the weave skips it on purpose (L717-721, "Argument-await: skip"), again with no message:
`IsGetAwaiterOnAssertionResult` (L753-791) reads only the statement's first `GetAwaiter`. The wiki says "The weaver
fully supports assertions in async methods" (`Assertion-Tracking.md` L310).

The same check skips a suite's own async Shouldly assertion, even awaited bare (`await _client.ShouldBeKnownAsync(id)`,
S24). The weave counts a method on the suite's own `[ShouldlyMethods]` type as an assertion (`IsShouldlyMethod`,
L887-891), but `IsGetAwaiterOnAssertionResult` asks `IsAssertionLibraryType` (L784), which reads library namespaces
only (L856-863), so the statement takes the argument-await skip (L717-721). kronikol-be found this in the code for
#210, and the third pass measured it: no note and no listing in Debug, Release or debug-plus, bare (S24a) or with
`ConfigureAwait(false)` (S24c), `summary-212c.txt` section G. The only fact for such an assertion is synchronous
(`ShouldlyWeaveTests.cs` L293-301).

Unlike F1, this loses the note in Release too, and it is the commoner shape. A text search of BreakfastProvider's
tests (`origin/main` `640c190`) finds 27 statements of it, such as `(await reader.ReadAsync()).Should().BeTrue();`,
against one of F5's compound shape (`InMemoryClickHouseServerEndToEndTests.cs` L100) and one of the issue's returned
shape (`ClickHouseConformanceTests.cs` L206), among about 3,400 assertion statements. Neither the issue nor its
expected table names it. The returned one reads:

```csharp
return (await act.Should().ThrowAsync<ClickHouseServerException>()).Which.Message;
```

### 3.9 Found on the way, not in this plan

- **A closure's field read off `this`** (S9 Release on the suite's builder: `IL_004e ldfld
  <>c__DisplayClass1_0::request` with the test object on the stack, ILVerify StackUnexpected,
  `il/S9.Release.woven.il.txt`). It is #211's F8 (D-A), and the evidence is cited there.
- **Bracketed subjects and the FluentAssertions split** (S12b and S16 labels): #210 (kronikol-be).
- **#211's repro does not throw through `TestAssemblyBuilder.BuildWithSdk`**, while #211's own xUnit v2 project throws
  every run: recorded in #211's F7, which reconciles it first.
- **Shouldly 4.3.0 has no `NotThrowAsync<T>`** (the probe's S10b does not compile), so no shape of it is needed.

### 3.10 Debug-plus, xUnit v2's Release

A third pass built the shapes with `dotnet build -c Release -p:DebugSymbols=true`, which gives csc
`/debug+ /debug:portable /optimize+` as xUnit v2's props do, beside plain `-c Release` (`/debug-`), on SDK 10.0.300
(`summary-212c.txt`; each build's switches read from its own output). It changes no weave decision, note, label or
merge point: the same statements are woven and refused, the issue's five rows read as in the other builds, and every
merge point is the awaiter's load, stack-empty (91 awaits in each build, none an `ldarg.0`).

What moves is where the result is kept. Debug-plus stores it in a local right after `GetResult`, where plain Release
hands it on the stack to the next instruction, often the next statement's first (27 awaits, S2, S8, S18a and S21a
among them). So the stack is empty at the statement's end in the build the issue's suites compile, and not in plain
Release, where 4.1 a's exit spill carries the result. In five shapes (S9, S9b, S15a, S15b and R212's `Throw`), plain
Release also carries a closure on the stack into the assertion statement, and the woven IL fails ILVerify where
debug-plus's does not: #211's D-A (3.9), which #211 fixes. S24 is skipped the same way in all three builds
(`summary-212c.txt`, section G).

---

## 4. Design

### 4.1 The rule

Every assertion statement draws one note per run, whose outcome is the assertion's. The same source draws the same
notes in a Debug build and a Release build.

a. **The awaited wrap starts at the merge point (F1).** `FindGetResultInstruction` returns the `brtrue`'s target with
   `GetResult`. `AwaitedRange` starts there, so the instructions between the merge point and `GetResult` (Debug's
   `ldarg.0`, the awaiter's load) are inside the `try`. `PlanStack` requires the stack to be empty at the merge point,
   as it is at all 226 measured (3.1), instead of at the awaiter's load. A merge point that is not stack-empty is
   refused, with its reason (b). So is an awaited assertion whose merge point the scan cannot find: today
   `isAwaited` then stays false (L713-715) and the statement is woven as a synchronous one, whose pass would come
   before the await (F4's fault); no measured shape reaches it, and a fact with a hand-made body does. For a,
   `WrapAwaitedAssertion` changes only its `AwaitedRange` call (L3147), and the retarget of the `brtrue` (L3225-3244) then matches the branch. Whatever the
   range leaves on the stack crosses the `leave` in the exit spill (L3295-3303), as today: with c, that is nothing in
   Debug or in debug-plus, and in plain Release the result, when the next statement reads it off the stack (S2,
   S8, S18a, S18c, S21a; 3.10).
   #211's carried reads and protected region go between `tryStart` and the awaiter's load, as the arrays do today,
   or in front of `tryStart` (section 8).

b. **A statement left unwoven is said in the build's output (F2).** The list of unwoven statements is logged at high
   importance, with the file, the line and the reason, which the console logger shows at minimal verbosity (a CI
   log). After a, it should be empty for every shape in the catalogue (6.1). The terminal logger hides it (3.2), so a
   warning with a code of its own, which a terminal shows, is recommended for R2 (Q2).

c. **The awaited wrap ends at the statement's end (F5).** The `try` covers the statement from the merge point to its
   last instruction before the next visible sequence point, or before the end of the protected region the statement
   starts in, whichever comes first, trimmed as the synchronous wrap trims, and the pass is recorded there. The
   region's end matters when the assertion is the method's last statement: the next visible sequence point, the
   closing brace, then lies past the compiler's own `catch` around the body, and a range read to it takes in the
   handler (S4, S7 and S16a on every SDK, S18a, S21b and S23 through the suite's builder: `summary-212b.txt`, table
   D). The part-way return and
   enters-part-way checks of `PlanStack` (L2521, L2527), which skip an awaited statement today, apply to that range.
   With the cut at the region's end, the only shape they refuse is S20, whose second await's resumption the dispatch
   jumps into (3.5).

d. **A statement head is read by one rule (F3).** In `Format`, after #210's two first steps (comments dropped, a
   lambda head and block body read, 5.16.3) and in place of today's `await` and `var` strips, before anything else
   reads the text,
   outside literals and brackets, a head is removed again and again until none is left:
   - `return ` and `await `;
   - a declaration or an assignment: `var name = `, `using var name = `, `Type name = ` (a type with dots, generic
     arguments, `?` or `[]`), `name = ` and `target.member = ` / `target[index] = `, and the discard `_ = `. A plain
     `=` only, never `==`, `=>`, `<=`, `>=`, `!=` or a compound assignment;
   - a trailing top-level `.ConfigureAwait(…)` after an `await` is removed with it.

   The existing `() => ` strip stays. A statement that starts with `(` is left alone: the bracketed form is #210's
   (section 8). The weave joins a statement's lines with spaces, so a head split across lines reads the same (S21a,
   S21b).

   Two labels are beyond this rule's reach, and #210's R1 reads both (its prototype, section 8): a statement that
   passes an awaited assertion's result into another call (S23, `Console.WriteLine((await …).Message)`), which keeps
   its whole source today and reads "Should throw InvalidOperationException" there, and TUnit's lambda subject (S18a),
   whose label today is its source, "Await Assert.That(() => _client.GetOrderAsync(r)).Throws<InvalidOperationException>()",
   which this rule cuts to the lambda read as code, "() => _client get order async(r) throws
   <InvalidOperationException>", and which #210 reads as its body, "Client get order async(r) throws
   <InvalidOperationException>".

e. **A trailing jump to the statement's end completes it (F6).** An unconditional branch that is the statement's last
   instruction and targets the instruction after it is trimmed like a trailing `leave` (L683-689), so it stays after
   the `catch`. Any other branch from inside the statement to its natural end is sent to the pass's first
   instruction, not past it.

f. **An assertion task the statement does not await is observed, or left unwoven (F4, Q1).** Recommended: a helper on
   `Track`, `AssertionTask(Task task, string expression, string? callerFilePath, int callerLineNumber)` and a
   `Task<T>` overload. It returns `task.ContinueWith(record, ExecuteSynchronously).Unwrap()`, so the task handed back
   ends exactly as the assertion's did, with the same exception object, and the note is recorded before anything
   awaiting it resumes. The weave puts the call right after the assertion's own call. The statement keeps its `try`, so a
   throw while the task is made (an argument the assertion rejects) is still drawn ✗, but it records no pass of its
   own: the helper records the outcome when the task ends. The helper never throws into the task. Measured on .NET 10
   (`harness/unwrap-probe/`): the awaiter of a failing task catches the same exception object, after the note is
   recorded; a passing task's result is the same object; a task already complete gives back one already complete; a
   cancelled one stays cancelled.

   A TUnit assertion the statement does not await (S19b) is not a task, and runs only when awaited, so the helper
   cannot take it. The weave leaves it unwoven, with its reason (b), in R1 whatever the answer to Q1: the ✓ it draws
   today comes before the assertion has run.

   The weave calls the helper only when the project's Kronikol reference is at least the release that adds it, the
   gate it already applies to `Track.AssertionPassed` at 2.30.7 (L486-496): the referenced assembly's version, which
   is the package's (`Directory.Build.props`). A reference the weave had to add itself (version 0.0.0.0, L477-484)
   keeps today's wrap, and so does an older core.

g. **An assertion after an await is read with its statement (F8).** `FindAssertionStatements` joins to each
   statement the instructions under the hidden sequence point after each of its awaits' merge points, up to the next
   visible sequence point or the end of the protected region (c), and looks for the assertion there too. A statement
   whose asserting call (the last assertion-library call in it) comes after its last merge point is wrapped as an
   awaited assertion is: from that merge point, which is stack-empty, to the statement's end, with the pass recorded
   at the end. What comes before the merge point, the awaited call itself, stays outside the `try`, as it does for an
   awaited assertion today. `IsGetAwaiterOnAssertionResult` reads the statement's last await, not its first, so
   TUnit's `await Assert.That(await x).IsEqualTo(y)` (S17c) is an awaited assertion at its last await, and it asks
   of the call that made the task what the weave asks of an assertion (a library's type, or the suite's own
   `[ShouldlyMethods]` type, as `IsShouldlyMethod` does), so S24 is an awaited assertion too. A statement
   with an assertion's call, or an awaited assertion's `GetResult`, before a later await is refused with its reason,
   as S20 is. Its values are read as an awaited assertion's are: no argument capture (L734-737), and captured
   variables as #211 leaves them (section 8).

### 4.2 What changes in the report

- Debug draws a note for every awaited assertion whose result is used, where it drew none.
- Labels of statements with a head read as the bare assertion does: the issue's assigned and returned rows read
  "Should throw InvalidOperationException" in Debug and in Release, as its other `ThrowAsync` and `Throw` rows do,
  and its `NotThrowAsync` row keeps "Should not throw".
- A compound awaited statement is drawn ✗ when its trailing assertion fails, where it was drawn ✓.
- A Debug `return` of an assertion draws its pass.
- With f: an un-awaited assertion task is drawn when it completes, with its outcome.
- An assertion on an awaited value draws a note in Debug and in Release, where it drew none (F8, g).
- A statement with an await after its assertion's (S20) is listed unwoven, where Release drew ✓ even when it failed.
- A TUnit assertion a helper returns without awaiting it (S19b) is listed unwoven, where it drew ✓ before it ran.

Every one of these is a fix. All but f's ship in R1, a patch, with the changes named in the changelog's Changed
section; f's ships in R2, a minor for its new member (section 5).
None changes a passing test's outcome. The build's output gains one line per statement still left unwoven.

Nothing compares assertion notes across runs, so no history verdict moves. A note is a diagram marker (`Track.cs`
L372), which history's fingerprints and call counts leave out (`InteractionShape.cs` L217; its dependency list too,
L284), and the statement's raw text is stored nowhere (`StepCollector.cs` L221-233 keeps the label). What does move:
a note's address (`s0/d0/n1`, numbered by position in `PayloadReader.cs` L251-283) and a sub-step's path (numbered
by `ReportIndex.AllSteps`, `ReportIndex.cs` L243-254 at `1ee6373a`) are positions, so they shift where Debug gains notes;
a Debug scenario with no calls gains a diagram to hold its notes; a committed Specifications markdown and a saved
search that names an old label read differently.

### 4.3 Options considered

- **Observe every awaited assertion's task with f's helper, not only the un-awaited ones (option E in #211's plan).**
  Rejected by both plans:
  - TUnit's assertions are not tasks (their `GetAwaiter` returns `TaskAwaiter<T>`), so the merge-point wrap stays in
    any case, and two mechanisms would do one job.
  - The failure's stack gains Kronikol frames.
  - Debug would read the values before the await.
  - It makes the patch a minor.

  For the un-awaited task (f) nothing else works, so there it is the recommendation.
- **Cut the label text in the weave** (pass only the assertion's expression). The weave knows the call but not its
  columns, a statement's subject can come before the call (FluentAssertions), and #210's argument capture matches the
  statement as written. `Format` sees the same text and fixes old weaves too. Rejected.
- **Spill Debug's `ldarg.0` into a local in front of the awaiter's load** (keep `AwaitedRange`, move the stack). It
  needs a store on both paths into the merge point, which is what starting at the merge point gives for free.
  Rejected.
- **Wrap S20 from its last merge point** (g's rule, without the refusal). Its first assertion would run before the
  `try`, so a `ThrowAsync` that does not throw would draw nothing, and a passing one would be drawn by its trailing
  assertion alone. Refusing it says so in the build. Rejected.
- **Leave F4 unwoven with the message of b** (patch only). This is the fallback if the owner declines Q1. It draws
  nothing where a passing helper draws ✓ today.

### 4.4 What does not change

- The text the weave stores, its argument capture and its variable values (#210 and #211 own those).
- `Format`'s signature, and every label of a statement without a head.
- A bare `await X;` is woven exactly as today (its merge point is the awaiter's load).
- Kronikol4J: it ports neither the weaver nor the formatter (Kronikol4J `CHANGELOG.md` L111-120,
  `docs/REMAINING_PARITY.md` L1633-1647, at `origin/main` `d7ad2b0f`), so nothing goes in the divergence ledger.

---

## 5. Releases

#211's patch was to go first (section 8). The owner green-lit this plan while #211 waits, so this plan goes first and
#211 rebases onto it.

- **R1, a patch:** 4.1 a to e and g, and f's refusal of a TUnit assertion returned un-awaited, with the facts of
  section 6, and the builder's debug-plus mode (#211's 4.1 h) that those facts need. Its number is claimed at push.
- **R2, a minor, only if the owner takes Q1 a or Q2 a:** 4.1 f, the `Track.AssertionTask` helper and the weave's call
  to it, and the `KRONIKOL003` warning. A new public member and a new warning code, so a minor even though both close
  defects. If the owner prefers one release, R1 and R2 ship together as a minor (Q3).

---

## 6. Tests (TDD: each written red against today's code, the same from 5.12.0 to 5.14.0, then made green)

### 6.1 Fixtures

- **The shape catalogue.** `AwaitedAssertionShapeTests` (new) builds each shape of the probes, from `S1` to `S24`,
  and the issue's five tests verbatim (`R212`). For each library: Shouldly, FluentAssertions, AwesomeAssertions, and
  TUnit through the suite's real TUnit.Assertions. In Debug, in Release, and in Release with debug-plus (xUnit v2's,
  #211's 4.1 h). Every awaited shape runs twice: once where the assertion holds, once where it fails. Each shape is
  its own source file under `Fixtures/`, so a new shape is one file. FluentAssertions 8 throws TUnit's
  `AssertionException` once TUnit.Assertions is loaded in the process, so a fact compares a failure with the unwoven
  build's, never with a fixed type. A fixture that MSBuild builds twice with only a property changed (debug-plus
  against plain Release) needs a project directory per build: with a shared `obj/`, the third pass's second build
  skipped `CoreCompile` and would have measured the first build's binary.
- **The un-awaited helpers** (S15a to S15d, S19a, S19c, S19d), passing and failing, for R2; S19b for R1's refusal.
- **`CrossSdkWeaverTests`** gains the issue's project and S2, S4, S7, S8, S16a and S17a, so SDKs 8, 9 and 10 build
  them as a consumer does. The second probe measured S2, S4, S7, S8 and S16a to agree on all three
  (`summary-212b.txt`); S17a and the issue's project have run on SDK 10 only (`summary.txt` Pass 2,
  `summary-212c.txt`), so these facts are their first run on 8 and 9.

### 6.2 Facts

For every shape in the catalogue, configuration and run. The un-awaited tasks (S15a to S15d, S19a, S19c, S19d) meet
facts 1 to 3 from R2 on, with Q1 a; in R1 they keep today's notes, pinned as such, and with Q1 b they are refused.

1. The weave leaves no statement unwoven (`WeaveResult.Unwoven` is empty), but those it refuses on purpose, S19b
   and S20 (and the un-awaited tasks with Q1 b), each listed with its reason.
2. Each assertion statement draws exactly one note.
3. The note's outcome is the assertion's: ✓ where it held, ✗ with its message where it failed.
4. The test's outcome is the unwoven build's: the same exception type and message, or none. Shouldly writes its
   message from the source line of the failing frame, and a failure's message is the one key history keeps across
   runs (`HistoryRunBuilder.cs` L103), so a weave that moved the line would show here.
5. The Debug notes equal the Release notes, label for label and outcome for outcome (parity).
6. ILVerify finds nothing in the woven assembly (`WovenIlVerifiesTests`' net).
7. Every assertion call in the original IL (the weave's `IsAssertionEntryPoint`, L871) is inside a woven statement
   or named in `WeaveResult.Unwoven`: nothing is skipped in silence (F8). The second probe checked this by hand
   (`results-212b-release.txt` L8-12 for S17a).

And these, each red on today's code:

- The issue's five rows read "Should throw InvalidOperationException" or "Should not throw" in both configurations,
  and `kronikol query note` lists two notes for `Assigned_ThrowAsync` in each.
- `Format` facts, one per head of 4.1 d and their combinations (`var e = await`, `return await`, `T e = await`,
  `e = await`, `this.e = await`, `_ = await`, `using var e = await`, a trailing `.ConfigureAwait(false)`), across the
  three libraries. Facts that the head reader leaves alone: `==`, `=>`, a compound assignment, a literal holding
  ` = `, a statement starting with `(`, and every existing formatter fact.
- S16 with the message differing: one ✗ note with the trailing assertion's message (F5).
- S9b and S15b in Debug: the pass is drawn (F6).
- S17a to S17c: one note each, ✓ in the passing run and ✗ in the failing one, in both configurations (F8, g).
- S20: listed unwoven with its reason, and no ✓ drawn in its failing run.
- S24a and S24c, a suite's own async `[ShouldlyMethods]` assertion: one note each, in all three builds (g).
- An awaited assertion whose merge point the scan cannot find (a hand-made method body) is listed unwoven, not
  woven as a synchronous statement (4.1 a).
- S4, S7, S16a, S18a, S21b and S23: the range ends before the compiler's handler, and each is woven (c).
- The build's output at minimal verbosity, through the console logger, names a statement the weave leaves unwoven
  (b), with a fixture that keeps one unwoven on purpose (a stack the weave cannot type). With Q2 a, the same build
  through the terminal logger (`-tl:on`) shows one `KRONIKOL003`, and `<NoWarn>KRONIKOL003</NoWarn>` removes it.
- R2: S15a to S15d draw ✓ when the assertion holds and ✗ when it fails, the test's exception is the same object as
  unwoven, and a project referencing an older Kronikol core weaves as today.

### 6.3 Mutations

Each must turn a fact red. If it does not, the fixture is fixed, not the mutation dropped:

| Mutation | Fact |
|---|---|
| `AwaitedRange` starts at the awaiter's load again | Debug notes (1, 2, 5) |
| The awaited `try` ends at `GetResult` again | S16 with the message differing |
| `Format` strips `await` before the declaration | `var e = await` label |
| No `return` head | `return await` and S9b labels |
| No `ConfigureAwait` tail | S8 label |
| The trailing `br` is not trimmed | S9b Debug pass |
| R1: the unwoven list at normal importance | the minimal-verbosity build fact (R1's; with Q2 a, R2's warning fact takes its place) |
| R2: `KRONIKOL003` not raised, or raised once per statement | the terminal-logger build fact |
| R2: the helper not called, or called on an older core | S15 failing run; the old-core fact |
| The hidden region after a merge point not joined to its statement | S17 notes (2, 7) |
| `IsGetAwaiterOnAssertionResult` reads the first await again | S17c's note (2) |
| `IsGetAwaiterOnAssertionResult` asks `IsAssertionLibraryType` again | S24's note (2, 7) |
| The cut at the protected region's end removed | S4, S7, S16a, S18a, S21b, S23 (1) |
| S20 not refused | S20's failing run (3) |

### 6.4 Checks

- The whole weaver suite, `tests/Kronikol.Tests` (the formatter's facts) and `release.slnf` built in Release for every
  target framework.
- The issue's project on the release's packages, in Debug and Release: five rows, each drawn, each a sentence. A
  prototype packed to a local feed is restored with `<WarningsAsErrors>$(WarningsAsErrors);NU1603</WarningsAsErrors>`.
  Without it, a version missing from the feed restores the next one up from nuget.org with only a warning, so the
  check would quietly run the published weave. #211's harness measured this: 5.12.0-proto211 restored 5.12.0.
- BreakfastProvider before and after, with a control run of 5.12.0: the notes per scenario compared, and each
  difference classified (new Debug notes for used results, labels from source to sentence, F5's ✓ turned ✗ where its
  trailing assertion failed). Any other difference is a defect.

---

## 7. Documentation and ledger

- **Wiki, `Assertion-Tracking.md`:**
  - "Async and Awaited Assertions" (L306-312) says what is woven: an awaited assertion whose result is used,
    returned or fed to another assertion, and an assertion on an awaited value (F8). A compound statement is one
    note, ✗ when either part fails. Its "fully supports assertions in async methods" (L310) gives way to the two
    shapes the weave refuses and names in the build: a second await after the assertion's in one statement (S20),
    and a TUnit assertion returned without being awaited.
  - "Shouldly" (L287-293) adds that `var e = await Should.ThrowAsync<T>(…)` reads "Should throw T".
  - The build message for a statement left unwoven goes where KRONIKOL001 is described, with what the terminal
    logger hides (3.2).
  - With R2, the un-awaited assertion task, and `KRONIKOL003` beside `KRONIKOL001` and `KRONIKOL002`.
- **CHANGELOG:** R1: Fixed for F1, F2 (the listing at high importance), F3, F5, F6 and F8, and the refusals of S19b,
  S20 and an awaited assertion with no merge point found, with the changes of 4.2 under Changed. R2: F4 under Fixed,
  `Track.AssertionTask` and `KRONIKOL003` under Added. Each version line says why it is a patch or a minor.
- **README:** nothing. **Kronikol4J:** nothing (4.4).

---

## 8. Coordination

- **#211 (session e132cc3e, kronikol-12, `AWAITED_ASSERTION_VALUES_PLAN.md`).** Agreed 2026-10-27:
  - Ownership: that plan owns where and when the values are read; this one owns the label text, the merge-point
    refusal, the statement's end (c) and the trailing branch (e). Both keep the merge-point wrap.
  - Lines on 5.12.0:
    - This plan: `FindGetResultInstruction` (798-854), `AwaitedRange` (2606-2621), `PlanStack`'s awaited branch
      (2521, 2527, 2538), the `AwaitedRange` call in `WrapAwaitedAssertion` (3147) and its pass placement
      (3255-3274), and `WrapSingleAssertion`'s retargets (3093-3113, trim at 683-689). Added in the review: g's
      `FindAssertionStatements` (561-744) and `IsGetAwaiterOnAssertionResult` (753-791), and b's listing in
      `WeaveAssertionsTask.cs` (110-117). None of these is in #211's patch.
    - #211: the arrays block (3159-3223), the store after the awaiter's store, a protected region, and
      `DetectCapturedVariables`' branches at 1737, 1745, 1799, 1807 and 1873.
    - #211's own section 8 lists 798-854, 2606-2621, 2538 and 3147 for this plan, from before c and e were added
      here; its 3.11 credits this plan with 3103-3113 and S9b. Its `prototype.patch` changes 1734-1870, 3135-3149
      (around the `AwaitedRange` call), 3159-3226 and 3374-3383, and none of this plan's other lines, so this
      plan's rebase onto it meets it at 3147 only.
  - Order: #211 goes first; this plan rebases. Changed 2026-10-28: the owner green-lit this plan while #211 waits,
    so this plan goes first (told kronikol-12 the same day). Its R1 adds #211's 4.1 h, the builder's debug-plus
    mode, as test infrastructure, which #211 then uses. Shapes whose captured variable #211's D-A reads wrongly in
    plain Release are kept out of this plan's comparisons by an `await Task.Yield()` before the assertion, as the
    issue's own tests do.
  - #211's parity facts check the assigned shape against its expected text, since Debug has no note before this
    plan; this plan tightens them to Debug and Release parity.
  - Added after the split (told kronikol-12 on 2026-10-27, who adds this plan's lines to #211's section 8): g's
    statements read their values after the merge point, as an awaited assertion's are, so #211's mechanism covers
    them; and #211's framework matrix (only xUnit v2 builds Release with debug-plus) decides which build each of
    this plan's facts uses. #211's 4.1 h now adds a debug-plus mode to the builder instead of replacing its Release.
- **#210 (kronikol-be, `ASSERTION_LABELS_PLAN.md`).** Agreed 2026-10-27:
  - Ownership: that plan owns the subject, the arguments, the FluentAssertions split at the first top-level
    `.Should().`, `nameof`, `DetectCapturedArguments` and `IsConstant`. This plan owns the head of `Format` (4.1 d),
    which runs before all of them and leaves a statement starting with `(` alone. Two labels this plan's rule does
    not reach are #210's to read, and its R1 prototype reads both (kronikol-be, 2026-10-27): TUnit's lambda subject
    as its body (S18a) and an awaited assertion inside another call's brackets as that assertion (S23). Its R1 also
    reads an assertion chain link by link, so `ParseMethodAndArgs` no longer pairs a chain's first call with its last
    call's arguments, and reads `async () =>` and block-bodied lambdas for `Track.That`; that edit sits just before
    this plan's head lines in `Format`, and whichever ships second rebases.
  - Order (#210's section 5): its R1, the formatter's reading, waits for nobody, and whichever of it and this plan
    ships second rebases. Its R2, the weave's values, was to go after #211 and this plan. Changed 2026-10-28: the
    owner green-lit #210 and kronikol-be is executing it (worktree `C:/Code/Kronikol-210r`, base 5.16.1). With this
    plan still waiting on the owner, #210 shipped R1 as 5.16.3 (`186d0ebc`) and R2 as 5.16.4 (`f33ed3e9`) the same
    day, and this plan rebases onto both. Measured against this plan's citations (`harness/line-map-5.16.4.txt`):
    `Format`'s head, L25-45, is L24-49, with #210's comment and lambda-head steps first and today's `await` and `var`
    strips after them, unchanged, so F3 holds and 4.1 d replaces those two strips. The weaver's lines up to L891 are
    where they were, and from L1737 on each is 97 lower, with no cited range changed inside. R2 also drops a line's
    `//` comment before `CutColumns` joins the lines, which the head rule's text then never holds.
- **#139 (kronikol-50):** `SplitPascalCase` lower-cases acronyms in labels as `ScenarioTitleResolver.Humanize` does in
  titles; this plan does not touch it.
- The version number is claimed by SendMessage at push.

---

## 9. Questions for the owner

**Q1. An assertion task that leaves its statement un-awaited (F4): returned by a helper, stored for a later await,
or returned by an expression-bodied test.** Today a failing one is drawn ✓. A TUnit assertion of this kind is not a
task, and R1 leaves it unwoven whatever the answer (4.1 f).
- a. **Recommended:** observe the task with a new public `Track.AssertionTask`, as R2, a minor. The note gets the
  assertion's real outcome, and an older core keeps today's weave.
- b. Leave such statements unwoven, with the message of 4.1 b, inside R1's patch. Nothing false is drawn, but a
  passing helper loses the ✓ it has today.
- c. Leave it as it is.

**Q2. How a statement left unwoven is reported (F2).** A high-importance message reaches a CI log, but the terminal
logger, the default in an interactive terminal since .NET 9, hides it (3.2).
- a. **Recommended:** both. The list at high importance in R1, which is no new surface. Then, in R2, a warning,
  `KRONIKOL003`, in `KRONIKOL002`'s shape (4.21.0): one warning a build, naming how many statements were left as
  written and the first one's file, line and reason. As with `KRONIKOL002`, the whole list then moves back to
  normal verbosity, since the warning carries the signal; R1's minimal-verbosity fact and its mutation give way to
  the warning's fact (6.2, 6.3). `<NoWarn>KRONIKOL003</NoWarn>` silences it. A new warning code is a minor, as
  `KRONIKOL002` was. It fails a build only where the project made task warnings errors
  (`MSBuildTreatWarningsAsErrors`, or `WarningsAsErrors` naming it).
- b. The list at high importance only, in R1. A CI log shows it; a developer at a terminal does not.

**Q3. Release shape.** R1 (patch) then R2 (minor), **recommended**: R2 carries Q1's helper and Q2's warning, or
whichever of them the owner takes. Or one minor carrying all of it.

---

## 10. Log

- 2026-10-27: plan written at 5.12.0. Probe of about forty shapes in Debug and Release, through the suite's builder
  and through `dotnet build` (SDK 10.0.300), `harness/probe/`. The split with #210 and #211 agreed by SendMessage. Not
  green-lit.
- 2026-10-27: 5.12.1 (`79e8d644`), 5.13.0 (`2b74a2e4`, #190 R0) and 5.14.0 (`8d7d1e1d`, #190 R1) released by other
  sessions. None touches the weaver, the formatter, `Track` or their tests, nor does the wiki up to `f3a62aa` touch
  `Assertion-Tracking.md`, so those line numbers hold. (The history files the review pass cites later did move, as
  the header says.)
- 2026-10-27, at the owner's ask to make the plan complete and accurate, with any deep dive that might change it:
  - Every citation checked by an independent pass, about 140; nine corrected (the S2 quote, the yield twins, S15b's
    label, the helpers' parameter, an IL file's name, Pass 2's coverage, the Kronikol4J lines, and the order with
    #210 and #211).
  - A second probe (`Probe212bTests.cs.txt`): fifteen new shapes, and SDKs 8, 9 and 10. Found F8. Widened F4 to
    FluentAssertions, TUnit, stored tasks and expression-bodied tests. S20 is refused. The range is cut at the
    protected region's end. Release often leaves the result on the stack. All 226 merge points are stack-empty.
  - The loggers measured (`tl-probe/`): the terminal logger hides a high-importance message, so Q2 now recommends a
    warning, in `KRONIKOL002`'s shape and as a minor.
  - 4.1 f's helper measured (`unwrap-probe/`), and its gate is the weave's existing version check.
  - Nothing compares assertion notes across runs (4.2).
  - A third pass built Release with debug-plus, as xUnit v2 does (3.10), after #211's audit found that only xUnit
    v2 builds Release that way. It also measured S24, which kronikol-be found in the code for #210.
- 2026-10-28: a final independent check of the edited plan, about 190 references and citations. Seventeen problems
  fixed: the SDK coverage stated wider than measured (S17, the merge-point census, table D, the cross-SDK facts), two
  history files that moved in 5.13.0 and 5.14.0, the S17a IL and S18a's label misquoted, BreakfastProvider's second
  `.Which` statement (a returned shape, not F5's), the R1 and R2 contents, the facts the un-awaited tasks meet only
  from R2, the list's verbosity once `KRONIKOL003` exists, three logger cells measured after the fact (run 4), the
  harness README, and one design gap: an awaited assertion whose merge point the scan cannot find is woven as a
  synchronous statement today, and 4.1 a now refuses it.
- 2026-10-28: 5.15.0 (`6bbc2ce1`), 5.15.1 (`5ac5022f`) and 5.16.0 (`15a95d4c`) released by other sessions, #190's
  plan complete. Of the cited files only `ReportIndex.cs` changed (its `AllSteps` block, L243 at `1ee6373a`, is L258
  at 5.16.0), so 4.2 names the method too; the weaver, the formatter, `Track` and their tests are unchanged.
- 2026-10-28: #210 green-lit and shipped by kronikol-be as 5.16.3 (`186d0ebc`, the formatter) and 5.16.4
  (`f33ed3e9`, the weave's constants), ahead of this plan, which rebases onto both. Every code citation mapped to
  `f33ed3e9` (`harness/line-map-5.16.4.txt`): F3 holds in the new `Format` head, the weaver's cited ranges moved
  without change, and 4.1 d now says where the head rule goes after #210's steps.
- 2026-10-28: green-lit by the owner ("can you implement the plan in full", "make sure to not interfere with any of
  the other sessions"), every question at its recommendation: Q1 a (`Track.AssertionTask` in R2), Q2 a (the list at
  high importance in R1, `KRONIKOL003` in R2), Q3 (R1 a patch, then R2 a minor). Executing from 5.22.0 (`3d1817b2`)
  in `C:/Code/Kronikol-212`, ahead of #211, which is not green-lit (section 8).
- 2026-10-28: R1 released as 5.26.1, a patch. What it ships differs from
  section 4 in these ways, each for the reason given:
  1. 4.1 e is wider. Any unconditional `br` that ends a statement is trimmed like a trailing `leave`, not only one to
     the next instruction: the end of an `if`'s branch jumps past its `else`, and that skipped the pass too (S25, a
     shape added for it). Its second sentence is not done. A null-conditional's short cut to the statement's end
     means the assertion never ran, so sending it to the pass would draw a ✓ for an assertion that did not run.
  2. One wrap. `WrapAwaitedAssertion` and `AwaitedRange` are gone: an awaited statement goes through
     `WrapSingleAssertion` from its merge point. With 4.1 c the awaited wrap needed every branch rule the synchronous
     one has (the outbound short cuts, the trailing trim, the exit spill), so a second copy of them was not kept.
     #211's arrays block is now `WrapSingleAssertion`'s (kronikol-12 told).
  3. Values of a statement R1 is the first to weave (F8, S17c's last await, S24's own type, any statement with two
     awaits) are the ones every build keeps in a state machine field: the async method's parameters, and what the
     statement reads after its last merge point. A local read only before the await is a `MoveNext` local in plain
     Release and gone after a suspension (#211's dead-local read; kronikol-12's warning that reading it would turn
     untracked statements into failing ones). S17f and S17g pin it. Statements woven before R1 keep their values as
     before; #211 owns those.
  4. "The merge point of an await in the statement could not be found" is a refusal only where the `GetAwaiter` is an
     await, with an `IsCompleted` test after it. A synchronous `.GetAwaiter().GetResult()` is no await and is woven as
     before.
  5. S15c and S15d hit #211's D-A in plain Release too (ILVerify's StackUnexpected), so they run in Debug and
     debug-plus only. 3.10 named S9, S9b, S15a, S15b and R212's `Throw`.
  6. A bracketed or awaited subject reads as code: `(await _client.GetNameAsync()) should be 'order'`, TUnit's
     `Await _client.GetNameAsync() is equal to 'order'`. Subjects and arguments are #210's reading (4.1 d); the shapes
     pin the labels as drawn, and a rule for them is #210's to take up.
  7. `WovenRun.Parse` rejoins a label the diagram wrapped (DiagramWidth's U+200B join markers). It cut a wrapped
     label at its first line, which hid the issue's long Release labels.
  8. The refusal of an un-awaited TUnit assertion (f) needs the chain's last assertion call to return something to
     await. As first written it took every `Assert.That` statement with no `GetAwaiter`, and the whole weaver suite
     found `Weave_TUnitAssertThat_InstrumentsAssertion` refused: a TUnit-namespaced assertion whose `IsEqualTo`
     returns nothing has run. Real TUnit's chains always return one, so S19b is still refused.
  9. Three facts pinned the skip of an await inside an assertion's argument, which g undoes, and now expect its
     note: `Weave_AsyncMethod_AwaitInsideAssertionArguments_DoesNotThrowInvalidProgram`, the mixed-statement fact
     (renamed `..._TracksEach`) and Shouldly's `An_await_inside_an_argument` shape. Each still runs the woven method.

  BreakfastProvider (6.4) changed nothing its xUnit in-memory lane draws: its awaited-value statements, and its
  compound and returned ones, are all in `BreakfastProvider.Tests.Unit`, which declares no `[assembly:
  TrackAssertions]`. So the before and after runs were compared on 5.23.0 and this release only, with no 5.12.0
  control: they differ in the values a run makes and in nothing else.

  Measured: `AwaitedAssertionShapeTests` holds 48 shapes and #212's five tests, in 134 shape builds (Debug, plain
  Release, debug-plus); on 5.22.0, 158 of its first 508 checks failed, and all 537 pass. The formatter's facts, 350.
  The cross-SDK fact, 60 builds on SDKs 8, 9 and 10. All 15 mutations of 6.3 and of the three rules added on the way
  (values limited, no merge point refused, TUnit un-awaited refused) turn a fact red. Checks: The whole weaver suite (1,025 facts; 28 skipped, the SDK 11 lanes), `tests/Kronikol.Tests` (11,521), and
  `release.slnf` built in Release for every target framework. #212's own project on this release's packages, from a
  local feed with NU1603 an error, in Debug and in Release: each of its five tests draws its notes, each a sentence,
  the same in both builds, and two for `Assigned_ThrowAsync`. BreakfastProvider's xUnit in-memory lane on 5.23.0 and
  on this release: 212 scenarios passed in both, with 896 notes in both, which differ only in the values a run makes
  (ids, names, times). That lane runs none of the shapes this release changes; BreakfastProvider's are in its unit
  tests, which are not woven.
- 2026-10-29: R1's commit is `9b2331d5`, with the wiki at `e994185`. R2 released as 5.27.0, a minor. What it ships
  differs from section 4 in these ways, each for the reason given:
  1. `Track.AssertionTaskWithValues`, with a `Task<T>` overload, beside 4.1 f's two members. 4.4 keeps a statement's
     values as they were, and an un-awaited statement showed them: FluentAssertions'
     `act.Should().ThrowAsync<T>().WithMessage(expected)` in a helper reads `expected`'s value. They are read where the
     statement made the task, where the synchronous wrap read them.
  2. Where the compiler wrote no reference to Kronikol, the gate reads the version of the Kronikol the build
     references. 4.1 f kept today's wrap for a reference the weave added itself (0.0.0.0), but the attribute is
     generated into the project, so an assembly that names no Kronikol type has no reference: the suite's fixtures,
     and #212's own project. The helper would never have been called there.
  3. A statement that waits for its assertion's task itself (a member of a task type takes it: `.Wait()`,
     `.GetAwaiter().GetResult()`, `.Result`) is not observed. Its catch draws the failure, and observing it too would
     draw two notes. S26 pins it. Only the statement's own instructions are read for that: plain Release leaves
     `var t = …; await t;`'s task on the stack for the next statement, whose await is the code after it.
  4. Found on the way, and fixed: a label read a trailing `.GetAwaiter().GetResult()`, as in S26's
     `Should throw InvalidOperationException get awaiter get result`. A statement that does not await reads without
     its wait tail now.
  5. S26 hits #211's D-A in plain Release too, so it runs in Debug and debug-plus.

  Measured: the catalogue holds 49 shapes and #212's five tests, 548 checks, all passing; the helper's facts, 8; the
  formatter's, 358; and the nine mutations of R2's rows in 6.3 and of the rules added on the way each turn a fact red.
  Checks: the whole weaver suite (1,039 facts; 28 skipped, the SDK 11 lanes), `tests/Kronikol.Tests` (11,537), and
  `release.slnf` built in Release for every target framework. A project on this release's packages, from a local feed
  with NU1603 an error, whose test assembly names no Kronikol type, so its only reference to Kronikol is the one the
  weave adds: in Debug and in Release its un-awaited helper is woven through `Track.AssertionTask`, and it draws ✗ with
  Shouldly's message where the assertion fails and ✓ where it holds. #212's own project is unchanged: its helper is
  `async`, so it awaits the assertion itself.
