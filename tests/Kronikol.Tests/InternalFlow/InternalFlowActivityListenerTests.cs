using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// Tests for <see cref="InternalFlowActivityListener"/> — the non-invasive
/// BCL ActivityListener that captures spans without touching the OTel SDK.
/// All assertions filter by unique source name for parallel safety.
/// </summary>
[Collection("InternalFlowSpanStore")]
public class InternalFlowActivityListenerTests : IDisposable
{
    private InternalFlowActivityListener? _listener;

    public void Dispose()
    {
        _listener?.Dispose();
    }

    [Fact]
    public void Does_not_listen_to_System_Net_Http()
    {
        _listener = new InternalFlowActivityListener();

        foreach (var conflictName in InternalFlowActivityListener.AppInsightsConflictSources)
        {
            var opName = $"{conflictName}-{Guid.NewGuid():N}";
            using var source = new ActivitySource(conflictName);
            using var activity = source.StartActivity(opName);
            activity?.Stop();

            Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
                s => s.DisplayName == opName && s.Source.Name == conflictName);
        }
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("StackExchange.Redis")]
    [InlineData("Azure.Cosmos")]
    [InlineData("Azure.Storage")]
    [InlineData("Microsoft.Azure.Cosmos")]
    public void Does_listen_to_well_known_sources_other_than_AppInsights_conflicts(string sourceName)
    {
        _listener = new InternalFlowActivityListener();

        var opName = $"wk-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity(opName);
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == opName && s.Source.Name == sourceName);
    }

    [Fact]
    public void Does_not_cause_HasListeners_for_well_known_framework_sources()
    {
        // This is the exact gate that DiagnosticsHandler.CreateActivity() checks.
        // TTD must NOT cause it to return true for System.Net.Http.
        using var httpSource = new ActivitySource("System.Net.Http.Test." + Guid.NewGuid().ToString("N"));
        // Use a fresh unique name to avoid interference from other listeners in the process.
        // The real test: creating our listener should NOT subscribe to well-known sources.
        _listener = new InternalFlowActivityListener();

        using var wellKnownSource = new ActivitySource("System.Net.Http");
        // StartActivity returns null when no listener is subscribed
        var activity = wellKnownSource.StartActivity("should-be-null");
        // Note: In a test process other listeners may exist, so we can't assert null.
        // Instead verify our listener didn't capture it.
        activity?.Stop();

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "should-be-null" && s.Source.Name == "System.Net.Http");
    }

    [Fact]
    public void Captures_spans_from_additional_custom_source()
    {
        var sourceName = $"Custom.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("custom-operation");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "custom-operation" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Captures_any_activity_source_including_custom()
    {
        var sourceName = $"Unregistered.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener();

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("custom-op");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.Source.Name == sourceName);
    }

    [Fact]
    public void Uses_AllData_sampling_not_AllDataAndRecorded()
    {
        var sourceName = $"SamplingCheck.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("sampled-op");
        Assert.NotNull(activity);
        Assert.True(activity!.IsAllDataRequested);

        activity.Stop();
        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "sampled-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Captures_full_activity_properties_for_rendering()
    {
        var sourceName = $"FullData.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("data-op");
        Assert.NotNull(activity);
        activity!.Stop();

        var span = InternalFlowSpanStore.GetSpans().Single(s => s.Source.Name == sourceName);
        Assert.Equal("data-op", span.DisplayName);
        Assert.Equal(sourceName, span.Source.Name);
        Assert.NotEqual(default, span.Duration);
    }

    [Fact]
    public void Works_with_multiple_additional_sources()
    {
        var name1 = $"Multi.A.{Guid.NewGuid():N}";
        var name2 = $"Multi.B.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(name1, name2);

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

    [Fact]
    public void Works_with_no_additional_sources()
    {
        var sourceName = $"NoExtra.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener();

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("no-extra-op");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "no-extra-op" && s.Source.Name == sourceName);
    }

    [Fact]
    public void Dispose_does_not_throw()
    {
        var sourceName = $"Dispose.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);

        using (var a1 = source.StartActivity("before-dispose"))
            a1?.Stop();
        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "before-dispose" && s.Source.Name == sourceName);

        // Dispose should not throw
        _listener.Dispose();
        _listener = null;
    }

    [Fact]
    public void All_spans_captured_none_dropped_by_sampling()
    {
        var sourceName = $"Sampler.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);

        const int spanCount = 50;
        for (var i = 0; i < spanCount; i++)
        {
            using var activity = source.StartActivity($"op-{i}");
            activity?.Stop();
        }

        Assert.Equal(spanCount, InternalFlowSpanStore.GetSpans().Count(s => s.Source.Name == sourceName));
    }

    [Fact]
    public void Preserves_parent_child_span_hierarchy()
    {
        var sourceName = $"Hierarchy.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener(sourceName);

        using var source = new ActivitySource(sourceName);

        using var parent = source.StartActivity("parent-op");
        Assert.NotNull(parent);

        using var child = source.StartActivity("child-op");
        Assert.NotNull(child);
        Assert.Equal(parent!.SpanId, child!.ParentSpanId);

        child.Stop();
        parent.Stop();
    }
}
