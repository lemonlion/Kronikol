namespace Kronikol;

/// <summary>
/// Controls which <see cref="System.Diagnostics.Activity"/> spans are included in internal-flow diagrams.
/// </summary>
public enum InternalFlowSpanGranularity
{
    /// <summary>
    /// Include the spans of every trace that holds a span from a well-known auto-instrumentation source (ASP.NET Core,
    /// HttpClient, EF Core, etc., and the sources in their namespaces), your own spans in those traces among them.
    /// </summary>
    AutoInstrumentation,

    /// <summary>
    /// Include only the spans whose source is one of <see cref="ReportConfigurationOptions.InternalFlowActivitySources"/>,
    /// or every span when it names none.
    /// </summary>
    Manual,

    /// <summary>Include all captured spans regardless of source.</summary>
    Full
}
