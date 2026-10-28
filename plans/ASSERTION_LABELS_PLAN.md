# Assertion labels read code as words and constants as names (#210)

**Issue:** #210 (filed by the owner on 2026-10-27 while integrating 5.11.0 into a monorepo's xUnit v2 suites that assert
with Shouldly 4.3.0).
**Measured on:** 5.12.0 (`1ee6373a`), with the published packages `Kronikol` and `Kronikol.AssertionTracking` 5.12.0,
Shouldly 4.3.0 and FluentAssertions 6.12.1, on net10.0, SDK 10.0.300, Windows 11, in Debug and in Release. Revised the
same day on 5.14.0 (`8d7d1e1d`), which changed none of the files this plan edits: the prototype's suites, the woven probe
and every corpus were run again there.
**Status:** **green-lit 2026-10-28** ("implement the plan in full"), every question at its recommendation: Q8 and Q9
yes, Q7 left to #139's release as Q7 recommends. **R1 released as 5.16.3** (the Log says what execution added and
found); **R2 released as 5.16.4**, which completes the plan.
**Harness:** `plans/ASSERTION_LABELS_PLAN.harness/` (Appendix B).

## TL;DR

- Both of the issue's labels are real. They reproduce on 5.12.0 and 5.14.0 in Shouldly, FluentAssertions and
  `Track.That` alike, in Debug and in Release (§2.1).
- Neither has the cause the issue suggests, and each is one case of a wider defect:
  1. **The subject is not read as code.** `FormatSubject` cuts the subject at every dot, inside a call's brackets too,
     and splits every piece on case. So a call's arguments, an index, a type argument and a lambda all become words:
     `ToString(CultureInfo.InvariantCulture)` reads `to string( culture info invariant culture)`, `Get<Item>()` reads
     `get< item>()` and `Single(x => x.Id == expected.Id)` reads `single(x => x id == expected id)`. In three public
     Shouldly suites (AutoMapper, MediatR, Marten: 9,326 one-line assertions) 1,785 subjects are rewritten this way,
     and 97 of BreakfastProvider's 1,016 FluentAssertions ones (§2.2, §2.4).
  2. **A compile-time constant is not shown as its value.** The weave leaves out an argument the compiler folded into
     a constant, because "its text already says what it is" (`AssertionWeaver.cs:1367-1368`). That is true for a
     literal and an enum member, and false for `nameof(…)`, a `const` field or local, and constant arithmetic. The
     formatter then shows the code, and a rule written for enum members cuts an argument list that is one `A.B` to
     `B`. So one value, "GBP", reads four ways: `'GBP'` (a `static readonly` field or a local), `Gbp` (a `const`
     field), `gbp` (a `const` local) and `nameof(CurrencyCode.GBP)`. A failing `const` reads `should be Usd` when the
     value compared was "USD". The Shouldly suites pass `nameof(…)` in 115 assertions; BreakfastProvider compares with
     a `const` in 116, and in 49 of them the name shown is not the value (`should be DefaultTemperature` for 180)
     (§2.3, §2.4).
- The same labels misread code in more ways, found on the way and in scope, because they are the same reading: an
  assertion's chain is read as its first call with its **last** call's arguments, so TUnit's
  `IsEqualTo(10.0).Within(0.5)` reads "is equal to 0.5" and Shouldly's `Should.Throw<T>(…).Message.ShouldContain("boom")`
  reads `Should throw T "boom"` (F10); `.And.` cuts inside a literal and loses the value (F9); FluentAssertions' subject
  ends at a `.Should()` inside brackets (F6); nested type arguments are cut at the first `>` (F11); `Track.That`
  labels keep line breaks (F7) and read `async () =>` and a block body as code (F16) (§0, §2.6 to §2.9).
- The issue's suggestions would fix less than they seem to. Reading an argument list "as one phrase" still rewrites
  code, and changes what a lambda says. Dropping a culture argument hides what a failed comparison turned on. Reading
  `nameof` alone leaves every `const` reading as a bare name (§2.5).
