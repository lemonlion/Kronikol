# Inventory of `C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc\scratchpad\shouldly-inventory\pkg-5.0.0-preview.2\x\lib\net8.0\Shouldly.dll`

- Assembly: `Shouldly, Version=5.0.0.0, Culture=neutral, PublicKeyToken=6042cbcb05cbc941`
- Public key token: `6042cbcb05cbc941`
- InformationalVersion: `5.0.0-preview.2+6feab8438ba5a387bf863d964d273ca039fb3d9c`
- TargetFramework attribute: `.NETCoreApp,Version=v8.0`
- Referenced assemblies: `System.Runtime 8.0.0.0`, `System.Collections.Concurrent 8.0.0.0`, `System.Threading 8.0.0.0`, `System.Diagnostics.StackTrace 8.0.0.0`, `System.Text.RegularExpressions 8.0.0.0`, `System.Collections 8.0.0.0`, `System.Linq.Expressions 8.0.0.0`, `System.Linq 8.0.0.0`, `System.Runtime.InteropServices 8.0.0.0`, `System.Memory 8.0.0.0`

## Counts

- Public types: 58 (57 in namespace `Shouldly`, 1 elsewhere: `Shouldly.ShouldlyExtensionMethods`)
- Types (any visibility) carrying `[ShouldlyMethods]`: 18 (18 public, 0 non-public)
- Public static methods on public types (no accessors): 211
- ... whose name starts with `Should`: 164 overloads, 63 distinct names
- ... `Should*` on a `[ShouldlyMethods]` type: 164; NOT on one: 0
- ... all public static methods on `[ShouldlyMethods]` types: 204 (70 distinct names)
- ... public static methods on `[ShouldlyMethods]` types whose name does NOT start with `Should`: `Shouldly.DynamicShould.Throw` x1, `Shouldly.DynamicShould.HaveProperty` x1, `Shouldly.Should.CompleteIn` x6, `Shouldly.Should.Satisfy` x1, `Shouldly.Should.Throw` x14, `Shouldly.Should.NotThrow` x11, `Shouldly.Should.ThrowAsync` x4, `Shouldly.Should.NotThrowAsync` x2
- ... returning non-void: 61 on marked types
- ... taking a delegate parameter: 53; an `Expression<>`: 4; a Task: 26
- ... with a `customMessage` parameter: 194 of 204 (forms: `string?` x194)
- ... with a `[CallerArgumentExpression]` parameter: 200
- ... marked `[Obsolete]`: 4; `[MethodImpl(NoInlining)]`: 5 of 204 on marked types

## Distinct `Should*` method names (public static, all public types)

`ShouldAllBe` (1), `ShouldBe` (21), `ShouldBeAssignableTo` (2), `ShouldBeDecoratedWith` (1), `ShouldBeEmpty` (2), `ShouldBeEquivalentTo` (2), `ShouldBeFalse` (2), `ShouldBeGreaterThan` (2), `ShouldBeGreaterThanOrEqualTo` (2), `ShouldBeInOrder` (3), `ShouldBeInRange` (1), `ShouldBeLessThan` (2), `ShouldBeLessThanOrEqualTo` (2), `ShouldBeNegative` (6), `ShouldBeNull` (2), `ShouldBeNullOrEmpty` (1), `ShouldBeNullOrWhiteSpace` (1), `ShouldBeOfType` (2), `ShouldBeOfTypes` (1), `ShouldBeOneOf` (2), `ShouldBePositive` (6), `ShouldBeSameAs` (1), `ShouldBeSubsetOf` (2), `ShouldBeTrue` (2), `ShouldBeUnique` (2), `ShouldContain` (7), `ShouldContainAll` (1), `ShouldContainAny` (1), `ShouldContainKey` (1), `ShouldContainKeyAndValue` (1), `ShouldContainWithoutWhitespace` (1), `ShouldEndWith` (1), `ShouldHaveCount` (1), `ShouldHaveFlag` (1), `ShouldHaveSingleItem` (1), `ShouldMatch` (1), `ShouldMatchApproved` (1), `ShouldNotBe` (9), `ShouldNotBeAssignableTo` (2), `ShouldNotBeEmpty` (2), `ShouldNotBeInRange` (1), `ShouldNotBeNull` (2), `ShouldNotBeNullOrEmpty` (1), `ShouldNotBeNullOrWhiteSpace` (1), `ShouldNotBeOfType` (2), `ShouldNotBeOneOf` (2), `ShouldNotBeSameAs` (1), `ShouldNotContain` (4), `ShouldNotContainAll` (1), `ShouldNotContainAny` (1), `ShouldNotContainKey` (1), `ShouldNotContainValueForKey` (1), `ShouldNotEndWith` (2), `ShouldNotHaveFlag` (1), `ShouldNotMatch` (1), `ShouldNotStartWith` (1), `ShouldNotThrow` (11), `ShouldNotThrowAsync` (2), `ShouldSatisfy` (1), `ShouldSatisfyAllConditions` (4), `ShouldStartWith` (1), `ShouldThrow` (16), `ShouldThrowAsync` (4)

