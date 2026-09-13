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

    /// <summary>
    /// The request a response answers — <see cref="FindResponse"/> run backwards, and by the same rule:
    /// the exact pairing id when the entry carries one, a short backward scan when it does not.
    /// </summary>
    internal static InteractionEntry? FindRequest(ScenarioEntry scenario, InteractionEntry response)
    {
        if (response.RequestResponseId is { } id)
        {
            foreach (var candidate in scenario.Interactions)
                if (candidate.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)
                    && candidate.RequestResponseId == id)
                    return candidate;
            return null;
        }

        for (var i = response.Ordinal - 1; i >= 0 && i >= response.Ordinal - 4; i--)
        {
            var candidate = scenario.Interactions[i];
            if (candidate.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)
                && candidate.ServiceName == response.ServiceName)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The other half of a call, with the word for how it relates. Null for an entry that has no other
    /// half — a marker, a user action, a fire-and-forget publish.
    /// </summary>
    internal static (string Role, InteractionEntry Other)? Counterpart(ScenarioEntry scenario, InteractionEntry interaction) =>
        interaction.Type.Equals("Request", StringComparison.OrdinalIgnoreCase)
            ? FindResponse(scenario, interaction) is { } response ? ("response", response) : null
            : interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase)
                ? FindRequest(scenario, interaction) is { } request ? ("answers", request) : null
                : null;

    /// <summary>
    /// An address as a reader can act on it. A response's own address fetches the response, but appears in
    /// no listing — every listing folds the pair into one row under the request's address — so on its own
    /// it cannot be traced back to the call it belongs to. Naming both closes that gap without widening
    /// every row in the tool to carry an address most readers never need.
    /// </summary>
    internal static string Describe(ReportIndex index, string occurrence)
    {
        if (!Address.TryParse(occurrence, out var address)
            || address.Kind != AddressKind.Interaction
            || index.Scenario(address.Scenario) is not { } scenario)
            return occurrence;

        var interaction = scenario.Interactions.FirstOrDefault(i => i.Ordinal == address.Interaction);
        if (interaction is null || !interaction.Type.Equals("Response", StringComparison.OrdinalIgnoreCase))
            return occurrence;

        return FindRequest(scenario, interaction) is { } request
            ? $"{occurrence}  (the response to {request.Address(scenario)})"
            : occurrence;
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

    /// <summary>The orderings <c>services --sort</c> understands.</summary>
    internal static readonly string[] ServiceSorts = ["calls", "duration", "bytes", "errors"];

    /// <summary>The orderings <c>interactions --group-by ... --sort</c> understands. Narrower than
    /// <see cref="ServiceSorts"/>: a bucket has no single byte total to sort on.</summary>
    internal static readonly string[] BucketSorts = ["calls", "duration", "errors"];

    /// <summary>
    /// Refuses an ordering the view cannot apply. Both call sites used to end in a <c>_ =&gt;</c> arm, so a
    /// misspelling - or a value only the other view supports, like <c>--group-by ... --sort bytes</c> -
    /// silently produced the default ordering while the agent read the output as sorted by what it asked
    /// for. Wrong order is a quieter failure than no output, which is exactly why it needs saying.
    /// </summary>
    internal static bool SortIsValid(QueryOptions options, string[] allowed, TextWriter error)
    {
        if (options.Sort is not { Length: > 0 } sort || allowed.Contains(sort, StringComparer.OrdinalIgnoreCase))
            return true;

        error.WriteLine($"Unknown --sort value: {sort}");
        error.WriteLine("Valid here: " + string.Join(", ", allowed));
        return false;
    }

    /// <summary>
    /// Whether a captured status is a failure. Delegates to <see cref="Kronikol.Reports.InteractionStatus"/>,
    /// which is where the rule moved when the failures digest started asking the same question: a digest
    /// that called a cache <c>Miss</c> an error while <c>query services</c> did not would be two surfaces
    /// of one report disagreeing about one call.
    /// </summary>
    internal static bool IsError(string? statusCode, string? statusText = null) =>
        Kronikol.Reports.InteractionStatus.IsError(statusCode, statusText);

    /// <summary>
    /// The numeric code and the label for an interaction, reading both the current two-field shape and
    /// the pre-3.1.0 single field. Every status question in the tool goes through here so no two verbs
    /// can disagree about the same call.
    /// </summary>
    internal static (int? Code, string? Text) StatusOf(InteractionEntry? interaction) =>
        interaction is null
            ? (null, null)
            : Kronikol.Reports.InteractionStatus.Read(interaction.StatusCode, interaction.StatusText);
}
