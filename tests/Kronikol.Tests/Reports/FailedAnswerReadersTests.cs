using System.Net;
using Kronikol.ComponentDiagram;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every shape a failed answer takes after #232, and a call nobody answered, against every reader in core that says how a call
/// ended (plans/FAILED_CALLS_PLAN.md §6.5, §2.2, §2.3): the one reading, <c>Failures.md</c>'s status and reason, the component
/// diagram's error rate and history's call line. It pins what each reads, so a reader that drifts from the others fails here.
/// </summary>
public class FailedAnswerReadersTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string, string?, string> Shapes => new()
    {
        // shape, the status as written, Error, history's line end under rule 12
        { "a call that threw", "!HttpRequestException", "Connection refused (localhost:1)", "!HttpRequestException" },
        { "a broker's failure, its note kept", "!InvalidOperationException", "The queue is full.", "!InvalidOperationException" },
        { "a span whose status is ERROR", "500", "Deadline exceeded", "500" },
        { "a SQL failure", "Error", "Deadlock victim", "Error" },
        { "a MassTransit fault", "Fault", "The consumer threw.", "Fault" },
        { "an answer that did not fail", "200", null, "200" },
    };

    private static RequestResponseLog[] Call(string status, string? error)
    {
        var pair = Guid.NewGuid();
        var uri = new Uri("http://orders/place");
        OneOf<HttpStatusCode, string> code = int.TryParse(status, out var numeric) ? (HttpStatusCode)numeric : status;
        return
        [
            new RequestResponseLog("Pay", "t0", HttpMethod.Post, "{}", uri, [], "Orders", "Caller", RequestResponseType.Request, pair, pair, false)
                { Timestamp = T0 },
            new RequestResponseLog("Pay", "t0", HttpMethod.Post, "the note", uri, [], "Orders", "Caller", RequestResponseType.Response, pair, pair, false, code)
                { Timestamp = T0.AddMilliseconds(5), Error = error }
        ];
    }

    private static Feature[] Failing() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure",
                    Steps = [new ScenarioStep { Keyword = "When", Text = "paying", Status = ExecutionResult.Failed, FailureMessage = "declined" }]
                }
            ]
        }
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_reader_reads_a_shape_alike(string shape, string status, string? error, string lineEnd)
    {
        var logs = Call(status, error);
        var failed = error is not null;

        Assert.True(failed == InteractionStatus.Failed(logs[1]), shape);

        var digest = FailuresDigestGenerator.Generate(Failing(), logs, "TestRunReport", "5.34.1").Markdown;
        Assert.Contains($"| {(int.TryParse(status, out var n) ? ((HttpStatusCode)n).ToString() : status)} |", digest, StringComparison.Ordinal);
        // The reason line under the table, by the call's address, for a failed answer alone.
        Assert.Equal(failed, digest.Contains("\n- `s0/i0` ", StringComparison.Ordinal));
        if (failed)
            Assert.Contains($": `{error}`", digest, StringComparison.Ordinal);

        var stats = ComponentFlowSegmentBuilder.ComputeRelationshipStats(ComponentDiagramGenerator.ExtractRelationships(logs), logs).Values.Single();
        Assert.Equal(failed ? 1.0 : 0.0, stats.ErrorRate, 2);

        Assert.Equal(lineEnd, Assert.Single(InteractionShape.Calls(logs)).Status);
    }

    [Fact]
    public void Every_reader_reads_a_call_nobody_answered_alike()
    {
        var request = Call("200", null)[0];

        var digest = FailuresDigestGenerator.Generate(Failing(), [request], "TestRunReport", "5.34.1").Markdown;
        Assert.Contains("| no answer |", digest, StringComparison.Ordinal);

        Assert.Empty(ComponentFlowSegmentBuilder.ComputeRelationshipStats(ComponentDiagramGenerator.ExtractRelationships([request]), [request]));

        Assert.Equal("-", Assert.Single(InteractionShape.Calls([request])).ToString().Split(' ')[^1]);
    }
}
