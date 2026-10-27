namespace Kronikol.Parties;

/// <summary>
/// What a run or the parties file declares about one party (#190), matched to the calls by <see cref="Name"/> exactly,
/// as the participant is named. A member left null declares nothing, so it never overrides one that does: a lane can
/// declare only its kinds and a file shared by several suites only their ids.
/// </summary>
/// <param name="Name">The participant's name as the calls carry it, matched ordinally.</param>
public sealed record PartyDescriptor(string Name)
{
    /// <summary>An id that names the party in every report that calls it, such as its catalogue name (<c>cow-service</c>).</summary>
    public string? Id { get; init; }

    /// <summary>What the party was in the run; null declares nothing.</summary>
    public PartyKind? Kind { get; init; }

    /// <summary>Free text: what answered in the party's place (<c>WireMock stub</c>), or the application under test.</summary>
    public string? Note { get; init; }
}

/// <summary>One row of a run's parties table (#190): a participant, with what is known of it and how.</summary>
/// <param name="Name">The participant's name as the calls carry it.</param>
/// <param name="Id">The declared id, or <see cref="ReportConfigurationOptions.ServiceId"/> on the subject's row.</param>
/// <param name="Kind">What the party was.</param>
/// <param name="KindSource">How <paramref name="Kind"/> became known; null when it is <see cref="PartyKind.Unknown"/>.</param>
/// <param name="Note">The declared note, or the application of an observed subject.</param>
public sealed record Party(string Name, string? Id, PartyKind Kind, PartyKindSource? KindSource, string? Note);