- **The fix is one rule, applied where the label is made: a label uses words only for a member path and for each call
  of its assertion's chain, keeps all other code exactly as written on one line, and shows a value wherever Kronikol
  or the compiler knows it.** Two patch releases (§3, §5):
  - **R1, the formatter (`Kronikol`):** the subject, the chain, `nameof(…)` read as `'GBP'`, one line,
    FluentAssertions' top-level split, and the other readings of §3.7.
  - **R2, the weave (`Kronikol.AssertionTracking`):** a constant argument is passed with its value unless its text is
    its own spelling (a literal or an enum's own members), so `Currencies.Gbp`, `gbp` and `Limits.MaxRetries` read
    `'GBP'`, `'GBP'` and `'3'`.
- A prototype of both is in the harness (`prototype.patch`, against 5.14.0). On it, the whole core suite fails only
  the two facts that pin `nameof(expected.Colour)` as written (9,107 of 9,111 pass, 2 skipped), the weaver suite
  passes as on 5.12.0 (406 passed, 28 skipped), and every woven probe case reads as §3.9 says, in Debug and Release
  alike. In the five corpora no label keeps a space inside a bracket or a line break, and 2,330 of 10,395 one-line
  labels change; none loses its sentence (`corpus/changes.py`).
- Nine questions for the owner, each with a recommendation (§11). Q8 (read `.And.` chains in full) and Q9 (escape
  creole in a label) are new in this revision.

## 0. Findings

| # | Finding | Level | Section |
|---|---|---|---|
| F1 | **A call in the subject is turned into words.** `FormatSubject` (`AssertionExpressionFormatter.cs:203-252`) cuts the subject at every `.` outside a literal, inside brackets too (`SplitOutsideLiterals`, `:272-289`, counts no brackets). `ToString(CultureInfo.InvariantCulture)` becomes the two pieces `ToString(CultureInfo` and `InvariantCulture)`, and `Words` (`:254-270`) splits each on case and lower-cases it. Arguments, indexes, type arguments and lambdas are all read as words: `get< item>()`, `map[ keys primary]`, `count(x => x is active)`, `single(x => x id == expected id)`, `Math round(result total, 2)` | RUN | §2.2 |
| F2 | **The space after `(` comes from the case splitter.** `SplitPascalCase` (`:600-616`) starts a word before a capital after any character that is not one, so also after `(`, `[`, `<` and a space; after a space it leaves two: `New  uri(result link) host`, `to string("yyyy-MM-dd",  culture info …)` | RUN | §2.2 |
| F3 | **A subject that is not a member path is rewritten too**: a cast, an operator, `new`, a bracketed `await`: `((int)response status code)`, `(result total + result tax)`, `(await  fail(connection, "SELEC 1"))` | RUN | §2.2 |
| F4 | **`nameof(…)` is drawn as code** (the issue's second label). The weave leaves out an argument whose value is a constant (`IsConstant`, `AssertionWeaver.cs:1367-1382`, called from `DetectCapturedArguments`, `:1208-1210`), and the formatter shows the code | RUN | §2.3 |
| F5 | **A named constant reads as a name, never as its value.** An argument list that is one `A.B` of two capitalised names is cut to `B` by `StripSimpleEnumPrefixes` (`AssertionExpressionFormatter.cs:589-598`, written for enum members, regex `^[A-Z][a-zA-Z0-9]*\.[A-Z][a-zA-Z0-9]*$`): `Currencies.Gbp` reads `Gbp`, `Limits.MaxRetries` `MaxRetries`, `Int32.MaxValue` `MaxValue`. Other shapes stay as written (`_consts.Gbp`, `Endpoints.Swagger.PancakesPath`, `int.MaxValue`). A `const` local reads as its own name (`gbp`). A failing one reads `should be Usd` against "USD". A `static readonly` field reads `'GBP'` | RUN | §2.3 |
| F6 | **FluentAssertions' subject ends at the first `.Should().` anywhere**, even inside brackets or a literal (`ShouldSplitRegex`, `:15-18`, used at `:51-57`): `(await act.Should().ThrowAsync<T>()).Which.Message.Should().StartWith("Code: 60.")` reads `(await act should throw async <T>` | RUN | §2.6 |
| F7 | **A `Track.That` label keeps the statement's line breaks.** `[CallerArgumentExpression]` passes the code as written, the formatter removes whitespace only before a dot (`CleanCode`, `:291-327`), and code no library's shape matches is returned whole (`:54`). The weave joins a statement's lines with a space (`CutColumns`), so woven labels stay on one line | RUN | §2.7 |
| F8 | **Tests and the wiki pin the defects.** `A_subject_literal_is_kept_as_written` (`AssertionExpressionFormatterTests.cs:305`) and `A_chain_inside_nameof_is_not_read` (`TrackValueResolutionTests.cs:443-449`) expect `nameof(expected.Colour)` as written. The wiki says "Not stored: a constant (`"A1"`, `2`, `Colour.Blue`)" and "Enum prefixes are stripped for simple arguments" next to "code with no value is shown as written" (`Assertion-Tracking.md:250`, `:563`, `:573`) | READ | §2.11 |
| F9 | **`.And.` and `.Or.` are cut wherever they appear**, inside a literal or a lambda too (`IndexOf`, FluentAssertions `:63-65`, TUnit `:161-166`). `x.Should().Be("Rock.And.Roll")` reads "X should be", its value lost; `BeEquivalentTo(expected, o => o.Excluding(…).And.Excluding(…))` loses all its arguments; TUnit's `IsEqualTo("Rock.And.Roll")` reads "Text is equal to" | RUN | §2.8 |
| F10 | **A chain is read as its first call's method with its last call's arguments** (`ParseMethodAndArgs`, `:387-444`), in all three libraries. TUnit `IsEqualTo(10.0).Within(0.5)` reads "Price is equal to 0.5", `IsEqualTo("bob").IgnoringCase()` "Name is equal to"; Shouldly `Should.Throw<X>(…).Message.ShouldContain("boom")` reads `Should throw X "boom"`; FluentAssertions `ContainKey("a").WhoseValue.Should().Be(1)` reads "Dict should contain key 1" and `Throw<X>().Which.Code.Should().Be(456)` "Act should throw <X>, 456". The weave captures the first call's arguments (`AssertionWeaver.cs:1170-1179`), so a captured value never showed where the label put another call's arguments | RUN | §2.8 |
| F11 | **A method's nested type arguments are cut at the first `>`** (`IndexOf('>')`, FluentAssertions `:431`, Shouldly `:104`): `BeOfType<List<int>>()` reads "should be of type <List<int>", `ShouldBeOfType<Dictionary<string, List<int>>>()` "should be of type Dictionary<string, List<int" | RUN | §2.9 |
| F12 | **A comment inside a statement reaches the label**: `ShouldBe(5 /* five */)` reads "should be 5 /* five */", and a `//` line inside a multi-line statement brings its text in; once the weave joins the lines, where the comment ended is lost. None in the five corpora | RUN | §2.9 |
| F13 | **An interpolated string ends at a quote inside its hole** (`LiteralMask`, `:334-347`): `$"{(flag ? "a.b" : "c")}.Should()."` is read as code from `a.b` on. One statement in the five corpora | RUN | §2.9 |
| F14 | **An argument list holding `=>` in a literal is put in lambda brackets** (`args.Contains("=>")`, `FormatArgs`, `:455`): `Ex message should contain [ "…(x => x.Value, …);" ]` | RUN | §2.9 |
| F15 | **Shouldly's argument split counts `<` and `>` as brackets** (`SplitTopLevelCommas`, `:565-586`), so the `>` of `=>` and a comma between type arguments cut an argument in two; once F10 shows a call's own arguments, `Should.Throw<T>(() => RegisterTagType<EnrolleeId, string>(…))` shows `[ string>(x => …) ]` | RUN | §2.9 |
| F16 | **Only an exact `() => ` head is stripped** (`:31-33`): `Track.ThatAsync`'s `async () =>`, a line break after the arrow and a block body `{ …; x.ShouldBe(y); }` are read as code. The one that reads today, `TrackThatTests.ThatAsync_resolves_captured_variable_value`, does so through F6: the first-match split finds the `.Should()` inside the braces | RUN | §2.9 |
| F17 | **An assertion inside other code is not found** (#212's probe, kronikol-c6): `Console.WriteLine((await Should.ThrowAsync<T>(…)).Message)` is labelled with the whole statement, and TUnit's lambda subject `Assert.That(() => _client.GetOrderAsync(r))` reads `() => _client get order async(r)` | RUN | §2.9 |

## 1. How far each claim was checked

- **RUN**: measured on 5.12.0 or 5.14.0, or on the prototype, by a harness script named in the section.
- **READ**: read in the code, the tests, the wiki or git history at the revision named, not run.
- **Not measured: the owner's nineteen reports.** They are not on this machine. I searched `C:/Code`, the user folders
  and the other sessions' notes. Five corpora stand in, taken from source and run through the published 5.12.0
  formatter and through the prototype (§2.4):
  - FluentAssertions: BreakfastProvider (`0d29537`, 2026-09-23, the checkout on this machine; its origin is 36 commits
    on) and the repo's `examples/` at `1ee6373a`;
  - Shouldly, the issue's library: AutoMapper (`fc14885`), MediatR (`0e88617`) and Marten (`baae1f1`), shallow clones
    of their public repositories taken on 2026-10-27, read as text and never built.
- **A corpus count is the formatter's, not a report's.** The corpus runs pass each statement to the formatter with no
  values, as the weave passes a statement whose arguments it did not capture. Two differences from a woven run: the
  weave removes a Shouldly statement's positional custom message first (`WithoutCustomMessage`, called at
  `AssertionWeaver.cs:677`, defined at `:1948`), and some statements are not woven at all: an assertion on an awaited
  value, `(await x).ShouldBe(y)`, sits under the await's hidden sequence point and is never woven (kronikol-c6's #212
  plan, F8: 27 such statements in BreakfastProvider; 17 of its 97 rewritten subjects start `(await`). Both runs see the
  same input, so the before and after counts compare.

## 2. What the code does today

### 2.1 The issue's labels (RUN)

`harness/probe` is a console app whose Shouldly and FluentAssertions statements are woven by the published packages,
with the same shapes through `Track.That`. Each case's notes are read back from the logger, as the #163 plan's probe
did. `s0/` holds the 5.12.0 runs (Debug and Release identical); `s2/` adds the audit's cases (`Audit210.cs`).

| Case | Statement | Label on 5.12.0 |
|---|---|---|
| W01 | `result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldNotBeNullOrEmpty()` | Result split percentage to string( culture info invariant culture) should not be null or empty |
| W02 | `result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.GBP))` | Result currency iso alpha3 should be nameof(CurrencyCode.GBP) |
| W03 | the same with `USD`, failing | ✗ … should be nameof(CurrencyCode.USD) |
| W04 | `….ShouldBe(Currencies.Gbp)`, a `const string` holding "GBP" | … should be Gbp |
| W05 | `….ShouldBe(Currencies.Usd)`, failing | ✗ … should be Usd |
| W06 | `….ShouldBe(Currencies.GbpReadonly)`, a `static readonly` "GBP" | … should be 'GBP' |
| W07 | `const string gbp = "GBP";`, then `….ShouldBe(gbp)` | … should be gbp |
| W08 | `result.Count.ShouldBe(Limits.MaxRetries)`, a `const int` 3 | Result count should be MaxRetries |
| W09 | `result.Big.ShouldBe(Int32.MaxValue)` | Result big should be MaxValue |
| W10 | `result.Code.ShouldBe(CurrencyCode.GBP)`, an enum | Result code should be GBP |
| W11 | `var expected = nameof(CurrencyCode.GBP);`, then `….ShouldBe(expected)` | … should be 'GBP' |
| W12 | `result.Stage.ShouldBe(nameof(ApplicationStage.Cancelled) + "")` | Result stage should be nameof(ApplicationStage.Cancelled) + "" |
| W13 | `result.Errors.ShouldContainKey(nameof(Request.Email))` | Result errors should contain key nameof(Request.Email) |
| W14 | `result.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe("2026-10-27")` | Result date to string("yyyy-MM-dd",  culture info invariant culture) should be "2026-10-27" |
| W15 | `result.Items.Count(x => x.IsActive).ShouldBe(2)` | Result items count(x => x is active) should be 2 |
| W16 | `result.Get<Item>().Name.ShouldBe("a")` | Result get< item>() name should be "a" |
| W17 | `new Uri(result.Link).Host.ShouldBe("example.com")` | New  uri(result link) host should be "example.com" |
| W18 | `result.Map[Keys.Primary].ShouldBe(1)` | Result map[ keys primary] should be 1 |
| W19 | `result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldBe("12,5")`, failing | ✗ Result split percentage to string( culture info invariant culture) should be "12,5" |
| F01 to F03 | W01, W02 and W04 in FluentAssertions | the same labels |
| T01 to T03 | W01, W02 and W04 through `Track.That` | the same labels |
| A01 to A03 | `result.StatusCode.ShouldBe(HttpStatusCode.OK)`, the same failing against `NotFound`, and in FluentAssertions: a framework enum | Result status code should be OK; ✗ … should be NotFound |
| A04 | `result.SplitPercentage.ShouldBe(Prices.Split)`, a `const decimal` 12.5 | Result split percentage should be '12.5' (captured today) |
| A05 | `result.MaybeCode.ShouldBe(CurrencyCode.GBP)`, a `CurrencyCode?` | Result maybe code should be 'GBP' (captured today) |
| A06 | `result.Code.ShouldBe(Defaults.Code)`, an enum-typed `const` | Result code should be Code |
| A07 | `result.Map.Should().ContainKey(Keys.Primary).WhoseValue.Should().Be(1)` | Result map should contain key 1 |
| A08 | `Should.Throw<InvalidOperationException>(() => Data.Fail()).Message.ShouldContain("boom")` | Should throw InvalidOperationException "boom" |
| A09 | `await Track.ThatAsync(async () => { await Task.Yield(); result.Code.ShouldBe(expected); })` | two notes: the woven statement's, Result code should be 'GBP', and `Track.ThatAsync`'s, its code with its line breaks (§8 item 8) |

### 2.2 The subject (READ, RUN)

`Format` finds the subject: the code before Shouldly's top-level `.ShouldX(`, before FluentAssertions' `.Should().`, or
inside TUnit's `Assert.That(…)`. `FormatSubject` then turns it into words:

1. `SplitOutsideLiterals(subject, '.')` cuts it at every dot outside a literal. It counts no brackets, so a dot inside a
   call's arguments, an index or a type argument cuts too.
2. A piece that is exactly `ToString()` is dropped (4.14.8, the #163 plan's F10), and `First()` and `Last()` move to
   the front ("First value of …").
3. `Words` splits each piece on case and lower-cases it, keeping only literals as written (4.14.8, the #163 plan's F9).
4. `SplitPascalCase` starts a word before a capital after any character that is not a capital. Inside code, that
   character is a bracket or a space as often as a letter.

Which entry points reach which branch (READ, `AssertionWeaver.cs`):

| Written as | Woven at | Arguments the weave captures | Read by |
|---|---|---|---|
| FluentAssertions, AwesomeAssertions or TUnit's Should syntax, `x.Should().M(…)` | `:856-863`, `:873` | the first call after `Should()` (`:1170-1179`) | the `.Should().` split (`:51-73`) |
| Shouldly `x.ShouldX(…)`, and a suite's own `[ShouldlyMethods]` | `:887-901` | the call (`:1165-1169`) | `FormatShouldly` (`:93-99`, `:178-201`) |
| Shouldly `Should.Throw<T>(…)`, `Should.ThrowAsync<T>(…)` | the same | the same | `FormatShouldly`'s `Should.` branch (`:88-92`) |
| TUnit `Assert.That(x).M(…)` | `:875-877` | none: the call must be an instance method (`:1173`) and the text must hold `.Should()` (`:1176-1178`) | `FormatTUnit` (`:138-174`) |
| xUnit, NUnit and MSTest `Assert.*` | not woven (`WeaveAssertionsTask.cs:169-180` points to `Track.That`) | none | no branch: the code as written (`:54`) |

`harness/formatter/probe-5.12.0.txt` runs 42 shapes through the formatter. Besides the issue's, they show
`Math round(result total, 2)`, `String join(",", result ids)`, `Dict[ keys primary]`, `((int)response status code)`,
`(result total + result tax)`, `Date time parse(result when) year`, `Json serializer serialize(result body)` and
`Result value to string( culture info invariant culture) to upper invariant()`. All three libraries share the subject
rule, so all three show the defect; 5.14.0's labels are identical.

The #163 plan met this rule once already. It named the cause in general (`ASSERTION_VALUE_SUBSTITUTION_PLAN.md:261-262`)
and fixed it for literals only, in 4.14.8 (its F9). Its test (`AssertionExpressionFormatterTests.cs:307`) pins
`x.ToString("G")` as `X to string("G")`, which the new rule keeps.

### 2.3 The constants (READ, RUN)

The weave's argument capture (4.16.0, the #163 plan's R2) passes each argument of the assertion call as the call
receives it, under the argument's text (`DetectCapturedArguments`, `AssertionWeaver.cs:1138-1227`). It leaves an
argument out when the argument's text names nothing (`EvaluatesSomething`, `:1319-1365`: `5`, `"A1"`,
`new[] { 1, 2 }`, a lambda), and when its value comes straight from a constant (`IsConstant`, `:1367-1382`: `ldc`,
`ldstr` or `ldnull`, through a conversion or a `box`), "its text already says what it is". The passing and the
failing label are both built from the arrays this fills (`AssertionPassedWithValues`, `:2944-2952`;
`AssertionFailedWithValues`, `:3032-3039`).

| Text | Is the text the value's own spelling? | Today |
|---|---|---|
| a literal: `5`, `"GBP"`, `'x'`, `true`, `null` | yes | left out by `EvaluatesSomething` |
| an enum member: `CurrencyCode.GBP`, `Perm.Read \| Perm.Write` | yes: the label shows the member's name | left out by `IsConstant` |
| `nameof(CurrencyCode.GBP)` | no: the value is `"GBP"` | left out by `IsConstant` (`NotEvaluated` exempts only the keyword, `:1314-1317`) |
| a `const` field or local: `Currencies.Gbp`, `gbp`, `Limits.MaxRetries`, `Int32.MaxValue` | no | left out by `IsConstant` |
| constant arithmetic or concatenation with a name: `Limits.MaxRetries + 1`, `nameof(X) + "s"` | no | left out by `IsConstant` |
| an enum-typed `const`: `Defaults.Code` | no: it names the field, not the member | left out by `IsConstant` |
| a `const decimal`, or a constant converted to `Nullable<T>` | no | **captured**: the compiler builds them with `newobj`, where `IsConstant` stops (`:1374-1377`; the audit's IL build) |

Every "left out" row whose text is not the value's spelling is drawn as its code. Then `FormatArgs`
(`AssertionExpressionFormatter.cs:446-465`) applies `StripSimpleEnumPrefixes` (`:589-598`) to an argument list that is
one `A.B` with no value in it, and keeps only the second name. The rule was written for `HttpStatusCode.OK`, cannot tell
an enum member from a `const`, and cuts both. What is left is a bare name that is neither the code (the owner type is
gone) nor the value: W04 reads `Gbp` for "GBP", W08 `MaxRetries` for 3.

Three paths never capture an argument, so no value reaches them:

- **Awaited assertions** (FluentAssertions' `…Async`, TUnit's Should syntax awaited): `ArgumentCapture = isAwaited ?
  null : …` (`AssertionWeaver.cs:734-737`, `Assertion-Tracking.md:252-253`).
- **TUnit's `Assert.That`** (above).
- **`Track.That` and `Track.ThatAsync`**, which take a delegate (`Track.cs:50-121`) and read values off its closure
  (`ClosureValueResolver.cs:142-184`), where a constant never is: T03 reads `Gbp`.

### 2.4 How often, in real suites (RUN)

`harness/corpus/extract.py` collects every one-line Shouldly, FluentAssertions and TUnit statement in a source tree
(a trailing comment is not part of the statement), `extract_multiline.py` those spread over lines.
`formatter/published.cs` (the 5.12.0 package) and `formatter/probe.cs` (the prototype) label them. `corpus/classify.py`
counts the shapes and looks each `A.B` argument up in the source; `bracket_space.py` counts labels with a space inside
a bracket or a line break, outside literals and the lambda brackets.

| On 5.12.0 | BreakfastProvider | `examples/` | AutoMapper | MediatR | Marten |
|---|---|---|---|---|---|
| Library | FluentAssertions | FluentAssertions | Shouldly | Shouldly | Shouldly |
| One-line assertion statements | 1,016 | 53 | 1,541 | 262 | 7,523 |
| Subject rewritten into words (F1 to F3, `classify.py`) | 97 | 1 | 230 | 8 | 1,547 |
| A space inside a bracket or a line break (`bracket_space.py`) | 54 | 0 | 62 | 2 | 258 |
| Two spaces in a row | 9 | 0 | 123 | 8 | 296 |
| A `nameof(…)` argument (F4) | 0 | 0 | 15 | 0 | 100 |
| A `const` argument read as a bare name (F5) | 116 | 0 | 0 | 1 | 26 |
| of which the name shown is not the value | 49 | 0 | 0 | 1 | 19 or more |
| An enum member argument, its type cut off (by design) | 164 | 6 | 28 | 12 | about 142 |
| Multi-line statements, and of them with a line break in a `Track.That` label | 49, 49 | 0 | 38, 23 | 20, 13 | 582, 123 |

- BreakfastProvider's 164 enum members are `HttpStatusCode` (157), `DateTimeKind` (5) and gRPC's `StatusCode` (2). Its
  14 static fields of types outside the suite (`Guid.Empty` 13, `DBNull.Value` 1) and 4 of its own
  (`DailySpecialDefaults.CinnamonSwirlId` 3, `ToppingDefaults.KnownBlueberryToppingId`) are cut the same way when no
  value is known; a woven run reads a field by value.
- `classify.py` finds a type in the suite's own source; for one outside it, the owner's name decides. Marten's 26
  constants are 13 of its own and 13 of other packages' (`PostgresErrorCodes.UniqueViolation` reads `UniqueViolation`
  for "23505", and `StatusCodes`, `StorageConstants`, `OtelConstants`); its enum members are 23 of its own and about
  119 of 169 outside `A.B` arguments, the others being 30 static fields (`Guid.Empty` 22, `DateTimeOffset.MinValue` 6,
  `DBNull.Value` 2), the 13 constants and 7 not told apart. AutoMapper's 28 are 19 of its own and 9 outside
  (`ConsoleColor`, `ServiceLifetime`, `LogLevel`), beside 5 static fields; MediatR's 12 are `ServiceLifetime` and
  `LogLevel`.
- Of BreakfastProvider's 116 `const` statements, 32 are awaited (`await ….Should().…`, TUnit's Should syntax), and so
  are 16 of the 49; R2 reaches at most 84 and 33 of them (§3.10).
