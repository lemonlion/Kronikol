using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kronikol.Tracking;

/// <summary>
/// Tracks assertions as inline notes in the sequence diagram.
/// Captures the assertion expression via <c>[CallerArgumentExpression]</c>,
/// formats it into a readable sentence, and injects it as a styled
/// <c>hnote across</c> entry in the PlantUML source.
/// </summary>
public static class Track
{
    internal const string PassColor = "#d4edda";
    internal const string FailColor = "#f8d7da";
    internal const string PassSymbol = "\u2713"; // ✓
    internal const string FailSymbol = "\u2717"; // ✗

    private static readonly ConcurrentQueue<string> DiagnosticEntries = new();

    /// <summary>
    /// When <c>true</c>, records diagnostic entries for assertion value resolution fallbacks.
    /// </summary>
    public static bool DiagnosticMode { get; set; }

    /// <summary>
    /// Diagnostic log entries recorded when <see cref="DiagnosticMode"/> is <c>true</c>.
    /// Contains details about closure value resolution fallbacks.
    /// </summary>
    public static IReadOnlyCollection<string> DiagnosticLog => DiagnosticEntries.ToArray();

    /// <summary>
    /// Clears all diagnostic log entries.
    /// </summary>
    public static void ClearDiagnosticLog() => DiagnosticEntries.Clear();

    /// <summary>
    /// Optional delegate that resolves the current test ID from a framework-specific context
    /// (e.g. LightBDD's <c>ScenarioExecutionContext</c>, xUnit's <c>TestContext</c>).
    /// Checked before <see cref="TestIdentityScope.Current"/> and <see cref="TestIdentityScope.GlobalFallback"/>.
    /// </summary>
    public static Func<string?>? TestIdResolver { get; set; }

    /// <summary>
    /// Executes an assertion action and tracks it in the sequence diagram.
    /// On success, logs a green assertion note. On failure, logs a red note and re-throws.
    /// </summary>
    public static void That(
        Action assertion,
        [CallerArgumentExpression(nameof(assertion))] string? expression = null,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        try
        {
            assertion();
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: true, resolvedValues: resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
        }
        catch (Exception ex)
        {
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: false, ex.Message, resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
            throw;
        }
    }

    /// <summary>
    /// Executes an assertion function, tracks it in the sequence diagram, and returns the value.
    /// </summary>
    public static T That<T>(
        Func<T> assertion,
        [CallerArgumentExpression(nameof(assertion))] string? expression = null,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        try
        {
            var result = assertion();
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: true, resolvedValues: resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
            return result;
        }
        catch (Exception ex)
        {
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: false, ex.Message, resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
            throw;
        }
    }

