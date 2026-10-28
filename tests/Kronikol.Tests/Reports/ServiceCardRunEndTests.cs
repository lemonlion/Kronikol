using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.4: the run end's <c>service.json</c>, on by default (Q2). It is written after the output
/// list, from the data file the list has just written, and before <c>Run.json</c>, which lists it and carries its
/// <c>behaviourHash</c>. A run with no JSON data file writes none and says nothing about it.
/// </summary>
[Collection("DiagramsFetcher")]
public class ServiceCardRunEndTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-card-run").FullName;

    public ServiceCardRunEndTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.AfterOutputsForTests.Value = null;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private ReportConfigurationOptions Options() => new()
    {
        ReportsFolderPath = _dir,
        InternalFlowTracking = false,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false
    };

    private string Card => Path.Combine(_dir, "service.json");

    private string DataFile => ReportFile.Resolve(Path.Combine(_dir, "TestRunReport.json"));

    /// <summary>One run: the test orders pancakes from Breakfast Provider, which asks Cow Service for milk inside that request. Returns what it printed and recorded.</summary>
    private (string Console, IReadOnlyList<DiagnosticEntry> Diagnostics) Run(ReportConfigurationOptions options)
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var testId = "card-run-" + Guid.NewGuid().ToString("N");
        var (order, milk, trace) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Post, "{\"dish\":\"pancakes\"}", new Uri("http://localhost/orders/123"), [], "Breakfast Provider", "Caller",
            RequestResponseType.Request, trace, order, false) { AttributionSource = AttributionSource.TestContext, Phase = TestPhase.Action }, at);
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri("http://localhost:5001/milk"), [], "Cow Service", "Breakfast Provider",
            RequestResponseType.Request, trace, milk, false) { AttributionSource = AttributionSource.RequestHeader }, at.AddMilliseconds(1));
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri("http://localhost:5001/milk"), [], "Cow Service", "Breakfast Provider",
            RequestResponseType.Response, trace, milk, false, HttpStatusCode.OK) { AttributionSource = AttributionSource.RequestHeader }, at.AddMilliseconds(2));
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Post, null, new Uri("http://localhost/orders/123"), [], "Breakfast Provider", "Caller",
            RequestResponseType.Response, trace, order, false, HttpStatusCode.Created) { AttributionSource = AttributionSource.TestContext, Phase = TestPhase.Action }, at.AddMilliseconds(3));

        Feature[] features = [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = testId, DisplayName = "Order pancakes", Result = ExecutionResult.Passed }] }];
        var collector = new ReportDiagnosticsCollector();
        var original = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using (ReportDiagnosticsScope.Begin(collector))
                ReportGenerator.CreateStandardReportsWithDiagrams(features, Start, Start.AddSeconds(30), options);
        }
        finally
        {
            Console.SetOut(original);
        }

        return (captured.ToString(), collector.Entries);
    }

    private static void Log(RequestResponseLog log, DateTimeOffset at)
    {
        log.Timestamp = at;
        RequestResponseLogger.Log(log);
    }

    private RunManifest Manifest() => RunManifest.TryRead(_dir) ?? throw new InvalidOperationException("no readable Run.json");

    // 5.4.1
    [Fact]
    public void The_card_is_written_listed_in_Run_json_and_its_hash_carried_there()
    {
        Run(Options());

        var manifest = Manifest();
        using var card = JsonDocument.Parse(File.ReadAllText(Card));
        var run = card.RootElement.GetProperty("run");
        Assert.Contains("service.json", manifest.Files);
        Assert.Equal(new RunManifestServiceCard("service.json", run.GetProperty("behaviourHash").GetString()!), manifest.ServiceCard);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(DataFile))), run.GetProperty("report").GetProperty("sha256").GetString());
        Assert.Equal(Path.GetFileName(DataFile), run.GetProperty("report").GetProperty("file").GetString());
        Assert.Equal(manifest.Run, run.GetProperty("id").GetString());
        Assert.Equal(manifest.At.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), run.GetProperty("at").GetString());
        Assert.Equal(["Breakfast Provider", "Caller"], card.RootElement.GetProperty("behaviour").GetProperty("edges").EnumerateArray()
            .Select(e => e.GetProperty("from").GetString()).Order(StringComparer.Ordinal));
    }

    // 5.4.1: after the output list, which writes the data file in parallel with everything else, never inside it.
    [Fact]
    public void The_card_is_written_after_the_output_list_from_the_data_file_it_wrote()
    {
        (bool Card, bool DataFile)? atTheListsEnd = null;
        ReportGenerator.AfterOutputsForTests.Value = () => atTheListsEnd = (File.Exists(Card), File.Exists(DataFile));
        try
        {
            Run(Options());
        }
        finally
        {
            ReportGenerator.AfterOutputsForTests.Value = null;
        }

        Assert.Equal((false, true), atTheListsEnd);
        Assert.True(File.Exists(Card));
    }

    // 5.4.4: the run's card is the one `kronikol card` writes from the data file it wrote, byte for byte.
    [Fact]
    public void The_run_ends_card_is_the_one_kronikol_card_writes_from_its_data_file()
    {
        Run(Options());

        var output = new StringWriter();
        var error = new StringWriter();
        Assert.True(CardCommand.Run([DataFile], output, error) == 0, error.ToString());
        Assert.Equal(File.ReadAllText(Card), output.ToString());
    }

    [Fact]
    public void With_the_card_off_there_is_neither_the_file_nor_the_key()
    {
        var options = Options();
        options.GenerateServiceCard = false;

        Run(options);

        Assert.False(File.Exists(Card));
        Assert.DoesNotContain("service.json", Manifest().Files);
        Assert.Null(Manifest().ServiceCard);
    }

    // 5.4.2: a run with no JSON data file has nothing the card is a function of; it writes none and says nothing of it.
    [Theory]
    [InlineData("no data file")]
    [InlineData("an XML data file")]
    public void Without_a_JSON_data_file_there_is_no_card_and_nothing_said_of_it(string shape)
    {
        var options = Options();
        if (shape == "no data file")
            options.GenerateTestRunReportData = false;
        else
            options.TestRunReportDataFormat = DataFormat.Xml;

        var (console, diagnostics) = Run(options);

        Assert.False(File.Exists(Card));
        Assert.Null(Manifest().ServiceCard);
        Assert.DoesNotContain("service.json", console, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("service.json", StringComparison.Ordinal));
    }

    // 5.4.2: StaleOutputTests' rule. A card that could not be written is no card of this run's.
    [Fact]
    public void A_card_that_cannot_be_written_is_left_out_of_Run_json()
    {
        var options = Options();
        options.KeepRuns = 0;
        File.WriteAllText(Card, "an earlier run's card");

        string console;
        using (new FileStream(Card, FileMode.Open, FileAccess.Read, FileShare.None))
            (console, _) = Run(options);

        Assert.Contains("could not write service.json", console, StringComparison.Ordinal);
        Assert.DoesNotContain("service.json", Manifest().Files);
        Assert.Null(Manifest().ServiceCard);
        // Its bytes are salvaged as every output's are, and listed under the name that reached disk.
        Assert.Contains("service2.json", Manifest().Files);
        Assert.Equal("an earlier run's card", File.ReadAllText(Card));
    }

    // 5.4.3: an ingested run goes through the same run end, with the card on by default.
    [Fact]
    public void An_ingested_run_writes_its_card()
    {
        var capture = Path.Combine(AppContext.BaseDirectory, "TestData", "Cards", "card-capture.ndjson");
        using (RequestResponseLogger.IsolateForTests())
            Assert.Equal(0, IngestCommand.Run([capture, "-o", _dir], new StringWriter(), new StringWriter()));

        Assert.True(File.Exists(Card));
        Assert.Equal("service.json", Manifest().ServiceCard?.File);
    }

    /// <summary>Measured at 148 ms on 2026-10-28, beside a run end of 685 ms without the card.</summary>
    private const double CardBudgetMs = 1000;

    // 5.4.8: what the card adds to a large run's end, which writes its data file and little else here.
    [Fact]
    public void The_card_adds_to_a_large_runs_end_within_its_budget()
    {
        TimeSpan RunEnd(bool card)
        {
            var directory = Directory.CreateTempSubdirectory("kronikol-card-cost").FullName;
            try
            {
                using var isolation = RequestResponseLogger.IsolateForTests();
                var features = new[] { new Feature { DisplayName = "Orders", Scenarios = [.. Enumerable.Range(0, 2000).Select(i => new Scenario { Id = $"cost-{i}", DisplayName = $"Order {i}", Result = ExecutionResult.Passed })] } };
                foreach (var i in Enumerable.Range(0, 2000))
                    RequestResponseLogger.LogPair($"Order {i}", $"cost-{i}", HttpMethod.Post, new Uri($"http://localhost/orders/{i}"), "Breakfast Provider", "Caller");
                var options = new ReportConfigurationOptions
                {
                    ReportsFolderPath = directory, InternalFlowTracking = false, GenerateComponentDiagram = false, GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false, GenerateTestRunReport = false, GenerateFailuresDigest = false, WriteAgentInstructions = false,
                    WriteQueryScript = false, KeepRuns = 0, GenerateServiceCard = card
                };
                var original = Console.Out;
                Console.SetOut(TextWriter.Null);
                var watch = Stopwatch.StartNew();
                try
                {
                    ReportGenerator.CreateStandardReportsWithDiagrams(features, Start, Start.AddMinutes(5), options);
                }
                finally
                {
                    watch.Stop();
                    Console.SetOut(original);
                }
                Assert.Equal(card, File.Exists(Path.Combine(directory, "service.json")));
                return watch.Elapsed;
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        RunEnd(card: false);
        var scale = ContentionScale.Measure();
        var without = RunEnd(card: false);
        var with = RunEnd(card: true);

        var added = (with - without).TotalMilliseconds;
        Assert.True(added < CardBudgetMs * scale, $"the card added {added:F0} ms ({without.TotalMilliseconds:F0} ms without it) against {CardBudgetMs * scale:F0}");
    }
}