- The 49 include `should be DefaultTemperature` (180), `should be DefaultDuration` (25), `should contain
  MaxToppingsExceeded` ("Maximum toppings exceeded."), `should be HeartbeatStatus` ("ok"), `should be FreshGoatMilk`
  ("Some_Fresh_Goat_Milk"), `should contain key CorrelationId` ("X-Correlation-Id") and `should have count
  ExpectedToppingCount` (5). The other 67 show the value only because the field happens to be named after it.
- The issue's own count, "ten such labels in three of nineteen reports", is the grep's: the Shouldly suites pass
  `nameof` in 115 of 9,326 assertions.
- Joined as the weave joins them (`join_lines.py`), BreakfastProvider's 49 multi-line statements read on one line on
  5.12.0 and on the prototype.

On the prototype, no label in the five corpora holds a space inside a bracket or a line break, multi-line ones
included. The labels that change: BreakfastProvider 106 of 1,016 (its 97 subjects and 9 chains), the examples 1
(`Json is valid(_cake response string)` becomes `Json is valid(_cakeResponseString)`), AutoMapper 292 of 1,541, MediatR
32 of 262 and Marten 1,899 of 7,523; of the multi-line statements as `Track.That` passes them, 49 of 49, 38 of 38, 18 of
20 and 361 of 582. `corpus/changes.py` finds none that loses its sentence, and one that grows by more than 40
characters: §2.6's, which was cut off before. The three statements `classify.py` still counts as rewritten drop a `!`,
by design. A random sample of Marten's changes (`seed 210`) reads, for example,
`(await session query< target>() count async()) should be 100` as `(await session.Query<Target>().CountAsync()) should
be 100` and `Rows single(x => x category ==  colors red) flagged should be 2` as
`Rows single(x => x.Category == Colors.Red) flagged should be 2`.

### 2.5 The issue's suggestions, critically

1. **"Humanize an argument list as one phrase: `to string(culture info invariant culture)`."** This removes the stray
   space but keeps rewriting code into words. A member access loses its dot and reads as a phrase (`culture info
   invariant culture` names nothing a reader can look up), and a lambda changes meaning:
   `Single(x => x.Id == expected.Id)` would read `single(x id == expected id)`. The two halves of one label would also
   read the same code two ways. An argument has kept its code since 4.12.3 and 4.14.8 (`Assertion-Tracking.md:573`),
   so `….ShouldBe(CultureInfo.InvariantCulture.Name)` reads `should be CultureInfo.InvariantCulture.Name` on the
   right, and the subject would read `culture info invariant culture` on the left.
2. **"Or drop an argument that is only a culture or format provider: `split percentage to string`."** There are three
   problems with this:
   - It contradicts the rule since 4.14.8, which drops `.ToString()` from a subject whole. The label would read
     `split percentage`, not `split percentage to string`.
   - The culture is part of what was compared, and is exactly what explains W19's failure: the invariant culture makes
     "12.5", the test expected "12,5", and a label without the culture hides the cause.
   - "Only a culture or format provider" needs the argument's type, which the formatter does not have. It would
     become a list of names (`CultureInfo.InvariantCulture`, `CultureInfo.CurrentCulture`, `provider`, …).
3. **"Show a `nameof` argument as the string it is, as a literal would be."** Right for `nameof`, but `nameof` is one
   case of F5. Reading `nameof` alone leaves every `const` reading as a bare name (116 labels in BreakfastProvider, 49
   of them naming something other than the value). Also, a literal is shown as written, in double quotes, while
   `nameof`'s string is a value the compiler made. It should read as a value does, in single quotes: `'GBP'`, as the
   issue's own "should be 'GBP'" has it, and as W11 (a local holding it) already reads.
