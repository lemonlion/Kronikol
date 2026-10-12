# Assertion notes show values the assertion never compared (#163)

**Date:** 2026-10-11 · **Repo version:** 4.14.6 (`main` at d59843fa; line numbers are for that commit, and
`src/Kronikol/Tracking/` is the same as at 98c4806d, the commit the issue cites) · **Status:** **green-lit in full
by the owner on 2026-10-12** ("implement the plan in full"), every question in §11 as recommended: Q1 the scalar
prefix, Q2 as written, Q3 taken as R2 (a minor), Q4 F7 to F10 in R1, Q5 the cuts left. Roadmap row 1.27. Executed:
R1 as 4.14.8, R2 as 4.16.0, both published 2026-10-12 (the Log).

Evidence labels: **RUN** (measured on 2026-10-11 with the published 4.14.6 packages, Shouldly 4.3.0 and
FluentAssertions 6.12.1 on net8.0, built by SDK 10.0.300, in Debug and Release; the probe and its saved output are in
[`ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/`](ASSERTION_VALUE_SUBSTITUTION_PLAN.harness/README.md)), **READ** (in
the source, tests, wiki or changelog at that commit), **INFERRED** (reasoned from two facts, stated by neither),
**ESTIMATED** (not measured; the step that would measure it is named).

The owner's issue, [#163](https://github.com/lemonlion/Kronikol/issues/163) (2026-10-11, found upgrading #132's
multi-host suite to 4.14.6):

> When an assertion's argument is a variable's member chain that ends in a method call, its note puts `'null'` in
> place of the chain, whatever the value. [...] Every one of these passes. The note says the expected value was null,
> so a passing assertion reads as one that compared against null, and a failing one would show the wrong expected
> value beside the library's message. In our suite, 48 of a run's 747 assertion notes read this way (6%).

It names the cause (`ResolveDottedPaths` keeps the method's name as a path segment, `WalkPropertyChain` answers null
for a segment it cannot find, `FormatValue(null)` writes `"null"`) and suggests telling "not found" from null, and not
reading a segment that a `(` follows as a property.

## TL;DR

- **The issue is right, and reproduces exactly (RUN, §2.1).** All five of its labels come out character for
  character on the published 4.14.6, in Debug and in Release. When the assertion fails, the note's label says the
  expected value was `'null'()` and Shouldly's message under it says it was `"Blue"`.
