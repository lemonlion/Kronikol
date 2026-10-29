using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Kronikol.Extensions.OpenTelemetry;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.OpenTelemetry;

/// <summary>
/// Tests that the OTel SDK path (<see cref="OpenTelemetryTrackingExtensions.AddTestTrackingExporter"/>)
/// still works and writes to <see cref="InternalFlowSpanStore"/>.
/// All assertions filter by unique source name for parallel safety.
/// </summary>
[Collection("InternalFlowSpanStore")]
public class AddTestTrackingExporterTests : IDisposable
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
        _provider.GetService<TracerProvider>();
        return _provider;
    }

    [Fact]
    public void Captures_to_InternalFlowSpanStore()
    {
        var sourceName = $"OTel.Compat.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddOpenTelemetry()
            .WithTracing(b => b.AddSource(sourceName).AddTestTrackingExporter()));

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("otel-op");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "otel-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Backward_compat_facade_reads_same_data()
    {
        var sourceName = $"Facade.{Guid.NewGuid():N}";
        BuildProvider(s => s.AddOpenTelemetry()
            .WithTracing(b => b.AddSource(sourceName).AddTestTrackingExporter()));

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("facade-op");
        activity?.Stop();

        Assert.Contains(TestTrackingSpanStore.GetSpans(),
            s => s.DisplayName == "facade-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Both_paths_feed_same_store()
    {
        var otelSourceName = $"OTelPath.{Guid.NewGuid():N}";
        var listenerSourceName = $"ListenerPath.{Guid.NewGuid():N}";

        var services = new ServiceCollection();
        services.AddOpenTelemetry()
            .WithTracing(b => b.AddSource(otelSourceName).AddTestTrackingExporter());
        services.AddActivityListenerForInternalFlowTracking(listenerSourceName);
        _provider = services.BuildServiceProvider();
        _provider.GetService<TracerProvider>();

        using var otelSource = new ActivitySource(otelSourceName);
        using var listenerSource = new ActivitySource(listenerSourceName);

        using var a1 = otelSource.StartActivity("otel-op");
        a1?.Stop();
        using var a2 = listenerSource.StartActivity("listener-op");
        a2?.Stop();

        var spans = InternalFlowSpanStore.GetSpans();
        Assert.Contains(spans, s => s.DisplayName == "otel-op" && s.Source.Name == otelSourceName);
        Assert.Contains(spans, s => s.DisplayName == "listener-op" && s.Source.Name == listenerSourceName);
    }
}
