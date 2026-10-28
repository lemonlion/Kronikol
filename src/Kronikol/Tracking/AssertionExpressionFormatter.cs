using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Naming;

namespace Kronikol.Tracking;

/// <summary>
/// Parses assertion expressions (typically captured via <c>[CallerArgumentExpression]</c>)
/// into readable English sentences: FluentAssertions' <c>.Should().Method(args)</c>, Shouldly's
/// <c>subject.ShouldMethod(args)</c> and TUnit's <c>Assert.That(subject).Method(args)</c>, falling back to the code
/// for other assertion styles. Words are used only for the subject's member path and for each call of the assertion's
/// chain; all other code is kept exactly as written, on one line. A value is shown in quotes in place of exactly the
/// code it was read for, and a <c>nameof(…)</c> as the string it makes (#210).
/// </summary>
public static partial class AssertionExpressionFormatter
{
    [GeneratedRegex(@"^(?:async\s+)?\(\)\s*=>\s*")]
    private static partial Regex LambdaHead();

    public static string Format(string? expression)
    {
        return Format(expression, resolvedValues: null);
    }

    public static string Format(string? expression, Dictionary<string, string>? resolvedValues)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "";

        // A comment is not code the label reads (#210, F12).
        expression = WithoutComments(expression);

        // Strip a lambda's head: "() => ", or "async () => " from Track.ThatAsync, with any whitespace after the arrow.
        // A block body reads as its assertion statements, each on its own (#210, F16).
        var head = LambdaHead().Match(expression);
        var expr = head.Success ? expression[head.Length..] : expression;
        if (head.Success && expr.TrimEnd() is ['{', .., '}'] block && BlockAssertions(block) is { } statements)
            return string.Join("; ", statements.Select(statement => Format(statement, resolvedValues)));

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

        // Split on FluentAssertions' own .Should(): the first outside brackets and literals, so an assertion inside
        // the subject's brackets stays part of the subject, as Shouldly's split does (TopLevelShouldCall).
        var should = TopLevel(expr, ".Should().");
        if (should < 0)
            return FormatShouldly(expr, resolvedValues) ?? FormatTUnit(expr, resolvedValues) ??
                   NestedAssertion(expr, resolvedValues) ?? CollapseSpace(expr);

        var subject = expr[..should];
        var assertionPart = expr[(should + ".Should().".Length)..];

        // The subject's member path in words, the code in its brackets as written.
        var formattedSubject = FormatSubject(subject);

        // The assertion's first call with its own arguments; the chain after it, .And.'s assertions among it, is read
        // link by link.
        var (method, args, rest) = ParseMethodAndArgs(assertionPart);
        var formattedMethod = NameWords(method);
        var formattedArgs = FormatArgs(args, resolvedValues);

        return (string.IsNullOrEmpty(formattedArgs)
            ? $"{formattedSubject} should {formattedMethod}"
            : $"{formattedSubject} should {formattedMethod} {formattedArgs}") + FormatChain(rest, resolvedValues);
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