4. **The diagnosis.** "The argument list is humanized word by word, and the opening parenthesis keeps the space before
   the first word" is close to the effect but not the mechanism. The argument list is never read as a list: the
   subject is cut at the dot inside it, and the space is the case splitter's (F1, F2). The same cut breaks type
   arguments, indexes and lambdas, which a grep for `nameof(` could not find.
5. **The count.** "Ten such labels in three of nineteen reports" counts the grep's hits. Every subject with a call that
   takes arguments is rewritten (1,785 of 9,326 in the Shouldly suites), and a `const` argument reads as a bare name
   with nothing in it to grep for.

### 2.6 FluentAssertions' split (RUN)

`Format` takes as the subject everything before the first match of `\.Should\(\)\.` (`:51-57`). Inside brackets, that
match is an inner assertion; inside a literal, it is text. BreakfastProvider's
`(await act.Should().ThrowAsync<ClickHouseServerException>()).Which.Message.Should().StartWith("Code: 60.")` reads
`(await act should throw async <ClickHouseServerException>`, cut off with its bracket still open, and
`"a.Should().b".Should().Be("x")` reads `"a should b". should(). be("x")`. Shouldly's `TopLevelShouldCall`
(`:176-201`) skips brackets and literals; FluentAssertions' split never did.

### 2.7 Line breaks (RUN, READ)

The weave reads a statement's own columns and joins its lines with a space (`CutColumns`, `AssertionWeaver.cs`, after
`ReadSourceText`). `Track.That` and `Track.ThatAsync` get their text from `[CallerArgumentExpression]`, which passes the
code as written, line breaks and indentation included. The formatter removes whitespace only before a dot
(`CleanCode`); `FormatShouldly` trims each top-level argument (`:114-120`), so in Shouldly only a break inside an
argument survives, while FluentAssertions keeps one after `(` ("should be \n    expected.Value"), and code no
library's shape matches keeps all of them (`Assert.Equal(\n    expected,\n    actual)`).

Passed as `Track.That` would pass them, every one of BreakfastProvider's 49 multi-line assertion statements gives a
label with a line break in it (`corpus/breakfastprovider-multiline-5.12.0.txt`), such as `Items should contain [ x =>`
with `x.Name == "A" ]` on the next line, and so do 159 of the Shouldly suites' 640. Such a note's sentence breaks
early, and the step's row breaks with it.

### 2.8 Chains (F9, F10) (RUN)

`harness/formatter/chains.txt` runs the chain shapes of the three libraries:

| Statement | 5.12.0 |
|---|---|
| `await Assert.That(price).IsEqualTo(10.0).Within(0.5)` | Price is equal to 0.5 |
| `await Assert.That(name).IsEqualTo("bob").IgnoringCase()` | Name is equal to |
| `await Assert.That(text).IsEqualTo("Rock.And.Roll")` | Text is equal to |
| `Should.Throw<InvalidOperationException>(() => service.Run()).Message.ShouldContain("boom")` | Should throw InvalidOperationException "boom" |
| `Should.Throw<ArgumentException>(act).ParamName.ShouldBe("id")` | Should throw ArgumentException "id" |
| `Should.Throw<AggregateException>(() => run()).InnerExceptions.ShouldHaveSingleItem()` | Should throw AggregateException |
| `dict.Should().ContainKey("a").WhoseValue.Should().Be(1)` | Dict should contain key 1 |
| `act.Should().Throw<ClickHouseEmulatorException>().Which.Code.Should().Be(456)` | Act should throw <ClickHouseEmulatorException>, 456 |
| `x.Should().Be("Rock.And.Roll")` | X should be |
| `x.Should().NotBeNull().And.HaveCount(2)` | X should not be null |

`ParseMethodAndArgs` takes the method's name from the first call and the arguments from the last top-level call, so
every link after the assertion's own call is lost and the last one's arguments are shown as the first one's. Two facts
pin "the first assertion only" for `.And.` (`AssertionExpressionFormatterTests.cs:71-75` and `:477`): a failing second
half is labelled with the half that passed (**Q8**). BreakfastProvider has 14 chains past the first call (`Which` 5,
`Subject` 4, `WithMessage` 5; `corpus/fa_chains.py`) and no `.And.`. The Shouldly suites chain past a `Should.Throw`
call in 13 of their 305 `Should.Throw` statements, all spread over lines (`Message` 9, `ShouldBeSameAs` 2,
`ParamName`, `InnerException`; `corpus/shouldly_chains.py`); the others hold the exception in a variable.

### 2.9 Other code the formatter misreads (F11 to F17) (RUN)

`harness/formatter/edges.txt`, `audit.txt`, `lambdas.txt` and `c6.txt` hold the shapes; each has a `-5.12.0.txt` and a
`-prototype.txt` output.

- F11: `result.Should().BeOfType<List<int>>()` reads "Result should be of type <List<int>".
- F12: `result.Total.ShouldBe(\n // the discount is applied first\n expected.Total)` reads "Result total should be // the
  discount is applied first\n expected.Total". No statement in the five corpora holds a comment inside it.
- F13: `result.Name.ShouldBe($"{(flag ? "a.b" : "c")}.Should().")` reads `Result name should be($"{(flag ? "a b" : "c")}
  should ")`. Verbatim strings, raw strings, `")"` and `')'` read right. Marten has one such statement.
- F14: Marten's `ex.Message.ShouldContain("…RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee\");")` reads
  with `[ … ]` round the literal.
- F15: Marten's `Should.Throw<InvalidOperationException>(() => opts.Events.RegisterTagType<EnrolleeId,
  string>(x => x.Value.ToString(), "enrollee"))` is split at the type arguments' comma.
- F16: `async () => { await Task.Yield(); "x".Should().Be(expected); }` reads "Async () =>\n {\n await  task
  yield();\n "x" should be expected", and `() => { a.ShouldBe(1); b.Total.ShouldBe(2); }` as written.
- F17: #212's S23 and S18a (kronikol-c6, `C:/Code/Kronikol-212/plans/AWAITED_ASSERTION_LABELS_PLAN.harness/probe/
  summary-212b.txt`): after #212's head reader, `Assert.That(() => _client.GetOrderAsync(r)).Throws<…>()` reads
  `() => _client get order async(r) throws <InvalidOperationException>`, and
  `Console.WriteLine((await Should.ThrowAsync<…>(…)).Message)` is the whole statement.

### 2.10 Where a label goes (READ)

`Track.LogAssertion` makes the label and keeps it twice (`Track.cs:343-372`): as a sub-step of the active step
(`ScenarioStep.Text` with `IsAssertion`, `StepCollector.cs:204-236`, while `IncludeTrackedAssertionsInStepList` is on,
its default) and as a `✓ label` note in the diagram. From there it reaches TestRunReport's JSON, `.krkl`, XML and YAML,
the Specifications files, the HTML page (the step, `data-search`, the search index, the diagram source),
`Failures.md` and `Failures.jsonl`, and the NDJSON capture. It does not reach OTLP export, CTRF or the history ledger.

**Nothing identifies anything by a label across runs.** Merge identifies a scenario by stable id, attempt, result,
duration and error message (`MergeableReportMerger.cs:441-455`); `kronikol query diff` by stable id
(`QueryCommand.Search.cs:705-713`); history by results, durations, call shapes that skip markers
(`InteractionShape.cs:237`) and the error message (`HistoryRunBuilder.cs:110-123`); failure clusters by the error
message (`FailureClusterer.cs:18-31`). The label never reaches the error message: `Track` rethrows the original
exception (`Track.cs:63-68`). Within one run, three comparisons include a label's text: the "All diagrams identical"
badge (`ReportGenerator.cs:2360-2372`), merge's drop of equal diagrams (`MergeableReportMerger.cs:457-465`) and
`kronikol query compare`, which compares steps by position, so a merge of shards from before and after a release
shows each changed label as a difference. A saved `kronikol query grep` written against today's wording stops
matching.

**How a label is drawn.** A note's text goes through `EscapeLoaderMarkup` (`PlantUmlCreator.cs:910-940`) and
`EscapePreprocessorLine` (`:854-876`), not through `EscapeCreoleMarkup`: "step names, test names and assertion text
keep their markup" (`DIAGRAM_COLOURS_PLAN.md`, its change list). So creole in a label styles it: `<B>`, `<U>`, lower-case
`<b> <i> <s> <u> <w>`, `""`, `**`, `//`, `--`, `__` and `~~` pairs, `[[…]]` and `<<x>>`
(`DIAGRAM_COLOURS_PLAN.harness/fuzz-npm-new-contexts.txt:620-695`). `<Item>` and `List<List<int>>` draw as written.
Arguments have kept their code since 4.14.8, so today's labels can hold every one of these; R1 adds the subject's code
(**Q9**). `Failures.md` escapes only `|` and line breaks in a step's text (`FailuresDigestGenerator.cs:768`, `:774`,
`:863-866`) and `Specifications.md` nothing (`SpecificationsMarkdownGenerator.cs:179-197`), so there `<Item>`, `*` and
`_` are Markdown (§8).

### 2.11 How the rules got here (READ)

- `StripSimpleEnumPrefixes` and the case-split subject come from the first formatter (2.x), which was written for
  `response.StatusCode.Should().Be(HttpStatusCode.OK)`.
