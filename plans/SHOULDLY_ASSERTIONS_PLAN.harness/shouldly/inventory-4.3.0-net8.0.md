# Inventory of `C:\Users\cex\.nuget\packages\shouldly\4.3.0\lib\net8.0\Shouldly.dll`

- Assembly: `Shouldly, Version=4.3.0.0, Culture=neutral, PublicKeyToken=6042cbcb05cbc941`
- Public key token: `6042cbcb05cbc941`
- InformationalVersion: `4.3.0+cb48f40b4d6087ce9fb1983041fce8a725436cb1`
- TargetFramework attribute: `.NETCoreApp,Version=v8.0`
- Referenced assemblies: `System.Runtime 8.0.0.0`, `System.Collections.Concurrent 8.0.0.0`, `System.Threading 8.0.0.0`, `System.Collections 8.0.0.0`, `System.Linq.Expressions 8.0.0.0`, `System.Diagnostics.StackTrace 8.0.0.0`, `System.Text.RegularExpressions 8.0.0.0`, `System.Linq 8.0.0.0`, `DiffEngine 1.0.0.0`, `Microsoft.CSharp 8.0.0.0`

## Counts

- Public types: 53 (45 in namespace `Shouldly`, 8 elsewhere: `Shouldly.Configuration`, `Shouldly.ShouldlyExtensionMethods`)
- Types (any visibility) carrying `[ShouldlyMethods]`: 17 (17 public, 0 non-public)
- Public static methods on public types (no accessors): 194
- ... whose name starts with `Should`: 149 overloads, 57 distinct names
- ... `Should*` on a `[ShouldlyMethods]` type: 149; NOT on one: 0
- ... all public static methods on `[ShouldlyMethods]` types: 187 (63 distinct names)
- ... public static methods on `[ShouldlyMethods]` types whose name does NOT start with `Should`: `Shouldly.DynamicShould.Throw` x1, `Shouldly.DynamicShould.HaveProperty` x1, `Shouldly.Should.CompleteIn` x6, `Shouldly.Should.Throw` x14, `Shouldly.Should.NotThrow` x10, `Shouldly.Should.ThrowAsync` x4, `Shouldly.Should.NotThrowAsync` x2
- ... returning non-void: 61 on marked types
- ... taking a delegate parameter: 48; an `Expression<>`: 4; a Task: 26
- ... with a `customMessage` parameter: 172 of 187 (forms: `string?` x172)
- ... with a `[CallerArgumentExpression]` parameter: 0
- ... marked `[Obsolete]`: 0; `[MethodImpl(NoInlining)]`: 187 of 187 on marked types

## Distinct `Should*` method names (public static, all public types)

`ShouldAllBe` (1), `ShouldBe` (18), `ShouldBeAssignableTo` (2), `ShouldBeDecoratedWith` (1), `ShouldBeEmpty` (1), `ShouldBeEquivalentTo` (1), `ShouldBeFalse` (1), `ShouldBeGreaterThan` (2), `ShouldBeGreaterThanOrEqualTo` (2), `ShouldBeInOrder` (3), `ShouldBeInRange` (1), `ShouldBeLessThan` (2), `ShouldBeLessThanOrEqualTo` (2), `ShouldBeNegative` (6), `ShouldBeNull` (2), `ShouldBeNullOrEmpty` (1), `ShouldBeNullOrWhiteSpace` (1), `ShouldBeOfType` (2), `ShouldBeOfTypes` (2), `ShouldBeOneOf` (3), `ShouldBePositive` (6), `ShouldBeSameAs` (1), `ShouldBeSubsetOf` (2), `ShouldBeTrue` (1), `ShouldBeUnique` (3), `ShouldContain` (7), `ShouldContainKey` (1), `ShouldContainKeyAndValue` (1), `ShouldContainWithoutWhitespace` (1), `ShouldEndWith` (1), `ShouldHaveFlag` (1), `ShouldHaveSingleItem` (1), `ShouldMatch` (1), `ShouldMatchApproved` (1), `ShouldNotBe` (5), `ShouldNotBeAssignableTo` (2), `ShouldNotBeEmpty` (1), `ShouldNotBeInRange` (1), `ShouldNotBeNull` (2), `ShouldNotBeNullOrEmpty` (1), `ShouldNotBeNullOrWhiteSpace` (1), `ShouldNotBeOfType` (2), `ShouldNotBeOneOf` (3), `ShouldNotBeSameAs` (1), `ShouldNotContain` (4), `ShouldNotContainKey` (1), `ShouldNotContainValueForKey` (1), `ShouldNotEndWith` (2), `ShouldNotHaveFlag` (1), `ShouldNotMatch` (1), `ShouldNotStartWith` (1), `ShouldNotThrow` (10), `ShouldNotThrowAsync` (2), `ShouldSatisfyAllConditions` (4), `ShouldStartWith` (1), `ShouldThrow` (16), `ShouldThrowAsync` (4)

