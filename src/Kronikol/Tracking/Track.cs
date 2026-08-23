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
    /// The weaver emits arrays of variable names and their boxed values; this method resolves
    /// dotted property paths and substitutes formatted values into the expression text.
    /// </summary>
    public static void AssertionPassedWithValues(string expression, string[] varNames, object?[] varValues, string? callerFilePath = null, int callerLineNumber = 0)
    {
        var resolved = ResolveVariableValues(expression, varNames, varValues);
        LogAssertion(expression, passed: true, resolvedValues: resolved,
            callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
    }

    /// <summary>
    /// Called by the IL weaver (AssertionWeaver) to log a failing assertion with runtime variable values.
    /// The weaver emits arrays of variable names and their boxed values; this method resolves
    /// dotted property paths and substitutes formatted values into the expression text.
    /// </summary>

    /// <summary>
    /// Attaches a file to the current step (or to the scenario if no step is active).
    /// The file will be rendered as a download link in the report.
    /// </summary>
    public static void Attachment(string filePath, string? name = null)
    {
        var testId = ResolveTestId();
        StepCollector.AddAttachment(testId, filePath, name);
    }

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
                if (item != null && !IsScalarType(item.GetType()))
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

    private static bool IsScalarType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string)
            || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(DateOnly)
            || type == typeof(TimeOnly);
    }

    private static Dictionary<string, string>? ResolveVariableValues(string expression, string[] varNames, object?[] varValues)
    {
        if (varNames == null || varValues == null || varNames.Length == 0)
            return null;

        try
        {
            var resolved = new Dictionary<string, string>();

            for (var i = 0; i < varNames.Length; i++)
            {
                var name = varNames[i];
                var value = i < varValues.Length ? varValues[i] : null;

                // Resolve dotted paths in the expression (up to 3 levels deep)
                ResolveDottedPaths(expression, name, value, resolved);

                // Also add the root variable's formatted value
                var formatted = FormatValue(value);
                if (formatted is not null)
                    resolved.TryAdd(name, formatted);
            }

            return resolved.Count > 0 ? resolved : null;
        }
        catch
        {
            return null;
        }
    }

    private static void ResolveDottedPaths(string expression, string rootName, object? rootValue, Dictionary<string, string> resolved)
    {
        if (rootValue is null)
            return;

        // Scan expression for patterns like rootName.Prop or rootName.Prop.Sub (up to 3 levels)
        var searchStart = 0;
        while (true)
        {
            var idx = expression.IndexOf(rootName, searchStart, StringComparison.Ordinal);
            if (idx < 0) break;

            // Ensure it's a word boundary before
            if (idx > 0 && IsIdentifierChar(expression[idx - 1]))
            {
                searchStart = idx + rootName.Length;
                continue;
            }

            var afterRoot = idx + rootName.Length;
            if (afterRoot >= expression.Length || expression[afterRoot] != '.')
            {
                searchStart = afterRoot;
                continue;
            }

            // We have rootName. — extract the dotted path (up to 3 segments)
            var path = ExtractDottedPath(expression, afterRoot, maxSegments: 3);
            if (path is not null)
            {
                var fullPath = rootName + path;
                if (!resolved.ContainsKey(fullPath))
                {
                    var walked = WalkPropertyChain(rootValue, path[1..]); // skip leading '.'
                    var formatted = FormatValue(walked);
                    if (formatted is not null)
                        resolved[fullPath] = formatted;
                }
            }

            searchStart = afterRoot;
        }
    }

    private static string? ExtractDottedPath(string expression, int dotIndex, int maxSegments)
    {
        var pos = dotIndex;
        var segments = 0;
        var result = "";

        while (segments < maxSegments && pos < expression.Length && expression[pos] == '.')
        {
            pos++; // skip '.'
            var segStart = pos;
            while (pos < expression.Length && IsIdentifierChar(expression[pos]))
                pos++;

            if (pos == segStart) break; // empty segment

            result += "." + expression[segStart..pos];
            segments++;
        }

        return segments > 0 ? result : null;
    }

    private static object? WalkPropertyChain(object? root, string path)
    {
        if (root is null) return null;

        var current = root;
        var segments = path.Split('.');

        foreach (var segment in segments)
        {
            if (current is null) return null;

            var type = current.GetType();
            var prop = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop is not null)
            {
                try { current = prop.GetValue(current); }
                catch { return null; }
                continue;
            }

            var field = type.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                try { current = field.GetValue(current); }
                catch { return null; }
                continue;
            }

            return null; // segment not found
        }

        return current;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

        // The note is the reader's sentence: capitalise it the way the step list is capitalised.
        var label = Reports.StepText.CapitaliseIfEnabled(formatted) ?? formatted;

        var noteContent = passed
            ? $"{symbol} {label}"
            : $"{symbol} {label}\n{failureMessage}";

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