- 4.12.3 (SHOULDLY_ASSERTIONS_PLAN F13) kept literals in arguments. 4.14.8 (#163 R1) made arguments keep their code,
  with a value only in place of the code it was read for, and kept literals in the subject (F9). 4.16.0 (#163 R2)
  passed each argument's value from the weave, constants excepted (commit `66d7cc93`).
- The #163 plan's probe met `nameof(expected.Colour)` and filed it as "as written" (its §2.3). Its F9 test pins it
  (`AssertionExpressionFormatterTests.cs:305`), and so does `TrackValueResolutionTests.cs:443-449`, whose point is that
  the chain inside `nameof` is not read as `expected`'s value.
- The wiki states the rules side by side (`Assertion-Tracking.md` at `ae3393f`, unchanged at `183dbb0`): "code with no
  value is shown as written" (`:573`), "Enum prefixes are stripped for simple arguments" (`:563`), and "Not stored: a
  constant (`"A1"`, `2`, `Colour.Blue`)" (`:250`).

## 3. Design

### 3.1 The rule

**A label uses words only for a member path and for the calls of its assertion's chain. It keeps all other code
exactly as written, on one line. Wherever the value is known, to Kronikol or to the compiler, the label shows the value
in quotes.**

Arguments have followed this rule since 4.14.8. R1 extends it to the subject, the chain and `nameof`; R2 extends it to
the other values the compiler knows.

### 3.2 The subject (R1, F1 to F3, F17)

- The subject is cut into links at each dot outside literals **and outside brackets and type argument lists**:
  `result.SplitPercentage.ToString(CultureInfo.InvariantCulture)` is `result`, `SplitPercentage` and
  `ToString(CultureInfo.InvariantCulture)`.
- A link is a name, then optionally a type argument list, then any number of call and index brackets. The **name** is
  read as words, as today (split on case, lower-cased, `_` trimmed). **The rest is kept as written**:
  `to string(CultureInfo.InvariantCulture)`, `get<Item>()`, `map[Keys.Primary]`, `count(x => x.IsActive)`.
- The first link may instead be a literal (kept as written since 4.14.8), a bracketed expression or `new T(…)`. It is
  kept as written, and the links after it are read as above: `(await Fail(connection, "SELEC 1"))`,
  `New Uri(result.Link) host`.
- A subject that is not one such first link followed by such links (an operator outside brackets, a conditional) is
  kept as written, whole (**Q2**); it keeps its own case, and only a subject read as words starts with a capital.
- A subject that is a lambda, TUnit's `Assert.That(() => _client.GetOrderAsync(r))`, reads as its body, as Shouldly's
  `Should.Throw(() => …)` does not show the action: "Client get order async(r) throws <InvalidOperationException>"
  (#212's S18a).
- A null-conditional index reads as a null-conditional member already does: `Items?[0]` as `items[0]`.
- Unchanged: a bare `ToString()` is dropped, as since 4.14.8; `First()` and `Last()` read "First value of …".
- A `ToString(…)` with arguments keeps them, as written (**Q1**): the culture or format is part of the value compared
  (W19).
- A type argument list is told from a less-than comparison as `AccessChain` tells it (`TypeArgumentsEnd`,
  `AccessChain.cs:309-330`): names, dots, commas, whitespace, `?`, `[]`, tuple brackets and nested lists, up to the
  matching `>`. The prototype's first copy accepted neither tuples nor nesting; the audit and the probe caught both.

### 3.3 `nameof` (R1, F4)

- In the arguments, outside literals, `nameof(operand)` reads as the string C# makes of it, in quotes as a value: the
  operand's last name, without type arguments or `@` (the C# specification's rule for `nameof` expressions).
  `nameof(CurrencyCode.GBP)` reads `'GBP'`, `nameof(List<int>)` `'List'`, `nameof(@class)` `'class'`,
  `nameof(this.Name)` `'Name'` and `nameof(global::System.String)` `'String'`. An operand that does not end in a name is
  kept as written.
- It is read after the weave's values are put in, so an argument whose whole value the weave passed keeps that value.
  R2 passes `nameof`'s value too (`EvaluatesSomething` is true for it, since the operand's names count,
  `:1344-1362`), and the two print alike, `'GBP'`. That order also covers the one case where the text rule would be
  wrong: with a method named `nameof` in scope, `nameof(x)` is a call, and the weave captures its value.
- Inside an interpolated string it is part of the literal and stays as written. In the subject it stays as written
  (**Q6**).

### 3.4 One line (R1, F7)

Code kept as written, in the subject, in the arguments and when no library's shape matches, reads on one line. A run of
whitespace outside literals becomes one space, and none is left after `(` or `[` or before `)` or `]`, where a line
break inside a call's brackets would otherwise leave one. Literals are not touched (§8 item 4).

### 3.5 FluentAssertions' split and `.And.` (R1, F6, F9)

- The subject ends at the first `.Should().` **outside brackets and literals**, as Shouldly's does. Then
  `(await act.Should().ThrowAsync<T>()).Which.Message.Should().StartWith("Code: 60.")` reads
  `(await act.Should().ThrowAsync<T>()) which message should start with "Code: 60."`. A statement that starts with `(`
  is never touched by #212's statement-head reader (kronikol-c6, by message), so this split owns the shape.
- `.And.` (FluentAssertions) and `.And.` and `.Or.` (TUnit) are found outside brackets and literals only. What follows
  them is dropped, as today, unless the owner answers **Q8** yes: then it reads "and …" or "or …". (Q8 was answered
  yes; R1 reads them as links of the chain, §3.6.)

### 3.6 The chain (R1, F10, F11)

The assertion's first call reads as today, with **its own** arguments; its type arguments end at their matching `>`.
Each link after it reads in order: a member as words, a call as its words and its arguments, `Should()` as "should".
A chain that goes on to a value of the assertion follows a comma, a call on the assertion itself a space; a last link
that only hands the value on (`Which`, `Subject`, `And`) is not read.

| Statement | After R1 |
|---|---|
| `IsEqualTo(10.0).Within(0.5)` | Price is equal to 10.0 within 0.5 |
| `IsEqualTo("bob").IgnoringCase()` | Name is equal to "bob" ignoring case |
| `Should.Throw<X>(() => service.Run()).Message.ShouldContain("boom")` | Should throw X, message should contain "boom" |
| `Should.Throw<ArgumentException>(act).ParamName.ShouldBe("id")` | Should throw ArgumentException act, param name should be "id" |
| `dict.Should().ContainKey("a").WhoseValue.Should().Be(1)` | Dict should contain key "a", whose value should be 1 |
| `act.Should().Throw<X>().Which.Code.Should().Be(456)` | Act should throw <X>, which code should be 456 |
| `act.Should().Throw<X>().WithMessage("boom")` | Act should throw <X> with message "boom" |
| `var row = list.Rows.Should().ContainSingle().Subject` | List rows should contain single |
| `result.Should().BeOfType<List<int>>()` | Result should be of type <List<int>> |

The weave captures the first call's arguments, so the value it read now shows where it belongs; a later call's
arguments show as code. A `Should.Throw` action that is a delegate variable (`act`) is shown, as it is today when
nothing follows the call; a lambda is not.

Two readings were settled in execution, on the Shouldly corpora: the comma goes before a member that moves on from an
assertion (the first call, a call that asserts, or what `.And.` starts), so a member further along that value's path
follows a space (`Should throw X, errors single names single should be "Id"`, where the first rule read `errors single,
names single`); and a later Shouldly call reads by Shouldly's own rule, as the first does
(`…, inner exception should be of type NotSupportedException`, not `<NotSupportedException>`).

### 3.7 Other code (R1, F12 to F17)

- **F14:** the lambda brackets go round an argument list that holds a lambda's `=>` outside its literals.
- **F15:** Shouldly's argument split counts `(`, `[` and `{`, and steps over a type argument list.
- **F16:** a lambda's head, `() =>` or `async () =>`, is stripped with any whitespace after the arrow, and a block
  body reads as its assertion statements, each read on its own and joined by `; `: `A should be 1; B total should be
  2`. This lives just before #212's head lines (`:30-45`); the second release to ship rebases (§5).
- **F17:** when no assertion is found at the top level, the first bracket that holds one alone, after an `await`, is
  read on its own: `Console.WriteLine((await Should.ThrowAsync<T>(…)).Message)` reads "Should throw T". An assertion
  in a lambda's body runs elsewhere (for each element, later), so `results.ForEach(r => r.Should().BeTrue())` stays as
  written.
- **F12 (designed, not prototyped):** the formatter drops `/* … */` and, in `Track.That`'s text, `// …` to the end of
  its line, outside literals; the weave drops a line's `// …` before it joins the lines (R2), since after the join
  where the comment ended is lost.
- **F13 (designed, not prototyped):** `LiteralMask` reads an interpolated string's holes as code, with literals of
  their own, up to the string's closing quote.

### 3.8 Constants (R2, F5)

- In `DetectCapturedArguments`, a constant argument (`IsConstant`) is left out **only when its text is its own
  spelling**. A literal already is left out, by `EvaluatesSomething`. The new check is for an enum's own members: the
  value's type is an enum (the parameter's type with the call's type arguments put in, as `BoxType` finds it, or the
  type of a `box` on the way), and every name in the text is the enum's own name, one of its members, a part of its
  namespace or an outer type: `CurrencyCode.GBP`, `Perm.Read | Perm.Write`. The walk to the `box` follows only what
  `IsConstant` follows (a conversion or a box); the prototype's first copy walked past the constant into the slot
  below it, another argument's (the R2 review).
