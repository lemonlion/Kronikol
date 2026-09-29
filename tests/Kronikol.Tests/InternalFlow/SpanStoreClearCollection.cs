namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests that clear <see cref="Kronikol.InternalFlow.InternalFlowSpanStore"/> run here, after every parallel collection.
/// </summary>
/// <remarks>
/// The store is one per process, and every report a test generates with internal flow on reads it. A clear made while
/// such a test sits between adding its spans and generating its report leaves its calls with none: under
/// <c>HideLink</c> the page then has no segment element at all, which is how
/// <see cref="InternalFlowSegmentMapReportTests"/> failed in about one run of the internal-flow tests in three.
/// <c>DisableParallelization</c> runs this collection after every parallel one, so nothing else reads the store while
/// its tests clear it. <c>ProcessGlobalStoreTests</c> keeps every clear here.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpanStoreClearCollection
{
    public const string Name = "SpanStoreClear";
}
