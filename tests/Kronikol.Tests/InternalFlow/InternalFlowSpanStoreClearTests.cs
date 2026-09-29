using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// <see cref="InternalFlowSpanStore.Clear"/> empties the store of the whole process, so its fact runs in
/// <see cref="SpanStoreClearCollection"/>, after every class that reads the store back.
/// </summary>
[Collection(SpanStoreClearCollection.Name)]
public class InternalFlowSpanStoreClearTests : IDisposable
{
    private readonly string _sourceName = $"StoreClearTest.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;

    public InternalFlowSpanStoreClearTests()
    {
        _source = new ActivitySource(_sourceName);
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void Clear_removes_all_spans()
    {
        using var activity = _source.StartActivity("clear-op")!;
        activity.Stop();
        InternalFlowSpanStore.Add(activity);

        InternalFlowSpanStore.Clear();

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "clear-op" && s.Source.Name == _sourceName);
    }
}
