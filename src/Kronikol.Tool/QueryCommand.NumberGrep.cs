using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// <c>grep --number</c> — numeric-aware search. The number the user quotes is the *formatted* one; the
/// payload holds the raw one, and <c>grep "4,173.00"</c> missing <c>4173</c> is a real failure of the
/// tool's flagship use case. Every candidate token is normalized under both separator conventions
/// (comma-as-thousands and comma-as-decimal), because <c>4.173,00</c> is European for <c>4173.00</c> and
/// a false negative here costs more than two parses per token.
/// </summary>
internal static partial class QueryCommand
{
    private static readonly Regex NumericToken = new(@"[-+]?\d[\d,._]*(\.\d+)?", RegexOptions.Compiled);

    private static int NumberGrep(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error)
    {
        var needle = options.Positional[0].TrimStart('$', '€', '£').Trim();
        var wanted = Interpretations(needle).ToArray();
        if (wanted.Length == 0)
        {
            error.WriteLine($"--number needs a numeric needle; \"{options.Positional[0]}\" is not one — drop the flag for text search.");
            return 2;
        }

        double? absolute = null;
        double? percent = null;
        if (options.Tolerance is { } tolerance)
        {
            if (tolerance.EndsWith('%') && double.TryParse(tolerance[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                percent = pct;
            else if (double.TryParse(tolerance, NumberStyles.Float, CultureInfo.InvariantCulture, out var abs))
                absolute = abs;
            else
            {
                error.WriteLine($"--tolerance takes a number (0.5) or a percentage (1%), not \"{tolerance}\".");
                return 2;
            }
        }

        bool Matches(double value) => wanted.Any(n =>
            absolute is { } a ? Math.Abs(value - n) <= a
            : percent is { } p ? Math.Abs(value - n) <= Math.Abs(n) * p / 100
            : Math.Abs(value - n) <= Math.Max(Math.Abs(value), Math.Abs(n)) * 1e-9);

        // Does any numeric token in this text match, and which token was it?
        string? TokenIn(string text)
        {
            foreach (Match token in NumericToken.Matches(text))
                if (Interpretations(token.Value).Any(Matches))
                    return token.Value;
            return null;
        }

        string Approx(string raw) => raw == needle ? "" : $" (≈ {needle})";

        var targets = (options.In ?? "bodies,uris,steps,assertions").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<string>();

        foreach (var scenario in index.Scenarios)
        {
            if (targets.Contains("steps") || targets.Contains("assertions"))
            {
                foreach (var (path, _, step) in scenario.AllSteps())
                {
                    if (step.IsAssertion && !targets.Contains("assertions"))
                        continue;
                    if (!step.IsAssertion && !targets.Contains("steps"))
                        continue;

                    var token = TokenIn(step.Text) ?? (step.FailureMessage is { } message ? TokenIn(message) : null);
                    if (token is not null)
                        hits.Add($"{scenario.Address}/{path,-6} {(step.IsAssertion ? "assertion" : "step")}  "
                                 + QueryWriter.OneLine(step.FailureMessage ?? step.Text, 100) + Approx(token));
                }
            }

            if (targets.Contains("uris"))
                foreach (var interaction in scenario.Interactions)
                    if (TokenIn(interaction.Uri) is { } token)
                        hits.Add($"{interaction.Address(scenario),-9} uri        {QueryWriter.OneLine(interaction.Uri, 100)}{Approx(token)}");

            if (targets.Contains("headers"))
                foreach (var interaction in scenario.Interactions.Where(i => i.HeaderCount > 0))
                    foreach (var (key, value) in PayloadReader.Headers(index, interaction))
                        if (value is not null && TokenIn(value) is { } token)
                            hits.Add($"{interaction.Address(scenario),-9} header     {key}: {QueryWriter.OneLine(value, 80)}{Approx(token)}");
        }

        if (targets.Contains("bodies"))
        {
            foreach (var body in index.Bodies.Values)
            {
                var content = PayloadReader.Read(index, body.First);
                if (content is null)
                    continue;

                var where = body.Occurrences.Count == 1
                    ? body.Occurrences[0]
                    : $"{body.Occurrences[0]} ×{body.Occurrences.Count}";

                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(content);
                }
                catch (JsonException)
                {
                    // Non-JSON body: token-scan the text.
                    if (TokenIn(content) is { } textToken)
                        hits.Add($"{where,-16} body       {Excerpt(content, textToken)}{Approx(textToken)}");
                    continue;
                }

                using (document)
                {
                    // A numeric match is always a value match, so --number always emits paths.
                    var found = 0;
                    Walk(document.RootElement, "$");

                    void Walk(JsonElement element, string path)
                    {
                        if (found >= 4)
                            return;
                        switch (element.ValueKind)
                        {
                            case JsonValueKind.Object:
                                foreach (var property in element.EnumerateObject())
                                    Walk(property.Value, PathEngine.Append(path, property.Name));
                                break;
                            case JsonValueKind.Array:
                                var i = 0;
                                foreach (var item in element.EnumerateArray())
                                    Walk(item, $"{path}[{i++}]");
                                break;
                            case JsonValueKind.Number:
                                if (element.TryGetDouble(out var value) && Matches(value))
                                {
                                    hits.Add($"{where,-16} body       {path} = {element.GetRawText()}{Approx(element.GetRawText())}");
                                    found++;
                                }
                                break;
                            case JsonValueKind.String:
                                if (TokenIn(element.GetString() ?? "") is { } token)
                                {
                                    hits.Add($"{where,-16} body       {path} = \"{QueryWriter.OneLine(element.GetString(), 40)}\"{Approx(token)}");
                                    found++;
                                }
                                break;
                        }
                    }
                }
            }
        }

        if (targets.Contains("notes"))
        {
            foreach (var scenario in index.Scenarios)
                for (var d = 0; d < scenario.Diagrams.Count; d++)
                {
                    var diagram = PayloadReader.Read(index, scenario.Diagrams[d]);
                    if (diagram is null)
                        continue;
                    foreach (var (i, text) in PayloadReader.Notes(diagram))
                        if (TokenIn(text) is { } token)
                            hits.Add($"{scenario.Address}/d{d}/n{i,-4} note       {Excerpt(text, token)}{Approx(token)}");
                }
        }

        if (options.Count)
        {
            writer.Line(hits.Count.ToString());
            return 0;
        }

        if (hits.Count == 0)
        {
            writer.Line($"{needle} is not in {string.Join(", ", targets)} — as a number, under any formatting");
            writer.Footer("--tolerance 0.5 or --tolerance 1% widens the match · --in bodies,headers,uris,steps,assertions,notes widens the search");
            return 0;
        }

        writer.Page(hits, options.Offset, Math.Min(options.Limit, 200), "hits", hit => writer.Line(hit),
            $"grep \"{needle}\" " + options.RerunPrefix());
        return 0;
    }

    /// <summary>
    /// The numeric readings of one token: comma-as-thousands (<c>4,173.00</c> → 4173.00) and
    /// comma-as-decimal (<c>4.173,00</c> → 4173.00). Matching either is a hit.
    /// </summary>
    private static IEnumerable<double> Interpretations(string token)
    {
        var plain = token.Replace(",", "").Replace("_", "").Replace(" ", "");
        var parsedPlain = double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var us);
        if (parsedPlain)
            yield return us;

        if (token.Contains(','))
        {
            var european = token.Replace(".", "").Replace("_", "").Replace(" ", "").Replace(',', '.');
            if (double.TryParse(european, NumberStyles.Float, CultureInfo.InvariantCulture, out var eu)
                && (!parsedPlain || eu != us))
                yield return eu;
        }
    }
}