- **Its diagnosis is right but narrower than the defect (RUN, §2.3).** `'null'()` is one of five ways a note puts
  something in value quotes that is not the value the assertion compared. Of the probe's 40 woven statements, 8 show
  the value compared, and **23 show something else in its place**: 13 a `'null'` for a value that was not null (the
  issue's shape), 3 a property's value for a method call of the same name, 5 the value of a different expression (a
  list for its element, a whole record for one of its properties, an object for one of its members), and 2 a member's
  name quoted as if it were the value. The other 9 show code as written or an honest partial value. The issue's first
  suggestion on its own would turn `'null'()` into the whole `expected` record (§2.4).
- **The worst is not `'null'`.** `actual.Lines.Count(l => l == "x").ShouldBe(expected.Lines.Count(l => l == "x"))`
  reads `should be [ '2'(l => l == "x") ]`: the walk finds `List<T>.Count`, the property, where the code called
  `Enumerable.Count`, the method, which returned 1. A plausible wrong number is harder to catch than a visible `null`,
  and `Track.That` draws it too (§2.3, F3).
- **The underlying problem (READ, §2.4):** the value layer matches the statement's text against reflection with
  string rules and has no model of what that text evaluates to. A path stops wherever an identifier stops, so a
  method's name becomes a member; every failure to read becomes the value null; and the substitution replaces a key
  wherever it starts a token, so a shorter key lands inside a longer chain it never read. Two resolvers implement
  these rules twice, differently (`Track` for woven statements, `ClosureValueResolver` for `Track.That`), and one
  assertion run both ways drew two different labels in one run (F6). The history shows the cost: the "whole record
  inside the chain" garbling was fixed for `Track.That` in 2.29.5-beta and came back for woven statements in 2.30.12,
  when the weave got a resolver of its own (§2.8).
- **Found on the way, all in shipped code (RUN):** a Shouldly assertion whose argument holds a `?:`, `??` or `?.`
  captures no value at all, since 4.14.0 (F7); `[SuppressAssertionTracking]` does not reach lambdas, local functions
  or the async methods of a suppressed class, though the wiki says it skips "all assertions within" (F8); subject
  labels rewrite string literals (`"Colour".ShouldBe(…)` reads `" colour" should be`, F9); and Shouldly and TUnit
  labels keep `to string()` where FluentAssertions' drop it, while FluentAssertions' rule also rewrites a quoted value
  that contains those words (F10).
- **R1, a patch (§5):** one reader of an argument's access chains, shared by both paths, that reads members only up to
  the first call or index, tells "could not read" from null, and substitutes a value only in place of exactly the code
  it was read for; a member's name is never quoted as a value; and F7 to F10. The rule a reader can rely on afterwards:
  **a quoted value in a note is the value of exactly the code it replaces, or there is no quoted value.**
- **Not in R1 (Q3, a minor if taken):** the weave could pass each argument's evaluated value, so
  `expected.Colour.ToString()` reads `'Blue'`, the value the assertion actually compared, with no reflection after
  the fact. It needs no new public member: the value travels in the arrays the weave already passes.

## 0. Findings

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **A method's name is read as a member, and a member that does not exist reads as null.** `ExtractDottedPath` stops at `(` but keeps the method's name as the last segment (`Track.cs:336-356`); `WalkPropertyChain` finds no property or field of that name and returns null (`:386`); `FormatValue(null)` writes `"null"` (`:183-184`); the formatter replaces the path and leaves the call's brackets: `'null'()`, `'null'("G")`, `'null'<int>()`, `'null'().Id`. 13 of the probe's 40 woven statements | RUN, READ | §2.1, §2.3 |
| F2 | **Every failure to read is the value null.** A getter that throws (`:373-374`, `:381-382`), a member not found (`:386`) and a null link reached by a plain `.` (`:367`), where the code itself would have thrown, all read as `'null'`. A fact pins the null link as `'null'` (`TrackValueResolutionTests.cs:248-264`) | RUN, READ | §2.3 |
| F3 | **A method that shares a property's name is read as the property.** `expected.Lines.Count(l => l == "x")` reads `'2'(…)`; the call returned 1. `Count()` reads `'2'()`, right only by coincidence. `ClosureValueResolver` does the same (`ClosureValueResolver.cs:265-299`, `:312-325`) | RUN | §2.3 |
| F4 | **A key is substituted where it only starts a longer chain.** `SubstituteResolvedValues` checks that the characters on either side are not identifier characters (`AssertionExpressionFormatter.cs:445-453`), so `expected.Lines` is replaced inside `expected.Lines[0]` (the list for its element), the root `expected` inside `expected.Plain` when `Plain` does not print (the whole record for one property), `expected.Inner` inside `expected.Inner!.Value` (the `!` ends the path in the raw text the resolver reads, and the formatter's cleaned text continues it), and `expected.Box.Inner.Value` inside a four-segment chain (`maxSegments: 3`, `Track.cs:319`) | RUN, READ | §2.3 |
| F5 | **A member's name is quoted like a value.** When nothing was substituted, `SimplifyMemberAccessPaths` turns a path of three segments or more, or one starting with `_`, into its last segment in quotes (`AssertionExpressionFormatter.cs:418-419`, `:475-520`). `value.ShouldBe(expected.Inner?.Value)` reads `should be 'Value'`, with the value `"v"`, and again with the value null; `Track.That`'s `plain.ShouldBeSameAs(expected.Plain)` reads `'Plain'` | RUN, READ | §2.3 |
| F6 | **Two resolvers, two rule sets.** The weave's values go through `Track.ResolveVariableValues` (three segments, the whole statement, not-found is null, computed expressions substituted, values cut at 100); `Track.That`'s through `ClosureValueResolver` (no depth limit, the arguments only, not-found is a fallback, computed expressions left, values cut at 50). In one run, `Track.That(() => actual.Colour.ToString().ShouldBe(expected.Colour.ToString()))` drew both `'null'()` (the weave reached the lambda, F8) and `expected.Colour.ToString()` | RUN, READ | §2.5 |
| F7 | **A Shouldly argument with a join captures nothing.** `actual.Id.ShouldBe(flag ? expected.Id : "none")`, `…ShouldBe(expected.Missing ?? expected.Id)` and `…ShouldBe(expected.Inner?.Value)` capture no variable, where FluentAssertions' `Be(expected.Missing ?? expected.Id)` reads `'null' ?? 'A1'`. `ShouldlyArguments` skips the `customMessage` argument's instructions from the end of the argument before it (`AssertionWeaver.cs:1509`), and when that argument's value comes from two paths its producer is unknown (`StackAnalysis.cs:99`), so the skip starts at the first value and swallows every argument. Since 4.14.0 | RUN, READ | §2.6 |
| F8 | **`[SuppressAssertionTracking]` misses code the compiler moves out of the method.** A lambda in a suppressed method is woven, `Track.That` in one draws two notes, and an async method in a suppressed class is woven. The weave checks a type's own attribute (`AssertionWeaver.cs:117`), and a state machine's parent method's (`:308-330`), but not the type that encloses a nested type, and not the method a lambda, a local function or a cached-lambda class was written in. The wiki: "Apply `[SuppressAssertionTracking]` to a method or class to skip all assertions within" (`Assertion-Tracking.md:110`) | RUN, READ | §2.6 |
| F9 | **Subject labels rewrite string literals.** `FormatSubject` splits the subject on every `.` and each part on case, then lower-cases it, literals included (`AssertionExpressionFormatter.cs:206-248`): `"Colour".ShouldBe(…)` reads `" colour" should be`, and `x.ToString("G").ShouldBe(…)` reads `x to string(" g") should be` | RUN, READ | §2.7 |
| F10 | **`to string()` is dropped from FluentAssertions labels alone, and from the whole sentence.** `Format` removes ` to string() ` after building the FluentAssertions sentence (`:73-74`); the Shouldly and TUnit branches return before it (`:52`), so `actual.Colour.ToString().ShouldBe(…)` reads `Actual colour to string() should be` where FluentAssertions reads `Actual colour should be`. Because the removal runs over the whole sentence, a value loses those words: `Be(note)`, with `note` `"call to string() now"`, reads `should be 'call now'` | RUN, READ | §2.7 |

## 1. How far each claim was checked

- **Every RUN finding was measured on the published 4.14.6 packages** with the probe in `harness/probe/`: a console
  app with no test framework that names a test per case through `Track.TestIdResolver` and reads each case's notes
  back from `RequestResponseLogger`, as SHOULDLY_ASSERTIONS_PLAN's probe did. 55 statements are woven
  (`s0/build-4.14.6-debug.txt`); the Debug and Release runs print the same notes (`s0/run-4.14.6-debug.txt`,
  `s0/run-4.14.6-release.txt`).
- **65 cases in five families:** `WovenShouldly` (31 statements, Shouldly 4.3.0), `WovenFluent` (9, FluentAssertions
  6.12.1, the issue's version), `ThatCases` (10, `Track.That`), `DirectCases` (10 calls to
  `Track.AssertionPassedWithValues` or `AssertionFailedWithValues` with the text and values the weave would pass, for
  shapes a woven statement cannot reach in a passing test: a getter that throws, a null root, TUnit's text, a value
  holding "to string()"), and `SuppressedMethods` with `SuppressedType` (5, F8).
- **Every code path is READ at d59843fa** with the line cited. The weave's capture rules were read in full by a
  research pass (§2.2).
- **The owner's suite** (48 of 747 notes) is the issue's and was not re-measured. Its acceptance is §4.4.

## 2. What the code does today

### 2.1 The repro on 4.14.6 (RUN)

The issue's five statements, in the probe's `Order` record (`Id`, an enum `Colour`, `List<string> Lines`), every one
passing:

| Statement | 4.14.6 note | Issue |
|---|---|---|
| `actual.Id.ShouldBe(expected.Id)` | `✓ Actual id should be 'A1'` | same |
| `actual.Colour.ToString().ShouldBe(expected.Colour.ToString())` | `✓ Actual colour to string() should be 'null'()` | same |
| `actual.Lines.First().ShouldBe(expected.Lines.First())` | `✓ First value of actual lines should be 'null'()` | same |
| `actual.Colour.ToString().ToLowerInvariant().ShouldBe(…)` | `✓ Actual colour to string() to lower invariant() should be 'null'().ToLowerInvariant()` | same |
| `actual.Colour.ToString().Should().Be(expected.Colour.ToString())` | `✓ Actual colour should be 'null'()` | same |
| `actual.Id.ShouldBe(splits.First().Id)` (the issue's `SplitId`) | `✓ Actual id should be 'null'().Id` | same shape |

And failing (the issue's "a failing one would show the wrong expected value"), `actual.Colour` being `Red`, the note
is the label and then Shouldly's message (the character table that follows it is cut here):

```
✗ Actual colour to string() should be 'null'()
actual.Colour.ToString()
    should be
"Blue"
    but was
"Red"
    difference
```

FluentAssertions' note reads `✗ Actual colour should be 'null'()` over `Expected actual.Colour.ToString() to be "Blue"
with a length of 4, but "Red" has a length of 3, …`.

### 2.2 The pipeline (READ)

1. **The weave captures roots, not arguments.** For each woven statement, `DetectCapturedVariables`
   (`AssertionWeaver.cs:1013-1465`) collects the locals, parameters, closure fields, state-machine fields and
   `this` fields whose names appear in the statement's text as whole words (`NameAppearsInExpression`, `:1692-1713`),
   scanning after the `Should()` call for FluentAssertions and TUnit and after the subject for Shouldly
   (`ShouldlyArguments`, `:1474-1514`). Each is passed by its name with its **root value** (`expected`, the whole
   record), boxed into two arrays **before** the statement runs (`:2353-2423`). Static fields and properties are
   never captured (no branch handles `ldsfld` or a getter call).
2. **`Track` walks the text after the statement has run.** `AssertionPassedWithValues` and `AssertionFailedWithValues`
   (`Track.cs:150-177`) call `ResolveVariableValues` (`:261-290`). For each name, `ResolveDottedPaths` (`:292-334`)
   finds every `name.` in the **whole statement** (subject included), takes up to three identifier segments after it
   (`ExtractDottedPath`, `:336-356`), walks them by reflection, property then field (`WalkPropertyChain`, `:358-390`),
   and stores `FormatValue` of the result under the path's text (`:181-250`). The root's own value is stored too. A
   null root resolves no path. Because the walk runs at log time, a property is read as it is after the assertion ran
   (INFERRED; Q3 removes this for arguments).
3. **The formatter substitutes into the arguments.** `AssertionExpressionFormatter.Format` (`:23-77`) cleans the code
   (`!` and `?.` removed, `CleanCode`, `:257-286`), splits the sentence by library, and formats the arguments
   (`FormatArgs`, `:405-428`): `SubstituteResolvedValues` (`:430-469`) replaces each key, longest first, wherever the
   characters on both sides are not identifier characters and it is not inside a literal, with `'value'`; an argument
   holding `=>` is wrapped in `[ … ]`; if nothing was substituted, `SimplifyMemberAccessPaths` (`:475-520`) quotes the
   last segment of a long or `_`-prefixed path; and `StripSimpleEnumPrefixes` (`:546-555`) reads `Status.Ok` as `Ok`.
4. **`Track.That` has its own resolver.** `ClosureValueResolver.ResolveValues` (`ClosureValueResolver.cs:21-81`)
   reads the delegate's closure fields, keeps those that appear in the **arguments**, and walks a dotted chain with no
   depth limit (`:265-299`, `:301-329`): a member not found is a fallback (nothing substituted), a null link resolves
   as null (`:310`), and a name next to an arithmetic operator is left as code (`:211-259`).
5. **The label is the step list's text and the query's too.** `LogAssertion` (`Track.cs:416-466`) writes the same
   formatted text as the assertion sub-step (`StepCollector.AddAssertionSubStep`), so the step list and
   `kronikol query assertions` show what the note shows. A report is written once: a report from 4.14.6 keeps its
   labels after an upgrade.

### 2.3 The probe's statements, by what the quoted value is (RUN)

Every row is a passing statement unless marked. "Compared" is the value the assertion compared.

| Group | Statement (argument) | 4.14.6 label (argument part) | Compared |
|---|---|---|---|
| **The value compared** (8) | `expected.Id`, `expected.Colour`, `expected.Lines.Count`, `expected.Field`, `expected.Missing` (null), `string.Concat(expected.Id)`, a local holding `expected.Colour.ToString()`, FluentAssertions' `expected.Id` | `'A1'`, `'Blue'`, `'2'`, `'f'`, `'null'`, `string.Concat('A1')`, `'Blue'`, `'A1'` | the same |
| **F1: `'null'` for a call** (13) | `expected.Colour.ToString()` (Shouldly, FluentAssertions, failing in both, inside `ShouldSatisfyAllConditions`), `expected.Lines.First()` (both), `….ToString().ToLowerInvariant()`, `expected.Lines.First().Length`, `expected.Id.ToString()`, `expected.Colour.ToString("G")`, `expected.Get<int>()`, `splits.First().Id` | `'null'()`, `'null'().Length`, `'null'("G")`, `'null'<int>()`, `'null'().Id`, `'null'().ToLowerInvariant()` | `"Blue"`, `"x"`, `"blue"`, `1`, `"A1"`, `"Blue"`, `0`, `"A1"` |
| **F3: a property for a call** (3) | `expected.Lines.Count()`; `expected.Lines.Count(l => l == "x")` (Shouldly and FluentAssertions) | `'2'()`; `[ '2'(l => l == "x") ]` | `2`; **`1`** |
| **F4: another expression's value** (5) | `expected.Lines[0]` (both libraries); `expected.Plain` (an object whose `ToString` is its type name; both libraries); `expected.Inner!.Value` | `'[ "x", "y" ]'[0]`; `'Order { Id = A1, Colour = Blue, Lines = …'` (the whole root); `'Inner(v)'.Value` | `"x"`; the `Plain` object; `"v"` |
| **F5: a member's name** (2) | `expected.Inner?.Value`, with `Inner` set and with it null | `'Value'`, `'Value'` | `"v"`; null |
| **Partial, but true** (4) | `expected.Box.Inner.Value.Length` (four segments); FluentAssertions' `expected.Inner?.Value` over a null; `expected.Code + 0`; `expected.Missing ?? expected.Id` | `'v'.Length`; `'null'.Value`; `'7' + 0`; `'null' ?? 'A1'` | `1`; null; `7`; `"A1"` |
| **As written** (5) | `nameof(expected.Colour)`, `$"{expected.Id}"`, a static field's `StaticExpected.Colour.ToString()`, Shouldly's `flag ? expected.Id : "none"` and `expected.Missing ?? expected.Id` (F7) | the code | |

`Track.That` (10 cases) and the direct calls (10) add:

| Path | Argument | Label (argument part) | Finding |
|---|---|---|---|
| `Track.That` | `expected.Lines.Count(l => l == "x")` | `[ '2'(l => l == "x") ]` | F3 |
| `Track.That` | `expected.Lines[0]` | `'[ "x", "y" ]'[0]` | F4 |
| `Track.That` | `expected.Plain` | `'Plain'` | F5 |
| `Track.That` | `expected.Colour.ToString()`, `expected.Lines.First()`, `expected.Code + 0` | as written | F6: the weave reads these differently |
| `Track.That` | `expected.Box.Inner.Value.Length` | `'1'` | F6: the weave reads `'v'.Length` |
| direct | `faulty.Throws`, a getter that throws | `'null'` | F2 |
| direct, failing with a `NullReferenceException` | `expected.Inner.Value`, `Inner` null | `'null'` | F2 (the code threw before comparing) |
| direct, failing likewise | `expected.Id`, `expected` null | `'null'.Id` | partial, true |
| direct | TUnit's `Assert.That(…).IsEqualTo(expected.Colour.ToString())` | `'null'()` | F1, in TUnit's branch |
| direct | `ids[0]` | `'[ "A1" ]'[0]` | F4 on a root |
| direct | `id.Trim()`, `plain.Name.ToUpper()` | `'null'()` | F1 on a one-segment path |
| direct | FluentAssertions' `Be(note)`, `note` holding `"call to string() now"` | `'call now'` | F10 |

### 2.4 Why: text rules with no model of the code (READ)

Every row in §2.3's middle groups comes from one of three rules, each correct for the shape it was written for
(`config.MaxRetries`) and wrong outside it.

- **A path is "identifiers joined by dots".** `ExtractDottedPath` cannot tell `Colour` (a member) from `ToString` (a
  method) because it does not look at what follows the last identifier. `ClosureValueResolver.ExtractDottedChain` has
  the same blind spot, and `List<T>.Count` beside `Enumerable.Count` shows that "not found" was never the only
  danger: when a method shares a property's name, the walk succeeds and reads the wrong thing (F3).
- **Null means "I have no value".** `WalkPropertyChain` returns `object?`, so "the value is null" and "I could not
  read it" share one answer, and `FormatValue` has no way to tell them apart (F1, F2).
- **A key matches where a token starts.** The substitution's boundary test is the character class, not the grammar:
  `.`, `(`, `[` and `<` all end a token, so `expected` is "a whole token" inside `expected.Plain`, and `expected.Lines`
  inside `expected.Lines[0]` (F4). The resolver reads the raw text, where `!` and `?` end a path, and the formatter
  substitutes into the cleaned text, where they are gone, so the two disagree about where a chain ends.

On top of them, **the formatter has a readability rule that turns code into something that looks like a value** (F5),
and **the rules live in two resolvers that disagree** (F6).

**The issue's two suggestions, checked against these rules (INFERRED from the code and §2.3's runs):**

- *"Tell not found from null, and resolve nothing for a path it can't walk"*, on its own, makes the issue's own case
  worse. With no key stored for `expected.Colour.ToString`, the root's key `expected` is still stored, and the third
  rule substitutes it inside the chain: the note would read `'Order { Id = A1, Colour = Blue, …'.Colour.ToString()`,
  the shape `expected.Plain` already shows (§2.3, F4). It also leaves F3 alone, since `Count` is found.
- *"Don't take a segment that a `(` follows as a property"* fixes F1 and F3, and with "resolve the property prefix"
  it is Q1's first option. It does not reach F2's getter, F4's index, `!` and depth cases, or F5.

So both are needed, and neither is enough without the third rule's fix (§3.3).

### 2.5 The two resolvers (READ, RUN)

| | Woven statements: `Track.ResolveVariableValues` | `Track.That`: `ClosureValueResolver` |
|---|---|---|
| Where it looks | the whole statement, subject included (`Track.cs:301`) | the arguments of the assertion call (`ClosureValueResolver.cs:83-128`) |
| Depth | three segments (`Track.cs:319`) | the whole chain |
| A member not found | `'null'` | nothing substituted, a fallback in `Track.DiagnosticLog` |
| A null link | `'null'` (pinned, `TrackValueResolutionTests.cs:248-264`) | `'null'` (`:310`) |
| A getter that throws | `'null'` | the exception escapes `ResolveValues` and is caught by `Track.ResolveClosureValues` (`Track.cs:409-413`): no value for any name |
| Next to `+ - * / %` | substituted (`'7' + 0`) | left as code (`:211-259`) |
| Value length | 100 (`Track.cs:179`) | 50 (`ClosureValueResolver.cs:13`) |
| Occurrences of a name | every `name.` in the statement | the first one followed by a dot (`:265-299`) |

### 2.6 Found on the way in the weave (RUN, READ)

**F7, a Shouldly argument with a join.** `ShouldlyArguments` (`AssertionWeaver.cs:1474-1514`) finds where each
argument's instructions start from the producer of the argument before it (`IndexOf(k - 1) + 1`, `:1509`), and for
`customMessage` it marks those instructions to skip, so a message is never read as a value. When the argument before
it is a `?:`, a `??` or a `?.`, its value reaches the call from two paths, the stack analysis records no single
producer (`StackAnalysis.cs:99`), `IndexOf` answers -1, and the skip starts at the first value: every argument's
instructions are skipped and nothing is captured. FluentAssertions' scan does not use the analysis and is not
affected. Every `ShouldBe` overload takes a `customMessage`, so this is every Shouldly assertion with a conditional
expected value; with a `?.` the label then falls into F5 (`'Value'`).

**F8, suppression.** The C# compiler moves a lambda's body into a nested class (`<>c__DisplayClass0_0`, or `<>c`
for one that captures nothing) as a method named after the one it was written in (`<U01_…>b__0`), an async method's
body into a nested state-machine type (`<U04_…>d__0`), and a local function into a method named
`<Outer>g__Name|0_0`. The weave walks every type, nested ones included (`GetTypes()`), and skips a type with the
attribute, a state machine whose parent **method** has it, and a method with it (`AssertionWeaver.cs:114-130`,
`:308-330`). So:

| Case | Expected | 4.14.6 |
|---|---|---|
| A suppressed method's own statement | no note | no note |
| A lambda in a suppressed method, run by the method | no note | **one note** |
| `Track.That(() => …)` in a suppressed method | one note (`Track.That`'s) | **two** |
| An async method in a suppressed class | no note | **one note** |
| A suppressed class's own statement | no note | no note |

### 2.7 Found on the way in the subject's words (RUN, READ)

The subject is not substituted, but it is rewritten into words, and two rules there are wrong in the same way as the
argument's: they treat code they do not model as words.

- **F9:** `FormatSubject` (`AssertionExpressionFormatter.cs:206-248`) splits on every `.`, then splits each part on
  case and lower-cases it. A string literal is code too: `"Colour"` becomes `" colour"`, `ToString("G")` becomes
  `to string(" g")`, and a literal holding a `.` would be split in two. The argument side keeps literals since
  SHOULDLY_ASSERTIONS_PLAN F13 (4.12.3); the subject side never did.
- **F10:** `result.Replace(" to string() ", " ")` (`:73-74`) runs only in the FluentAssertions branch and over the
  whole sentence, values included: a value `"call to string() now"` is drawn `'call now'`.

### 2.8 The tests today, and how the rules got here (READ)

- **`tests/Kronikol.Tests/Tracking/TrackValueResolutionTests.cs`** (the weave's runtime path): 15 `FormatValue_*` facts
  (`:23-117`), the substitution facts (`:126-195`) and four on dotted paths: `config.MaxRetries` reads `'5'` (`:213`),
  `options.Inner.MaxRetries` reads `'3'` (`:231`), the same with `Inner` null reads `'null'` (`:249`, the fact F2
  changes), and a key used twice (`:267`).
- **`ClosureValueResolverTests.cs`** (`Track.That`): dotted chains (`:280`, `:293`, `:342`), a missing member falls
  back (`:305`), and a null link reads `"null"` (`:319`, which F2 changes too, despite its name
  `Falls_back_when_property_chain_returns_null`).
- **`AssertionExpressionFormatterTests.cs`** (43 facts): the simplification of F5 is pinned three times (`:236`
  `_eggsSteps.EggsResponse.Eggs` → `'Eggs'`, `:244`, `:261`) and once more by a fact added in 4.12.3, `:371`, which
  pins `Be(map[key]!.Id)` as `Value should be 'Id'`: a member's name in value quotes. Longest key first (`:215`), no
  partial token (`:187`), no substitution in a literal (`:177`), values inside a lambda argument (`:269`), the
  `First()` subject (`:304`) and `to string()` in a FluentAssertions subject (`:336`) are pinned and stay.
- **`TrackThatTests.cs`** (24 facts): none has a dotted argument.
- **Woven** (`tests/Kronikol.Tests.AssertionTracking/`): `AssertionWeaverTests.cs` checks substrings of the PlantUML
  (`'42'`, `'null'` for a null local at `:870`, closure values); its only member chains, `expected.ExpectedCount`
  (`:525`) and `responseBody?.Substring(…)` (`:596`), check only that nothing throws. `ShouldlyWeaveTests.cs` pins exact
  labels (`:115`, `:250`, `:263-270`, `:282`), as do `Shouldly5PreviewTests.cs:100` and the Playwright
  `ShouldlyAssertionNoteTests.cs` (`:24`, `:68`).
- **No test anywhere has an argument with a method call after a member path, an index, a `?.` over a value, a getter
  that throws, a chain of four segments, or a member that does not print under a root that does.** That is how F1 to
  F5 shipped.

The history (`git log -S`):

| Version | Commit | Change |
|---|---|---|
| 2.28.37 | `f40ec48f` | `Track.That` resolves closure values; `SubstituteResolvedValues` is written |
| 2.29.5-beta | `980a0589` | `Track.That` walks dotted chains. The changelog names the defect it fixed: `expected.ExpectedIngredientCount` read `'MuffinBatchExpectation { ExpectedIngredientCount =...'.ExpectedIngredientCount`, the whole record inside the chain, which is F4. It also says the walk "falls back gracefully when properties don't exist or intermediate values are null"; the code reads an intermediate null as `"null"` |
| 2.30.12 | `07d29259` | The weave captures values, and `Track` gets **its own** resolver (`ResolveDottedPaths`, three segments, "segment not found" as null). F1 and F2 date from here, and F4 came back for every woven statement whose path does not print |
| 2.30.26 | `20864c38` | `SimplifyMemberAccessPaths` (F5): "`_steps.Response.Items` → `'Items'`" |
| 4.12.3 | `3ac6ac5c` | `LiteralMask` in the formatter (SHOULDLY_ASSERTIONS_PLAN F13) |
| 4.14.0 | | Shouldly values: "the arguments after the subject" (F7 dates from here) |

A defect class fixed in one resolver and reintroduced by the second is the strongest argument for one reader shared by
both (§3.2), held by a parity fact (§4.1 fact 7).

## 3. Design

### 3.1 The rule

**A note puts a value in quotes only for code whose value Kronikol read, and only in place of exactly that code. Code
it did not read stays as written.** Every change in R1 is this rule applied to one of §2.4's three text rules, to the
formatter's simplification (F5), and to the second resolver (F6).

### 3.2 One reader of access chains (R1)

A new internal class in `src/Kronikol/Tracking/` (`AccessChain`, about 200 lines, ESTIMATED) parses the chains that
start at a captured name, and both resolvers and the formatter use it.

- **A chain** starts at the captured name where it is a whole word, outside a literal (`LiteralMask`), not after a
  `.` (in `x.expected`, `expected` is a member, not the variable), and not in `nameof(…)`. It continues through
  segments: `.Name`, `?.Name`, a postfix `!` (no effect), a call `(…)` with an optional type argument list `<…>`
  before it, and an index `[…]`, with whitespace allowed before a `.` as in a multi-line statement. Brackets are
  balanced with the literal mask, as `ParseMethodAndArgs` already does.
- **Reading.** From the root's value, each member segment is read by property, then field, as today, until the first
  call or index segment or the chain's end. A member segment that a call follows (`Count(`, `ToString(`,
  `Get<int>(`) is a method's name and is **never read as a member** (F1, F3).
- **Three answers, not one.** Reading gives a value, null, or *unreadable*:
  - a member not found, or a getter that throws: unreadable (F2);
  - a null before a plain `.`: unreadable, since the code itself would have thrown;
  - a null before a `?.`: the chain's value is null, which is what C# gives it;
  - a value whose `ToString` is its type name: read, but it does not print (as today), so it is shown as nothing.
- **No depth limit.** A chain is read to its last member segment however long it is (the weave's three-segment limit
  is what made `'v'.Length`, F4); the statement's own text bounds it. `ClosureValueResolver` has never had a limit.
- **Canonical text.** A chain's key is its text with `?.` read as `.`, `!` removed and the whitespace before a dot
  dropped: the form `CleanCode` gives the formatter, so the key the resolver stores is the text the formatter sees.
  Today the resolver keys the raw text and the formatter matches the cleaned text, which is half of F4.

### 3.3 What is substituted (R1)

| The chain | Shown | Example |
|---|---|---|
| Read to its end, and the value prints | the value, in place of the whole chain | `expected.Id` → `'A1'`; `expected.Inner?.Value` with `Inner` null → `'null'` |
| Read up to a call or an index | **Q1.** Recommended: the read part's value when it is a scalar (a number, an enum, a string, a bool, a `Guid`, a date or time), then the rest as written; otherwise the chain as written | `expected.Colour.ToString()` → `'Blue'.ToString()`; `id.Trim()` → `'A1'.Trim()`; `expected.Lines.First()`, `expected.Lines[0]` and `splits.First().Id` as written |
| Unreadable, or read and does not print | the chain as written, and **no shorter key inside it** | `expected.Plain` stays `expected.Plain`, not the whole record |

- **Substitution by span, not by search.** The formatter finds the chains in the cleaned arguments with the same
  parser and replaces a chain's span (or its read part's span, per Q1) by its key. A key can no longer match where it
  only starts a longer chain (F4). Inside a literal, nothing is replaced, as today.
- **Why Q1 is recommended as a scalar rule.** `'Blue'.ToString()` tells the reader what was compared at a glance. A
  list or a record in that position is long and is not the value compared: `'[3 items]'.First().SplitId` says less
  than `splits.First().SplitId`. The rule keeps the first and drops the second. The alternatives are always the read
  part (the issue's second suggestion) and never (its first).
- **What stays as it is:** a root alone (`'A1'`), a value inside a call (`string.Concat('A1')`), an operand of an
  operator (`'7' + 0`, `'null' ?? 'A1'`), the 100-character cut, the collection forms (`[ "x", "y" ]`,
  `[11 items]`), and the enum prefix rule.

### 3.4 A member's name is not a value (R1, F5)

`SimplifyMemberAccessPaths` quotes the last segment of a path when nothing was substituted. Since a quoted word in a
note now means a value (§3.1), that form has to go. **Q2.** Recommended: show the code as written
(`_eggsSteps.EggsResponse.Eggs`); the alternatives are the last segment without quotes (`Eggs`), which still reads
like an enum value, or the subject's words (`eggs steps eggs response eggs`). Whatever is chosen, it applies only to
a chain with no value; after §3.2 that is a chain whose root was not captured (a static field, a property of `this`
that the weave does not read) or one that could not be read.

The rule is not on the wiki: the formatter's rules (`Assertion-Tracking.md:447-459` on `origin/master`) do not mention
it, and only the 2.30.26 changelog does. Four formatter facts pin it (§2.8), and they take Q2's form.

### 3.5 `Track.That` reads the same way (R1, F6)

`ClosureValueResolver` keeps what is its own (the closure's fields, the arguments as its scope, the fallbacks it
reports to `Track.DiagnosticLog`) and takes the chain reading, the three answers, the substitution and the
simplification from §3.2 to §3.4. A getter that throws then costs one chain, not every value in the assertion
(§2.5). Two differences stay, since neither puts a false value in a note: an operand of an arithmetic operator is
left as code by `Track.That` and substituted by the weave, and values are cut at 50 and 100 characters. §8 lists
both. A fact runs every §2.3 shape through both paths and requires the same label (§4.1 fact 7).

### 3.6 Shouldly arguments with a join (R1, F7)

`ShouldlyArguments` finds where an argument starts from the stack depths the analysis already records, not from the
producer of the argument before it: argument *k* starts at the last instruction, at or before its own producer, whose
incoming stack holds exactly the call's first *k* arguments. A join inside argument *k*-1 then leaves argument *k*'s
start known. When an argument's own producer is unknown (a `customMessage` that is itself a `?:`), nothing is
skipped for it, as today's `end < 0` branch does. The facts run in Debug and Release, beside the existing Shouldly
facts, through the ILVerify net (`WovenIl`).

### 3.7 Suppression reaches what the compiler moved (R1, F8)

- **A type is suppressed** when it, or any type that encloses it, carries the attribute: nested types, display
  classes, `<>c` and state machines of a suppressed class are all inside it.
- **A method is suppressed** when it carries the attribute or its type is suppressed, and **so is the compiler-
  generated code it references**, followed transitively: the methods its `ldftn` loads (a lambda in a display class or
  in `<>c`, a local function), the methods its `call` reaches on a display class or a local function, and the type its
  `[AsyncStateMachine]` or `[IteratorStateMachine]` attribute names. A lambda in a lambda is reached through the outer
  one, so the rule holds at any depth.
- **References, not names.** The weave reads a state machine's parent from its name today (`<M>d__0`,
  `:313-320`). A name cannot tell overloads apart (each overload's lambdas are `<M>b__…`), and the compiler's naming
  is not a contract; a reference is exact. The name rule stays only as the fallback for a state machine whose method
  carries no attribute naming it.

### 3.8 The subject's words (R1, F9, F10)

- `FormatSubject` leaves each string and character literal exactly as written and splits only the code around it on
  `.` and case, using `LiteralMask` as the argument side does.
- The ` to string()` rule moves from the sentence to the subject and applies in all three shapes: FluentAssertions',
  Shouldly's and TUnit's subjects drop a trailing `.ToString()` alike, and a value is never rewritten.

### 3.9 What a reader sees after R1

With Q1 and Q2 as recommended:

| Statement | 4.14.6 | R1 |
|---|---|---|
| `actual.Colour.ToString().ShouldBe(expected.Colour.ToString())` | `Actual colour to string() should be 'null'()` | `Actual colour should be 'Blue'.ToString()` |
| `actual.Lines.First().ShouldBe(expected.Lines.First())` | `First value of actual lines should be 'null'()` | `First value of actual lines should be expected.Lines.First()` |
| `actual.Colour.ToString().Should().Be(expected.Colour.ToString())` | `Actual colour should be 'null'()` | `Actual colour should be 'Blue'.ToString()` |
| `response.SplitId.ShouldBe(splits.First().SplitId)` | `Response split id should be 'null'().SplitId` | `Response split id should be splits.First().SplitId` |
| `….Count(l => l == "x").ShouldBe(expected.Lines.Count(l => l == "x"))` | `… should be [ '2'(l => l == "x") ]` | `… should be [ expected.Lines.Count(l => l == "x") ]` |
| `actual.Lines[0].ShouldBe(expected.Lines[0])` | `… should be '[ "x", "y" ]'[0]` | `… should be expected.Lines[0]` |
| `plain.ShouldBeSameAs(expected.Plain)` | `Plain should be same as 'Order { Id = A1, …'` | `Plain should be same as expected.Plain` |
| `value.ShouldBe(expected.Inner?.Value)`, `Inner` null | `Value should be 'Value'` | `Value should be 'null'` |
| `actual.Box.Inner.Value.Length.ShouldBe(expected.Box.Inner.Value.Length)` | `… should be 'v'.Length` | `… should be '1'` |
| `actual.Id.ShouldBe(flag ? expected.Id : "none")` | `Actual id should be flag ? expected.Id : "none"` | `Actual id should be 'True' ? 'A1' : "none"` |
| `"Colour".ShouldBe(nameof(expected.Colour))` | `" colour" should be nameof(expected.Colour)` | `"Colour" should be nameof(expected.Colour)` |

With Q3, the rows that stay as written would read the value compared instead (`'x'`, `'A1'`, `'1'`, …).

### 3.10 Q3: the value the assertion compared (not in R1)

The values a note shows today are reconstructed: the weave saves each root before the statement, and `Track` reads
members off it afterwards. The faithful value is the argument the assertion method received, and the weave can save
it:

- **How.** The stack analysis (4.14.0) names the instruction that pushes each argument of the assertion call.
  Before the call, the weave stores each argument it can type into a local of its own and loads it back (the spill
  machinery it already has), and passes it to `Track` with **the argument's own text as its name**:
  `["expected.Colour.ToString()"]`, `["Blue"]`. The longest key wins in the substitution, so the whole argument is
  replaced by the value it had. The arrays are then filled where `Track` is called (after the statement, and in the
  `catch`), not before the statement as now, since an argument has no value until it is evaluated. An argument the
  weave cannot type or box (a `ref struct`) keeps R1's reading.
- **No new public member.** The name and value travel in the `string[]` and `object?[]` the weave already passes, so
  an older `Kronikol` (any since 2.30.12, when the overloads arrived) substitutes them unchanged, which matters
  because the weave cannot know which `Kronikol` the test run will load.
- **When the assertion throws before the call** (a `NullReferenceException` in an argument), the local still holds
  its initial value. The weave starts it as a fresh `object`, whose `ToString` is its type name, so `FormatValue`
  prints nothing and the argument stays as written. No sentinel type is needed.
- **What it fixes beyond R1:** every argument the weave can type reads its value (`'x'` for `expected.Lines.First()`,
  `'1'` for the `Count(…)`), however it was computed; nothing is read after the statement, so a getter's side effect
  or a later change cannot reach the note; and a static field's chain gets a value.
- **Why not in R1.** It is new behaviour (a note shows values it never showed), it changes the weave's IL at every
  assertion call, and it needs its own Release and ILVerify proofs. By the repo's rule that is a minor, and by
  `plan-recommendations-are-not-green-lights` it is the owner's to take.

### 3.11 Kronikol4J

The port has no counterpart. Its `Track` writes the description it is given (`Track.java:212-233`), and its
`Tier2Recorder` appends `[actual: …, expected: …]` to the raw source line (`Tier2Recorder.java:60-66`).
`docs/REMAINING_PARITY.md:1632-1646` records closure-value resolution and the label formatter as a verified
C#-specific boundary, and `:2418` says the port has no label formatter. R1 takes one ledger line after `:2428`, in the
ledger's form: `- **Assertion labels quote only values read (.NET 4.14.R1, <date>, #163).** Not mirrored, a ledger
entry only. <the changelog's first two sentences>; the port has no label formatter or value resolution, so nothing to
mirror.` R2 (Q3) would take another of the same kind.

## 4. Tests, in the order they are written

Runtime facts go in `tests/Kronikol.Tests/Tracking/` (a new `AccessChainTests.cs`, and `TrackValueResolutionTests.cs`,
`ClosureValueResolverTests.cs` and `AssertionExpressionFormatterTests.cs` for their own rows); weaver facts in
`tests/Kronikol.Tests.AssertionTracking/` (`ShouldlyWeaveTests.cs`, `AssertionWeaverTests.cs`), Debug and Release,
through the ILVerify net. Every red run is read for its message: a fact can fail for the wrong reason.

### 4.1 New facts

| # | Fact | File | Red on 4.14.6 |
|---|---|---|---|
| 1 | `A_call_after_a_path_is_not_read_as_a_member` (theory: the issue's five statements, `expected.Id.ToString()`, `ToString("G")`, `Get<int>()`, `splits.First().Id`, TUnit's text), through `AssertionPassedWithValues`; no label holds `'null'(`, and each reads Q1's form | `TrackValueResolutionTests.cs` | yes, `'null'()` |
| 2 | `A_call_named_like_a_property_is_not_read_as_the_property` (`Count()`, `Count(l => …)`), both resolvers | same, and `ClosureValueResolverTests.cs` | yes, `'2'(…)` |
| 3 | `An_unreadable_chain_shows_no_value` (theory: a getter that throws, a member not found, a null before a plain `.`) | `TrackValueResolutionTests.cs` | yes, `'null'` |
| 4 | `A_null_before_a_null_conditional_reads_null` (`expected.Inner?.Value`, and `?.` then `!`) | same | yes, `'null'.Value` or nothing |
| 5 | `A_key_is_not_substituted_inside_a_longer_chain` (an index, a property that does not print under a root that does, a chain after `!`, five segments) | `AssertionExpressionFormatterTests.cs`, `TrackValueResolutionTests.cs` | yes |
| 6 | `A_member_name_is_never_quoted_as_a_value` (Q2's form, through both paths) | `AssertionExpressionFormatterTests.cs` | yes, `'Value'` |
| 7 | `Both_paths_label_one_assertion_alike` (every §2.3 shape through `Track.That` and through `AssertionPassedWithValues` with the closure's values; operators excepted, §3.5) | new `ValuePathParityTests.cs` | yes, F6's rows |
| 8 | `A_getter_that_throws_costs_only_its_own_chain` (`Track.That` with two names, one getter throwing) | `ClosureValueResolverTests.cs` | yes, no value at all |
| 9 | `A_subject_literal_is_kept_as_written` (`"Colour"`, `ToString("G")`, a literal holding a `.`) | `AssertionExpressionFormatterTests.cs` | yes, `" colour"` |
| 10 | `To_string_is_read_alike_in_every_library`, and `A_value_holding_to_string_is_not_rewritten` | same | yes |
| 11 | `A_Shouldly_argument_with_a_join_captures_its_values` (theory: `?:`, `??`, `?.`, a join before a custom message), Debug and Release | `ShouldlyWeaveTests.cs` | yes, nothing captured |
| 12 | `Suppression_reaches_lambdas_local_functions_state_machines_and_nested_types` (theory over the §2.6 table, plus a lambda in a lambda), and the guard `An_unsuppressed_lambda_is_still_woven` | `AssertionWeaverTests.cs` | yes, the three rows; the guard no (M12) |
| 13 | `The_issues_statements_label_their_values` (the issue's five plus the SplitId shape, real Shouldly and FluentAssertions, woven, Debug and Release, exact labels) | `ShouldlyWeaveTests.cs` | yes |
| 14 | `A_method_call_argument_note_paints_its_value` (Playwright: a report whose run recorded fact 13's first statement paints `'Blue'.ToString()` in the note with the Assertions toggle on, and no `'null'`) | `tests/Kronikol.Tests.EndToEnd` | yes |

### 4.2 Existing facts

- **Change with R1**, each named by a sentence of the changelog:
  - `TrackValueResolutionTests.AssertionPassedWithValues_null_intermediate_in_chain_shows_null` (`:249`) and
    `ClosureValueResolverTests.Falls_back_when_property_chain_returns_null` (`:319`). A null before a plain `.` is
    unreadable, so the argument stays as written. Each becomes a pair: the `?.` form reads `'null'`, the plain form
    reads as written, which is what 2.29.5-beta's changelog promised.
  - `AssertionExpressionFormatterTests` `:236`, `:244`, `:261` (the simplification) and `:371` (`map[key]!.Id` →
    `'Id'`): Q2's form.
- **Stay green unchanged:** every other fact in §2.8, in particular the longest key first (`:215`), no partial token
  (`:187`), no substitution in a literal (`:177`), values inside a lambda argument (`:269`), the `First()` subject
  (`:304`), and the Shouldly and TUnit labels with values (`:391-419`, `ShouldlyWeaveTests`, `Shouldly5PreviewTests`,
  `ShouldlyAssertionNoteTests`).
- **S1 greps every test project's expected labels** for `'null'(`, `]'[`, `'.`, a quoted single word and
  `to string()`, and lists each hit as "changes" or "stays" before any code moves.

### 4.3 Mutations, one per behaviour

| # | Mutation | Turns red |
|---|---|---|
| M1 | a member that a call follows read as a member again | 1, 2, 13 |
| M2 | unreadable answered as null again | 3 |
| M3 | a null before a plain `.` read as null | 3 |
| M4 | a null before `?.` read as unreadable | 4 |
| M5 | the substitution back to a search with the identifier-character boundary | 5 |
| M6 | the three-segment limit restored | 5 |
| M7 | the resolver keying the raw text instead of the canonical one | 4, 5 |
| M8 | Q1's scalar test removed (always the read part), and inverted (never) | 1, 5 |
| M9 | the quoted last segment restored | 6 |
| M10 | `ClosureValueResolver` back on its own walker | 2, 7, 8 |
| M11 | `ShouldlyArguments`' start from the previous producer again | 11 |
| M12 | suppression by the type's own attribute alone; and every lambda suppressed | 12 |
| M13 | `FormatSubject` splitting literals again | 9 |
| M14 | the ` to string()` rule over the whole sentence, FluentAssertions only | 10 |

### 4.4 Proofs before the tag

- **Red on 4.14.6.** The new facts copied into a worktree at v4.14.6, with any new internal member stubbed so they
  compile, and each failure's message read.
- **Mutations** in a `git stash create` snapshot worktree, each file backed up, never reverted with
  `git checkout`.
- **Suites and builds.** The core suite, the weaver suite on net8.0, net9.0 and net10.0 in Release with the ILVerify
  net, the cross-SDK facts on every installed SDK, the full Playwright suite, and `release.slnf` built in Release for
  every target framework, after the version bump.
- **The harness probe on local packages** (an isolated NuGet cache and a version label nuget.org never has), Debug and
  Release: every §2.3 row reads its §3.9 form, the suppression table's rows read "expected", and the probe's labels are
  diffed against `s0/` with every difference classified.
- **The issue's stack:** a scratch net8.0 project with xUnit 2.9.3, `Kronikol.xUnit2`, `Kronikol.AssertionTracking`,
  Shouldly 4.3.0 and FluentAssertions 6.12.1 on the local packages, the issue's test inside a step: `kronikol query
  assertions` prints the §3.9 labels. The report is read only through the query tool.
- **The owner's suite** (the owner's to run, or to hand over): `kronikol query assertions` on a run before and after,
  and `harness/classify.py` over both outputs counts each §2.3 group. The issue's 48 `'null'` notes go to 0, and the
  other groups are counted for the first time.

## 5. Releases and bumps

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| R1 | F1 to F10 (§3.2 to §3.8), Q1 and Q2 as answered | patch: 4.14.7, or the next free patch | Bug fixes: no member, option or format field is added. Labels change, and the changelog says how |
| R2, only if Q3 is taken | §3.10 | minor: the next free minor | A note shows values it never showed, through new IL at every assertion call: new behaviour, not a fix |

Steps:

- **S0, done (RUN):** the repro and the probe (§2.1, §2.3, §2.6).
- **S1:** R1's facts (1 to 14), each run red on v4.14.6.
- **S2:** `AccessChain` (§3.2) with its own facts, then `Track` and the formatter on it (§3.3, §3.4).
- **S3:** `ClosureValueResolver` on it (§3.5).
- **S4:** the weave's two fixes (§3.6, §3.7), with the ILVerify net and the Release theories green.
- **S5:** the subject's words (§3.8).
- **S6:** the documentation (§6), the proofs (§4.4) and the release.

Draft of R1's changelog entry:

> **Fixed.** An assertion note could put a value in quotes that the assertion never compared (#163). An argument that
> called a method on a variable's member, such as `expected.Colour.ToString()`, `expected.Lines.First()` or
> `splits.First().SplitId`, read `'null'()`, whatever the value; a method that shares a property's name read as the
> property, so `expected.Lines.Count(l => l == "x")` showed the list's `Count`; a variable's value was put inside a
> longer expression it did not stand for (the whole list for `expected.Lines[0]`, the whole record for
> `expected.Plain`); a getter that threw read as `'null'`; and an argument that could not be read showed its last
> member's name in quotes (`'Value'`). A quoted value is now always the value of exactly the code it replaces:
> members are read up to the first method call or index, `'Blue'.ToString()` shows the value read before a call
> when it is a single value, and code that cannot be read is shown as written. `Track.That` reads values by the same
> rules. A Shouldly assertion whose argument held `?:`, `??` or `?.` captured no values; it does now.
> `[SuppressAssertionTracking]` now also covers lambdas, local functions and the async methods of a suppressed class,
> as the documentation said. A string literal in an assertion's subject is no longer rewritten into words
> (`"Colour"` read `" colour"`), and `.ToString()` is dropped from a subject in Shouldly and TUnit labels as in
> FluentAssertions'. Patch: bug fixes, nothing new to call. Labels change for the shapes above; reports written
> before this release keep their labels.

The per-release checklist, as the last plans ran it: every package's version (`Directory.Build.props`, `plugin.json`,
`marketplace.json`, the history action's `VERSION`), template pins strictly behind and moved only once nuget.org lists
every pinned id, the number claimed by `SendMessage` before tagging (placeholder `4.14.R1` in comments until then), no
"fixes #N" in a commit message, `gh issue close 163` after publication, and every pushed SHA's CI, CodeQL and Summary
Preview runs, with the "Assertion Tracking Tests" job's log read for the new facts by name (`release.yml` does not run
that project).

## 6. Documentation

Wiki line numbers are on `origin/master`; the local wiki checkout was 26 commits behind on 2026-10-11.

| Where | Change |
|---|---|
| Wiki `Assertion-Tracking.md:176-189`, the value rules | A member is read up to the first method call or index; a chain that cannot be read (a member not found, a getter that throws, a null before a plain `.`) is shown as written; a `?.` over a null reads `'null'`; Q1's form for a call after a value; no depth limit (replaces "up to 3 levels") |
| Same page, `:104-127`, suppression | It covers lambdas, local functions, async methods and nested types (§3.7) |
| Same page, `:433-459`, the formatter's table and rules | Rows for a call after a path and for an index; `.ToString()` dropped from a subject in every library; a literal in a subject kept as written. Found on the way: the table's lambda row shows `List should contain single x => x.IsAdmin`, while the formatter wraps a lambda in brackets (`[ x => x.IsAdmin ]`, pinned by `AssertionExpressionFormatterTests.cs:111`); the row is corrected |
| Same page, the `Track.That` section | The same rules as a woven statement; a getter that throws costs only its own chain |
| `Diagnostics-and-Debugging.md:60` | Its "could not be resolved to runtime values" sentence checked against §3.5's fallbacks |
| XML docs: `AssertionExpressionFormatter.cs:6-10`, `Track.cs:145-149`, `:167-171` | "resolves dotted property paths" becomes the chain rule. The docs ship in the package |
| `CHANGELOG.md` | §5's entry, folding in any `[Unreleased]` section |
| Kronikol4J `docs/REMAINING_PARITY.md`, after `:2428` | §3.11's line |

Before the release, grep the wiki for `'null'`, `3 levels`, `dotted`, `SuppressAssertionTracking` and `ToString` for
any page this table missed, and the skill references (`.claude/skills/`, `templates/skills/`) for `query assertions`.

## 7. Where it sits in the roadmap

- **R1:** stage 1, the patch train, by rule 1: a report that states a value the run never had is the defect class
  the launch bar cannot carry. A row is taken by `SendMessage` when the owner green-lights the plan, not before.
- **R2 (Q3):** after R1, which edits the same files; breadth for the bar, the owner's placement.
- **Files:** `src/Kronikol/Tracking/` (`Track.cs`, `AssertionExpressionFormatter.cs`, `ClosureValueResolver.cs`, the
  new `AccessChain.cs`) and `src/Kronikol.AssertionTracking/AssertionWeaver.cs`. No other plan in flight on
  2026-10-11 touches them (`ListAgents`: every session idle).

## 8. Found on the way, not in this plan

| # | Finding | Level | Recommendation |
|---|---|---|---|
| 1 | `Track.That` leaves an operand of an arithmetic operator as code (`expected.Code + 0`), and the weave substitutes it (`'7' + 0`). Both are true | RUN | Leave; if one form is wanted, pick it with Q3 |
| 2 | Values are cut at 100 characters by the weave's path and at 50 by `Track.That`'s | READ | Leave, or use one constant with R1's shared code if the owner wants the same length |
| 3 | `NameAppearsInExpression` counts a name inside a string literal (`AssertionWeaver.cs:1692-1713`), so a variable named only in a message is captured; the formatter never substitutes inside a literal, so no note changes | READ | Leave |
| 4 | FluentAssertions' `because` arguments are scanned for values (Shouldly's `customMessage` is skipped, `:1496-1512`); a `because` string's interpolated variable could be substituted into the reason's code text | READ | Check with a fact when Q3 is designed, since Q3 reads arguments by position |
| 5 | Static fields and `this` properties are never captured (no branch for `ldsfld` or a getter call), so `StaticExpected.Colour.ToString()` stays as written | RUN, READ | Q3 covers them for arguments |
| 6 | The weave's version gate still says 2.30.7 (`AssertionWeaver.cs:349-359`); the `WithValues` overloads arrived in 2.30.12. No `Kronikol` assembly before 3.0.0 has the name the gate reads, so nothing can fall in the gap | READ | Leave |

## 9. What is not known

- How the owner's 48 notes split across §2.3's groups, and how many of the other 699 sit in groups the issue did
  not count (F3 to F5 do not print `null`). `harness/classify.py` answers it from `kronikol query assertions`.
- How often real suites call a method that shares a property's name in an argument (F3). `Count` on a `List<T>`,
  an array's `Length` beside LINQ's `Count()`, and `First` on a `LinkedList<T>` are the common ones.
- Q3's cost on the weave's size and time, which S0 of R2 would measure on the weaver's largest fixture and on the
  owner's suite.

## 10. Risks and traps

1. **A label change is a report change.** Every §3.9 row changes text that a golden file, a test or Kronikol4J might
   pin. S1 greps the test projects for `'null'(`, `'null'.`, `]'[`, `to string()` and quoted single words, and
   each change has a changelog line.
2. **The weave is shared code.** F7 and F8 change what every Shouldly and every suppressed suite weaves. The ILVerify
   net, the Release theories and the cross-SDK facts run on S4 before anything else lands on it.
3. **The parser is a C# parser in miniature.** Its grammar is the chain only (§3.2); anything else in an argument
   (operators, lambdas, `new`, `switch`) is left to the formatter as today. A chain the parser cannot follow is
   unreadable, so the failure mode is "as written", never a false value. Fact 7 and the probe are its corpus.
4. **Reading a member is still code running.** A getter can have side effects; R1 does not add any read the walk
   did not make (it reads fewer: none past a call). Q3 removes the reads for arguments.
5. **Reports are written once.** An old report keeps its labels; `kronikol merge` of an old shard with a new one
   shows both forms. The changelog says so.
6. **Release numbers.** Other sessions may release first; the number is claimed by `SendMessage` at the tag.
7. **Reading reports.** Acceptance reads reports through `kronikol query`, never by opening the JSON or the HTML.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | A chain read up to a call or an index: show the read part's value before the rest, or the chain as written | **The read part's value when it is a scalar, else as written** (§3.3): `'Blue'.ToString()`, `'A1'.Trim()`, but `expected.Lines.First()` rather than `'[ "x", "y" ]'.First()`. The alternatives are the issue's two suggestions: always the read part, or never |
| Q2 | A chain with no value: how to show it now that a quoted word means a value | **As written** (§3.4). The alternatives keep today's readability rule without the quotes (`Eggs`), or use the subject's words |
| Q3 | Capture each argument's evaluated value in the weave (§3.10) | **Yes, as R2, a minor, after R1.** It is the only way a note can show the value compared for every argument, with nothing read after the statement; it needs no new public member |
| Q4 | F7 to F10 in R1 | **Yes.** Each is a bug in the same note's text or capture, found by this plan's probe; the repo's rule is to fix the bugs found on the way, and none adds surface |
| Q5 | The 100 and 50 character cuts (§8 item 2) | **Leave** unless the owner wants one length; it changes no value's truth |

## Appendix A. Edit sites

| File | Change |
|---|---|
| `src/Kronikol/Tracking/AccessChain.cs` (new, internal) | §3.2: the chain parser, the reader with its three answers, the canonical key |
| `src/Kronikol/Tracking/Track.cs:261-390` | `ResolveVariableValues` on `AccessChain`; `ResolveDottedPaths`, `ExtractDottedPath` and `WalkPropertyChain` removed |
| `src/Kronikol/Tracking/AssertionExpressionFormatter.cs:73-74`, `:206-248`, `:405-520` | §3.3 substitution by span, §3.4, §3.8 |
| `src/Kronikol/Tracking/ClosureValueResolver.cs:21-81`, `:265-329` | §3.5 |
| `src/Kronikol.AssertionTracking/AssertionWeaver.cs:1474-1514` | §3.6 |
| `src/Kronikol.AssertionTracking/AssertionWeaver.cs:114-130`, `:295-330` | §3.7 |
| `tests/Kronikol.Tests/Tracking/`, `tests/Kronikol.Tests.AssertionTracking/`, `tests/Kronikol.Tests.EndToEnd/` | §4.1 |
| `CHANGELOG.md`, the version files, the template pins | §5 |
| `../Kronikol.wiki`, Kronikol4J's ledger | §6, §3.11 |

## Log

- 2026-10-11: drafted from #163 by kronikol-6b. S0 RUN on the published 4.14.6 packages (`harness/s0/`); the weave's
  capture rules, the two resolvers and the formatter READ at d59843fa. Nothing in `src/` is changed; not green-lit.
- 2026-10-12: green-lit in full by the owner ("implement the plan in full"), every §11 question as recommended;
  placed as roadmap row 1.27 (stage 1, rule 1, no decision number; 1.26 is the row PR #164, a cloud session's 4.15.0 in review, took on 2026-10-11). Executed by kronikol-6b in the worktree
  `Kronikol-163`.
- 2026-10-12, R1 built as 4.14.8 (4.14.7 went to kronikol-28's #133 audit patch the same morning). What changed from
  the plan while building it:
  - **Facts placed in their own files.** The weaver facts 11 to 13 are `AssertionValueWeaveTests.cs` (one fixture
    holds the issue's statements, the joins and the suppression table) rather than `ShouldlyWeaveTests.cs` and
    `AssertionWeaverTests.cs`; fact 14 is a second fact in `ShouldlyAssertionNoteTests.cs`.
  - **A third join defect (F11), found by fact 11.** With the capture fixed, `actual.Id.ShouldBe(flag ? expected.Id :
    "none", "the id")` still drew `, "the id"` in its label: `WithoutCustomMessage` (`AssertionWeaver.cs`) left a
    call "whose arguments are named" as written and tested that by `": "` anywhere in the arguments, which a `?:`
    holds. It now looks for a name and a colon where an argument starts (`HasNamedArgument`), and M15 reverts it.
  - **F8 was wider than its table:** a type nested in a suppressed class was woven too (fact 12 fails on 4.14.6 for
    `SuppressedType+Nested`).
  - **A dictionary passed to the public `Format` replaces whole chains only.** Only Kronikol's own readers know that
    a value read before a call is a single value (`ChainValues.Prefixes`), so `'Blue'.ToString()` comes from them;
    a caller's dictionary never puts its key before a call or an index
    (`A_plain_dictionary_key_does_not_stand_before_a_call`).
  - **M10** was run as the whole of `ClosureValueResolver.cs` at v4.14.6 put back (15 facts fail).
  Proofs, in `harness/r1/`: red on v4.14.6 for 72 core facts, 20 of the 25 weaver facts (the 5 that pass there are
  guards) and the Playwright fact; mutations M1 to M15 and M10 all killed (`mutations.txt`); the probe on local
  packages `4.14.8-local.163`, Debug equal to Release, 52 of 65 cases changed, each to its §3.9 form
  (`compare-with-s0.txt`; 42 statements woven where 4.14.6 wove 55, the 13 the attribute now covers); and the issue's
  stack, a scratch copy of the xUnit v2 template on net8.0 with Shouldly 4.3.0 and FluentAssertions 6.12.1, read
  through `kronikol query assertions` (`issue-stack-query.txt`). The plan's roadmap row is 1.27: a cloud session's
  PR #164 (4.15.0, in review) had taken 1.26 on 2026-10-11, so R2 takes 4.16.0.
- 2026-10-12, R2 built as 4.16.0 (4.15.0 is the number open PR #164 is written for). What changed from §3.10 while
  building it:
  - **The arrays are built where they were, and the call fills its slots.** The weave still builds the names and
    values arrays before the statement; each captured argument's slot starts as a plain `object`, and the weave
    stores the argument's value into it just in front of the call (it stores what the call takes from the stack from
    the lowest captured argument up, copies the captured ones, and loads them all back). A branch to the call, which a
    join in the last argument makes (a custom message written `flag ? "a" : "b"`), is sent to those stores, or one
    path would skip them (`Join_in_a_custom_message_taken` and `_not_taken`).
  - **What is captured.** Each argument of Shouldly's call, or of the first call that takes what FluentAssertions'
    `Should()` returned, unless the library shows it (`customMessage`, `because`, `becauseArgs`, a
    `[CallerArgumentExpression]` parameter), its text evaluates nothing (a constant, `new[] { 1, 2 }`, a lambda; an
    interpolated string with a hole does evaluate something, `Interpolated_argument`), it comes straight from a constant
    (`ldc`, `ldstr`, `ldnull`, perhaps converted or boxed), it is a variable already captured
    (`A_variable_passed_alone_is_named_once`), or its value cannot be held in an object (a by-ref, a pointer, a ref
    struct, a delegate, an expression tree). A value type is boxed as the parameter's type with the call's type
    arguments put in, so a `?:` of two character constants, which the stack holds as `int32`, reads `'A'` and not `'65'`
    (`Char_join`). Awaited assertions keep R1's reading: their arrays are built at the merge point, after the call.
  - **The argument texts are matched against the statement as written**, before `WithoutCustomMessage` cuts
    Shouldly's message: Shouldly 4.3.0 has 13 methods whose `customMessage` comes before another parameter
    (`ShouldBe(string, string, string customMessage, StringCompareShould options)` among them), and the cut text put
    the next argument out of place (`Argument_after_a_custom_message`).
  - **The runtime** tells an argument's text from a variable's name by `AccessChain.IsIdentifier`, adds an
    argument's value before any chain is read so that it wins, and the formatter replaces each top-level argument
    whose canonical text is a key before it looks at the chains of the others.
  - **§8 item 4 checked:** a variable in FluentAssertions' `because` (`$"ids match for {who}"`) is never put into
    the reason's text (`Fluent_because_holding_a_variable`).
  - **S0 of R2 (§9, Q3's cost)** was measured on the plan's probe rather than the weaver's largest fixture: the woven
    `Probe.dll` grows by 2,560 bytes (6.6% in Debug, 6.8% in Release) against the published 4.14.8, for the same 42
    statements, and the weave's time stays within noise (`r2/cost.txt`). The owner's suite remains the owner's to run.
  Proofs, in `harness/r2/`: red on v4.14.8 for 26 of the 46 weaver facts, each on 4.14.8's own form (the 20 that pass
  there are R1's suppression facts and guards: constants, a variable alone, `?.`, an argument that threw), 8 of the 9
  new Track facts (the ninth is the not-evaluated guard) and the new Playwright fact; 15 of 15 mutations killed
  (`mutations.txt`; N8 and N14 survived the first run, which showed two facts too weak, so `Char_join` and a join in
  the two-argument row were added); the probe on local packages `4.16.0-local.163`, Debug equal to Release, with 25 of
  its 65 cases changed from `r1/`, each to the value the call received (`compare-with-r1.txt`); and the issue's stack,
  R1's scratch project on `4.16.0-local.163`, prints `'A1'`, `'Blue'`, `'x'`, `'blue'`, `'Blue'` and `'S1'`
  (`issue-stack-query.txt`).
- 2026-10-12, both releases published, and the plan is complete. 4.14.8: Release run 38225066058, CI 38225062843
  (passed on its second attempt; the first lost one Linux Playwright fact, `ExportFilteredHtmlRenderingTests`, to a
  60-second `DrawAll` timeout that passes locally), CodeQL 38225062772 and CI Summary Preview 38225062780 passed. 4.16.0:
  Release 38245472402, CI 38245469525 (first attempt), CodeQL 38245469479 and Summary Preview 38245469498 passed.
  nuget.org listed all 62 ids at each. The wiki has R1's edits (37c6537) and R2's (55c275c), Kronikol4J's ledger a line
  for each release (4809398, 97560f3), and #163 is closed with a comment naming both releases. Left to the owner: the
  suite the issue came from, before and after through `harness/classify.py` (§4.4), and PR #164, written as 4.15.0,
  which has to take the next free minor when it merges, since 4.16.0 is published.
