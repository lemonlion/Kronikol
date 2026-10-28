using Kronikol.Parties;

namespace Kronikol.CallGraph;

/// <summary>
/// One run's call graph (plans/SERVICE_CARD_PLAN.md 4.2): its parties, the edges between them and each edge's operations,
/// and for each call the call it ran inside, its <c>origin</c>, its <c>cause</c> and its effective phase. Built in memory
/// from <see cref="CallRecord"/>s by <see cref="CallGraphBuilder"/>; <c>service.json</c> is a projection of it
/// (<see cref="Kronikol.Reports.ServiceCardWriter"/>). Every list is sorted by ordinal comparison, so the graph of one
/// set of records is the same whatever order they came in and whatever the culture.
/// </summary>
internal sealed class Graph
{
    /// <summary>One entry a request, in the order the records came.</summary>
    public required IReadOnlyList<GraphCall> Calls { get; init; }

    /// <summary>Every caller and service name in the call set, by name.</summary>
    public required IReadOnlyList<GraphParty> Parties { get; init; }

    /// <summary>The subject row: the <c>underTest</c> party that received the most calls, then the first by name; null when no party is.</summary>
    public GraphParty? Subject { get; init; }

    /// <summary>By from, to and protocol.</summary>
    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    /// <summary>Every operation of every edge, by <see cref="GraphOperation.Op"/>.</summary>
    public required IReadOnlyList<GraphOperation> Operations { get; init; }

    /// <summary>The subject's messages, by party, topic, direction and op.</summary>
    public required IReadOnlyList<GraphMessage> Messages { get; init; }

    /// <summary>The operations the subject received that are neither deliveries nor user actions, by op.</summary>
    public required IReadOnlyList<string> Exposes { get; init; }

    /// <summary>The scenarios that are tests, by stable id; rows sharing one are one.</summary>
    public required IReadOnlyList<GraphScenario> Scenarios { get; init; }

    /// <summary>The report can carry calls: false only for a mergeable file written before 3.1.0.</summary>
    public bool CallsRecorded { get; init; } = true;

    /// <summary>Some call in the run was made by the subject.</summary>
    public bool SubjectCallsRecorded { get; init; }

    /// <summary>The message operations whose URI holds no destination where one belongs, or only a tracker's placeholder, by op.</summary>
    public required IReadOnlyList<string> EmptyTargets { get; init; }

    /// <summary>The rules the graph was built under, which a card records in <c>rules</c>.</summary>
    public required GraphRules Rules { get; init; }
}

/// <summary>
/// The rules a graph was built under: the operation key's version, the consumer's templating rules as a report records them
/// (their hash, <c>"none"</c>, or null for a report written before it recorded them), and the inference's version when a
/// row was inferred.
/// </summary>
internal sealed record GraphRules(int OperationKey, string? TemplateRules, int? Inference);

/// <summary>One call: its request and response records, the call it ran inside, and what the graph reads of it.</summary>
internal sealed class GraphCall
{
    public required CallRecord Request { get; init; }

    /// <summary>The first response carrying the request's id, or null when none was recorded.</summary>
    public CallRecord? Response { get; init; }

    /// <summary>The position in <see cref="Graph.Calls"/> of the call this one ran inside, or null at the top level.</summary>
    public int? Parent { get; set; }

    /// <summary>The caller's role: <c>test</c>, <c>subject</c> or <c>other</c>.</summary>
    public string Origin { get; set; } = "";

    /// <summary>Why the call was made: <c>test</c>, <c>request</c>, <c>background</c> or <c>unknown</c>.</summary>
    public string Cause { get; set; } = "";

    /// <summary><c>setup</c>, <c>action</c> or <c>unknown</c>.</summary>
    public string EffectivePhase { get; set; } = "";

    /// <summary>The effective phase came through nesting, not from the call's own record.</summary>
    public bool PhaseDerived { get; set; }

    /// <summary>The operation's id.</summary>
    public string Op { get; set; } = "";

    /// <summary>The status as the card writes it: the code's digits, else the label, else <c>none</c> for a call with no response.</summary>
    public string Status { get; set; } = "";

    public bool IsError { get; set; }
}

/// <summary>A party: its row from the report's block or the inference, the categories its calls carried, and the run's facts of it.</summary>
internal sealed record GraphParty(string Name, string? Id, PartyKind Kind, PartyKindSource? KindSource, string? Note)
{
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The distinct URI authorities of the calls it received, as written.</summary>
    public IReadOnlyList<string> Hosts { get; init; } = [];

    /// <summary>How many of the calls it received carry a test double's mark.</summary>
    public int DoubleCalls { get; init; }

    /// <summary>How many calls it received.</summary>
    public int Received { get; init; }
}

/// <summary>An edge: two parties and a protocol, with its operations by method, target and GraphQL operation.</summary>
internal sealed record GraphEdge(string From, string To, string Protocol)
{
    public IReadOnlyList<GraphOperation> Operations { get; init; } = [];
}

/// <summary>
/// One operation of an edge: what is behaviour (its sets) and what is the run's (its counts and first address).
/// </summary>
internal sealed class GraphOperation
{
    public required string Op { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Protocol { get; init; }
    public required string Method { get; init; }
    public required string Target { get; init; }
    public string? GraphqlOperation { get; init; }

    public IReadOnlyList<string> Statuses { get; set; } = [];
    public IReadOnlyList<string> Origins { get; set; } = [];
    public IReadOnlyList<string> Causes { get; set; } = [];
    public IReadOnlyList<string> EffectivePhases { get; set; } = [];

    public int Calls { get; set; }
    public int Errors { get; set; }

    /// <summary>Calls by status, by status.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> StatusCounts { get; set; } = [];
    public int Setup { get; set; }
    public int Action { get; set; }
    public int Unknown { get; set; }
    public int DerivedPhases { get; set; }

    /// <summary>The first call's address in report order; null for an operation seen only in the root background block.</summary>
    public string? Example { get; set; }
}

/// <summary>A message the subject publishes or consumes: <see cref="Direction"/> is <c>publishes</c>, <c>consumes</c>, or null for a label the table cannot map.</summary>
internal sealed record GraphMessage(string Op, string? Direction, string Party, string? Topic);

/// <summary>A scenario that is a test, with the test's own operations in it and every operation its calls reached.</summary>
internal sealed record GraphScenario(CallScenario Scenario, IReadOnlyList<string> Ops, IReadOnlyList<string> AllOps);
