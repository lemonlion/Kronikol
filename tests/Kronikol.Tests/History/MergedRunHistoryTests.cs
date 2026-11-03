using System.Net;
using System.Text.Json;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// A merge reads a method as the run wrote it, so its history agrees with what the run recorded
/// (plans/CALLS_AND_EVENTS_PLAN.md F1, F2). A merge used to read any casing of the nine verbs as a verb at
/// <c>Default</c>, and the writer put a verb in capitals, so a Cosmos DB <c>Delete</c> left <c>kronikol merge</c> as
/// <c>DELETE</c> while the ledger held <c>Delete</c>, and identical runs read as changed behaviour.
///
/// <para>Each fact runs in CI's order: the shard is written with no ledger in sight, and is read against the ledger only
/// by the merge, with the run not yet recorded, so a shard that calls itself partial (F20) or a recorded run counted as
/// an earlier one (F18) can neither hide the defect nor stand in for it. These facts hold the method. They do not claim
/// a merge's other differences from the run's own reading (F20, F21).</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class MergedRunHistoryTests : IDisposable
{
    private const string ScenarioId = "merged-run-scenario";
    private const string ScenarioName = "Removing an order";

    private static readonly DateTime Start = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-merged-run").FullName;

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");
    private string Shards => Path.Combine(_dir, "shards");
    private string Combined => Path.Combine(_dir, "Combined.html");
    private string LabsPage => Path.Combine(_dir, "Combined.labs.html");

    /// <summary>The pill itself: the page's own stylesheet names the class whatever the verdict.</summary>
    private const string BehaviourChangedPill = "<span class=\"history-verdict history-verdict-behaviour-changed\"";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Features(bool failed = false) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = ScenarioId, DisplayName = ScenarioName, Duration = TimeSpan.FromSeconds(1),
                    Result = failed ? ExecutionResult.Failed : ExecutionResult.Passed,
                    ErrorMessage = failed ? "Expected the order to be gone" : null
                }
            ]
        }
    ];

    private static RequestResponseLog[] Call(OneOf<HttpMethod, string> method, string? category, DateTimeOffset at, string address = "probe:///orders")
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri(address);
        return
        [
            new RequestResponseLog(ScenarioName, ScenarioId, method, null, uri, [], "Dependency", "Orders Api",
                RequestResponseType.Request, trace, pair, false, DependencyCategory: category) { Timestamp = at },
            new RequestResponseLog(ScenarioName, ScenarioId, method, null, uri, [], "Dependency", "Orders Api",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK, DependencyCategory: category) { Timestamp = at.AddMilliseconds(5) }
        ];
    }

    /// <summary>The runs before this one, each recorded by the run itself, from the calls it made.</summary>
    private void RecordEarlierRuns(OneOf<HttpMethod, string> method, string? category, int runs = 6, string address = "probe:///orders")
    {
        for (var n = 1; n <= runs; n++)
            Record(method, category, new DateTimeOffset(Start).AddHours(n - runs - 1), failed: false, address);
    }

    private void Record(OneOf<HttpMethod, string> method, string? category, DateTimeOffset at, bool failed, string address = "probe:///orders")
    {
        var options = new ReportConfigurationOptions { HistoryFilePath = Ledger, HistoryBranch = "" };
        var context = HistoryRunContext.Create(Features(failed), Call(method, category, at, address), null, null, at, options, _dir, "5.27.13", _ => null, _dir);
        Assert.NotNull(context);
        Assert.Equal(HistoryAppendOutcome.Appended, context.Append()?.Outcome);
    }

    /// <summary>This run's shard, as a run writes it: the label as the tracker wrote it, in the file a merge reads.</summary>
    private void WriteShard(OneOf<HttpMethod, string> method, string? category, bool failed = false, string address = "probe:///orders")
    {
        Directory.CreateDirectory(Shards);
        File.WriteAllText(Path.Combine(Shards, "runner1.json"), ReportGenerator.GenerateMergeableReportJson(
            Features(failed), Start, Start.AddMinutes(1), null, null, null, null, WholeTestFlowVisualization.None, null,
            trackedLogs: Call(method, category, new DateTimeOffset(Start).AddSeconds(1), address)));
    }

    private string Merge(Func<string, string?>? environment = null)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = MergeCommand.Run([Shards, "-o", Combined, "--history", Ledger], output, error, environment ?? (_ => null));
        Assert.True(exit == 0, error.ToString());
        return output.ToString();
    }

    /// <summary>A run with nothing to say prints no history line, so what it read is on the page beside it.</summary>
    private void AssertReadsStable(string output)
    {
        Assert.True(!output.Contains("history:", StringComparison.Ordinal), output);
        var page = ReportHtml.Read(LabsPage);
        Assert.Contains("<details id=\"history-section\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain(BehaviourChangedPill, page, StringComparison.Ordinal);
    }

    private static OneOf<HttpMethod, string> MethodOf(string written, bool builtByHand) =>
        builtByHand ? new HttpMethod(written) : written;

    /// <summary>
    /// The labels F2 lists at default verbosity, which <c>verb-labels</c> and <c>verb-routes</c> in the plan's harness read
    /// from each package's own classifier, and a verb built by hand, which the writer upper-cased, so a change to the reader
    /// alone leaves it red.
    /// </summary>
    public static TheoryData<string, string?, bool> VerbCased => new()
    {
        { "Delete", DependencyCategories.CosmosDB, false },
        { "Patch", DependencyCategories.CosmosDB, false },
        { "Delete", DependencyCategories.SQL, false },
        { "Delete", DependencyCategories.BigQuery, false },
        { "Delete", DependencyCategories.BlobStorage, false },
        { "Delete", DependencyCategories.MessageQueue, false },
        { "Get", DependencyCategories.Redis, false },
        { "Delete", DependencyCategories.Redis, false },
        { "Get", DependencyCategories.Grpc, false },
        { "Delete", null, false },
        { "Patch", null, true },
        { "patch", null, true },
    };

    [Theory]
    [MemberData(nameof(VerbCased))]
    public void A_merged_run_reads_as_nothing_changed_against_its_own_ledger(string written, string? category, bool builtByHand)
    {
        RecordEarlierRuns(MethodOf(written, builtByHand), category);
        WriteShard(MethodOf(written, builtByHand), category);

        AssertReadsStable(Merge());
    }

    /// <summary>A method in capitals reads as itself, as it always did: what the facts above must not have traded away.</summary>
    [Fact]
    public void A_verb_in_capitals_still_reads_as_nothing_changed()
    {
        RecordEarlierRuns(HttpMethod.Delete, DependencyCategories.SQL);
        WriteShard(HttpMethod.Delete, DependencyCategories.SQL);

        AssertReadsStable(Merge());
    }

    /// <summary>
    /// A path ending in an escaped space is written with the space itself, and parsing that text trims it, so a merge read another
    /// path than the run recorded (plans/CALLS_AND_EVENTS_PLAN.md F21).
    /// </summary>
    [Theory]
    [InlineData("http://svc/search/foo%20")]
    [InlineData("http://svc/search/foo%20%20")]
    [InlineData("probe:///foo%20")]
    public void A_path_ending_in_an_escaped_space_reads_as_nothing_changed(string address)
    {
        RecordEarlierRuns(HttpMethod.Get, null, address: address);
        WriteShard(HttpMethod.Get, null, address: address);

        AssertReadsStable(Merge());
    }

    /// <summary>What keeps the facts above from passing because a merge had stopped reading calls at all.</summary>
    [Theory]
    [InlineData("Delete", "Patch")]
    [InlineData("Delete", "DELETE")]
    public void A_real_change_of_call_is_still_named(string recorded, string now)
    {
        RecordEarlierRuns(recorded, DependencyCategories.CosmosDB);
        WriteShard(now, DependencyCategories.CosmosDB);

        var output = Merge();

        Assert.True(output.Contains("history: 1 behaviour changed (against 6 earlier runs", StringComparison.Ordinal), output);
        Assert.Contains(BehaviourChangedPill, ReportHtml.Read(LabsPage), StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_that_keeps_failing_is_given_no_false_lead()
    {
        RecordEarlierRuns("Delete", DependencyCategories.CosmosDB, runs: 5);
        Record("Delete", DependencyCategories.CosmosDB, new DateTimeOffset(Start).AddMinutes(-30), failed: true);
        WriteShard("Delete", DependencyCategories.CosmosDB, failed: true);
        var summary = Path.Combine(_dir, "summary.md");

        var output = Merge(name => name switch
        {
            "GITHUB_ACTIONS" => "true",
            "GITHUB_STEP_SUMMARY" => summary,
            _ => null
        });

        var digest = File.ReadAllText(Path.Combine(_dir, "Failures.md"));
        Assert.Contains("**failing**", digest, StringComparison.Ordinal);
        Assert.True(output.Contains("history: 1 still failing (against", StringComparison.Ordinal), output);
        Assert.DoesNotContain("different calls", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("behaviour changed", digest, StringComparison.Ordinal);
        var ciSummary = File.ReadAllText(summary);
        Assert.True(ciSummary.Contains("history: 1 still failing (against", StringComparison.Ordinal), ciSummary);
        Assert.DoesNotContain("behaviour changed", ciSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_report_History_tab_says_nothing_changed()
    {
        RecordEarlierRuns("Delete", DependencyCategories.CosmosDB);
        WriteShard("Delete", DependencyCategories.CosmosDB);

        Merge();

        var report = ReportHtml.Read(Combined);
        Assert.Contains("<details id=\"history-section\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain(BehaviourChangedPill, report, StringComparison.Ordinal);
        Assert.DoesNotContain(BehaviourChangedPill, ReportHtml.Read(LabsPage), StringComparison.Ordinal);
    }

    /// <summary>The same page on a real change, which keeps the absence above from meaning the page draws no verdict at all.</summary>
    [Fact]
    public void A_merged_report_History_tab_names_a_real_change()
    {
        RecordEarlierRuns("Delete", DependencyCategories.CosmosDB);
        WriteShard("Patch", DependencyCategories.CosmosDB);

        Merge();

        Assert.Contains(BehaviourChangedPill, ReportHtml.Read(Combined), StringComparison.Ordinal);
        Assert.Contains(BehaviourChangedPill, ReportHtml.Read(LabsPage), StringComparison.Ordinal);
    }

    /// <summary>No history rule moves, because a ledger's lines come from the run's own record and never from a merge.</summary>
    [Fact]
    public void A_merge_writes_nothing_to_the_ledger()
    {
        RecordEarlierRuns("Delete", DependencyCategories.CosmosDB);
        WriteShard("Delete", DependencyCategories.CosmosDB);
        var before = File.ReadAllBytes(Ledger);

        Merge();

        Assert.Equal(before, File.ReadAllBytes(Ledger));
    }

    /// <summary>
    /// A merged <c>service.json</c> names each call as the run's did. The label moved a call's operation id, and read as a
    /// verb a Storage Queues <c>Delete</c> left its row of <see cref="Kronikol.CallGraph.MessageTable"/>, which is ordinal,
    /// so a delete drew a message of no direction.
    /// </summary>
    [Fact]
    public void A_merge_keeps_a_calls_operation_id()
    {
        Directory.CreateDirectory(Shards);
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        var at = new DateTimeOffset(Start).AddSeconds(1);
        var uri = new Uri("storagequeue:///orders");
        RequestResponseLog[] logs =
        [
            new(ScenarioName, ScenarioId, "Delete", null, uri, [], "Queue", "Orders Api", RequestResponseType.Request, trace, pair, false,
                DependencyCategory: DependencyCategories.MessageQueue) { Timestamp = at },
            new(ScenarioName, ScenarioId, "Delete", null, uri, [], "Queue", "Orders Api", RequestResponseType.Response, trace, pair, false, HttpStatusCode.NoContent,
                DependencyCategory: DependencyCategories.MessageQueue) { Timestamp = at.AddMilliseconds(5) }
        ];
        // A card lists message rows for the service it is about, so the run says which that is.
        var declared = new ReportConfigurationOptions { ServiceId = "orders-api", Parties = [new("Orders Api") { Id = "orders-api", Kind = PartyKind.UnderTest }] };
        var shard = Path.Combine(Shards, "runner1.json");
        File.WriteAllText(shard, ReportGenerator.GenerateMergeableReportJson(Features(), Start, Start.AddMinutes(1), null, null, null, null,
            WholeTestFlowVisualization.None, null, trackedLogs: logs, parties: PartiesReport.Resolve(declared, Features(), logs, _dir, _ => null, _dir)));

        var error = new StringWriter();
        Assert.True(MergeCommand.Run([Shards, "-o", Combined], new StringWriter(), error, _ => null) == 0, error.ToString());

        var run = new StringWriter();
        Assert.True(CardCommand.Run([shard], run, error) == 0, error.ToString());
        var (runOps, runMessages) = OperationsOf(run.ToString());
        var (mergedOps, mergedMessages) = OperationsOf(File.ReadAllText(Path.Combine(_dir, "service.json")));

        Assert.NotEmpty(runOps);
        Assert.Empty(runMessages);
        Assert.Empty(mergedMessages);
        Assert.Equal(runOps, mergedOps);
    }

    /// <summary>Every operation id and every message row anywhere in a card, as the plan's <c>card-queue-read.py</c> reads them.</summary>
    private static (List<string> Ops, List<string> Messages) OperationsOf(string card)
    {
        using var document = JsonDocument.Parse(card);
        var (ops, messages) = (new List<string>(), new List<string>());
        Walk(document.RootElement);
        return (ops.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), messages);

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Walk(item);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    if (property.NameEquals("op") && property.Value.ValueKind == JsonValueKind.String)
                        ops.Add(property.Value.GetString()!);
                    if (property.NameEquals("messages") && property.Value.ValueKind == JsonValueKind.Array)
                        messages.AddRange(property.Value.EnumerateArray().Select(m => m.GetRawText()));
                    else
                        Walk(property.Value);
                }
            }
        }
    }
}
