using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Tracking;

/// <summary>
/// Parses assertion expressions (typically captured via <c>[CallerArgumentExpression]</c>)
/// into readable English sentences: FluentAssertions' <c>.Should().Method(args)</c>, Shouldly's
/// <c>subject.ShouldMethod(args)</c> and TUnit's <c>Assert.That(subject).Method(args)</c>, falling back to the code
/// for other assertion styles. The arguments keep their code, with a value in quotes only in place of exactly the code
/// it was read for; code with no value is shown as written.
/// </summary>
public static partial class AssertionExpressionFormatter
{
    private static readonly Regex ShouldSplitRegex = CreateShouldSplitRegex();

    [GeneratedRegex(@"\.Should\(\)\.", RegexOptions.None)]
    private static partial Regex CreateShouldSplitRegex();

    public static string Format(string? expression)
    {
        return Format(expression, resolvedValues: null);
    }

    public static string Format(string? expression, Dictionary<string, string>? resolvedValues)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "";

        // Strip lambda prefix: "() => "
        var expr = expression.StartsWith("() => ")
            ? expression["() => ".Length..]
            : expression;

        // Strip await prefix
        if (expr.StartsWith("await "))
            expr = expr["await ".Length..];

        // Strip var assignment prefix: "var identifier = "
        if (expr.StartsWith("var "))
        {
            var eqIdx = expr.IndexOf(" = ", 4, StringComparison.Ordinal);
            if (eqIdx > 0)
                expr = expr[(eqIdx + 3)..];
        }

        // Remove null-forgiving operators (!) and null-conditional ones (?. reads as .), and the whitespace
        // before a dot (multi-line expressions from [CallerArgumentExpression]): in the code, never in a literal.
        expr = CleanCode(expr);

        // Split on .Should().
        var match = ShouldSplitRegex.Match(expr);
        if (!match.Success)
            return FormatShouldly(expr, resolvedValues) ?? FormatTUnit(expr, resolvedValues) ?? expr;

        var subject = expr[..match.Index];
        var assertionPart = expr[(match.Index + match.Length)..];

        // Format subject: split on '.', PascalCase-split each segment, rejoin with spaces
        var formattedSubject = FormatSubject(subject);

        // Format assertion: method(args) — handle .And. chaining by taking first assertion
        var andIndex = assertionPart.IndexOf(".And.", StringComparison.Ordinal);
        if (andIndex >= 0)
            assertionPart = assertionPart[..andIndex];

        var (method, args) = ParseMethodAndArgs(assertionPart);
        var formattedMethod = SplitPascalCase(method).ToLowerInvariant();
        var formattedArgs = FormatArgs(args, resolvedValues);

