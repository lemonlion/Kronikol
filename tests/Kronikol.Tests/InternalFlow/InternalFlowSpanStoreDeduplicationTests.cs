using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests for <see cref="InternalFlowSpanStore.Add"/> reference-equality deduplication.
/// </summary>
public class InternalFlowSpanStoreDeduplicationTests
{
    [Fact]
    public void Add_same_activity_twice_stores_only_one_copy()
    {
        var sourceName = $"Dedup.{Guid.NewGuid():N}";
        using var listener = new InternalFlowActivityListener();
        using var activitySource = new ActivitySource(sourceName);

        using var activity = activitySource.StartActivity("dedup-op")!;
        activity.Stop();
        // ActivityStopped already called Add once; call it again for dedup test
        InternalFlowSpanStore.Add(activity);

        Assert.Single(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "dedup-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Add_different_activities_stores_both()
    {
        var sourceName = $"NoDup.{Guid.NewGuid():N}";
        using var listener = new InternalFlowActivityListener();
        using var activitySource = new ActivitySource(sourceName);

        using var a1 = activitySource.StartActivity("op-1")!;
        a1.Stop();
        using var a2 = activitySource.StartActivity("op-2")!;
        a2.Stop();

        // ActivityStopped already added both; Add again to ensure no false dedup
        InternalFlowSpanStore.Add(a1);
        InternalFlowSpanStore.Add(a2);

        Assert.Equal(2, InternalFlowSpanStore.GetSpans().Count(s => s.Source.Name == sourceName));
    }
}
