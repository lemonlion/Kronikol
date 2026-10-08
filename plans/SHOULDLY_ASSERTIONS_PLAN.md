# Shouldly assertions in assertion tracking (#141)

**Date:** 2026-10-07 · **Repo version:** 4.9.0 (`main` at 417c8e58; line numbers are for that commit) · **Status:**
**not green-lit.** Drafted from #141, with its two siblings from the same suite and the same day, #144 and #145.

Evidence labels: **RUN** (measured on 2026-10-07 with the published 4.9.0 packages, Shouldly 4.3.0 and
AwesomeAssertions 9.6.0 on net8.0, built by SDK 10.0.300; inputs, scripts and saved output in
[`SHOULDLY_ASSERTIONS_PLAN.harness/`](SHOULDLY_ASSERTIONS_PLAN.harness/README.md)), **READ** (in the source, tests,
wiki or changelog at that commit), **INFERRED** (reasoned from two facts, stated by neither), **ESTIMATED** (not
measured; the step that would measure it is named).

The owner's issue, [#141](https://github.com/lemonlion/Kronikol/issues/141) (2026-10-07, filed against 4.6.0):

> Assertion tracking detects FluentAssertions / AwesomeAssertions `.Should()` and TUnit `Assert.That()` only.
> Shouldly is common in .NET suites (`value.ShouldBe(x)`, `ShouldNotBeNull()`, `ShouldBeEmpty()`,
> `ShouldSatisfyAllConditions(…)`); ours has several hundred such assertions, and wrapping each in `Track.That()`
> isn't practical. `ErrorDiffParser` already parses Shouldly's failure messages.
>
> Supporting Shouldly's `Should*` extension methods in the weaver / rewriter would give these suites the assertion
> sub-steps and values the others get.

Its comment, on 4.7.1: the suite has 447 Shouldly calls across 18 files, the most common being `ShouldBe`,
`ShouldNotBeNull`, `ShouldBeTrue`, `ShouldNotBeEmpty` and `ShouldSatisfyAllConditions`. With
`Kronikol.AssertionTracking` and `[assembly: TrackAssertions]` added, the build succeeds, `-v:d` shows
`Completed in 45ms (no assertions found - No assertion library referenced)`, all 52 scenarios pass, and the report
has no assertion notes and no Assertions toggle. The package and the attribute are left in place, so the suite picks
up Shouldly support on upgrade.

The siblings:

