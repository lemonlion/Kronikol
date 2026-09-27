namespace Kronikol;

/// <summary>
/// Controls how internal-flow visualization content is bundled in the HTML report.
/// </summary>
public enum InternalFlowContentStrategy
{
    /// <summary>Span data is embedded directly in the main report HTML.</summary>
    Embedded,

    /// <summary>Span data is stored in separate fragment files loaded on demand. Not implemented: behaves as <see cref="Embedded"/>.</summary>
    SeparateFragments
}