## All types carrying `[ShouldlyMethods]` (any visibility)

- `Shouldly.DynamicShould` (public, static class)
- `Shouldly.GuidShouldBeTestExtensions` (public, static class)
- `Shouldly.ObjectGraphTestExtensions` (public, static class)
- `Shouldly.Should` (public, static class)
- `Shouldly.ShouldBeBooleanExtensions` (public, static class)
- `Shouldly.ShouldBeDecoratedWithExtensions` (public, static class)
- `Shouldly.ShouldBeDictionaryTestExtensions` (public, static class)
- `Shouldly.ShouldBeEnumerableTestExtensions` (public, static class)
- `Shouldly.ShouldBeNullExtensions` (public, static class)
- `Shouldly.ShouldBeStringTestExtensions` (public, static class)
- `Shouldly.ShouldBeTestExtensions` (public, static class)
- `Shouldly.ShouldMatchApprovedTestExtensions` (public, static class)
- `Shouldly.ShouldNotThrowTaskAsyncExtensions` (public, static class)
- `Shouldly.ShouldSatisfyAllConditionsTestExtensions` (public, static class)
- `Shouldly.ShouldThrowAsyncExtensions` (public, static class)
- `Shouldly.ShouldThrowExtensions` (public, static class)
- `Shouldly.ShouldThrowTaskExtensions` (public, static class)
- `Shouldly.ShouldlyExtensionMethods.ShouldHaveEnumExtensions` (public, static class)

## Public types