- Every other constant goes through the capture path unchanged (spilled, copied into the values array under the
  argument's text, reloaded): `nameof`, a `const` field or local, constant arithmetic, an enum-typed `const`.
- A constant can lower the start of the stack range the capture copies. When the wider range cannot be copied
  (`:1218-1219`), the statement keeps the values it copied without its constants instead of losing them all.
- A framework enum resolves at weave time: A01 to A03 (`HttpStatusCode`) keep reading `OK` and `NotFound` in Debug
  and Release (`s2/`). An enum the weave could not resolve would fail the check and pass its value, `'OK'`; fact 17
  pins the framework case.
- No new member: the values travel in the arrays 4.16.0's weave already passes, and every `Kronikol` since 4.16.0
  substitutes an argument's whole value (`Assertion-Tracking.md:237-254`). The weave package does not depend on
  `Kronikol` and refuses only cores below 2.30.7 (`AssertionWeaver.cs:486-495`): a new weave beside a 4.14 core
  leaves values out and keeps today's names, a new core with an older weave reads `nameof` (R1) and leaves `const` as
  today.
- A statement whose only non-literal argument is such a constant moves from `Track.AssertionPassed` to
  `Track.AssertionPassedWithValues`; the wiki's "zero-overhead" sentence (`:256`) still holds for literals and enum
  members. The probe's woven `Probe.dll` (file sizes in 512-byte steps): with 22 statements, Debug 25,088 to 26,112
  bytes and Release 24,576 to 25,088 (`s0/`, `s1/`); with 31, Debug 31,232 to 32,256 and Release 30,720 to 31,744
  (`s2/woven-size.txt`).
- If the subject throws before the call, the captured slot keeps its placeholder, which prints as nothing
  (`Track.cs:243-247`), and the label shows the bare name, as today.
- Considered and not taken: emitting a constant's value at the logging call from a copy of its instructions, with no
  spill. It gives the same labels with about 8 fewer IL instructions per constant and covers a subject that throws
  first, but its one real gain, after an `await`, reaches no assertion today: Shouldly's async assertions take a
  delegate, FluentAssertions' `ThrowAsync` only `because`, and TUnit's calls are never captured (§2.2).

### 3.9 What a reader sees

From `s0/` and `s2/` (5.12.0) and `s1/` and `s2/` (the prototype on local packages; Debug and Release identical):

| Case | 5.12.0 | After R1 and R2 |
|---|---|---|
| W01 | Result split percentage to string( culture info invariant culture) should not be null or empty | Result split percentage to string(CultureInfo.InvariantCulture) should not be null or empty |
| W02 | … should be nameof(CurrencyCode.GBP) | … should be 'GBP' |
| W03 | ✗ … should be nameof(CurrencyCode.USD) | ✗ … should be 'USD' |
| W04, W05 | … should be Gbp; ✗ … should be Usd | … should be 'GBP'; ✗ … should be 'USD' |
| W06, W11 | … should be 'GBP' | unchanged |
| W07 | … should be gbp | … should be 'GBP' |
| W08, W09 | … should be MaxRetries; … should be MaxValue | … should be '3'; … should be '2147483647' |
| W10 | Result code should be GBP | unchanged |
| W12 | … should be nameof(ApplicationStage.Cancelled) + "" | … should be 'Cancelled' |
| W13 | … should contain key nameof(Request.Email) | … should contain key 'Email' |
| W14 | Result date to string("yyyy-MM-dd",  culture info invariant culture) … | Result date to string("yyyy-MM-dd", CultureInfo.InvariantCulture) … |
| W15 | Result items count(x => x is active) should be 2 | Result items count(x => x.IsActive) should be 2 |
| W16 | Result get< item>() name should be "a" | Result get<Item>() name should be "a" |
| W17 | New  uri(result link) host … | New Uri(result.Link) host … |
| W18 | Result map[ keys primary] should be 1 | Result map[Keys.Primary] should be 1 |
| T02 (`Track.That`) | … should be nameof(CurrencyCode.GBP) | … should be 'GBP' (R1 alone does this) |
| T03 (`Track.That`, a `const`) | … should be Gbp | unchanged (§3.10) |
| A01 to A05 | as §2.1 | unchanged: a framework enum keeps its name; a `const decimal` and a nullable constant were values already |
| A06 | Result code should be Code | Result code should be 'GBP' |
| A07 | Result map should contain key 1 | Result map should contain key 'primary', whose value should be 1 |
| A08 | Should throw InvalidOperationException "boom" | Should throw InvalidOperationException, message should contain "boom" |
| A09 | the woven note and `Async () =>…` | both notes read Result code should be 'GBP' |

After R1 alone, W04, W05 and W07 to W09 are unchanged, and W12 reads `'Cancelled' + ""`; R2 changes those.

### 3.10 What this does not reach

- **A `const` with no value to show** keeps the bare name: `Track.That` and `Track.ThatAsync` (T03), awaited
  assertions (32 of BreakfastProvider's 116 `const` statements, and 16 of the 49), TUnit's `Assert.That`, and a later
  call of a chain (`.WithMessage(Messages.Boom)`), since the weave captures the first call's arguments only.
  `StripSimpleEnumPrefixes` still cuts `A.B` for the sake of enum members. Showing `A.B` as written wherever no value
  is known is what §3.1's rule would say, but it changes every enum member argument with no value too (`OK` to
  `HttpStatusCode.OK`, 164 of BreakfastProvider's 1,016 labels): **Q5**.
- **`Track.That`'s constants could be reached** by reading the lambda's IL at run time (`GetMethodBody`,
  `Module.ResolveString`, the weave's rules ported from Cecil to reflection, a cache per method, a hop for an async
  lambda's state machine, nothing under NativeAOT). It needs no new API, so it would be a patch of its own; it is not in
  this plan. An `Expression<>` overload would be new surface, and C# rejects an omitted optional argument inside an
  expression tree, which every Shouldly and FluentAssertions call has.
- **Words written in capitals** in a subject are lower-cased (`HTTPStatus` reads `http status`), as #139 found for
  scenario titles: **Q7**.

## 4. Tests, in the order they are written

Each new fact is run red on 5.14.0 first, and each behaviour gets a mutation that turns a fact red.

### 4.1 R1 (`tests/Kronikol.Tests/Tracking/`)

| # | Fact | File | Red on 5.14.0 |
|---|---|---|---|
| 1 | `A_call_in_the_subject_keeps_its_arguments_as_written` (theory: W01 in Shouldly, FluentAssertions and TUnit, W14, W15, W16, W18, `Single(x => x.Id == expected.Id).Name`, `Math.Round(result.Total, 2)`, `GetProperty(Endpoints.Swagger.PancakesPath)`, `Get<(int, string)>()`, `Get<List<int>>()`, `Items?[0]`) | `AssertionExpressionFormatterTests.cs` | yes (the tuple row reads the same today: a guard) |
| 2 | `A_subject_that_is_not_a_member_path_is_kept_as_written` (`((int)response.StatusCode)`, `(result.Total + result.Tax)`, `(await Fail(connection, "SELEC 1"))`, `new Uri(result.Link).Host`, a conditional) | same | yes |
| 3 | `A_lambda_subject_reads_as_its_body` (#212's S18a, with and without `async`/`await`) | same | yes |
| 4 | `No_label_holds_a_space_inside_a_bracket_or_two_spaces` (every statement of BreakfastProvider's and the examples' statement files, the owner's code, copied in as test data, outside literals and the lambda brackets; the Shouldly suites stay a harness measurement, as two of them are under the Lucky Penny Software licence) | same | yes |
| 5 | `A_statement_over_several_lines_reads_on_one_line` (`harness/formatter/multiline.txt`'s five and `Assert.Equal(\n expected,\n actual)`, as `Track.That` passes them and as the weave joins them) | same | yes |
| 6 | `A_nameof_argument_reads_as_its_value` (W02, `List<int>`, `Dictionary<string, List<int>>.KeyCollection`, `@class`, `this.Name`, `global::System.String`, `nameof(X) + "s"`, in FluentAssertions and TUnit too; inside `$"…"` it stays) | same | yes |
| 7 | `A_value_the_weave_passed_wins_over_the_nameof_reading` | same | no: pins the order |
| 8 | `A_FluentAssertions_subject_ends_at_its_own_Should` (§2.6's two, and a `.Should()` inside a lambda argument) | same | yes |
| 9 | `And_and_Or_are_read_outside_literals_and_brackets` (`Be("Rock.And.Roll")`, TUnit `IsEqualTo("a.Or.b")`, `BeEquivalentTo(…, o => o.Excluding(…).And.Excluding(…))`) | same | yes |
| 10 | `A_chain_reads_each_call_with_its_own_arguments` (§3.6's table, in the three libraries) | same | yes |
| 11 | `A_lambda_head_and_a_block_body_read_as_their_assertions` (`async () =>`, a break after the arrow, a block with one and with two assertions, a block with none) | same | yes |
| 12 | `An_assertion_inside_other_code_reads_as_that_assertion` (#212's S23; `results.ForEach(r => r.Should().BeTrue())` stays as written) | same | yes |
| 13 | `Lambda_brackets_and_the_argument_split_count_code_not_literals` (F14, F15: Marten's two) | same | yes |
| 14 | `A_nameof_argument_reads_as_its_value_through_the_weave_and_Track_That` (W02 through `AssertionPassedWithValues`, T02 through `Track.That`) | `TrackValueResolutionTests.cs` | yes |
| 15 | A Playwright fact: a report whose run recorded W01, W02, W15, W16, W18 and §3.6's chains paints those labels in their notes, with Assertions shown, character for character, and no drawn line starts with `PlantUML ` (the engine's error picture) | `tests/Kronikol.Tests.EndToEnd` | yes |

Fact 15 matters because R1 keeps more code in labels, and a note's text keeps its creole (§2.10). `<Item>`,
`[Keys.Primary]`, `=>`, `&&` and `(int)` draw as written; a type argument named `B` or `U` and `[[…]]` would not
(**Q9**).

Two existing facts change. `AssertionExpressionFormatterTests.cs:305` expects `"Colour" should be 'Colour'`, and
`TrackValueResolutionTests.cs:443-449` expects a label ending in ` should be 'Colour'` with no `'Blue'` in it, which
keeps its point (the chain inside `nameof` is not read). A third, `TrackThatTests.ThatAsync_resolves_captured_variable_value`,
keeps its expectation, and passes through F16 instead of F6's accident. Measured: on the first prototype, the whole
core suite failed only the two (`s1/suites-5.14.0-prototype.txt`); with F6 and F10 it failed the third too, and F16
fixed it; on the final prototype it fails only the two again, 9,107 of 9,111 passing (`s2/suites-5.14.0-prototype.txt`).
The audit grepped every project under `tests/`, the end-to-end suite included, and found no other expected label that
the prototype changes. In execution, Q8's yes turned three more facts round: the two §2.8 names
(`AssertionExpressionFormatterTests.cs:71-75`, `:477`) and `Format_handles_multiline_expressions_with_whitespace_around_dots`
(three rows ending in `.And`). Q9's yes changed one fact that reads a note's source
(`TrackValueResolutionTests.A_call_after_a_path_is_not_read_as_a_member`: `Get<int>()` is written `Get~<int>()`, which
draws as written, and copy and the search index read back).

### 4.2 R2 (`tests/Kronikol.Tests.AssertionTracking/`, through `WovenRun`, in Debug and Release)

| # | Fact | Red on 5.14.0 |
|---|---|---|
| 16 | `A_constant_that_names_something_else_reads_as_its_value` (theory: W04, W05, W07, W08, W09, W12, an enum-typed `const`, `Limits.MaxRetries + 1`; Shouldly and FluentAssertions; passing and failing; as built, also a `const string` above another argument's enum box and a constant below a `ReadOnlySpan<char>`) | yes |
| 17 | `An_enum_member_keeps_its_name` (W10, `Perm.Read \| Perm.Write`, an enum passed as `object`, and a framework enum, `HttpStatusCode.OK`, which has to resolve at weave time) | no: pins today's |
| 18 | `A_const_decimal_and_a_nullable_constant_read_as_before` (captured today, `newobj`) | no: pins today's |
| 19 | `A_constant_that_widens_the_copy_keeps_the_other_values` (a suite's own `[ShouldlyMethods]` helper whose constant argument sits below a `ref` argument, whose address no local can hold, with a call above it that only the copy reads; as planned it sat below a `ReadOnlySpan<char>`, which can be a local, see the Log) | no on 5.14.0 (no constant is copied); red on R2 without the fallback |
| 20 | The new fixtures through the ILVerify net (`WovenIlVerifiesTests`) | no |

On 5.12.0 and on the prototype the weaver suite passes with the same counts (406 passed, 28 skipped), so no existing
fact pins the constant rule either way.

### 4.3 Mutations

Each must turn the fact named red: links cut at every dot again (1); a link's brackets read as words (1); a subject
that is not a member path read as words (2); a lambda subject kept as written (3); a space kept after `(` (4, 5); the
`nameof` reading removed (6, 14); the `nameof` reading done before the values (7); FluentAssertions' split by the old
regex (8); `.And.` found by `IndexOf` (9); the last call's arguments for the first (10); the lambda head regex
narrowed to `() => ` (11); the nested search entering a lambda (12); `=>` found in a literal (13); R2's skip restored
for every constant (16); the enum check true for any enum type (16, its enum-typed `const` row); the enum check
always false (17); the walk not stopping at the constant (17); the fallback without constants removed (19).

### 4.4 Proofs before each tag

- The new facts red on 5.14.0, each failure read.
- The core suite, the weaver suite, fact 15, and `release.slnf` built in Release for every target framework. The
  weaver builds for an older framework than the core, and the prototype's first build failed there (`MatchCollection`
  has no generic enumerator; `.Cast<Match>()`).
- The probe on local packages (`probe_local.sh`), Debug and Release identical, against §3.9.
- The five corpora again (`formatter/probe.cs`, then `classify.py` and `bracket_space.py`), every changed label
  classified.

## 5. Releases and bumps

| Release | What | Package | Bump |
|---|---|---|---|
| R1 | §3.2 to §3.7: the subject, `nameof`, one line, FluentAssertions' split, the chain, the other readings | `Kronikol` (every package moves to the same number) | **Patch**: fixes only. Labels change, which is report output, and by CLAUDE.md not a major bump on its own; the changelog lists what reads differently |
| R2 | §3.8: constants passed with their values | `Kronikol.AssertionTracking` | **Patch** recommended (**Q3**): a label that names the wrong thing is corrected, with nothing new to call or configure. 4.16.0, which first passed argument values, was a minor; R2 passes the constants that release left out on a false premise |

- **Order (Q4): R1 first.** It changes no IL, so it waits for nobody; its lambda-head edit sits just before #212's
  head lines in `Format`, and whichever of the two ships second rebases (agreed with kronikol-c6). R2 changes
  `AssertionWeaver.cs`, which #211 (session e132cc3e) and #212 (kronikol-c6) change too, in other functions: R2 goes
  after their releases, rebased. #212 keeps the weave's statement text and its argument capture (its plan, lines
  196-199, 368-370 and 379), so `DetectCapturedArguments`' matching of argument texts stands.
- Release numbers are claimed at push, as the repo's sessions do.
- No `ROADMAP.md` row is taken for a plan that is not green-lit. The roadmap's last committed row is 1.32
  (`8d7d1e1d`); 1.33 is claimed by #125's uncommitted plan.
- Kronikol4J: a ledger line per release (§7).

## 6. Documentation

| Where (the wiki at `183dbb0`) | Change | Release |
|---|---|---|
| `Assertion-Tracking.md:562` | "PascalCase is split into space-separated words" becomes: a subject's member names are, and each call of the assertion's chain; the code in brackets, and a subject that is not a member path, are kept as written | R1 |
| `:563` | The enum rule applies to an argument list that is one `A.B` with no value; a constant the weave passes reads as its value | R1, R2 |
| `:573`, and new lines after it | `nameof(…)` reads as its value; code kept as written is on one line; FluentAssertions' subject ends at its own `.Should()`; a chain reads link by link; `Track.That`'s `async () =>` and block bodies | R1 |
| `:540-554`, the table | Rows for W01, W02, W16 and a chain, then W04 | R1, R2 |
| `:545`, `:564` | They show `BeOfType<T>()` reading `T`; the formatter writes `<OrderResponse>` (`AssertionExpressionFormatterTests.cs:62`) | R1 |
| `:237-254`, "Argument values" | "Not stored: a constant (`"A1"`, `2`, `Colour.Blue`)" becomes: a literal and an enum member are not stored; `nameof`, a `const` and constant arithmetic are, by value | R2 |
| `:256` | The zero-overhead path is for literals and enum members | R2 |

XML docs and comments that ship and would become false: `AssertionExpressionFormatter`'s summary (`:6-12`),
`FormatSubject`'s (`:203-208`), `SubstituteResolvedValues`' (`:467-475`), the comment at `:59`,
`DetectCapturedArguments`' (`AssertionWeaver.cs:1138-1149`, "a constant") and `IsConstant`'s (`:1367-1368`). Each
release's CHANGELOG entry states which part of the version moved and why, and lists what reads differently. Before each
release, grep the wiki for `to string`, `nameof`, `Enum prefix`, `constant` and `.And.`.

## 7. Kronikol4J

The port has no label formatter: its `Track.that` writes the description it is given, or the lambda's source as
written (`docs/REMAINING_PARITY.md`, the entries for #141 and #163 at `:2419-2434` on `d7ad2b0`). R1 and R2 each take a
ledger line in the ledger's form: "Not mirrored, a ledger entry only", with the changelog's first two sentences.

## 8. Found on the way, not in this plan

| # | Finding | Level | Recommendation |
|---|---|---|---|
| 1 | Words written in capitals are lower-cased in a subject (`HTTPStatus` reads `http status`). This is the label's side of #139: kronikol-50's `TITLES_AS_WRITTEN_PLAN.md` (not committed, not green-lit) proposes one sentence-case rule that keeps such words, with a probe to reuse | READ | **Q7** |
| 2 | FluentAssertions' `because` is drawn as an argument (`Entry value status should not be null or empty $"health check entry '{entry.Key}' should have a status"`), while Shouldly's custom message is not (`Assertion-Tracking.md:567`) | RUN (corpus) | The owner's call; not in this plan |
| 3 | FluentAssertions' type argument reads `should be of type <string>`, Shouldly's `should be of type Order` (`AssertionExpressionFormatterTests.cs:62`, `:447`) | READ | Leave, unless one form is wanted |
| 4 | The weave's join trims each line of a multi-line verbatim or raw string literal and joins the lines with a space, so such a literal in a label is not the code's (`CutColumns`) | READ | Leave: a label is one line |
| 5 | `const string X = null` is left out as a null (`StackAnalysis.IsNull`) and keeps its bare name | READ | Leave (rare) |
| 6 | A suite's own async `[ShouldlyMethods]` assertion is never woven and never listed as unwoven: `IsShouldlyMethod` accepts the suite's type (`AssertionWeaver.cs:887-891`), the await check asks only for a library type (`:784`) | READ | Told to kronikol-c6, who confirmed it and took it into #212 (its F8, 4.1 g) |
| 7 | `Failures.md` and `Specifications.md` do not escape Markdown in a step's text (§2.10), and the search index strips anything shaped like `<i…>`, `<b…>`, `<color…>`, `<font…>`, `<size…>` or `<back…>` after lower-casing (`SearchNormalizer.cs:106-158`), so `<Item>`, `<int>` and `<B>` are not found | READ | A patch of its own: escape the text in both digests (outside the failure message, which is fenced already) and keep code in the index |
| 8 | A woven assertion inside `Track.That`'s or `Track.ThatAsync`'s lambda draws two notes, the weave's and `Track`'s (A09, on 5.12.0 and on the prototype) | RUN | Leave unless a suite mixes the two; a fact could pin one note |

## 9. What is not known

- The owner's nineteen reports' counts (§1). Five public and local corpora stand in for them.
- How a note paints in the Java engine (the CI summary's pictures, PlantUML server renderers). Fact 15 covers the
  report's engine.
- R2's weave time on a large suite. The probe's statements weave in a few hundred milliseconds either way, which says
  nothing about a large suite.

## 10. Risks and traps

1. **A label change is a report change.** R1 changes 106 of BreakfastProvider's 1,016 one-line labels and 2,223 of
   the Shouldly suites' 9,326, all of the defects' shapes, and R2
   BreakfastProvider's 84 reachable `const` labels. Old reports keep their labels. Nothing keys on a label across runs,
   so an upgrade does not make scenarios look changed; a merge of shards from both sides of a release shows both forms,
   and `kronikol query compare` reports each changed label (§2.10).
2. **Longer labels.** A subject that is an expression is now shown whole: BreakfastProvider's
   `(_retrievalSteps.Response.CreatedAt > DateTime.UtcNow.AddMinutes(-2) && …)` is 139 characters, its label 154. The
   note wraps (`DiagramWidth.WrapBlockNoteBody`); nothing is cut.
3. **The weave is shared code** with #211 and #212 (§5).
4. **Two target frameworks** (§4.4).
5. **Creole in kept code** (§2.10, fact 15, Q9).
6. **A framework enum must resolve at weave time**, or R2 passes it as a value and `OK` reads `'OK'` (§3.8, fact 17).
7. **Parallel sessions.** This plan's files are its own; `PLANS_STATUS.md` gets one row.

## 11. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | A `ToString(…)` with arguments in a subject: keep it with its arguments, or drop it as a bare `ToString()` is dropped (the issue's second suggestion) | **Keep it** (§3.2): the culture or format is part of what was compared, and in a failure it can be the cause (W19). Dropping it only for some arguments would need a list of provider names |
| Q2 | A subject that is not a member path (an operator, a cast, `new`, a bracketed `await`): as written, or each member path inside it as words | **As written** (§3.2): `a > b && c < d` cannot be made into a sentence by splitting words, and the code is exact. A lambda subject is the exception, read as its body (S18a) |
| Q3 | R2's bump | **Patch** (§5) |
| Q4 | One release or two | **Two**: R1 now, R2 after #211's and #212's weave releases |
| Q5 | An `A.B` argument with no value (`Track.That`, awaited and TUnit assertions, a chain's later call): keep cutting it to `B`, or show it as written | **Keep it** for now. R2 gives the woven ones their value; showing `A.B` as written would change every enum member with no value (`OK` to `HttpStatusCode.OK`, 164 of BreakfastProvider's 1,016 labels) |
| Q6 | `nameof` in a subject (`nameof(Foo).ShouldBe("Foo")`) | **As written**: the subject is the code under test |
| Q7 | Words written in capitals in a subject's words (`HTTPStatus`) | **Follow #139's decision, in #139's release**, not here: it is the same rule for titles and labels, and it is not part of #210. Agreed with kronikol-50: on a yes, #139's R1 changes only the case rule (`SplitPascalCase`) in `AssertionExpressionFormatter.cs`, this plan's R1 keeps changing its callers, and whichever ships second rebases onto the first |
| Q8 | A chain's `.And.` (and TUnit's `.Or.`): keep reading the first assertion only, as two facts pin (`AssertionExpressionFormatterTests.cs:71-75`, `:477`), or read every link ("X should not be null and have count 2") | **Read every link**: when the second half fails, today's label names the half that passed. Neither BreakfastProvider nor the examples have one, so few labels change |
| Q9 | A label is code, not markup someone wrote: escape its creole as a payload's is (`EscapeCreoleMarkup`), or keep its markup as step and test names keep theirs | **Escape it**, in R1: a type argument `B`, a `""` or a `**` in code should draw as written, and R1 shows more code. It changes how such labels paint today, so fact 15 pins both kinds |

## Appendix A. Edit sites (5.14.0, the same lines as 5.12.0)

| File and lines | What | Release |
|---|---|---|
| `src/Kronikol/Tracking/AssertionExpressionFormatter.cs:15-18`, `:51-57` | FluentAssertions' split at its own top-level `.Should().` | R1 |
| `:30-33` | The lambda head and block body (F16), before #212's head lines | R1 |
| `:54` | The nested assertion (F17), then the code as written on one line | R1 |
| `:63-65`, `:161-166` | `.And.` and `.Or.` outside brackets and literals | R1 |
| `:104`, `:387-444` (`ParseMethodAndArgs`), and a chain reader beside it | The first call's own arguments and type arguments; the chain | R1 |
| `:203-252` (`FormatSubject`), `:254-270` (`Words`), `:272-289` (`SplitOutsideLiterals`) | Links at top-level dots; names as words, the rest as written; a lambda subject | R1 |
| `:291-327` (`CleanCode`) | `?[` reads as `?.` does | R1 |
| `:446-465` (`FormatArgs`) | The `nameof` reading after the values; one line; lambda brackets for code only | R1 |
| `:565-586` (`SplitTopLevelCommas`) | Brackets, not `<` and `>` | R1 |
| `:6-12`, `:59`, `:203-208`, `:467-475` | XML docs and comments | R1 |
| `tests/Kronikol.Tests/Tracking/AssertionExpressionFormatterTests.cs:305`, `TrackValueResolutionTests.cs:443-449` | The two pins | R1 |
| `src/Kronikol.AssertionTracking/AssertionWeaver.cs:1195-1227` | A constant left out only when its text is its own spelling; the fallback without constants | R2 |
| `:1367-1382` (`IsConstant` and its doc), and an enum check beside it | | R2 |
| `:1138-1149` | XML doc | R2 |

## Appendix B. The harness

`plans/ASSERTION_LABELS_PLAN.harness/README.md` lists every file and how to re-run it. In short: `probe/` (the woven
console probe), `s0/` (5.12.0), `s1/` (the first prototype, and its suites on 5.14.0), `s2/` (the audit's cases and the
final prototype), `formatter/` (`probe.cs`, `published.cs`, the shape files and their outputs), `corpus/` (`extract.py`,
`extract_multiline.py`, `join_lines.py`, `classify.py`, `bracket_space.py`, `fa_chains.py`, and the corpora's statements
with their labels and counts), `probe_local.sh` and `prototype.patch`.

## Log

- 2026-10-27, kronikol-be: drafted, measured on 5.12.0 and on a prototype (`prototype.patch`, not applied). Split agreed
  with kronikol-c6 (#212) by message: c6 owns `Format`'s statement head and the weave's awaited path, and this plan
  owns the subject, the constants and FluentAssertions' split. #211 is session e132cc3e's. kronikol-50 (#139,
  `TITLES_AS_WRITTEN_PLAN.md`) recorded Q7 in its own section 8; on a yes, its R1 takes only the case rule (Q7's row).
- 2026-10-27, kronikol-be, revised at the owner's word ("make sure the plan is fully complete and accurate and do any
  deep dives that might change the plan"), on 5.14.0:
  - Every claim was checked against the code by an independent audit; its 13 corrections are in (the `A.B` rule's
    reach, the constants captured today, R2's reach, the corpus counts as the formatter's, citations).
  - The prototype's whole core suite was run, not only the Tracking facts.
  - The corpora were corrected (a statement with a trailing comment was skipped or glued to the next lines; two
    statement files were not UTF-8: BreakfastProvider 1,012 to 1,016 statements, 54 to 49 multi-line), and three
    public Shouldly suites were added, since the issue's library had no corpus.
  - Found and designed into R1: F9 to F17 (chains, `.And.`, nested type arguments, comments, interpolated holes,
    lambda brackets, Shouldly's argument split, `Track.That`'s lambda heads, an assertion inside other code); F16
    because F6 alone broke a `Track.ThatAsync` label that read by accident. #212's S18a and S23 taken from kronikol-c6.
  - R2 kept after a review of the alternative (no spill), with two fixes to the prototype. Where a label goes, and
    that nothing keys on it, mapped (§2.10). New questions Q8 and Q9.
- 2026-10-28, kronikol-be, executed at the owner's word ("implement the plan in full"), every question at its
  recommendation. #212 (kronikol-c6) and #211 are not being executed, so R2 follows R1 at once and they rebase onto
  both (c6, by message).
  - **R1, released as 5.16.3.** The prototype's formatter, written up: release comments, the helpers after the
    library branches, and every name read as words through one helper, `NameWords`, the seam agreed with kronikol-50,
    whose #139 R1 changes its body. Built beyond the prototype: F12 (comments; a `//` one only up to its line break,
    since text the weave joined has none), F13 (an interpolated string's holes), Q8 (`.And.` and `.Or.` read as "and …"
    and "or …") and Q9 (a label's creole escaped in its diagram note by `Track.LogAssertion`, the step list unchanged).
    Settled on the Shouldly corpora: the two chain readings of §3.6's last paragraph.
  - Found on the way: three more facts pinned an `.And.`'s first assertion (§4.1); Q9 writes a type argument's `<` as
    `~<` in a note's source (`get~<Item>()`), which draws as written and which copy and the search index read back,
    so one fact that read the source changed; the corpus fact's first cut read a `\n` inside a one-line statement's
    literal as a line break. And 5.16.2's `DeclaredDoubleDiagramTests` failed in two whole runs of the core suite
    of two, passing alone: the class before it left the diagram cache filled, so its first report had no sequence
    diagram, and its second report took the first's, so it compared one diagram's participant lines with themselves
    (kronikol-7b's catch). Each of its reports resets the cache first now (CHANGELOG 5.16.3, Tests).
  - Proofs (`s3/`): the new facts red on 5.16.1 (95 of 297) and, final, on 5.16.2 (100 of 298; fact 15 there with all
    ten labels misdrawn); fact 15 red on R1 without Q9's escape (exactly its three creole rows); the core suite 9,275
    passed and 2 skipped, the weaver suite 406 passed and 28 skipped; 34 Playwright facts (fact 15 with
    `ShouldlyAssertionNoteTests`, `AssertionToggleTests`, `AssertionTooltipTests` and `FilteredBlocksTests`); `release.slnf` in Release;
    21 mutations, each caught (`s3/mutations.txt`); the woven probe on local packages, Debug and Release identical and as §3.9 says for R1 alone; and the
    five corpora against the prototype's labels (`s3/corpora-vs-prototype.txt`): BreakfastProvider's and the
    examples' identical, the Shouldly suites' 13 differences all of the two settled readings and F13.
  - **R2, released as 5.16.4.** The weave's constants as section 3 designs them: a constant argument is passed with
    its value unless its text spells an enum's own members (`SpellsEnumMembers`, `EnumOf`); a constant that would take
    the copy past a slot no local can hold is dropped before the statement's other values are; and a statement's
    `//` comments are left out before its lines are joined (F12's weave side, `WithoutLineComments`).
  - Found on the way: fact 19's premise was wrong. A `ReadOnlySpan<char>` can be a local (`CanHoldAsLocal` refuses
    only a managed reference, a pointer and a function pointer), so the weave copies across a span and a constant
    below one reads as its value; that is a row of fact 16 now, and fact 19's constant sits below a `ref` argument.
    Its first form passed without the fallback too: a variable passed alone and a member path off a variable reach
    the label through the statement's variables and their access chains, not through the copy, so only a call above
    the address (`actual.ToUpperInvariant()`) shows the fallback. The walk not stopping at the constant (section 4.3)
    survived the planned facts; a row of fact 16 puts a `const string` (`Codes.Primary`) above another argument's
    box of an enum whose names spell it (`Codes.Kind.Primary`). Fact 20 needed no code: every fixture is woven
    through `WovenIl.Weave`, which reads it with ILVerify before and after, and `Every_woven_fixture_verifies` holds
    that none is woven another way.
  - Proofs (`s4/`): the new facts red on 5.16.3 (13 of 21; the 8 that pass pin enum members, the `newobj` constants
    and the fallback, which 5.16.3 keeps by copying no constant); section 4.3's five R2 mutations and F12's, each
    caught (`s4/mutations.txt`); the weaver suite 427 passed and 28 skipped; and the woven probe on local packages,
    Debug and Release identical, W04 `'GBP'`, W08 `'3'`, W12 `'Cancelled'`, A06 `'GBP'` and A07 "contain key
    'primary'" as section 3.9 says.
