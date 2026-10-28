namespace Kronikol.CallGraph;

/// <summary>
/// A scenario as the call graph reads it: what the card's scenario index names it by (plans/SERVICE_CARD_PLAN.md 4.3),
/// and whether it is a test at all. <see cref="Ordinal"/> is its <c>sN</c> address in the report.
/// </summary>
internal sealed record CallScenario
{
    public int Ordinal { get; init; }
    public string StableId { get; init; } = "";
    public string Feature { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>The effective tags: the feature's, then the scenario's own, each once.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? SourceFile { get; init; }
    public int? SourceLine { get; init; }
    public string? FeatureSourceFile { get; init; }

    /// <summary>False for a scenario that is no test (an ingest's fold, a stack's start-up), which the card leaves out.</summary>
    public bool IsTest { get; init; } = true;

    /// <summary>The result as the report spells it (<c>Passed</c>, <c>SkippedAfterFailure</c>).</summary>
    public string Result { get; init; } = "";

    public string Address => "s" + Ordinal;
}

/// <summary>
/// One record of a call, its request or its response, in the neutral shape every reader maps into
/// (plans/SERVICE_CARD_PLAN.md 4.2): a scanned report of any version the scanner reads, or records written by hand in a
/// test. Values are as the data file holds them, with the scanner's two readings applied: the empty Guid is no id, and a
/// phase of <c>Unknown</c> and a meta type of <c>Default</c> are none.
/// </summary>
internal sealed record CallRecord
{
    /// <summary>The scenario that owns the call, or null for a call of the data file's root <c>background</c> block.</summary>
    public CallScenario? Scenario { get; init; }

    /// <summary>The record's place in its scenario's list as the data file writes it, responses counted: the <c>iM</c> of its address.</summary>
    public int Position { get; init; }

    /// <summary>True for a request, false for a response.</summary>
    public bool IsRequest { get; init; } = true;

    public string Caller { get; init; } = "";
    public string Service { get; init; } = "";

    /// <summary>The verb, or a tracker's label (<c>Produce → orders</c>), as the data file writes it.</summary>
    public string? Method { get; init; }

    /// <summary>The call's URI as the data file writes it.</summary>
    public string Uri { get; init; } = "";

    public string? Category { get; init; }
    public string? CallerCategory { get; init; }

    /// <summary>The record's meta type is <c>Event</c>, which Spanner, BigQuery and Bigtable write: no message call.</summary>
    public bool IsEvent { get; init; }

    /// <summary>The recorded phase, <c>Setup</c> or <c>Action</c>; null when none was recorded.</summary>
    public string? Phase { get; init; }

    /// <summary>How the call got its scenario (<c>RequestHeader</c>, <c>TestContext</c>), or null where the producer records none.</summary>
    public string? AttributionSource { get; init; }

    public string? RequestResponseId { get; init; }
    public string? TraceId { get; init; }

    /// <summary>On a response, its status: the code's digits, else its label; null when it carries neither.</summary>
    public string? Status { get; init; }

    /// <summary>On a response, whether <see cref="Kronikol.Reports.InteractionStatus.IsError"/> reads its status as an error.</summary>
    public bool IsError { get; init; }

    public bool IsUserAction { get; init; }

    /// <summary>What answered the call in its service's place, when a capturer saw a test double do so (#190).</summary>
    public string? TestDouble { get; init; }

    /// <summary>
    /// The request's content where the operation key reads it, a statement-shaped call's or a POST's (a GraphQL
    /// candidate); null on every other record, so no other body is read.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>The call's address, <c>sN/iM</c>; null for a call of the root background block, which has none.</summary>
    public string? Address => Scenario is { } scenario ? $"{scenario.Address}/i{Position}" : null;
}