        var (method, args, rest) = ParseMethodAndArgs(call);
        var sentence = new StringBuilder(subject.Length == 0 ? "Should" : FormatSubject(subject) + " should");
        if (ShouldlyPredicate(method, args, resolvedValues) is { Length: > 0 } predicate)
            sentence.Append(' ').Append(predicate);
        return sentence.Append(FormatChain(rest, resolvedValues)).ToString();
    }

    /// <summary>What a Shouldly call says after its <c>Should</c>, wherever it stands in a chain: the words of the rest
    /// of its name, without an <c>Async</c> suffix, then its type argument without brackets and its arguments, less
    /// an action written <c>() =&gt; …</c> and a custom message passed by name.</summary>
    private static string ShouldlyPredicate(string method, string? args, Dictionary<string, string>? resolvedValues)
    {
        string? typeArgument = null;
        if (args is not null && args.StartsWith('<') &&
            TypeArgumentsEnd(args, LiteralMask(args), 0) is > 0 and var afterType)
        {
            typeArgument = args[1..(afterType - 1)];
            args = args[afterType..].TrimStart(',', ' ');
        }

        var words = NameWords(method);
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
        return string.Join(" ", new[] { words, typeArgument, formattedArgs }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>
    /// TUnit's <c>Assert.That(subject).IsEqualTo(5)</c> reads "Subject is equal to 5": the subject between the
    /// brackets of <c>Assert.That</c>, then the assertion's first call in words with its own arguments and each link
    /// of the chain after it (<see cref="FormatChain"/>), with resolved values put
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

        var (method, args, rest) = ParseMethodAndArgs(assertionPart);
        var words = NameWords(method);
        var formattedArgs = FormatArgs(args, resolvedValues);
        return (string.IsNullOrEmpty(formattedArgs)
            ? $"{FormatSubject(subject)} {words}"
            : $"{FormatSubject(subject)} {words} {formattedArgs}") + FormatChain(rest, resolvedValues);
    }

    /// <summary>The statements of a lambda's block body that are assertions, or null when none is: the body is then
    /// shown as written.</summary>
    private static List<string>? BlockAssertions(string block)
    {
        var body = block[1..^1];
        var literal = LiteralMask(body);
        var statements = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length && literal[i])
                continue;
            if (i < body.Length && body[i] is '(' or '[' or '{')
                depth++;
            else if (i < body.Length && body[i] is ')' or ']' or '}')
                depth--;
            else if (i == body.Length || (body[i] == ';' && depth == 0))
            {
                var statement = body[start..Math.Min(i, body.Length)].Trim();
                var bare = statement.StartsWith("await ", StringComparison.Ordinal) ? statement["await ".Length..] : statement;
                if (bare.Length > 0 && (TopLevel(bare, ".Should().") >= 0 || TopLevelShouldCall(bare) >= 0 ||
                                        bare.StartsWith("Should.", StringComparison.Ordinal) ||
                                        bare.StartsWith("Assert.That(", StringComparison.Ordinal)))
                    statements.Add(statement);
                start = i + 1;
            }
        }
        return statements.Count > 0 ? statements : null;
    }

    /// <summary>The index of <paramref name="token"/> outside literals, or -1.</summary>
    private static int IndexOutsideLiterals(string text, string token)
    {
        var literal = LiteralMask(text);
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + 1, StringComparison.Ordinal))
        {
            if (!literal[i])
                return i;
        }
        return -1;
    }

    /// <summary>An assertion inside another call's arguments or brackets, read as that assertion (#210, F17; #212's
    /// S23: <c>Console.WriteLine((await Should.ThrowAsync&lt;T&gt;(…)).Message)</c>): the outermost bracket that holds
    /// one alone. One in a lambda's body runs elsewhere (for each element, later), and the code stays as written:
    /// null.</summary>
    private static string? NestedAssertion(string expr, Dictionary<string, string>? resolvedValues)
    {
        var literal = LiteralMask(expr);
        for (var open = 0; open < expr.Length; open++)
        {
            if (literal[open] || expr[open] != '(')
                continue;
            var depth = 0;
            var close = -1;
            for (var j = open; j < expr.Length && close < 0; j++)
            {
                if (literal[j])
                    continue;
                if (expr[j] is '(' or '[' or '{')
                    depth++;
                else if (expr[j] is ')' or ']' or '}' && --depth == 0)
                    close = j;
            }
            if (close < 0)
                return null;
            var inner = expr[(open + 1)..close].Trim();
            var bare = inner.StartsWith("await ", StringComparison.Ordinal) ? inner["await ".Length..] : inner;
            if (TopLevelArrow(bare) >= 0)
            {
                open = close;   // a lambda: what it asserts runs elsewhere
                continue;
            }
            if (bare.StartsWith("Should.", StringComparison.Ordinal) || bare.StartsWith("Assert.That(", StringComparison.Ordinal) ||
                TopLevel(bare, ".Should().") >= 0 || TopLevelShouldCall(bare) >= 0)
                return Format(inner, resolvedValues);
        }
        return null;
    }

    /// <summary>The index of a lambda's <c>=&gt;</c> outside brackets and literals, or -1.</summary>
    private static int TopLevelArrow(string text)
    {
        var literal = LiteralMask(text);
        var depth = 0;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (literal[i])
                continue;
            if (text[i] is '(' or '[' or '{')
                depth++;
            else if (text[i] is ')' or ']' or '}')
                depth--;
            else if (depth == 0 && text[i] == '=' && text[i + 1] == '>')
                return i;
        }
        return -1;
    }

    /// <summary>The index of the first <paramref name="token"/>, which starts with a dot, outside brackets and
    /// literals, or -1.</summary>
    private static int TopLevel(string expr, string token)
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
                case '.' when depth == 0 && string.CompareOrdinal(expr, i, token, 0, token.Length) == 0:
                    return i;
            }
        }
        return -1;
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
    /// The subject in words: its member path split at each <c>.</c> outside literals and brackets, each link's name
    /// split on case and lower-cased and the rest of the link (type arguments, a call's or an index's brackets) kept
    /// exactly as written, with <c>First()</c> and <c>Last()</c> read as "First value of …". It was cut at every dot
    /// and each piece split on case, so a call's arguments read as words (#210). A first link that is not a name (a
    /// literal, a bracketed expression, <c>new T(…)</c>) is kept as written, and a subject that is not a member path
    /// (an operator outside brackets) is kept as written whole, in its own case. A <c>.ToString()</c> is how the code
    /// reaches the value it compares, not part of what is compared, and is left out for every library alike.
    /// </summary>
    private static string FormatSubject(string subject)
    {
        // A subject that is an action (TUnit's Assert.That(() => …)) reads as what the action does (#212's S18a).
        if (LambdaHead().Match(subject.TrimStart()) is { Success: true } lambda)
        {
            subject = subject.TrimStart()[lambda.Length..];
            if (subject.StartsWith("await ", StringComparison.Ordinal))
                subject = subject["await ".Length..];
        }

        // A member path in words, the code in its brackets as written; any other subject is code, kept as written.
        if (SubjectLinks(subject) is not { } segments)
            return CollapseSpace(subject.Trim());
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

    /// <summary>One subject link in words: its name split on case and lower-cased, then its type arguments and
    /// brackets exactly as written. A link that does not start with a name (a literal, a bracketed
    /// expression, <c>new T(…)</c>) is code and is kept as written.</summary>
    private static string Words(string segment)
    {
        var nameEnd = 0;
        while (nameEnd < segment.Length && IsIdentifierChar(segment[nameEnd]))
            nameEnd++;
        if (nameEnd == 0 || char.IsDigit(segment[0]) || segment.StartsWith("new ", StringComparison.Ordinal))
            return CollapseSpace(segment);
        var name = segment[..nameEnd].Trim('_');
        return NameWords(name) + CollapseSpace(segment[nameEnd..]);
    }

    /// <summary>
    /// The subject's links: split at each <c>.</c> outside literals and outside brackets, so a call's
    /// arguments, an index and a type argument list stay inside the link they belong to. Null when the subject is not
    /// one primary followed by member links: an operator, a lambda or anything else outside brackets after the first
    /// link means the subject is code, kept as written.
    /// </summary>
    private static List<string>? SubjectLinks(string subject)
    {
        var literal = LiteralMask(subject);
        var links = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < subject.Length; i++)
        {
            if (literal[i])
                continue;
            switch (subject[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case '<' when depth == 0 && i > 0 && IsIdentifierChar(subject[i - 1]) && TypeArgumentsEnd(subject, literal, i) is > 0 and var close:
                    i = close - 1;
                    break;
                case '.' when depth == 0 && !(i + 1 < subject.Length && subject[i + 1] == '.'):
                    links.Add(subject[start..i]);
                    start = i + 1;
                    break;
            }
        }
        links.Add(subject[start..]);

        for (var l = 0; l < links.Count; l++)
        {
            if (l == 0 && !StartsWithName(links[0]))
                continue;
            if (!IsNameLink(links[l], l == 0))
                return null;
        }
        return links;
    }

    private static bool StartsWithName(string link) =>
        link.Length > 0 && (char.IsLetter(link[0]) || link[0] == '_') && !link.StartsWith("new ", StringComparison.Ordinal);

    /// <summary>Whether a link is a name with only type arguments and brackets after it (outside literals).</summary>
    private static bool IsNameLink(string link, bool first)
    {
        var literal = LiteralMask(link);
        var i = 0;
        while (i < link.Length && IsIdentifierChar(link[i]))
            i++;
        if (i == 0)
            return first;
        if (i < link.Length && link[i] == '<' && TypeArgumentsEnd(link, literal, i) is > 0 and var afterTypes)
            i = afterTypes;
        var depth = 0;
        for (; i < link.Length; i++)
        {
            if (literal[i])
                continue;
            var c = link[i];
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
                depth--;
            else if (depth == 0)
                return false;
        }
        return depth == 0;
    }

    /// <summary>The index after the type argument list that opens at <paramref name="open"/>, or -1 when it is a
    /// comparison: only names, dots, commas, spaces, <c>?</c>, <c>[]</c> and nested lists, then <c>(</c> or the end.</summary>
    private static int TypeArgumentsEnd(string text, bool[] literal, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (literal[i])
                return -1;
            var c = text[i];
            if (c == '<')
                depth++;
            else if (c == '>')
            {
                if (--depth == 0)
                    return i + 1;
            }
            else if (!(IsIdentifierChar(c) || char.IsWhiteSpace(c) || c is '.' or ',' or '?' or '[' or ']' or '(' or ')'))
                return -1;
        }
        return -1;
    }

    /// <summary>Each run of whitespace outside literals read as one space, so code kept as written stays on one line;
    /// none after <c>(</c> or <c>[</c> or before <c>)</c> or <c>]</c>, where a line break in a call's arguments
    /// left one.</summary>
    private static string CollapseSpace(string code)
    {
        var literal = LiteralMask(code);
        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            if (!literal[i] && char.IsWhiteSpace(code[i]))
            {
                var j = i;
                while (j < code.Length && char.IsWhiteSpace(code[j]) && !literal[j])
                    j++;
                var before = sb.Length == 0 ? '\0' : sb[^1];
                var after = j < code.Length ? code[j] : '\0';
                if (sb.Length > 0 && before is not ('(' or '[') && after is not (')' or ']' or '\0'))
                    sb.Append(' ');
                i = j - 1;
                continue;
            }
            sb.Append(code[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Each <c>nameof(…)</c> outside literals as the string the compiler made of it, in quotes as a value
    /// (#210): the operand's last name, without type arguments or <c>@</c>, as C# defines it.
    /// <c>nameof(CurrencyCode.GBP)</c> reads <c>'GBP'</c>. An operand that does not end in a name is kept as written.
    /// </summary>
    internal static string ReadNameofs(string code)
    {
        var literal = LiteralMask(code);
        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            if (!literal[i] && string.CompareOrdinal(code, i, "nameof", 0, 6) == 0 &&
                (i == 0 || !(IsIdentifierChar(code[i - 1]) || code[i - 1] is '.' or '@')))
            {
                var open = i + 6;
                while (open < code.Length && code[open] == ' ')
                    open++;
                if (open < code.Length && code[open] == '(' && Close(code, literal, open) is > 0 and var close &&
                    SimpleName(code[(open + 1)..close]) is { } name)
                {
                    sb.Append('\'').Append(name).Append('\'');
                    i = close;
                    continue;
                }
            }
            sb.Append(code[i]);
        }
        return sb.ToString();
    }

    private static int Close(string text, bool[] literal, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (literal[i])
                continue;
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i;
        }
        return -1;
    }

    private static string? SimpleName(string operand)
    {
        var text = operand.Trim();
        string previous;
        do
        {
            previous = text;
            text = Regex.Replace(text, "<[^<>]*>", "");
        } while (text != previous);
        var colons = text.LastIndexOf("::", StringComparison.Ordinal);
        var cut = Math.Max(text.LastIndexOf('.'), colons >= 0 ? colons + 1 : -1);
        var name = (cut >= 0 ? text[(cut + 1)..] : text).Trim().TrimStart('@');
        return name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(IsIdentifierChar) ? name : null;
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
                if (c == '?' && (next == '.' || (next == '[' && i > 0 && (IsIdentifierChar(expr[i - 1]) || expr[i - 1] is ')' or ']'))))
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
    /// The expression with its comments outside literals taken out, each read as one space, as C# reads it: a
    /// comment reached the label as code (#210, F12). A <c>//</c> comment ends at its line break, and one with no line
    /// break after it is kept: in text whose lines the weave joined, it would take the rest of the statement with it.
    /// </summary>
    private static string WithoutComments(string expr)
    {
        if (!expr.Contains("/*", StringComparison.Ordinal) && !expr.Contains("//", StringComparison.Ordinal))
            return expr;

        var sb = new StringBuilder(expr.Length);
        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];
            if (c is '"' or '\'')
            {
                var end = EndOfLiteral(expr, i);
                sb.Append(expr, i, end - i + 1);
                i = end;
                continue;
            }
            var close = c != '/' || i + 1 >= expr.Length ? -1
                : expr[i + 1] == '*' ? expr.IndexOf("*/", i + 2, StringComparison.Ordinal) + 1
                : expr[i + 1] == '/' ? expr.IndexOf('\n', i + 2) - 1
                : -1;
            if (close > i)
            {
                sb.Append(' ');
                i = close;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Marks each character of <paramref name="text"/> that belongs to a string or character literal, its
    /// quotes included: regular, verbatim and interpolated strings (their holes too), raw strings of three or more
    /// quotes, and characters. An unclosed literal runs to the end of the text.
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
        var interpolated = false;
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
            interpolated = start > 0 && (text[start - 1] == '$' || (start > 1 && text[start - 1] == '@' && text[start - 2] == '$'));
        }

        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\' && !verbatim)
            {
                i++;
                continue;
            }
            if (interpolated && text[i] == '{')
            {
                // "{{" is a brace; a hole is code, whose literals and brackets are its own (#210, F13: a quote in a
                // hole ended the string).
                i = i + 1 < text.Length && text[i + 1] == '{' ? i + 1 : EndOfHole(text, i);
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

    /// <summary>The index of the <c>}</c> that closes the interpolated string's hole opening at
    /// <paramref name="open"/>, or the last index when it is not closed.</summary>
    private static int EndOfHole(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"' or '\'':
                    i = EndOfLiteral(text, i);
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when --depth == 0:
                    return i;
            }
        }
        return text.Length - 1;
    }

    /// <summary>The assertion's first call: its name, its type arguments and its own
    /// arguments (type arguments first, as <c>&lt;T&gt;, args</c>), and the code after its closing bracket, which
    /// <see cref="FormatChain"/> reads. The arguments were the last call's in the chain, so
    /// <c>IsEqualTo(10.0).Within(0.5)</c> read "is equal to 0.5".</summary>
    private static (string Method, string? Args, string Tail) ParseMethodAndArgs(string assertionPart)
    {
        var literal = LiteralMask(assertionPart);
        var i = 0;
        while (i < assertionPart.Length && IsIdentifierChar(assertionPart[i]))
            i++;
        if (i == 0)
            return (assertionPart, null, "");
        var method = assertionPart[..i];

        var typeArguments = "";
        if (i < assertionPart.Length && assertionPart[i] == '<' &&
            TypeArgumentsEnd(assertionPart, literal, i) is > 0 and var afterTypes)
        {
            typeArguments = assertionPart[i..afterTypes];
            i = afterTypes;
        }

        string? args = null;
        if (i < assertionPart.Length && assertionPart[i] == '(')
        {
            var depth = 0;
            var close = -1;
            for (var j = i; j < assertionPart.Length && close < 0; j++)
            {
                if (literal[j])
                    continue;
                if (assertionPart[j] is '(' or '[' or '{')
                    depth++;
                else if (assertionPart[j] is ')' or ']' or '}' && --depth == 0)
                    close = j;
            }
            if (close < 0)
                return (method, null, "");
            args = assertionPart[(i + 1)..close];
            i = close + 1;
        }

        if (typeArguments.Length > 0)
            args = typeArguments + (string.IsNullOrWhiteSpace(args) ? "" : ", " + args);
        return (method, string.IsNullOrWhiteSpace(args) ? null : args, assertionPart[i..]);
    }

    private static readonly HashSet<string> HandOn = ["Which", "Subject", "And", "Or", "Which()"];

    /// <summary>The links after an assertion's first call, in order (#210, F10): a member reads as words, a
    /// call as its words and its arguments, <c>Should()</c> as "should", and <c>.And.</c> and <c>.Or.</c> as "and" and
    /// "or" before the next assertion (Q8). A member that moves on from an assertion to a value of it (<c>.Which.Code</c>,
    /// <c>.WhoseValue</c>, <c>.Message</c>) follows a comma; a member further along that value's path
    /// (<c>.Errors.Single().Names</c>) and every call (<c>.Within(0.5)</c>, <c>.WithMessage("…")</c>,
    /// <c>.Single()</c>) follow a space. A last link that only hands the value on (<c>Which</c>, <c>Subject</c>,
    /// <c>And</c>) is not read. Empty when nothing follows the call.</summary>
    private static string FormatChain(string rest, Dictionary<string, string>? resolvedValues)
    {
        rest = rest.Trim();
        if (rest.Length < 2 || rest[0] != '.')
            return "";
        if (SubjectLinks(rest[1..]) is not { } links)
            return " " + CollapseSpace(rest);
        while (links.Count > 0 && HandOn.Contains(links[^1]))
            links.RemoveAt(links.Count - 1);
        if (links.Count == 0)
            return "";

        var sb = new StringBuilder();
        // Whether the chain stands on an assertion (its first call, or a call that asserts, or what .And. starts) or on
        // a value one of its members reached.
        var onAssertion = true;
        for (var l = 0; l < links.Count; l++)
        {
            if (links[l] is "And" or "Or")
            {
                // FluentAssertions' and TUnit's next assertion on the same subject: only the first was read, so a
                // failing second half was labelled with the half that passed (#210, Q8).
                sb.Append(' ').Append(NameWords(links[l]));
                onAssertion = true;
                continue;
            }
            var (name, args, _) = ParseMethodAndArgs(links[l]);
            var isCall = links[l].Length > name.Length && links[l][name.Length] is '(' or '<';
            sb.Append(!isCall && onAssertion ? ", " : " ");
            if (isCall && name.Length > "Should".Length && name.StartsWith("Should", StringComparison.Ordinal) &&
                char.IsUpper(name["Should".Length]))
            {
                // A later Shouldly call reads as its first one does: "inner should be of type Order".
                sb.Append("should");
                if (ShouldlyPredicate(name["Should".Length..], args, resolvedValues) is { Length: > 0 } predicate)
                    sb.Append(' ').Append(predicate);
                onAssertion = true;
                continue;
            }
            sb.Append(NameWords(name.Trim('_')));
            if (isCall && FormatArgs(args, resolvedValues) is { Length: > 0 } formatted)
                sb.Append(' ').Append(formatted);
            else if (!isCall && links[l].Length > name.Length)
                sb.Append(CollapseSpace(links[l][name.Length..]));
            if (!isCall)
                onAssertion = false;
            else if (name.StartsWith("Should", StringComparison.Ordinal))
                onAssertion = true;
        }
        return sb.ToString();
    }

    private static string FormatArgs(string? args, Dictionary<string, string>? resolvedValues)
    {
        if (string.IsNullOrWhiteSpace(args))
            return "";

        if (resolvedValues is { Count: > 0 })
            args = SubstituteResolvedValues(args, resolvedValues);

        // A nameof is a value the compiler made, read as one (#210); code kept as written reads on one line.
        args = CollapseSpace(ReadNameofs(args).Trim());

        // Wrap lambda expressions in square brackets for readability
        if (IndexOutsideLiterals(args, "=>") >= 0)
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
    /// whole by it, before any chain inside it is looked at. Code with no value, a literal among it, stays as written
    /// here; <see cref="FormatArgs"/> then reads a <c>nameof(…)</c> as the string it makes (#210).
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
                case '(' or '[' or '{': depth++; break;
                case ')' or ']' or '}': depth--; break;
                case '<' when i > 0 && IsIdentifierChar(text[i - 1]) && TypeArgumentsEnd(text, literal, i) is > 0 and var afterTypes:
                    i = afterTypes - 1;
                    break;
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

    /// <summary>
    /// A name in words, by the rule a scenario's title takes (plans/TITLES_AS_WRITTEN_PLAN.md 4.1, #210's question 7): split
    /// on case, a word written in capitals kept and every other word lower-cased. Every name a label reads as words comes
    /// here.
    /// </summary>
    private static string NameWords(string name) => SentenceCase.Words(name);
}
