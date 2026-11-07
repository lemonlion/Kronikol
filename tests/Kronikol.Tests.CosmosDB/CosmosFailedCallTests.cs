using System.Net;
using Kronikol.Extensions.CosmosDB;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.CosmosDB;

/// <summary>
/// A call through the Cosmos DB handler that throws (plans/FAILED_CALLS_PLAN.md F3, §5.7): a send that throws was answered since 3.18.0,
/// and a response whose body breaks off as it is read is now answered too, where it left the request alone; either way the
/// exception reaches the caller as it was thrown.
/// </summary>
public class CosmosFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private CosmosTrackingMessageHandlerOptions Options() => new() { ServiceName = "Cosmos DB", CallerName = "Caller", CurrentTestInfoFetcher = () => ("Orders are kept", _testId) };

    private static HttpRequestMessage Request(string operation) => operation switch
    {
        "Create" => new(HttpMethod.Post, "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = new StringContent("""{"id":"order-1","total":42.50}""")
        },
        "Read" => new(HttpMethod.Get, "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/order-1"),
        _ => new(HttpMethod.Delete, "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/order-1"),
    };

    public static TheoryData<string> Operations() => ["Create", "Read", "Delete"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_send_that_throws_is_answered_with_the_exceptions_type_and_left_as_it_was(string operation)
    {
        var thrown = FailedCallContract.Refused();
        using var invoker = new HttpMessageInvoker(new CosmosTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)));

        var caught = await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request(operation), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (request, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(request.Method.Value, answer.Method.Value);
        Assert.Equal(request.Uri, answer.Uri);
        Assert.Null(answer.Content);
        Assert.Empty(answer.Headers);
    }

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_response_whose_body_breaks_off_is_answered_with_the_exceptions_type(string operation)
    {
        var thrown = new InvalidOperationException("The response body broke off.");
        using var invoker = new HttpMessageInvoker(new CosmosTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(thrown))));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request(operation), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task History_reads_a_send_that_threw_as_each_rule_recorded_it()
    {
        using var invoker = new HttpMessageInvoker(new CosmosTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("Create"), TestContext.Current.CancellationToken));

        foreach (var rule in InteractionShape.KeptVersions)
            Assert.Equal(rule >= 6 ? "!HttpRequestException" : null, Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task History_rules_before_12_read_a_body_that_broke_off_as_no_answer_as_they_recorded_none()
    {
        using var invoker = new HttpMessageInvoker(new CosmosTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(new InvalidOperationException("The response body broke off.")))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request("Create"), TestContext.Current.CancellationToken));

        foreach (var rule in InteractionShape.KeptVersions)
            Assert.Equal(rule >= 6 ? "!InvalidOperationException" : null, Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_send_that_throws_is_answered_at_each_phases_verbosity_as_its_request_is()
    {
        // Under a phase verbosity the request carries a rendering for each phase, and its answer must too, or the diagram drops
        // the return at whichever phase the call is drawn in (FAILED_CALLS_PLAN.md F18).
        var options = Options();
        options.SetupVerbosity = CosmosTrackingVerbosity.Summarised;
        options.ActionVerbosity = CosmosTrackingVerbosity.Raw;
        var before = TestPhaseContext.Current;
        TestPhaseContext.Current = TestPhase.Unknown;
        try
        {
            using var invoker = new HttpMessageInvoker(new CosmosTrackingMessageHandler(options, new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

            await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("Create"), TestContext.Current.CancellationToken));
        }
        finally
        {
            TestPhaseContext.Current = before;
        }

        var (request, answer) = FailedCallContract.AssertAnswered(Logs(), FailedCallContract.Refused());
        Assert.NotNull(request.SetupVariant);
        Assert.NotNull(answer.SetupVariant);
        Assert.NotNull(answer.ActionVariant);
        Assert.Equal(request.SetupVariant!.Method.Value, answer.SetupVariant!.Method.Value);
        Assert.Equal(request.ActionVariant!.Uri, answer.ActionVariant!.Uri);
        Assert.Null(answer.ActionVariant.Content);
        Assert.Empty(answer.ActionVariant.Headers);
    }
}
