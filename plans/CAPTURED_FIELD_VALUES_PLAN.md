# Captured field values plan (#213)

**Status:** EXECUTED as 5.32.1 (tag `v5.32.1`, 2026-11-07) at the owner's word of 2026-11-07, "can you implement the plan in full - make sure to not interfere with any of the other sessions", every question of section 9 at its recommendation; section 11 records the execution, measured again on 5.31.1 and released on 5.32.0. Written 2026-10-29 at 5.27.2 (`e4a1c82a`) by kronikol-13, at the owner's ask for "a detailed, well
researched plan to implement issue 213", and checked again the same day, at the owner's ask to make sure it is
complete and accurate, on its base: 5.27.10 (`2efa6535`), whose weaver is 5.27.4's (`41d0d38f`, #211's release;
5.27.5 to 5.27.10 do not touch the weaver or its tests). It was not green-lit then.
The defect is measured on the published 5.27.5 packages (the issue's tests built by the SDK, as the owner's comment of
the same day reports) and on 5.27.6's weaver (a catalogue of 24 shapes compiled the three ways a project builds them,
and the weaver suite). A prototype of the fix, re-made on 5.27.6 in place of #211's guards
(`prototype-5.27.6.patch`), is measured the same way (section 6). Line numbers are 5.27.6's, which are 5.27.4's to
5.27.10's; section 8 maps them to 5.27.2's, where the plan was first written.

**Issue:** [#213](https://github.com/lemonlion/Kronikol/issues/213), "Assertion tracking, Release build: an
assertion's woven values read a captured primary-constructor parameter from the async state machine instead of
`this`, recording the wrong object or crashing the test host with AccessViolationException", filed 2026-10-27 from a
.NET 10 monorepo's Reqnroll + xUnit v2 suites (Kronikol 5.12.0, Shouldly 4.3.0, Reqnroll 3.3.4, xunit 2.9.3, macOS 26.7.1
arm64). One suite's test host died on the same step in 2 of 3 Release runs and failed its scenario in the third.

**Harness:** `plans/CAPTURED_FIELD_VALUES_PLAN.harness/`, described in its `README.md`: `repro/` builds the issue's
tests as a consumer builds them, on the published packages; `probe/` is the shape catalogue run through the weaver
suite's own builder in the three compiles a project makes; `results/` holds every run and `il/` the woven IL the plan
cites.

## 1. Summary

**What happens.** When an assertion's lambda uses a primary constructor's parameter, the weave reads the parameter
off the wrong object to fill the note's values. The class keeps the parameter in a field, `<name>P`, and the weave
loads it with `ldarg.0; ldfld <name>P`. In an async method's `MoveNext`, `ldarg.0` is the state machine, and in a
closure's method it is the closure, so the load takes whatever lies at that field's offset of another object and
stores it in the note's `object[]`. On the published packages, 5.27.2 and again 5.27.5, the value read is never the
parameter: it is null, the test class's own instance, the state machine's own box, some other object, or, for a
parameter of a value type, a number that changes from run to run. Where the bytes at that offset are not a reference,
the test process dies with an AccessViolationException. That happens either while the value is stored
(`CastHelpers.StelemRef`, the issue's stack) or later, inside Kronikol (`Object.GetType` in `ChainValues.Keep`). An
access violation cannot be caught in .NET, by #211's protected region or anything else. The host exits with
0xC0000005, every other test's result is lost, no report is written, and xUnit v3's summary even says every test
passed.

**Which builds.** Release is no longer the only one. Since 5.26.1 (#212) a Debug build weaves an awaited assertion
whose result is used, so the issue's own second test now crashes the test process in Debug, on xUnit v2 and v3, on
every run, although it passes in Release. Which shape crashes in which build depends on the state machine's layout (a
class in Debug, a struct in Release), not on anything the source shows. Of seven shapes built from the issue's, the
five that use a primary constructor's parameter in an async method each crash in one build (three in Release, two in
Debug) and read null or a wrong object in the other; the two controls (a synchronous method, an ordinary field) pass
in both. The published 5.27.5 gives every one of those outcomes again, as the owner's comment of 2026-10-29 reports
for the issue's own suite.

**Why.** The walk of a lambda's body maps any field named `<name>…` to the variable `name`. That rule was written for
a state machine's hoisted locals, and the walk captures the field as one of the state machine's own without checking
which type it belongs to (F1). #211's guards (5.27.4) are in other arms and do not reach it. In a real suite, the only
field a lambda reaches that rule with is a primary constructor's parameter. Shouldly's assertions reach it through a
lambda among their arguments, and FluentAssertions', AwesomeAssertions' and TUnit's through a lambda after `.Should()`
or `Assert.That(…)`.

**Found on the way.**
- An awaited statement that names the parameter itself draws no value for it, where an ordinary field draws its own
  (F4).
- An expression tree's primary-constructor parameter is left out of the note, and so is a member read through it
  (F5).
- The weave does not check that the code read a field off `this` before reading it off `this` (F6). A field of
  another instance of the test class is read off this one, and, in Release, a field of another class is read off the
  test instance, which ILVerify rejects. The worst of F6, a closure's field read off the test class and a static
  method's parameter read off another, went in #211's 5.27.4 guards.
- A field of a generic base class, named in a synchronous method, is boxed as `!0`, which the runtime rejects with
  BadImageFormatException in every build (F7). 5.29.2 fixed it before this release, and the value is drawn
  (section 11).

**The fix: one patch.**
- One rule for every arm that captures a field: a field's value is read off the object the code under test reads it
  from, and only where the woven method holds that object.
- One check where the loads are emitted, so that no arm can emit a read of one type's field off another.

It replaces #211's two guards and keeps its `OwnField` and its carry.

**Measured** on 5.27.6's weaver, with the prototype re-made there (`prototype-5.27.6.patch`):
- The catalogue's 129 cases run 285 times on the prototype, and every woven run ends exactly as its unwoven run does.
  On 5.27.6, 21 of those cases crash the test process in all three rounds, and 18 more draw another note: a wrong
  value, or a value left out.
- The ILVerify errors the weave adds go from 51 to 0 across the catalogue, and from 23 to 0 across the prototype's
  scratch fixture.
- The weaver suite ends test for test as on 5.27.6: 1,432 passed and 28 skipped (section 6.2).

## 2. What the issue says, checked

