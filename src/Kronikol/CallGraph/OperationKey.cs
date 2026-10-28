using Kronikol.History;

namespace Kronikol.CallGraph;

/// <summary>
/// What operation a call is: its templated target, and its GraphQL operation apart (plans/SERVICE_CARD_PLAN.md 4.2). The
/// key is <see cref="CallOperation"/>'s (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.1), the one rule history, the warm-up
/// marks, the component diagram and the query engine's <c>route</c> share, read with the query's keys left out.
/// </summary>
internal static class OperationKey
{
    /// <summary>The key's version, which a card records as <c>rules.operationKey</c>.</summary>
    internal static int Version => InteractionShape.Version;

    internal static (string Target, string? Operation) Parts(string? method, string uri, string? dependencyCategory, string? requestContent,
        HistoryShapeRules? rules, bool queryKeys) =>
        CallOperation.Parts(method, uri, dependencyCategory, requestContent, rules, queryKeys);
}
