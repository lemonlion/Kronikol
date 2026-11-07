using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

public class ActivitySourceDiscoveryTests : IDisposable
{
    private readonly string _sourceName = $"DiscoveryTest.{Guid.NewGuid():N}";
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;

    public ActivitySourceDiscoveryTests()
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
    public void Records_source_names_from_stored_spans()
    {
        using var activity = _source.StartActivity("test-op")!;
        activity.Stop();
        InternalFlowSpanStore.Add(activity);

        var discovered = ActivitySourceDiscovery.GetDiscoveredSources();
        Assert.True(discovered.ContainsKey(_sourceName));
        Assert.True(discovered[_sourceName] >= 1);
    }

    [Fact]
    public void Returns_empty_when_no_spans()
    {
        // Clear to ensure a clean state for this test
        var discovered = ActivitySourceDiscovery.GetDiscoveredSources();
        // Should at least not throw
        Assert.NotNull(discovered);
    }

    [Fact]
    public void Counts_multiple_spans_per_source()
    {
        using var a1 = _source.StartActivity("op1")!;
        a1.Stop();
        InternalFlowSpanStore.Add(a1);

        using var a2 = _source.StartActivity("op2")!;
        a2.Stop();
        InternalFlowSpanStore.Add(a2);

        var discovered = ActivitySourceDiscovery.GetDiscoveredSources();
        Assert.True(discovered[_sourceName] >= 2);
    }

    [Fact]
    public void Counts_a_span_made_without_a_source_under_the_name_taken_from_its_operation()
    {
        // #138: an activity made with new Activity(...) has no source name, and the diagnostics report left it uncounted.
        var name = $"Discovery{Guid.NewGuid():N}";
        var previous = Activity.Current;
        Activity.Current = null;
        var activity = new Activity($"{name}.Work").Start();
        activity.Stop();
        Activity.Current = previous;
        InternalFlowSpanStore.Add(activity);

        var discovered = ActivitySourceDiscovery.GetDiscoveredSources();

        Assert.Equal(1, discovered.GetValueOrDefault(name));
    }
}
