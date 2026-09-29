using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests for <c>AddActivityListenerForInternalFlowTracking</c> IServiceCollection extension.
/// All assertions filter by unique source/operation name for parallel safety.
/// The store's clear runs in <see cref="SpanStoreClearCollection"/>, after every parallel collection, so it cannot race these.
/// </summary>
[Collection("InternalFlowSpanStore")]
public class AddActivityListenerForInternalFlowTrackingTests : IDisposable
{
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        _provider = services.BuildServiceProvider();
        return _provider;
    }

    [Fact]
    public void Does_not_capture_spans_from_well_known_source()
    {
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking());

        var opName = $"GET-{Guid.NewGuid():N}";
        using var source = new ActivitySource("System.Net.Http");
        using var activity = source.StartActivity(opName);
        activity?.Stop();

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == opName && s.Source.Name == "System.Net.Http");
    }

    [Fact]
    public void Captures_spans_from_additional_custom_source()
    {
        var sourceName = $"Custom.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking(sourceName));

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("custom-operation");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "custom-operation" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Captures_any_source_including_unregistered()
    {
        var sourceName = $"Unregistered.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking());

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("unregistered-op");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(), s => s.Source.Name == sourceName);
    }

    [Fact]
    public void Returns_service_collection_for_fluent_chaining()
    {
        var services = new ServiceCollection();
        var result = services.AddActivityListenerForInternalFlowTracking();
        Assert.Same(services, result);
    }

    [Fact]
    public void Listener_captures_until_explicitly_disposed()
    {
        var sourceName = $"Lifecycle.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking(sourceName));

        using var source = new ActivitySource(sourceName);

        using (var before = source.StartActivity("before"))
            before?.Stop();

        var beforeSpans = InternalFlowSpanStore.GetSpans()
            .Where(s => s.Source.Name == sourceName)
            .ToArray();
        Assert.Single(beforeSpans);
        Assert.Equal("before", beforeSpans[0].DisplayName);

        // Dispose should not throw
        var listener = _provider!.GetService<InternalFlowActivityListener>()!;
        listener.Dispose();
    }

    [Fact]
    public void Non_invasive_listener_uses_AllData_sampling()
    {
        var sourceName = $"Sampling.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking(sourceName));

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("non-invasive-op");
        Assert.NotNull(activity);
        Assert.True(activity!.IsAllDataRequested);

        activity.Stop();
        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "non-invasive-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Works_without_any_otel_sdk_configuration()
    {
        var sourceName = $"NoOtel.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking());

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("no-otel-op");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "no-otel-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Multiple_additional_sources()
    {
        var name1 = $"Multi.A.{Guid.NewGuid():N}";
        var name2 = $"Multi.B.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddActivityListenerForInternalFlowTracking(name1, name2));

        using var source1 = new ActivitySource(name1);
        using var source2 = new ActivitySource(name2);
        using var a1 = source1.StartActivity("op-a");
        a1?.Stop();
        using var a2 = source2.StartActivity("op-b");
        a2?.Stop();

        var spans = InternalFlowSpanStore.GetSpans();
        Assert.Contains(spans, s => s.DisplayName == "op-a" && s.Source.Name == name1);
        Assert.Contains(spans, s => s.DisplayName == "op-b" && s.Source.Name == name2);
    }
}
