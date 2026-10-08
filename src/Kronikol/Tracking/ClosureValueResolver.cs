using System.Collections;
using System.Reflection;

namespace Kronikol.Tracking;

/// <summary>
/// Inspects a delegate's closure (Target) to resolve runtime values of captured variables.
/// Returns a dictionary of variable-name → display-value for variables that appear
/// as assertion arguments in the expression text.
/// </summary>
public static class ClosureValueResolver
{
    private const int MaxValueLength = 50;

    private static readonly HashSet<char> ArithmeticOperators = ['+', '-', '*', '/', '%'];

    /// <summary>
    /// Attempts to resolve captured variable values from the delegate's closure.
    /// Returns resolved values and any fallback reasons (for diagnostic logging).
    /// </summary>
    public static ResolveResult ResolveValues(Delegate assertion, string? expression)
    {
        var result = new ResolveResult();

        if (string.IsNullOrWhiteSpace(expression))
            return result;

        var target = assertion.Target;
        if (target is null)
            return result;

        var argsText = ExtractArgsText(expression);
        if (argsText is null)
            return result;

        var closureFields = GetClosureFields(target);

        foreach (var (name, value) in closureFields)
        {
            if (!IsStandaloneToken(argsText, name))
                continue;

            // Check if the token is followed by a dotted property chain (e.g. "expected.Prop.Sub")
            var dottedChain = ExtractDottedChain(argsText, name);
            if (dottedChain is not null)
            {
                var fullKey = name + dottedChain;

                if (IsInComputedExpression(argsText, fullKey))
                {
                    result.AddFallback(fullKey, "computed expression");
                    continue;
                }

                var leafValue = WalkPropertyChain(value, dottedChain);
                if (leafValue.Success)
                {
                    var displayValue = FormatValue(leafValue.Value, fullKey, result);
                    if (displayValue is not null)
                        result.ResolvedValues[fullKey] = displayValue;
                }
                else
                {
                    result.AddFallback(fullKey, leafValue.FailureReason ?? "property chain navigation failed");
                }
                continue;
            }

            if (IsInComputedExpression(argsText, name))
            {
                result.AddFallback(name, "computed expression");
                continue;
            }

            var displayVal = FormatValue(value, name, result);
            if (displayVal is not null)
                result.ResolvedValues[name] = displayVal;
        }

        return result;
    }

    private static string? ExtractArgsText(string expression)
    {
        // Strip lambda prefix
        var expr = expression.StartsWith("() => ")
            ? expression["() => ".Length..]
            : expression;

        // Find .Should(). and then the method args; a Shouldly assertion is the call itself
        // (`result.ShouldBe(42)`, `Should.Throw<T>(…)`), so its own arguments are the ones read.
        var shouldIdx = expr.IndexOf(".Should().", StringComparison.Ordinal);
        string afterShould;
        if (shouldIdx >= 0)
            afterShould = expr[(shouldIdx + ".Should().".Length)..];
        else if (ShouldlyCall(expr) is var shouldly and >= 0)
            afterShould = expr[shouldly..];
        else
            return null;

        // Find the opening paren of the assertion method
        var parenIdx = afterShould.IndexOf('(');
        if (parenIdx < 0)
            return null;

        // Extract content between outermost parens, skipping brackets inside string and character literals
        var literal = AssertionExpressionFormatter.LiteralMask(afterShould);
        var depth = 0;
        var start = parenIdx + 1;
        for (var i = parenIdx; i < afterShould.Length; i++)
        {
            if (literal[i])
                continue;
            switch (afterShould[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                        return afterShould[start..i];
                    break;
            }
        }

        return null;
    }

    /// <summary>Where a Shouldly call's method name starts (<c>ShouldBe</c> in <c>result.ShouldBe(42)</c>,
    /// <c>Throw</c> in <c>Should.Throw&lt;T&gt;(…)</c>), or -1.</summary>
    private static int ShouldlyCall(string expr)
    {
        if (expr.StartsWith("Should.", StringComparison.Ordinal))
            return "Should.".Length;
        for (var at = expr.IndexOf(".Should", StringComparison.Ordinal); at >= 0;
             at = expr.IndexOf(".Should", at + 1, StringComparison.Ordinal))
        {
            var next = at + ".Should".Length;
            if (next < expr.Length && char.IsUpper(expr[next]))
                return at + 1;
        }
        return -1;
    }

    private static List<(string Name, object? Value)> GetClosureFields(object target)
    {
        var results = new List<(string, object?)>();
        var type = target.GetType();
        var isCompilerGenerated = type.IsDefined(
            typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

        // If target is NOT compiler-generated, it means the lambda captured 'this' directly
        // (no local variables captured). Inspect the instance's fields.
        if (!isCompilerGenerated)
        {
            var instanceFields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in instanceFields)
                results.Add((f.Name, f.GetValue(target)));
            return results;
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in fields)
        {
            var name = field.Name;
            var value = field.GetValue(target);

            // Handle 'this' capture — compiler names it <>4__this or similar
            if (name.Contains("__this") || name.Contains("<>4__this"))
            {
                if (value is not null)
                {
                    var thisFields = value.GetType().GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var tf in thisFields)
                        results.Add((tf.Name, tf.GetValue(value)));
                }
                continue;
            }

            results.Add((name, value));
        }

        return results;
    }

