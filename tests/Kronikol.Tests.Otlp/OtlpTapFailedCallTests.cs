using System.Net;
using Kronikol.Extensions.Otlp;
using Kronikol.History;
using Kronikol.Tests.Tracking;

namespace Kronikol.Tests.Otlp;

/// <summary>
/// A span whose status is ERROR (plans/FAILED_CALLS_PLAN.md C2): its answer keeps the status it had and carries the span's message
/// as its reason, in <c>Error</c>, where the message was only its note.
/// </summary>
public class OtlpTapFailedCallTests
{
    private static OtlpTapOptions Options()
    {
        var options = new OtlpTapOptions { ListenPort = 1 };
        options.ServiceNameMap["bq-emulator"] = "bigquery";
        options.ServiceNameMap["localhost:27099"] = "mongo";
        return options;
    }

    private static OtlpSpan Failed() => Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.BigQueryFailed)));

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_failed_span_is_answered_as_failed_with_its_message_as_the_reason()
    {
        var (request, response) = SpanToInteractionMapper.Map(Failed(), Options())!.ToLogs();

        var (_, answer) = FailedCallContract.AssertFailed([request, response], HttpStatusCode.InternalServerError, "Deadline exceeded");
        Assert.Equal("Deadline exceeded", answer.Content);
    }

    [Fact]
    public void The_records_it_writes_for_the_ingest_carry_the_reason_too()
    {
        var (_, response) = SpanToInteractionMapper.Map(Failed(), Options())!.ToRecords();

        Assert.Equal("500", response.StatusCode);
        Assert.Equal("Deadline exceeded", response.Error);
        Assert.Equal("Deadline exceeded", response.ToLog().Error);
    }

    [Fact]
    public void A_failed_span_with_no_message_still_says_it_failed()
    {
        var (_, response) = SpanToInteractionMapper.Map(Failed() with { StatusMessage = null }, Options())!.ToLogs();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
        Assert.Equal("The span's status is ERROR, with no message.", response.Error);
        Assert.Null(response.Content);
    }

    [Fact]
    public void A_span_that_did_not_fail_has_no_reason()
    {
        var mapped = SpanToInteractionMapper.Map(
            Assert.Single(OtlpTraceReader.ReadJson(OtlpGoldens.Utf8(OtlpGoldens.MongoFindOldSemconv))), Options())!;

        Assert.Null(mapped.ToLogs().Response.Error);
        Assert.Null(mapped.ToRecords().Response.Error);
    }

    [Fact]
    public void Every_history_rule_reads_the_answer_as_it_did()
    {
        // The status stays, so no rule's call line moves: the reason is not part of one.
        var (request, response) = SpanToInteractionMapper.Map(Failed(), Options())!.ToLogs();

        foreach (var rule in InteractionShape.KeptVersions.Append(InteractionShape.Version))
            Assert.Equal("500", Assert.Single(InteractionShape.Calls([request, response], null, rule)).Status);
    }
}

/// <summary>
/// The OTLP export's span status (plans/FAILED_CALLS_PLAN.md F21, §5.4): a failure's message is its reason, in <c>Error</c>, where a
/// SQL failure was exported with its status word as the message; and a reason beside a status the word list does not name is a
/// failure too, as every other reader reads it.
/// </summary>
public class OtlpExportFailedCallTests
{
    private static Kronikol.Tracking.RequestResponseLog Answer(string status, string? error = null) =>
        new("t", "c", "SELECT", null, new Uri("sql:///orders"), [], "Orders", "Caller", Kronikol.Tracking.RequestResponseType.Response,
            Guid.NewGuid(), Guid.NewGuid(), false, status) { Error = error };

    [Fact]
    public void A_failure_is_exported_with_its_reason_as_the_message()
    {
        Assert.Equal((OtlpStatusCode.Error, (string?)null, "deadlock victim"), OtlpSpanMapper.StatusOf(Answer("Error", "deadlock victim")));
        Assert.Equal((OtlpStatusCode.Error, (string?)null, "Error"), OtlpSpanMapper.StatusOf(Answer("Error")));
    }

    [Fact]
    public void A_reason_beside_a_status_word_the_list_does_not_name_is_a_failure()
    {
        Assert.Equal((OtlpStatusCode.Error, (string?)null, "The broker refused it."), OtlpSpanMapper.StatusOf(Answer("Sent", "The broker refused it.")));
        Assert.Equal((OtlpStatusCode.Unset, (string?)null, (string?)null), OtlpSpanMapper.StatusOf(Answer("Sent")));
    }
}