    /// <summary>
    /// Executes an async assertion and tracks it in the sequence diagram.
    /// </summary>
    public static async Task ThatAsync(
        Func<Task> assertion,
        [CallerArgumentExpression(nameof(assertion))] string? expression = null,
        [CallerFilePath] string? callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        try
        {
            await assertion();
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: true, resolvedValues: resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
        }
        catch (Exception ex)
        {
            var resolved = ResolveClosureValues(assertion, expression);
            LogAssertion(expression, passed: false, ex.Message, resolved,
                callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
            throw;
        }
    }

    /// <summary>
    /// Called by the IL weaver (AssertionWeaver) to log a passing assertion.
    /// Unlike <see cref="That(Action, string?, string?, int)"/>, this does NOT wrap
    /// the assertion in a lambda — the original code runs unchanged with full semantic
    /// fidelity (null propagation, ref params, etc.).
    /// </summary>
    public static void AssertionPassed(string expression, string? callerFilePath = null, int callerLineNumber = 0)
    {
        LogAssertion(expression, passed: true, callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
    }

    /// <summary>
    /// Called by the IL weaver (AssertionWeaver) to log a failing assertion.
    /// Unlike <see cref="That(Action, string?, string?, int)"/>, this does NOT wrap
    /// the assertion in a lambda — the original code runs unchanged with full semantic
    /// fidelity (null propagation, ref params, etc.).
    /// </summary>
    public static void AssertionFailed(string expression, string failureMessage, string? callerFilePath = null, int callerLineNumber = 0)
    {
        LogAssertion(expression, passed: false, failureMessage, callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
    }

    /// <summary>
    /// Called by the IL weaver (AssertionWeaver) to log a passing assertion with runtime variable values.
    /// The weaver emits arrays of variable names and their boxed values; this method reads each access chain that
    /// starts at one of them (members up to the first method call or index) and puts a value in the label only in
    /// place of exactly the code it was read for. A chain it cannot read is shown as written. A name that is not an
    /// identifier is an argument of the assertion call as written, and its value is the one the call received
    /// (from 4.16.0's weaver): it replaces that whole argument.
    /// </summary>
    public static void AssertionPassedWithValues(string expression, string[] varNames, object?[] varValues, string? callerFilePath = null, int callerLineNumber = 0)
    {
        var resolved = ResolveVariableValues(expression, varNames, varValues);
        LogAssertion(expression, passed: true, resolvedValues: resolved,
            callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
    }

    /// <summary>
    /// Attaches a file to the current step (or to the scenario if no step is active). The report copies it into its
    /// <c>attachments/</c> folder when it is drawn: an image draws inline, anything else as a link, and a file the report
    /// does not hold then (not there, or it could not be copied) is shown as its name and why, with no link.
    /// </summary>
    public static void Attachment(string filePath, string? name = null)
    {
        var testId = ResolveTestId();
        StepCollector.AddAttachment(testId, filePath, name);
    }

    /// <summary>
    /// Called by the IL weaver (AssertionWeaver) to log a failing assertion with runtime variable values.
    /// The weaver emits arrays of variable names and their boxed values; this method reads each access chain that
    /// starts at one of them (members up to the first method call or index) and puts a value in the label only in
    /// place of exactly the code it was read for. A chain it cannot read is shown as written. A name that is not an
    /// identifier is an argument of the assertion call as written, and its value is the one the call received
    /// (from 4.16.0's weaver): it replaces that whole argument.
    /// </summary>
    public static void AssertionFailedWithValues(string expression, string failureMessage, string[] varNames, object?[] varValues, string? callerFilePath = null, int callerLineNumber = 0)
    {
        var resolved = ResolveVariableValues(expression, varNames, varValues);
        LogAssertion(expression, passed: false, failureMessage, resolved,
            callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
    }

    private const int MaxValueLength = 100;

    internal static string? FormatValue(object? value)
    {
        if (value is null)
            return "null";

        if (value is string s)
            return s.Length > MaxValueLength
                ? s[..MaxValueLength] + "..."
                : s;

        if (value is IEnumerable enumerable)
        {
            var items = new List<object?>();
            var allScalar = true;
            foreach (var item in enumerable)
            {
                items.Add(item);
                if (items.Count > 10)
                {
                    allScalar = false;
                    break;
                }
                if (item != null && !AccessChain.IsScalarType(item.GetType()))
                    allScalar = false;
            }

            if (items.Count == 0)
                return "[0 items]";

            if (allScalar && items.Count <= 10)
            {
                var formatted = items.Select(i =>
                    i is null ? "null" :
                    i is string str ? $"\"{str}\"" :
                    i.ToString() ?? "null");
                return $"[ {string.Join(", ", formatted)} ]";
            }

            // For >10 items, we already collected 11 but need the full count.
            // Re-enumerate for IEnumerable that may not be re-iterable.
            // For collections implementing ICollection, use Count directly.
            if (items.Count > 10)
            {
                if (enumerable is System.Collections.ICollection collection)
                    return $"[{collection.Count} items]";

                // Already have items.Count from the first pass (stopped at 11+)
                return $"[{items.Count} items]";
            }
            return $"[{items.Count} items]";
        }

        try
        {
            var str = value.ToString();
            var typeName = value.GetType().FullName;
            var shortTypeName = value.GetType().Name;

            if (str is null || str == typeName || str == shortTypeName)
                return null;

            return str.Length > MaxValueLength
                ? str[..MaxValueLength] + "..."
                : str;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The values the weave passed, under the canonical text of the code each is the value of. A name that is an
    /// identifier is a variable, read for every access chain in the statement that starts at it, the subject's
    /// included: a chain's value under its own text, or the part read before a method call or an index when that is a
    /// single value (<see cref="AccessChain"/>); a chain that cannot be read gets no value and stays as written (#163).
    /// Any other name is an argument's text, passed with the value the assertion received for it (from 4.16.0's weave):
    /// that value is the argument's, and wins over a value read off a variable afterwards. An argument the statement
    /// threw before evaluating is passed as a plain <c>object</c>, which prints as its type name and so as nothing.
    /// </summary>
    private static Dictionary<string, string>? ResolveVariableValues(string expression, string[] varNames, object?[] varValues)
    {
        if (varNames == null || varValues == null || varNames.Length == 0)
            return null;

        try
        {
            var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
            var values = new ChainValues();
            for (var i = 0; i < varNames.Length; i++)
            {
                var value = i < varValues.Length ? varValues[i] : null;
                if (AccessChain.IsIdentifier(varNames[i]))
                    roots.TryAdd(varNames[i], value);
                else if (FormatValue(value) is { } formatted)
                    values.TryAdd(AccessChain.CanonicalKey(varNames[i]), formatted);
            }

            var read = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chain in AccessChain.Find(expression))
            {
                if (roots.TryGetValue(chain.Root, out var rootValue) && read.Add(chain.Key))
                    values.Keep(chain, chain.Read(rootValue), (value, _) => FormatValue(value));
            }

            return values.Count > 0 ? values : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ResolveClosureValues(Delegate assertion, string? expression)
    {
        try
        {
            var result = ClosureValueResolver.ResolveValues(assertion, expression);

            if (DiagnosticMode && result.Fallbacks.Count > 0)
            {
                foreach (var (fieldName, reason) in result.Fallbacks)
                    DiagnosticEntries.Enqueue(
                        $"[Track.That] Value resolution fallback: '{fieldName}' — {reason} (expression: {expression})");
            }

            return result.ResolvedValues.Count > 0 ? result.ResolvedValues : null;
        }
        catch
        {
            // Resolution must never break assertion tracking
            return null;
        }
    }

    private static void LogAssertion(string? expression, bool passed, string? failureMessage = null,
        Dictionary<string, string>? resolvedValues = null, string? callerFilePath = null, int callerLineNumber = 0)
    {
        var testId = ResolveTestId();
        if (testId is null)
            return;

        // Inside an AssertionScope a failing assertion returns instead of throwing, and the scope throws at its
        // end: an assertion that returned failed if the scope gathered a failure no assertion has claimed
        // (SHOULDLY_ASSERTIONS_PLAN F12). One that threw owns what it gathered too.
        var gathered = AssertionScopeProbe.ClaimNewFailures();
        if (passed && gathered.Count > 0)
        {
            passed = false;
            failureMessage = string.Join("\n", gathered);
        }

        // If there's an active step, record this assertion as a sub-step
        if (StepCollector.HasActiveStep(testId))
        {
            var assertionText = AssertionExpressionFormatter.Format(expression, resolvedValues)
                                ?? expression ?? "assertion";
            StepCollector.AddAssertionSubStep(testId, assertionText, passed,
                failureMessage, SourceFileName(callerFilePath), callerLineNumber);
        }

        var formatted = AssertionExpressionFormatter.Format(expression, resolvedValues);
        if (string.IsNullOrEmpty(formatted))
            formatted = expression ?? "assertion";

        var color = passed ? PassColor : FailColor;
        var symbol = passed ? PassSymbol : FailSymbol;

        // The note is the reader's sentence: capitalise it the way the step list is capitalised. Its code is code, not
        // markup anyone wrote, so its creole is escaped as a payload's is: a type argument <b>, two "" or two URLs'
        // // on one line styled the text instead of drawing it (#210, Q9). The step list keeps the label as it is.
        var label = PlantUml.PlantUmlCreator.EscapeCreoleMarkup(Reports.StepText.CapitaliseIfEnabled(formatted) ?? formatted);

        // The assertion note's body never passes through FormatNoteContent, so it is the one note class
        // that misses the unbreakable-run bound — an assertion diff quoting a token, a connection string
        // or minified JSON has no whitespace for `skinparam wrapWidth` to break at, and the note simply
        // grows (measured: a 1200-character run drew 8003px).
        var noteContent = PlantUml.DiagramWidth.WrapBlockNoteBody(
            passed ? $"{symbol} {label}" : $"{symbol} {label}\n{failureMessage}");

        var plantUml = $"hnote across <<assertionNote>> {color}\n{noteContent}\nend note";

        // Append source location as a PlantUML comment (invisible in rendered output)
        if (SourceFileName(callerFilePath) is { } fileName)
            plantUml += $"\n'__^*__:{fileName}:L{callerLineNumber}";

        DefaultTrackingDiagramOverride.InsertPlantUml(testId, plantUml, DiagramMarkerKind.Assertion);
    }

    /// <summary>
    /// The bare file name from a <c>CallerFilePath</c>, or null when the caller supplied none. Uses
    /// <see cref="string.LastIndexOfAny(char[])"/> rather than <c>Path.GetFileName</c> because the path was
    /// baked in at compile time on whichever machine built the test — a Windows path can arrive on Linux,
    /// where <c>Path</c> does not treat a backslash as a separator.
    /// </summary>
    private static string? SourceFileName(string? callerFilePath)
    {
        if (string.IsNullOrEmpty(callerFilePath))
            return null;

        var separatorIndex = callerFilePath.LastIndexOfAny(['/', '\\']);
        return separatorIndex >= 0 ? callerFilePath[(separatorIndex + 1)..] : callerFilePath;
    }

    private static string? ResolveTestId()
    {
        try
        {
            var resolved = TestIdResolver?.Invoke();
            if (resolved is not null)
                return resolved;
        }
        catch
        {
            // Resolver threw (e.g. no active scenario context) — fall through
        }

        var identity = TestIdentityScope.Current;
        if (identity is not null)
            return identity.Value.Id;

        var fallback = TestIdentityScope.GlobalFallback;
        if (fallback is not null)
            return fallback.Value.Id;

        return null;
    }
}