    internal static bool IsStandaloneToken(string text, string token)
    {
        var idx = 0;
        while (idx <= text.Length - token.Length)
        {
            idx = text.IndexOf(token, idx, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var before = idx > 0 ? text[idx - 1] : ' ';
            var after = idx + token.Length < text.Length ? text[idx + token.Length] : ' ';

            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
                return true;

            idx += token.Length;
        }

        return false;
    }

    internal static bool IsInComputedExpression(string argsText, string token)
    {
        var idx = 0;
        while (idx <= argsText.Length - token.Length)
        {
            idx = argsText.IndexOf(token, idx, StringComparison.Ordinal);
            if (idx < 0)
                return false;

            var before = idx > 0 ? argsText[idx - 1] : ' ';
            var after = idx + token.Length < argsText.Length ? argsText[idx + token.Length] : ' ';

            if (!IsIdentifierChar(before) && !IsIdentifierChar(after))
            {
                // Check chars around the token (skip whitespace) for arithmetic operators
                if (HasAdjacentOperator(argsText, idx, token.Length))
                    return true;
            }

            idx += token.Length;
        }

        return false;
    }

    private static bool HasAdjacentOperator(string text, int tokenStart, int tokenLength)
    {
        // Check after token
        for (var i = tokenStart + tokenLength; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;
            if (ArithmeticOperators.Contains(text[i]))
                return true;
            break;
        }

        // Check before token
        for (var i = tokenStart - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;
            if (ArithmeticOperators.Contains(text[i]))
                return true;
            break;
        }

        return false;
    }

    /// <summary>
    /// If the token in argsText is followed by ".Property.Sub..." chains, extracts that suffix
    /// (e.g. ".ExpectedIngredientCount"). Returns null if no dotted chain follows.
    /// </summary>
    private static string? ExtractDottedChain(string argsText, string token)
    {
        var idx = 0;
        while (idx <= argsText.Length - token.Length)
        {
            idx = argsText.IndexOf(token, idx, StringComparison.Ordinal);
            if (idx < 0)
                return null;

            var before = idx > 0 ? argsText[idx - 1] : ' ';
            var afterPos = idx + token.Length;

            if (!IsIdentifierChar(before) && afterPos < argsText.Length && argsText[afterPos] == '.')
            {
                // Extract the full dotted chain: .Prop1.Prop2...
                var chainStart = afterPos;
                var pos = chainStart + 1; // skip the first dot
                while (pos < argsText.Length && (IsIdentifierChar(argsText[pos]) || argsText[pos] == '.'))
                {
                    // Don't end on a trailing dot
                    if (argsText[pos] == '.' && (pos + 1 >= argsText.Length || !IsIdentifierChar(argsText[pos + 1])))
                        break;
                    pos++;
                }

                var chain = argsText[chainStart..pos];
                if (chain.Length > 1) // must be ".X" at minimum
                    return chain;
            }

            idx += token.Length;
        }

        return null;
    }

    private static (bool Success, object? Value, string? FailureReason) WalkPropertyChain(object? root, string chain)
    {
        // chain is e.g. ".ExpectedIngredientCount" or ".Inner.Value"
        var segments = chain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var segment in segments)
        {
            if (current is null)
                return (true, null, null); // null in the middle → resolve as "null"

            var prop = current.GetType().GetProperty(segment,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop is null)
            {
                var field = current.GetType().GetField(segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is null)
                    return (false, null, $"property '{segment}' not found on {current.GetType().Name}");
                current = field.GetValue(current);
            }
            else
            {
                current = prop.GetValue(current);
            }
        }

        return (true, current, null);
    }

    private static string? FormatValue(object? value, string fieldName, ResolveResult result)
    {
        if (value is null)
            return "null";

        if (value is string s)
            return s.Length > MaxValueLength
                ? s[..MaxValueLength] + "..."
                : s;

        // Collections — show inline values for small scalar collections, count for others
        if (value is IEnumerable enumerable and not string)
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
                    i is string str2 ? $"\"{str2}\"" :
                    i.ToString() ?? "null");
                return $"[ {string.Join(", ", formatted)} ]";
            }

            if (items.Count > 10)
            {
                if (enumerable is System.Collections.ICollection collection)
                    return $"[{collection.Count} items]";
                return $"[{items.Count} items]";
            }
            return $"[{items.Count} items]";
        }

        var str = value.ToString();
        var typeName = value.GetType().FullName;
        var shortTypeName = value.GetType().Name;

        // If ToString() just returns the type name, it's an unresolvable complex object
        if (str is null || str == typeName || str == shortTypeName)
        {
            result.AddFallback(fieldName, "ToString returned type name");
            return null;
        }

        return str.Length > MaxValueLength
            ? str[..MaxValueLength] + "..."
            : str;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsScalarType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string)
            || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(DateOnly)
            || type == typeof(TimeOnly);
    }
}

/// <summary>
/// Result of closure value resolution, containing resolved values and any fallback diagnostics.
/// </summary>
public class ResolveResult
{
    public Dictionary<string, string> ResolvedValues { get; } = new();
    public List<(string FieldName, string Reason)> Fallbacks { get; } = [];

    public void AddFallback(string fieldName, string reason)
    {
        Fallbacks.Add((fieldName, reason));
    }
}