## All types carrying `[ShouldlyMethods]` (any visibility)

- `Shouldly.DynamicShould` (public, static class)
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
| `Shouldly.Configuration.FilenameGenerator` | delegate |  |  | MulticastDelegate -> Delegate |  |
| `Shouldly.Configuration.FindMethodUsingAttribute`1` | class |  |  |  |  |
| `Shouldly.Configuration.FirstNonShouldlyMethodFinder` | class |  |  |  |  |
| `Shouldly.Configuration.ITestMethodFinder` | interface |  |  |  |  |
| `Shouldly.Configuration.ShouldMatchConfiguration` | class |  |  |  |  |
| `Shouldly.Configuration.ShouldMatchConfigurationBuilder` | class |  |  |  |  |
| `Shouldly.Configuration.TestMethodInfo` | class |  |  |  |  |
| `Shouldly.DynamicShould` | static class | yes |  |  | Throw x1, HaveProperty x1 |
| `Shouldly.ExpectedActualIgnoreOrderShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualKeyShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualToleranceShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedActualWithCaseSensitivityShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedEquivalenceShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedOrderShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ExpectedShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.IShouldlyAssertionContext` | interface |  |  |  |  |
| `Shouldly.ObjectGraphTestExtensions` | static class | yes | EditorBrowsable |  | ShouldBeEquivalentTo x1 |
| `Shouldly.Should` | static class | yes | DebuggerStepThrough |  | CompleteIn x6, Throw x14, NotThrow x10, ThrowAsync x4, NotThrowAsync x2 |
| `Shouldly.ShouldAssertException` | exception class |  |  | Exception |  |
| `Shouldly.ShouldBeBooleanExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeTrue x1, ShouldBeFalse x1 |
| `Shouldly.ShouldBeDecoratedWithExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeDecoratedWith x1 |
| `Shouldly.ShouldBeDictionaryTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContainKey x1, ShouldNotContainKey x1, ShouldContainKeyAndValue x1, ShouldNotContainValueForKey x1 |
| `Shouldly.ShouldBeEnumerableTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContain x6, ShouldNotContain x3, ShouldAllBe x1, ShouldBeEmpty x1, ShouldNotBeEmpty x1, ShouldHaveSingleItem x1, ShouldBeSubsetOf x2, ShouldBeUnique x3, ShouldBe x1, ShouldBeInOrder x3, ShouldBeOfTypes x2 |
| `Shouldly.ShouldBeNullExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeNull x2, ShouldNotBeNull x2 |
| `Shouldly.ShouldBeStringTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldContainWithoutWhitespace x1, ShouldContain x1, ShouldNotContain x1, ShouldMatch x1, ShouldNotMatch x1, ShouldBeNullOrEmpty x1, ShouldNotBeNullOrEmpty x1, ShouldBeNullOrWhiteSpace x1, ShouldNotBeNullOrWhiteSpace x1, ShouldBe x3, ShouldStartWith x1, ShouldEndWith x1, ShouldNotStartWith x1, ShouldNotEndWith x2 |
| `Shouldly.ShouldBeTestExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldBeGreaterThan x2, ShouldBeLessThan x2, ShouldBeGreaterThanOrEqualTo x2, ShouldBeLessThanOrEqualTo x2, ShouldBePositive x6, ShouldBeNegative x6, ShouldBeOneOf x3, ShouldNotBeOneOf x3, ShouldBeInRange x1, ShouldNotBeInRange x1, ShouldBeAssignableTo x2, ShouldBeOfType x2, ShouldNotBeAssignableTo x2, ShouldNotBeOfType x2, ShouldBe x14, ShouldNotBe x5, ShouldBeSameAs x1, ShouldNotBeSameAs x1 |
| `Shouldly.ShouldCompleteInException` | exception class |  |  | ShouldlyTimeoutException -> TimeoutException -> SystemException -> Exception |  |
| `Shouldly.ShouldContainWithCountShouldlyMessage` | class |  |  | ShouldlyMessage |  |
| `Shouldly.ShouldMatchApprovedException` | exception class |  |  | ShouldAssertException -> Exception |  |
| `Shouldly.ShouldMatchApprovedTestExtensions` | static class | yes | EditorBrowsable |  | ShouldMatchApproved x1 |
| `Shouldly.ShouldNotThrowTaskAsyncExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldNotThrowAsync x2 |
| `Shouldly.ShouldSatisfyAllConditionsTestExtensions` | static class | yes | EditorBrowsable |  | ShouldSatisfyAllConditions x4 |
| `Shouldly.ShouldThrowAsyncExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldThrowAsync x4 |
| `Shouldly.ShouldThrowExtensions` | static class | yes | DebuggerStepThrough, EditorBrowsable |  | ShouldThrow x4, ShouldNotThrow x2 |
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

