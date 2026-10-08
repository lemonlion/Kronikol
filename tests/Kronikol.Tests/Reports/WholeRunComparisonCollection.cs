namespace Kronikol.Tests.Reports;

/// <summary>
/// A fact that runs the whole pipeline twice and compares what the two runs wrote runs here, after every parallel
/// collection.
/// </summary>
/// <remarks>
/// The pipeline reads every call the process logged, and the component diagram draws them all, whichever test logged
/// them, so a call another class logged between the two runs made them differ: <see cref="CultureInvariantPipelineTests"/>
/// failed under ar-SA in a full run of the core suite on 2026-10-08, and failed under every culture when a call under
/// another test's id was logged between its runs. Here nothing runs beside it, so nothing logs between them, and
/// nothing draws through the diagram cache while it does, which is what the DiagramsFetcher collection is for.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WholeRunComparisonCollection
{
    public const string Name = "WholeRunComparison";
}
