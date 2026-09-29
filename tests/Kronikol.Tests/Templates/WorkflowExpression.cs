using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The part of GitHub's expression language the template actions use, evaluated the way the runner evaluates it:
/// the contexts <c>inputs</c>, <c>github</c>, <c>runner</c>, <c>env</c>, <c>steps</c>, <c>strategy</c>, <c>matrix</c> and
/// <c>job</c>; string, number and boolean literals and <c>null</c>; <c>==</c>, <c>!=</c>, <c>!</c>, <c>&amp;&amp;</c>,
/// <c>||</c> and parentheses; and the status functions <c>always()</c>, <c>success()</c>, <c>failure()</c> and
/// <c>cancelled()</c>. Anything else is refused, not approximated: an action that needs more has logic in its YAML that
/// belongs in a script, where the facts can run it.
///
/// <para>The semantics are the documented ones. Strings compare ignoring case, and values of different types compare
/// as numbers (null 0, true 1, false 0, a string parsed or NaN, the empty string 0). <c>&amp;&amp;</c> and <c>||</c>
/// return one of their operands. <c>null</c>, <c>false</c>, <c>0</c>, NaN and the empty string are falsy. A step's
/// <c>if:</c> with no status function is read as <c>success() &amp;&amp; (it)</c>.</para>
/// </summary>
internal static class WorkflowExpression
{
    public static readonly IReadOnlyList<string> Contexts = ["inputs", "github", "runner", "env", "steps", "strategy", "matrix", "job"];
    private static readonly string[] StatusFunctions = ["always", "success", "failure", "cancelled"];
    private static readonly Regex Template = new(@"\$\{\{(.*?)\}\}", RegexOptions.Singleline);

    /// <summary>A context value by its dotted path (<c>inputs.branch</c>, <c>steps.stage.outputs.files</c>), or null.</summary>
    public delegate object? Lookup(string path);

    /// <summary>The expressions a text holds, each without its <c>${{ }}</c>.</summary>
    public static IReadOnlyList<string> ExpressionsIn(string text) =>
        Template.Matches(text).Select(match => match.Groups[1].Value.Trim()).ToList();

    /// <summary>Parses <paramref name="expression"/>, throwing <see cref="NotSupportedException"/> on anything outside the subset.</summary>
    public static void Validate(string expression) => Parse(expression);

    /// <summary>A text with every <c>${{ }}</c> replaced by its value's text, as an input default, a <c>with:</c> or an <c>env:</c> value is.</summary>
    public static string Interpolate(string text, Lookup lookup, string status = "success") =>
        Template.Replace(text, match => ToText(Evaluate(Parse(match.Groups[1].Value), lookup, status)));

    /// <summary>A step's <c>if:</c>, written with or without <c>${{ }}</c>; empty means <c>success()</c>.</summary>
    public static bool Condition(string? text, Lookup lookup, string status)
    {
        var expression = (text ?? "").Trim();
        if (expression.StartsWith("${{", StringComparison.Ordinal) && expression.EndsWith("}}", StringComparison.Ordinal)
            && Template.Matches(expression).Count == 1)
            expression = expression[3..^2].Trim();
        if (expression.Length == 0)
            expression = "success()";

        var node = Parse(expression);
        if (!CallsStatusFunction(node))
            node = new And(new Call("success"), node);
        return Truthy(Evaluate(node, lookup, status));
    }

    // ─── The tree ───────────────────────────────────────────────

    private abstract record Node;
    private sealed record Literal(object? Value) : Node;
    private sealed record Context(string Path) : Node;
    private sealed record Call(string Name) : Node;
    private sealed record Not(Node Operand) : Node;
    private sealed record And(Node Left, Node Right) : Node;
    private sealed record Or(Node Left, Node Right) : Node;
    private sealed record Equal(Node Left, Node Right, bool Negated) : Node;

    private static bool CallsStatusFunction(Node node) => node switch
    {
        Call => true,
        Not not => CallsStatusFunction(not.Operand),
        And and => CallsStatusFunction(and.Left) || CallsStatusFunction(and.Right),
        Or or => CallsStatusFunction(or.Left) || CallsStatusFunction(or.Right),
        Equal equal => CallsStatusFunction(equal.Left) || CallsStatusFunction(equal.Right),
        _ => false
    };

