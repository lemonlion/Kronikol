using System.Diagnostics;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #138: the name a span's lane, the granularity filters and the discovered sources read. An activity made with
/// <c>new Activity(...)</c> belongs to .NET's unnamed default source, and its lane was "Unknown"; it is named from its
/// operation now (<c>plans/INTERNAL_FLOW_SOURCES_PLAN.md</c> R2).
/// </summary>
public class SpanSourceTests
{
    [Theory]
    [InlineData("System.Net.Http.HttpRequestOut", "System.Net.Http")]
    [InlineData("Microsoft.AspNetCore.Hosting.HttpRequestIn", "Microsoft.AspNetCore")]
    [InlineData("Grpc.Net.Client.GrpcOut", "Grpc.Net.Client")]
    public void An_activity_whose_source_is_known_takes_that_source_whichever_way_it_was_made(string operation, string source) =>
        Assert.Equal(source, SpanSource.Of("", operation));

    [Theory]
    [InlineData("Microsoft.ApplicationInsights.Init", "Microsoft.ApplicationInsights")]
    [InlineData("BlobClient.Upload", "BlobClient")]
    [InlineData("MyApp.Orders.Checkout", "MyApp.Orders")]
    public void An_activity_with_no_source_takes_its_operations_name_up_to_the_last_dot(string operation, string source) =>
        Assert.Equal(source, SpanSource.Of("", operation));

    [Fact]
    public void An_operation_name_with_no_dot_names_the_lane_itself() =>
        Assert.Equal("Checkout", SpanSource.Of("", "Checkout"));

    [Fact]
    public void A_source_name_wins_over_the_operation() =>
        Assert.Equal("MyApp", SpanSource.Of("MyApp", "System.Net.Http.HttpRequestOut"));

    [Fact]
    public void An_activity_with_neither_has_no_name() =>
        Assert.Equal("", SpanSource.Of("", ""));

    [Fact]
    public void An_activity_made_without_a_source_is_named_from_its_operation()
    {
        using var activity = new Activity("System.Net.Http.HttpRequestOut");

        Assert.Equal("", activity.Source.Name);
        Assert.Equal("System.Net.Http", SpanSource.Of(activity));
        Assert.Equal("System.Net.Http", FlowSpan.From(activity).Source);
    }
}
