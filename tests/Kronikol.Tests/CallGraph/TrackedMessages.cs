using Kronikol.CallGraph;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7: a call a broker package's tracker recorded, read by the service card as it reads the
/// call from the data file (its label and URI as the file writes them), beside the test's own call into the subject.
/// Linked into each broker package's tests, which hold that package's row of the card's message table to the labels and
/// URIs its tracker writes.
/// </summary>
internal static class TrackedMessages
{
    /// <summary>The service under test: name a tracker's caller this, or its service for a delivery.</summary>
    internal const string Subject = "Breakfast Provider";

    /// <summary>What the card says of the call: <c>publishes</c>, <c>consumes</c>, <c>no direction</c> for a label its table cannot map, or <c>no message</c>.</summary>
    internal static string Direction(RequestResponseLog call) =>
        Graph(call).Messages is [var message] ? message.Direction ?? "no direction" : "no message";

    /// <summary>The topic or queue the card names for the call; null when it names none or the call is no message.</summary>
    internal static string? Topic(RequestResponseLog call) => Graph(call).Messages is [var message] ? message.Topic : null;

    /// <summary>The call's operation is one the subject exposes, as no delivery into it is.</summary>
    internal static bool Exposed(RequestResponseLog call)
    {
        var graph = Graph(call);
        return graph.Exposes.Count > 1;
    }

    /// <summary>The call is listed in the card's targets that name no destination.</summary>
    internal static bool EmptyTarget(RequestResponseLog call) => Graph(call).EmptyTargets.Count > 0;

    private static Graph Graph(RequestResponseLog call)
    {
        var scenario = new CallScenario { Ordinal = 0, StableId = "orders-0", Feature = "Orders", Name = "Order pancakes" };
        CallRecord[] records =
        [
            new() { Scenario = scenario, Position = 0, Caller = "Caller", Service = Subject, Method = "POST", Uri = "http://localhost/orders", AttributionSource = "TestContext" },
            new()
            {
                Scenario = scenario, Position = 1, Caller = call.CallerName, Service = call.ServiceName, Method = ReportGenerator.MethodText(call.Method),
                Uri = call.Uri.ToString(), Category = call.DependencyCategory, CallerCategory = call.CallerDependencyCategory,
                IsEvent = call.MetaType == RequestResponseMetaType.Event, AttributionSource = "RequestHeader"
            }
        ];
        return CallGraphBuilder.Build([scenario], records,
            new PartiesInput([new Party(Subject, "breakfast-provider", PartyKind.UnderTest, PartyKindSource.Declared, null)], "breakfast-provider"));
    }
}