- [#144](https://github.com/lemonlion/Kronikol/issues/144): the weave that instruments nothing says so only at
  detailed build verbosity, and the package description still names `[assembly: TrackAssertionsBeta]`.
- [#145](https://github.com/lemonlion/Kronikol/issues/145): `kronikol query assertions` lists every step recorded
  without a keyword as an assertion (17 NScenario steps in the owner's suite, with no assertion tracked at all).

## TL;DR

- **Nothing is instrumented, and nothing says so (RUN, §2.1).** On 4.9.0 a Shouldly-only probe builds with no
  output about the weave at the default verbosity; `-v:d` prints `No assertion library referenced`. With
  AwesomeAssertions referenced as well, the weaver runs and instruments its 7 statements, and none of the 11 Shouldly
  ones.
- **Recognising Shouldly takes a few lines. A Release build is where it breaks (RUN, §2.7).** A prototype that only
  adds the name checks weaves all 11 cases in Debug, with Shouldly's own failure messages byte for byte unchanged. The
  same prototype on a **Release** build throws `InvalidProgramException` in 9 of the 11. The C# compiler keeps a
  Shouldly subject on the evaluation stack across statements (`int result = 5;` is never stored), so the injected
  `try` starts on a non-empty stack, and the weaver's spill step skips the spill whenever a statement's first
  instruction pops nothing, which FluentAssertions' shape never hit. Removing that shortcut fixes 5 of the 9, the
  other 4 need a real stack analysis, and wherever a spill is inserted Shouldly's message starts quoting the
  previous line of the test. CI commonly builds Release (this repo's own does), so the core of the minor is the stack
  analysis, not the name check.
- **Everything after the weave reads FluentAssertions' shape (READ, RUN, §2.5).** The label formatter splits on
  `.Should().` and otherwise prints the raw code, uncapitalised, and throws the captured values away, so a Shouldly
  row reads `✓ result.ShouldBe(expected)` where FluentAssertions reads `✓ Result should be '5'`. The value capture
  looks only after the entry call, and Shouldly's arguments come before it. `Track.That` with Shouldly, the wiki's
  workaround today, resolves no values either.
- **Found on the way, in shipped code (§0):** a failed FluentAssertions assertion inside an `AssertionScope` is drawn
  as a green pass (RUN); a project with `<DebugType>embedded</DebugType>` is skipped as silently as one without a
  supported library (RUN); a `!` is dropped from string literals in labels (RUN); an assertion in a lambda on its own
  line is labelled with the lambda's parameter and arrow (RUN); a failure whose values are not strings gets no
  Expected and Actual, in Shouldly's shape and FluentAssertions' alike (RUN); unrelated Shouldly failures on one
  subject are digested as a single cause (RUN); the wiki says the templates ship assertion tracking, which they
  stopped doing in 2.37.1 (READ).
- **Releases (§5).** **R1, a patch:** the shipped defects above. **R2, a minor:** Shouldly support (recognition, the
  stack analysis, labels, values) with #144's warning, which fails a build run with `-warnaserror` (RUN) and so does
  not belong in a patch. **#145** rides in R2 if the owner takes Q1; its fix adds a field to `TestRunReport.json`, which
  is a minor on its own.

## 0. Findings

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **A Shouldly-only assembly is skipped before any method is read.** The fast path looks for FluentAssertions, AwesomeAssertions or TUnit references (`AssertionWeaver.cs:216-228`) and returns `No assertion library referenced` (`:61-66`) | RUN, READ | §2.1 |
| F2 | **The skip is logged at `MessageImportance.Low`** (`WeaveAssertionsTask.cs:76-81`), so `dotnet build` and `dotnet test` print nothing (#144). So is the PDB skip (`:52-57`) | RUN | §2.1, §2.3 |
| F3 | **When the weaver does run, a Shouldly call is no entry point.** `IsAssertionEntryPoint` takes a method named `Should` on a FluentAssertions, AwesomeAssertions or TUnit type, or TUnit's `Assert.That` (`:650-658`, `:637-643`) | RUN | §2.2: 7 of 7 AwesomeAssertions statements, 0 of 11 Shouldly ones |
| F4 | **A project without a `.pdb` file is skipped silently, `DebugType=embedded` included**, so its FluentAssertions assertions go untracked too (`WeaveAssertionsTask.cs:52-57`, `File.Exists` before Cecil is asked; Cecil reads embedded portable PDBs) | RUN | §2.3 |
| F5 | **Release: the injected `try` can start on a non-empty stack.** The compiler leaves a Shouldly subject on the stack across statements; `SpillStackIfNeeded` returns early when the statement's first instruction pops nothing (`:1675-1682`) | RUN | §2.7, prototype only |
| F6 | **Without that shortcut, 4 shapes stay invalid.** The stack depth is a linear walk that resets at branch targets (`ComputeStackDepthAt`, `:1794-1836`) and the spill types are guessed backwards from the previous instruction, falling back to `object` (`:1684-1712`, `:1923-1978`) | RUN | §2.7, prototype only |
| F7 | **Where a spill is inserted, Shouldly's message quotes the previous line** (`string? name = null;⏎ name⏎ should not be null but was`). Shouldly reads the source line of the test's frame, and the inserted prologue sits under the previous statement's sequence point | RUN (the message), INFERRED (the frame) | §2.7, prototype only |
| F8 | **Values are captured only from instructions after the entry call** (`DetectCapturedVariables`, `:786-819`). Every argument of `x.ShouldBe(y)` is loaded before the call, so a Shouldly row would capture nothing | READ, RUN | §2.4 |
| F9 | **The label formatter is FluentAssertions-only.** Without `.Should().` it returns the cleaned raw expression and drops the resolved values (`AssertionExpressionFormatter.cs:15`, `:54-57`, `:184-191`); a raw camelCase label is not capitalised (`StepText.cs:83-84`). TUnit's `Assert.That` rows are raw for the same reason | READ, RUN | §2.5 |
| F10 | **`ClosureValueResolver` needs `.Should().`** (`ClosureValueResolver.cs:90-95`), so `Track.That(() => result.ShouldBe(42))`, the wiki's Shouldly example (`Assertion-Tracking.md:331-332`), shows no values | READ | §2.5 |
| F11 | **The statement text is read by whole source lines, not the sequence point's columns** (`ReadSourceText`, `:660-731`). An assertion in a lambda on its own line takes its neighbours' text: Shouldly's `order.Id.ShouldBe(2),` and `order.Name.ShouldBe("b"))` (prototype), and on 4.9.0 FluentAssertions' `✓ First => first should be 1` | RUN | §2.2, §2.7 |
| F12 | **A failed FluentAssertions or AwesomeAssertions assertion inside an `AssertionScope` is drawn as a pass.** The scope defers the throw to its `Dispose`, which is in no woven statement: `result.Should().Be(5)` with `result` 3 drew `✓ Result should be 5`, and the test failed | RUN | §2.2 |
| F13 | **The formatter removes every `!`** (`AssertionExpressionFormatter.cs:46`, meant for the null-forgiving operator): `greeting.Should().Be("Hi!")` is labelled `✓ Greeting should be "Hi"`. It also turns `!=` into `=` | RUN, READ | §2.2 |
| F14 | **`ErrorDiffParser` reads only quoted values.** Its Shouldly pattern needs quotes (`ErrorDiffParser.cs:27-28`), and Shouldly writes numbers, booleans and enums bare (`result⏎    should be⏎5⏎    but was⏎3`); its FluentAssertions pattern matches only `Expected string to be` (`:25`). In an ingest of six failures, `Failures.jsonl` carried an expected and an actual for the string `ShouldBe` alone, none for the int `ShouldBe` or for `Expected result to be 5, but found 3.` Its one Shouldly test is a string (`ErrorDiffParserTests.cs:83-96`) | RUN | `harness/s0b/cluster/` |
| F15 | **Failures cluster on their message's first line** (`FailuresDigestGenerator.cs:494`, `FailureClusterer.cs:20`, `HistoryRunBuilder.cs:101`), and a Shouldly message's first line is its subject. An int `ShouldBe`, a string `ShouldBe` and a `ShouldBeGreaterThan`, all on `result`, were digested as `### result — 3 scenarios` with one worked through; FluentAssertions' two messages, differing only in a value, got an entry each | RUN | `harness/s0b/cluster/` |
| F16 | **#145: a step is an assertion when it has no keyword** (`ReportIndex.cs:212-213`). `[Step]` steps (`StepWeaver.cs:41`) and bridged NScenario steps have none | READ | §3.10 |
| F17 | **Ingested assertion steps carry their glyph in the text** (`✓ text`, `FeatureSynthesizer.cs:414-429`, `CucumberFeatureMerger.cs:208-219`); in-process ones do not (`StepCollector.cs:221-232`) | READ | §3.10 |
| F18 | **The docs disagree with the code.** `Assertion-Tracking.md:236` and `Project-Templates.md:9`, `:142` say the templates ship assertion tracking; 2.37.1 removed it (`CHANGELOG.md:6146`, and `Project-Templates.md:80` says so). `Assertion-Tracking.md:24` installs 2.31.0. The package description names FluentAssertions only and `[assembly: TrackAssertionsBeta]` (`Kronikol.AssertionTracking.csproj:8`, #144), as do `WeaveAssertionsTask.cs:11-14` and the reference attribute (`Attributes/TrackAssertionsAttribute.cs:12`) | READ | §6 |
| F19 | **No test reads a failure note, none runs `WeaveAssertionsTask` or the `.targets`, and no project in the repo builds with `[assembly: TrackAssertions]`** (templates dropped it in 2.37.1; the examples use FluentAssertions and TUnit) | READ | §2.8 |
| F20 | **The source rewriter keys on `.Should()` too** (`AssertionWrappingRewriter.cs:244-270`), but it has been unpublished since 2.31.0 (not in `release.slnf`; `CHANGELOG.md:6841`, `:6849-6850`) | READ | §8 |

## 1. How far each claim was checked

- **The repro and every RUN finding in §0 outside the prototype's (F5 to F7) were measured on the published 4.9.0
  packages** with the probe in
  `harness/probe/` (a console app with no test framework, which names a test per case through `Track.TestIdResolver`
  and reads the notes back from `RequestResponseLogger`). Saved output: `harness/s0/`.
- **The prototype numbers are from a copy of the weaver** with the changes in `harness/proto/weaver-prototype.diff`,
  run over the same probe by `harness/proto/Runner.cs`. They describe what a first cut would do. Nothing in `src/`
  is changed.
- **Every code path is READ at 417c8e58** with the line cited. Four research passes over the weaver, the runtime path
  after `Track`, the tests and CI, and the wiki are summarised in §2.4 to §2.8.
- **Shouldly's API surface and message shapes** are in §2.6 (RUN, the inventory in `harness/shouldly/`).
- **F14 and F15 were measured with the published 4.9.0 tool**: `kronikol ingest` of six failing tests (four Shouldly
  messages, two FluentAssertions ones) with history off, then `Failures.md` and `Failures.jsonl`
  (`harness/s0b/cluster/`). How a task's coded warning behaves under `<NoWarn>` and warnings-as-errors was measured on
  SDKs 8.0.421 and 10.0.300 (`harness/s0b/nowarn-results.txt`).
- **The owner's suite** (447 calls, 52 scenarios) is the issue's and was not re-measured. Its acceptance is §4.4.

## 2. What the code does today

### 2.1 The repro on 4.9.0 (RUN)

`harness/probe/` references `Kronikol`, `Kronikol.AssertionTracking` (both 4.9.0) and Shouldly 4.3.0, declares
`[assembly: TrackAssertions]`, and runs eleven cases, each as its own test: the issue's five most common methods
(`ShouldBe` passing and failing, `ShouldNotBeNull`, `ShouldBeTrue`, `ShouldNotBeEmpty`), a custom message, an array
argument, a null-conditional subject, `ShouldSatisfyAllConditions` with two failing conditions, `Should.Throw` and an
awaited `Should.ThrowAsync`. Each case is written one statement per line, as real tests are (see §10, trap 4).

- `dotnet build`: nothing about the weave (`s0/plain-default.log`).
- `dotnet build -v:d` (`s0/plain-detailed.log`):

  ```
  Kronikol.AssertionTracking: Completed in 206ms (no assertions found - No assertion library referenced)
  ```

- The run (`s0/plain-run.txt`): the assembly carries no weave sentinel, the six failing cases throw Shouldly's own
  `ShouldAssertException`, and **every case records 0 assertion notes.**

### 2.2 The control: AwesomeAssertions beside Shouldly (RUN)

`dotnet build -p:Control=true` adds AwesomeAssertions 9.6.0 and five cases that use it (`s0/control-build.log`,
`s0/control-run.txt`):

```
AssertionTracking: 13 types, 64 methods, 7 assertions weaved in 364ms
Kronikol.AssertionTracking: Instrumented 7 assertion(s) in 6 method(s) (478ms)
```

Seven is every AwesomeAssertions statement, lambda bodies included, and none of the Shouldly ones.

| Case | What it shows |
|---|---|
| The eleven Shouldly cases | 0 notes each: the weaver ran and skipped them (F3) |
| `Control_Should_Be_fails` | `✗ Result should be '5'⏎Expected result to be 5, but found 3.`, the shape a Shouldly row should match |
| `Control_AssertionScope_fails` | Inside `using (new AssertionScope())`, `result.Should().Be(5)` with `result` 3 and then `result.Should().Be(3)`: **two green notes, `✓ Result should be 5` and `✓ Result should be 3`**, and the test fails with `Expected result to be 5, but found 3.` from the scope's `Dispose` (F12) |
| `Control_label_keeps_a_bang_in_a_literal` | `greeting.Should().Be("Hi!")` is labelled `✓ Greeting should be "Hi"` (F13) |
| `Control_lambda_conditions` | `items.Should().SatisfyRespectively(` with one lambda per line draws three notes: `✓ First => first should be 1`, `✓ Second => second should be 2` and the outer `✓ Items should satisfy respectively [  first => first.Should().Be(1), second => second.Should().Be(2) ]` (F11; also the precedent for Q4) |

### 2.3 An embedded PDB (RUN)

`dotnet build -p:Control=true -p:DebugType=embedded -v:d` (`s0/embedded-build.log`):

```
AssertionTracking: PDB not found at obj\Debug\net8.0\Probe.pdb, skipping
```

The run draws no note for the AwesomeAssertions assertions either. `WeaveAssertionsTask` checks for the `.pdb` file
before Cecil is asked, and Cecil's `DefaultSymbolReaderProvider` reads an embedded portable PDB (F4). `DebugType=none`
takes the same branch, and there the skip is right: without sequence points there is no statement to wrap. Neither
says so at the default verbosity.

### 2.4 The weaver, step by step (READ)

`WeaveAssertionsTask` runs after `CoreCompile` on the intermediate assembly (`build/Kronikol.AssertionTracking.targets`)
and hands it to `AssertionWeaver.Weave`:

1. **Gates.** The `[assembly: TrackAssertions]` attribute, or the old `TrackAssertionsBeta` (`:207-214`); a reference
   to FluentAssertions, AwesomeAssertions, `TUnit.Assertions` or `TUnit.Assertions.Should`, or a type defined in one
   of those namespaces (`:216-228`, the second form is how the TUnit tests use stubs); no weave sentinel (`:158-162`);
   a Kronikol reference of 2.30.7 or later (`:267-352`).
2. **Statements.** A statement is one sequence point. A method is read only if one of its calls is an entry point
   (`:368-384`); each visible sequence point whose instructions hold an entry call becomes an `AssertionStatement`
   (`:416-522`), unless a `pragma:TrackAssertions:disable` comment covers it (`:750-784`) or the method or type carries
   `[SuppressAssertionTracking]` (`:230-265`). Compiler-generated types are walked too, so a lambda's body is woven on
   its own.
3. **Entry points.** A method named `Should` on a type in one of the three namespaces, or `Assert.That` in
   `TUnit.Assertions` (`:637-658`).
4. **Text.** The statement's source is read from the PDB's document path, by whole lines, extended until parentheses
   balance, at most 20 lines (`ReadSourceText`, `:660-742`).
5. **Values.** `DetectCapturedVariables` scans the instructions **after** the entry call for locals, parameters, state
   machine fields, closure fields, lambda bodies and expression-tree field tokens whose names appear in the text
   (`:786-1228`). Their values are copied into two arrays before the `try`.
6. **The wrap.** `try { statement; Track.AssertionPassed[WithValues](text, [names, values,] file, line); }
   catch (Exception ex) { Track.AssertionFailed[WithValues](text, ex.Message, …); throw; }` (`:1980-2337`), with three
   repairs for Release builds: values left on the stack at the statement's start are spilled and reloaded inside the
   `try` (`SpillStackIfNeeded`, `:1657-1725`), values the statement leaves for the next one are saved around the
   `leave` (`ComputeExitStackDepth`, `:1732-1787`), and branches into or out of the range are retargeted.
7. **Awaited assertions.** When the statement awaits a task that an assertion-library method returned, the `try` goes
   around `GetResult()` at the await's merge point instead (`:478-509`, `:2347` onwards). An `await` inside an
   argument is skipped.

### 2.5 After `Track` (READ, and RUN for the labels)

- **`Track.LogAssertion`** (`Track.cs:416-456`) finds the test (`TestIdResolver`, then `TestIdentityScope`; none,
  nothing is recorded), adds a sub-step to the active step if there is one (`StepCollector.AddAssertionSubStep`,
  keyword `null`, no values; gated by `StepTrackingOptions.IncludeTrackedAssertionsInStepList`, default true), and
  always splices a note into the test's diagram: `hnote across <<assertionNote>> #d4edda|#f8d7da`, `✓ label` or
  `✗ label⏎message`, and a `'__^*__:file:Lline` comment.
- **The label** is `AssertionExpressionFormatter.Format(text, values)`, capitalised. The formatter strips `() => `,
  `await ` and `var x = `, removes every `!`, turns `?.` into `.`, and then splits on `.Should().`
  (`AssertionExpressionFormatter.cs:15-57`). With a match it writes `{Subject} should {method words} {arguments}`,
  substituting the captured values into the arguments. Without one it returns the cleaned code, and the values are
  computed and dropped (F9). `StepText.CapitaliseIfEnabled` leaves a camelCase first word alone, so the label stays
  `result.ShouldBe(expected)`.
- **Nothing downstream depends on `.Should()`.** The diagram style, the Assertions toggle (drawn when a report has an
  `<<assertionNote>>` and renders with BrowserJs, `ReportGenerator.cs:1413-1415`; shown by default when
  `ReportToggleDefaults.AssertionsShown`), the note scripts and the query verbs key on the stereotype, the glyph and the
  comment.
- **The failure message** reaches the note verbatim. `ErrorDiffParser` reads only the scenario's error, for the
  report's side-by-side diff and `Failures.md`'s table (F14), and the failure digest clusters on its first line (F15).

### 2.6 Shouldly 4.3.0: its API and its messages (RUN)

The inventory (`harness/shouldly/`) reflects over the package's `lib/net8.0/Shouldly.dll`
(`4.3.0+cb48f40b`; the package also ships `net9.0` and `netstandard2.0` builds).

- **53 public types**, 45 of them in namespace `Shouldly`. **17 carry `[ShouldlyMethods]`**, all public static
  classes, among them `ShouldBeTestExtensions`, `ShouldBeNullExtensions`, `ShouldBeEnumerableTestExtensions`,
  `ShouldSatisfyAllConditionsTestExtensions`, `ShouldThrowExtensions`, `Should` and `DynamicShould`.
- **187 public static methods on those 17 types**, and none elsewhere that asserts. 149 of them (57 names) start
  with `Should`; the rest are `Should.Throw` (14 overloads), `Should.NotThrow` (10), `Should.CompleteIn` (6),
  `Should.ThrowAsync` (4), `Should.NotThrowAsync` (2), `DynamicShould.Throw` and `DynamicShould.HaveProperty`.
- **One of the 17 types is in another namespace:** `ShouldHaveEnumExtensions`, whose `ShouldHaveFlag` and
  `ShouldNotHaveFlag` live in `Shouldly.ShouldlyExtensionMethods`. A rule "a `Should*` method in namespace
  `Shouldly`" misses those two and `DynamicShould.HaveProperty`; the marker attribute catches all 187 (§3.1).
- **61 return a value** (`ShouldBeOfType<T>()` returns the `T`, `Should.Throw<T>()` the exception), **48 take a
  delegate**, 4 an `Expression<>` and 26 a `Task`. **172 take a `customMessage`**, always as `string?`. None uses
  `[CallerArgumentExpression]`, and all 187 are `[MethodImpl(MethodImplOptions.NoInlining)]`, which keeps their
  frames on the stack for the message's source lookup.
- **The 57 names:** `ShouldAllBe`, `ShouldBe` (18 overloads), `ShouldBeAssignableTo`, `ShouldBeDecoratedWith`,
  `ShouldBeEmpty`, `ShouldBeEquivalentTo`, `ShouldBeFalse`, `ShouldBeGreaterThan`, `ShouldBeGreaterThanOrEqualTo`,
  `ShouldBeInOrder`, `ShouldBeInRange`, `ShouldBeLessThan`, `ShouldBeLessThanOrEqualTo`, `ShouldBeNegative`,
  `ShouldBeNull`, `ShouldBeNullOrEmpty`, `ShouldBeNullOrWhiteSpace`, `ShouldBeOfType`, `ShouldBeOfTypes`,
  `ShouldBeOneOf`, `ShouldBePositive`, `ShouldBeSameAs`, `ShouldBeSubsetOf`, `ShouldBeTrue`, `ShouldBeUnique`,
  `ShouldContain` (7), `ShouldContainKey`, `ShouldContainKeyAndValue`, `ShouldContainWithoutWhitespace`,
  `ShouldEndWith`, `ShouldHaveFlag`, `ShouldHaveSingleItem`, `ShouldMatch`, `ShouldMatchApproved`, `ShouldNotBe` (5),
  `ShouldNotBeAssignableTo`, `ShouldNotBeEmpty`, `ShouldNotBeInRange`, `ShouldNotBeNull`, `ShouldNotBeNullOrEmpty`,
  `ShouldNotBeNullOrWhiteSpace`, `ShouldNotBeOfType`, `ShouldNotBeOneOf`, `ShouldNotBeSameAs`, `ShouldNotContain`,
  `ShouldNotContainKey`, `ShouldNotContainValueForKey`, `ShouldNotEndWith`, `ShouldNotHaveFlag`, `ShouldNotMatch`,
  `ShouldNotStartWith`, `ShouldNotThrow` (10), `ShouldNotThrowAsync`, `ShouldSatisfyAllConditions` (4),
  `ShouldStartWith`, `ShouldThrow` (16) and `ShouldThrowAsync` (4).

The messages, measured by the probe (`s0/plain-run.txt`, Debug, unwoven; `⏎` is a line break):

| Case | Message |
|---|---|
| `result.ShouldBe(expected)`, 3 and 5 | `result⏎    should be⏎5⏎    but was⏎3` |
| `name.ShouldNotBeNull()` | `name⏎    should not be null but was` |
| `flag.ShouldBeTrue()` | `flag⏎    should be⏎True⏎    but was⏎False` |
| `items.ShouldNotBeEmpty()` | `items⏎    should not be empty but was` |
| `result.ShouldBe(5, "custom message")` | `result⏎    should be⏎5⏎    but was⏎3⏎⏎Additional Info:⏎    custom message` |
| `order.ShouldSatisfyAllConditions(…)`, both failing | `order⏎    should satisfy all the conditions specified, but does not.⏎The following errors were found ...⏎--------------- Error 1 ---------------⏎    order.Id⏎        should be⏎    …` |
| `await Should.ThrowAsync<InvalidOperationException>(async () => await Task.Yield())` | ``Task `async () => await Task.Yield()`⏎    should throw⏎System.InvalidOperationException⏎    but did not`` |

What the plan takes from them:

- **The first line is the subject's code**, read from the test's source file: that is why F15's cluster key needs
  more than the first line, and why F7 matters.
- **Values are written unquoted unless they are strings** (`5`, `True`), which `ErrorDiffParser` cannot read (F14).
- **A failure is a `ShouldAssertException`**, and `ShouldSatisfyAllConditions` gathers its conditions' failures into
  one. Shouldly has no scope that defers a failure (READ: no such type in its source), so the `AssertionScope`
  defect (F12) cannot happen to it.
- **Where the subject's code comes from (READ, Shouldly at `cb48f40`, its 4.3.0 build).**
  `FirstNonShouldlyMethodFinder.GetTestMethodInfo` walks the stack trace and takes the first frame whose method is
  neither on a `[ShouldlyMethods]` type, or a type nested in one (`ShouldlyCoreExtensions.IsShouldlyMethod`), nor
  compiler-generated; the message's first line is read from that frame's file and line. So the weave must keep every
  Shouldly call in the method it was written in (it does: the `try` wraps the statement in place), and a suite's own
  `[ShouldlyMethods]` helper is already skipped by Shouldly when it picks the frame, which is what Q5 builds on.
- **Shouldly 5.0.0-preview.2** (inventoried the same way): 18 marked types and 204 methods, a superset of 4.3.0's
  names (new: `ShouldContainAll`, `ShouldContainAny`, `ShouldHaveCount`, `ShouldNotContainAll`, `ShouldNotContainAny`,
  `ShouldSatisfy`), so the marker rule carries over. Two things change: **200 of the 204 take a
  `[CallerArgumentExpression]` parameter**, and only 5 are still `NoInlining`. In 5.x the subject's code text arrives
  as a compiler-filled string argument instead of being read from the source file, so F7 cannot happen there, and the
  value capture must skip that parameter as it skips `customMessage` (§3.4).

### 2.7 A first cut, measured (RUN, prototype)

`harness/proto/weaver-prototype.diff` is the smallest change that recognises Shouldly: `Shouldly` in the reference
fast path, the `Shouldly` namespace as an assertion-library type (which also feeds the awaited path), and a Shouldly
entry point (a `Should*` method in `Shouldly`, or a member of its static `Should` class). Two switches let the runs
separate causes: `PROTO_CAPTURE_BEFORE=1` scans for values before the entry call instead of after it, and
`PROTO_SPILL_POP0=1` removes `SpillStackIfNeeded`'s early return. `harness/proto/Runner.cs` weaves the probe's built
assembly, and the probe then runs as before.

| Run | Build | Weaved | Cases that run | Saved |
|---|---|---|---|---|
| P1 | Debug | 14 statements in 13 methods | 11 of 11 | `proto/p1-debug-run.txt` |
| P2 | Debug, values before the call | 14 in 13 | 11 of 11, labels unchanged | `proto/p2-debug-capture-before-run.txt` |
| P3 | **Release**, values before the call | 14 in 13 | **2 of 11**: 9 throw `InvalidProgramException` | `proto/p3-release-run.txt` |
| P4 | **Release**, values after the call | 14 in 13 | **2 of 11**, the same 9 | `proto/p4-release-default-capture-run.txt` |
| P6, P7 | Release, no early return, values either side | 14 in 13 | 7 of 11 | `proto/p6-…`, `proto/p7-…` |

What P1 shows, in Debug:

- **Shouldly's messages are byte for byte the same** woven and unwoven, for all six failing cases. The call stays
  in the same method, so the frame Shouldly reads its code text from is unchanged.
- **The labels are raw code** (`✓ result.ShouldBe(expected)`, `✗ name.ShouldNotBeNull()⏎name⏎    should not be
  null but was`) and carry no values, in P1 and in P2 alike: the formatter drops them (F9).
- **`ShouldSatisfyAllConditions` draws three notes**: one per condition, woven inside the compiler's lambdas, and one
  for the outer call, whose message repeats both conditions' messages. The conditions' labels take their lines'
  punctuation: `order.Id.ShouldBe(2),` and `order.Name.ShouldBe("b"))` (F11).
- **`Should.Throw` and the awaited `Should.ThrowAsync` are recorded**, the second through the existing awaited path
  once the namespace is known.
- `order?.Name.ShouldBe("a")` is labelled `order.Name.ShouldBe("a")`: the formatter turns `?.` into `.` for every
  library.

Why Release fails (P5, `proto/p5-release-il-before.txt` and `-after.txt`, dumped by `proto/IlDump.cs`). For

```csharp
int result = 5;
int expected = 5;
result.ShouldBe(expected);
```

the Release compiler emits

```
L51  ldc.i4.5            // result: never stored, left on the stack
L52  ldc.i4.5
     stloc.0             // expected
L53  ldloc.0
     ldnull
     call ShouldBe<int>(!!0, !!0, string)
```

and the woven method opens its `try` at a `nop` after `stloc.0`, with `result` still on the stack, which the runtime
rejects. `ComputeStackDepthAt` does find the depth of 1 here, but `SpillStackIfNeeded` returns before spilling because
the statement's first instruction (`ldloc.0`) pops nothing (`:1675-1682`). That early return is safe for
FluentAssertions, whose statement starts by consuming its subject (`call Should(x)`); a Shouldly subject is the first
argument of a static call, so the stack can hold it under the later arguments. The two cases that survive have no
such value: `ShouldSatisfyAllConditions` hoists `order` into a closure, and `Should.ThrowAsync` runs in an async state
machine.

With the early return removed (P6, P7) five more cases run, which leaves four that need more than a heuristic
(`ShouldBe` passing and failing, the null-conditional and `Should.Throw`), and it shows F7: in the cases that now
spill, Shouldly's message quotes the line above the assertion.

```
P1, Debug:        name⏎    should not be null but was
P6, Release:      string? name = null;⏎        name⏎    should not be null but was
```

The spill, the `try`'s `nop` and the reload sit before the statement's sequence point, so they belong to the line
above. The frame Shouldly reads evidently reports an offset among them (INFERRED from the message: the source it
quotes starts on that line). A Release
fix therefore has three parts: an exact stack analysis (§3.2), the statement's sequence point moved onto the first
inserted instruction (§3.3), and a fact per shape that the woven message equals the unwoven one.

### 2.8 The tests today (READ)

- **`tests/Kronikol.Tests.AssertionTracking`** (net8.0, net9.0, net10.0; its own CI job, "Assertion Tracking Tests",
  `ci.yml:147-148`, on SDKs 8, 9 and 10, Release):
  - `AssertionWeaverTests.cs`: 34 facts and 25 theories, 83 cases, 24 of the theories in both Debug and Release.
    `TestAssemblyBuilder.Build` compiles a fixture in memory with Roslyn against the **real** FluentAssertions 8.9.0
    and AwesomeAssertions 9.4.0 assemblies, writes the source beside it so the weaver can read the text, weaves, loads
    and runs it. TUnit is tested through stub namespaces in the fixture source.
  - `CrossSdkWeaverTests.cs`: 56 facts, seven FluentAssertions sources (async with and without `await`, null
    conditional, ternary, try/catch/finally, switch, `await using`) built by the real SDKs 8, 9, 10 and an 11 preview,
    Debug and Release, each required to load and run without an exception; skipped where the SDK is missing.
  - 13 tests read a note back through `TestIdentityScope` and `RequestResponseLogger` and check a substituted value.
    **None reads a failure note's content**; the failing case checks only that the original exception propagates.
- **No test runs `WeaveAssertionsTask` or the `.targets`**, and no project in the repo builds with
  `[assembly: TrackAssertions]`: the templates dropped it in 2.37.1, the examples use FluentAssertions (five) and TUnit
  (one), and none uses Shouldly.
- `AssertionExpressionFormatterTests.cs:33-38` pins the raw fallback for an expression without `.Should().`, on
  `Assert.True(value)`. `ErrorDiffParserTests.cs:83-96` has the one Shouldly message test, a string.
- `release.yml` runs only `Kronikol.Tests` before publishing, so the weaver's tests gate CI but not a release.

## 3. Design

### 3.1 Recognising a Shouldly assertion (R2)

- **The fast path** (`:216-228`) adds the `Shouldly` assembly reference.
- **An entry point** is a call to a method whose declaring type carries Shouldly's own marker,
  `[Shouldly.ShouldlyMethods]`, which Shouldly puts on all 17 of its assertion classes and nothing else (§2.6). The
  weaver resolves the type through the references it already has; a type it cannot resolve counts when it comes from
  the `Shouldly` assembly (Cecil's `DeclaringType.Scope`) in a namespace starting `Shouldly`.
- **Not a name rule.** "A `Should*` method in namespace `Shouldly`" misses `ShouldHaveFlag` and `ShouldNotHaveFlag`
  (namespace `Shouldly.ShouldlyExtensionMethods`) and `DynamicShould.HaveProperty`, and a bare `Should` prefix takes
  in user methods: the weaver's own tests define `ShouldBeTheSame<T>` (`AssertionWeaverTests.cs:2449`).
- **`IsAssertionLibraryType`** (`:637-643`) adds the namespace, which routes `await Should.ThrowAsync(…)` through the
  awaited path (§3.8).
- **A suite's own `[ShouldlyMethods]` helpers** are Q5. Shouldly documents the attribute as the way to write a custom
  assertion, and skips such frames when it reads the code text. Recommended: a call to a method on a
  `[ShouldlyMethods]` type in the test assembly is an entry point, and that type's own methods are not woven, so the
  report draws one row for `order.ShouldBeValid()` rather than the helper's inner checks.

### 3.2 Valid IL in every build (R2, the core)

- **One stack analysis per method replaces three heuristics:** the linear `ComputeStackDepthAt` (`:1794-1836`), the
  backwards type guess (`:1684-1712`, `GetPushedType` `:1923-1978`) and the early return for a first instruction that
  pops nothing (`:1675-1682`). It is a worklist over the method's control flow from the entry (empty stack) and each
  handler's start (empty, or the exception for a catch or filter), following fall-through, branch and switch targets.
  It records each instruction's incoming stack as a list of types, taken from what pushed them: a call's return type
  with its generic arguments resolved, a local's, an argument's or a field's type, a constant's, `null`, `dup`'s
  source, `newobj` and `newarr`. At a join the depths agree in any IL the compiler emits, and two types merge to
  `object` when they differ.
- **A statement whose first instruction has values under it spills exactly those**, with their types, and reloads
  them inside the `try`. `ComputeExitStackDepth`'s dup count (`:1732-1787`) gives way to the same analysis: the depth
  after the statement's last instruction on its fall-through path.
- **When the analysis cannot type a slot or reconcile a depth, the statement is not woven.** The weave counts it and
  says why at `Normal` importance. It never writes IL it cannot account for.
- **The same code serves FluentAssertions and TUnit**, so their 24 Release theories and the 56 cross-SDK facts are
  the regression net for the change, alongside the new Shouldly facts.
- **A verification net (rule 3, Q8):** the weaver's tests run ILVerify (`Microsoft.ILVerification`) over every woven
  fixture and fail on any error the unwoven assembly does not already have. A run only proves the methods it reaches;
  the verifier reads them all.

### 3.3 Shouldly's message stays Shouldly's (R2)

- **The statement's sequence point moves to the first instruction the weave inserts before it** (a spill, the `try`'s
  `nop`, the value arrays), so every offset in the prologue maps to the assertion's own line (F7). The calls the weave
  appends after the statement stay under the statement's sequence point, as today.
- **A fact per shape compares the messages**: the woven assembly's exception message equals the unwoven one's, for
  every Shouldly case in §4.1, Debug and Release.
- FluentAssertions also reads its subject's name from the caller's line (INFERRED; its caller identification walks
  the stack the same way), so the same comparison runs for the FluentAssertions shapes that spill.

### 3.4 Values (R2)

- **For a Shouldly entry, the values are the call's arguments after the subject.** The stack analysis names the
  instruction that pushed each argument slot, and the existing name rules (locals, parameters, fields, closures,
  lambda bodies) apply to the instructions that fill slots 1 to n. This is FluentAssertions' parity: there the values
  are the assertion method's arguments, never the subject. Q3 asks whether to capture the subject as well.
- **A `customMessage` argument is never captured.** In 4.3.0 it is a `string?` parameter on 172 of the 187 methods.
  Nor is a parameter marked `[CallerArgumentExpression]`, which the compiler fills with code text: Shouldly 5's
  preview has one on 200 of its 204 methods (§2.6).
- **Delegate arguments** (`ShouldSatisfyAllConditions`, `ShouldAllBe(x => …)`, `Should.Throw(() => …)`) go through the
  existing lambda-body scan (`:1011-1102`), as FluentAssertions' predicates do.

### 3.5 Labels in plain English (R2)

- **`AssertionExpressionFormatter` learns Shouldly's two shapes**, by rule rather than by table, since the method
  list is long (§2.6) and grows:
  - `subject.ShouldMethod(args)` reads `{Subject} should {method words} {args}`, the words being the method name
    after `Should`, split on case: `ShouldBe` → `should be`, `ShouldNotBeNull` → `should not be null`,
    `ShouldBeGreaterThan` → `should be greater than`, `ShouldBeOfType<Order>()` → `should be of type Order`,
    `ShouldSatisfyAllConditions` → `should satisfy all conditions`.
  - `Should.Throw<T>(…)` and its async and negative forms read `Should throw T` and `Should not throw`;
    `action.ShouldThrow<T>()` reads `Action should throw T`.
  - Resolved values go into the arguments as they do for FluentAssertions, and a `customMessage` argument is not
    shown.
- **`ClosureValueResolver`** (`:90-95`) learns the same split, so `Track.That(() => result.ShouldBe(42))` gets its
  values (F10).
- TUnit's `Assert.That(x).IsEqualTo(y)` stays raw: same cause (F9), not this plan's ask (§8).

### 3.6 The statement's text (R1, which R2 then relies on)

- **`ReadSourceText` cuts the sequence point's columns** (`StartColumn` on the first line, `EndColumn` on the last)
  instead of taking whole lines, so a condition lambda's statement is `first.Should().Be(1)`, without its parameter,
  arrow or trailing punctuation (F11). A statement alone on its line reads as before.
- The parenthesis balancing and the 20-line cap stay for the case they were written for, a sequence point that ends
  before its statement.
- It changes FluentAssertions labels only in the lambda case, which is the fix. One Kronikol4J ledger line.

### 3.7 `ShouldSatisfyAllConditions` (R2, Q4)

- **FluentAssertions already sets the shape (RUN, §2.2):** a row per condition lambda, and the outer call as one more
  row. P1 draws the same for Shouldly, and the condition rows are the readable part once §3.6 trims their text.
- The outer row's failure repeats every condition's message, since that is Shouldly's message. Q4 offers the
  alternatives: conditions only (the outer call is not woven when every argument is a lambda the weave instruments),
  or the outer row only (lambdas passed to it are not woven). Recommended: keep the FluentAssertions shape, so the two
  libraries read alike.

### 3.8 Awaited assertions (R2)

- With the namespace known, `await Should.ThrowAsync<T>(…)` takes the awaited path (RUN, P1) and is wrapped at its
  `GetResult()`. Facts cover each async member §2.6 lists, in Debug and Release, inside `async` methods with and
  without an earlier `await`, as the FluentAssertions async theories do.

### 3.9 #144: the weave says when it does nothing (R2), and embedded PDBs (R1)

- **R1:** the task stops requiring a `.pdb` file. Cecil reads symbols from the assembly when it embeds them, and the
  weave writes them back the same way (`EmbeddedPortablePdbWriterProvider`) (F4). `DebugType=none` keeps skipping:
  without sequence points there is no statement. `AssertionWeaver.Weave(assemblyPath, pdbPath)` is public, but the
  package ships its DLL under `tasks/` (`BuildOutputTargetFolder`), where no consumer compiles against it, so a change
  to its parameters is no change to the public API.
- **R2: a warning with a code** (the repo's first; Q2 names it) when `[assembly: TrackAssertions]` is declared and
  the weave instruments nothing because no supported library is referenced (the message names the libraries the
  weaver reads, Shouldly among them, and `Track.That` for the rest), because the build has no symbols, or because no
  statement in the assembly calls one.
- **How such a warning behaves (RUN, `harness/s0b/`, SDKs 8.0.421 and 10.0.300):** a task's coded warning prints at
  the default verbosity; `<NoWarn>` and `<MSBuildWarningsAsMessages>` silence it; the `TreatWarningsAsErrors` property
  leaves it a warning, since that property governs the compiler's warnings; and `dotnet build -warnaserror` or
  `MSBuildTreatWarningsAsErrors=true` turn it into an error that fails the build, unless `<NoWarn>` names it.
- **Why the warning waits for R2:** a pipeline that builds with `-warnaserror` would fail on it, and the issue's own
  suite (the attribute declared, Shouldly only) would be one of them on a patch that shipped the warning before
  Shouldly support. In R2 that suite is instrumented instead, and only a suite that really gets nothing sees it.
- **R1 corrects the package description and the task's doc comment** (F18), which are metadata.

### 3.10 #145, if Q1 takes it (R2)

- `StepCollector.AddAssertionSubStep` and ingest's two `assertion` events write `"assertion": true` on the step in
  `TestRunReport.json`, the XML and the YAML, and the writer omits it otherwise, so a report without assertions is
  written byte for byte as before. `StepEntry.IsAssertion` reads the field and keeps today's inference for a report
  that predates it (`ReportIndex.cs:212-213`).
- A new field in a published format and its schemas is a minor (the 4.8.0 precedent), so it belongs in R2 or in a
  minor of its own. The schema's sentence that `keyword` is null "for a tracked assertion or a sub-step"
  (`ReportGenerator.cs:6688`) changes, and the hand-built keyword-less sub-step in `TestRunReport.pin.json`
  (`:225-241`) either gains the field or is re-pinned.
- The glyph that ingested assertion steps carry in their text (F17) is left as it is; the field makes it harmless to
  the query.

### 3.11 What a reader sees (R2)

| Statement | 4.9.0 | R2 (diagram note; the step list shows the label without the glyph) |
|---|---|---|
| `result.ShouldBe(expected)`, result 3, expected 5 | nothing | `✗ Result should be '5'` and Shouldly's message under it |
| `name.ShouldNotBeNull()` | nothing | `✓ Name should not be null` |
| `flag.ShouldBeTrue()` | nothing | `✓ Flag should be true` |
| `items.ShouldNotBeEmpty()` | nothing | `✓ Items should not be empty` |
| `order.ShouldSatisfyAllConditions(() => order.Id.ShouldBe(2), …)` | nothing | `✗ Order id should be 2`, one row per condition, and `✗ Order should satisfy all conditions` (Q4) |
| `Should.Throw<InvalidOperationException>(() => …)` | nothing | `✓ Should throw InvalidOperationException` |

The report gains its Assertions toggle when the first note is drawn, as it does for FluentAssertions.

### 3.12 Kronikol4J

Java has no Shouldly. R1's two label fixes (F11, F13) change report output, so each takes a ledger line, recorded as
agreement or divergence once Kronikol4J's AssertJ labels are checked for the same two cases. R2 adds labels for a
.NET-only library, and its ledger line says so. Kronikol4J's own assertion gap is #127.

## 4. Tests, in the order they are written

Weaver facts go in `tests/Kronikol.Tests.AssertionTracking`. `TestAssemblyBuilder` gains a reference to the real
Shouldly 4.3.0 assembly beside FluentAssertions and AwesomeAssertions (a package reference in the test project), and
`Fixtures/WeaverFixture.csproj` gains Shouldly for the real-SDK builds. Stubs would not do: Shouldly's message reads
the test's source line, which is what §3.3 must keep. Every weaver theory runs in Debug and Release.

### 4.1 New facts

R1:

| # | Fact | File | Red on 4.9.0 |
|---|---|---|---|
| 1 | `The_task_weaves_an_assembly_whose_pdb_is_embedded` (the task run with a recording `IBuildEngine`, the first test of the task itself, F19) | new `WeaveAssertionsTaskTests.cs` | yes, skipped |
| 2 | `The_task_weaves_a_portable_pdb_as_before` | same | no, a guard (M14) |
| 3 | `A_lambda_condition_on_its_own_line_is_labelled_by_its_body` (the control's `SatisfyRespectively`: `✓ First should be 1`) | `AssertionWeaverTests.cs` | yes, `First => first should be 1` |
| 4 | `A_statement_alone_on_its_line_reads_as_before` | same | no, a guard (M10) |
| 5 | `A_bang_in_a_string_literal_survives_the_label`, and `x != y` keeps its `!` | `AssertionExpressionFormatterTests.cs` | yes, `"Hi"` |
| 6 | `A_null_forgiving_bang_is_still_dropped` | same | no, a guard (M11) |
| 7 | `An_unquoted_failure_has_an_expected_and_actual` (theory: Shouldly's int, bool, enum, decimal, `null` and multi-line string; FluentAssertions' `Expected result to be 5, but found 3.`) | `ErrorDiffParserTests.cs` | yes, no match |
| 8 | `Two_Shouldly_failures_on_one_subject_cluster_apart`, and two identical ones together | `FailureClusterKeyTests.cs` | yes, one cluster |
| 9 | `A_failed_assertion_in_an_AssertionScope_is_drawn_failed` (FluentAssertions and AwesomeAssertions; if Q7 finds a way) | `AssertionWeaverTests.cs` | yes, two green notes |
| 10 | `The_package_description_names_the_attribute_and_the_libraries` | `tests/Kronikol.Tests` packaging facts | yes, `TrackAssertionsBeta` |

R2:

| # | Fact | File | Red on R1 |
|---|---|---|---|
| 11 | `A_Shouldly_only_assembly_is_woven` | `AssertionWeaverTests.cs` | yes, skipped |
| 12 | `Each_Shouldly_shape_runs_woven` (theory, Debug and Release): the eleven probe cases, plus `ShouldBeOfType<T>()` whose result is used, `ShouldContain(x => …)`, `ShouldAllBe(x => …)`, a parameter, a generic `T`, a struct, a record built with `with`, `ShouldHaveFlag` (another namespace), `DynamicShould.HaveProperty`, inside `lock`, `try`/`finally`, a `switch` arm and a ternary argument, an `async` method with and without an earlier `await`, `task.ShouldThrowAsync<T>()`, and an `await` inside an argument (left unwoven) | same | yes, nothing woven |
| 13 | `A_woven_Shouldly_message_equals_the_unwoven_one` (the same shapes, Debug and Release; with Q6, on 4.3.0 and on 5.0's preview) | same | no: unwoven it is trivially equal. Proved by M5, red in Release |
| 14 | `Each_Shouldly_case_records_its_outcome` (one note per statement, the right colour, the label of §3.11) and `A_Shouldly_failure_note_carries_Shouldlys_message` (the first fact on a failure note's content, F19) | same | yes |
| 15 | `A_subject_left_on_the_stack_is_spilled` (the P5 IL, as a crafted method) and `The_stack_analysis_types_each_slot` (dup, `newarr`, a generic call's return, `ldnull` joined with a reference, a catch handler's start) | new `StackAnalysisTests.cs` | yes, no spill |
| 16 | `A_statement_the_analysis_cannot_type_is_left_unwoven_and_counted` | same | yes |
| 17 | `Every_woven_fixture_verifies` (ILVerify over every fixture source in the project, FluentAssertions, AwesomeAssertions, the TUnit stubs and Shouldly, Debug and Release; an error the unwoven assembly has is not counted) | new `WovenIlVerifiesTests.cs` | yes, for Shouldly |
| 18 | The cross-SDK sources again with Shouldly (seven sources, SDKs 8, 9, 10 and 11, Debug and Release: 56 facts) | `CrossSdkWeaverTests.cs` | yes |
| 19 | `Shouldly_values_are_the_arguments_after_the_subject`, `A_custom_message_is_not_captured`, `A_condition_lambdas_closure_value_is_captured` | `AssertionWeaverTests.cs` | yes |
| 20 | `Shouldly_labels_read_as_sentences` (theory over method-name families, `Should.Throw<T>`, `ShouldThrow<T>`, a generic argument, substituted values, a `customMessage` left out) | `AssertionExpressionFormatterTests.cs` | yes, raw code |
| 21 | `Track_That_with_Shouldly_resolves_values` | `TrackThatTests.cs` | yes, none (F10) |
| 22 | `A_ShouldlyMethods_helper_is_one_row` and `Its_body_is_not_woven` (Q5) | `AssertionWeaverTests.cs` | yes |
| 23 | `A_users_ShouldBe_method_outside_Shouldly_is_not_an_assertion` | same | no, a guard (M2) |
| 24 | `The_task_warns_when_the_attribute_finds_no_supported_library` (the code, and a message naming the libraries and `Track.That`), `…when_the_build_has_no_symbols`, `…when_no_statement_calls_an_assertion` | `WeaveAssertionsTaskTests.cs` | yes, a Low message |
| 25 | `No_warning_without_the_attribute`, `No_warning_when_something_is_woven` | same | no, guards (M16) |
| 26 | `A_Shouldly_assertion_note_paints_its_sentence` (Playwright: a report whose run recorded a Shouldly assertion draws the Assertions toggle and, with it on, the note's sentence and Shouldly's message) | `tests/Kronikol.Tests.EndToEnd` | yes, raw code |
| 27 | If Q1: `An_assertion_sub_step_is_marked_in_the_data_file` (in-process, both ingest events), `A_step_without_a_keyword_is_not_an_assertion` (`[Step]`, an ingested step, a bridged NScenario-style step), `A_report_without_the_field_keeps_the_inference`, `A_report_without_assertions_is_written_as_before` | `tests/Kronikol.Tests` (Query, Ingestion, Reports) | yes, for the first two |

Facts 1, 2, 24 and 25 drive the task through a recording build engine, so they cover what no test covers today: the
task's own branches and their message importance.

### 4.2 Existing facts

- **The 24 Release theories, the 56 cross-SDK facts and every value-capture test** must stay green across §3.2,
  which rewrites the code they exercise. They are the reason the stack analysis is safe to land.
- **`AssertionExpressionFormatterTests.cs:33-38`** pins the raw fallback for an expression without `.Should().`. Its
  sample is `Assert.True(value)`, which stays raw, so it stays green; fact 20 is the Shouldly side.
- **`ErrorDiffParserTests.cs:83-96`** (a quoted Shouldly string) stays green; fact 7 widens it.
- **`TestRunReport.pin.json`** stays byte for byte if #145's field is omitted when false (§3.10).
- **Any fact that pins a FluentAssertions label from a lambda on its own line** changes with fact 3; S1 greps the test
  projects for such labels before the change.

### 4.3 Mutations, one per behaviour

| # | Mutation | Turns red |
|---|---|---|
| M1 | the fast path without `Shouldly` | 11 |
| M2 | an entry point by the `Should` prefix alone | 23 |
| M2b | an entry point by namespace `Shouldly` and the `Should` prefix, without the marker | 12 (`ShouldHaveFlag`, `HaveProperty`) |
| M3 | the early return for a first instruction that pops nothing, restored | 12 (Release), 15, 17 |
| M4 | every spilled slot typed `object` | 15, 17 |
| M5 | the sequence point left on the statement's first original instruction | 13 (Release) |
| M6 | values scanned after the entry call again | 19 |
| M7 | the `customMessage` argument, or a `[CallerArgumentExpression]` one, captured | 19 |
| M8 | the formatter's Shouldly branch removed | 14, 20, 26 |
| M9 | `ClosureValueResolver` without Shouldly | 21 |
| M10 | whole lines read again | 3 |
| M11 | every `!` removed again | 5 |
| M12 | `ErrorDiffParser` quoted-only again | 7 |
| M13 | the cluster key back to the first line | 8 |
| M14 | the task requiring a `.pdb` file again | 1 |
| M15 | the warning logged at `Low` | 24 |
| M16 | the warning logged without the attribute | 25 |
| M17 | `[ShouldlyMethods]` helpers woven inside | 22 |
| M18 | #145's field written but not read, or read but not written | 27 |

### 4.4 Proofs before each tag

- **Red on the previous release.** R1's facts in a worktree at v4.9.0, R2's in one at R1's tag, with any new internal
  helper stubbed so they compile, and each failure's message read: a fact can be red for the wrong reason.
- **Mutations** in a `git stash create` snapshot worktree, each file backed up rather than reverted by
  `git checkout`, so uncommitted release work survives.
- **Every build the weaver meets.** The weaver suite on net8.0, net9.0 and net10.0 in Release, as CI runs it; the
  cross-SDK facts on every SDK installed here (8.0.4xx, 9.0.3xx, 10.0.300), with the 11 preview on CI only if it is
  installed there; and ILVerify over every fixture.
- **Suites and build.** The core suite, the weaver suite, the full Playwright suite for fact 26, and `release.slnf`
  built and packed in Release for every target framework.
- **The harness probe on the new packages** (`-p:KronikolVersion=<new>`), Debug and Release: 11 of 11 cases run,
  each records the note §3.11 gives, and every Shouldly message equals the unwoven build's. The control's cases change
  only where R1 fixes them.
- **A real test project, the issue's stack:** a scratch net8.0 xUnit 2.9.3 project with `Kronikol.xUnit2`,
  `Kronikol.AssertionTracking` and Shouldly 4.3.0 on local packages, Debug and Release. `kronikol query assertions`
  lists the Shouldly rows (and, with Q1, no step that is not an assertion), and `kronikol query failures` shows the
  failing case's Shouldly message and, after R1, its Expected and Actual. The report itself is read only through the
  query tool.
- **The owner's suite** (the owner's to run, or to hand over): with no change to the suite, the build prints
  `Instrumented N assertion(s)` with N near its 447 calls (calls inside helpers and lambdas count where they are), no
  warning, all 52 scenarios pass in Debug and in Release, and the report has the notes and the toggle.

## 5. Releases and bumps

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| R1 | F4 (embedded PDBs), F11 (the statement's columns), F13 (`!`), F14 (unquoted values, Shouldly's and FluentAssertions'), F15 (Shouldly's cluster key), F12 (`AssertionScope`, if Q7 finds a way), F18 (the package description, the task's doc comment, the wiki's corrections) | patch: 4.9.1, or the next free patch | Bug fixes and corrected metadata. No member, option or format field is added. The changelog names each change to a label |
| R2 | Shouldly (§3.1 to §3.8), #144's warning (§3.9), Q4 and Q5 as answered, #145 if Q1 takes it (§3.10) | minor: 4.10.0, or the next free minor | A newly supported library and the repo's first coded build warning are new surface, and #145's field is a new field in a published format |

Steps:

- **S0, done (RUN):** the repro, the control and the prototype (§2.1 to §2.3, §2.7).
- **S0b, partly done (RUN):** F14 and F15 on a real digest, and the warning's behaviour (`harness/s0b/`). Still to
  measure before R1: the `AssertionScope` API across FluentAssertions 6, 7 and 8 and AwesomeAssertions 8 and 9 (Q7); Kronikol4J's labels for
  F11's and F13's cases (§3.12). (Shouldly 5's preview was inventoried with 4.3.0: §2.6.)
- **S1:** R1's facts (1 to 10), each run red on v4.9.0.
- **S2:** §3.6, the formatter's `!`, `ErrorDiffParser`, the cluster key (a Shouldly message is keyed on its lines up
  to the first value, `result should be`, so the subject and the check together pick the group), the task's
  symbols, and F12 if Q7 allows.
- **S3:** R1's documentation (§6) and the release.
- **S4:** the stack analysis (§3.2) and the ILVerify net, first and on their own: they change the code every
  FluentAssertions and TUnit user runs, so the existing Release theories and cross-SDK facts must be green on it before
  Shouldly lands on top (rule 3).
- **S5:** R2's facts (11 to 27), each run red on R1.
- **S6:** recognition, the sequence point, values (§3.1, §3.3, §3.4, §3.8).
- **S7:** labels and `ClosureValueResolver` (§3.5), and `ShouldSatisfyAllConditions` as Q4 is answered (§3.7).
- **S8:** #144's warning (§3.9), then #145 if Q1 takes it (§3.10).
- **S9:** R2's documentation, the proofs (§4.4) and the release.

Draft of R1's changelog entry:

> **Fixed.** Assertion tracking skipped a project that embeds its PDB (`<DebugType>embedded</DebugType>`), saying so
> only at detailed build verbosity, so none of its assertions were drawn; the weave now reads and writes embedded
> symbols. An assertion in a lambda on its own line, such as a `SatisfyRespectively` condition, was labelled with the
> lambda's parameter and arrow (`First => first should be 1`); a label is now read from its statement's own columns
> (`First should be 1`). A `!` in a string literal was dropped from labels (`.Be("Hi!")` read `should be "Hi"`), and
> `!=` read as `=`; only the null-forgiving `!` is removed now. A failure whose values were not strings got no
> Expected and Actual in the report, `Failures.md` or `Failures.jsonl`, whether Shouldly's (`should be⏎5⏎ but was⏎3`)
> or FluentAssertions' (`Expected result to be 5, but found 3.`); both shapes are read now. `Failures.md` grouped
> Shouldly failures by their subject alone, so unrelated checks on one variable read as one cause; a Shouldly
> message is now grouped by its subject and its check.
> The package's description named only FluentAssertions and the old `[assembly: TrackAssertionsBeta]`; it names
> `[assembly: TrackAssertions]` and the libraries the weaver reads. Patch: bug fixes, nothing new to call.

(Plus F12's sentence if Q7 takes it in.)

Draft of R2's changelog entry:

> **Added.** Assertion tracking instruments Shouldly (#141). Each of Shouldly's `Should*` assertions (`ShouldBe`,
> `ShouldNotBeNull`, `ShouldBeTrue`, `ShouldNotBeEmpty`, `ShouldSatisfyAllConditions` and the rest) and
> `Should.Throw`, `Should.ThrowAsync` and their kin is drawn as an assertion note and a step-list sub-step, labelled
> as a sentence (`Result should be '5'`) with the values of the variables it compares; a failure carries Shouldly's
> own message, which the weave leaves unchanged in Debug and Release builds. The weave warns (`<code>`, Q2) when
> `[assembly: TrackAssertions]` is declared and nothing is instrumented: no supported library is referenced, the build
> has no symbols, or no statement calls an assertion (#144). It fails a build run with `-warnaserror` or
> `MSBuildTreatWarningsAsErrors`; `<NoWarn>` silences it. **Changed.** Before it wraps a statement, the weaver now works out exactly what the
> evaluation stack holds, which a Release build of a Shouldly assertion needs (the compiler keeps the subject on the
> stack between statements); a statement it cannot account for is left unwoven and counted, never miscompiled.
> `Track.That(() => x.ShouldBe(y))` shows its values. Minor: a newly supported library and a new warning code.

The per-release checklist, as the last plans ran it:

- **Versions:** every package (`Directory.Build.props`, `plugin.json`, `marketplace.json`, the history action's
  `VERSION`). Template pins stay strictly behind the repo's version, moved only once nuget.org lists every pinned id.
- **Numbers:** other sessions are planning releases today (#132, #134). Claim each number by `SendMessage` before
  tagging, keep a placeholder (`4.9.R1`, `4.9.R2`) in comments until then, and release R2 only after R1 lists on
  NuGet.
- **Commit messages:** close #141 and #144 (and #145 with Q1) with `gh issue close` after publication; never write
  "fixes #N" or "closes #N" in passing; never quote the bracketed skip-CI marker.
- **After each push:** the CI, CodeQL and Summary Preview runs of every pushed SHA, and the "Assertion Tracking Tests"
  job's log for the new facts by name (`release.yml` does not run that project).

## 6. Documentation

| Where | Change | Release |
|---|---|---|
| Wiki `Assertion-Tracking.md:13`, `:15` | Shouldly among the libraries, with both of its shapes | R2 |
| Same page, `:161`, `:178`, `:197`, `:212` | value capture per library: the assertion method's arguments for FluentAssertions, the arguments after the subject for Shouldly | R2 |
| Same page, `:222` | a Shouldly example beside the awaited FluentAssertions one | R2 |
| Same page, `:240`, `:247`, `:322-335` | Shouldly leaves the "not detected" list; the `Track.That` Shouldly example says it is no longer needed, and that it now shows values | R2 |
| Same page, `:377-389` | the formatter's table gains Shouldly's sentences | R2 |
| Same page, a new "Troubleshooting" section | the `Instrumented N assertion(s)` line at `-v:n`; embedded PDBs (R1); `DebugType=none`; the warning, its code, `<NoWarn>`, and that `-warnaserror` makes it an error (R2) | R1, R2 |
| Same page, `:236` and `:24` | the templates stopped shipping assertion tracking in 2.37.1; install the current version, not 2.31.0 | R1 |
| `Project-Templates.md:9`, `:142` | the same correction (`:80` is already right) | R1 |
| `API-Reference.md:79` | `ErrorDiffParser`'s Shouldly shapes include unquoted values | R1 |
| `Step-Tracking.md:218-240` | check its `.Should()` example and its requirement lines against R2 | R2 |
| `Querying-Reports.md:477-478`, `:525-536` | `query assertions` reads the step's field | R2, if Q1 |
| `README.md:18`, `:298`; `templates/README.md:91`, `:104` | Shouldly named among the libraries | R2 |
| `Kronikol.AssertionTracking.csproj:8` (the nuget.org description); XML docs `WeaveAssertionsTask.cs:11-14`, `AssertionWeaver.cs:14`, `:357`, `:647-648`; `Attributes/TrackAssertionsAttribute.cs:12`; comments in `build/Kronikol.AssertionTracking.targets:3`, `:45` | the attribute's real name and every library (Shouldly from R2) | R1, R2 |
| `AssertionExpressionFormatter.cs:8` (XML doc) | "FluentAssertions and Shouldly" | R2 |
| `CHANGELOG.md` | §5's entries, folding in any `[Unreleased]` section | R1, R2 |
| Kronikol4J's ledger (`docs/REMAINING_PARITY.md`) | §3.12 | R1, R2 |

Before each release, grep the wiki for `FluentAssertions`, `.Should()`, `TrackAssertions` and `Shouldly` for any page
this table missed, and the skill references under `.claude/skills/` and `templates/skills/` for `query assertions`.

## 7. Where it sits in the roadmap

- **R1:** stage 1, the patch train, by rule 1. A failed assertion drawn as a green pass (F12) is the worst kind of
  defect a report can have: it says the opposite of the run. The next free stage 1 row at green-light, claimed by
  `SendMessage` when the owner places it: other sessions are placing plans today (#135's took row 1.21 and D37 on
  2026-10-07, and #132 and #134 are in flight). No decision number unless a question needs one.
- **R2:** breadth for the bar (rule 9: a visitor whose library is missing writes the product off), and the owner's
  placement. By rule 5 it follows R1, which edits the same files (`AssertionWeaver.cs`, the formatter).
- **#145:** in R2's row if Q1 takes it; otherwise its own row, a minor for its field.
- **Files:** `src/Kronikol.AssertionTracking/`, `src/Kronikol/Tracking/AssertionExpressionFormatter.cs` and
  `ClosureValueResolver.cs`, `src/Kronikol/Reports/ErrorDiffParser.cs` and the cluster key, and with Q1
  `src/Kronikol/Query/ReportIndex.cs`, `StepCollector.cs` and the ingest synthesizers. The plans in flight today
  (#132 in `src/Kronikol.xUnit2`, #134 in the gRPC extension) share none of them.

## 8. Found on the way, not in this plan

| # | Finding | Level | Recommendation |
|---|---|---|---|
| 1 | F20. The source rewriter has been unpublished since 2.31.0 but is still in `Kronikol.sln` (`:226`, `:228`), and CI's remainder job runs its tests | READ | Delete the project and its tests in a repo-only commit (no bump), or say in its csproj that it is not published |
| 2 | TUnit's `Assert.That(x).IsEqualTo(y)` rows are labelled with raw code and drop their values, for F9's reason | READ | A formatter branch for TUnit's shape once R2 has set the pattern. Whether a better label is a fix or a feature is the owner's call |
| 3 | `WeaveResult.DiagMessages` is collected (`AssertionWeaver.cs:364`) and never logged | READ | Log it at `Low` beside R2's "left unwoven" count, or delete it |
| 4 | An interactions-feed `kind: "assertion"` record draws a note with no source comment and no step (`InteractionRecord.cs:381`, `:445-453`), where the tests feed's `assertion` event draws both | READ | With #145's field, or a patch of its own |
| 5 | The formatter turns `?.` into `.` (`AssertionExpressionFormatter.cs:49`); the probe's `order?.Name.ShouldBe("a")` reads `order.Name…` | READ, RUN | By design (the label is English, not code); left |

## 9. What is not known

- How many of the owner's 447 calls sit in shapes §4.1 does not cover, and whether the suite builds Release in CI.
  The acceptance run answers both.
- Whether FluentAssertions' messages drift today wherever a spill is inserted (F7's mechanism, INFERRED for
  FluentAssertions). R2's message facts cover its spilling shapes.
- What the stack analysis costs on a large assembly. The weave took 45 ms on the owner's suite and 206 to 478 ms on the
  probe's cold builds; S4 times it on the largest fixture and on the owner's suite.
- Whether FluentAssertions and AwesomeAssertions expose a stable way to see a scope's new failures (S0b, Q7).
- When Shouldly 5 leaves preview, and whether its final API keeps the preview's shape (Q6).

## 10. Risks and traps

1. **Release builds.** The prototype passed every Debug case and failed 9 of 11 in Release. Every Shouldly fact runs in
   Release as well, and the ILVerify net reads every woven method. A green Debug run proves nothing about the weave.
2. **The stack analysis is shared code.** It changes what every FluentAssertions and TUnit user runs, so it lands
   first and alone (S4), with their Release theories and the cross-SDK facts green on every installed SDK.
3. **The warning fails `-warnaserror` builds** (RUN; the `TreatWarningsAsErrors` property alone does not). It ships in
   the minor, with a code, and the changelog and the wiki say how to silence it.
4. **Shouldly reads the source line.** A fixture with two statements on one line reads a different subject: the
   probe's first cut put whole methods on one line and Shouldly quoted `public static void` as the subject. Fixtures
   keep one statement per line, and the message facts compare woven and unwoven builds of the same file.
5. **A label change is a report change.** R1's two label fixes alter text that a golden file or Kronikol4J might pin:
   S1 greps the tests for affected labels, and each takes a ledger line.
6. **Release numbers.** Other sessions are planning releases today. The first to land takes the number; comments
   carry a placeholder until then.
7. **Process-wide state.** The weaver facts load woven assemblies into the test process and read
   `RequestResponseLogger`; they stay in the project's existing collections, and the Playwright fact goes through
   `ReportTestHelper`'s whole-pipeline path.
8. **Reading reports.** Acceptance reads reports through `kronikol query`, never by opening `TestRunReport.json` or
   the HTML.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Take #145 into R2 | **Yes.** R2 makes `kronikol query assertions` worth reading for a Shouldly suite, and in the issue's suite it would list 17 NScenario steps beside the real rows. The field is small, and a minor wherever it lands |
| Q2 | #144: a warning or a message, and its code | **A warning with a code, in R2** (§3.9). The repo has no coded warnings yet, so the first sets the series; `KRONIKOL001` is the plain choice. Measured: `<NoWarn>` silences it, and only `-warnaserror` or `MSBuildTreatWarningsAsErrors` make it fail a build. The alternative is a `High` message: visible at the default verbosity and never failing a build, but not suppressible by code |
| Q3 | Capture the subject's value as well | **No**, for parity with FluentAssertions: a failure's message already names the subject's value. If wanted, change both libraries in one later release |
| Q4 | `ShouldSatisfyAllConditions`' rows | **A: a row per condition and one for the outer call**, as FluentAssertions draws `SatisfyRespectively` today. B is the conditions alone, C the outer call alone |
| Q5 | A suite's own `[ShouldlyMethods]` helpers | **Yes:** a call to one is one row, and its body is not woven, as Shouldly's own methods are not |
| Q6 | Shouldly 5 (in preview) | **Support 4.x now, and let the facts run on 5.0's preview too.** The marker rule already covers its 18 types and 204 methods (§2.6, RUN); what differs is its `[CallerArgumentExpression]` parameter, which §3.4 skips. A 5.x lane in the weaver's tests costs one more package version in the theories |
| Q7 | F12, an assertion inside an `AssertionScope` | **Fix it in R1** if S0b finds a public, version-stable way to see that the scope gained a failure during the statement (then draw ✗ with the scope's new message). Otherwise draw no note inside a scope rather than a green one, also a patch, and file the full fix as its own issue |
| Q8 | ILVerify as a test dependency | **Yes**, in the weaver's test project only; nothing ships with it |
| Q9 | R1 and R2 as two releases | **Yes, in that order.** R1 fixes live defects for every FluentAssertions user (rule 1) and is small; R2 rewrites shared weaver code and should not hold them |
| Q10 | The source rewriter | **Out of scope**; §8 item 1 recommends removing it |

## Appendix A. Edit sites

| File | Release | Change |
|---|---|---|
| `src/Kronikol.AssertionTracking/WeaveAssertionsTask.cs:11-14`, `:52-57`, `:76-81` | R1, R2 | the doc comment (R1); symbols without a `.pdb` file (R1); the warning (R2) |
| `src/Kronikol.AssertionTracking/AssertionWeaver.cs:30-156` | R1, R2 | embedded symbols read and written back (R1); the count of statements left unwoven (R2) |
| Same file, `:216-228`, `:637-658` | R2 | the Shouldly reference, namespace and entry point |
| Same file, `:660-742` | R1 | the statement's columns |
| Same file, `:786-1228` | R2 | the Shouldly capture window |
| Same file, `:1657-1836`, `:1923-1978`; a new `StackAnalysis.cs` beside it | R2 | the stack analysis replaces the spill heuristics |
| Same file, `:1980-2337`, `:2347` onwards | R2 | the sequence point on the first inserted instruction |
| `src/Kronikol.AssertionTracking/Kronikol.AssertionTracking.csproj:8`, `Attributes/TrackAssertionsAttribute.cs:12`, `build/Kronikol.AssertionTracking.targets:3`, `:45` | R1 | the description and comments |
| `src/Kronikol/Tracking/AssertionExpressionFormatter.cs:8`, `:46`, `:54-57` | R1 (`!`), R2 (Shouldly) | |
| `src/Kronikol/Tracking/ClosureValueResolver.cs:90-95` | R2 | |
| `src/Kronikol/Reports/ErrorDiffParser.cs:25`, `:27-28` | R1 | unquoted values, both shapes |
| `src/Kronikol/Reports/FailureClusterer.cs:20`, `FailuresDigestGenerator.cs:494`, `HistoryRunBuilder.cs:101` | R1 | a Shouldly message's cluster key |
| `src/Kronikol/Tracking/StepCollector.cs:204-232`, `src/Kronikol/Query/ReportIndex.cs:212-213`, `src/Kronikol/Reports/ReportGenerator.cs:5110-5148`, `:6688`, `FeatureSynthesizer.cs:414-429`, `CucumberFeatureMerger.cs:208-219` | R2, if Q1 | #145's field |
| `tests/Kronikol.Tests.AssertionTracking/` (`TestAssemblyBuilder.cs`, `Fixtures/WeaverFixture.csproj`, the csproj, new test files) | R1, R2 | §4.1 |
| `tests/Kronikol.Tests/` (the formatter, `ErrorDiffParserTests.cs`, `FailureClusterKeyTests.cs`, `TrackThatTests.cs`, packaging, Q1's facts) | R1, R2 | §4.1 |
| `tests/Kronikol.Tests.EndToEnd/` | R2 | fact 26 |
| `CHANGELOG.md`, the version files, the template pins | R1, R2 | the release |
| `../Kronikol.wiki` | R1, R2 | §6 |

## Log

- 2026-10-07: drafted from #141, with #144 and #145. S0 RUN on the published 4.9.0 packages (`harness/s0/`), the
  prototype RUN on a copy of the weaver (`harness/proto/`), and part of S0b (F14, F15 and the warning's behaviour,
  `harness/s0b/`). Shouldly 4.3.0 and 5.0.0-preview.2 inventoried (`harness/shouldly/`). Nothing in `src/` is
  changed; not green-lit.
