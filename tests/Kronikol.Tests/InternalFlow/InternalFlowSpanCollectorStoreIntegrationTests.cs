using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests that <see cref="InternalFlowSpanCollector"/> reads directly from
/// <see cref="InternalFlowSpanStore"/> (no reflection).
/// </summary>
public class InternalFlowSpanCollectorStoreIntegrationTests : IDisposable
{
    private readonly string _sourceName;
    private readonly ActivitySource _source;
    private readonly ActivityListener _rawListener;

    public InternalFlowSpanCollectorStoreIntegrationTests()
    {
        _sourceName = $"CollectorInteg.{Guid.NewGuid():N}";
        _source = new ActivitySource(_sourceName);
        _rawListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == _sourceName || s.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_rawListener);
    }

    public void Dispose()
    {
        _rawListener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void CollectSpans_returns_spans_from_store()
    {
        using var activity = _source.StartActivity("store-op")!;
        activity.Stop();
        InternalFlowSpanStore.Add(activity);

        var collected = InternalFlowSpanCollector.CollectSpans(InternalFlowSpanGranularity.Full);
        Assert.Contains(collected, s => s.DisplayName == "store-op" && s.Source.Name == _sourceName);
    }

    [Fact]
    public void AutoInstrumentation_includes_custom_spans_sharing_trace_with_well_known()
    {
        using var wellKnown = new ActivitySource("Microsoft.AspNetCore");
        var opName = $"aspnet-{Guid.NewGuid():N}";
        using var a1 = wellKnown.StartActivity(opName)!;
        var wellKnownTraceId = a1.TraceId;
        a1.Stop();
        InternalFlowSpanStore.Add(a1);

        // A custom source span with the SAME TraceId (child of well-known)
        var childContext = new ActivityContext(wellKnownTraceId, a1.SpanId, ActivityTraceFlags.None);
        using var custom = _source.StartActivity("custom-child", ActivityKind.Internal, childContext)!;
        custom.Stop();
        InternalFlowSpanStore.Add(custom);

        // A custom source span with a DIFFERENT TraceId (unrelated)
        var unrelatedOpName = $"unrelated-{Guid.NewGuid():N}";
        using var unrelated = _source.StartActivity(unrelatedOpName)!;
        unrelated.Stop();
        InternalFlowSpanStore.Add(unrelated);

        var collected = InternalFlowSpanCollector.CollectSpans(InternalFlowSpanGranularity.AutoInstrumentation);
        Assert.Contains(collected, s => s.DisplayName == opName);
        Assert.Contains(collected, s => s.DisplayName == "custom-child");
        Assert.DoesNotContain(collected, s => s.DisplayName == unrelatedOpName);
    }

    [Fact]
    public void Manual_with_System_Net_Http_keeps_an_HttpClient_span_made_without_its_source()
    {
        // #138: HttpClient makes its span with new Activity(...) when nothing hears its source, and that span belongs to
        // no source, so Manual with System.Net.Http matched none of them.
        var http = MadeWithoutASource("System.Net.Http.HttpRequestOut");

        var collected = InternalFlowSpanCollector.CollectSpans(InternalFlowSpanGranularity.Manual, ["System.Net.Http"]);

        Assert.Contains(collected, s => ReferenceEquals(s, http));
    }

    [Fact]
    public void AutoInstrumentation_keeps_a_trace_whose_only_well_known_span_is_an_HttpClient_span_made_without_its_source()
    {
        // #138: System.Net.Http is a well-known source, but no span captured carried the name, so such a trace was dropped.
        var http = MadeWithoutASource("System.Net.Http.HttpRequestOut");
        var under = new ActivityContext(http.TraceId, http.SpanId, ActivityTraceFlags.None);
        using var custom = _source.StartActivity($"custom-{Guid.NewGuid():N}", ActivityKind.Internal, under)!;
        custom.Stop();
        InternalFlowSpanStore.Add(custom);

        var collected = InternalFlowSpanCollector.CollectSpans(InternalFlowSpanGranularity.AutoInstrumentation);

        Assert.Contains(collected, s => ReferenceEquals(s, http));
        Assert.Contains(collected, s => ReferenceEquals(s, custom));
    }

    [Fact]
    public void AutoInstrumentation_keeps_a_trace_anchored_by_a_source_in_a_listed_namespace()
    {
        // Cosmos DB's SDK starts its spans from Azure.Cosmos.Operation, the Azure Storage clients theirs from
        // Azure.Storage.Blobs.BlobClient and the like. No source is named Azure.Cosmos or Azure.Storage, so the list's
        // names matched none of them, and a trace whose only well-known span was one of theirs was dropped.
        using var cosmos = new ActivitySource("Azure.Cosmos.Operation");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => ReferenceEquals(s, cosmos),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var previous = Activity.Current;
        Activity.Current = null;
        var operation = cosmos.StartActivity($"ReadItemAsync {Guid.NewGuid():N}", ActivityKind.Client)!;
        operation.Stop();
        Activity.Current = previous;
        InternalFlowSpanStore.Add(operation);
        var under = new ActivityContext(operation.TraceId, operation.SpanId, ActivityTraceFlags.None);
        using var custom = _source.StartActivity($"custom-{Guid.NewGuid():N}", ActivityKind.Internal, under)!;
        custom.Stop();
        InternalFlowSpanStore.Add(custom);

        var collected = InternalFlowSpanCollector.CollectSpans(InternalFlowSpanGranularity.AutoInstrumentation);

        Assert.Contains(collected, s => ReferenceEquals(s, operation));
        Assert.Contains(collected, s => ReferenceEquals(s, custom));
    }

    [Theory]
    [InlineData("Azure.Cosmos.Operation", true)]
    [InlineData("Azure.Storage.Blobs.BlobClient", true)]
    [InlineData("Microsoft.AspNetCore", true)]
    [InlineData("System.Net.Http", true)]
    [InlineData("Azure.StorageAccounts", false)]
    [InlineData("Experimental.System.Net.Http.Connections", false)]
    [InlineData("MyApp.Orders", false)]
    public void A_listed_name_is_well_known_with_every_source_in_its_namespace(string source, bool wellKnown) =>
        Assert.Equal(wellKnown, InternalFlowSpanCollector.IsWellKnown(source));

    private static Activity MadeWithoutASource(string operationName)
    {
        var previous = Activity.Current;
        Activity.Current = null;
        var activity = new Activity(operationName).Start();
        activity.Stop();
        Activity.Current = previous;
        InternalFlowSpanStore.Add(activity);
        return activity;
    }
}