    private static object? Evaluate(Node node, Lookup lookup, string status) => node switch
    {
        Literal literal => literal.Value,
        Context context => lookup(context.Path),
        Call { Name: "always" } => true,
        Call call => string.Equals(call.Name, status, StringComparison.Ordinal),
        Not not => !Truthy(Evaluate(not.Operand, lookup, status)),
        And and => Evaluate(and.Left, lookup, status) is var left && !Truthy(left) ? left : Evaluate(and.Right, lookup, status),
        Or or => Evaluate(or.Left, lookup, status) is var left && Truthy(left) ? left : Evaluate(or.Right, lookup, status),
        Equal equal => AreEqual(Evaluate(equal.Left, lookup, status), Evaluate(equal.Right, lookup, status)) != equal.Negated,
        _ => throw new InvalidOperationException(node.ToString())
    };

    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };

    public static string ToText(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static bool AreEqual(object? left, object? right)
    {
        if (left is string a && right is string b)
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (left is null && right is null)
            return true;
        if (left is bool x && right is bool y)
            return x == y;
        var l = ToNumber(left);
        var r = ToNumber(right);
        return !double.IsNaN(l) && !double.IsNaN(r) && l == r;
    }

    private static double ToNumber(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        double d => d,
        string s when s.Trim().Length == 0 => 0,
        string s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN,
        _ => double.NaN
    };

    // ─── The parser ─────────────────────────────────────────────

    private static Node Parse(string expression)
    {
        var tokens = Tokenize(expression);
        var position = 0;
        var node = ParseOr(tokens, ref position, expression);
        if (position != tokens.Count)
            throw Refused(expression, $"unexpected '{tokens[position]}'");
        return node;
    }

    private static Node ParseOr(List<string> tokens, ref int position, string expression)
    {
        var node = ParseAnd(tokens, ref position, expression);
        while (position < tokens.Count && tokens[position] == "||")
        {
            position++;
            node = new Or(node, ParseAnd(tokens, ref position, expression));
        }
        return node;
    }

    private static Node ParseAnd(List<string> tokens, ref int position, string expression)
    {
        var node = ParseEquality(tokens, ref position, expression);
        while (position < tokens.Count && tokens[position] == "&&")
        {
            position++;
            node = new And(node, ParseEquality(tokens, ref position, expression));
        }
        return node;
    }

    /// <summary><c>!</c> binds tighter than <c>==</c>, as the documented precedence has it: <c>!a == b</c> is <c>(!a) == b</c>.</summary>
    private static Node ParseEquality(List<string> tokens, ref int position, string expression)
    {
        var node = ParseNot(tokens, ref position, expression);
        if (position < tokens.Count && tokens[position] is "==" or "!=")
        {
            var negated = tokens[position] == "!=";
            position++;
            node = new Equal(node, ParseNot(tokens, ref position, expression), negated);
        }
        return node;
    }

    private static Node ParseNot(List<string> tokens, ref int position, string expression)
    {
        if (position < tokens.Count && tokens[position] == "!")
        {
            position++;
            return new Not(ParseNot(tokens, ref position, expression));
        }
        return ParsePrimary(tokens, ref position, expression);
    }

    private static Node ParsePrimary(List<string> tokens, ref int position, string expression)
    {
        if (position >= tokens.Count)
            throw Refused(expression, "it ends too soon");

        var token = tokens[position++];
        if (token == "(")
        {
            var inner = ParseOr(tokens, ref position, expression);
            if (position >= tokens.Count || tokens[position] != ")")
                throw Refused(expression, "a '(' is not closed");
            position++;
            return inner;
        }

        if (token.StartsWith('\''))
            return new Literal(token[1..^1].Replace("''", "'", StringComparison.Ordinal));
        if (char.IsDigit(token[0]) || token[0] == '-')
            return new Literal(double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
        switch (token)
        {
            case "true": return new Literal(true);
            case "false": return new Literal(false);
            case "null": return new Literal(null);
        }

        if (position < tokens.Count && tokens[position] == "(")
        {
            if (!StatusFunctions.Contains(token, StringComparer.Ordinal))
                throw Refused(expression, $"the function {token}() is not one the actions may use");
            if (position + 1 >= tokens.Count || tokens[position + 1] != ")")
                throw Refused(expression, $"{token}() takes no arguments");
            position += 2;
            return new Call(token);
        }

        var root = token.Split('.')[0];
        if (!Contexts.Contains(root, StringComparer.Ordinal))
            throw Refused(expression, $"the context '{root}' is not one the actions may use");
        if (!token.Contains('.'))
            throw Refused(expression, $"the whole '{root}' context is not a value");
        return new Context(token);
    }

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < expression.Length)
        {
            var c = expression[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '(' or ')')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (Two("==") || Two("!=") || Two("&&") || Two("||"))
            {
                tokens.Add(expression.Substring(i, 2));
                i += 2;
            }
            else if (c == '!')
            {
                tokens.Add("!");
                i++;
            }
            else if (c == '\'')
            {
                var text = new StringBuilder("'");
                i++;
                while (true)
                {
                    if (i >= expression.Length)
                        throw Refused(expression, "a string is not closed");
                    if (expression[i] == '\'' && i + 1 < expression.Length && expression[i + 1] == '\'')
                    {
                        text.Append("''");
                        i += 2;
                    }
                    else if (expression[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        text.Append(expression[i++]);
                    }
                }
                tokens.Add(text.Append('\'').ToString());
            }
            else if (char.IsDigit(c) || (c == '-' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
            {
                var start = i++;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                    i++;
                tokens.Add(expression[start..i]);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] is '_' or '-' or '.'))
                {
                    if (expression[i] == '.' && (i + 1 >= expression.Length || !(char.IsLetter(expression[i + 1]) || expression[i + 1] == '_')))
                        throw Refused(expression, "a property filter or an index is not one the actions may use");
                    i++;
                }
                tokens.Add(expression[start..i]);
            }
            else
            {
                throw Refused(expression, $"'{c}' is not an operator the actions may use");
            }

            bool Two(string op) => string.CompareOrdinal(expression, i, op, 0, 2) == 0;
        }

        return tokens;
    }

    private static NotSupportedException Refused(string expression, string why) =>
        new($"${{{{ {expression.Trim()} }}}}: {why}");
}
