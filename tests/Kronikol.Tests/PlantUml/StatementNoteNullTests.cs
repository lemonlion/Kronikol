using System.Net;
using Kronikol.Constants;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// A drawn JSON note drops a null property, as noise, except in a statement's note, where the content is the call
/// itself and a null is part of what it means (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F22): <c>{ "deletedAt" : null }</c>
/// matches the documents with no <c>deletedAt</c>; drawn as <c>{}</c> it read as a filter matching every document.
/// </summary>
public class StatementNoteNullTests
{
    private static RequestResponseLog Request(string? category, string content, string method = "Find ← loans") =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: method,
            Content: content,
            Uri: new Uri("mongodb:///app/loans"),
            Headers: [],
            ServiceName: "MongoDB",
            CallerName: "LoanService",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            DependencyCategory: category);

    private static RequestResponseLog Response(RequestResponseLog request, string? content) =>
        request with
        {
            Type = RequestResponseType.Response,
            Content = content,
            StatusCode = HttpStatusCode.OK,
        };

    private static string Drawn(params RequestResponseLog[] logs) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;

    [Fact]
    public void A_null_in_a_statement_note_is_drawn()
    {
        var request = Request(DependencyCategories.MongoDB, "{ \"filter\" : { \"deletedAt\" : null } }");

        var plantUml = Drawn(request, Response(request, null));

        Assert.Contains("\"deletedAt\": null", plantUml);
    }

    [Fact]
    public void A_null_in_a_statement_reply_is_drawn()
    {
        // A findAndModify that matched nothing answers value: null.
        var request = Request(DependencyCategories.MongoDB, "{ \"query\" : { \"status\" : \"Ready\" } }", "FindAndModify ↔ loans");

        var plantUml = Drawn(request, Response(request, "{ \"lastErrorObject\" : { \"n\" : 0, \"updatedExisting\" : false }, \"value\" : null }"));

        Assert.Contains("\"value\": null", plantUml);
    }

    [Fact]
    public void A_capped_statement_reply_keeps_its_nulls()
    {
        var request = Request(DependencyCategories.MongoDB, "{ \"filter\" : { } }");

        var plantUml = Drawn(request, Response(request, "[{ \"_id\" : 1, \"closedAt\" : null }]\n... (2 more documents not shown)"));

        Assert.Contains("\"closedAt\": null", plantUml);
        Assert.Contains("... (2 more documents not shown)", plantUml);
    }

    [Fact]
    public void An_atlas_data_api_body_keeps_its_nulls()
    {
        var request = Request(DependencyCategories.AtlasDataApi, "{\"collection\":\"loans\",\"filter\":{\"closedAt\":null}}", "POST");

        Assert.Contains("\"closedAt\": null", Drawn(request, Response(request, null)));
    }

    [Fact]
    public void An_http_body_still_drops_its_nulls()
    {
        var request = Request(null, "{\"name\":\"Ada\",\"middleName\":null}", "POST") with { Uri = new Uri("http://example.com/api/people") };

        var plantUml = Drawn(request, Response(request, null));

        Assert.Contains("\"name\": \"Ada\"", plantUml);
        Assert.DoesNotContain("middleName", plantUml);
    }
}
