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
    /// The statuses that are not numbers and are not failures. The HTTP non-200 successes, and then the
    /// labels Kronikol itself stamps on calls that have no status code: a broker publish is <c>Sent</c>,
    /// a consume <c>Ack</c>, a reply <c>Responded</c> (<c>MessageTracker</c>'s own defaults), a cache
    /// lookup <c>Hit</c> or <c>Miss</c> — a miss is an outcome, not a failure — and a Spanner
    /// transaction <c>Committed</c>. A refusal (<c>Nack</c>, <c>Fault</c>) is deliberately absent.
    /// </summary>
    private static readonly HashSet<string> NonErrorStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Created", "Accepted", "NoContent",
        "Sent", "Ack", "Responded", "Hit", "Miss", "Committed",
    };

    /// <summary>
    /// Treats anything that is not a success as an error, including the non-numeric statuses the non-HTTP
    /// taps use (a database driver reports <c>ERROR</c>, not 500) — while knowing the successes that are
    /// not spelled <c>OK</c> by name (<see cref="NonErrorStatuses"/>). The single classifier behind
    /// <c>services</c>, <c>flow --errors-only</c> and <c>--group-by</c>, so no two commands can disagree
    /// about the same call.
    /// </summary>
    internal static bool IsError(string? statusCode, string? statusText = null)
    {
        var (code, text) = Kronikol.Reports.InteractionStatus.Read(statusCode, statusText);

        // A number settles it on its own: from 3.1.0 every HTTP call has one, so the name list below is
        // only ever consulted for the taps that genuinely have no code.
        if (code is { } numeric) return numeric >= 400;

        return text is { Length: > 0 }
               && !text.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
               && !NonErrorStatuses.Contains(text);
    }

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