        return string.IsNullOrEmpty(formattedArgs)
            ? $"{formattedSubject} should {formattedMethod}"
            : $"{formattedSubject} should {formattedMethod} {formattedArgs}";
    }

    /// <summary>
    /// Shouldly's two shapes, read by rule rather than by table, since its method list is long and grows:
    /// <c>subject.ShouldMethod(args)</c> reads "{Subject} should {method words} {args}", the words being the method
    /// name after <c>Should</c> split on case, and <c>Should.Throw&lt;T&gt;(…)</c> reads "Should throw T". An
    /// <c>Async</c> suffix is not read, a type argument is named without its brackets, and an action written
    /// <c>() =&gt; …</c> is not shown: Shouldly runs it (each condition of <c>ShouldSatisfyAllConditions</c> is a
    /// row of its own). A custom message passed by name is not shown either; the weave leaves a positional one out
    /// of the statement's text. Null when the expression is neither shape.
    /// </summary>
    private static string? FormatShouldly(string expr, Dictionary<string, string>? resolvedValues)
    {
        string subject, call;
        if (expr.StartsWith("Should.", StringComparison.Ordinal))
        {
            subject = "";
            call = expr["Should.".Length..];
        }
        else
        {
            var at = TopLevelShouldCall(expr);
            if (at < 0)
                return null;
            subject = expr[..at];
            call = expr[(at + 1 + "Should".Length)..];
        }

        var (method, args) = ParseMethodAndArgs(call);
        string? typeArgument = null;
        if (args is not null && args.StartsWith('<') && args.IndexOf('>') is > 0 and var close)
        {
            typeArgument = args[1..close];
            args = args[(close + 1)..].TrimStart(',', ' ');
        }

        var words = SplitPascalCase(method).ToLowerInvariant();
        if (words == "async" || words.EndsWith(" async", StringComparison.Ordinal))
            words = words[..^"async".Length].TrimEnd();

        var shown = SplitTopLevelCommas(args ?? "")
            .Select(a => a.Trim())
            .Where(a => a.Length > 0 &&
                        !a.StartsWith("() =>", StringComparison.Ordinal) &&
                        !a.StartsWith("async () =>", StringComparison.Ordinal) &&
                        !a.StartsWith("customMessage:", StringComparison.Ordinal))
            .ToArray();
        var formattedArgs = FormatArgs(shown.Length == 0 ? null : string.Join(", ", shown), resolvedValues);

        var sentence = new StringBuilder(subject.Length == 0 ? "Should" : FormatSubject(subject) + " should");
        foreach (var part in new[] { words, typeArgument, formattedArgs })
        {
            if (!string.IsNullOrEmpty(part))
                sentence.Append(' ').Append(part);
        }
        return sentence.ToString();
    }

    /// <summary>
    /// TUnit's <c>Assert.That(subject).IsEqualTo(5)</c> reads "Subject is equal to 5": the subject between the
    /// brackets of <c>Assert.That</c>, then the first assertion's words and its arguments, with resolved values put
    /// in as for FluentAssertions. It was drawn as the raw code, and the values the weave read were dropped
    /// (SHOULDLY_ASSERTIONS_PLAN section 8, item 2). Null when the expression is not that shape.
    /// </summary>
    private static string? FormatTUnit(string expr, Dictionary<string, string>? resolvedValues)
    {
        const string prefix = "Assert.That(";
        if (!expr.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var literal = LiteralMask(expr);
        var depth = 0;
        var close = -1;
        for (var i = prefix.Length - 1; i < expr.Length && close < 0; i++)
        {
            if (literal[i])
                continue;
            if (expr[i] == '(')
                depth++;
            else if (expr[i] == ')' && --depth == 0)
                close = i;
        }
        if (close < 0 || close + 2 >= expr.Length || expr[close + 1] != '.')
            return null;

        var subject = expr[prefix.Length..close];
        var assertionPart = expr[(close + 2)..];
        foreach (var chain in new[] { ".And.", ".Or." })
        {
            var at = assertionPart.IndexOf(chain, StringComparison.Ordinal);
            if (at >= 0)
                assertionPart = assertionPart[..at];
        }

        var (method, args) = ParseMethodAndArgs(assertionPart);
        var words = SplitPascalCase(method).ToLowerInvariant();
        var formattedArgs = FormatArgs(args, resolvedValues);
        return string.IsNullOrEmpty(formattedArgs)
            ? $"{FormatSubject(subject)} {words}"
            : $"{FormatSubject(subject)} {words} {formattedArgs}";
    }

    /// <summary>The index of the dot before the first <c>.ShouldX(</c> call outside brackets and literals, or -1:
    /// the subject is what comes before it.</summary>
    private static int TopLevelShouldCall(string expr)
    {
        var literal = LiteralMask(expr);
        var depth = 0;
        for (var i = 0; i < expr.Length; i++)
        {
            if (literal[i])
                continue;
            switch (expr[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case '.' when depth == 0 && i > 0 &&
                              string.CompareOrdinal(expr, i + 1, "Should", 0, "Should".Length) == 0 &&
                              i + 1 + "Should".Length < expr.Length && char.IsUpper(expr[i + 1 + "Should".Length]):
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// The subject in words: split on each <c>.</c> and each part on case, lower-cased, with <c>First()</c> and
    /// <c>Last()</c> read as "First value of …". A string or character literal is code too, and is kept exactly as
    /// written (<c>"Colour"</c> read <c>" colour"</c>). A <c>.ToString()</c> is how the code reaches the value it
    /// compares, not part of what is compared, and is left out for every library alike.
    /// </summary>
    private static string FormatSubject(string subject)
    {
        var segments = SplitOutsideLiterals(subject, '.');
        if (segments.Count > 1)
            segments.RemoveAll(segment => segment is "ToString()" or "ToString");

        // Detect First()/Last() and reorder: "collection.First().Prop" → "First value of collection prop"
        var firstLastIdx = -1;
        string? firstLastWord = null;
        for (var j = 0; j < segments.Count; j++)
        {
            if (segments[j] == "First()") { firstLastIdx = j; firstLastWord = "First"; break; }
            if (segments[j] == "Last()") { firstLastIdx = j; firstLastWord = "Last"; break; }
        }

        if (firstLastIdx >= 0)
        {
            var sb = new StringBuilder();
            sb.Append(firstLastWord);
            sb.Append(" value of");
            for (var i = 0; i < segments.Count; i++)
            {
                if (i == firstLastIdx) continue;
                sb.Append(' ');
                sb.Append(Words(segments[i]));
            }
            return sb.ToString();
        }

        {
            var sb = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Words(segments[i]));
            }

            // Capitalize first letter
            if (sb.Length > 0)
                sb[0] = char.ToUpperInvariant(sb[0]);

            return sb.ToString();
        }
    }

    /// <summary>One subject segment in words: its code split on case and lower-cased, its literals as written.</summary>
    private static string Words(string segment)
    {
        segment = segment.Trim('_');
        var literal = LiteralMask(segment);
        var sb = new StringBuilder();
        for (var i = 0; i < segment.Length;)
        {
            var end = i;
            while (end < segment.Length && literal[end] == literal[i])
                end++;
            var part = segment[i..end];
            sb.Append(literal[i] ? part : SplitPascalCase(part).ToLowerInvariant());
            i = end;
        }
        return sb.ToString();
    }

    /// <summary><paramref name="text"/> split at each <paramref name="separator"/> outside string and character
    /// literals.</summary>
    private static List<string> SplitOutsideLiterals(string text, char separator)
    {
        var literal = LiteralMask(text);
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == separator && !literal[i])
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// The expression with its null-forgiving operators removed, each <c>?.</c> read as <c>.</c> and the
    /// whitespace before a dot dropped, outside its string and character literals. Removing every <c>!</c>
    /// took one from a literal (<c>"Hi!"</c> read <c>"Hi"</c>), turned <c>!=</c> into <c>=</c> and a negation
    /// into its opposite; only a postfix <c>!</c>, after a name, a call or an index and not before <c>=</c>,
    /// is the null-forgiving operator.
    /// </summary>
    private static string CleanCode(string expr)
    {
        var literal = LiteralMask(expr);
        var sb = new StringBuilder(expr.Length);
        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];
            if (!literal[i])
            {
                var next = i + 1 < expr.Length ? expr[i + 1] : '\0';
                if (c == '!' && i > 0 && (IsIdentifierChar(expr[i - 1]) || expr[i - 1] is ')' or ']') && next != '=')
                    continue;
                if (c == '?' && next == '.')
                    continue;
                if (char.IsWhiteSpace(c))
                {
                    var j = i;
                    while (j < expr.Length && char.IsWhiteSpace(expr[j]))
                        j++;
                    if (j < expr.Length && expr[j] == '.' && !literal[j])
                    {
                        i = j - 1;
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Marks each character of <paramref name="text"/> that belongs to a string or character literal, its
    /// quotes included: regular, verbatim and interpolated strings, raw strings of three or more quotes, and
    /// characters. An unclosed literal runs to the end of the text.
    /// </summary>
    internal static bool[] LiteralMask(string text)
    {
        var mask = new bool[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('"' or '\''))
                continue;
            var end = EndOfLiteral(text, i);
            for (var j = i; j <= end; j++)
                mask[j] = true;
            i = end;
        }
        return mask;
    }

    private static int EndOfLiteral(string text, int start)
    {
        var quote = text[start];
        var verbatim = false;
        if (quote == '"')
        {
            var run = 0;
            while (start + run < text.Length && text[start + run] == '"')
                run++;
            if (run >= 3)
            {
                var close = text.IndexOf(new string('"', run), start + run, StringComparison.Ordinal);
                return close < 0 ? text.Length - 1 : close + run - 1;
            }
            if (run == 2)
                return start + 1;
            verbatim = start > 0 && (text[start - 1] == '@' || (start > 1 && text[start - 1] == '$' && text[start - 2] == '@'));
        }

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\' && !verbatim)
            {
                i++;
                continue;
            }
            if (text[i] != quote)
                continue;
            if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
                continue;
            }
            return i;
        }
        return text.Length - 1;
    }

    private static (string Method, string? Args) ParseMethodAndArgs(string assertionPart)
    {
        // Handle generic methods: BeOfType<string>()
        var parenDepth = 0;
        var angleBracketDepth = 0;
        var methodEnd = -1;
        var argsStart = -1;
        var argsEnd = -1;
        var literal = LiteralMask(assertionPart);

        for (var i = 0; i < assertionPart.Length; i++)
        {
            if (literal[i])
                continue;
            var c = assertionPart[i];
            switch (c)
            {
                case '<' when parenDepth == 0:
                    if (methodEnd < 0) methodEnd = i;
                    angleBracketDepth++;
                    break;
                case '>' when parenDepth == 0 && angleBracketDepth > 0:
                    angleBracketDepth--;
                    break;
                case '(':
                    if (angleBracketDepth == 0 && methodEnd < 0) methodEnd = i;
                    if (angleBracketDepth == 0 && parenDepth == 0) argsStart = i + 1;
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    if (parenDepth == 0 && angleBracketDepth == 0) argsEnd = i;
                    break;
            }
        }

        if (methodEnd < 0) return (assertionPart, null);

        var method = assertionPart[..methodEnd];

        // Extract generic type suffix if present
        var genericSuffix = "";
        if (assertionPart.Length > methodEnd && assertionPart[methodEnd] == '<')
        {
            var closeAngle = assertionPart.IndexOf('>', methodEnd);
            if (closeAngle >= 0)
                genericSuffix = " " + assertionPart[methodEnd..(closeAngle + 1)];
        }

        string? args = null;
        if (argsStart >= 0 && argsEnd > argsStart)
            args = assertionPart[argsStart..argsEnd];

        if (!string.IsNullOrEmpty(genericSuffix))
            args = genericSuffix.TrimStart() + (args is not null ? ", " + args : "");

        return (method, string.IsNullOrWhiteSpace(args) ? null : args);
    }

    private static string FormatArgs(string? args, Dictionary<string, string>? resolvedValues)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        if (resolvedValues is { Count: > 0 })
            args = SubstituteResolvedValues(args, resolvedValues);

        // Wrap lambda expressions in square brackets for readability
        if (args.Contains("=>"))
            return $"[ {args} ]";

        // Strip enum prefixes: TaskStatus.RanToCompletion → RanToCompletion, HttpStatusCode.OK → OK
        // But preserve complex expressions like DateTime.UtcNow, TimeSpan.FromSeconds(5)
        // Only strip if no substitutions were made (resolved values are already formatted)
        if (resolvedValues is not { Count: > 0 } || !args.Contains('\''))
            args = StripSimpleEnumPrefixes(args);

        return args;
    }

    /// <summary>
    /// Puts each value in quotes in place of exactly the code it was read for (#163). The arguments' access chains
    /// are found with the readers' own parser (<see cref="AccessChain"/>), and a chain is replaced only by the value
    /// kept under its whole text, or, for a value Kronikol read before a method call or an index
    /// (<see cref="ChainValues.Prefixes"/>), its read part is: <c>'Blue'.ToString()</c>. A shorter key never matches
    /// inside a longer chain, so <c>expected</c>'s value does not stand in for <c>expected.Plain</c>, nor a list for
    /// its element. An argument whose own value the weave passed (under the argument's text, from 4.16.0) is replaced
    /// whole by it, before any chain inside it is looked at. Code with no value, a literal among it, stays as written.
    /// </summary>
    private static string SubstituteResolvedValues(string args, Dictionary<string, string> resolvedValues)
    {
        var prefixes = (resolvedValues as ChainValues)?.Prefixes;
        Dictionary<string, string> values;
        if (resolvedValues is ChainValues)
            values = resolvedValues;
        else
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in resolvedValues)
                values.TryAdd(AccessChain.CanonicalKey(key), value);
        }

        // An argument's own value (the weave passes each argument's value under the argument's text) replaces the
        // whole argument; the chains of the arguments left are substituted as their values were read.
        var replacements = new List<(int Start, int End, string Value)>();
        foreach (var (start, end) in TopLevelArguments(args))
        {
            if (values.TryGetValue(AccessChain.CanonicalKey(args[start..end]), out var value))
                replacements.Add((start, end, value));
        }

        foreach (var chain in AccessChain.Find(args))
        {
            if (replacements.Any(r => chain.Start < r.End && chain.End > r.Start))
                continue;

            if (values.TryGetValue(chain.Key, out var value))
                replacements.Add((chain.Start, chain.End, value));
            else if (chain.HasUnreadRest && prefixes is not null && prefixes.Contains(chain.ReadKey) &&
                     values.TryGetValue(chain.ReadKey, out var part))
                replacements.Add((chain.Start, chain.ReadEnd, part));
        }

        var sb = new StringBuilder(args.Length);
        var pos = 0;
        foreach (var (start, end, value) in replacements.OrderBy(r => r.Start))
        {
            if (start < pos)
                continue;
            sb.Append(args, pos, start - pos).Append('\'').Append(value).Append('\'');
            pos = end;
        }
        sb.Append(args, pos, args.Length - pos);
        return sb.ToString();
    }

    /// <summary>Each top-level argument of a list, as its span without the whitespace around it: split at the commas
    /// outside brackets and literals.</summary>
    private static IEnumerable<(int Start, int End)> TopLevelArguments(string args)
    {
        var literal = LiteralMask(args);
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= args.Length; i++)
        {
            if (i < args.Length && literal[i])
                continue;
            if (i < args.Length)
            {
                switch (args[i])
                {
                    case '(' or '[' or '{':
                        depth++;
                        continue;
                    case ')' or ']' or '}':
                        depth--;
                        continue;
                    case not ',':
                        continue;
                }
                if (depth != 0)
                    continue;
            }

            var s = start;
            var e = i;
            while (s < e && char.IsWhiteSpace(args[s]))
                s++;
            while (e > s && char.IsWhiteSpace(args[e - 1]))
                e--;
            if (e > s)
                yield return (s, e);
            start = i + 1;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string[] SplitTopLevelCommas(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        var literal = LiteralMask(text);
        for (var i = 0; i < text.Length; i++)
        {
            if (literal[i])
                continue;
            switch (text[i])
            {
                case '(' or '<': depth++; break;
                case ')' or '>': depth--; break;
                case ',' when depth == 0:
                    parts.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }
        parts.Add(text[start..]);
        return parts.ToArray();
    }

    private static string StripSimpleEnumPrefixes(string args)
    {
        // Only strip "EnumType.Value" patterns for simple single-value args
        // (no commas, no parentheses). This avoids mangling complex expressions
        // like "DateTime.UtcNow, TimeSpan.FromSeconds(5)".
        if (args.Contains(',') || args.Contains('('))
            return args;

        return Regex.Replace(args, @"^([A-Z][a-zA-Z0-9]*)\.([A-Z][a-zA-Z0-9]*)$", "$2");
    }

    private static string SplitPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]) && !char.IsUpper(input[i - 1]))
                sb.Append(' ');
            else if (i > 1 && char.IsUpper(input[i]) && char.IsUpper(input[i - 1]) && i + 1 < input.Length && char.IsLower(input[i + 1]))
                sb.Append(' ');
            sb.Append(input[i]);
        }

        return sb.ToString();
    }
}
