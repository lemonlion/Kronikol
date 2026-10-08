using Google.Cloud.Spanner.Data;
using Kronikol.Extensions.Spanner;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

// Reads the process-wide TrackingComponentRegistry, which TrackingSpannerCommandFailureTests clears.
[Collection("TrackingComponentRegistry")]
public class SpannerConnectionStringBuilderExtensionTests
{
    private SpannerTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Spanner",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("Test", Guid.NewGuid().ToString()),
    };

    [Fact]
    public void WithTestTracking_Returns_same_builder_instance()
    {
        var builder = new SpannerConnectionStringBuilder
        {
            DataSource = "projects/p/instances/i/databases/d"
        };

        var result = builder.WithTestTracking(MakeOptions());

        Assert.Same(builder, result);
    }

    [Fact]
    public void WithTestTracking_Sets_SessionPoolManager()
    {
        var builder = new SpannerConnectionStringBuilder
        {
            DataSource = "projects/p/instances/i/databases/d"
        };
        var defaultManager = builder.SessionPoolManager;

        builder.WithTestTracking(MakeOptions());

        Assert.NotSame(defaultManager, builder.SessionPoolManager);
    }

    [Fact]
    public void WithTestTracking_Preserves_DataSource()
    {
        var builder = new SpannerConnectionStringBuilder
        {
            DataSource = "projects/p/instances/i/databases/d"
        };

        builder.WithTestTracking(MakeOptions());

        Assert.Equal("projects/p/instances/i/databases/d", builder.DataSource);
    }

    // The overload without an accessor builds its tracker from the options alone.
    [Fact]
    public void WithTestTracking_without_an_accessor_passes_the_options_accessor_to_its_tracker()
    {
        var serviceName = "Spanner " + Guid.NewGuid();
        var options = MakeOptions() with { ServiceName = serviceName, HttpContextAccessor = RequestHeaderAccessor.For("Header Test", "header-id") };
        var builder = new SpannerConnectionStringBuilder { DataSource = "projects/p/instances/i/databases/d" };

        builder.WithTestTracking(options);

        var tracker = TrackingComponentRegistry.GetRegisteredComponents().OfType<SpannerTracker>()
            .Single(t => t.ComponentName == $"SpannerTracker ({serviceName})");
        Assert.True(tracker.HasHttpContextAccessor);
    }
}
