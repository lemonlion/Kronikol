using System.Net;
using System.Text.Json;
using AngleSharp;
using Kronikol.InternalFlow;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// A party the run declares a test double is drawn by its name alone (5.16.2, reversing #190 R1's Q11): no diagram the
/// run draws, sequence or component, says what a party is, and each draws its participants as a run that declares
/// nothing does. What the party is stays in the data file's <c>parties</c> block and in <c>kronikol query parties</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class DeclaredDoubleDiagramTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-declared-double").FullName;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    // One word each, so a name is drawn on one line; tagged, since the process's call log holds every test's calls.
    private string Shop => "Shop" + _tag;
    private string Orders => "Orders" + _tag;
    private string Stock => "Stock" + _tag;
    private string TestId => "declared-double-" + _tag;

    /// <summary>
    /// Shop, drawn as the test's actor, calls Orders, which calls Stock, a database: each is declared a double, so every
    /// way a participant is declared in either diagram meets a declared name.
    /// </summary>
    private void LogCalls()
    {
        RequestResponseLogger.LogPair("Orders", TestId, HttpMethod.Post, new Uri("http://orders/orders"), Orders, Shop, statusCode: HttpStatusCode.Created);
        RequestResponseLogger.LogPair("Orders", TestId, "SELECT", new Uri("http://stock/items"), Stock, Orders, dependencyCategory: "SqlServer");
    }

    private string Run(string folder, bool declared)
    {
        var reports = Path.Combine(_dir, folder);
        var names = new HashSet<string> { Shop, Orders, Stock };
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [new Feature { DisplayName = "Shop " + _tag, Scenarios = [new Scenario { Id = TestId, DisplayName = "Orders", Result = ExecutionResult.Passed }] }],
            Start, Start.AddMinutes(1), new ReportConfigurationOptions
            {
                ReportsFolderPath = reports,
                ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = names.Contains },
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
                CompressTestRunReport = false,
                Parties = declared
                    ? [.. names.Order(StringComparer.Ordinal).Select(n => new PartyDescriptor(n) { Kind = PartyKind.Double, Note = "WireMock stub" })]
                    : []
            });
        return reports;
    }

    /// <summary>Every diagram source the run report carries, by its element's id: each scenario's and the Components tab's.</summary>
    private static Dictionary<string, string> Sources(string reports)
    {
        var page = BrowsingContext.New(Configuration.Default)
            .OpenAsync(req => req.Content(ReportHtml.Read(Path.Combine(reports, "TestRunReport.html")))).GetAwaiter().GetResult();
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(page.GetElementById("puml-data")!.TextContent)!;
        return map.ToDictionary(e => e.Key, e => InternalFlowHtmlGenerator.DecompressFromBase64(e.Value));
    }

    /// <summary>The lines of <paramref name="source"/> that declare one of the run's three parties, which name it in quotes.</summary>
    private string[] Declarations(string source) =>
        source.Replace("\r", "").Split('\n')
            .Where(l => new[] { Shop, Orders, Stock }.Any(n => l.Contains("\"" + n, StringComparison.Ordinal) || l.Contains("**" + n, StringComparison.Ordinal)))
            .ToArray();

    private static string Query(params string[] args)
    {
        var output = new StringWriter();
        QueryCommand.Run(args, output, new StringWriter());
        return output.ToString();
    }

    [Fact]
    public void A_declared_double_is_drawn_by_its_name_alone_in_every_diagram_the_run_draws()
    {
        LogCalls();
        var declared = Sources(Run("declared", declared: true));
        var plain = Sources(Run("plain", declared: false));

        // the scenario's sequence diagram, Shop its actor, and the Components tab's diagram, Shop its person, draw the three
        var drawing = declared.Where(e => Declarations(e.Value).Length == 3).ToList();
        Assert.Contains(drawing, e => e.Value.Contains($"actor \"{Shop}\"", StringComparison.Ordinal));
        Assert.Contains(drawing, e => e.Value.Contains("<<person>>", StringComparison.Ordinal));
        Assert.All(declared.Values, s => Assert.DoesNotContain("test double", s, StringComparison.OrdinalIgnoreCase));
        foreach (var (id, source) in drawing)
            Assert.Equal(Declarations(plain[id]), Declarations(source));
    }

    [Fact]
    public void The_data_file_and_query_parties_still_say_each_is_a_double()
    {
        LogCalls();
        var reports = Run("declared", declared: true);

        using var report = JsonDocument.Parse(ReportFile.Read(Path.Combine(reports, "TestRunReport.json")));
        var kinds = report.RootElement.GetProperty("parties").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => (p.GetProperty("kind").GetString(), p.GetProperty("kindSource").GetString()));
        foreach (var name in new[] { Shop, Orders, Stock })
            Assert.Equal(("double", "declared"), kinds[name]);

        var lines = Query("parties", reports).Replace("\r", "").Split('\n');
        foreach (var name in new[] { Shop, Orders, Stock })
            Assert.Contains(lines, l => l.StartsWith(name, StringComparison.Ordinal) && l.Contains(" double ") && l.Contains("declared") && l.Contains("WireMock stub"));
    }
}
