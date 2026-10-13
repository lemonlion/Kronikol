using System.Net;
using Kronikol.Extensions.MongoDB;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// MongoDB notes as the subscriber writes them, painted in a generated report (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md,
/// R1): a reply is JSON, so the YAML view is offered for it and draws it, and a null in a statement is drawn. Before,
/// an update's reply was <c>n=1, nModified=0</c>, which no view could read, and the drawn filter
/// <c>{ "deletedAt" : null }</c> lost its null. Read from the painted text, not the markup.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class MongoNoteTests : DiagramNotePlaywrightBase
{
    public MongoNoteTests(PlaywrightFixture fixture) : base(fixture) { }

    private static ConnectionId Connection() => new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    /// <summary>A report whose one diagram holds one MongoDB command, its notes written by the real subscriber.</summary>
    private string Report(string fileName, string commandName, BsonDocument command, BsonDocument reply, NotePayloadFormat format)
    {
        var testId = "mongo-" + Guid.NewGuid().ToString("N");
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "MongoDB",
            CallerName = "LoanService",
            AttributeByDocumentOwner = false,
            AutoCorrelateWrites = false,
            CurrentTestInfoFetcher = () => ("Mongo notes", testId),
        });
        subscriber.OnCommandStarted(new CommandStartedEvent(commandName, command, new DatabaseNamespace("app"), 1L, 1, Connection()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent(commandName, reply, new DatabaseNamespace("app"), 1L, 1, Connection(), TimeSpan.FromMilliseconds(2)));
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(2, logs.Length);

        var (features, _) = ReportTestHelper.CreateTestData();
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("t1", "", source)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { NotePayloadFormat = format });
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task Open(string url)
    {
        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private static readonly BsonDocument FindActive = new()
    {
        { "find", "loans" },
        { "filter", new BsonDocument { { "status", "Ready" }, { "deletedAt", BsonNull.Value } } },
        { "$db", "app" },
    };

    private static readonly BsonDocument FindReply = new()
    {
        { "cursor", new BsonDocument
            {
                { "firstBatch", new BsonArray { new BsonDocument { { "loanNumber", "L-1001" }, { "status", "Ready" }, { "principal", Decimal128.Parse("1000.50") } } } },
                { "id", 0L },
                { "ns", "app.loans" },
            }
        },
        { "ok", 1.0 },
    };

    private static readonly BsonDocument Update = new()
    {
        { "update", "loans" },
        { "updates", new BsonArray { new BsonDocument { { "q", new BsonDocument("loanNumber", "L-1001") }, { "u", new BsonDocument("$set", new BsonDocument("status", "Open")) } } } },
        { "$db", "app" },
    };

    private static readonly BsonDocument UpdateReply = new() { { "n", 1 }, { "nModified", 0 }, { "ok", 1.0 } };

    [Fact]
    public async Task A_find_reply_offers_the_YAML_view_and_draws_YAML()
    {
        await Open(Report("MongoNote_FindYaml.html", "find", FindActive, FindReply, NotePayloadFormat.Json));
        var before = await GetNormalizedSvgText();
        Assert.Contains("\"loanNumber\": \"L-1001\"", before);

        // The request note is the first note, the reply the second.
        await ClickNoteFormatButton(1);

        var text = await GetNormalizedSvgText();
        Assert.Contains("loanNumber: L-1001", text);
        Assert.Contains("$numberDecimal: \"1000.50\"", text);
        Assert.DoesNotContain("\"loanNumber\": \"L-1001\"", text);
    }

    [Fact]
    public async Task An_update_reply_offers_the_YAML_view_and_draws_its_counts_as_YAML()
    {
        await Open(Report("MongoNote_UpdateYaml.html", "update", Update, UpdateReply, NotePayloadFormat.Json));
        Assert.Contains("\"nModified\": 0", await GetNormalizedSvgText());

        await ClickNoteFormatButton(0);

        var text = await GetNormalizedSvgText();
        Assert.Contains("n: 1", text);
        Assert.Contains("nModified: 0", text);
        Assert.DoesNotContain("n=1", text);
    }

    [Fact]
    public async Task A_null_in_a_filter_is_drawn_in_JSON_and_in_YAML()
    {
        await Open(Report("MongoNote_NullJson.html", "find", FindActive, FindReply, NotePayloadFormat.Json));
        Assert.Contains("\"deletedAt\": null", await GetNormalizedSvgText());

        await Open(Report("MongoNote_NullYaml.html", "find", FindActive, FindReply, NotePayloadFormat.Yaml));
        Assert.Contains("deletedAt: null", await GetNormalizedSvgText());
    }
}
