using System.Net.Sockets;
using Kronikol.Extensions.Elasticsearch;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Elasticsearch;

/// <summary>
/// An Elasticsearch call that never reached the server (plans/FAILED_CALLS_PLAN.md F22, §5.7): the client throws nothing and hands
/// back a response with no status and the failure in <c>OriginalException</c>, and the call is answered with that failure's type
/// and why, where it was answered with nothing at all.
/// </summary>
public class ElasticsearchFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private ElasticsearchTrackingCallbackHandler Handler() => new(new ElasticsearchTrackingOptions
    {
        ServiceName = "Elasticsearch",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Orders are searched", _testId),
    });

    private static HttpRequestException Refused() =>
        new("Connection refused (127.0.0.1:1)", new SocketException((int)SocketError.ConnectionRefused));

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_call_that_never_reached_the_server_is_answered_with_the_failure_the_client_handed_back()
    {
        var failure = Refused();

        Handler().LogOperation("POST", new Uri("http://127.0.0.1:1/orders/_search"), statusCode: null,
            "{\"query\":{\"match_all\":{}}}"u8.ToArray(), responseBodyBytes: null, failure);

        FailedCallContract.AssertAnswered(Logs(), failure);
    }

    [Fact]
    public void History_rules_before_12_read_it_as_the_answer_with_no_status_it_was()
    {
        Handler().LogOperation("POST", new Uri("http://127.0.0.1:1/orders/_search"), statusCode: null, null, null, Refused());

        Assert.Equal("!HttpRequestException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public void A_call_the_server_answered_keeps_the_servers_status()
    {
        Handler().LogOperation("POST", new Uri("http://localhost:9200/orders/_search"), statusCode: 503, null, null, failure: null);

        var answer = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, answer.StatusCode?.Value);
        Assert.Null(answer.Error);
    }
}
