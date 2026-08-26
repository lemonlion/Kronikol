using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Infrastructure shared by every command that walks the run: exact request/response pairing, run-wide
/// iteration, and the one error classifier — one answer to "is this an error", however a command asks.
/// </summary>
internal static partial class QueryCommand
{
    /// <summary>
    /// Every request in scope with its exactly-paired response — null when the call is genuinely
    /// unpaired: a fire-and-forget event, or a request whose response was never captured. Pairing is by
    /// <c>requestResponseId</c>, the identity the report itself groups on; the proximity heuristic
    /// survives only for entries that carry no id.
    /// </summary>
    internal static IEnumerable<(ScenarioEntry Scenario, InteractionEntry Request, InteractionEntry? Response)>
        AllInteractions(ReportIndex index, ScenarioEntry? only = null)
    {
        IEnumerable<ScenarioEntry> scope = only is null ? index.Scenarios : [only];
        foreach (var scenario in scope)
        {
            var responsesById = ResponsesById(scenario);
            foreach (var interaction in scenario.Interactions)
            {
                if (!interaction.Type.Equals("Request", StringComparison.OrdinalIgnoreCase))
                    continue;

                var response = interaction.RequestResponseId is { } id
                    ? responsesById.GetValueOrDefault(id)
                    : FindResponse(scenario, interaction);
                yield return (scenario, interaction, response);
            }
        }
    }

    private static Dictionary<string, InteractionEntry> ResponsesById(ScenarioEntry scenario)
    {
        var byId = new Dictionary<string, InteractionEntry>(StringComparer.Ordinal);
        foreach (var interaction in scenario.Interactions)
            if (interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)
                && interaction.RequestResponseId is { } id)
                byId.TryAdd(id, interaction);
        return byId;
    }

    /// <summary>
    /// Parses every <c>--where</c> expression, or explains the grammar and returns null (exit 2).
    /// </summary>
    private static List<WhereClause>? ParseWheres(QueryOptions options, TextWriter error)
    {
        var clauses = new List<WhereClause>();
        foreach (var expression in options.Where)
        {
            if (!WhereClause.TryParse(expression, out var clause, out var parseError))
            {
                error.WriteLine($"Bad --where: {parseError}");
                error.WriteLine(WhereClause.Grammar);
                return null;
            }
            clauses.Add(clause!);
        }
        return clauses;
    }

    /// <summary>
    /// Whether the call passes every clause. A clause whose targeted body is missing or not JSON cannot
    /// be satisfied — the call fails and <paramref name="unevaluable"/> says why, so the caller can
    /// footnote the exclusion rather than hide it.
    /// </summary>
    private static bool SatisfiesWheres(IReadOnlyList<WhereClause> clauses, InteractionEntry request,
        InteractionEntry? response, QueryOptions options, BodyCache cache, ref bool unevaluable)
    {
        foreach (var clause in clauses)
        {
            var target = clause.TargetsRequest || options.Request ? request : response;
            if (target?.BodyHash is not { } hash || cache.Json(hash) is not { } document)
            {
                unevaluable = true;
                return false;
            }
            if (!clause.Evaluate(document.RootElement))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Treats anything that is not a success as an error, including the non-numeric statuses the non-HTTP
    /// taps use (a database driver reports <c>ERROR</c>, not 500) — while knowing the non-200 successes
    /// (<c>Created</c>, <c>Accepted</c>, <c>NoContent</c>) by name. The single classifier behind
    /// <c>services</c>, <c>flow --errors-only</c> and <c>--group-by</c>, so no two commands can disagree
    /// about the same call.
    /// </summary>
    internal static bool IsError(string? status) =>
        status is { Length: > 0 }
        && (int.TryParse(status, out var numeric)
            ? numeric >= 400
            : !status.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
              && !status.Equals("Created", StringComparison.OrdinalIgnoreCase)
              && !status.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
              && !status.Equals("NoContent", StringComparison.OrdinalIgnoreCase));
}
