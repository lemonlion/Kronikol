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
    public void Hears_System_Net_Http_only_under_a_parent()
    {
        // #138: HttpClient's spans keep their source and their kind. One with no parent is not sampled, so a call made
        // outside a trace makes no span and sends no traceparent, as when the source was left out.
        _listener = new InternalFlowActivityListener();
        using var parents = new ActivitySource($"Parent.{Guid.NewGuid():N}");
        using var http = new ActivitySource("System.Net.Http");
        var root = $"root-{Guid.NewGuid():N}";
        var child = $"child-{Guid.NewGuid():N}";
        Activity.Current = null;

        http.StartActivity(root, ActivityKind.Client)?.Stop();
        using (parents.StartActivity("parent"))
            http.StartActivity(child, ActivityKind.Client)?.Stop();

        var spans = InternalFlowSpanStore.GetSpans();
        Assert.DoesNotContain(spans, s => s.DisplayName == root);
        var captured = Assert.Single(spans, s => s.DisplayName == child);
        Assert.Equal("System.Net.Http", captured.Source.Name);
        Assert.Equal(ActivityKind.Client, captured.Kind);
    }

    [Fact]
    public void A_root_HttpClient_span_another_listener_samples_is_not_kept()
    {
        // A span another listener samples is reported to every listener of its source when it stops, whatever each one
        // sampled: an app's OpenTelemetry that samples every span makes HttpClient start one for a call outside a trace.
        // The listener keeps HttpClient's spans only under a parent, so it does not keep that one either.
        _listener = new InternalFlowActivityListener();
        using var http = new ActivitySource("System.Net.Http");
        using var everything = new ActivityListener
        {
            ShouldListenTo = s => ReferenceEquals(s, http),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(everything);
        var root = $"root-{Guid.NewGuid():N}";
        Activity.Current = null;

        using (var span = http.StartActivity(root, ActivityKind.Client))
            Assert.NotNull(span);

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(), s => s.DisplayName == root);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("StackExchange.Redis")]
    [InlineData("Azure.Cosmos")]
    [InlineData("Azure.Storage")]
    [InlineData("Microsoft.Azure.Cosmos")]
    public void Does_listen_to_well_known_sources(string sourceName)
    {
        _listener = new InternalFlowActivityListener();

        var opName = $"wk-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity(opName);
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == opName && s.Source.Name == sourceName);
    }

    [Theory]
    [InlineData("Experimental.System.Net.Http.Connections")]
    [InlineData("Experimental.System.Net.NameResolution")]
    [InlineData("Experimental.System.Net.Sockets")]
    [InlineData("Experimental.System.Net.Security")]
    public void Does_not_listen_to_the_runtimes_connection_sources(string sourceName)
    {
        // From .NET 9 the runtime builds these spans for every connection, and only because something listens: socket
        // connect, DNS lookup, TLS and connection setup, never drawn, and the wait for a connection, pool noise.
        _listener = new InternalFlowActivityListener();
        using var parents = new ActivitySource($"Parent.{Guid.NewGuid():N}");
        using var source = new ActivitySource(sourceName);
        var opName = $"connection-{Guid.NewGuid():N}";
        Activity.Current = null;

        using (parents.StartActivity("parent"))
            source.StartActivity(opName)?.Stop();

        Assert.DoesNotContain(InternalFlowSpanStore.GetSpans(), s => s.DisplayName == opName);
    }

    [Fact]
    public void Captures_a_custom_source_with_no_setup()
    {
        var sourceName = $"Custom.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener();

        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("custom-operation");
        activity?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(),
            s => s.DisplayName == "custom-operation" && s.Source.Name == sourceName);
    }

    [Fact]
    public void The_obsolete_constructor_still_hears_every_source_whatever_it_is_given()
    {
        // #138: the names were never read. A caller that still passes some is warned, and gets what it always got.
        var unnamed = $"Unnamed.{Guid.NewGuid():N}";
#pragma warning disable CS0618
        _listener = new InternalFlowActivityListener($"Named.{Guid.NewGuid():N}");
#pragma warning restore CS0618

        using var source = new ActivitySource(unnamed);
        source.StartActivity("unnamed-operation")?.Stop();

        Assert.Contains(InternalFlowSpanStore.GetSpans(), s => s.DisplayName == "unnamed-operation" && s.Source.Name == unnamed);
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
        _listener = new InternalFlowActivityListener();

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
        _listener = new InternalFlowActivityListener();

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
    public void Captures_several_custom_sources_with_no_setup()
    {
        var name1 = $"Multi.A.{Guid.NewGuid():N}";
        var name2 = $"Multi.B.{Guid.NewGuid():N}";
        _listener = new InternalFlowActivityListener();

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
        _listener = new InternalFlowActivityListener();

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
        _listener = new InternalFlowActivityListener();

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
        _listener = new InternalFlowActivityListener();

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
