using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The one reading of how an answered call ended (plans/FAILED_CALLS_PLAN.md §5.4): it failed when its status says so, or when it
/// carries a reason in <c>Error</c>. The digest, the query verbs, the component diagram and the OTLP export all take it, where
/// each had a rule of its own.
/// </summary>
public class InteractionStatusFailedTests
{
    [Theory]
    [InlineData("500", "InternalServerError", null, true)]
    [InlineData(null, "!HttpRequestException", "Connection refused", true)]
    [InlineData(null, "Error", "deadlock", true)]
    [InlineData(null, "Error", null, true)]
    [InlineData(null, "Sent", "The broker refused the message.", true)]
    [InlineData(null, "Sent", null, false)]
    [InlineData("200", "OK", null, false)]
    [InlineData(null, null, "Connection refused", true)]
    [InlineData(null, null, " ", false)]
    [InlineData(null, null, null, false)]
    public void An_answer_failed_when_its_status_says_so_or_it_carries_a_reason(string? code, string? text, string? error, bool failed) =>
        Assert.Equal(failed, InteractionStatus.Failed(code, text, error));

    [Fact]
    public void A_log_is_read_the_same_way()
    {
        var answer = new RequestResponseLog("t", "c", HttpMethod.Get, null, new Uri("http://sut/orders"), [], "Orders", "Caller",
            RequestResponseType.Response, Guid.NewGuid(), Guid.NewGuid(), false, HttpStatusCode.OK);

        Assert.False(InteractionStatus.Failed(answer));
        Assert.True(InteractionStatus.Failed(answer with { Error = "The connection closed." }));
        Assert.True(InteractionStatus.Failed(answer with { StatusCode = "!IOException" }));
        Assert.True(InteractionStatus.Failed(answer with { StatusCode = HttpStatusCode.ServiceUnavailable }));
        Assert.False(InteractionStatus.Failed(answer with { StatusCode = null }));
    }
}
