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
    /// <remarks>
    /// The assertion's arguments are read as the weave's values are (<see cref="AccessChain"/>): each access chain that
    /// starts at a captured variable, members up to the first method call or index, under its own text. A chain that
    /// cannot be read (a member that is not there, a getter that throws, a null before a plain <c>.</c>) costs only
    /// itself, and is recorded as a fallback. An operand of an arithmetic operator is left as code.
    /// </remarks>
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

        var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in GetClosureFields(target))
            roots.TryAdd(name, value);

        var chains = AccessChain.Find(argsText).Where(chain => roots.ContainsKey(chain.Root)).ToList();
        var computed = chains
            .Where(chain => HasAdjacentOperator(argsText, chain.Start, chain.End - chain.Start))
            .Select(chain => chain.Key)
            .ToHashSet(StringComparer.Ordinal);
        var values = (ChainValues)result.ResolvedValues;
        var read = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chain in chains)
        {
            if (!read.Add(chain.Key))
                continue;

            if (computed.Contains(chain.Key))
            {
                result.AddFallback(chain.Key, "computed expression");
                continue;
            }

            var chainRead = chain.Read(roots[chain.Root]);
            if (!chainRead.Readable)
            {
                result.AddFallback(chain.Key, chainRead.Reason ?? "property chain navigation failed");
                continue;
            }

            if (!values.Keep(chain, chainRead, (value, key) => FormatValue(value, key, result)) && !chainRead.Whole)
                result.AddFallback(chain.Key, "read up to a method call or an index, and not a single value");
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
                if (item != null && !AccessChain.IsScalarType(item.GetType()))
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
}

/// <summary>
/// Result of closure value resolution, containing resolved values and any fallback diagnostics.
/// </summary>
public class ResolveResult
{
    public Dictionary<string, string> ResolvedValues { get; } = new ChainValues();
    public List<(string FieldName, string Reason)> Fallbacks { get; } = [];

    public void AddFallback(string fieldName, string reason)
    {
        Fallbacks.Add((fieldName, reason));
    }
}