| # | The issue says | Measured | Verdict |
|---|---|---|---|
| 1 | In an async method the weave loads the parameter's field with `ldarg.0`, which is the state machine | `ldarg.0; ldfld <handlers>P` in T2's woven `MoveNext`, `<third>P` in T1's, on 5.12.0 and 5.27.2 (`il/`, `results/il-values-arrays.txt`) | true (F1) |
| 2 | Where the state machine holds a reference there, the note records that object and the test passes | a probe whose label prints the value: null (T1, T7 in Release; T6 in Debug), the test class's own instance (T5 in Debug), another object (T2 in Debug); never the parameter | true; the object is null or a wrong one (3.2) |
| 3 | Where it holds anything else, `stelem.ref` type-checks garbage and the host dies | an AccessViolationException in `StelemRef` (T1, T2, T5, T7, each in one build); T6 gets past the store and dies in `Object.GetType` inside `ChainValues.Keep` | true, with a second crash site (3.2) |
| 4 | Release only: Debug does not weave this statement with values (#212) | true of 5.12.0, where Debug passes both tests; since 5.26.1 Debug weaves it, and T1 (the issue's own second test) and T7 crash in Debug on every run, on xUnit v2 and v3 | out of date (3.3) |
| 5 | Adding parameters, so that `<third>P` moves, did not make the second test crash | T5 (one parameter) and T6 (six) crash in Release, and T1 itself crashes in Debug | layout decides; the issue's tries found layouts that pass |
| 6 | The compiler's own code reads the field through the local it hoisted `<>4__this` into | true in Release (`ldloc.1`); Debug reads it off `<>4__this` | true |
| 7 | Suggestion: load the field through `<>4__this` (or the compiler's local), as the weave loads a captured local through `<>8__1` | right for this arm. `<>4__this` holds the instance in every build, the compiler's local only in Release (option D). Other arms make the same mistake (3.6) | right, not enough |
| 8 | #211 is the same statement shape inside async methods, so the two may share a fix | #211 reads locals that an await has ended; #213 reads the wrong object, in synchronous methods too. They share a function and #211's D-A guard, not a mechanism | in part (section 8) |
| 9 | Workaround: the pragma around the statement | it leaves the statement unwoven | true; it costs the note |
| 10 | The owner's comment of 2026-10-29: still reproduces on 5.27.5 (2 of 3 Release runs crashed, the third never reached the step), with the stack filed; #211's guard does not reach this path | F1's arm is unchanged in 5.27.4 (2014-2031), and #211's carry passes its variable over (`MoveNextLocal`, 1051, gives -1 for a `StateField` with no closure). T2's woven Release `MoveNext` on the published 5.27.5 still reads `ldarg.0; ldfld <handlers>P`, now inside #211's protected region (`il/xunit2-5.27.5-Release.il`), and T1 to T7 on 5.27.5 end exactly as on 5.27.2, under xUnit v2 and v3, 3 of 3 (`results/outcomes-5.27.5.txt`) | true (section 8) |

The issue's first test is a Reqnroll step. The weave treats a step binding's method like any other method of the
assembly, so the harness builds both tests as xUnit tests, on xUnit v2 (whose Release is the issue's, with debug-plus)
and on xUnit v3 (plain Release).

## 3. Findings

### 3.1 F1: a field a lambda reads, named `<name>…`, is taken for a field of the state machine

`DetectCapturedVariables` (`src/Kronikol.AssertionTracking/AssertionWeaver.cs:1755`) collects the variables an
assertion statement names, for the values of its note. When the statement makes a delegate from a lambda, it resolves
the lambda's method (`ResolveLambdaMethod`, 1994) and walks every `ldfld` in the lambda's body (2009-2013). Each field
goes first through `GetStateFieldOriginalName` (2461), which returns `name` for any field called `<name>…`
(2464-2468). The helper was written for the hoisted locals of a state machine (its summary says `<name>5__N`, 2458-2459),
and a name it returns is captured as a "State machine field (e.g., <localVar>5__1)" (2016-2031): the variable's
`StateField` is the lambda's own field reference, with no `ClosureField`, and nothing checks which type the field
belongs to. The scan starts after the assertion's entry point (`shouldIdx`, 1765-1785; the loop at 1796): for Shouldly,
at the call's first argument after the subject (`Should.ThrowAsync(() => …)`, `x.ShouldSatisfyAllConditions(h => …)`);
for FluentAssertions, AwesomeAssertions and TUnit's assertions, after `.Should()` or `Assert.That(…)`
(`items.Should().AllSatisfy(x => …)`). A lambda before the entry point is never walked: FluentAssertions'
`FluentActions.Awaiting(() => third.Run(…)).Should().ThrowAsync<T>()` captures nothing (P22, every build).

The values are built where the statement runs, and a variable with a `StateField` alone is read `ldarg.0; ldfld
StateField` (3250-3253). In an instance method of the class, `ldarg.0` is `this`, and the read is right. In a state
machine's `MoveNext`, `ldarg.0` is the state machine (a reference to it in Debug, where it is a class; a managed pointer
to it in Release, where it is a struct), so the weave reads a field of the test class off another object. The runtime
does not check the type of an `ldfld`'s object; it reads at the field's offset whatever lies there.

A primary constructor's parameter that the class body uses is kept in a field named `<name>P` (Roslyn's generated-name
kind `P`, C# 12 and later). A lambda reads it `ldarg.0; ldfld <name>P` when the lambda is a method of the class (it
captures `this` alone, as in the issue's second test), or `ldarg.0; ldfld <>4__this; ldfld <name>P` when it is a method
of a display class (it also captures a local, as in the issue's first test, which captures `cts`). The walk meets
`<name>P` in both, maps it to `name`, finds `name` in the statement's source and captures it.

Nothing else the compiler generates reaches this arm from a lambda's body in practice. A lambda never reads the fields
of the state machine it is made in: a local it captures moves to a display class, as a field named as the local is.
`<>4__this` and `<>8__1` give an empty name, which the helper turns into none (2468). An auto-property's backing field
(`<Name>k__BackingField`) is read directly only inside the property's own accessors. So in a real suite the arm's one
input is a primary constructor's parameter, and wherever it is reached in a `MoveNext` or in a closure's method
(below), its read is wrong.

F1 is not only an async defect. The weave weaves every method that holds an assertion (`Weave`, 154), a closure's
method among them: a lambda that captures a local as well as `this` is a method of a display class, whose `ldarg.0` is
the closure. An assertion there whose own lambda uses the parameter loads `ldarg.0; ldfld <name>P` off the closure. P23's
`new List<int> { 1 }.ForEach(x => { GC.KeepAlive(local); Should.Throw<T>(() => third.Run(…)…); })` records another
object of the closure's for `third` in every build, a synchronous method among them (ILVerify rejects the load), and
its async form, an async lambda, crashes both Release builds 3 of 3 (`results/case-5.27.6-p23.txt`). And the wrong
object can reach a label: P22b's FluentAssertions `new[] { 1 }.Should().AllSatisfy(x => GC.KeepAlive(third))` draws
`GC.KeepAlive('null')` in Release, and in Debug the state machine's own box
(``GC.KeepAlive('System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1+AsyncStateMachineBox`1[…]')``).

### 3.2 F2: what the load reads, and how it fails

The load takes the bytes at the parameter's field offset in the test class, read from the state machine. In Debug
the state machine is a class, reached by reference. In Release it is a struct, reached by a managed pointer: on the
stack until the method first suspends, then in a box. T2's state machine is never boxed, because `Service.Start`
throws before anything suspends, so its read is off the stack. What lies at that offset decides what happens:

- **A reference** (null, the state machine's `<>4__this`, its builder's task, a closure): the test goes on, and the
  value stored for the parameter is that object. Agent-built probes whose labels print the value read null (T1 and T7
  in Release, T6 in Debug), the test class's own instance (T5 in Debug) and another object (T2 in Debug). The
  catalogue's P6, whose async method uses `this` only inside the lambda, reads the test instance in Debug.
- **Anything else** (the state's `int`, part of an awaiter): the store into the `object[]` reads the "object"'s type to
  check it and faults, an AccessViolationException in `CastHelpers.StelemRef` (crash S). Or the store passes and
  Kronikol faults later, in `Object.GetType` inside `ChainValues.Keep`, called from `Track.ResolveVariableValues`
  (crash G, T6 in Release).

A crash ends the test process with exit code -1073741819 (0xC0000005): `dotnet test` exits 1, every other test of
the assembly loses its result, and no report is written (`results/crash-exit-codes.txt`; whole-assembly runs crash 3
of 3). xUnit v3 then prints "Total tests: 4, Passed: 4" while exiting 1.

On the published 5.27.2 packages, each case run 3 times alone (`results/summary.txt`, the same outcome every run), and
again on the published 5.27.5, with #211's release in them, every row the same (`results/outcomes-5.27.5.txt`):

| Shape | xUnit v2 Release | xUnit v2 Debug | xUnit v3 Release | xUnit v3 Debug |
|---|---|---|---|---|
| T1, the issue's second test verbatim | pass | crash S | pass | crash S |
| T2, the issue's first test as an xUnit test | crash S | pass | crash S | pass |
| T3, T1 made synchronous (control) | pass | pass | pass | pass |
| T4, T1 with an ordinary field (control) | pass | pass | pass | pass |
| T5, one parameter | crash S | pass | crash S | pass |
| T6, six parameters, the lambda reading the sixth | crash G | pass | crash G | pass |
| T7, three parameters and a `long` and an `int` field | pass | crash S | pass | crash S |

The 5.12.0 packages (the issue's) give T1 pass and T2 crash S in xUnit v2's Release, with the issue's IL exactly, and
pass both in Debug, which then did not weave the statement with values. In the weaver suite's own builder, on
5.27.6's weaver (`results/case-5.27.6.txt`: 120 cases, those of the shapes with a wrong load run three times, 258
runs), the same as on 5.27.2 (`results/case-5.27.2.txt`):
- P1 (T1) crashes in Debug;
- P6 and all eight layouts of P12 (one to six parameters, with and without value-type fields) crash in both Release
  compiles;
- P19, a parameter of a value type, never crashes: the load boxes the bytes at its offset, and the note prints a
  number that is not the parameter's and changes from run to run (`GC.KeepAlive('205793864')`, then `'2130909160'`,
  for an `int` that is 7 in Debug; `0`, or `1498830495744` for a `long` that is 7, in Release).

None of the issue's labels show the value: Shouldly's `Should throw X` leaves the lambda out, so the value read is
stored, resolved and then dropped. The harm is the crash, and the wrong object in the report's data.

### 3.3 F3: Debug since 5.26.1

#212's R1 (5.26.1) wraps an awaited assertion from its merge point in Debug as in Release, so `_exception = await
Should.ThrowAsync<T>(…)` has been woven with its values in Debug since then. In 5.12.0, Debug left it unwoven, which is
what the issue's "Release only" describes. On 5.27.2, and again on the published 5.27.5, the issue's own second test,
which passes in Release, crashes the test process in Debug on xUnit v2 and v3 (T1, P1), and T7 does the same. Which
build crashes is the layout's choice: T1 and T7 crash in Debug, while T2, T5, T6, P12 and P23's async closure crash in
Release.

### 3.4 F4: an awaited statement that names the parameter itself draws no value for it

The arm of F1 is reached from a lambda. A statement that names the parameter itself reads it off the instance, which
`MoveNext` reaches through `<>4__this` (`ldarg.0; ldfld <>4__this; ldfld <name>P` in Debug), or through the local into
which Release copies `this` (`ldloc.1; ldfld <first>P`, the issue's own IL). The chained arm (1894-1930) takes the second
field only when its name has no angle bracket (1901), and the standalone arm (2161-2163) the same, so no variable is
captured there. Nothing is read wrong, and for a statement that is not awaited nothing is lost either: the note's value
of an argument is the value the call received, copied off the stack (the #163 path), and P5 draws `Third should be
'Harness(third)'` in all three compiles.

An awaited statement has no such path: `ArgumentCapture` is null for one (838, since #212), so its values come from the
captured variables alone. There the parameter draws nothing where an ordinary field draws its value. P18's
`(await GetParameter()).ShouldBe(expected)` draws `(await GetParameter()) should be expected` in every build, and its
twin with a field, `(await GetField()).ShouldBe(_field)`, draws `... should be 'Harness(field)'`
(`results/case-5.27.6.txt`). A value left out, never a wrong one. The subject of a Shouldly assertion is never read for
values in any method (`other._id.ShouldBe(2)`, P17 a).

### 3.5 F5: an expression tree's primary constructor parameter is left out

The `ldtoken` arm (2092-2148), which reads the fields an expression tree names (`items.ShouldContain(x => x == expected)`),
skips every field with an angle bracket (2097). With `expected` a primary constructor's parameter, the note draws the
predicate without its value, in Debug and in Release, where an ordinary field's value is drawn (P8). A member read
through the parameter is left out with it: `first.Name == "first"` and `x != third.Name` stay as written (the scratch
fixture's `Pc`).

### 3.6 F6: the object a field is read off is never checked

The raw-name arms were written for a field of the test class. Whatever object the code read the field off, they load
it off `this` in an instance method (`ldarg.0`) or off `<>4__this` in a state machine. On 5.27.2 that read a closure's
field off the test class (#211's D-A: P14 in plain Release, whose woven method reads `IL_0036: ldarg.0; IL_0037: ldfld
P14/<>c__DisplayClass2_0::other`, and P13's async Release build), and, in a static method, a field of one parameter off
another: in `static void Second(int n, C c)`, `Should.Throw<T>(() => c._harness.Run(…)…)` loaded `ldarg.0; ldfld
C::_harness` with `ldarg.0` being `n`, and the weave's own NullReferenceException was recorded as the user's failure,
in every build (P15 `Second`; P16 is the async form, which ILVerify rejects). #211's 5.27.4 guards end those
(`method.IsStatic || !IsFieldAccessibleOnType(field, method.DeclaringType)` before the capture, 2074 and 2139; the
static half came from #211's review, with its shapes S51 and S52). On 5.27.6, P13 to P17 end as unwoven in every build
(`results/case-5.27.6.txt`). What the guards do not reach:

- **Another instance's field, off `this`.** In P13 (`new[] { 2 }.Should().Contain(x => x == other._id)`) and P17 b
  (`2.ShouldBe(other._id)`) the note is right ('2'), because the label's value comes from the access chain of `other`.
  But the values array also holds `this._id` (1, the wrong instance): P13's in its three synchronous builds and in
  Debug's async one, P17 b's in both async Release builds, where the standalone arm reads it. The field is the test
  class's own, so the guard lets it through (`results/values-5.27.6.txt`). No label uses that value today.
- **Another class's field, off the instance.** The standalone arm (2150-2208) takes `ldloc.N; ldfld F` for Release's
  copy of `this` without checking that local N holds it, or that `F` is the instance's: any field read off a local
  that the statement does not reach through a call or another field is read as `<>4__this`'s. In plain and debug-plus
  Release, `Receivers.Async_another_objects_field` of the scratch fixture (`other._id.Should().Be(other._id)`, with
  `other` of another class) reads `Other::_id` off the test instance, which ILVerify rejects ("Found ref 'Receivers',
  Expected ref 'Other'", `results/scratch-runs-5.27.6-Release.txt`). The note is right; the values array holds what
  lies at that offset of the test instance, and a field of a reference type there could crash as F1 does.

### 3.7 F7: a generic base class's field in a synchronous method is boxed as `!0`

Found by the prototype's runs (`results/scratch-runs-proto213.txt`). A synchronous test of a class derived from
`Base<T>` that names `Base<T>`'s field, in the statement (`Derived.Sync_generic_base_field`) or in a lambda
(`Derived.Sync_lambda_generic_base_field`), throws BadImageFormatException in all three compiles, on 5.27.2 and again
on 5.27.6 (`results/scratch-runs-5.27.6-*.txt`). The weave
reads the field through its reference on `Base<!0>` and boxes its type, `!0`, in a method that has no type parameter.
The async forms are right: Issue #55 resolves the field and its type through `<>4__this`'s type
(`ResolveGenericFieldType`, `ResolveFieldReference`, 2054-2057). The prototype resolves both through
`method.DeclaringType` in a synchronous method. Both tests then run and draw `Value should be '5'` and `Should not
throw`.

### 3.8 Since when

The walk of a lambda's fields and the arm of F1 came with `6e02bbb4` (2026-05-07, "feat: lambda variable capture +
multi-line expression fix", before the rename to Kronikol), and `GetStateFieldOriginalName` the day before
(`07d29259`). A primary constructor needs C# 12, the .NET 8 SDK's default for net8.0, so the defect has been reachable
in every release of the weave, by any project with a primary constructor whose async test, or whose lambda that also
captures a local, hands an assertion a lambda that uses one of its parameters. No fixture of the weaver suite has a class with a primary constructor (its fixtures' positional
records, such as `ShouldlyWeaveTests.cs:21`'s `Order(int Id, string Name)`, turn their parameters into properties, not
`<name>P` fields), and every weave of the suite goes through ILVerify
(`WovenIl`), which rejects a field of one type read off another: one such fixture would have found it.

### 3.9 Found on the way, not in this plan

- `IsKeptAcrossTheAwait` (1062-1071, #212) keeps an instance field read through `<>4__this` only when the statement
  reads it again after the merge point, though `<>4__this` holds the instance in every build. Since 5.27.4 the filter
  runs only where #211's carry cannot be placed (`CarryPoints`, 1016, is false: the method is not a state machine's
  `MoveNext`, or an await does not store its awaiter in a local right after `GetAwaiter()`). In the compiler's own
  shape every captured variable stays, and a field read through `<>4__this` is read at the merge point. Where the
  filter does run, such a value is left out, never wrong.
- `IsFieldAccessibleOnType`'s summary (2578-2582) says a field read off a type that lacks it "would produce
  BadImageFormatException". The runtime reads it without a check, as this issue shows. The release corrects the
  sentence, since it is the guard this plan's rule is built on.
- An async lambda's own state machine is never scanned (P7, P9): whatever such a lambda captures is left out of the
  values, in every build. In P10, the lambda's ordinary field `_field` is not captured beside a parameter and a local.
  Both are values left out, and a delegate's values never reach its label.
- `<Name>k__BackingField` takes F1's arm as `<name>P` does, but C# reads a backing field directly only inside the
  property's own accessors, so no test produces the load.
- xUnit v3 prints "Total tests: 4, Passed: 4" when the host has crashed, and exits 1 (3.2). That is xUnit's summary,
  not Kronikol's.

## 4. Design

### 4.1 The rule

**The weave reads a field's value off the object the code under test reads it from, and only where the woven method
holds that object.** One helper decides it for every arm that captures a field: the walk of a lambda's body, the
expression-tree arm, and the statement's chained and standalone arms.

a. **Which name.** A field gives a variable's name by the kind the compiler gave it. A plain name is itself;
   `<name>5__N` (a hoisted local) and `<name>P` (a primary constructor's parameter) are `name`; every other generated
   name (`<>4__this`, `<>8__1`, `<Name>k__BackingField`, `<>u__1`, `<>1__state`) belongs to no variable, and the field is
   passed over. `GetStateFieldOriginalName` gains the check of the kind; today it takes any `<name>…`.
b. **Which object the code reads it off.** In a lambda's body, a field is the instance's when the lambda reads it off
   its own `this` while being a method of the class (`ldarg.0; ldfld F` in a lambda that captures `this` alone), or off
   its display class's `<>4__this` (`ldarg.0; ldfld <>4__this; ldfld F`, through a parent display class too). In an
   expression tree, the field's `Expression.Field` takes `Expression.Constant(this, …)`: the call to
   `Expression.Constant` comes right before `ldtoken F`. In the statement, the field is read off `this` (`ldarg.0;
   ldfld F` in an instance method), off `<>4__this` (`ldarg.0; ldfld <>4__this; ldfld F`), or off the local into which
   Release copies `<>4__this` (`ldloc.N; ldfld F`, where local N is stored from `ldarg.0; ldfld <>4__this`). A field read
   off anything else is not the instance's. That covers a display class, a captured local, a parameter and a call's
   result. A display class's own field is read through the closure, as now (and as #211 carries it). A member of
   another object is read through the access chain of the variable it is read off (#210), never off `this`.
c. **Where the woven method holds the object.** The instance is `ldarg.0` in an instance method of the field's class or
   of a class derived from it (`method.HasThis` and `IsFieldAccessibleOnType(F, method.DeclaringType)`). In a state
   machine's `MoveNext`, and in a closure's method, it is `ldarg.0; ldfld <>4__this`, when `<>4__this` is of the field's
   class or a class derived from it (`IsFieldAccessibleOnType(F, <>4__this.FieldType)`). The woven method's own type
   comes first, so a closure's own field, read in the closure's method, is read off the closure. There `<>4__this` is named through #211's `OwnField` and
   `F` through `ResolveFieldReference`, for a generic class (Issue #55's rule). A field of the state machine itself (a
   hoisted local, a parameter of the async method) is read `ldarg.0; ldfld F`, as now. Anywhere else the value is left
   out: in a static method, `ldarg.0` is the first parameter, and a `MoveNext` without `<>4__this` holds no instance.
d. **Left out means not captured.** A value the rule cannot place is left out of the values, and the note draws the
   name as written, as it does for any variable the weave cannot read.
e. **One check where the loads are emitted.** Every value is loaded by one block of `WrapSingleAssertion` (3227-3263,
   #211's carried values first, 3229). Before it emits a value's loads, the block checks each field against the object
   it is loaded off: `StateField` against `method.DeclaringType` and its bases, and `ClosureField` against the type of
   `StateField`, or of the closure's local. A variable that fails is dropped from the values, and from #211's carry
   (`CarriedAfter`) before `CarryAcrossTheAwait` (3182) adds a field for it. A carried closure is checked as the local
   it was carried from. The arms decide what to read, and this check means no arm can emit a read of one type's field
   off another, whatever it gets wrong.

**As prototyped** (`prototype.patch` on 5.27.2, re-made on 5.27.6 as `prototype-5.27.6.patch`, in the worktree
`C:/Code/Kronikol-213p`, branch `proto/213-on-5275`, uncommitted), with these details the text above leaves open, each
measured:
- **A method of the class (b).** A lambda method is "a method of the class" when it is an instance method declared on
  a type whose name does not start with `<`. The compiler's display classes (`<>c__DisplayClass…`) and its static
  lambda holder (`<>c`) all do. Placement (c) then checks how that type relates to the field's.
- **A static field in an expression tree (b).** `Expression.Field(null, F)` puts `ldnull`, not a call to `Constant`,
  before the token, so the rule as written lost a static field's value (`x == 'a1'` became `x == Expected`). The
  prototype also accepts `ldnull` before a static field of the class, read as 5.27.6 reads it (an `ldfld` of a static
  field ignores its object). A static field of another class is left out, as 5.27.6's guard leaves it; 5.27.2 read it
  off `this`, which ILVerify rejects.
- **A generic base class (c).** The `this` branch resolves the field and its type through `method.DeclaringType`, as
  the `<>4__this` branch resolves them through `<>4__this`'s type, which fixes F7.
- **The statement's pair arm in a static method.** The statement's own `ldarg.0; ldfld F` reads `F` off the first
  parameter. The prototype requires `method.HasThis` there, so the parameter path captures that parameter, and its
  member is read through its access chain, not as a field of `this`. Measured: a static method's own `c._id` (P21,
  sync and async) and a local function that captures only a local, which Roslyn makes static with its closure, a
  struct, as `ldarg.0` (P20), draw the same notes on both weavers in every build.
- **The emitter's check (e).** It also requires `method.HasThis` for a load off `ldarg.0` that is not a field of a
  state machine, and a name counts as seen only once its value is placed, so a later arm can still place a name an
  earlier arm could not.
- **D-B.** `FieldOfInstance` names a `<>4__this` taken from the type's own fields through #211's `OwnField`; the one
  the chained arm passes is the compiler's own reference, already instantiated. On the 5.27.2 prototype, without
  `OwnField`, a generic class's async lambda kept 2 ILVerify errors per build; on 5.27.6 it has none.
- **#211's carry.** A variable the rule reads through `<>4__this` has no closure local (`ClosureLocalIndex` stays -1),
  so `MoveNextLocal` gives -1 and `CarryPoints` passes it over. It is read at the merge point, where `<>4__this` holds
  the instance in every build.

The prototype's helpers: `VariableNameOf` (a), `FieldOfInstance` (c, d), `IsThisProxy`, `IsStaticField`,
`OuterThisField`, `HoldsTheInstance` and `IsStloc` (b), `CanLoad` (e, applied in `WrapSingleAssertion` before the arrays
are sized).

### 4.2 What changes in the report

- An assertion whose lambda uses a primary constructor's parameter runs, in an async method and in a closure's
  method, in Debug and in Release. Where it crashed the test process or recorded a wrong object, the parameter's value
  is now its own: `GC.KeepAlive('Harness(third)')` where 5.27.6 drew `GC.KeepAlive('null')` (P22b), and a parameter of
  a value type draws its own number (`'7'`, P19).
- An awaited statement that names a primary constructor's parameter draws its value: `(await GetParameter()) should
  be 'Harness(expected)'` where 5.27.6 drew `... should be expected` (F4, P18).
- An expression tree's primary-constructor parameter draws its value, and so does a member read through it:
  `x == 'Harness(expected)'` for `x == expected` (P8), `'first' == "first"` for `first.Name == "first"` and
  `x != 'third'` for `x != third.Name` (the scratch fixture's `Pc`).
- In an expression tree inside a lambda the weave weaves as a method of its own, a closure's own field draws its
  value: `x == 'a1' && x == 'a1'` where 5.27.6 drew `x == local && x == 'a1'` (the scratch fixture's `InLambdas`).
  5.27.6's `ldtoken` arm takes a type that has `<>4__this` for a state machine and asks for a field of the instance;
  the rule reads the closure's field off the closure, which is the woven method's own `this`.
- A field the code reads off another object is no longer also read off the instance (P13, P17 b, and the scratch
  fixture's `Receivers` in Release). That changes the values array only; no label measured changes.
- A synchronous test that names a generic base class's field runs (F7). On 5.29.2 and later it runs already and
  draws its value, so the release changes nothing there (section 11).
- No other note changes: 90 of the catalogue's 129 cases draw the same notes on both weavers. Of the other 39, 21
  crashed on 5.27.6, and 18 are the changes above (P8, P18, P19, P22b). The weaver suite ends test for test as on
  5.27.6.

### 4.3 Options considered

| Option | What it does | Verdict |
|---|---|---|
| **A. The rule (4.1)** | one decision of name, object and place for every arm, and a check at the emitter | **recommended**: fixes F1, F4, F5, F6 and F7 together, replaces #211's two guards, and makes the class of defect impossible at the emitter |
| B. The issue's suggestion: read `<name>P` through `<>4__this` in F1's arm alone | the crash and the wrong object go | leaves F4, F5, the receiver defects of 3.6, F7, and the next generated name the helper maps |
| C. Leave every `<…>` field a lambda reads out of the values | the smallest change | loses values that are right today, in synchronous methods |
| D. Read through the local the compiler's own code reads `this` from (the issue's "or the local") | the same object as A in Release | Debug has no such local, and Release reuses locals as it likes; `<>4__this` holds the instance in every build |
| E. Verify each woven assembly with ILVerify at build time, and leave a method that fails unwoven | catches any future shape, of any kind | needs ILVerify inside the build task (a package, seconds per build), and a method that fails loses all its notes; not taken: 4.1 e gives the same guarantee for field loads at no cost |

### 4.4 What does not change

The formatter of labels, the `Track` API, the values the weave already reads right (locals, closures read through
their locals, parameters), #212's wrap from the merge point and #211's carry across an await.

### 4.5 Cost

None at run time: the same loads, with `ldfld <>4__this` added where it was missing. At weave time, a check per
captured field.

## 5. Releases

One patch: fixes only, nothing new for a consumer to call. It goes on top of #211's release, 5.27.4, and the releases
after it (section 8), and its number is the next free patch at push, claimed then. The changelog's Changed section
names the behaviour changes of 4.2.

## 6. Tests (TDD: each written red against the base, #211's release, then made green)

### 6.1 Fixtures

- **`CapturedFieldValueTests` (new)**, from the probe (`probe/Probe213Tests.cs`).
  - The catalogue's 24 shapes: the issue's two tests and their controls (P1 to P5), the parameter used only inside the
    lambda (P6), a generic class (P7), an expression tree (P8), async lambdas (P9, P11), a lambda that also reads a
    field and a local (P10), the eight P12 layouts, the receivers (P13 to P17), an awaited statement (P18), parameters
    of value types (P19), a static local function (P20), a static method's own field read (P21), FluentAssertions
    before and after `.Should()` (P22, P22b), and a closure's method, synchronous and async (P23).
  - Each shape is compiled as Debug, plain Release and debug-plus Release (`TestAssemblyBuilder.BuildConfiguration`).
  - Each fact checks ILVerify (`WovenIl`) before it runs a woven method. On the base, every shape that crashes fails
    ILVerify first, so a red run never reaches the read that kills the test process (#211 used the same order).
  - The values are compared by identity: each captured object has a `ToString()` of its own, and the facts read what
    the weave recorded through the probe's recorder.
- **From the prototype's scratch fixture** (`probe/Proto213ScratchTests.cs`): F7's two generic-base tests, a static
  field in an expression tree (4.1, as prototyped), a generic class's async lambda (D-B, through `OwnField`), another
  class's field read off a local in Release (`Receivers`, F6), and an expression tree in a woven lambda (`InLambdas`).
- **To add beyond the probe:** P13's form with a delegate (M2), and the emitter's unit facts (M3, M4, M12).
- **#211's catalogue** (its shapes up to S52, built in Debug, plain Release and debug-plus Release) is in the suite
  since 5.27.4, and holds on the prototype test for test (6.2).
- **`CrossSdkWeaverTests`** gains the issue's T1 and T2 as an SDK-built project, in Debug and Release, on SDKs 8, 9 and
  10 where installed. The run must end as the unwoven one does: exit code 0, every test passed, a report written. This
  is the end-to-end crash proof, in a child process, as `repro/` measures it.

### 6.2 Facts

| Fact | 5.27.6 | Prototype |
|---|---|---|
| `A_woven_shape_verifies` (ILVerify adds no error), 24 shapes in 3 compiles (93 builds) | 51 errors, all F1: P1, P2, P6, P10, P12, P19, P22b, P23 (`results/dive-5.27.6.txt`) | 0 (`results/dive-proto-5.27.6.txt`) |
| `A_woven_shape_runs_as_the_unwoven_one`, 129 cases | 21 cases crash the test process in all three rounds: P1 in Debug; P6, all eight P12 layouts and P23's async closure in both Release compiles | all 129 cases (285 runs) end as unwoven |
| `The_values_are_the_objects_the_source_names` | for the parameter: null, the test instance, the state machine's box or another object (P1, P2, P6, P10, P12, P22b, P23), a number that is not its own (P19); `this._id` beside `other._id` (P13, P17 b) | the object named, or left out where the rule cannot place it |
| `An_awaited_statement_draws_a_primary_constructor_parameter` (P18) | `... should be expected` | `... should be 'Harness(expected)'` |
| `An_expression_tree_draws_a_primary_constructor_parameter` (P8, `Pc`) | `x == expected`, `first.Name == "first"` | `x == 'Harness(expected)'`, `'first' == "first"` |
| `A_generic_base_field_in_a_synchronous_test_runs` (F7) | BadImageFormatException, every compile | runs: `Value should be '5'` |
| The scratch fixture's ILVerify count (19 methods, 3 compiles) | 23 errors (F1, F7, and `Receivers` in both Releases) | 0 |
| The issue's project, through `CrossSdkWeaverTests` | the host dies: T1 in Debug, T2 in Release | as unwoven |
| The weaver suite | 1,432 passed, 28 skipped for the .NET 11 SDK (`results/suite-5.27.6.trx`) | the same, test for test (`results/suite-proto-5.27.6.trx`) |

The two suite runs each also failed `WovenIlVerifiesTests.Every_woven_fixture_verifies`, alike: its scan flagged the
scratch fixture's direct call to the weaver while the fixture sat in the test project. The harness's copy now calls it
by reflection, as the probe does, and the test passes with both files in the project (`results/woven-il-scan-*.txt`).

### 6.3 Mutations

| # | Mutation | Caught by |
|---|---|---|
| M1 | F1's arm loads `<name>P` off `ldarg.0` again (5.27.6's read) | the values facts (P1's `third`). The emitter's check drops the bad load, so ILVerify stays clean, which is why the values facts exist |
| M2 | the lambda walk without the receiver check (4.1 b) | a fact to add: P13's form with a delegate, `Should.NotThrow(() => GC.KeepAlive(other._id))` with `other` of the test class; without the check `this._id` comes back (P13 itself is an expression tree, M5's) |
| M3 | `method.HasThis` dropped from the emitter's check | its unit fact: a static method's `ldarg.0` with a field of the method's own class is dropped |
| M4 | the emitter's check (4.1 e) dropped, together with M1 | the ILVerify facts. The check alone, with every arm right, changes nothing by design, so a unit fact hands the emitter a variable whose field is on another type and asserts that the variable is dropped |
| M5 | the `ldtoken` arm without the `Constant` check | P13's values fact |
| M6 | the `ldtoken` arm without the `ldnull` case for a static field | the static-field expression-tree fact (`x == 'a1'` turns into `x == Expected`) |
| M7 | the standalone arm without `HoldsTheInstance` | P17 b's values fact, and `Receivers`' ILVerify fact in Release |
| M8 | the `this` branch without generic resolution | F7's facts |
| M9 | `<>4__this` named on the open generic type (no `OwnField`) | the generic class's ILVerify fact |
| M10 | the builder compiles one way only | the crash rows move with the compile: P1 is red only in Debug, P12 and P23's async closure only in Release, so facts compiled one way miss half of them |
| M11 | `FieldOfInstance` tries `<>4__this` before the woven method's own type | `InLambdas`' label fact (`x == local` comes back) |
| M12 | a variable `CanLoad` drops is left in #211's carry | a unit fact, as M4's: the emitter is handed a carried variable whose field is on another type, and no carrier field may be added to the state machine. With every arm right nothing is dropped, so no shape shows it |

### 6.4 Checks

- The weaver suite on net8.0, net9.0 and net10.0, with ILVerify over every weave.
- `TargetsBuildTests`, and the core suite.
- The harness's `repro/` on packages packed from the release: xUnit v2 and v3, Debug and Release, 3 runs each. Every
  test must pass and a report must be written.
- The catalogue, the scratch fixture and the weaver suite on the release's weaver and on its base, through
  `probe/run-all.sh` (its `run-cases.sh` and `run-values.sh`), every case as unwoven and the suite test for test.
- `release.slnf` in Release, and the weaver suite in the Linux container.
- The new facts, run red on the base before the fix.

## 7. Documentation and ledger

- `CHANGELOG.md`: the patch's entry, with 4.2 under Changed.
- The wiki's `Assertion-Tracking.md` (line numbers at wiki `3f9722f`, 5.27.5's; 5.27.4's `e7fbbf2` changed other
  sections): "Lambda Closure Captures" (274-295) lists what a lambda's values are read from and says nothing of a
  primary constructor, or of the object a field is read off. The release adds a line to its list: a primary
  constructor's parameter is read off the instance, in an async method and in a closure's method too, and in an
  expression tree with its members (the version named); a field the code reads off another object is not read off the
  test class. "Async and Awaited Assertions" (323-374) gains that an awaited statement draws a primary constructor's
  parameter it names.
- Kronikol4J: a "Not mirrored" line in `docs/REMAINING_PARITY.md`, as every .NET release gets one, after 5.27.4's
  (line 2555 at `e715c23`): the port has no compile-time weave, and its AssertJ agent (`Tier2Recorder`) records each
  comparison's own values where it returns or throws, so it reads no field off another object. The report's format
  does not change.
- #213: a comment at the release, at the owner's word (section 9), and the issue closed by it.

## 8. Coordination

- **#211 (kronikol-12, `AWAITED_ASSERTION_VALUES_PLAN.md`) shipped as 5.27.4** (`41d0d38f`, 2026-10-29) and is
  closed; as agreed with kronikol-12 by message that day, this plan's release goes on top of it. Its changes to
  `DetectCapturedVariables` are narrow. D-A puts `if (method.IsStatic || !IsFieldAccessibleOnType(field,
  method.DeclaringType)) continue;` before the capture in the lambda branch's non-async raw arm (2074) and the `ldtoken`
  branch's non-async arm (2139). D-B adds `OwnField` (3547), which names the three `<>4__this` fields (2061, 2129, 2206)
  through the state machine instantiated over its own generic parameters. It does not touch F1's arm (2014-2031),
  `GetStateFieldOriginalName` (2461) or the standalone arm's checks. Outside that function it carries a local of
  `MoveNext` across the await (`CarryPoints`, 1016; `CarryAcrossTheAwait`), keeps `IsKeptAcrossTheAwait` (1062) for a
  statement the carry cannot be placed in (816-829), and builds an awaited statement's values in a protected region of
  their own (3275-3297).
- **What that leaves of F1 on the base.** `MoveNextLocal` (1051) gives -1 for F1's variable, which has a `StateField`
  and no closure, so the carry passes it over and it is read `ldarg.0; ldfld <name>P` at the merge point (3250-3253).
  The protected region catches a read that throws, and a fault near address zero, which .NET raises as a
  NullReferenceException; it cannot catch the access violation in `StelemRef`. The owner's comment of 2026-10-29 (the
  issue's suite on 5.27.5) and the harness's runs on 5.27.5 (3.2) both show the crash unchanged.
- **The prototype, re-made on 5.27.6** (`prototype-5.27.6.patch`, worktree `C:/Code/Kronikol-213p`, branch
  `proto/213-on-5275`): the rule in place of both D-A guards; `FieldOfInstance` naming a `<>4__this` from the type's
  fields through `OwnField`; `CanLoad` dropping a variable from the carry too. The weaver suite, #211's catalogue in
  it, ends test for test as on 5.27.6 (6.2). `prototype.patch` (5.27.2) stays in the harness as the first measurement.
- 5.27.5 (kronikol-2d, #97), 5.27.6 and 5.27.9 (kronikol-5d, #125), and 5.27.7, 5.27.8 and 5.27.10 (kronikol-2d, F26,
  F30 and #205) do not touch the weaver or its tests. This patch's number is claimed at push. #212 (kronikol-c6) shipped as 5.26.1 and 5.27.0; #210 as 5.16.3 and 5.16.4. At the time of writing no
  worktree has an uncommitted change in `src/Kronikol.AssertionTracking`, and no local branch a commit there that origin
  lacks (`v5` included).
- The plan's line numbers are 5.27.6's, the same as 5.27.4's to 5.27.10's. The sites at 5.27.2, where the plan was
  first written:

  | Site | 5.27.2 | 5.27.6 |
  |---|---|---|
  | `IsKeptAcrossTheAwait` | 1024 | 1062 |
  | `DetectCapturedVariables` | 1717 | 1755 |
  | the statement's pair arm, `GetStateFieldOriginalName(fieldRef)` | 1849 | 1887 |
  | the chained arm (its angle-bracket check) | 1856-1892 (1863) | 1894-1930 (1901) |
  | `ResolveLambdaMethod`; the walk of the lambda's fields | 1956; 1971-1975 | 1994; 2009-2013 |
  | F1's arm (`lambdaVarName`) | 1976-1993 | 2014-2031 |
  | the lambda's async raw arm (`OwnField(outerThisField)` at 5.27.6) | 2008-2028 | 2046-2066 (2061) |
  | the lambda's non-async raw arm (#211's guard at 5.27.6) | 2029-2039 | 2067-2087 (2074) |
  | the `ldtoken` arm (its check; `OwnField`; the guard) | 2048-2100 (2053) | 2092-2148 (2097; 2129; 2139) |
  | the standalone arm (its check; `OwnField`) | 2103-2160 (2116) | 2150-2208 (2163; 2206) |
  | `GetStateFieldOriginalName` | 2414 | 2461 |
  | `IsFieldAccessibleOnType` | 2536 | 2583 |
  | the emitter's loads (`WrapSingleAssertion`) | 3179-3195 | 3227-3263 |
  | `OwnField` | (none) | 3547 |

## 9. Questions for the owner

Answered 2026-11-07: every question at its recommendation (section 11).

1. **The rule (A)**, or the issue's narrow fix (B: read `<name>P` through `<>4__this` in F1's arm alone)? B ends the
   crash and the wrong objects of F1, and leaves F4, F5, the reads off another object (F6) and F7. Recommended: A.
2. **The emitter's check (4.1 e)** in this release? It drops any load whose field is not on the object it is loaded
   off, whichever arm produced it. Recommended: yes.
3. **The values drawn where the note drew a name** (4.2): an awaited statement's primary-constructor parameter (F4,
   `... should be 'Harness(expected)'` for `... should be expected`), an expression tree's, with its members (F5), and
   a closure's own field in an expression tree inside a woven lambda (`x == 'a1'` for `x == local`). Each fixes a gap
   but changes a label people see. Recommended: yes.
4. **F7 (a generic base class's field in a synchronous test) and F6's reads off another object** (P13, P17 b, and
   another class's field in Release) in this release, as CLAUDE.md asks for bugs found on the way? Recommended: yes.
5. **A comment on #213**: the owner's own comment of 2026-10-29 confirms the crash on 5.27.5. The plan's comment would
   add the conditions measured (Debug too since 5.26.1, the layout decides the build, the second crash site, a
   closure's method and FluentAssertions' delegates as well), and that the pragma stays the workaround until the
   release. Post it now, at the release, or not at all? Recommended: at the release.

## 10. Log

- 2026-10-29, kronikol-13: the issue read (`gh issue view 213`), the capture code traced at 5.27.2 (F1, F5, 3.4, 3.8),
  the harness's runs started (`repro/` on the published packages, `probe/` in the weaver suite's builder), and the
  split with #211 agreed with kronikol-12 (#211 first, as 5.27.4).
- 2026-10-29, kronikol-13: the measurements.
  - `repro/`: T1 to T7 on xUnit v2 and v3, Debug and Release, 3 runs each, on 5.27.2 and, for T1 and T2, on 5.12.0.
    The issue's IL reproduces exactly on 5.12.0.
  - `probe/`: 96 cases, 216 runs, plus the values pass.
  - `prototype.patch`: the weaver suite unchanged test for test, every case as unwoven, ILVerify errors 50 to 0.
  - Changed in the plan by the measurements:
    - the issue's "Release only" is out of date (F3);
    - a second crash site, in `ChainValues.Keep` (F2);
    - the static-method receiver (3.6);
    - F7, found by the prototype;
    - a statement's own read of the parameter is not a finding (3.4), since its value comes through the argument
      path;
    - the dive's static dump re-detects on woven IL, so the disassembly decides P14's cause (#211's D-A).
- 2026-10-29, kronikol-13, at the owner's ask to make sure the plan is complete and accurate: checked again on
  5.27.6's weaver (the same in 5.27.4 to 5.27.10).
  - The owner's comment (5.27.5 still crashes) read, and the harness's T1 to T7 run on the published 5.27.5: every
    outcome as on 5.27.2, and the load in #211's protected region (row 10).
  - The prototype re-made on 5.27.6 in place of #211's guards (`prototype-5.27.6.patch`), and both weavers run through
    the weaver suite, the catalogue and the scratch fixture (`probe/run-all.sh`).
  - Seven shapes added (P18 to P23, P22b). They found F4 again (an awaited statement draws no value for the parameter;
    the earlier "not a finding" held only for a statement that is not awaited), F1 in a closure's method (synchronous
    code too) and through FluentAssertions after `.Should()`, and a value type's wrong number (P19).
  - Also found: the standalone arm reads another class's field off the instance in Release (F6), and three label
    changes of the prototype that the first version did not list (4.2).
  - Corrected: the static-method and closure-field cases (P14 to P16) are #211's since 5.27.4; the scan's start decides
    which FluentAssertions lambdas reach F1 (3.1); the filter of 3.9 runs only where the carry cannot be placed.
- 2026-11-07, kronikol-13, at the owner's word to implement the plan in full: measured again on 5.31.1, the
  prototype re-made there, the tests written red first, the fix read by an independent review and its
  faults fixed, and the release made on 5.32.0 (section 11).

## 11. Execution (2026-11-07)

The owner's word, 2026-11-07: "can you implement the plan in full - make sure to not interfere with any of the other
sessions". Every question of section 9 at its recommendation: the rule (A), the emitter's check, the values drawn where
names were, F6's reads in this release, and the comment on #213 at the release. Released as 5.32.1, measured on 5.31.1
(`896ddad3`) and moved onto 5.32.0 (#219's two releases came first, and touch neither the weaver nor its tests), from
the worktree `C:/Code/Kronikol-213r`.

**What had moved since the plan.** Three releases changed the weaver after 5.27.10: 5.28.3 (`7a8b98a3`, the branch
targets of an assertion in a condition), 5.29.0 (`c667bad6`, step method shapes) and 5.29.2 (`f76dabf1`, F31 of
`STEP_METHOD_SHAPES_PLAN.md`: `WhatCanBeRead`, and `IsFieldOf` in the standalone arm). Measured again on 5.31.1
(`results/*-main-5.31.1*`):
- The catalogue is as on 5.27.6: the same 21 cases crash the test process in all three rounds, every other note is the
  same (P19's wrong number changes from run to run, as before), the recorded values are the same, and ILVerify finds
  the same 51 errors. F1 is untouched by those releases.
- The scratch fixture's `Pc.Async_lambda_this_only` crashes the test process in Debug (a segmentation fault). Run
  without `Pc`, the fixture shows 6 ILVerify errors where 5.27.6 showed 23, all of them F1's.
- F7 is fixed: `Derived.Sync_generic_base_field` runs and draws `Value should be '5'`. So is the standalone arm's read
  of another class's field off the instance (`Receivers` in Release), through `IsFieldOf`. This release keeps both and
  replaces `IsFieldOf` with the rule's own check (`FieldOfInstance`, through `IsFieldAccessibleOnType`), which reads
  the same in every case measured.

**The fix.** `prototype-5.27.6.patch` applied to 5.31.1 with seven of its eight hunks as they were; the eighth (the
`ldtoken` and standalone arms) met 5.29.2's two edits there and was merged by hand, the rule taking the place of both.
Then: `IsFieldAccessibleOnType`'s summary corrected (3.9), the unused `IsFieldOf` removed, and the emitter's check
made `DropWhatCannotBeLoaded`, which the unit facts call.

**Measured on the fix** before the review (`C:/Code/Kronikol-213q` and `-213r`, `results/*proto-main*`; on the
final code the new test class runs the same catalogue, with the review's shapes, every case as unwoven and every
shape-build ILVerify-clean):
- The weaver suite ends test for test as on 5.31.1: 1,603 passed and 28 skipped, no outcome changed.
- The catalogue's 129 cases: none crashes, every woven run ends as its unwoven run does, and the notes that change are
  exactly those of section 4.2 (39 cases: the 21 crashes and 18 labels).
- ILVerify: 0 errors across the catalogue (51 on 5.31.1) and across the scratch fixture (6).
- The values: 72 cases differ from 5.31.1, each as section 4.2 says. Two more were measured here: P20's second
  statement no longer records `actual` (a static local function, whose `ldarg.0` is its closure, is left out by the
  rule, and no label uses it), and P21 records `c` and `c._id` through the parameter path where 5.31.1 recorded a bare
  `_id`.

**Tests.** `CapturedFieldValueTests` (new), 412 facts: the catalogue's shapes and the scratch fixture, with P13d added
(P13's read in a delegate, M2); `A_woven_shape_verifies`, `A_woven_shape_runs_as_the_unwoven_one`,
`A_note_draws_the_value_the_source_names` (the 39 cases), `The_values_are_the_objects_the_source_names`,
`The_scratch_cases_run_and_draw_what_their_source_names` and four load-check facts. `CrossSdkWeaverTests` gains the
issue's two tests, built by the .NET 8, 9 and 10 SDKs in Debug and Release. On 5.31.1, with an empty
`DropWhatCannotBeLoaded` so that the facts compile, 205 of the first 412 fail and none ends the process
(`results/red-main-5.31.1-b.trx`), and the SDK theory fails 6 of 6, each at ILVerify, on F1's load in both of the
issue's tests (`results/crosssdk-213q.txt`). On the fix every one passes (`results/green-213-b.trx`,
`results/crosssdk-213r.txt`).

**The review.** An independent read of the fix against this plan found what the catalogue had not:
- A closure's own field, read by an inner lambda where the woven method is that closure's method
  (`new List<int> { 1 }.ForEach(x => x.ShouldSatisfyAllConditions(v => v.ShouldBe(expected)))`), was left out, where
  5.31.1 drew it. The prototype placed it only in an expression tree (`InLambdas`), though 4.1 c names the case. The
  same held through a child closure's link to its parent, in an async lambda, and for every lambda of a file-local
  class, whose metadata name starts with `<`.
- A `volatile.` prefix hid a field's receiver, and an async method of a struct reads its copy of `this` by address
  (`ldflda <>4__this`); both left a value out that 5.31.1 drew.
- Older than the fix: a field read at a branch target took the instruction before it as its object, so
  `2.ShouldBe((_parent ?? this)._depth)` recorded this instance's `_depth` (P24 fails on 5.31.1 in every build).
- The review also read a method group's body as walked as if it were a lambda bound to `this`
  (`Should.Throw<T>(other.Fail, "_id")`). Measured, it records nothing wrong, on 5.31.1 or on the fix (P25 passes on
  both), so the walk's new limit to the lambdas and local functions the compiler made is hardening, not a fix.
- M8 survived: 5.29.2 drops a value boxed as `!0`, and the argument path draws the parameter anyway, so no note
  showed the `this` branch's generic resolution.
- No fact could see a value left out.

So the rule's receiver check now asks of a lambda's `ldarg.0` that the lambda be a method of the woven method's own
type or of the type its `<>4__this` holds, and accepts a closure's link to a parent closure of one of those types; a
read at a branch target is left out; only a lambda or local function the compiler made is walked; a prefix is passed
over; the statement's pair arm takes `ldflda <>4__this`. `CanLoad` also runs where the values are detected, so the
argument path and the carry see the same set. The tests gained the review's shapes (the scratch fixture's `Closures`,
`FileSteps` and `StructSteps`, the catalogue's P24 and P25, and `Derived.Sync_expression_generic_base_field`, which
catches M8), and a presence fact (`A_primary_constructors_parameter_is_recorded_as_its_own_argument`, 63 values).
With them the class has 499 facts. On 5.31.1 274 of those 505 fail, none ending the process
(`results/red-main-5.31.1-final.trx`); on the fix with the review's changes every one passes, and the review's six
scratch shapes draw exactly what 5.31.1 draws, in every build. On the fix before the review the scratch fact fails
in all three builds.

**Mutations** (section 6.3, `results/mutations/`). On the fix before the review (`mutations-213.txt`) ten of the
plan's eleven weaver mutants were caught and M8 survived; with the generic-base expression case it is caught
(`mutations-213-M8-rerun.txt`). On the final code (`mutations-213-final.txt`) 16 of 18 are caught: M1 to M9, M11 and
M12 of the plan, and M13 to M17 for the review's changes, each by the facts the file names. Two survive, as expected:
M18 (no limit to the lambdas the compiler made) is the hardening above, which no measured shape can see, and M19 (no
load check at detection) leaves the emitter's own check to drop the same values. M10, the builder compiling one way
only, mutates the tests and not the weaver, so the script does not run it; every fact runs in all three builds.

**Checks:** the weaver suite on the final code, 2,108 passed and 28 skipped (`results/suite-final-213-b.txt`); the same
suite on Linux in the .NET 10 SDK container, 1,987 passed and 149 skipped for the SDKs it lacks
(`results/linux-weaver-213.txt`); the step-tracking suite, the only other one that builds the assertion weaver,
2,237 of 2,237 (`results/steptracking-213.txt`); `rerun-local.sh`, the issue's T1 to T7 on the release's weaver packed
locally over the published 5.31.1 core, 96 of 96 runs passed under xUnit v2 and v3, Debug and Release, each with its
report (`results/outcomes-5.31.3-local.213b.txt`). Not run: the core suite, which does not reference the weaver, and
a whole `release.slnf` build in Release, with C: below 10 GB for three sessions; the one shipped project that
changed, `Kronikol.AssertionTracking` (netstandard2.0), was built in Release and packed, and CI builds the rest.