## Public static properties and fields (all public types)

- `Shouldly.ShouldlyConfiguration.CompareAsObjectTypes` property `List<string>` (get)
- `Shouldly.ShouldlyConfiguration.ShouldMatchApprovedDefaults` property `ShouldMatchConfigurationBuilder` (get)
- `Shouldly.ShouldlyConfiguration.DefaultFloatingPointTolerance` field `double`
- `Shouldly.ShouldlyConfiguration.DefaultTaskTimeout` field `TimeSpan`

## Public instance members of public non-static, non-enum types (declared only)

- `Shouldly.ActualFilteredWithPredicateShouldlyMessage`: `.ctor(Expression filter, object result, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ActualShouldlyMessage`: `.ctor(object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.AsyncShouldlyNotThrowShouldlyMessage`: `.ctor(Type exception, string /*nullability:?*/ customMessage, StackTrace stackTrace, string exceptionMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.AsyncShouldlyThrowShouldlyMessage`: `.ctor(Type exception, string /*nullability:?*/ customMessage, StackTrace stackTrace, [CallerMemberName] string shouldlyMethod = null)`; `.ctor(Type expected, Type actual, string /*nullability:?*/ customMessage, StackTrace stackTrace)`
- `Shouldly.CompleteInShouldlyMessage`: `.ctor(string what, TimeSpan timeout, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.Configuration.FilenameGenerator`: `.ctor(object object, IntPtr method)`; `string Invoke(TestMethodInfo testMethodInfo, string discriminator, string fileType, string fileExtension)`; `IAsyncResult BeginInvoke(TestMethodInfo testMethodInfo, string discriminator, string fileType, string fileExtension, AsyncCallback callback, object object)`; `string EndInvoke(IAsyncResult result)`
- `Shouldly.Configuration.FindMethodUsingAttribute`1`: `.ctor()`; `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`
- `Shouldly.Configuration.FirstNonShouldlyMethodFinder`: `.ctor()`; `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`; `int Offset { get; set; }`
- `Shouldly.Configuration.ITestMethodFinder`: `TestMethodInfo GetTestMethodInfo(StackTrace stackTrace, int startAt = 0)`
- `Shouldly.Configuration.ShouldMatchConfiguration`: `.ctor()`; `.ctor(ShouldMatchConfiguration initialConfig)`; `StringCompareShould StringCompareOptions { get; set; }`; `string FilenameDiscriminator { get; set; }`; `bool PreventDiff { get; set; }`; `string FileExtension { get; set; }`; `ITestMethodFinder TestMethodFinder { get; set; }`; `string ApprovalFileSubFolder { get; set; }`; `Func<string, string> Scrubber { get; set; }`; `FilenameGenerator FilenameGenerator { get; set; }`
- `Shouldly.Configuration.ShouldMatchConfigurationBuilder`: `.ctor(ShouldMatchConfiguration initialConfig)`; `ShouldMatchConfigurationBuilder WithStringCompareOptions(StringCompareShould stringCompareOptions)`; `ShouldMatchConfigurationBuilder WithDiscriminator(string fileDiscriminator)`; `ShouldMatchConfigurationBuilder NoDiff()`; `ShouldMatchConfigurationBuilder WithFileExtension(string fileExtension)`; `ShouldMatchConfigurationBuilder WithFilenameGenerator(FilenameGenerator filenameGenerator)`; `ShouldMatchConfigurationBuilder DoNotIgnoreLineEndings()`; `ShouldMatchConfigurationBuilder SubFolder(string subfolder)`; `ShouldMatchConfigurationBuilder UseCallerLocation()`; `ShouldMatchConfigurationBuilder LocateTestMethodUsingAttribute<T>() where T : Attribute`; `ShouldMatchConfigurationBuilder WithScrubber(Func<string, string> scrubber)`; `ShouldMatchConfigurationBuilder Configure(Action<ShouldMatchConfiguration> configure)`; `ShouldMatchConfiguration Build()`
- `Shouldly.Configuration.TestMethodInfo`: `.ctor(StackFrame callingFrame)`; `string SourceFileDirectory { get; }`; `string MethodName { get; }`; `string DeclaringTypeName { get; }`
- `Shouldly.ExpectedActualIgnoreOrderShouldlyMessage`: `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedActualKeyShouldlyMessage`: `.ctor(object expected, object actual, object key, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedActualShouldlyMessage`: `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedActualToleranceShouldlyMessage`: `.ctor(object expected, object actual, object tolerance, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedActualWithCaseSensitivityShouldlyMessage`: `.ctor(object expected, object actual, Case? caseSensitivity, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedEquivalenceShouldlyMessage`: `.ctor(object expected, object actual, IEnumerable<string> path, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedOrderShouldlyMessage`: `.ctor(object actual, SortDirection expectedDirection, int outOfOrderIndex, object outOfOrderObject, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ExpectedShouldlyMessage`: `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.IShouldlyAssertionContext`: `string ShouldMethod { get; set; }`; `string CodePart { get; set; }`; `string FileName { get; set; }`; `int? LineNumber { get; set; }`; `object Key { get; set; }`; `object Expected { get; set; }`; `object Actual { get; set; }`; `object Tolerance { get; set; }`; `TimeSpan? Timeout { get; set; }`; `bool IgnoreOrder { get; set; }`; `bool HasRelevantActual { get; set; }`; `bool HasRelevantKey { get; set; }`; `bool IsNegatedAssertion { get; }`; `string CustomMessage { get; set; }`; `Case? CaseSensitivity { get; set; }`; `bool CodePartMatchesActual { get; }`; `Expression Filter { get; set; }`; `int? MatchCount { get; set; }`; `SortDirection SortDirection { get; set; }`; `int OutOfOrderIndex { get; set; }`; `object OutOfOrderObject { get; set; }`; `IEnumerable<string> Path { get; set; }`
- `Shouldly.ShouldAssertException`: `.ctor(string message)`; `.ctor(string message, Exception innerException)`; `string StackTrace { get; }`
- `Shouldly.ShouldCompleteInException`: `.ctor(string message, ShouldlyTimeoutException inner)`
- `Shouldly.ShouldContainWithCountShouldlyMessage`: `.ctor(object expected, object actual, int matchCount, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ShouldMatchApprovedException`: `.ctor(string message, string receivedFile, string approvedFile)`
- `Shouldly.ShouldlyAssertionContext`: `.ctor(string shouldlyMethod, object expected = null, object actual = null, StackTrace stackTrace = null)`; `string ShouldMethod { get; set; }`; `string CodePart { get; set; }`; `string FileName { get; set; }`; `int? LineNumber { get; set; }`; `object Key { get; set; }`; `object Expected { get; set; }`; `object Actual { get; set; }`; `object Tolerance { get; set; }`; `Case? CaseSensitivity { get; set; }`; `bool CodePartMatchesActual { get; }`; `TimeSpan? Timeout { get; set; }`; `bool IgnoreOrder { get; set; }`; `bool HasRelevantActual { get; set; }`; `bool HasRelevantKey { get; set; }`; `bool IsNegatedAssertion { get; }`; `string CustomMessage { get; set; }`; `Expression Filter { get; set; }`; `int? MatchCount { get; set; }`; `SortDirection SortDirection { get; set; }`; `int OutOfOrderIndex { get; set; }`; `object OutOfOrderObject { get; set; }`; `IEnumerable<string> Path { get; set; }`
- `Shouldly.ShouldlyMessage`: `string ToString()`
- `Shouldly.ShouldlyMethodsAttribute`: `.ctor()`
- `Shouldly.ShouldlyThrowMessage`: `.ctor(object expected, string exceptionMessage, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`; `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`; `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`
- `Shouldly.ShouldlyTimeoutException`: `.ctor()`; `.ctor(string message, ShouldlyTimeoutException inner)`; `string StackTrace { get; }`
- `Shouldly.TaskShouldlyThrowMessage`: `.ctor(object expected, Exception exception, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`; `.ctor(object expected, object actual, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`; `.ctor(object expected, string /*nullability:?*/ customMessage, [CallerMemberName] string shouldlyMethod = null)`

## Enums

- `Shouldly.Case`: Sensitive, Insensitive
- `Shouldly.SortDirection`: Ascending, Descending
- `Shouldly.StringCompareShould`: IgnoreCase, IgnoreLineEndings

## Every public static method, by declaring type

### `Shouldly.DynamicShould` [ShouldlyMethods]

```csharp
void HaveProperty(object dynamicTestObject, string propertyName, string /*nullability:?*/ customMessage = null);
TException Throw<TException>(Action actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
```

### `Shouldly.ObjectGraphTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBeEquivalentTo(this object actual, object expected, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.Should` [ShouldlyMethods]

```csharp
T CompleteIn<T>(Func<T> function, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
T CompleteIn<T>(Func<Task<T>> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
T CompleteIn<T>(Task<T> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
void CompleteIn(Action action, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
void CompleteIn(Func<Task> actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
void CompleteIn(Task actual, TimeSpan timeout, string /*nullability:?*/ customMessage = null);
T NotThrow<T>(Func<T> action, string /*nullability:?*/ customMessage = null);
T NotThrow<T>(Func<Task<T>> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
T NotThrow<T>(Func<Task<T>> action, string /*nullability:?*/ customMessage = null);
T NotThrow<T>(Task<T> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
T NotThrow<T>(Task<T> action, string /*nullability:?*/ customMessage = null);
void NotThrow(Action action, string /*nullability:?*/ customMessage = null);
void NotThrow(Func<Task> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
void NotThrow(Func<Task> action, string /*nullability:?*/ customMessage = null);
void NotThrow(Task action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
void NotThrow(Task action, string /*nullability:?*/ customMessage = null);
Task NotThrowAsync(Func<Task> actual, string /*nullability:?*/ customMessage = null);
Task NotThrowAsync(Task task, string /*nullability:?*/ customMessage = null);
Exception Throw(Action actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
Exception Throw(Func<Task> actual, TimeSpan timeoutAfter, Type exceptionType);
Exception Throw(Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType);
Exception Throw(Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
Exception Throw(Func<object> actual, Type exceptionType);
Exception Throw(Func<object> actual, string /*nullability:?*/ customMessage, Type exceptionType);
Exception Throw(Task actual, TimeSpan timeoutAfter, Type exceptionType, string /*nullability:?*/ customMessage = null);
Exception Throw(Task actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
TException Throw<TException>(Action actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException Throw<TException>(Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException Throw<TException>(Func<Task> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException Throw<TException>(Func<object> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException Throw<TException>(Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException Throw<TException>(Task actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
Task<Exception> ThrowAsync(Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
Task<Exception> ThrowAsync(Task task, Type exceptionType, string /*nullability:?*/ customMessage = null);
Task<TException> ThrowAsync<TException>(Func<Task> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
Task<TException> ThrowAsync<TException>(Task task, string /*nullability:?*/ customMessage = null) where TException : Exception;
```

### `Shouldly.ShouldBeBooleanExtensions` [ShouldlyMethods]

```csharp
void ShouldBeFalse(this bool actual, string /*nullability:?*/ customMessage = null);
void ShouldBeTrue(this bool actual, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldBeDecoratedWithExtensions` [ShouldlyMethods]

```csharp
void ShouldBeDecoratedWith<T>(this Type actual, string /*nullability:?*/ customMessage = null) where T : Attribute;
```

### `Shouldly.ShouldBeDictionaryTestExtensions` [ShouldlyMethods]

```csharp
void ShouldContainKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, string /*nullability:?*/ customMessage = null);
void ShouldContainKeyAndValue<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue val, string /*nullability:?*/ customMessage = null);
void ShouldNotContainKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, string /*nullability:?*/ customMessage = null);
void ShouldNotContainValueForKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue val, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldBeEnumerableTestExtensions` [ShouldlyMethods]

```csharp
void ShouldAllBe<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null);
void ShouldBe(this IEnumerable<string> actual, IEnumerable<string> expected, Case caseSensitivity, string /*nullability:?*/ customMessage = null);
void ShouldBeEmpty<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, SortDirection expectedSortDirection, IComparer<T> customComparer, string /*nullability:?*/ customMessage = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, SortDirection expectedSortDirection, string /*nullability:?*/ customMessage = null);
void ShouldBeInOrder<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null);
void ShouldBeOfTypes<T>(this IEnumerable<T> actual, Type[] expected, string /*nullability:?*/ customMessage);
void ShouldBeOfTypes<T>(this IEnumerable<T> actual, params Type[] expected);
void ShouldBeSubsetOf<T>(this IEnumerable<T> actual, IEnumerable<T> expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeSubsetOf<T>(this IEnumerable<T> actual, IEnumerable<T> expected, string /*nullability:?*/ customMessage = null);
void ShouldBeUnique<T>(this IEnumerable<T> actual, IEqualityComparer<T> comparer);
void ShouldBeUnique<T>(this IEnumerable<T> actual, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage);
void ShouldBeUnique<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null);
void ShouldContain(this IEnumerable<double> actual, double expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldContain(this IEnumerable<float> actual, float expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, int expectedCount, string /*nullability:?*/ customMessage = null);
void ShouldContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null);
void ShouldContain<T>(this IEnumerable<T> actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldContain<T>(this IEnumerable<T> actual, T expected, string /*nullability:?*/ customMessage = null);
T ShouldHaveSingleItem<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null);
void ShouldNotBeEmpty<T>(this IEnumerable<T> actual, string /*nullability:?*/ customMessage = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, Expression<Func<T, bool>> elementPredicate, string /*nullability:?*/ customMessage = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldNotContain<T>(this IEnumerable<T> actual, T expected, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldBeNullExtensions` [ShouldlyMethods]

```csharp
void ShouldBeNull<T>(this T actual, string /*nullability:?*/ customMessage = null) where T : class;
void ShouldBeNull<T>(this T? actual, string /*nullability:?*/ customMessage = null) where T : struct;
T ShouldNotBeNull<T>(this T actual, string /*nullability:?*/ customMessage = null) where T : class;
T ShouldNotBeNull<T>(this T? actual, string /*nullability:?*/ customMessage = null) where T : struct;
```

### `Shouldly.ShouldBeStringTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBe(this string actual, string expected, StringCompareShould options);
void ShouldBe(this string actual, string expected, string /*nullability:?*/ customMessage = null);
void ShouldBe(this string actual, string expected, string /*nullability:?*/ customMessage, StringCompareShould options);
void ShouldBeNullOrEmpty(this string actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNullOrWhiteSpace(this string actual, string /*nullability:?*/ customMessage = null);
void ShouldContain(this string actual, string expected, Case caseSensitivity = 1, string /*nullability:?*/ customMessage = null);
void ShouldContainWithoutWhitespace(this string actual, object expected, string /*nullability:?*/ customMessage = null);
void ShouldEndWith(this string actual, string expected, Case caseSensitivity = 1, string /*nullability:?*/ customMessage = null);
void ShouldMatch(this string actual, string regexPattern, string /*nullability:?*/ customMessage = null);
void ShouldNotBeNullOrEmpty(this string actual, string /*nullability:?*/ customMessage = null);
void ShouldNotBeNullOrWhiteSpace(this string actual, string /*nullability:?*/ customMessage = null);
void ShouldNotContain(this string actual, string expected, Case caseSensitivity = 1, string /*nullability:?*/ customMessage = null);
void ShouldNotEndWith(this string actual, string expected, Case caseSensitivity);
void ShouldNotEndWith(this string actual, string expected, string /*nullability:?*/ customMessage = null, Case caseSensitivity = 1);
void ShouldNotMatch(this string actual, string regexPattern, string /*nullability:?*/ customMessage = null);
void ShouldNotStartWith(this string actual, string expected, Case caseSensitivity = 1, string /*nullability:?*/ customMessage = null);
void ShouldStartWith(this string actual, string expected, Case caseSensitivity = 1, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldBeTestExtensions` [ShouldlyMethods]

```csharp
void ShouldBe(this DateTime actual, DateTime expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this DateTimeOffset actual, DateTimeOffset expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this IEnumerable<decimal> actual, IEnumerable<decimal> expected, decimal tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this IEnumerable<double> actual, IEnumerable<double> expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this IEnumerable<float> actual, IEnumerable<float> expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this TimeSpan actual, TimeSpan expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this decimal actual, decimal expected, decimal tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this double actual, double expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe(this float actual, float expected, double tolerance, string /*nullability:?*/ customMessage = null);
void ShouldBe<T>(this IEnumerable<T> actual, IEnumerable<T> expected, IEqualityComparer<T> comparer, bool ignoreOrder = false, string /*nullability:?*/ customMessage = null);
void ShouldBe<T>(this IEnumerable<T> actual, IEnumerable<T> expected, bool ignoreOrder = false);
void ShouldBe<T>(this IEnumerable<T> actual, IEnumerable<T> expected, bool ignoreOrder, string /*nullability:?*/ customMessage);
void ShouldBe<T>(this T actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBe<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null);
T ShouldBeAssignableTo<T>(this object actual, string /*nullability:?*/ customMessage = null);
void ShouldBeAssignableTo(this object actual, Type expected, string /*nullability:?*/ customMessage = null);
void ShouldBeGreaterThan<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeGreaterThan<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldBeGreaterThanOrEqualTo<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeGreaterThanOrEqualTo<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldBeInRange<T>(this T actual, T from, T to, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldBeLessThan<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeLessThan<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldBeLessThanOrEqualTo<T>(this T actual, T expected, IComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeLessThanOrEqualTo<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldBeNegative(this decimal actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNegative(this double actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNegative(this float actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNegative(this int actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNegative(this long actual, string /*nullability:?*/ customMessage = null);
void ShouldBeNegative(this short actual, string /*nullability:?*/ customMessage = null);
T ShouldBeOfType<T>(this object actual, string /*nullability:?*/ customMessage = null);
void ShouldBeOfType(this object actual, Type expected, string /*nullability:?*/ customMessage = null);
void ShouldBeOneOf<T>(this T actual, T[] expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldBeOneOf<T>(this T actual, T[] expected, string /*nullability:?*/ customMessage);
void ShouldBeOneOf<T>(this T actual, params T[] expected);
void ShouldBePositive(this decimal actual, string /*nullability:?*/ customMessage = null);
void ShouldBePositive(this double actual, string /*nullability:?*/ customMessage = null);
void ShouldBePositive(this float actual, string /*nullability:?*/ customMessage = null);
void ShouldBePositive(this int actual, string /*nullability:?*/ customMessage = null);
void ShouldBePositive(this long actual, string /*nullability:?*/ customMessage = null);
void ShouldBePositive(this short actual, string /*nullability:?*/ customMessage = null);
void ShouldBeSameAs(this object actual, object expected, string /*nullability:?*/ customMessage = null);
void ShouldNotBe(this DateTime actual, DateTime expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldNotBe(this DateTimeOffset actual, DateTimeOffset expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldNotBe(this TimeSpan actual, TimeSpan expected, TimeSpan tolerance, string /*nullability:?*/ customMessage = null);
void ShouldNotBe<T>(this T actual, T expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldNotBe<T>(this T actual, T expected, string /*nullability:?*/ customMessage = null);
void ShouldNotBeAssignableTo(this object actual, Type expected, string /*nullability:?*/ customMessage = null);
void ShouldNotBeAssignableTo<T>(this object actual, string /*nullability:?*/ customMessage = null);
void ShouldNotBeInRange<T>(this T actual, T from, T to, string /*nullability:?*/ customMessage = null) where T : IComparable<T>;
void ShouldNotBeOfType(this object actual, Type expected, string /*nullability:?*/ customMessage = null);
void ShouldNotBeOfType<T>(this object actual, string /*nullability:?*/ customMessage = null);
void ShouldNotBeOneOf<T>(this T actual, T[] expected, IEqualityComparer<T> comparer, string /*nullability:?*/ customMessage = null);
void ShouldNotBeOneOf<T>(this T actual, T[] expected, string /*nullability:?*/ customMessage);
void ShouldNotBeOneOf<T>(this T actual, params T[] expected);
void ShouldNotBeSameAs(this object actual, object expected, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldMatchApprovedTestExtensions` [ShouldlyMethods]

```csharp
void ShouldMatchApproved(this string actual, Action<ShouldMatchConfigurationBuilder> configureOptions = null, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldNotThrowTaskAsyncExtensions` [ShouldlyMethods]

```csharp
Task ShouldNotThrowAsync(this Func<Task> actual, string /*nullability:?*/ customMessage = null);
Task ShouldNotThrowAsync(this Task task, string /*nullability:?*/ customMessage = null);
```

### `Shouldly.ShouldSatisfyAllConditionsTestExtensions` [ShouldlyMethods]

```csharp
void ShouldSatisfyAllConditions(this object actual, params Action[] conditions);
void ShouldSatisfyAllConditions(this object actual, string /*nullability:?*/ customMessage, params Action[] conditions);
void ShouldSatisfyAllConditions<T>(this T actual, params Action<T>[] conditions);
void ShouldSatisfyAllConditions<T>(this T actual, string /*nullability:?*/ customMessage, params Action<T>[] conditions);
```

### `Shouldly.ShouldThrowAsyncExtensions` [ShouldlyMethods]

```csharp
Task<Exception> ShouldThrowAsync(this Func<Task> actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
Task<Exception> ShouldThrowAsync(this Task task, Type exceptionType, string /*nullability:?*/ customMessage = null);
Task<TException> ShouldThrowAsync<TException>(this Func<Task> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
Task<TException> ShouldThrowAsync<TException>(this Task task, string /*nullability:?*/ customMessage = null) where TException : Exception;
```

### `Shouldly.ShouldThrowExtensions` [ShouldlyMethods]

```csharp
T ShouldNotThrow<T>(this Func<T> action, string /*nullability:?*/ customMessage = null);
void ShouldNotThrow(this Action action, string /*nullability:?*/ customMessage = null);
Exception ShouldThrow(this Action actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
Exception ShouldThrow(this Func<object> actual, Type exceptionType, string /*nullability:?*/ customMessage = null);
TException ShouldThrow<TException>(this Action actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException ShouldThrow<TException>(this Func<object> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
```

### `Shouldly.ShouldThrowTaskExtensions` [ShouldlyMethods]

```csharp
T ShouldNotThrow<T>(this Func<Task<T>> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
T ShouldNotThrow<T>(this Func<Task<T>> action, string /*nullability:?*/ customMessage = null);
T ShouldNotThrow<T>(this Task<T> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
T ShouldNotThrow<T>(this Task<T> action, string /*nullability:?*/ customMessage = null);
void ShouldNotThrow(this Func<Task> action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
void ShouldNotThrow(this Func<Task> action, string /*nullability:?*/ customMessage = null);
void ShouldNotThrow(this Task action, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null);
void ShouldNotThrow(this Task action, string /*nullability:?*/ customMessage = null);
Exception ShouldThrow(this Func<Task> actual, TimeSpan timeoutAfter, Type exceptionType);
Exception ShouldThrow(this Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType);
Exception ShouldThrow(this Func<Task> actual, Type exceptionType);
Exception ShouldThrow(this Func<Task> actual, string /*nullability:?*/ customMessage, Type exceptionType);
Exception ShouldThrow(this Task actual, TimeSpan timeoutAfter, Type exceptionType);
Exception ShouldThrow(this Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage, Type exceptionType);
Exception ShouldThrow(this Task actual, Type exceptionType);
Exception ShouldThrow(this Task actual, string /*nullability:?*/ customMessage, Type exceptionType);
TException ShouldThrow<TException>(this Func<Task> actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException ShouldThrow<TException>(this Func<Task> actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException ShouldThrow<TException>(this Task actual, TimeSpan timeoutAfter, string /*nullability:?*/ customMessage = null) where TException : Exception;
TException ShouldThrow<TException>(this Task actual, string /*nullability:?*/ customMessage = null) where TException : Exception;
```

### `Shouldly.ShouldlyConfiguration`

```csharp
IDisposable DisableSourceInErrors();
bool IsSourceDisabledInErrors();
```

### `Shouldly.ShouldlyCoreExtensions`

```csharp
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, Case caseSensitivity, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null);
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, object tolerance, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null);
void AssertAwesomely<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null);
void AssertAwesomelyIgnoringOrder<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null);
void AssertAwesomelyWithCaseSensitivity<T>(this T actual, Func<T, bool> specifiedConstraint, object originalActual, object originalExpected, Case caseSensitivity, string /*nullability:?*/ customMessage = null, [CallerMemberName] string shouldlyMethod = null);
```

### `Shouldly.ShouldlyExtensionMethods.ShouldHaveEnumExtensions` [ShouldlyMethods]

```csharp
void ShouldHaveFlag(this Enum actual, Enum expectedFlag, string /*nullability:?*/ customMessage = null);
void ShouldNotHaveFlag(this Enum actual, Enum expectedFlag, string /*nullability:?*/ customMessage = null);
```