| Type | Kind | `[ShouldlyMethods]` | Other attributes | Base | Public static methods (name x overloads) |
|---|---|---|---|---|---|
| `Shouldly.ActualFilteredWithPredicateShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ActualShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.AsyncShouldlyNotThrowShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.AsyncShouldlyThrowShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.Case` | enum |  |  | Enum -> ValueType |  |
| `Shouldly.CompleteInShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.DiffStyle` | enum |  |  | Enum -> ValueType |  |
| `Shouldly.DynamicShould` | static class | yes |  |  | Throw x1, HaveProperty x1 |
| `Shouldly.EquivalencyOptions` | class |  |  |  |  |
| `Shouldly.EscapeStyle` | enum |  |  | Enum -> ValueType |  |
| `Shouldly.ExpectedActualIgnoreOrderShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualKeyShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualToleranceShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualWithCaseSensitivityShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedEquivalenceShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedOrderShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.FilenameGenerator` | delegate |  |  | MulticastDelegate -> Delegate |  |
| `Shouldly.FindMethodUsingAttribute`1` | class |  |  |  |  |
| `Shouldly.FirstNonShouldlyMethodFinder` | class |  |  |  |  |
| `Shouldly.GuidShouldBeTestExtensions` | static class | yes | EditorBrowsable |  | ShouldBeEmpty x1, ShouldNotBeEmpty x1 |
| `Shouldly.IDiffViewer` | interface |  |  |  |  |
| `Shouldly.IShouldlyAssertionContext` | interface |  |  |  |  |
| `Shouldly.ITestMethodFinder` | interface |  |  |  |  |
| `Shouldly.ObjectGraphTestExtensions` | static class | yes | EditorBrowsable |  | ShouldBeEquivalentTo x2 |
| `Shouldly.Should` | static class | yes | DebuggerStepThrough |  | CompleteIn x6, Satisfy x1, Throw x14, NotThrow x11, ThrowAsync x4, NotThrowAsync x2 |
| `Shouldly.ShouldAssertException` | exception class |  |  | Exception |  |
| `Shouldly.ShouldBeBooleanExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeTrue x2, ShouldBeFalse x2 |
| `Shouldly.ShouldBeDecoratedWithExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeDecoratedWith x1 |
| `Shouldly.ShouldBeDictionaryTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContainKey x1, ShouldNotContainKey x1, ShouldContainKeyAndValue x1, ShouldNotContainValueForKey x1 |
| `Shouldly.ShouldBeEnumerableTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContain x6, ShouldNotContain x3, ShouldAllBe x1, ShouldBeEmpty x1, ShouldNotBeEmpty x1, ShouldHaveSingleItem x1, ShouldHaveCount x1, ShouldBeSubsetOf x2, ShouldBeUnique x2, ShouldBe x1, ShouldBeInOrder x3, ShouldBeOfTypes x1 |
| `Shouldly.ShouldBeNullExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeNull x2, ShouldNotBeNull x2 |
| `Shouldly.ShouldBeStringTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContainWithoutWhitespace x1, ShouldContain x1, ShouldNotContain x1, ShouldMatch x1, ShouldNotMatch x1, ShouldContainAll x1, ShouldContainAny x1, ShouldNotContainAll x1, ShouldNotContainAny x1, ShouldBeNullOrEmpty x1, ShouldNotBeNullOrEmpty x1, ShouldBeNullOrWhiteSpace x1, ShouldNotBeNullOrWhiteSpace x1, ShouldBe x3, ShouldStartWith x1, ShouldEndWith x1, ShouldNotStartWith x1, ShouldNotEndWith x2 |
| `Shouldly.ShouldBeTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBe x17, ShouldNotBe x9, ShouldBeSameAs x1, ShouldNotBeSameAs x1, ShouldBeGreaterThan x2, ShouldBeLessThan x2, ShouldBeGreaterThanOrEqualTo x2, ShouldBeLessThanOrEqualTo x2, ShouldBePositive x6, ShouldBeNegative x6, ShouldBeOneOf x2, ShouldNotBeOneOf x2, ShouldBeInRange x1, ShouldNotBeInRange x1, ShouldBeAssignableTo x2, ShouldBeOfType x2, ShouldNotBeAssignableTo x2, ShouldNotBeOfType x2 |
| `Shouldly.ShouldCompleteInException` | exception class |  |  | ShouldlyTimeoutException -> TimeoutException -> SystemException -> Exception |  |
| `Shouldly.ShouldContainWithCountShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ShouldMatchApprovedException` | exception class |  |  | ShouldAssertException -> Exception |  |
| `Shouldly.ShouldMatchApprovedTestExtensions` | static class | yes | EditorBrowsable |  | ShouldMatchApproved x1 |
| `Shouldly.ShouldMatchConfiguration` | class |  |  |  |  |
| `Shouldly.ShouldMatchConfigurationBuilder` | class |  |  |  |  |
| `Shouldly.ShouldNotThrowTaskAsyncExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldNotThrowAsync x2 |
| `Shouldly.ShouldSatisfyAllConditionsTestExtensions` | static class | yes | EditorBrowsable |  | ShouldSatisfy x1, ShouldSatisfyAllConditions x4 |
| `Shouldly.ShouldThrowAsyncExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldThrowAsync x4 |
| `Shouldly.ShouldThrowExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldThrow x4, ShouldNotThrow x3 |
| `Shouldly.ShouldThrowTaskExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldThrow x12, ShouldNotThrow x8 |
| `Shouldly.ShouldlyAssertionContext` | class |  |  |  |  |
| `Shouldly.ShouldlyConfiguration` | static class |  |  |  | DisableSourceInErrors x1, IsSourceDisabledInErrors x1 |
| `Shouldly.ShouldlyCoreExtensions` | static class |  |  |  | AssertAwesomely x3, AssertAwesomelyWithCaseSensitivity x1, AssertAwesomelyIgnoringOrder x1 |
| `Shouldly.ShouldlyExtensionMethods.ShouldHaveEnumExtensions` | static class | yes | EditorBrowsable |  | ShouldHaveFlag x1, ShouldNotHaveFlag x1 |
| `Shouldly.ShouldlyMessage` | abstract class |  |  |  |  |
| `Shouldly.ShouldlyMethodsAttribute` | class |  | Usage | Attribute |  |
| `Shouldly.ShouldlyThrowMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ShouldlyTimeoutException` | exception class |  |  | TimeoutException -> SystemException -> Exception |  |
| `Shouldly.SortDirection` | enum |  |  | Enum -> ValueType |  |
| `Shouldly.StringCompareShould` | enum |  | Flags | Enum -> ValueType |  |
| `Shouldly.TaskShouldlyThrowMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.TestMethodInfo` | class |  |  |  |  |

