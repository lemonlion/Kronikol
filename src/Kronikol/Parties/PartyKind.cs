namespace Kronikol.Parties;

/// <summary>
/// What a party was in a run (#190). Written in the data file in camel case: <c>test</c>, <c>underTest</c>, <c>real</c>,
/// <c>double</c>, <c>unknown</c>.
/// </summary>
public enum PartyKind
{
    /// <summary>The test itself: what drove the run, such as the test's HTTP client or a user of an ingested UI test.</summary>
    Test,

    /// <summary>The service the tests are about.</summary>
    UnderTest,

    /// <summary>
    /// The party's own software produced its answers: a deployed instance, or its real image run for the test, such as a
    /// Testcontainers PostgreSQL. Never inferred.
    /// </summary>
    Real,

    /// <summary>
    /// Anything else that answered in the party's place: a stub, a fake, a recording, an MSW handler or a vendor's
    /// emulator. <see cref="PartyDescriptor.Note"/> says which. Never inferred.
    /// </summary>
    Double,

    /// <summary>Nothing declared, observed or inferred says.</summary>
    Unknown
}

/// <summary>How a party's <see cref="PartyKind"/> became known (#190).</summary>
public enum PartyKindSource
{
    /// <summary><see cref="ReportConfigurationOptions.Parties"/>, <see cref="ReportConfigurationOptions.ServiceId"/> or the parties file said so.</summary>
    Declared,

    /// <summary>Kronikol saw it: the test client <c>CreateTestTrackingClient</c> built, and the calls that client made.</summary>
    Observed,

    /// <summary>Read from the shape of the calls alone, the way a report is read when nothing is declared.</summary>
    Inferred
}
