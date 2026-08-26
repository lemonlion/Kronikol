using System.Globalization;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// One <c>--where</c> predicate: <c>[req:]PATH OP LITERAL</c>. Repeated clauses compose as AND — OR is
/// deliberately absent (run the command twice). Wildcard paths use any-semantics: the agent asking
/// "which calls returned a negative price" wants <em>any</em> element to satisfy; all-semantics has no
/// natural question behind it here.
/// </summary>
internal sealed class WhereClause
{
    public const string Grammar =
        "--where \"[req:]PATH OP LITERAL\" — ops: = != < > <= >= ~ !~ exists !exists (exists takes no literal); " +
        "literals: null, true, false, numbers, quoted or bare strings";

    public required string Raw { get; init; }

    /// <summary>The <c>req:</c> prefix — this clause reads the request body whatever the command's default is.</summary>
    public bool TargetsRequest { get; init; }

    public required IReadOnlyList<PathEngine.Segment> Segments { get; init; }
    public required string Operator { get; init; }
    public string? Literal { get; init; }

    public static bool TryParse(string expression, out WhereClause? clause, out string? error)
    {
        clause = null;
        error = null;
        var text = expression.Trim();

        var targetsRequest = false;
        if (text.StartsWith("req:", StringComparison.OrdinalIgnoreCase))
        {
            targetsRequest = true;
            text = text[4..].Trim();
        }

        string? op = null;
        var path = "";
        string? literal = null;

        foreach (var existsForm in new[] { "!exists", "exists" })
        {
            if (text.EndsWith(" " + existsForm, StringComparison.OrdinalIgnoreCase))
            {
                op = existsForm;
                path = text[..^(existsForm.Length + 1)].Trim();
                break;
            }
        }

        if (op is null)
        {
            // Operators are space-delimited, which makes the scan unambiguous: " = " never occurs
            // inside " >= " or " != ". The earliest operator wins, so a literal containing one is safe.
            var bestAt = int.MaxValue;
            foreach (var candidate in new[] { ">=", "<=", "!=", "!~", "=", "~", ">", "<" })
            {
                var at = text.IndexOf(" " + candidate + " ", StringComparison.Ordinal);
                if (at >= 0 && at < bestAt)
                {
                    bestAt = at;
                    op = candidate;
                }
            }

            if (op is null)
            {
                error = $"no operator in \"{expression}\"";
                return false;
            }

            path = text[..bestAt].Trim();
            literal = text[(bestAt + op.Length + 2)..].Trim();
            if (literal.Length >= 2 && (literal[0] == '"' && literal[^1] == '"' || literal[0] == '\'' && literal[^1] == '\''))
                literal = literal[1..^1];
            if (literal.Length == 0)
            {
                error = $"{op} needs a literal in \"{expression}\"";
                return false;
            }
        }

        if (path.Length == 0)
        {
            error = $"no path in \"{expression}\"";
            return false;
        }

        if (!PathEngine.TryParse(path, out var segments, out var pathError))
        {
            error = pathError;
            return false;
        }

        clause = new WhereClause
        {
            Raw = expression,
            TargetsRequest = targetsRequest,
            Segments = segments,
            Operator = op,
            Literal = literal
        };
        return true;
    }

    /// <summary>Whether the body satisfies this clause. Any selected value passing is a pass.</summary>
    public bool Evaluate(JsonElement root)
    {
        switch (Operator)
        {
            case "exists":
                return PathEngine.SelectAll(root, Segments).Any();
            case "!exists":
                return !PathEngine.SelectAll(root, Segments).Any();
        }

        foreach (var (_, value) in PathEngine.SelectAll(root, Segments))
            if (Compare(value))
                return true;
        return false;
    }

    private bool Compare(PathValue value)
    {
        // Strings compare on their unescaped text so `= APPROVED` matches "APPROVED"; everything else on
        // its raw JSON text so `= null` and `= true` mean the JSON values, not lookalike strings.
        var text = value.IsCount ? value.Row()
            : value.Element.ValueKind == JsonValueKind.String ? value.Element.GetString() ?? ""
            : value.Element.GetRawText();

        var numeric = value.TryNumber(out var number)
                      && double.TryParse(Literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var literalNumber);
        var comparison = numeric
            ? number.CompareTo(double.Parse(Literal!, NumberStyles.Float, CultureInfo.InvariantCulture))
            : string.Compare(text, Literal, StringComparison.OrdinalIgnoreCase);

        return Operator switch
        {
            "=" => comparison == 0,
            "!=" => comparison != 0,
            "<" => comparison < 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            ">=" => comparison >= 0,
            "~" => text.Contains(Literal!, StringComparison.OrdinalIgnoreCase),
            "!~" => !text.Contains(Literal!, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