## Public static properties and fields (all public types)

- `Shouldly.ShouldMatchConfiguration.ShouldMatchApprovedDefaults` property `ShouldMatchConfigurationBuilder` (get)
- `Shouldly.ShouldlyConfiguration.CompareAsObjectTypes` property `List<string>` (get)
- `Shouldly.ShouldlyConfiguration.DiffStyle` property `DiffStyle` (get/set)
- `Shouldly.ShouldlyConfiguration.EscapeStyle` property `EscapeStyle` (get/set)
- `Shouldly.ShouldlyConfiguration.DefaultFloatingPointTolerance` field `double`
- `Shouldly.ShouldlyConfiguration.DefaultTaskTimeout` field `TimeSpan`

## Public instance members of public non-static, non-enum types (declared only)

- `Shouldly.ActualFilteredWithPredicateShouldlyMessage`: `.ctor(Expression filter, object result, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ActualShouldlyMessage`: `.ctor(object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.AsyncShouldlyNotThrowShouldlyMessage`: `.ctor(Type exception, string /*nullability:?*/ customMessage, StackTrace stackTrace, string exceptionMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.AsyncShouldlyThrowShouldlyMessage`: `.ctor(Type exception, string /*nullability:?*/ customMessage, StackTrace stackTrace, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`; `.ctor(Type expected, Type actual, string /*nullability:?*/ customMessage, StackTrace stackTrace, string actualExpression = null)`
- `Shouldly.CompleteInShouldlyMessage`: `.ctor(string what, TimeSpan timeout, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.EquivalencyOptions`: `.ctor()`; `bool IgnoreOrder { get; set; }`; `ICollection<string> MembersToIgnore { get; }`
- `Shouldly.ExpectedActualIgnoreOrderShouldlyMessage`: `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedActualKeyShouldlyMessage`: `.ctor(object expected, object actual, object key, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedActualShouldlyMessage`: `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedActualToleranceShouldlyMessage`: `.ctor(object expected, object actual, object tolerance, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedActualWithCaseSensitivityShouldlyMessage`: `.ctor(object expected, object actual, Case? caseSensitivity, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedEquivalenceShouldlyMessage`: `.ctor(object expected, object actual, IEnumerable<string> path, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedOrderShouldlyMessage`: `.ctor(object actual, SortDirection expectedDirection, int outOfOrderIndex, object outOfOrderObject, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ExpectedShouldlyMessage`: `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.FilenameGenerator`: `.ctor(object object, IntPtr method)`; `string Invoke(TestMethodInfo testMethodInfo, string discriminator, string fileType, string fileExtension)`; `IAsyncResult BeginInvoke(TestMethodInfo testMethodInfo, string discriminator, string fileType, string fileExtension, AsyncCallback callback, object object)`; `string EndInvoke(IAsyncResult result)`
- `Shouldly.FindMethodUsingAttribute`1`: `.ctor()`; `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`
- `Shouldly.FirstNonShouldlyMethodFinder`: `.ctor()`; `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`; `int Offset { get; set; }`
- `Shouldly.IDiffViewer`: `void Launch(string receivedFile, string approvedFile)`
- `Shouldly.IShouldlyAssertionContext`: `string ShouldMethod { get; set; }`; `string CodePart { get; set; }`; `string FileName { get; set; }`; `int? LineNumber { get; set; }`; `object Key { get; set; }`; `object Expected { get; set; }`; `object Actual { get; set; }`; `object Tolerance { get; set; }`; `TimeSpan? Timeout { get; set; }`; `bool IgnoreOrder { get; set; }`; `bool HasRelevantActual { get; set; }`; `bool HasRelevantKey { get; set; }`; `bool IsNegatedAssertion { get; }`; `string CustomMessage { get; set; }`; `Case? CaseSensitivity { get; set; }`; `bool CodePartMatchesActual { get; }`; `Expression Filter { get; set; }`; `int? MatchCount { get; set; }`; `SortDirection SortDirection { get; set; }`; `int OutOfOrderIndex { get; set; }`; `object OutOfOrderObject { get; set; }`; `IEnumerable<string> Path { get; set; }`
- `Shouldly.ITestMethodFinder`: `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`
- `Shouldly.ShouldAssertException`: `.ctor(string message)`; `.ctor(string message, Exception innerException)`; `string StackTrace { get; }`
- `Shouldly.ShouldCompleteInException`: `.ctor(string message, ShouldlyTimeoutException inner)`
- `Shouldly.ShouldContainWithCountShouldlyMessage`: `.ctor(object expected, object actual, int matchCount, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ShouldMatchApprovedException`: `.ctor(string message, string receivedFile, string approvedFile, bool viewerNotPresent)`
- `Shouldly.ShouldMatchConfiguration`: `.ctor()`; `.ctor(ShouldMatchConfiguration initialConfig)`; `StringCompareShould StringCompareOptions { get; set; }`; `string FilenameDiscriminator { get; set; }`; `bool PreventDiff { get; set; }`; `IDiffViewer DiffViewer { get; set; }`; `string FileExtension { get; set; }`; `ITestMethodFinder TestMethodFinder { get; set; }`; `string ApprovalFileSubFolder { get; set; }`; `Func<string, string> Scrubber { get; set; }`; `FilenameGenerator FilenameGenerator { get; set; }`
- `Shouldly.ShouldMatchConfigurationBuilder`: `.ctor(ShouldMatchConfiguration initialConfig)`; `ShouldMatchConfigurationBuilder WithStringCompareOptions(StringCompareShould stringCompareOptions)`; `ShouldMatchConfigurationBuilder WithDiscriminator(string fileDiscriminator)`; `ShouldMatchConfigurationBuilder NoDiff()`; `ShouldMatchConfigurationBuilder WithFileExtension(string fileExtension)`; `ShouldMatchConfigurationBuilder WithFilenameGenerator(FilenameGenerator filenameGenerator)`; `ShouldMatchConfigurationBuilder DoNotIgnoreLineEndings()`; `ShouldMatchConfigurationBuilder SubFolder(string subfolder)`; `ShouldMatchConfigurationBuilder UseCallerLocation()`; `ShouldMatchConfigurationBuilder LocateTestMethodUsingAttribute<T>() where T : Attribute`; `ShouldMatchConfigurationBuilder WithScrubber(Func<string, string> scrubber)`; `ShouldMatchConfigurationBuilder Configure(Action<ShouldMatchConfiguration> configure)`; `ShouldMatchConfiguration Build()`
- `Shouldly.ShouldlyAssertionContext`: `.ctor(string shouldlyMethod, object expected = null, object actual = null, StackTrace stackTrace = null, string actualExpression = null)`; `string ShouldMethod { get; set; }`; `string CodePart { get; set; }`; `string FileName { get; set; }`; `int? LineNumber { get; set; }`; `object Key { get; set; }`; `object Expected { get; set; }`; `object Actual { get; set; }`; `object Tolerance { get; set; }`; `Case? CaseSensitivity { get; set; }`; `bool CodePartMatchesActual { get; }`; `TimeSpan? Timeout { get; set; }`; `bool IgnoreOrder { get; set; }`; `bool HasRelevantActual { get; set; }`; `bool HasRelevantKey { get; set; }`; `bool IsNegatedAssertion { get; }`; `string CustomMessage { get; set; }`; `Expression Filter { get; set; }`; `int? MatchCount { get; set; }`; `SortDirection SortDirection { get; set; }`; `int OutOfOrderIndex { get; set; }`; `object OutOfOrderObject { get; set; }`; `IEnumerable<string> Path { get; set; }`
- `Shouldly.ShouldlyMessage`: `string ToString()`
- `Shouldly.ShouldlyMethodsAttribute`: `.ctor()`
- `Shouldly.ShouldlyThrowMessage`: `.ctor(object expected, string exceptionMessage, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`; `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`; `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.ShouldlyTimeoutException`: `.ctor()`; `.ctor(string message, ShouldlyTimeoutException inner)`; `string StackTrace { get; }`
- `Shouldly.TaskShouldlyThrowMessage`: `.ctor(object expected, Exception exception, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`; `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`; `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null)`
- `Shouldly.TestMethodInfo`: `.ctor(StackFrame callingFrame)`; `string SourceFileDirectory { get; }`; `string MethodName { get; }`; `string DeclaringTypeName { get; }`

## Enums

- `Shouldly.Case`: Sensitive, Insensitive
- `Shouldly.DiffStyle`: Unicode, Ascii
- `Shouldly.EscapeStyle`: CStyle, ControlPictures, Descriptive
- `Shouldly.SortDirection`: Ascending, Descending
- `Shouldly.StringCompareShould`: IgnoreCase, IgnoreLineEndings

## Every public static method, by declaring type

### `Shouldly.DynamicShould` [ShouldlyMethods]

```csharp
void HaveProperty(Func<object> dynamicTestObject, string propertyName, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("dynamicTestObject")] string actualExpression = null);
TException Throw<TException>(Action actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
```

### `Shouldly.GuidShouldBeTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBeEmpty(this Guid actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeEmpty(this Guid actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ObjectGraphTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBeEquivalentTo(this object actual, object expected, EquivalencyOptions options, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeEquivalentTo(this object actual, object expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.Should` [ShouldlyMethods]

```csharp
T CompleteIn<T>(Func<T> function, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("function")] string actualExpression = null);
T CompleteIn<T>(Func<Task<T>> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
T CompleteIn<T>(Task<T> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void CompleteIn(Action action, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void CompleteIn(Func<Task> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void CompleteIn(Task actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
T NotThrow<T>(Func<T> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T NotThrow<T>(Func<Task<T>> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T NotThrow<T>(Func<Task<T>> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T NotThrow<T>(Task<T> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T NotThrow<T>(Task<T> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow(Action action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow(Func<Task> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow(Func<Task> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow(Task action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow(Task action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void NotThrow<TException>(Action action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null) where TException : Exception;
Task NotThrowAsync(Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Task NotThrowAsync(Task task, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null);
void Satisfy(Action[] conditions, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("conditions")] string actualExpression = null);
Exception Throw(Action actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Func<Task> actual, TimeSpan timeoutAfter, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Func<object> actual, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Func<object> actual, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Task actual, TimeSpan timeoutAfter, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception Throw(Task actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
TException Throw<TException>(Action actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException Throw<TException>(Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException Throw<TException>(Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException Throw<TException>(Func<object> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException Throw<TException>(Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException Throw<TException>(Task actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
Task<Exception> ThrowAsync(Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Task<Exception> ThrowAsync(Task task, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null);
Task<TException> ThrowAsync<TException>(Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
Task<TException> ThrowAsync<TException>(Task task, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null) where TException : Exception;
```

### `Shouldly.ShouldBeBooleanExtensions` [ShouldlyMethods]

```csharp
void ShouldBeFalse(this bool actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeFalse(this bool? actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeTrue(this bool actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeTrue(this bool? actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ShouldBeDecoratedWithExtensions` [ShouldlyMethods]

```csharp
void ShouldBeDecoratedWith<T>(this Type actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : Attribute;
```

### `Shouldly.ShouldBeDictionaryTestExtensions` [ShouldlyMethods]

```csharp
void ShouldContainKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("dictionary")] string actualExpression = null);
void ShouldContainKeyAndValue<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue val, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("dictionary")] string actualExpression = null);
void ShouldNotContainKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("dictionary")] string actualExpression = null);
void ShouldNotContainValueForKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue val, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("dictionary")] string actualExpression = null);
```

### `Shouldly.ShouldBeEnumerableTestExtensions` [ShouldlyMethods]

```csharp
void ShouldAllBe<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this IEnumerable<string> actual, IEnumerable<string> expected, Case caseSensitivity, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeEmpty<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, SortDirection expectedSortDirection, IComparer<T> customComparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, SortDirection expectedSortDirection, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeOfTypes<T>(this IEnumerable<T> actual, Type[] expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeSubsetOf<T>(this IEnumerable<T> actual, IEnumerable<T> expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeSubsetOf<T>(this IEnumerable<T> actual, IEnumerable<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeUnique<T>(this IEnumerable<T> actual, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeUnique<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain(this IEnumerable<double> actual, double expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain(this IEnumerable<float> actual, float expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, int expectedCount, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain<T>(this IEnumerable<T> actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain<T>(this IEnumerable<T> actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldHaveCount<T>(this IEnumerable<T> actual, int expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
T ShouldHaveSingleItem<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeEmpty<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ShouldBeNullExtensions` [ShouldlyMethods]

```csharp
void ShouldBeNull<T>(this T actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : class;
void ShouldBeNull<T>(this T? actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : struct;
T ShouldNotBeNull<T>(this T actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : class;
T ShouldNotBeNull<T>(this T? actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : struct;
```

### `Shouldly.ShouldBeStringTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBe(this string actual, string expected, StringCompareShould options, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this string actual, string expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this string actual, string expected, string /*nullability:?*/ customMessage, StringCompareShould options, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNullOrEmpty(this string actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNullOrWhiteSpace(this string actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContain(this string actual, string expected, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContainAll(this string actual, string[] expectedValues, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContainAny(this string actual, string[] expectedValues, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldContainWithoutWhitespace(this string actual, object expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldEndWith(this string actual, string expected, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldMatch(this string actual, string regexPattern, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeNullOrEmpty(this string actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeNullOrWhiteSpace(this string actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContain(this string actual, string expected, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContainAll(this string actual, string[] expectedValues, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotContainAny(this string actual, string[] expectedValues, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotEndWith(this string actual, string expected, Case caseSensitivity, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotEndWith(this string actual, string expected, string /*nullability:?*/ customMessage = null, Case caseSensitivity = 0, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotMatch(this string actual, string regexPattern, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotStartWith(this string actual, string expected, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldStartWith(this string actual, string expected, Case caseSensitivity = 0, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ShouldBeTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBe(this DateTime actual, DateTime expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this DateTimeOffset actual, DateTimeOffset expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this IEnumerable<decimal> actual, IEnumerable<decimal> expected, decimal tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this IEnumerable<double> actual, IEnumerable<double> expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this IEnumerable<float> actual, IEnumerable<float> expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this TimeSpan actual, TimeSpan expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this decimal actual, decimal expected, decimal tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this double actual, double expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe(this float actual, float expected, double tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this IEnumerable<T> actual, IEnumerable<T> expected, IEqualityComparer<T> comparer, bool ignoreOrder = false, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this IEnumerable<T> actual, IEnumerable<T> expected, bool ignoreOrder = false, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this Memory<T> actual, Memory<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this ReadOnlyMemory<T> actual, ReadOnlyMemory<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this ReadOnlySpan<T> actual, ReadOnlySpan<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this Span<T> actual, ReadOnlySpan<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this T actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBe<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
T ShouldBeAssignableTo<T>(this object actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeAssignableTo(this object actual, Type expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeGreaterThan<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeGreaterThan<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldBeGreaterThanOrEqualTo<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeGreaterThanOrEqualTo<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldBeInRange<T>(this T actual, T from, T to, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldBeLessThan<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeLessThan<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldBeLessThanOrEqualTo<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeLessThanOrEqualTo<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldBeNegative(this decimal actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNegative(this double actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNegative(this float actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNegative(this int actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNegative(this long actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeNegative(this short actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
T ShouldBeOfType<T>(this object actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeOfType(this object actual, Type expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeOneOf<T>(this T actual, T[] expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeOneOf<T>(this T actual, T[] expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this decimal actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this double actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this float actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this int actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this long actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBePositive(this short actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldBeSameAs(this object actual, object expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe(this DateTime actual, DateTime expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe(this DateTimeOffset actual, DateTimeOffset expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe(this TimeSpan actual, TimeSpan expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this Memory<T> actual, Memory<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this ReadOnlyMemory<T> actual, ReadOnlyMemory<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this ReadOnlySpan<T> actual, ReadOnlySpan<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this Span<T> actual, ReadOnlySpan<T> expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this T actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBe<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeAssignableTo(this object actual, Type expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeAssignableTo<T>(this object actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeInRange<T>(this T actual, T from, T to, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where T : IComparable<T>;
void ShouldNotBeOfType(this object actual, Type expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeOfType<T>(this object actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeOneOf<T>(this T actual, T[] expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeOneOf<T>(this T actual, T[] expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotBeSameAs(this object actual, object expected, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ShouldMatchApprovedTestExtensions` [ShouldlyMethods]

```csharp
void ShouldMatchApproved(this string actual, Action<ShouldMatchConfigurationBuilder> configureOptions = null, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

### `Shouldly.ShouldNotThrowTaskAsyncExtensions` [ShouldlyMethods]

```csharp
Task ShouldNotThrowAsync(this Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Task ShouldNotThrowAsync(this Task task, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null);
```

### `Shouldly.ShouldSatisfyAllConditionsTestExtensions` [ShouldlyMethods]

```csharp
void ShouldSatisfy<T>(this T actual, Action<T>[] conditions, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
[Obsolete] void ShouldSatisfyAllConditions(this object actual, params Action[] conditions);
[Obsolete] void ShouldSatisfyAllConditions(this object actual, string /*nullability:?*/ customMessage, params Action[] conditions);
[Obsolete] void ShouldSatisfyAllConditions<T>(this T actual, params Action<T>[] conditions);
[Obsolete] void ShouldSatisfyAllConditions<T>(this T actual, string /*nullability:?*/ customMessage, params Action<T>[] conditions);
```

### `Shouldly.ShouldThrowAsyncExtensions` [ShouldlyMethods]

```csharp
Task<Exception> ShouldThrowAsync(this Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Task<Exception> ShouldThrowAsync(this Task task, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null);
Task<TException> ShouldThrowAsync<TException>(this Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
Task<TException> ShouldThrowAsync<TException>(this Task task, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("task")] string actualExpression = null) where TException : Exception;
```

### `Shouldly.ShouldThrowExtensions` [ShouldlyMethods]

```csharp
T ShouldNotThrow<T>(this Func<T> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow(this Action action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow<TException>(this Action action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null) where TException : Exception;
Exception ShouldThrow(this Action actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Func<object> actual, Type exceptionType, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
TException ShouldThrow<TException>(this Action actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException ShouldThrow<TException>(this Func<object> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
```

### `Shouldly.ShouldThrowTaskExtensions` [ShouldlyMethods]

```csharp
T ShouldNotThrow<T>(this Func<Task<T>> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T ShouldNotThrow<T>(this Func<Task<T>> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T ShouldNotThrow<T>(this Task<T> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
T ShouldNotThrow<T>(this Task<T> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow(this Func<Task> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow(this Func<Task> action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow(this Task action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
void ShouldNotThrow(this Task action, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("action")] string actualExpression = null);
Exception ShouldThrow(this Func<Task> actual, TimeSpan timeoutAfter, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Func<Task> actual, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Func<Task> actual, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Task actual, TimeSpan timeoutAfter, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Task actual, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
Exception ShouldThrow(this Task actual, string /*nullability:?*/ customMessage, Type exceptionType, [CallerArgumentExpression("actual")] string actualExpression = null);
TException ShouldThrow<TException>(this Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException ShouldThrow<TException>(this Func<Task> actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException ShouldThrow<TException>(this Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
TException ShouldThrow<TException>(this Task actual, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null) where TException : Exception;
```

### `Shouldly.ShouldlyConfiguration`

```csharp
IDisposable DisableSourceInErrors();
bool IsSourceDisabledInErrors();
```

### `Shouldly.ShouldlyCoreExtensions`

```csharp
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, Case caseSensitivity, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null);
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, object tolerance, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null);
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null);
void AssertAwesomelyIgnoringOrder<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null);
void AssertAwesomelyWithCaseSensitivity<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, Case caseSensitivity, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null, string actualExpression = null);
```

### `Shouldly.ShouldlyExtensionMethods.ShouldHaveEnumExtensions` [ShouldlyMethods]

```csharp
void ShouldHaveFlag(this Enum actual, Enum expectedFlag, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
void ShouldNotHaveFlag(this Enum actual, Enum expectedFlag, string /*nullability:?*/ customMessage = null, [CallerArgumentExpression("actual")] string actualExpression = null);
```

